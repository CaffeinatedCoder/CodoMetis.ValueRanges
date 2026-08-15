#!/usr/bin/env bash
#
# publish-nuget.sh — build, test, pack, and push all packable projects to NuGet.
#
# The version is read from MSBuild property evaluation (Directory.Build.props is the
# single source of truth), and the packages are discovered from the pack output —
# nothing is hardcoded, so future satellite packages are picked up automatically.
#
# Usage:
#   NUGET_API_KEY=<key> ./scripts/publish-nuget.sh            # full publish
#   ./scripts/publish-nuget.sh --dry-run                      # everything except push
#   NUGET_API_KEY=<key> ./scripts/publish-nuget.sh --skip-tests --allow-dirty
#
# Environment:
#   NUGET_API_KEY   API key for the target feed (required unless --dry-run)
#   NUGET_SOURCE    Feed URL (default: https://api.nuget.org/v3/index.json)
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
ARTIFACTS="$REPO_ROOT/dist"

DRY_RUN=false
SKIP_TESTS=false
ALLOW_DIRTY=false

for arg in "$@"; do
    case "$arg" in
        --dry-run)     DRY_RUN=true ;;
        --skip-tests)  SKIP_TESTS=true ;;
        --allow-dirty) ALLOW_DIRTY=true ;;
        *) echo "Unknown option: $arg" >&2; exit 2 ;;
    esac
done

cd "$REPO_ROOT"

# --- Preconditions -----------------------------------------------------------

if ! $DRY_RUN && [[ -z "${NUGET_API_KEY:-}" ]]; then
    echo "error: NUGET_API_KEY is not set (use --dry-run to test without pushing)" >&2
    exit 1
fi

if ! $ALLOW_DIRTY && [[ -n "$(git status --porcelain)" ]]; then
    echo "error: working tree is not clean — commit or stash first (or pass --allow-dirty)" >&2
    exit 1
fi

# --- Version (MSBuild evaluation, not XML regex) ------------------------------

VERSION="$(dotnet msbuild src/CodoMetis.ValueRanges/CodoMetis.ValueRanges.csproj -getProperty:Version)"
if [[ -z "$VERSION" ]]; then
    echo "error: could not evaluate the Version property" >&2
    exit 1
fi
echo "==> Publishing version $VERSION to $SOURCE"

# --- Build, test, pack ---------------------------------------------------------

echo "==> Building (Release)"
dotnet build -c Release --nologo

if $SKIP_TESTS; then
    echo "==> Skipping tests (--skip-tests)"
else
    # CI=true makes a missing live database FAIL the integration suite instead of
    # skipping it — never publish with the PostgreSQL parity layer silently skipped.
    echo "==> Running full test suite (live-PostgreSQL layer mandatory)"
    CI=true dotnet test -c Release --no-build --nologo
fi

echo "==> Packing into $ARTIFACTS"
rm -rf "$ARTIFACTS"
dotnet pack -c Release --no-build --nologo -o "$ARTIFACTS"

# --- Collect the packages for this version ------------------------------------

PACKAGES=("$ARTIFACTS"/*."$VERSION".nupkg)
if [[ ! -e "${PACKAGES[0]}" ]]; then
    echo "error: no *.$VERSION.nupkg found in $ARTIFACTS" >&2
    exit 1
fi

echo "==> Packages (${#PACKAGES[@]}):"
for package in "${PACKAGES[@]}"; do
    echo "      $(basename "$package")"
done

# --- Push ----------------------------------------------------------------------

if $DRY_RUN; then
    echo "==> Dry run — nothing pushed."
    exit 0
fi

for package in "${PACKAGES[@]}"; do
    echo "==> Pushing $(basename "$package")"
    dotnet nuget push "$package" \
        --api-key "$NUGET_API_KEY" \
        --source "$SOURCE" \
        --skip-duplicate
done

echo "==> Done. Consider tagging the release:"
echo "      git tag v$VERSION && git push origin v$VERSION"

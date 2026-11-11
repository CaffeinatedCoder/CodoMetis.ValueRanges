#!/usr/bin/env bash
# Builds the documentation site for one line (#417).
#
#   site/build.sh <checkout> <output> <line>
#
#   checkout  the repository checkout whose docs and packages to build: this one, or a worktree of
#             release/10.x
#   output    the folder the site is written to (replaced)
#   line      the label shown in the header, for example "10.x" or "11.x (preview)"
#
# The configuration is this script's own folder, whichever checkout is being built. Both lines have
# the same files under docs/ and src/*/README.md, so one config serves both, and release/10.x needs
# no copy of it (or of docs.yml) to be published. The pages are the Markdown files as they are:
# nothing is copied into them or rewritten, so test/oracles/claims.json keeps quoting what readers see.
#
# The exception is a page that is on main but not yet cherry-picked into release/10.x. Its menu entry
# would point at a file that line doesn't have, and fail its build (#420). So for another checkout,
# the menu leaves out the entries whose page is missing there, and says which. For this script's own
# checkout it is used as it is, and a missing page fails the build.
#
# The API reference is read from published assemblies, not from the projects. Publishing the
# EF Core NodaTime package yields all four packages, their XML docs and every dependency, which
# DocFX needs to resolve the signatures; pointing it at a csproj would make DocFX load the SDK
# through MSBuild itself, which a preview SDK does not reliably survive.
#
# Warnings are errors, so a broken link fails the build. DocFX runs from this checkout's tool
# manifest (.config/dotnet-tools.json), since release/10.x's does not pin it.
set -euo pipefail

if [ $# -ne 3 ]; then
  echo "usage: $0 <checkout> <output> <line>" >&2
  exit 2
fi

site=$(cd "$(dirname "$0")" && pwd)
checkout=$(cd "$1" && pwd)
mkdir -p "$2"
output=$(cd "$2" && pwd)
line=$3

target="$checkout/site"
if [ "$site" != "$target" ]; then
  rm -rf "$target"
  mkdir -p "$target"
  cp "$site/docfx.json" "$site/index.md" "$target/"
  # An entry is a `- name:` line and the `href:` line under it. Section entries (`items:`) and
  # absolute links are kept.
  awk -v checkout="$checkout" '
    /^[[:space:]]*- name:/ { if (held != "") print held; held = $0; next }
    /^[[:space:]]*href: \.\.\// && held != "" {
      page = $0
      sub(/^[[:space:]]*href: \.\.\//, "", page)
      if (system("test -f \"" checkout "/" page "\"") != 0) {
        print "site/build.sh: not on this line, left out of its menu: " page > "/dev/stderr"
        held = ""
        next
      }
    }
    { if (held != "") { print held; held = "" } print }
    END { if (held != "") print held }
  ' "$site/toc.yml" > "$target/toc.yml"
fi
rm -rf "$target/obj" "$output"

(cd "$checkout" && dotnet publish -c Release src/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime -o "$target/obj/pub" -v q)

cd "$site/.."
dotnet tool restore >/dev/null
dotnet docfx "$target/docfx.json" --warningsAsErrors -o "$output" \
  --metadata _appTitle="CodoMetis.ValueRanges $line" \
  --metadata _appName="CodoMetis.ValueRanges $line"

#!/usr/bin/env bash
#
# End-to-end check of the path a real consumer takes, which nothing else in the suite covers.
#
# Every other layer references the projects directly. A ProjectReference hides exactly the things
# that break for a consumer of the *package*: a satellite whose nuspec fails to declare the core
# package, a version range that lets NuGet resolve a different core than the one built here, a
# type that is public in the project and missing from the package, a plugin that registers through
# a project reference and not through the assembly a consumer restores. Each of those packs
# cleanly and restores cleanly, and the first person to notice is a consumer.
#
# So this packs the packages, restores them into throwaway projects created *outside* the
# repository — inside it, Directory.Build.props would apply and they would stop resembling anything
# a consumer builds — compiles real code against them, runs it, and asserts on what it prints and on
# the SQL the EF plugin translates. Never on the exit code alone.
#
# Two consumers: one installs only the core package, the most common shape; one installs the
# NodaTime EF satellite, which pulls in all four through the nuspec dependency chain — the chain is
# what that consumer is testing.
#
# Usage: consumer-smoke-test.sh [feed-directory]
#   With no argument the packages are packed fresh. Pass a directory of existing .nupkg files
#   (the release workflow passes its pack output) to test exactly the artifacts being shipped.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

feed="${1:-}"
failed=0

# A private package cache, and not an optimisation to skip.
#
# NuGet resolves id+version from the global packages folder before it ever looks at a source, so with
# the version number held at the current release a locally built package is shadowed by whatever
# that version was restored before — including the one published on nuget.org. The test then
# reports on a package that has nothing to do with the working tree. The sibling repository's smoke
# test passed a deliberately sabotaged build that way.
export NUGET_PACKAGES="$work/packages"

version="$(dotnet msbuild "$repo_root/src/CodoMetis.ValueRanges/CodoMetis.ValueRanges.csproj" -getProperty:Version)"
version="$(echo "$version" | tr -d '[:space:]')"

if [[ -z "$feed" ]]; then
    feed="$work/feed"
    echo "==> Packing $version"
    dotnet build "$repo_root/CodoMetis.ValueRanges.slnx" -c Release --verbosity quiet
    dotnet pack "$repo_root/CodoMetis.ValueRanges.slnx" -c Release --no-build -o "$feed" --verbosity quiet
else
    feed="$(cd "$feed" && pwd)"
    echo "==> Using existing feed: $feed"
fi

for id in CodoMetis.ValueRanges CodoMetis.ValueRanges.NodaTime CodoMetis.ValueRanges.EFCore.PostgreSQL CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime; do
    ls "$feed/$id.$version.nupkg" >/dev/null
done

# Quiet while it works, but never silent when it does not: swallowing this output once hid an
# NU1101 behind a bare "exit code 1" in CI.
run() {
    if ! output="$("$@" 2>&1)"; then
        echo "FAILED: $*"
        echo "$output" | sed 's/^/    /'
        exit 1
    fi
}

assert_contains() {
    if grep -qF -- "$1" "$2"; then
        echo "  ok: $3"
    else
        echo "  FAIL: $3 (expected '$1' in $(basename "$2"))"
        failed=1
    fi
}

# Creates a consumer project outside the repository and installs one package. Program.cs is written
# by the caller beforehand, at $work/<name>.cs. Restores, builds, runs, and leaves the program's
# output at $work/<name>.out and the resolved package graph at $work/<name>.packages.
#
# Source mapping, not just source ordering: this repository's own ids must come from the local feed
# and nowhere else, because these versions also exist on nuget.org and a published one satisfying
# the restore would mean testing something unrelated to the working tree. Everything else (EF Core,
# Npgsql, NodaTime) has to come from nuget.org, so restricting the whole restore to the local feed
# is not an option either — that fails with NU1101 on the transitive dependencies. <clear/> drops
# any machine-level sources.
scaffold_consumer() {
    local name="$1" package="$2" app="$work/$1"

    echo "==> $name: consumer project, $package"
    mkdir -p "$app"
    cd "$app"
    run dotnet new console --framework net10.0 --output .

    cat > nuget.config <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="CodoMetis.ValueRanges*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
XML

    cp "$work/$name.cs" Program.cs

    # No --source flag: it would override the mapping above and restrict the whole restore,
    # transitive dependencies included, to the local feed.
    run dotnet add package "$package" --version "$version"
    run dotnet build
    run dotnet list package --include-transitive
    echo "$output" > "$work/$name.packages"

    echo "==> $name: running the consumer"
    run dotnet run --no-build
    echo "$output" > "$work/$name.out"
    sed 's/^/      /' "$work/$name.out"
}

# ── Core only ─────────────────────────────────────────────────────────────────────────────────────

cat > "$work/core.cs" <<'CSHARP'
using System.Text.Json;
using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.Serialization;

// Literal round trip: parsed from PostgreSQL literal syntax, printed back in canonical form.
var parsed = Int32Range.Parse("[1,3]", null);
Console.WriteLine($"parsed={parsed}");

// The algebra, in memory.
var a = Int32Range.CreateFinite(1, 10);
var b = Int32Range.CreateFinite(5, 20);
Console.WriteLine($"intersect={a.Intersect(b)}");
Console.WriteLine($"overlaps={a.Overlaps(b)}");

// JSON through the shipped converters, both directions.
var json = new JsonSerializerOptions().AddRangeConverters();
var text = JsonSerializer.Serialize(a, json);
Console.WriteLine($"json={text}");
Console.WriteLine($"roundtrip={JsonSerializer.Deserialize<Int32Range>(text, json) == a}");

// A value set: canonical order on construction, membership.
StringSet tags = ["b", "a", "b"];
Console.WriteLine($"set={tags}");
Console.WriteLine($"contains={tags.Contains("a")}");
CSHARP

scaffold_consumer core CodoMetis.ValueRanges

echo "==> core: asserting"
assert_contains "> CodoMetis.ValueRanges " "$work/core.packages" "the core package resolved from the feed"
assert_contains "parsed=[1,3]"    "$work/core.out" "a literal parses and prints back canonically"
assert_contains "intersect=[5,10]" "$work/core.out" "intersection is computed and canonical"
assert_contains "overlaps=True"   "$work/core.out" "overlap predicate"
assert_contains 'json="[1,10]"'   "$work/core.out" "JSON converter writes the literal form"
assert_contains "roundtrip=True"  "$work/core.out" "JSON round trip is lossless"
assert_contains "set={a,b}"       "$work/core.out" "value set is canonical: sorted, de-duplicated"
assert_contains "contains=True"   "$work/core.out" "value set membership"

# ── EF Core + NodaTime satellite: the whole dependency chain ──────────────────────────────────────

cat > "$work/postgres.cs" <<'CSHARP'
using CodoMetis.ValueRanges;
using Microsoft.EntityFrameworkCore;
using NodaTime;

await using var db = new SmokeContext();

// Constants inline, so they render as SQL literals through the package's own literal generation
// rather than binding as parameters.
Console.WriteLine("bcl=" + db.Bookings.Where(x => x.Period.Contains(new DateOnly(2024, 6, 15))).ToQueryString());
Console.WriteLine("noda=" + db.Bookings.Where(x => x.Stay.Contains(new LocalDate(2024, 6, 15))).ToQueryString());
Console.WriteLine("set=" + db.Bookings.Where(x => x.Tags.Contains("x")).ToQueryString());

public sealed class Booking
{
    public int Id { get; set; }

    // Core package: BCL range -> daterange.
    public DateRange Period { get; set; } = DateRange.Empty;

    // NodaTime satellite: NodaTime range -> daterange, via the NodaTime EF satellite.
    public LocalDateRange Stay { get; set; } = LocalDateRange.Empty;

    // Value set -> text[].
    public StringSet Tags { get; set; } = StringSet.Empty;
}

public sealed class SmokeContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();

    // ToQueryString never opens the connection; the connection string only has to parse.
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseNpgsql("Host=localhost;Database=smoke", npgsql => npgsql.UseValueRangesNodaTime());
}
CSHARP

scaffold_consumer postgres CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime

echo "==> postgres: asserting"
# The nuspec dependency chain: installing the top satellite must bring every package at this
# version, from the local feed (source mapping guarantees the feed; this guarantees the chain).
for id in CodoMetis.ValueRanges CodoMetis.ValueRanges.NodaTime CodoMetis.ValueRanges.EFCore.PostgreSQL CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime; do
    assert_contains "> $id " "$work/postgres.packages" "$id resolved through the dependency chain"
    if grep -F "> $id " "$work/postgres.packages" | grep -qvF " $version"; then
        echo "  FAIL: $id resolved at a version other than $version"
        grep -F "> $id " "$work/postgres.packages" | sed 's/^/      /'
        failed=1
    fi
done
# The translations only exist if the plugins registered from the packaged assemblies; without them
# EF would refuse the query or evaluate it on the client, and neither prints these operators.
assert_contains '"Period" @> DATE '"'"'2024-06-15'"'"'' "$work/postgres.out" "BCL range predicate translates to @> (core plugin)"
assert_contains '"Stay" @> DATE '"'"'2024-06-15'"'"''   "$work/postgres.out" "NodaTime range predicate translates to @> (NodaTime satellite)"
assert_contains '"Tags" @> ARRAY['"'"'x'"'"']::text[]'  "$work/postgres.out" "value set membership translates to array containment"

[[ $failed -eq 0 ]] || { echo; echo "consumer smoke test FAILED"; exit 1; }
echo
echo "consumer smoke test passed ($version)"

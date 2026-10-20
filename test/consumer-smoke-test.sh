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
# Four consumers: one installs only the core package, the most common shape; one installs the
# NodaTime EF satellite, which pulls in all four through the nuspec dependency chain — the chain is
# what that consumer is testing; one publishes with Native AOT and runs the native binary,
# because whether a generic instantiation exists under AOT depends on the whole program, and a
# probe inside the repository can turn green by accident; and one installs the EF package next to
# EFCore.ComplexIndexes.PostgreSQL from nuget.org, the pairing docs/efcore.md recommends for
# exclusion and temporal constraints, which neither package references.
#
# Usage: consumer-smoke-test.sh [feed-directory]
#   With no argument the packages are packed fresh. Pass a directory of existing .nupkg files
#   (the release workflow passes its pack output) to test exactly the artifacts being shipped.
#
#   SMOKE_AOT_LINKER_ARGS — extra native linker arguments for the AOT consumer, space-separated,
#   for SDK builds whose link line needs library paths the toolchain does not find by itself
#   (Homebrew's dotnet on macOS: "-L/opt/homebrew/opt/openssl@3/lib -L/opt/homebrew/opt/brotli/lib").
#
#   SMOKE_AOT_SDK — the .NET SDK version to pin the AOT consumer to, as `dotnet --list-sdks` prints
#   it ("11.0.100-rc.2.26504.105"). The consumer gets a global.json naming it and targets that SDK's
#   framework (net11.0), so its publish runs that SDK's ILCompiler over the net10.0 packages — the
#   configuration in which the released 10.0.0 trapped (SIGTRAP on the first value set ToString,
#   #299, dotnet/runtime#136073), and the check to repeat by hand until the 10.x CI has a .NET 11
#   SDK leg (#297). The SDK has to sit in the dotnet root the `dotnet` on PATH resolves, beside the
#   .NET 10 SDK the repository's global.json needs. With neither installed, put both into one
#   folder and that folder first on PATH:
#     dotnet-install.sh --channel 11.0 --quality preview --install-dir ~/dotnet-smoke --no-path
#     dotnet-install.sh --channel 10.0 --install-dir ~/dotnet-smoke --no-path
#     PATH=~/dotnet-smoke:$PATH SMOKE_AOT_SDK=11.0.100-rc.2.26504.105 test/consumer-smoke-test.sh

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

assert_absent() {
    if grep -qF -- "$1" "$2"; then
        echo "  FAIL: $3 (found '$1' in $(basename "$2"))"
        failed=1
    else
        echo "  ok: $3"
    fi
}

# The release notes in each package are its version's section of its own CHANGELOG.md, in plain
# text, ending with a link to that changelog at the version's tag (#346). PackagingConventionTests
# compares a freshly generated nuspec with the section line for line; this reads the .nupkg itself,
# which in the release workflow's mode is the package about to be pushed. The first line of the
# section is checked in the plain form nuget.org shows: no heading marker, a link as `text (url)`,
# no backticks or bold, and XML-escaped as the nuspec holds it.
echo "==> Release notes in each package"
for id in CodoMetis.ValueRanges CodoMetis.ValueRanges.NodaTime CodoMetis.ValueRanges.EFCore.PostgreSQL CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime; do
    nuspec="$work/$id.nuspec"
    notes="$work/$id.notes"
    unzip -p "$feed/$id.$version.nupkg" "$id.nuspec" > "$nuspec"
    sed -n '/<releaseNotes>/,/<\/releaseNotes>/p' "$nuspec" > "$notes"
    first="$(awk -v heading="## [$version]" '
        index($0, heading) == 1 { inside = 1; next }
        inside && /^## / { exit }
        inside && NF { print; exit }' "$repo_root/src/$id/CHANGELOG.md" \
        | sed -E -e 's/^### //' -e 's/\[([^][]+)\]\((https?:\/\/[^()[:space:]]+)\)/\1 (\2)/g' -e 's/`//g' -e 's/\*\*//g' \
                 -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g')"
    if [[ -z "$first" ]]; then
        echo "  FAIL: $id: src/$id/CHANGELOG.md has no '## [$version]' section with a line in it"
        failed=1
    else
        assert_contains "<releaseNotes>$first" "$notes" "$id: release notes open with its '## [$version]' section"
    fi
    assert_contains "Full changelog: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/v$version/src/$id/CHANGELOG.md</releaseNotes>" "$notes" "$id: release notes end with its changelog at v$version"
    assert_absent '`' "$notes" "$id: release notes hold no Markdown backticks"
    assert_absent '](' "$notes" "$id: release notes hold no Markdown links"
done

# Creates a consumer project outside the repository and installs one package of this repository, plus
# any further packages from nuget.org given as id@version. Program.cs is written by the caller
# beforehand, at $work/<name>.cs. Restores, builds, runs, and leaves the program's
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
    shift 2

    echo "==> $name: consumer project, $package"
    mkdir -p "$app"
    cd "$app"
    # A pinned SDK (consumer_sdk, from SMOKE_AOT_SDK) goes in before `dotnet new`, so the template,
    # the restore and the publish all run on it, and the consumer targets that SDK's framework.
    local framework=net10.0
    if [[ -n "${consumer_sdk:-}" ]]; then
        printf '{ "sdk": { "version": "%s", "rollForward": "disable" } }\n' "$consumer_sdk" > global.json
        framework="net${consumer_sdk%%.*}.0"
    fi
    run dotnet new console --framework "$framework" --output .

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
    local extra
    for extra in "$@"; do
        run dotnet add package "${extra%@*}" --version "${extra#*@}"
    done
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

// JSON with nothing registered: the types carry their converters, so the literal is the default.
var bare = JsonSerializer.Serialize(new Dictionary<Int32Range, Int32Range> { [b] = a });
Console.WriteLine($"json-unregistered={bare}");
Console.WriteLine($"roundtrip-unregistered={JsonSerializer.Deserialize<Dictionary<Int32Range, Int32Range>>(bare)![b] == a}");

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
assert_contains 'json-unregistered={"[5,20]":"[1,10]"}' "$work/core.out" "JSON without registration writes the literal, as value and as dictionary key"
assert_contains "roundtrip-unregistered=True" "$work/core.out" "JSON without registration round-trips"
assert_contains "set={a,b}"       "$work/core.out" "value set is canonical: sorted, de-duplicated"
assert_contains "contains=True"   "$work/core.out" "value set membership"

# ── Native AOT: core + NodaTime, published and run as a native binary ────────────────────────────

cat > "$work/aot.cs" <<'CSHARP'
using System.Collections;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges;
using NodaTime;

var ctx = SmokeJson.Default;

void Case(string name, Func<string> run)
{
    try { Console.WriteLine($"{name}={run()}"); }
    catch (Exception e) { Console.WriteLine($"{name}=FAILED {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
}

var h1 = DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

// A union, its variant and a dictionary key: closed converters named by the types' attributes.
Case("aot-range", () => JsonSerializer.Serialize(h1, ctx.DateRange));
Case("aot-variant", () => JsonSerializer.Deserialize("\"[2026-01-01,2026-06-30]\"", ctx.Finite)!.ToString());
Case("aot-key", () => JsonSerializer.Serialize(new Dictionary<DateRange, int> { [h1] = 181 }, ctx.DictionaryDateRangeInt32));

// Generic types: before 9.0.0 these were built with MakeGenericType and failed under AOT.
Case("aot-rangeset", () => JsonSerializer.Serialize(RangeSet<DateRange, DateOnly>.From([h1]), ctx.RangeSetDateRangeDateOnly));
// Only ever deserialized — no code in this program constructs these types.
Case("aot-rangeset-read", () => JsonSerializer.Deserialize("\"{[1,5],[7,10]}\"", ctx.RangeSetInt32RangeInt32)!.ToString());
Case("aot-noda-rangeset-read", () => JsonSerializer.Deserialize("\"{[2026-01-01,2026-01-31]}\"", ctx.RangeSetLocalDateRangeLocalDate)!.ToString());
Case("aot-wrapper", () =>
{
    var keys = StringSet<Key>.From(Key.Parse("users.read", null));
    var json = JsonSerializer.Serialize(keys, ctx.StringSetKey);
    return json + (JsonSerializer.Deserialize(json, ctx.StringSetKey) == keys ? " roundtrip" : " MISMATCH");
});
// A validated-wrapper set this program only ever deserializes.
Case("aot-wrapper-read", () => JsonSerializer.Deserialize("[2,1]", ctx.Int32SetCode) is { } codes ? codes.ToString() : "null");
// The documented minimal context for a primitive set: the set and its element type, even string.
Case("aot-stringset", () =>
{
    var tags = StringSet.From("beta", "alpha");
    var json = JsonSerializer.Serialize(tags, ctx.StringSet);
    return json + (JsonSerializer.Deserialize(json, ctx.StringSet) == tags ? " roundtrip" : " MISMATCH");
});
// A double set writes PostgreSQL's float8out text through WriteRawValue: 2^-25 reads back exactly,
// where System.Text.Json's own writer prints a text one unit lower.
Case("aot-float64set", () =>
{
    var weights = Float64Set.From(1e23, 2.9802322387695312e-08);
    var json    = JsonSerializer.Serialize(weights, ctx.Float64Set);
    return json + (JsonSerializer.Deserialize(json, ctx.Float64Set) == weights ? " roundtrip" : " MISMATCH");
});
// A double-backed wrapper set this program only ever deserializes: its own closed converters.
Case("aot-float64set-wrapper-read", () => JsonSerializer.Deserialize("[1e23,2.9802322387695312e-08]", ctx.Float64SetRatio)!.ToString());
// The float family, read only: its own closed converters, and real's text (1e+06, never 1000000).
Case("aot-float32set-wrapper-read", () => JsonSerializer.Deserialize("[1000000,0.1]", ctx.Float32SetPortion)!.ToString());
// Through object: the serializer resolves the runtime type (the variant) from the context.
Case("aot-object", () => JsonSerializer.Serialize<object>(h1, ctx.Object));
// A closed temporal set reads its elements with the literal grammar: an offset is required.
Case("aot-dtoset", () => JsonSerializer.Serialize(
    JsonSerializer.Deserialize("[\"2026-01-01T09:30:00+01:00\"]", ctx.DateTimeOffsetSet)!, ctx.DateTimeOffsetSet));
Case("aot-dtoset-offsetless", () => JsonSerializer.Deserialize("[\"2026-01-01T09:30:00\"]", ctx.DateTimeOffsetSet)!.ToString());
// Declared as an interface: written through the runtime type's contract, which the context lists.
Case("aot-interface", () => JsonSerializer.Serialize(new Declared(h1, StringSet.From("alpha")), ctx.Declared));
// A union converter on the options reaches the variant through object, and reads into a variant.
var tagged = new SmokeJson(new JsonSerializerOptions { Converters = { new TaggedDateRange() } });
Case("aot-union-converter", () => JsonSerializer.Serialize<object>(h1, tagged.Object)
                                + " " + JsonSerializer.Deserialize("\"tag:[2026-01-01,2026-06-30]\"", tagged.Finite));
// Default options under AOT: reflection-based serialization is off, so every type throws — these
// included, typed or through object. A converter attribute does not bring the reflection back.
#pragma warning disable IL2026, IL3050 // The reflection-based overloads are what is tested.
Case("aot-default-options", () => string.Join(" ", new Func<string>[]
{
    () => JsonSerializer.Serialize(h1),
    () => JsonSerializer.Serialize((DateRange.Finite)h1),
    () => JsonSerializer.Serialize(RangeSet<DateRange, DateOnly>.From([h1])),
    () => JsonSerializer.Serialize(StringSet.From("alpha")),
    () => JsonSerializer.Serialize<object>(h1),
    () => JsonSerializer.Serialize<object>(StringSet.From("alpha")),
}.Select(write => { try { return "WROTE " + write(); } catch (Exception e) { return e.GetType().Name; } })));
#pragma warning restore IL2026, IL3050
#pragma warning disable IL2026 // TypeDescriptor is not trim-safe in general; the converter lookup is what is tested.
Case("aot-typeconverter", () => TypeDescriptor.GetConverter(typeof(RangeSet<Int64Range, long>)).ConvertFromInvariantString("{[1,5]}")!.ToString()!);
#pragma warning restore IL2026

// ── The 10.0.0 additions under AOT (#254) ──────────────────────────────────────────────────────────
// The float ranges: a value, a variant and a dictionary key, and range sets this program never builds
// (the closed-converter registration case). The texts are PostgreSQL's float8out and float4out.
var f64 = Float64Range.CreateFinite(0.1, 2.9802322387695312e-08 + 1);
Case("aot-f64-range", () => JsonSerializer.Serialize(f64, ctx.Float64Range));
Case("aot-f64-range-read", () => JsonSerializer.Deserialize("\"[1e23,2e23)\"", ctx.Float64Range)!.ToString());
Case("aot-f64-variant-read", () => JsonSerializer.Deserialize("\"[1.5,)\"", ctx.Float64RangeUnboundedEnd)!.ToString());
Case("aot-f64-key", () => JsonSerializer.Serialize(new Dictionary<Float64Range, int> { [f64] = 1 }, ctx.DictionaryFloat64RangeInt32));
Case("aot-f64-rangeset-read", () => JsonSerializer.Deserialize("\"{[1,2),[3,4)}\"", ctx.RangeSetFloat64RangeDouble)!.ToString());
Case("aot-f32-range-read", () => JsonSerializer.Deserialize("\"[0.1,1e6)\"", ctx.Float32Range)!.ToString());
Case("aot-f32-rangeset-read", () => JsonSerializer.Deserialize("\"{[0.1,1e6)}\"", ctx.RangeSetFloat32RangeSingle)!.ToString());
Case("aot-f32-set-read", () => JsonSerializer.Deserialize("[1000000,0.1]", ctx.Float32Set)!.ToString());
Case("aot-f64-nan-refused", () => JsonSerializer.Deserialize("\"[NaN,1)\"", ctx.Float64Range)!.ToString());

// IComparable in PostgreSQL's range order, the non-generic path included (Array.Sort over object, ArrayList).
object?[] boxed = [Int32Range.CreateFinite(5, 9), null, Int32Range.Empty, Int32Range.CreateUnboundedStart(3)];
Case("aot-icomparable-object-sort", () => { Array.Sort(boxed); return string.Join(" ", boxed.Select(b => b?.ToString() ?? "null")); });
var months = new List<DateRange> { DateRange.Month(2026, 3), DateRange.Empty, DateRange.Month(2026, 1) };
Case("aot-icomparable-generic-sort", () => { months.Sort(); return string.Join(" ", months); });
var boxedSets = new ArrayList { RangeSet<Int64Range, long>.Parse("{[5,6]}", null), RangeSet<Int64Range, long>.Parse("{[1,2]}", null) };
Case("aot-icomparable-rangeset-arraylist", () => { boxedSets.Sort(); return string.Join(" ", boxedSets.Cast<object>()); });
Case("aot-icomparable-comparer-default", () => Comparer<LocalDateRange>.Default.Compare(LocalDateRange.Month(2026, 1), LocalDateRange.Month(2026, 2)).ToString());

// Shift, Expand, Split and Values(step), through JSON where the result has a converter.
Case("aot-shift-int64", () => JsonSerializer.Serialize(Int64Range.CreateFinite(1, 10).Shift(5), ctx.Int64Range));
Case("aot-expand-decimal", () => JsonSerializer.Serialize(DecimalRange.CreateFinite(1.5m, 2.5m).Expand(0.25m), ctx.DecimalRange));
Case("aot-shift-float64", () => JsonSerializer.Serialize(Float64Range.CreateFinite(0.1, 0.2).Shift(0.1), ctx.Float64Range));
Case("aot-split-int64", () => JsonSerializer.Serialize(Int64Range.CreateFinite(1, 10).Split(4).ToList(), ctx.ListInt64Range));
Case("aot-split-float32", () => JsonSerializer.Serialize(Float32Range.CreateFinite(0f, 1f).Split(0.25f).ToList(), ctx.ListFloat32Range));
Case("aot-split-datetime", () => JsonSerializer.Serialize(DateTimeRange.CreateFinite(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)).Split(TimeSpan.FromHours(4)).ToList(), ctx.ListDateTimeRange));
Case("aot-split-by-month", () => JsonSerializer.Serialize(DateRange.CreateFinite(new DateOnly(2026, 1, 15), new DateOnly(2026, 3, 10)).Split(DateRange.Month).ToList(), ctx.ListDateRange));
Case("aot-values-step-int32", () => string.Join(",", Int32Range.CreateFinite(1, 10).Values(3)));
Case("aot-shift-localdate", () => JsonSerializer.Serialize(LocalDateRange.Month(2026, 2).Shift(Period.FromMonths(1)), ctx.LocalDateRange));

// The calendar factories, core and NodaTime, and one as a dictionary key.
Case("aot-cal-date", () => string.Join(" ", DateRange.Month(2026, 2), DateRange.Quarter(2026, 4), DateRange.Year(2026), DateRange.IsoWeek(2026, 1), DateRange.IsoWeek(new DateOnly(2027, 1, 1))));
Case("aot-cal-noda", () => string.Join(" ", LocalDateRange.IsoWeek(2026, 53), LocalDateRange.Month(new LocalDate(2024, 2, 10)), YearMonthRange.Quarter(2026, 2)));
Case("aot-cal-json", () => JsonSerializer.Serialize(new Dictionary<YearMonthRange, LocalDateRange> { [YearMonthRange.Quarter(2026, 1)] = LocalDateRange.IsoWeek(2026, 1) }, ctx.DictionaryYearMonthRangeLocalDateRange));

// Gap, Gaps and the aggregates, over ranges and over sets.
Case("aot-gaps", () => RangeSet<DateRange, DateOnly>.Parse("{[2026-01-01,2026-01-05],[2026-01-10,2026-01-12]}", null).Gaps().ToString());
Case("aot-gap", () => Int32Range.CreateFinite(1, 3).Gap(Int32Range.CreateFinite(10, 12)).ToString());
Case("aot-range-agg", () => JsonSerializer.Serialize(new[] { Float64Range.CreateFinite(1, 2), Float64Range.CreateFinite(1.5, 3) }.RangeAgg(), ctx.RangeSetFloat64RangeDouble));
Case("aot-range-intersect-agg-sets", () => new[] { RangeSet<Int32Range, int>.Parse("{[1,2]}", null), RangeSet<Int32Range, int>.Parse("{[2,5]}", null) }.RangeIntersectAgg()!.ToString());

// Every value set family's ToString. Before #299 each ran through the interface's default
// IFormattable.ToString, whose shared-generic stub ILCompiler 11 RC2 compiles to a trap.
Case("aot-set-tostring-every-family", () => string.Join(" ",
    StringSet.From("a"), GuidSet.From(Guid.Empty), Int16Set.From(1), Int32Set.From(1), Int64Set.From(1),
    DecimalSet.From(1.5m), Float32Set.From(0.5f), Float64Set.From(0.5),
    DateSet.From(new DateOnly(2026, 1, 1)), TimeSet.From(new TimeOnly(9, 30)),
    DateTimeSet.From(new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Utc)),
    DateTimeOffsetSet.From(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero)),
    LocalDateSet.From(new LocalDate(2026, 1, 1)), LocalTimeSet.From(new LocalTime(9, 30)),
    LocalDateTimeSet.From(new LocalDateTime(2026, 1, 1, 9, 30)), InstantSet.From(Instant.FromUtc(2026, 1, 1, 9, 30)),
    YearMonthSet.From(new YearMonth(2026, 1))));

public sealed record Declared(CodoMetis.ValueRanges.Core.IRange<DateOnly> Period, CodoMetis.ValueRanges.Core.IValueSet<string> Tags);

public sealed class TaggedDateRange : JsonConverter<DateRange>
{
    public override DateRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateRange.Parse(reader.GetString()!["tag:".Length..], null);

    public override void Write(Utf8JsonWriter writer, DateRange value, JsonSerializerOptions options)
        => writer.WriteStringValue($"tag:{value}");
}

public readonly record struct Key(string Value) : IFormattable, IParsable<Key>
{
    public static Key Parse(string s, IFormatProvider? provider) => new(s);
    public static bool TryParse(string? s, IFormatProvider? provider, out Key result) { result = new(s ?? ""); return s is not null; }
    public string ToString(string? format, IFormatProvider? formatProvider) => Value;
}

[JsonSerializable(typeof(DateRange))]
[JsonSerializable(typeof(DateRange.Finite))]
[JsonSerializable(typeof(Dictionary<DateRange, int>))]
[JsonSerializable(typeof(RangeSet<DateRange, DateOnly>))]
[JsonSerializable(typeof(RangeSet<Int32Range, int>))]
[JsonSerializable(typeof(RangeSet<LocalDateRange, LocalDate>))]
[JsonSerializable(typeof(StringSet<Key>))]
[JsonSerializable(typeof(Key))]
[JsonSerializable(typeof(Int32Set<Code>))]
[JsonSerializable(typeof(Code))]
[JsonSerializable(typeof(StringSet))]
[JsonSerializable(typeof(Float64Set))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(Float64Set<Ratio>))]
[JsonSerializable(typeof(Ratio))]
[JsonSerializable(typeof(Float32Set<Portion>))]
[JsonSerializable(typeof(Portion))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(DateTimeOffsetSet))]
[JsonSerializable(typeof(Float64Range))]
[JsonSerializable(typeof(Float64Range.UnboundedEnd), TypeInfoPropertyName = "Float64RangeUnboundedEnd")]
[JsonSerializable(typeof(Dictionary<Float64Range, int>))]
[JsonSerializable(typeof(RangeSet<Float64Range, double>))]
[JsonSerializable(typeof(Float32Range))]
[JsonSerializable(typeof(RangeSet<Float32Range, float>))]
[JsonSerializable(typeof(Float32Set))]
[JsonSerializable(typeof(Int64Range))]
[JsonSerializable(typeof(DecimalRange))]
[JsonSerializable(typeof(List<Int64Range>))]
[JsonSerializable(typeof(List<Float32Range>))]
[JsonSerializable(typeof(List<DateTimeRange>))]
[JsonSerializable(typeof(List<DateRange>))]
[JsonSerializable(typeof(LocalDateRange))]
[JsonSerializable(typeof(Dictionary<YearMonthRange, LocalDateRange>))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(Declared))]
internal partial class SmokeJson : JsonSerializerContext;

public readonly record struct Ratio(double Value) : IFormattable, IParsable<Ratio>, IComparable<Ratio>
{
    public static Ratio Parse(string s, IFormatProvider? provider) => new(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
    public static bool TryParse(string? s, IFormatProvider? provider, out Ratio result)
    {
        var ok = double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v);
        result = new(v);
        return ok;
    }
    public int CompareTo(Ratio other) => Value.CompareTo(other.Value);
    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct Portion(float Value) : IFormattable, IParsable<Portion>, IComparable<Portion>
{
    public static Portion Parse(string s, IFormatProvider? provider) => new(float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
    public static bool TryParse(string? s, IFormatProvider? provider, out Portion result)
    {
        var ok = float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v);
        result = new(v);
        return ok;
    }
    public int CompareTo(Portion other) => Value.CompareTo(other.Value);
    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct Code(int Value) : IFormattable, IParsable<Code>, IComparable<Code>
{
    public static Code Parse(string s, IFormatProvider? provider) => new(int.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
    public static bool TryParse(string? s, IFormatProvider? provider, out Code result)
    {
        var ok = int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var v);
        result = new(v);
        return ok;
    }
    public int CompareTo(Code other) => Value.CompareTo(other.Value);
    public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
CSHARP

consumer_sdk="${SMOKE_AOT_SDK:-}"
scaffold_consumer aot CodoMetis.ValueRanges.NodaTime
consumer_sdk=""

echo "==> aot: publishing with Native AOT (SDK $(dotnet --version))"
rid="$(dotnet --info | awk '/^ *RID:/ { print $2; exit }')"
{
    echo '<Project><ItemGroup>'
    for arg in ${SMOKE_AOT_LINKER_ARGS:-}; do echo "  <LinkerArg Include=\"$arg\" />"; done
    echo '</ItemGroup></Project>'
} > "$work/aot/Directory.Build.targets"
run dotnet publish "$work/aot" -c Release -r "$rid" -p:PublishAot=true -p:TrimmerSingleWarn=false -o "$work/aot/native"
echo "$output" > "$work/aot.publish"
"$work/aot/native/aot" > "$work/aot.native.out" 2>&1 && native_exit=0 || native_exit=$?
sed 's/^/      /' "$work/aot.native.out"

echo "==> aot: asserting"
# A trap after the last asserted line would otherwise pass: 133 is SIGTRAP, what ILCompiler 11 RC2
# compiled into every value set's ToString before #299.
if (( native_exit != 0 )); then
    echo "  FAIL: the native binary exited with $native_exit"
    failed=1
else
    echo "  ok: the native binary ran to the end"
fi
if grep -E "warning IL[0-9]+" "$work/aot.publish" | grep -q "CodoMetis"; then
    echo "  FAIL: the packages raise trim/AOT warnings in a consumer's publish"
    grep -E "warning IL[0-9]+" "$work/aot.publish" | grep "CodoMetis" | sed 's/^/      /'
    failed=1
else
    echo "  ok: no trim/AOT warnings from the packages"
fi
assert_contains 'aot-range="[2026-01-01,2026-06-30]"'          "$work/aot.native.out" "AOT: a range serializes through its attribute"
assert_contains 'aot-variant=[2026-01-01,2026-06-30]'          "$work/aot.native.out" "AOT: a variant deserializes"
assert_contains 'aot-key={"[2026-01-01,2026-06-30]":181}'      "$work/aot.native.out" "AOT: a range is a dictionary key"
assert_contains 'aot-rangeset="{[2026-01-01,2026-06-30]}"'     "$work/aot.native.out" "AOT: a RangeSet serializes"
assert_contains 'aot-rangeset-read={[1,5],[7,10]}'             "$work/aot.native.out" "AOT: a RangeSet the program never constructs deserializes"
assert_contains 'aot-noda-rangeset-read={[2026-01-01,2026-01-31]}' "$work/aot.native.out" "AOT: a NodaTime RangeSet the program never constructs deserializes"
assert_contains 'aot-wrapper=["users.read"] roundtrip'         "$work/aot.native.out" "AOT: a validated-wrapper set round-trips"
assert_contains 'aot-wrapper-read={1,2}'                       "$work/aot.native.out" "AOT: a validated-wrapper set the program never constructs deserializes"
assert_contains 'aot-stringset=["alpha","beta"] roundtrip'     "$work/aot.native.out" "AOT: a primitive set round-trips through the documented minimal context"
assert_contains 'aot-float64set=[2.9802322387695312e-08,9.999999999999999e+22] roundtrip' "$work/aot.native.out" "AOT: a Float64Set writes PostgreSQL's text and round-trips 2^-25"
assert_contains 'aot-float64set-wrapper-read={2.9802322387695312e-08,9.999999999999999e+22}' "$work/aot.native.out" "AOT: a Float64Set<T> the program never constructs deserializes"
assert_contains 'aot-float32set-wrapper-read={0.1,1e+06}' "$work/aot.native.out" "AOT: a Float32Set<T> the program never constructs deserializes and prints real's text"
assert_contains 'aot-object="[2026-01-01,2026-06-30]"'         "$work/aot.native.out" "AOT: a range serializes through object"
assert_contains 'aot-dtoset=["2026-01-01T09:30:00+01:00"]'     "$work/aot.native.out" "AOT: a DateTimeOffsetSet round-trips"
assert_contains 'aot-dtoset-offsetless=FAILED JsonException'   "$work/aot.native.out" "AOT: a DateTimeOffsetSet element without an offset is refused"
assert_contains 'aot-default-options=InvalidOperationException InvalidOperationException InvalidOperationException InvalidOperationException InvalidOperationException InvalidOperationException' "$work/aot.native.out" "AOT: default options throw InvalidOperationException for a range, a variant, a RangeSet and a value set, typed or through object"
assert_contains 'aot-typeconverter={[1,5]}'                    "$work/aot.native.out" "AOT: TypeConverter for a RangeSet the program never constructs"
assert_contains 'aot-interface={"Period":"[2026-01-01,2026-06-30]","Tags":["alpha"]}' "$work/aot.native.out" "AOT: interface-declared properties write through the runtime type"
assert_contains 'aot-union-converter="tag:[2026-01-01,2026-06-30]" [2026-01-01,2026-06-30]' "$work/aot.native.out" "AOT: a union converter on the options reaches the variant"
assert_contains 'aot-f64-range="[0.1,1.0000000298023224)"' "$work/aot.native.out" "AOT: a Float64Range serializes as float8out text"
assert_contains 'aot-f64-range-read=[9.999999999999999e+22,1.9999999999999998e+23)' "$work/aot.native.out" "AOT: a Float64Range deserializes"
assert_contains 'aot-f64-variant-read=[1.5,)' "$work/aot.native.out" "AOT: a Float64Range variant deserializes"
assert_contains 'aot-f64-key={"[0.1,1.0000000298023224)":1}' "$work/aot.native.out" "AOT: a Float64Range is a dictionary key"
assert_contains 'aot-f64-rangeset-read={[1,2),[3,4)}' "$work/aot.native.out" "AOT: a RangeSet<Float64Range> the program never constructs deserializes"
assert_contains 'aot-f32-range-read=[0.1,1e+06)' "$work/aot.native.out" "AOT: a Float32Range deserializes and prints float4out text"
assert_contains 'aot-f32-rangeset-read={[0.1,1e+06)}' "$work/aot.native.out" "AOT: a RangeSet<Float32Range> the program never constructs deserializes"
assert_contains 'aot-f32-set-read={0.1,1e+06}' "$work/aot.native.out" "AOT: a Float32Set deserializes"
assert_contains 'aot-f64-nan-refused=FAILED JsonException' "$work/aot.native.out" "AOT: a NaN bound is refused"
assert_contains 'aot-icomparable-object-sort=null empty (,2] [5,9]' "$work/aot.native.out" "AOT: Array.Sort over object uses the non-generic IComparable"
assert_contains 'aot-icomparable-generic-sort=empty [2026-01-01,2026-01-31] [2026-03-01,2026-03-31]' "$work/aot.native.out" "AOT: List<DateRange>.Sort uses the range order"
assert_contains 'aot-icomparable-rangeset-arraylist={[1,2]} {[5,6]}' "$work/aot.native.out" "AOT: ArrayList.Sort over RangeSets"
assert_contains 'aot-icomparable-comparer-default=-1' "$work/aot.native.out" "AOT: Comparer<LocalDateRange>.Default"
assert_contains 'aot-shift-int64="[6,15]"' "$work/aot.native.out" "AOT: Shift"
assert_contains 'aot-expand-decimal="[1.25,2.75)"' "$work/aot.native.out" "AOT: Expand"
assert_contains 'aot-shift-float64="[0.2,0.30000000000000004)"' "$work/aot.native.out" "AOT: Shift on a Float64Range is IEEE addition"
assert_contains 'aot-split-int64=["[1,4]","[5,8]","[9,10]"]' "$work/aot.native.out" "AOT: Split on a discrete range"
assert_contains 'aot-split-float32=["[0,0.25)","[0.25,0.5)","[0.5,0.75)","[0.75,1)"]' "$work/aot.native.out" "AOT: Split on a Float32Range"
assert_contains 'aot-split-datetime=["[2026-01-01T00:00:00.0000000Z,2026-01-01T04:00:00.0000000Z)","[2026-01-01T04:00:00.0000000Z,2026-01-01T08:00:00.0000000Z)","[2026-01-01T08:00:00.0000000Z,2026-01-01T12:00:00.0000000Z)"]' "$work/aot.native.out" "AOT: Split by a TimeSpan"
assert_contains 'aot-split-by-month=["[2026-01-15,2026-01-31]","[2026-02-01,2026-02-28]","[2026-03-01,2026-03-10]"]' "$work/aot.native.out" "AOT: Split(DateRange.Month)"
assert_contains 'aot-values-step-int32=1,4,7,10' "$work/aot.native.out" "AOT: Values(step)"
assert_contains 'aot-shift-localdate="[2026-03-01,2026-03-31]"' "$work/aot.native.out" "AOT: Shift by a NodaTime Period"
assert_contains 'aot-cal-date=[2026-02-01,2026-02-28] [2026-10-01,2026-12-31] [2026-01-01,2026-12-31] [2025-12-29,2026-01-04] [2026-12-28,2027-01-03]' "$work/aot.native.out" "AOT: the DateRange calendar factories"
assert_contains 'aot-cal-noda=[2026-12-28,2027-01-03] [2024-02-01,2024-02-29] [2026-04,2026-06]' "$work/aot.native.out" "AOT: the NodaTime calendar factories"
assert_contains 'aot-cal-json={"[2026-01,2026-03]":"[2025-12-29,2026-01-04]"}' "$work/aot.native.out" "AOT: calendar ranges as JSON key and value"
assert_contains 'aot-gaps={[2026-01-06,2026-01-09]}' "$work/aot.native.out" "AOT: Gaps"
assert_contains 'aot-gap=[4,9]' "$work/aot.native.out" "AOT: Gap"
assert_contains 'aot-range-agg="{[1,3)}"' "$work/aot.native.out" "AOT: RangeAgg over Float64Range"
assert_contains 'aot-range-intersect-agg-sets={[2,2]}' "$work/aot.native.out" "AOT: RangeIntersectAgg over RangeSets"
assert_contains 'aot-set-tostring-every-family={a} {00000000-0000-0000-0000-000000000000} {1} {1} {1} {1.5} {0.5} {0.5} {2026-01-01} {09:30:00.0000000} {2026-01-01T09:30:00.0000000Z} {2026-01-01T09:30:00.0000000+00:00} {2026-01-01} {09:30:00} {2026-01-01T09:30:00} {2026-01-01T09:30:00Z} {2026-01}' "$work/aot.native.out" "AOT: ToString on every value set family (#299)"

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

# ── EF Core + EFCore.ComplexIndexes.PostgreSQL: a joint consumer ─────────────────────────────────
#
# The other package accepts a temporal-constraint period or an exclusion-constraint column by its store
# type name, so the pairing only works if the packaged mappings carry their range store types. The
# integration suite proves it against PostgreSQL through a test-only reference; this proves the two
# packages restore and compose in one consumer, from the feeds a consumer uses. GenerateCreateScript
# runs the other package's differ (registered by UseNpgsqlComplexIndexes()), which renders the same
# statements a migration would, and needs no database.
complex_indexes_version=5.4.0

cat > "$work/complexindexes.cs" <<'CSHARP'
using CodoMetis.ValueRanges;
using EFCore.ComplexIndexes.PostgreSQL;
using Microsoft.EntityFrameworkCore;

await using var db = new SmokeContext();
Console.WriteLine(db.Database.GenerateCreateScript());

public sealed class Booking
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public DateRange Period { get; set; } = DateRange.Empty;

    public RangeSet<DateRange, DateOnly> Days { get; set; } = RangeSet<DateRange, DateOnly>.Empty;

    public TimeRange Hours { get; set; } = TimeRange.Empty;
}

public sealed class SmokeContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseNpgsql("Host=localhost;Database=smoke", npgsql => npgsql.UseValueRanges())
                  .UseNpgsqlComplexIndexes();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasPostgresRange("timerange", "time");
        model.Entity<Booking>(booking =>
        {
            booking.HasExclusionConstraint(ex => ex.WithEquality(b => b.RoomId).WithOverlaps(b => b.Period).HasName("ex_period"));
            booking.HasTemporalConstraint(b => b.RoomId, b => b.Days, "uq_days");
            booking.HasTemporalConstraint(b => b.RoomId, b => b.Hours, "uq_hours");
        });
    }
}
CSHARP

scaffold_consumer complexindexes CodoMetis.ValueRanges.EFCore.PostgreSQL "EFCore.ComplexIndexes.PostgreSQL@$complex_indexes_version"

echo "==> complexindexes: asserting"
for pair in "CodoMetis.ValueRanges.EFCore.PostgreSQL $version" "EFCore.ComplexIndexes.PostgreSQL $complex_indexes_version"; do
    id="${pair% *}" expected="${pair#* }"
    assert_contains "> $id " "$work/complexindexes.packages" "$id installed"
    if grep -F "> $id " "$work/complexindexes.packages" | grep -qvF " $expected"; then
        echo "  FAIL: $id resolved at a version other than $expected"
        grep -F "> $id " "$work/complexindexes.packages" | sed 's/^/      /'
        failed=1
    fi
done
assert_contains 'CREATE TYPE timerange AS RANGE'                                    "$work/complexindexes.out" "HasPostgresRange creates timerange"
assert_contains '"Period" daterange NOT NULL'                                       "$work/complexindexes.out" "DateRange column is daterange"
assert_contains 'EXCLUDE USING gist ("RoomId" WITH =, "Period" WITH &&)'            "$work/complexindexes.out" "exclusion constraint over a DateRange column"
assert_contains 'UNIQUE ("RoomId", "Days" WITHOUT OVERLAPS)'                        "$work/complexindexes.out" "temporal constraint over a RangeSet (datemultirange) column"
assert_contains 'UNIQUE ("RoomId", "Hours" WITHOUT OVERLAPS)'                       "$work/complexindexes.out" "temporal constraint over a TimeRange (timerange) column"

[[ $failed -eq 0 ]] || { echo; echo "consumer smoke test FAILED"; exit 1; }
echo
echo "consumer smoke test passed ($version)"

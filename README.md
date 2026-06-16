# CodoMetis.ValueRanges

[![NuGet](https://img.shields.io/nuget/v/CodoMetis.ValueRanges)](https://www.nuget.org/packages/CodoMetis.ValueRanges)
[![Context7](https://img.shields.io/badge/Context7-Indexed-3B82F6)](https://context7.com/caffeinatedcoder/codometis.valueranges)
[![dev.to](https://img.shields.io/badge/dev.to-Article-3B82F6)](https://dev.to/caffeinatedcoder/the-interval-is-the-thing-modelling-range-types-as-first-class-domain-objects-in-net-3jha)
[![hashnode](https://img.shields.io/badge/hashnode.dev-Article-3B82F6)](https://codometis.hashnode.dev/stop-modeling-time-with-two-columns-codometis-valueranges-brings-interval-logic-to-your-net-domain?utm_source=hashnode&utm_medium=feed)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

Fully functional, in-memory range types for .NET — complete interval algebra without any database dependency.

## Overview

`CodoMetis.ValueRanges` provides concrete, type-safe range types covering the same six value domains as PostgreSQL's built-in range types (`int4range`, `int8range`, `numrange`, `daterange`, `tsrange`, `tstzrange`), together with a full in-memory implementation of every range operation PostgreSQL exposes.

The library is designed to stand on its own: all operations execute in process, with no ORM or database driver required. A companion EF Core package is planned that will bridge these types to `NpgsqlRange<T>` for automatic LINQ-to-SQL translation, making the same code work both in memory and as PostgreSQL queries.

### Design

Each range type is modelled as a **discriminated union** of five sealed variants:

| Variant          | Represents                  | Interval notation |
|------------------|-----------------------------|-----------------|
| `Finite`         | Bounded on both sides       | `[1, 10]`       |
| `UnboundedStart` | Unbounded on the left       | `(-∞, 10]`      |
| `UnboundedEnd`   | Unbounded on the right      | `[1, +∞)`       |
| `EmptyRange`     | The empty range (no values) | `∅`             |
| `Infinity`       | Unbounded on both ends      | `(-∞, +∞)`      |

The *shape* of a range is encoded in its static type. An `UnboundedEnd` range has no `End` property — the property does not exist at compile time. An `Empty` range carries no bound information whatsoever. Invalid states are unrepresentable by construction, and pattern matching over a range is exhaustive with compiler-enforced coverage.

## Supported Types

| .NET type              | PostgreSQL equivalent | Element type     | Discrete |
|------------------------|-----------------------|------------------|----------|
| `Int32Range`           | `int4range`           | `int`            | ✓        |
| `Int64Range`           | `int8range`           | `long`           | ✓        |
| `DecimalRange`         | `numrange`            | `decimal`        | —        |
| `DateRange`            | `daterange`           | `DateOnly`       | ✓        |
| `DateTimeRange`        | `tsrange`             | `DateTime`       | —        |
| `DateTimeOffsetRange`  | `tstzrange`           | `DateTimeOffset` | —        |

Discrete types (`int`, `long`, `DateOnly`) know their step size. This matters for adjacency checks: `[1, 5]` and `[6, 10]` are adjacent for integers because there is no integer between 5 and 6.

## Installation

```sh
dotnet add package CodoMetis.ValueRanges
```

> Requires .NET 10 or later.

## Creating Ranges

Every type exposes four static factory methods:

```csharp
// Bounded on both sides
Int32Range closed = Int32Range.CreateFinite(1, 10);                       // [1, 10]
Int32Range half   = Int32Range.CreateFinite(1, 10, endInclusive: false);  // [1, 10)

// Unbounded on the left — end exclusive by default
DateRange upToToday = DateRange.CreateUnboundedStart(DateOnly.FromDateTime(DateTime.Today)); // (-∞, today)
// Inclusive variant:
DateRange throughToday = DateRange.CreateUnboundedStart(DateOnly.FromDateTime(DateTime.Today), endInclusive: true);

// Unbounded on the right — start inclusive by default
Int32Range fromFive = Int32Range.CreateUnboundedEnd(5);  // [5, +∞)

// Unbounded on both ends
Int32Range everything = Int32Range.Infinite;  // (-∞, +∞)

// Explicitly empty
Int32Range empty = Int32Range.Empty;
```

`CreateFinite()` automatically returns an `Empty` when the arguments form a degenerate or inverted interval (e.g. `start > end`, or equal bounds that are both exclusive).

**Default boundary inclusiveness:**

| Range type                                                 | `CreateFinite()` default   |
|------------------------------------------------------------|----------------------------|
| `Int32Range`, `Int64Range`, `DateRange`                    | `[start, end]` — closed    |
| `DecimalRange`, `DateTimeRange`, `DateTimeOffsetRange`     | `[start, end)` — half-open |

Discrete types default to fully closed intervals; continuous types default to the half-open convention that is conventional for monetary amounts and timestamps.

## Pattern Matching

The nested sealed records are first-class citizens and ideal for exhaustive pattern matching:

```csharp
string Describe(Int32Range range) => range switch
{
    Int32Range.EmptyRange       => "empty",
    Int32Range.Finite f         => $"[{f.Start}, {f.End}]",
    Int32Range.UnboundedStart s => $"(-∞, {s.End}]",
    Int32Range.UnboundedEnd e   => $"[{e.Start}, +∞)",
    Int32Range.Infinity         => "(-∞, +∞)",
};
```

The private constructor on the abstract base record prevents any subtypes being declared outside the assembly, so the compiler guarantees this switch is complete.

## Query Operations

All query methods are extension methods on `IRange<T>` and work across any combination of range shapes.

### Containment

```csharp
var sprint = DateRange.CreateFinite(new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 17));

sprint.Contains(new DateOnly(2025, 1, 10));  // true  — point containment
sprint.Contains(new DateOnly(2025, 1, 20));  // false

var inner = DateRange.CreateFinite(new DateOnly(2025, 1, 8), new DateOnly(2025, 1, 14));
sprint.Contains(inner);       // true  — range containment
inner.IsContainedBy(sprint);  // true  — symmetric alias
```

### Overlap

```csharp
var a = Int32Range.CreateFinite(1, 5);
var b = Int32Range.CreateFinite(5, 10);
var c = Int32Range.CreateFinite(6, 10);

a.Overlaps(b);  // true  — they share the point 5
a.Overlaps(c);  // false
```

### Adjacency

Two ranges are adjacent when they are contiguous with no gap and no overlap — their union would form a single range.

```csharp
// Discrete: consecutive integer values are adjacent
var a = Int32Range.CreateFinite(1, 5);
var b = Int32Range.CreateFinite(6, 10);
a.IsAdjacentTo(b);  // true — NextValueAfter(5) == 6

// Continuous: touching bounds with complementary inclusiveness
var x = DecimalRange.CreateFinite(1m, 5m, endInclusive: true);      // [1, 5]
var y = DecimalRange.CreateFinite(5m, 10m, startInclusive: false);  // (5, 10)
x.IsAdjacentTo(y);  // true — one side claims 5, the other does not
```

### Directional Comparisons

```csharp
Int32Range.CreateFinite(1, 3).IsStrictlyLeftOf(Int32Range.CreateFinite(5, 9));  // true
Int32Range.CreateFinite(1, 5).IsStrictlyLeftOf(Int32Range.CreateFinite(5, 9));  // false — they share 5

Int32Range.CreateFinite(7, 9).IsStrictlyRightOf(Int32Range.CreateFinite(1, 5)); // true
```

**PostgreSQL `&<` / `&>` equivalents:**

```csharp
// Does not extend to the right of other  (&<)
Int32Range.CreateFinite(1, 5).DoesNotExtendRightOf(Int32Range.CreateFinite(1, 10));  // true

// Does not extend to the left of other  (&>)
Int32Range.CreateFinite(3, 10).DoesNotExtendLeftOf(Int32Range.CreateFinite(1, 10));  // true
```

## Set Operations

Set operations are extension methods on the concrete range types (any type that implements `IRangeFactory<TRange, T>`).

### Intersection

Returns the largest range contained by both operands. The intersection of two ranges is always expressible as a single range, so `Intersect` returns the range type directly — `Empty` genuinely means an empty intersection.

```csharp
var a = Int32Range.CreateFinite(1, 10);
var b = Int32Range.CreateFinite(5, 15);

Int32Range intersection = a.Intersect(b);       // [5, 10]
a.Intersect(Int32Range.CreateFinite(11, 20));   // Empty — no overlap
```

All shape combinations are handled: `Finite ∩ UnboundedStart`, `UnboundedEnd ∩ UnboundedStart`, and so on, each producing the correctly shaped result type.

### Union

Returns a `RangeSet<TRange, T>` containing every value of both operands. When the ranges overlap or are adjacent, the set holds the single merged range; when they are disjoint, the set holds both — the union of two separated ranges genuinely *is* two ranges, and the result type says so.

```csharp
var a = Int32Range.CreateFinite(1, 5);
var b = Int32Range.CreateFinite(5, 10);
var c = Int32Range.CreateFinite(7, 10);

var ab = a.Union(b);  // { [1, 10] }        — overlapping, one element
var ac = a.Union(c);  // { [1, 5], [7, 10] } — disjoint, two elements

ab.Count;  // 1
ac.Count;  // 2
ac[1];     // [7, 10]
```

Merging an `UnboundedEnd` with an overlapping `Finite` yields an `UnboundedEnd`; an `UnboundedStart` overlapping an `UnboundedEnd` covers the entire domain and yields `{ Infinity }`.

### Except (Set Difference)

Removes the overlap of `other` from the receiver, returning a `RangeSet<TRange, T>` whose cardinality reflects the structural outcome directly.

```csharp
var range  = Int32Range.CreateFinite(1, 10);
var remove = Int32Range.CreateFinite(4, 6);

// [4, 6] is interior to [1, 10] — the result is split in two
var result = range.Except(remove);
// result[0] = [1, 4) ≡ [1, 3]
// result[1] = (6, 10] ≡ [7, 10]
```

| Result       | Meaning                                                            |
|--------------|--------------------------------------------------------------------|
| `0` elements | The receiver is fully contained by `other`; nothing remains        |
| `1` element  | One-sided trim or no overlap; the remaining range                  |
| `2` elements | `other` was strictly interior to the receiver; it is split in two  |

Boundary inclusiveness is inverted at the cut point so that no value is lost or double-counted across the resulting pieces.

## RangeSet — Multirange Support

`RangeSet<TRange, T>` is the in-memory counterpart of a PostgreSQL 14+ multirange (`int4multirange`, `nummultirange`, …): an immutable, always-normalized set of disjoint ranges. Its invariant — elements sorted by lower bound, pairwise disjoint, pairwise non-adjacent — is enforced on every construction: empty ranges are dropped, overlapping or adjacent inputs are merged, and any `Infinity` input collapses the set to `RangeSet<TRange, T>.Infinite`.

```csharp
using IntSet = RangeSet<Int32Range, int>;

// Construction normalizes: [1, 5] and [6, 10] are adjacent for int and merge.
var set = IntSet.From([
    Int32Range.CreateFinite(6, 10),
    Int32Range.CreateFinite(1, 5),
    Int32Range.CreateFinite(20, 30)
]);
// { [1, 10], [20, 30] }

// Query operations
set.Contains(7);                              // true
set.Contains(Int32Range.CreateFinite(2, 8));  // true  — within a single element
set.Overlaps(Int32Range.CreateFinite(15, 25)); // true

// Set operations — single-range and bulk variants, with operator aliases (|, &, -)
set.Union(Int32Range.CreateFinite(11, 19));    // { [1, 30] } — bridges the gap
set | Int32Range.CreateFinite(11, 19);         // { [1, 30] }

set.Intersect(Int32Range.CreateFinite(5, 25)); // { [5, 10], [20, 25] }
set & Int32Range.CreateFinite(5, 25);          // { [5, 10], [20, 25] }

set.Except(Int32Range.CreateFinite(4, 6));     // { [1, 3], [7, 10], [20, 30] }
set - Int32Range.CreateFinite(4, 6);           // { [1, 3], [7, 10], [20, 30] }

// Complement — every value not covered by the set
set.Complement();  // { (-∞, 0], [11, 19], [31, +∞) }
```

The set implements `IReadOnlyList<TRange>` (enumeration in lower-bound order, `Count`, indexer) and structural equality: two sets built from different inputs that normalize identically are equal.

```csharp
var a = IntSet.From([Int32Range.CreateFinite(1, 10)]);
var b = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(6, 10)]);
a.Equals(b);  // true — both normalize to { [1, 10] }
```

## Parsing and Formatting

All range types and `RangeSet<TRange, T>` implement `IParsable<T>` and `IFormattable`. The canonical string representation is the PostgreSQL range literal format — the same syntax PostgreSQL uses on the wire.

### Formatting

`ToString()` (and `IFormattable.ToString(format, provider)`) produces PostgreSQL range literals:

```csharp
Int32Range.CreateFinite(1, 10).ToString()              // "[1,10]"
Int32Range.CreateFinite(1, 10, endInclusive: false)
          .ToString()                                  // "[1,10)"
Int32Range.CreateUnboundedStart(5).ToString()          // "(,5]"
Int32Range.CreateUnboundedEnd(5).ToString()            // "[5,)"
Int32Range.Infinite.ToString()                         // "(,)"
Int32Range.Empty.ToString()                            // "empty"

DateRange.CreateFinite(new DateOnly(2025, 1, 1),
                       new DateOnly(2025, 3, 31)).ToString()
// "[2025-01-01,2025-03-31]"

DateTimeOffsetRange.CreateFinite(
    new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.FromHours(1)),
    new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.FromHours(1))).ToString()
// "[2024-06-01T00:00:00.0000000+01:00,2024-07-01T00:00:00.0000000+01:00)"
```

The optional `format` parameter is forwarded to the element type, so you can control how individual bound values are rendered:

```csharp
((IFormattable)DateRange.CreateFinite(new DateOnly(2025, 1, 1),
                                      new DateOnly(2025, 3, 31)))
    .ToString("MMM d yyyy", CultureInfo.InvariantCulture)
// "[Jan 1 2025,Mar 31 2025]"
```

`RangeSet<TRange, T>` formats as a PostgreSQL multirange literal:

```csharp
IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(7, 10)])
      .ToString()    // "{[1,5],[7,10]}"

IntSet.Empty.ToString()    // "{}"
IntSet.Infinite.ToString() // "{(,)}"
```

### Parsing

Every concrete range type exposes `Parse` and `TryParse` static methods that accept any valid PostgreSQL range literal:

```csharp
var r1 = Int32Range.Parse("[1,10]", null);     // Finite [1, 10]
var r2 = Int32Range.Parse("(,5]", null);       // UnboundedStart (−∞, 5]
var r3 = Int32Range.Parse("[3,)", null);        // UnboundedEnd [3, +∞)
var r4 = Int32Range.Parse("(,)", null);         // Infinity (−∞, +∞)
var r5 = Int32Range.Parse("empty", null);       // Empty

if (Int32Range.TryParse(userInput, null, out var range))
    Console.WriteLine(range);
```

Discrete types canonicalize on parse — `"[1,10)"` is equivalent to `"[1,9]"` and both parse to the same closed `[1, 9]` range:

```csharp
Int32Range.Parse("[1,10)", null).ToString()  // "[1,9]"
```

`RangeSet<TRange, T>` parses multirange literals in the same way:

```csharp
var set = RangeSet<Int32Range, int>.Parse("{[1,5],[7,10]}", null);
set.Count;   // 2
set[0];      // [1, 5]
set[1];      // [7, 10]
```

## JSON Serialization

The `CodoMetis.ValueRanges.Serialization` namespace provides `System.Text.Json` converters for all range types and their multirange counterparts. Ranges serialize as JSON strings in PostgreSQL literal format — compact and round-trippable.

### Registration

Register all converters at once using the `AddRangeConverters()` extension:

```csharp
using CodoMetis.ValueRanges.Serialization;

var options = new JsonSerializerOptions().AddRangeConverters();
```

Or use the factory for automatic registration on any range/multirange type:

```csharp
var options = new JsonSerializerOptions
{
    Converters = { new RangeJsonConverterFactory() }
};
```

In ASP.NET Core, add it to your serializer configuration:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.AddRangeConverters());
```

### Usage

```csharp
var range = Int32Range.CreateFinite(1, 10);
string json = JsonSerializer.Serialize(range, options);   // "\"[1,10]\""

var back = JsonSerializer.Deserialize<Int32Range>(json, options);
// back == Int32Range.CreateFinite(1, 10)

// Multirange
var set = RangeSet<Int32Range, int>.From([
    Int32Range.CreateFinite(1, 5),
    Int32Range.CreateFinite(7, 10)
]);
string setJson = JsonSerializer.Serialize(set, options);   // "\"{[1,5],[7,10]}\""

// Works with all six range types and their multirange counterparts
var dates = JsonSerializer.Serialize(
    DateRange.CreateFinite(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)), options);
// "\"[2025-01-01,2025-12-31]\""
```

A null JSON token is rejected with `JsonException`; use the literal `"empty"` to represent an empty range.

## Interface Overview

The library exposes a structured set of interfaces for writing generic code:

| Interface                  | Purpose                                                            |
|----------------------------|--------------------------------------------------------------------|
| `IRange<T>`                | Base marker for all range types                                    |
| `IFiniteRange<T>`          | `Start`, `End`, and their inclusiveness flags                      |
| `IUnboundedStartRange<T>`  | `End` and `EndInclusive`                                           |
| `IUnboundedEndRange<T>`    | `Start` and `StartInclusive`                                       |
| `IEmptyRange<T>`           | Marker for the empty range; no bound properties                    |
| `IInfinityRange<T>`        | Marker for the range covering the entire domain                    |
| `IRangeFactory<TRange, T>` | Abstract static factories; also `NextValueAfter`/`PreviousValueBefore` for step-aware (discrete) types |

`T` is constrained to `struct, IComparable<T>, IEquatable<T>` throughout.

## Migration from v1.x

### `ToString()` now returns a PostgreSQL range literal

In v1.x, calling `.ToString()` on any range variant returned the default C# record representation:

```
Finite { Start = 1, End = 10, StartInclusive = True, EndInclusive = True }
```

From v2.0.0, `ToString()` returns the PostgreSQL range literal:

```
[1,10]
```

If your code depended on the old format for logging, display, serialization, or string comparison, update it to use the new literal format or, if you need the structural representation, reconstruct it from the variant's properties via pattern matching.

## Migration from v2.x

### State-check methods now require parentheses

`IsEmpty`, `IsFinite`, `IsInfinity`, `IsUnboundedStart`, and `IsUnboundedEnd` were extension properties in v2.x. In v3.0.0 they are extension methods — add parentheses at every call site:

```csharp
// v2.x
if (range.IsEmpty) { … }

// v3.0.0
if (range.IsEmpty()) { … }
```

The change is mechanical and the compiler will flag every affected site. The motivation is EF Core compatibility: extension properties cannot appear in LINQ expression trees, preventing SQL translation. As extension methods they are fully translated by the EF Core companion package — see the [EF Core section](#entity-framework-core-postgresql) below.

## Entity Framework Core (PostgreSQL)

The companion package **CodoMetis.ValueRanges.EFCore.PostgreSQL** maps every range type to its PostgreSQL range column and `RangeSet<TRange, T>` to the corresponding multirange column, bridging through `NpgsqlRange<T>` at the provider boundary — giving you identical semantics whether executing against an in-memory collection or a live PostgreSQL database.

```bash
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL
```

Enable it with one line — no value converters, comparers, or column types to configure:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());
```

Properties of the six range types and of `RangeSet<TRange, T>` are then mapped by convention:

| Property type                    | Column type      |
|----------------------------------|------------------|
| `Int32Range`                     | `int4range`      |
| `RangeSet<Int32Range, int>`      | `int4multirange` |
| `DateRange`                      | `daterange`      |
| `RangeSet<DateRange, DateOnly>`  | `datemultirange` |
| … and so on for all six types    |                  |

The full range algebra translates from LINQ to SQL:

```csharp
var day = new DateOnly(2024, 6, 15);

// b."Period" @> @day
bookings.Where(b => b.Period.Contains(day));

// b."Period" && b."Blocked", b."Period" << @other, b."Period" -|- @other, ...
bookings.Where(b => b.Period.Overlaps(other));

// b."Period" * @other                                   (intersection)
bookings.Select(b => b.Period.Intersect(other));

// datemultirange(b."Period") + datemultirange(@other)   (union -> multirange)
bookings.Select(b => b.Period.Union(other));

// b."BlockedDays" @> @day, multirange + - * operators, complement, ...
bookings.Where(b => b.BlockedDays.Contains(day));
bookings.Select(b => b.BlockedDays | b.Period);

// CASE WHEN b."From" <= b."To" THEN daterange(b."From", b."To", '[]') ELSE 'empty' END
bookings.Where(b => DateRange.CreateFinite(b.From, b.To).Contains(day));
```

`Contains`, `Overlaps`, `IsContainedBy`, `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf` and `IsAdjacentTo` map to `@>`, `&&`, `<@`, `<<`, `>>`, `&<`, `&>` and `-|-`. `Intersect` maps to `*`; `Union` and `Except` lift both operands to multiranges (`+`/`-`), matching their `RangeSet` return type — a disjoint union is a real two-element multirange, never an error. The `CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` factories translate to guarded range constructor calls with the model's inverted-bounds-yield-empty semantics.

Notes:

- Range state checks translate directly: `IsEmpty()` → `isempty`, `IsUnboundedStart()` → `lower_inf`, `IsUnboundedEnd()` → `upper_inf`, `IsInfinity()` → `lower_inf AND upper_inf`, `IsFinite()` → `NOT lower_inf AND NOT upper_inf AND NOT isempty`.
- `DateTimeRange` bounds are written as `timestamp` with `DateTimeKind.Unspecified`; `DateTimeOffsetRange` bounds are normalized to UTC for `timestamptz` (instants are preserved).

## License

MIT — see [LICENSE](LICENSE).

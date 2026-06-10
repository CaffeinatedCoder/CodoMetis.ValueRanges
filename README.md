# CodoMetis.ValueRanges

[![NuGet](https://img.shields.io/nuget/v/CodoMetis.ValueRanges)](https://www.nuget.org/packages/CodoMetis.ValueRanges)
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

`CreateFinite()` automatically returns an `Empty` when the arguments form a degenerate or inverted interval (e.g. `lowerBound > upperBound`, or equal bounds that are both exclusive).

**Default boundary inclusiveness:**

| Range type                                                 | `CreateFinite()` default          |
|------------------------------------------------------------|-----------------------------|
| `Int32Range`, `Int64Range`, `DateRange`                    | `[lower, upper]` — closed   |
| `DecimalRange`, `DateTimeRange`, `DateTimeOffsetRange`     | `[lower, upper)` — half-open |

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

// Set operations — single-range and bulk variants
set.Union(Int32Range.CreateFinite(11, 19));   // { [1, 30] } — bridges the gap
set.Intersect(Int32Range.CreateFinite(5, 25)); // { [5, 10], [20, 25] }
set.Except(Int32Range.CreateFinite(4, 6));     // { [1, 3], [7, 10], [20, 30] }

// Complement — every value not covered by the set
set.Complement();  // { (-∞, 0], [11, 19], [31, +∞) }
```

The set implements `IReadOnlyList<TRange>` (enumeration in lower-bound order, `Count`, indexer) and structural equality: two sets built from different inputs that normalize identically are equal.

```csharp
var a = IntSet.From([Int32Range.CreateFinite(1, 10)]);
var b = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(6, 10)]);
a.Equals(b);  // true — both normalize to { [1, 10] }
```

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

## Roadmap

A companion package is planned. It will bridge the range types in this library to `NpgsqlRange<T>` and register LINQ expression translators so that every range operation maps to its corresponding PostgreSQL range operator in EF Core queries — giving you identical semantics whether executing against an in-memory collection or a live PostgreSQL database.

## License

MIT — see [LICENSE](LICENSE).

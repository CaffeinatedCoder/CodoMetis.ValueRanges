# CodoMetis.ValueRanges

[![NuGet](https://img.shields.io/nuget/v/CodoMetis.ValueRanges)](https://www.nuget.org/packages/CodoMetis.ValueRanges)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

Fully functional, in-memory range types for .NET — complete interval algebra without any database dependency.

## Overview

`CodoMetis.ValueRanges` provides concrete, type-safe range types covering the same six value domains as PostgreSQL's built-in range types (`int4range`, `int8range`, `numrange`, `daterange`, `tsrange`, `tstzrange`), together with a full in-memory implementation of every range operation PostgreSQL exposes.

The library is designed to stand on its own: all operations execute in process, with no ORM or database driver required. A companion EF Core package is planned that will bridge these types to `NpgsqlRange<T>` for automatic LINQ-to-SQL translation, making the same code work both in memory and as PostgreSQL queries.

### Design

Each range type is modelled as a **discriminated union** of four sealed variants:

| Variant      | Represents                  | Interval notation |
|--------------|-----------------------------|-------------------|
| `Finite`     | Bounded on both sides       | `[1, 10]`         |
| `OpenStart`  | Unbounded on the left       | `(-∞, 10]`        |
| `OpenEnd`    | Unbounded on the right      | `[1, +∞)`         |
| `Empty`      | The empty range (no values) | `∅`               |

The *shape* of a range is encoded in its static type. An `OpenEnd` range has no `UpperBound` property — the property does not exist at compile time. An `Empty` range carries no bound information whatsoever. Invalid states are unrepresentable by construction, and pattern matching over a range is exhaustive with compiler-enforced coverage.

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
Int32Range closed = Int32Range.Closed(1, 10);                              // [1, 10]
Int32Range half   = Int32Range.Closed(1, 10, upperBoundInclusive: false);  // [1, 10)

// Unbounded on the left — upper bound exclusive by default
DateRange upToToday = DateRange.WithOpenStart(DateOnly.FromDateTime(DateTime.Today)); // (-∞, today)
// Inclusive variant:
DateRange throughToday = DateRange.WithOpenStart(DateOnly.FromDateTime(DateTime.Today), upperBoundInclusive: true);

// Unbounded on the right — lower bound inclusive by default
Int32Range fromFive = Int32Range.WithOpenEnd(5);  // [5, +∞)

// Explicitly empty
Int32Range empty = Int32Range.EmptyRange();
```

`Closed()` automatically returns an `Empty` when the arguments form a degenerate or inverted interval (e.g. `lowerBound > upperBound`, or equal bounds that are both exclusive).

**Default boundary inclusiveness:**

| Range type                                                 | `Closed()` default          |
|------------------------------------------------------------|-----------------------------|
| `Int32Range`, `Int64Range`, `DateRange`                    | `[lower, upper]` — closed   |
| `DecimalRange`, `DateTimeRange`, `DateTimeOffsetRange`     | `[lower, upper)` — half-open |

Discrete types default to fully closed intervals; continuous types default to the half-open convention that is conventional for monetary amounts and timestamps.

## Pattern Matching

The nested sealed records are first-class citizens and ideal for exhaustive pattern matching:

```csharp
string Describe(Int32Range range) => range switch
{
    Int32Range.Empty       => "empty",
    Int32Range.Finite f    => $"[{f.LowerBound}, {f.UpperBound}]",
    Int32Range.OpenStart s => $"(-∞, {s.UpperBound}]",
    Int32Range.OpenEnd e   => $"[{e.LowerBound}, +∞)",
};
```

The private constructor on the abstract base record prevents any subtypes being declared outside the assembly, so the compiler guarantees this switch is complete.

## Query Operations

All query methods are extension methods on `IRange<T>` and work across any combination of range shapes.

### Containment

```csharp
var sprint = DateRange.Closed(new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 17));

sprint.Contains(new DateOnly(2025, 1, 10));  // true  — point containment
sprint.Contains(new DateOnly(2025, 1, 20));  // false

var inner = DateRange.Closed(new DateOnly(2025, 1, 8), new DateOnly(2025, 1, 14));
sprint.Contains(inner);       // true  — range containment
inner.IsContainedBy(sprint);  // true  — symmetric alias
```

### Overlap

```csharp
var a = Int32Range.Closed(1, 5);
var b = Int32Range.Closed(5, 10);
var c = Int32Range.Closed(6, 10);

a.Overlaps(b);  // true  — they share the point 5
a.Overlaps(c);  // false
```

### Adjacency

Two ranges are adjacent when they are contiguous with no gap and no overlap — their union would form a single range.

```csharp
// Discrete: consecutive integer values are adjacent
var a = Int32Range.Closed(1, 5);
var b = Int32Range.Closed(6, 10);
a.IsAdjacentTo(b);  // true — GetNextValueFor(5) == 6

// Continuous: touching bounds with complementary inclusiveness
var x = DecimalRange.Closed(1m, 5m, upperBoundInclusive: true);    // [1, 5]
var y = DecimalRange.Closed(5m, 10m, lowerBoundInclusive: false);  // (5, 10)
x.IsAdjacentTo(y);  // true — one side claims 5, the other does not
```

### Directional Comparisons

```csharp
Int32Range.Closed(1, 3).IsStrictlyLeftOf(Int32Range.Closed(5, 9));  // true
Int32Range.Closed(1, 5).IsStrictlyLeftOf(Int32Range.Closed(5, 9));  // false — they share 5

Int32Range.Closed(7, 9).IsStrictlyRightOf(Int32Range.Closed(1, 5)); // true
```

**PostgreSQL `&<` / `&>` equivalents:**

```csharp
// Does not extend to the right of other  (&<)
Int32Range.Closed(1, 5).DoesNotExtendRightOf(Int32Range.Closed(1, 10));  // true

// Does not extend to the left of other  (&>)
Int32Range.Closed(3, 10).DoesNotExtendLeftOf(Int32Range.Closed(1, 10));  // true
```

## Set Operations

Set operations are extension methods on the concrete range types (any type that implements `IRangeFactory<TRange, T>`). They return `null` when the result cannot be represented as a single contiguous range.

### Intersection

Returns the largest range contained by both operands.

```csharp
var a = Int32Range.Closed(1, 10);
var b = Int32Range.Closed(5, 15);

Int32Range? intersection = a.Intersect(b);              // [5, 10]
a.Intersect(Int32Range.Closed(11, 20));                  // null — no overlap
```

All shape combinations are handled: `Finite ∩ OpenStart`, `OpenEnd ∩ OpenStart`, `OpenStart ∩ OpenEnd`, and so on, each producing the correctly shaped result type.

### Merge / Union

Returns the smallest range that spans both operands. Returns `null` when the ranges are neither overlapping nor adjacent.

```csharp
var a = Int32Range.Closed(1, 5);
var b = Int32Range.Closed(5, 10);
var c = Int32Range.Closed(7, 10);

a.Merge(b);  // [1, 10]  — overlapping, forms one range
a.Union(b);  // [1, 10]  — Union is an alias for Merge
a.Merge(c);  // null     — there is a gap between [1, 5] and [7, 10]
```

Merging an `OpenEnd` with a `Finite` yields an `OpenEnd`; merging an `OpenStart` with an `OpenEnd` covers the entire domain and cannot be expressed as a single range type — this case returns `null`.

### Except (Set Difference)

Removes the overlap of `other` from the receiver.

```csharp
var range  = Int32Range.Closed(1, 10);
var remove = Int32Range.Closed(4, 6);

// [4, 6] is interior to [1, 10] — the result is split in two
(Int32Range Left, Int32Range? Right)? result = range.Except(remove);
// Left  = [1, 4)
// Right = (6, 10]
```

| Return value        | Meaning                                                              |
|---------------------|----------------------------------------------------------------------|
| `null`              | The receiver is fully contained by `other`; nothing remains         |
| `(remainder, null)` | One-sided trim or no overlap; `Left` holds the remaining range      |
| `(left, right)`     | `other` was strictly interior to the receiver; it is split in two   |

Boundary inclusiveness is inverted at the cut point so that no value is lost or double-counted across the resulting pieces.

## Interface Overview

The library exposes a structured set of interfaces for writing generic code:

| Interface                  | Purpose                                                        |
|----------------------------|----------------------------------------------------------------|
| `IRange<T>`                | Base marker for all range types                                |
| `IFiniteRange<T>`          | `LowerBound`, `UpperBound`, and their inclusiveness flags      |
| `IOpenStartRange<T>`       | `UpperBound` and `UpperBoundInclusive`                         |
| `IOpenEndRange<T>`         | `LowerBound` and `LowerBoundInclusive`                         |
| `IEmptyRange<T>`           | Marker for the empty range; no bound properties                |
| `IDiscreteRange<T>`        | Exposes `GetNextValueFor(T)` for step-aware adjacency logic    |
| `IRangeFactory<TRange, T>` | Abstract static factory methods; required for set operations   |

The read-side interfaces use a covariant `out T` parameter, enabling safe use in generic abstractions. `T` is constrained to `struct, IComparable<T>, IEquatable<T>` throughout.

## Roadmap

A companion package is planned. It will bridge the range types in this library to `NpgsqlRange<T>` and register LINQ expression translators so that every range operation maps to its corresponding PostgreSQL range operator in EF Core queries — giving you identical semantics whether executing against an in-memory collection or a live PostgreSQL database.

## License

MIT — see [LICENSE](LICENSE).

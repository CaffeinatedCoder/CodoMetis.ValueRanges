# CodoMetis.ValueRanges

[![NuGet](https://img.shields.io/nuget/v/CodoMetis.ValueRanges)](https://www.nuget.org/packages/CodoMetis.ValueRanges)
[![Build & Tests](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/actions/workflows/dotnet.yml/badge.svg)](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/actions/workflows/dotnet.yml)
[![Context7](https://img.shields.io/badge/Context7-Indexed-3B82F6)](https://context7.com/caffeinatedcoder/codometis.valueranges)
[![dev.to](https://img.shields.io/badge/dev.to-Article-3B82F6)](https://dev.to/caffeinatedcoder/the-interval-is-the-thing-modelling-range-types-as-first-class-domain-objects-in-net-3jha)
[![hashnode](https://img.shields.io/badge/hashnode.dev-Article-3B82F6)](https://codometis.hashnode.dev/stop-modeling-time-with-two-columns-codometis-valueranges-brings-interval-logic-to-your-net-domain?utm_source=hashnode&utm_medium=feed)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

Fully functional, in-memory range and set types for .NET — complete interval and membership algebra without any database dependency.

## Overview

`CodoMetis.ValueRanges` provides immutable, canonical-at-construction value domain types with PostgreSQL-native storage shapes: concrete, type-safe **range types** covering the same six value domains as PostgreSQL's built-in range types (`int4range`, `int8range`, `numrange`, `daterange`, `tsrange`, `tstzrange`) with a full in-memory implementation of every range operation PostgreSQL exposes, their **multirange** counterpart `RangeSet<TRange, T>`, and — since v6 — **[value sets](#value-sets)**, canonical sets of scalar values whose storage shape is a native PostgreSQL array.

The library is designed to stand on its own: all operations execute in process, with no ORM or database driver required. A companion EF Core package ([CodoMetis.ValueRanges.EFCore.PostgreSQL](#entity-framework-core-postgresql)) bridges the range types to `NpgsqlRange<T>` and the set types to native arrays for automatic LINQ-to-SQL translation, making the same code work both in memory and as PostgreSQL queries.

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

### Unboundedness is a shape, not a bound value

Encoding the shape in the type also keeps *"there is no upper bound"* apart from *"the upper bound is the largest representable value"* — two different facts that a bounds-plus-flags representation stores in the same object.

In a representation built from two nullable bounds plus an `IsUpperInfinite` bit, the two facts occupy the same fields and have to be reconciled at runtime. `NpgsqlRange<T>` reconciles them by discarding: pass an upper bound together with `upperBoundInfinite: true` and the constructor keeps the flag and silently drops the value. That is a sound invariant, but it is enforced by a constructor rather than by the type, and it leaves `LowerBound`/`UpperBound` typed `T?` on *every* instance — so even code that has already established the range is bounded still has a nullable to answer for.

Here the question cannot be asked in the first place. `UnboundedEnd` has no `End` property to put a sentinel in; `Finite` has no flag to disown its `End`, and its `Start`/`End` are not nullable. The distinction is carried by the type rather than by a constructor rule that callers have to know about:

```csharp
DateTimeRange.CreateUnboundedEnd(start)                  // UnboundedEnd — genuinely open-ended
DateTimeRange.CreateFinite(start, DateTime.MaxValue)     // Finite — ends at a specific instant
```

The two are not interchangeable, and the compiler will not let them be confused. This matters at the database boundary as well, where Npgsql maps `DateTime.MaxValue` to PostgreSQL `infinity` — a *finite bound that happens to be infinite*, which is still distinct from an unbounded side. See [Entity Framework Core](#entity-framework-core-postgresql) for how that round-trips.

## What's new in v6.0

**Value sets — a second type family.** The package's model was never "ranges" narrowly; it is *immutable, canonical-at-construction value domains with PostgreSQL-native storage shapes*. `RangeSet` has embodied "canonical set with a native store shape" (multirange) since v2; v6 applies the same concept one level down: **canonical sets of scalar values, stored as native PostgreSQL arrays** (`text[]`, `uuid[]`, `integer[]`, …) — deduplicated, sorted, structurally equal, with the membership algebra PostgreSQL's own array operators speak.

- **Ten closed types** in the core package — `StringSet`, `GuidSet`, `Int16Set`, `Int32Set`, `Int64Set`, `DecimalSet`, `DateSet`, `TimeSet`, `DateTimeSet`, `DateTimeOffsetSet` — plus **validated-wrapper arities** `StringSet<T>`, `GuidSet<T>`, `Int32Set<T>`, `Int64Set<T>` for generator-produced domain values (Vogen, Metalama aspects, StronglyTypedId, hand-written wrappers), constrained only on BCL interfaces so domain types never reference this package. See [Value Sets](#value-sets).
- **Five NodaTime types** in the satellite: `LocalDateSet`, `LocalDateTimeSet`, `InstantSet`, `LocalTimeSet`, and the month-granularity `YearMonthSet` (stored as a month-aligned `date[]`, like `YearMonthRange`'s `daterange`).
- **Membership algebra** — `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, `Union`, `Count`, `IsEmpty` (plus client-side `Intersect`/`Except`/`Add`/`Remove`) — PostgreSQL array literals (`{a,b}`), JSON support through the existing converter factory, and collection expressions (`StringSet tags = ["a", "b"];`).
- The **EF Core packages map them by convention** to native array columns — no configuration, no registration, wrapper instantiations recognized automatically — with LINQ translation to the array operator algebra (`@>`, `&&`, `<@`, `cardinality`, `array_cat`). Containment always translates as `@>`, so a plain GIN index serves it. See [Value set columns](#value-set-columns).

v6.0 contains **no breaking changes**; the major marks the package growing a second type family.

## What's new in v5.0

**Two new range domains** — the first additions beyond PostgreSQL's six built-ins, chosen because their element types clear the same bar (a total order the type's own comparisons agree with, and a defined step where adjacency needs one):

- **`TimeRange`** (core package) — a time-of-day range over `TimeOnly`, the equivalent of the most common custom range type in PostgreSQL practice: `CREATE TYPE timerange AS RANGE (subtype = time)`. Continuous, half-open by default, so `[09:00, 12:00)` and `[12:00, 17:00)` compose the way opening hours and shifts do. A window that crosses midnight is two ranges — which `RangeSet` represents naturally. See [TimeRange and the custom timerange type](#timerange-and-the-custom-timerange-type) for the EF Core mapping.
- **`YearMonthRange`** (NodaTime satellite) — a month-granularity range over NodaTime's `YearMonth` for billing and reporting periods. Discrete with a one-month step: `[2024-01, 2024-03]` and `[2024-04, 2024-06]` are adjacent and merge. The EF Core NodaTime satellite stores it as a **month-aligned `daterange`** — no custom database type needed, and every operator works server-side. Conversions to and from `LocalDateRange` and `DateInterval` are included.

Both types carry the complete algebra, multiranges, literals, JSON support and aggregate overloads of the existing eight. The EF Core packages map them by convention; `timerange` needs two one-line opt-ins on the database side ([documented below](#timerange-and-the-custom-timerange-type)).

v5.0 contains **no breaking changes** to existing APIs; the major bump marks the model growing beyond the PostgreSQL built-ins.

## What's new in v4.1

**NodaTime satellites** — two new packages bring the range model to NodaTime-based projects:

- **[CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime)** — `LocalDateRange` (`daterange`), `LocalDateTimeRange` (`tsrange`) and `InstantRange` (`tstzrange`) with the complete algebra, multiranges, literals and JSON support, plus conversions to and from NodaTime's own `Interval` and `DateInterval`. See [NodaTime](#nodatime).
- **[CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime)** — maps them to PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`: `options.UseNpgsql(..., npgsql => npgsql.UseValueRangesNodaTime())`.

The core and base EF packages are unchanged apart from the EF plugin's internal type registry becoming extensible for satellites. No source changes are required.

## What's new in v4.0

**PostgreSQL feature-matrix completion** — every remaining range/multirange operator and function now has an in-memory implementation and a LINQ-to-SQL translation:

- **Bound accessors** — `LowerBound()` / `UpperBound()` return `T?` (`null` when unbounded or empty, matching PostgreSQL `lower`/`upper` `NULL` semantics), and `LowerBoundInclusive()` / `UpperBoundInclusive()` mirror `lower_inc`/`upper_inc` — on ranges and on `RangeSet`. Sorting by range start finally works straight from LINQ: `query.OrderBy(b => b.Period.LowerBound())` → `ORDER BY lower("Period")`. See [Bound Accessors](#bound-accessors).
- **`Merge`** — the smallest single range spanning both operands *including any gap* (PostgreSQL `range_merge`), on ranges and as `RangeSet.Merge()`. See [Merge (Convex Hull)](#merge-convex-hull).
- **Aggregates** — `RangeAgg()` and `RangeIntersectAgg()` over sequences of ranges (`range_agg`, `range_intersect_agg`), translated inside `GroupBy` projections. See [Aggregates](#aggregates).
- **Multirange operator parity** — `RangeSet` gains `Contains(RangeSet)`, `Overlaps(RangeSet)`, `IsAdjacentTo`, `IsStrictlyLeftOf`/`RightOf` and `DoesNotExtendLeftOf`/`RightOf` (range and set operands), plus the state checks `IsEmpty()`, `IsUnboundedStart()`, `IsUnboundedEnd()` — each translating to its multirange operator or function.
- **`==` / `!=` on `RangeSet`** — structural equality operators, so in-memory comparisons agree with the SQL `=` the EF Core provider generates. **Behavioral change:** `==` on sets was previously reference equality; recompiling against v4 switches those call sites to value equality. This is the change that makes v4 a major version.
- **Full `&<` / `&>` parity** — `DoesNotExtendRightOf`/`DoesNotExtendLeftOf` now treat an infinite bound as comparing equal to another infinite bound (`+∞ ≤ +∞`, `-∞ ≥ -∞`), exactly like PostgreSQL. **Behavioral change:** an unbounded receiver previously always returned `false`, even against an operand unbounded on the same side.
- **Bug fix** — `RangeSet.Infinite.Contains(range)` and `RangeSet.Infinite.Overlaps(range)` threw `InvalidOperationException` for operands with a finite bound; they now return the expected result.
- **Live-PostgreSQL integration suite** — a Testcontainers-based test project executes the translated SQL against real PostgreSQL and asserts agreement with the in-memory results: round-trips for all six range and both multirange column types, the timestamp normalization rules, and the v4 operations end-to-end.

## What's new in v3.1

**Performance** — `RangeSet<TRange, T>` now exploits its sorted, disjoint, non-adjacent invariant for sub-linear queries and merge-join set operations. No public API or results changed — only the time complexity:

| Operation | Before | After |
|---|---|---|
| `Contains(T)`, `Contains(IRange<T>)`, `Overlaps(IRange<T>)` | O(n) linear scan | O(log n) binary search on lower bounds |
| `Union(RangeSet, RangeSet)` | re-sort of concatenation | O(n + m) merge of two pre-sorted streams |
| `Intersect(RangeSet, RangeSet)` | O(n · m) nested loop | O(n + m) two-pointer merge-join |
| `Except(RangeSet, RangeSet)` | per-element re-normalization | O(n + m) two-pointer walk |
| `Except` from `Infinite` | O(\|other\|²) | O(\|other\|) single-pass complement walk |
| `From` single-element input | list + sort + merge | zero-allocation fast path |

**New API** — `RangeSet<TRange, T>.LowerBoundComparer` exposes the set's internal lower-bound ordering as a public `IComparer<TRange>` singleton, for sorting arbitrary `List<TRange>`s the same way the set does. Also available as `RangeLowerBoundComparer<TRange, T>.Instance`. See [RangeSet — Sorting ranges externally](#sorting-ranges-externally).

**Bug fix** — Quoted range bounds now unescape PostgreSQL `\"` → `"` and `\\` → `\` on parse, so element types whose stringification can contain quotes or backslashes round-trip correctly. See [Parsing — Quoted bounds](#quoted-bounds).

## Supported Types

| .NET type              | PostgreSQL equivalent | Element type     | Discrete |
|------------------------|-----------------------|------------------|----------|
| `Int32Range`           | `int4range`           | `int`            | ✓        |
| `Int64Range`           | `int8range`           | `long`           | ✓        |
| `DecimalRange`         | `numrange`            | `decimal`        | —        |
| `DateRange`            | `daterange`           | `DateOnly`       | ✓        |
| `DateTimeRange`        | `tsrange`             | `DateTime`       | —        |
| `DateTimeOffsetRange`  | `tstzrange`           | `DateTimeOffset` | —        |
| `TimeRange`            | `timerange` (custom)  | `TimeOnly`       | —        |

Discrete types (`int`, `long`, `DateOnly`) know their step size. This matters for adjacency checks: `[1, 5]` and `[6, 10]` are adjacent for integers because there is no integer between 5 and 6.

`TimeRange` is a time-of-day range — opening hours, shifts, booking slots. A single range cannot cross midnight; a 22:00–06:00 window is two ranges, which is exactly what a two-element `RangeSet` (and its PostgreSQL multirange counterpart) represents. PostgreSQL has no built-in `timerange`, so the EF Core companion maps it to the custom type users conventionally create for this — see [TimeRange and the custom timerange type](#timerange-and-the-custom-timerange-type).

### Why these element types

The list is deliberately vetted. Interval algebra needs a total order that the type's own comparisons agree with, and — for adjacency — a defined step between neighbouring values. The first six domains have both and are the six PostgreSQL ships as built-ins; `TimeOnly` (and, in the NodaTime satellite, `YearMonth`) clear the same bar and joined in v5.

`double` and `float` have neither, and fail *quietly*. `double.CompareTo` reports `NaN` as less than every value and equal to itself, which is a total order; the IEEE operators disagree, since `NaN < 5.0`, `NaN > 5.0` and `NaN == NaN` are all `false`. A range library generic over `IComparable<T>` therefore accepts `double` without complaint and answers containment against a `NaN` bound with a straight face. There is no exception to catch and no bound to reject at construction — the result is simply wrong. Restricting `T` to a vetted set is what makes the algebra sound, not a limitation left in for later.

`Guid` is absent for a different reason: v7 values are ordered, so the algebra would be well-defined, but "every GUID between these two" is not a question with a domain meaning.

### NodaTime

For projects that build on NodaTime's primitives instead of the BCL date/time types, the satellite package **[CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime)** provides the three NodaTime types that clear the same bar — a total order the type's own comparisons agree with, mapping onto a PostgreSQL built-in:

| .NET type              | PostgreSQL equivalent      | Element type     | Discrete |
|------------------------|----------------------------|------------------|----------|
| `LocalDateRange`       | `daterange`                | `LocalDate`      | ✓        |
| `LocalDateTimeRange`   | `tsrange`                  | `LocalDateTime`  | —        |
| `InstantRange`         | `tstzrange`                | `Instant`        | —        |
| `YearMonthRange`       | `daterange` (month-aligned) | `YearMonth`     | ✓        |

`YearMonthRange` (v5) is a month-granularity range for billing and reporting periods — discrete with a one-month step, so `[2024-01, 2024-03]` and `[2024-04, 2024-06]` are adjacent. It converts losslessly to the `LocalDateRange` covering exactly its months (`ToLocalDateRange()` / `ToYearMonthRange()`), which is also how the EF Core satellite stores it: as a month-aligned `daterange`, with every operator working server-side and reads validating alignment rather than silently shifting boundaries.

The same algebra, literals, JSON support and `RangeSet` multiranges apply unchanged. Notably, the two timestamp caveats documented [below](#entity-framework-core-postgresql) do not arise there: `LocalDateTime` is wall-clock time by construction and `Instant` is an instant by construction, so there is no `Kind` to reinterpret and no offset to normalize away. `ZonedDateTime` and `OffsetDateTime` are excluded by the same reasoning as `double` — NodaTime deliberately gives them no default ordering (instant order and local order disagree), so the `IComparable<T>` constraint rejects them at compile time. See the satellite's README for the full rationale and the `Interval`/`DateInterval` interop.

A companion EF Core package, **[CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime)**, maps them to the same PostgreSQL columns through `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
// implies UseNodaTime() and UseValueRanges() — BCL and NodaTime ranges coexist in one model
```

One point where the model is *stricter* than the database it mirrors: PostgreSQL's `numeric` has a `NaN` value (sorted above all others by fiat), so a `numrange` bound can be `NaN`. .NET's `decimal` has no such value, so `DecimalRange` cannot form one — the case that `numrange` has to define away does not arise.

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

### Bound Accessors

The PostgreSQL `lower` / `upper` / `lower_inc` / `upper_inc` functions, on any range shape. The variants expose `Start`/`End` only where they exist structurally; the accessors provide the dynamic view: `T?` with `null` for a missing bound — exactly PostgreSQL's `NULL` semantics.

```csharp
Int32Range.CreateFinite(1, 10).LowerBound();          // 1
Int32Range.CreateFinite(1, 10).UpperBoundInclusive(); // true

Int32Range.CreateUnboundedStart(5, true).LowerBound(); // null — no lower bound
Int32Range.Empty.UpperBound();                         // null
Int32Range.Infinite.LowerBoundInclusive();             // false

// On RangeSet: the first element's lower bound, the last element's upper bound.
var set = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);
set.LowerBound();  // 1
set.UpperBound();  // 9
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

### Merge (Convex Hull)

Returns the smallest single range containing both operands — PostgreSQL's `range_merge`. Unlike `Union`, the result also covers any gap between disjoint operands.

```csharp
var a = Int32Range.CreateFinite(1, 3);
var b = Int32Range.CreateFinite(10, 12);

a.Union(b);  // { [1, 3], [10, 12] } — two elements, the gap stays open
a.Merge(b);  // [1, 12]              — one range, the gap is covered

// Empty operands are ignored; unbounded edges span accordingly:
Int32Range.CreateUnboundedStart(3, true).Merge(Int32Range.CreateUnboundedEnd(10)); // (-∞, +∞)

// RangeSet.Merge() spans the whole set:
RangeSet<Int32Range, int>.From([a, b]).Merge(); // [1, 12]
```

### Aggregates

`RangeAgg()` and `RangeIntersectAgg()` aggregate a sequence of ranges — the in-memory counterparts of PostgreSQL's `range_agg` and `range_intersect_agg`:

```csharp
new[] { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(3, 8), Int32Range.CreateFinite(20, 25) }
    .RangeAgg();           // { [1, 8], [20, 25] } — a normalized RangeSet

new[] { Int32Range.CreateFinite(1, 10), Int32Range.CreateFinite(5, 15) }
    .RangeIntersectAgg();  // [5, 10] — the common intersection; null for an empty source
```

In EF Core queries they translate to the SQL aggregates inside `GroupBy` projections — see [Entity Framework Core](#entity-framework-core-postgresql).

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

// State checks — isempty / lower_inf / upper_inf equivalents
set.IsEmpty();           // false
set.IsUnboundedStart();  // false
set.IsUnboundedEnd();    // false

// Set-operand comparisons — the full multirange operator matrix
set.Contains(IntSet.From([Int32Range.CreateFinite(2, 8)]));   // true   (@>)
set.Overlaps(IntSet.From([Int32Range.CreateFinite(25, 40)])); // true   (&&)
set.IsStrictlyLeftOf(Int32Range.CreateFinite(40, 50));        // true   (<<)
set.DoesNotExtendRightOf(Int32Range.CreateFinite(1, 30));     // true   (&<)
set.IsAdjacentTo(Int32Range.CreateFinite(31, 40));            // true   (-|-)
```

**Adjacency mirrors PostgreSQL exactly:** it is *directional through the outer edges* — the operand must end exactly where the set's first element begins, or begin exactly where the set's last element ends. Touching any interior boundary, even the inner side of the first or last element, does not count ([verified against live PostgreSQL](#verified-against-postgresql)):

```csharp
var three = IntSet.From([
    Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9), Int32Range.CreateFinite(20, 22)
]);
three.IsAdjacentTo(Int32Range.CreateFinite(23, 25)); // true  — attaches after the last element
three.IsAdjacentTo(Int32Range.CreateFinite(4, 6));   // false — inner side of the first element
three.IsAdjacentTo(Int32Range.CreateFinite(10, 12)); // false — touches only the interior [7, 9]
```

The positional operators (`<<`, `>>`, `&<`, `&>`) likewise compare the first/last element's bounds.

The set implements `IReadOnlyList<TRange>` (enumeration in lower-bound order, `Count`, indexer) and structural equality, including `==`/`!=`: two sets built from different inputs that normalize identically are equal.

```csharp
var a = IntSet.From([Int32Range.CreateFinite(1, 10)]);
var b = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(6, 10)]);
a.Equals(b);  // true — both normalize to { [1, 10] }
a == b;       // true — same value semantics as the ranges themselves
```

### Sorting ranges externally

The set's internal lower-bound ordering is exposed as `RangeSet<TRange, T>.LowerBoundComparer` — an `IComparer<TRange>` singleton for sorting arbitrary `List<TRange>`s the same way the set does, for example to pre-sort inputs before handing them to `From`. `IUnboundedStartRange<T>` sorts first (its lower bound is -∞); at the same finite value, an inclusive lower bound sorts before an exclusive one (`[5, …` before `(5, …`).

```csharp
var unsorted = new List<Int32Range>
{
    Int32Range.CreateFinite(20, 30),
    Int32Range.CreateFinite(1, 5),
    Int32Range.CreateUnboundedStart(10, true)
};

unsorted.Sort(RangeSet<Int32Range, int>.LowerBoundComparer);
// { (-∞, 10], [1, 5], [20, 30] }
```

The same instance is available as `RangeLowerBoundComparer<Int32Range, int>.Instance` for contexts where you only have the comparer type and not the set type.

## Value Sets

A value set is an immutable, **canonical** set of scalar values: deduplicated, sorted, never containing null, with structural equality. It relates to a PostgreSQL array column exactly as `RangeSet<DateRange, DateOnly>` relates to `datemultirange` — the CLR type models the domain concept (a set), the column is its storage encoding (an array):

| CLR type | Canonical set of… | PostgreSQL shape |
|---|---|---|
| `RangeSet<DateRange, DateOnly>` | ranges | `datemultirange` |
| `StringSet` | values | `text[]` |

| .NET type | Element type | PostgreSQL column | Wrapper arity |
|---|---|---|---|
| `StringSet` | `string` | `text[]` | `StringSet<TElement>` |
| `GuidSet` | `Guid` | `uuid[]` | `GuidSet<TElement>` |
| `Int16Set` | `short` | `smallint[]` | — |
| `Int32Set` | `int` | `integer[]` | `Int32Set<TElement>` |
| `Int64Set` | `long` | `bigint[]` | `Int64Set<TElement>` |
| `DecimalSet` | `decimal` | `numeric[]` | — |
| `DateSet` | `DateOnly` | `date[]` | — |
| `TimeSet` | `TimeOnly` | `time[]` | — |
| `DateTimeSet` | `DateTime` | `timestamp[]` | — |
| `DateTimeOffsetSet` | `DateTimeOffset` | `timestamptz[]` | — |

The NodaTime satellite adds `LocalDateSet` (`date[]`), `LocalDateTimeSet` (`timestamp[]`), `InstantSet` (`timestamptz[]`), `LocalTimeSet` (`time[]` — a built-in array type, so unlike `timerange` no `CREATE TYPE` is needed) and `YearMonthSet` (month-aligned `date[]`). `LocalDate`/`LocalDateTime` elements normalize to the ISO calendar at construction; `YearMonth` elements must already be ISO, mirroring the range types.

```csharp
var tags = StringSet.From("beta", "alpha", "beta");   // {alpha,beta} — deduplicated, sorted
StringSet more = ["gamma", "alpha"];                  // collection expressions work

tags.Contains("alpha");      // true
tags.Overlaps(more);         // true  — shares "alpha"
tags.IsSubsetOf(more);       // false
tags.Union(more);            // {alpha,beta,gamma}
tags.Count;                  // 2
tags.IsEmpty;                // false
```

`Intersect`, `Except`, `Add` and `Remove` are also available; they evaluate client-side only (PostgreSQL has no native array intersection/difference operator), and operations that change nothing return the same instance.

### Canonical form is the contract

Every construction path deduplicates and sorts — `From`, parsing, JSON, and materialization from the database. This is load-bearing twice: the EF `ValueComparer` collapses to a cheap equality with no false diffs in change detection, and SQL `=` on the stored array coincides with set equality.

The order rules are deliberate:

- **String-backed sets sort ordinal** — never a culture-sensitive comparison. Canonical form is a cross-writer storage contract, not a display order; a culture sort would make two machines disagree about the same set.
- **Everything else sorts by the element's own comparison** (numeric, chronological, `Guid.CompareTo`).

PostgreSQL itself motivates the design: its array *query* algebra is already set-semantic — `@>`, `<@` and `&&` ignore both order and duplicates (`ARRAY[1,1] <@ ARRAY[1]` is true) — while only `=` compares arrays as sequences. Canonical form closes that split, the same way PostgreSQL itself canonicalizes discrete ranges and multiranges. Arrays that need to be *lists* (ordered, duplicates preserved) are a different concept — and one Npgsql already maps natively as `T[]`/`List<T>`.

### Validated wrapper elements

The wrapper arities carry domain values — typed keys, strongly typed IDs — without the domain type referencing this package. `TElement` is constrained only on BCL interfaces, which validated-value generators emit out of the box:

```csharp
// A generator-shaped wrapper: struct, IEquatable (record), IFormattable, IParsable.
public readonly record struct AccessRight : IFormattable, IParsable<AccessRight>
{
    private readonly string _value;
    private AccessRight(string value) => _value = value;

    public static AccessRight Parse(string s, IFormatProvider? provider)
        => Validate(s) ? new(s.Trim().ToLowerInvariant()) : throw new FormatException(…);
    public string ToString(string? format, IFormatProvider? formatProvider) => _value;
    // TryParse elided
}

StringSet<AccessRight> rights = [AccessRight.Parse("users.read", null)];
```

`IFormattable` supplies the element's backing text on the way out; `IParsable<TSelf>` **re-runs the element's validation on the way in**, so materializing corrupt data throws instead of smuggling invalid values into the domain. `GuidSet<T>`, `Int32Set<T>` and `Int64Set<T>` additionally require `IComparable<TElement>` (canonical order delegates to the backing primitive). One contract cannot be expressed in constraints and is convention instead: *the element's invariant text form must be exactly the backing primitive's text form* — a decorative format (`"CUST-{value}"`) fails loudly at the persistence boundary with an error naming the contract.

String-backed wrappers sort ordinal over their text form — deliberately not the element's own `IComparable`, whose generated implementations typically delegate to culture-sensitive string comparison.

### Why these element types

The same vetting as [for ranges](#why-these-element-types) applies, with one notable difference: `Guid` is absent from ranges ("every GUID between these two" has no domain meaning) but present in sets — *membership* is exactly the question ID collections ask. Excluded, deliberately: `bool` (a set over a two-value domain), `float`/`double` (NaN breaks total order and equality — the same quiet failure as for ranges), `byte[]` (nested variable-length elements have no cheap canonical order), and `TimeSpan` (PostgreSQL `interval` is a months/days/microseconds triple that `TimeSpan` cannot represent losslessly).

### Literals, parsing, JSON

`ToString()` produces the PostgreSQL array literal, with the same quoting rules the server uses; `Parse`/`TryParse` accept it back, normalizing to canonical form:

```csharp
StringSet.From("a b", "plain").ToString();   // {"a b",plain}
Int32Set.Parse("{2,1,2}", null);             // {1,2} — normalizes on parse
StringSet.Parse("{a,NULL}", null);           // FormatException — sets never contain null
```

JSON serialization goes through the same converter factory as the ranges (`options.AddRangeConverters()`) and produces plain JSON arrays (`["alpha","beta"]`), delegating element serialization to System.Text.Json — element converters (including NodaTime's `ConfigureForNodaTime`) apply. Reads normalize and reject null elements.

### When not to use a set

A value set is for *value catalogs*: tags, codes, keys, dates — elements that are data, not entity references. If the elements are rows in another table and you need referential integrity, PostgreSQL cannot put a foreign key on array elements (a long-standing limitation); use a junction table. If you need order-as-data or duplicates, you want a list, which Npgsql's native `T[]`/`List<T>` mapping already serves.

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

### Quoted bounds

PostgreSQL allows quoting individual bounds to embed commas, brackets, or other characters that would otherwise confuse the parser:

```csharp
Int32Range.Parse("[\"1\",\"10\"]", null);   // [1, 10]
```

Inside quotes, `\"` is unescaped to `"` and `\\` to `\`, matching PostgreSQL's quoted-bound syntax. The no-quote fast path stays allocation-free; unescaping only runs when a backslash is actually present inside the quotes.

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

For sorting ranges externally, `RangeLowerBoundComparer<TRange, T>` (an `IComparer<TRange>` singleton) exposes the same lower-bound ordering the set uses internally. See [Sorting ranges externally](#sorting-ranges-externally).

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

## Migration from v3.x

### `RangeSet` `==` is now structural

`RangeSet<TRange, T>` defines `operator ==`/`!=` as value equality, delegating to `Equals` — consistent with the range types themselves (records) and with the SQL `=` the EF Core provider generates. Code that compared sets with `==` previously got reference equality; recompiling against v4 silently changes those call sites to value comparison. If you relied on reference identity, switch to `ReferenceEquals(a, b)`.

### `DoesNotExtendRightOf`/`LeftOf` now match PostgreSQL for infinite bounds

An unbounded receiver previously always returned `false`. In v4, an infinite bound compares equal to another infinite bound — `[5, +∞).DoesNotExtendRightOf([100, +∞))` is now `true` (`+∞ ≤ +∞`), matching the `&<`/`&>` operators exactly. Results against finite-bounded or empty operands are unchanged.

Everything else in v4 is additive — no other source changes are required.

## Entity Framework Core (PostgreSQL)

The companion package **CodoMetis.ValueRanges.EFCore.PostgreSQL** maps every range type to its PostgreSQL range column and `RangeSet<TRange, T>` to the corresponding multirange column, bridging through `NpgsqlRange<T>` at the provider boundary — giving you identical semantics whether executing against an in-memory collection or a live PostgreSQL database.

```bash
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL
```

Enable it with one line — no value converters, comparers, or column types to configure:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());
```

Properties of the range types and of `RangeSet<TRange, T>` are then mapped by convention:

| Property type                    | Column type      |
|----------------------------------|------------------|
| `Int32Range`                     | `int4range`      |
| `RangeSet<Int32Range, int>`      | `int4multirange` |
| `DateRange`                      | `daterange`      |
| `RangeSet<DateRange, DateOnly>`  | `datemultirange` |
| `TimeRange`                      | `timerange` ([custom type](#timerange-and-the-custom-timerange-type)) |
| … and so on for all types        |                  |

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

`Contains`, `Overlaps`, `IsContainedBy`, `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf` and `IsAdjacentTo` map to `@>`, `&&`, `<@`, `<<`, `>>`, `&<`, `&>` and `-|-` — on ranges and, since v4, on `RangeSet` with range or multirange operands. `Intersect` maps to `*`; `Union` and `Except` lift both operands to multiranges (`+`/`-`), matching their `RangeSet` return type — a disjoint union is a real two-element multirange, never an error. The `CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` factories translate to guarded range constructor calls with the model's inverted-bounds-yield-empty semantics.

New in v4:

```csharp
// ORDER BY lower(b."Period") — bound accessors: lower / upper / lower_inc / upper_inc
bookings.OrderBy(b => b.Period.LowerBound());

// range_merge(b."Period", @other) and range_merge(b."BlockedDays")
bookings.Select(b => b.Period.Merge(other));
bookings.Select(b => b.BlockedDays.Merge());

// range_agg(b."Period") / range_intersect_agg(b."Period") per group
bookings.GroupBy(b => b.CustomerId)
        .Select(g => g.Select(b => b.Period).RangeAgg());

// isempty / lower_inf / upper_inf on multirange columns
bookings.Where(b => !b.BlockedDays.IsEmpty());

// Value equality on multirange columns — b."BlockedDays" = @set
bookings.Where(b => b.BlockedDays == someSet);
```

Notes:

- Range state checks translate directly: `IsEmpty()` → `isempty`, `IsUnboundedStart()` → `lower_inf`, `IsUnboundedEnd()` → `upper_inf`, `IsInfinity()` → `lower_inf AND upper_inf`, `IsFinite()` → `NOT lower_inf AND NOT upper_inf AND NOT isempty`. The same state checks exist on `RangeSet` and translate to the multirange functions.
- `LowerBound()`/`UpperBound()` return `T?` because PostgreSQL's `lower`/`upper` return `NULL` for an unbounded or empty operand — the in-memory implementation matches.
- For the discrete types (`int4range`, `int8range`, `daterange`), PostgreSQL canonicalizes to half-open `[lower, upper)` while the model canonicalizes to closed `[lower, upper]`. `UpperBound()` therefore translates to `upper(x) - 1` and `UpperBoundInclusive()` to `NOT upper_inf(x) AND NOT isempty(x)`, so server results always equal the in-memory results ([verified against live PostgreSQL](#verified-against-postgresql)).
- The aggregates return `NULL` in SQL for zero input rows (standard PostgreSQL aggregate behavior), while the in-memory `RangeAgg()` returns the empty set. `RangeIntersectAgg()` returns `null` in both worlds.
- The factory-method bound-inclusiveness flags must be compile-time constants to translate (they pick the bounds literal, e.g. `'[]'`); in practice they always are, because the flags default at the call site.

Timestamp semantics:

- `DateTimeRange` bounds are written as `timestamp` with `DateTimeKind.Unspecified` — a UTC-kinded `DateTime` is reinterpreted as wall-clock time, not converted. `DateTimeOffsetRange` bounds are normalized to UTC for `timestamptz`: the instant is preserved, but the original offset is not round-tripped (values read back carry offset `+00:00` and compare equal to what was written, since `DateTimeOffset` equality is instant-based).
- Npgsql by default maps `DateTime.MinValue`/`MaxValue` to PostgreSQL `-infinity`/`infinity`. A *finite* bound of `DateTime.MaxValue` therefore becomes an explicit `infinity` bound in the database — which is distinct from an *unbounded* side (`upper_inf` stays `false`), so shape checks behave consistently.
- Reverse engineering (`dotnet ef dbcontext scaffold`) maps range columns to `NpgsqlRange<T>`, not to these types — the plugin provides no design-time services. Apply the range types manually after scaffolding.

### Value set columns

The same package maps every [value set type](#value-sets) to its native PostgreSQL array column — by convention, with nothing to configure. Wrapper instantiations (`StringSet<AccessRight>`) are recognized automatically from the open generic; there is no per-element registration to forget:

| Property type | Column type |
|---|---|
| `StringSet`, `StringSet<TElement>` | `text[]` |
| `GuidSet`, `GuidSet<TElement>` | `uuid[]` |
| `Int32Set`, `Int32Set<TElement>` | `integer[]` |
| `DateSet` | `date[]` |
| `YearMonthSet` (NodaTime) | `date[]` (month-aligned) |
| … and so on for all types | |

The set algebra translates to PostgreSQL's array operators:

```csharp
// b."Tags" @> ARRAY[@tag]::text[]   — containment, not = ANY: a GIN index always serves it
bookings.Where(b => b.Tags.Contains(tag));

// b."Tags" && @wanted               — order- and duplicate-insensitive, like all of these
bookings.Where(b => b.Tags.Overlaps(wanted));

// b."Tags" <@ @allowed  /  b."Tags" @> @required
bookings.Where(b => b.Tags.IsSubsetOf(allowed));
bookings.Where(b => b.Tags.IsSupersetOf(required));

// cardinality(b."Tags") > 2  /  cardinality(b."Tags") = 0
bookings.Where(b => b.Tags.Count > 2);
bookings.Where(b => b.Tags.IsEmpty);

// array_cat(b."Tags", @more) @> ARRAY[@tag]::text[]
bookings.Where(b => b.Tags.Union(more).Contains(tag));
```

`Intersect`, `Except`, `Add` and `Remove` are client-side only and fail query translation by design. Wrapper elements bind as their backing primitive (`AccessRight` parameters travel as `text`), and materialization re-runs the element's validation.

**Indexing** is ordinary EF configuration — no package involvement:

```csharp
modelBuilder.Entity<Booking>()
    .HasIndex(b => b.Tags)
    .HasMethod("GIN");
```

`Contains` deliberately translates as containment (`@>`) rather than `= ANY(...)`, because only containment is GIN-servable — one code path, always indexable.

**Set equality** (`==`) translates to SQL `=`, which is order-sensitive on arrays: it means set equality exactly because every writer stores canonical form. Rows written by other tools in non-canonical order are still matched correctly by all the operators above (they ignore order and duplicates) and normalize when materialized — only `==` carries the canonical-writers precondition. The empty set and a NULL column stay distinct (`{}` vs `NULL`); nullability is the property's own concern.

Two boundary notes: plain `T[]`/`List<T>` properties keep their native Npgsql mapping — both can coexist in one model — and database scaffolding produces plain arrays, since opting into a set type is a model decision. The NodaTime satellite registers its five set types via the same `UseValueRangesNodaTime()` call; `YearMonthSet` persists first-of-month dates and reads validate alignment, exactly like `YearMonthRange`.

### TimeRange and the custom timerange type

`timerange` is not built into PostgreSQL, so using `TimeRange` columns takes two one-line opt-ins beyond `UseValueRanges()`:

```csharp
// 1. The database needs the type — this generates
//    CREATE TYPE timerange AS RANGE (SUBTYPE = time) in your migrations
//    (PostgreSQL 14+ auto-creates timemultirange alongside it):
modelBuilder.HasPostgresRange("timerange", "time");

// 2. Npgsql needs permission to resolve the unmapped type on the wire:
options.UseNpgsql(connectionString, npgsql => npgsql
    .UseValueRanges()
    .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));
// (call EnableUnmappedTypes() on your own NpgsqlDataSourceBuilder instead
//  if you pass a pre-built NpgsqlDataSource to UseNpgsql)
```

Everything else is automatic: all range and multirange operators, functions and aggregates in PostgreSQL are polymorphic (`anyrange`/`anymultirange`), so the full LINQ translation works on the custom type exactly as on the built-ins — [verified against live PostgreSQL](#verified-against-postgresql). One caveat: PostgreSQL's `time` admits the special value `24:00:00`, which `TimeOnly` cannot represent; express "until end of day" as an unbounded end or an inclusive `TimeOnly.MaxValue` bound.

### YearMonthRange storage

The NodaTime satellite stores `YearMonthRange` as a **month-aligned `daterange`** — `[2024-01, 2024-03]` becomes `[2024-01-01, 2024-04-01)` — so no custom database type is involved and every operator, bound accessor and aggregate translates and agrees with the in-memory results (`upper()` compensation lands on the last day of the end month, whose month is the model's inclusive upper bound). Reads validate month alignment: a `daterange` covering a partial month fails loudly instead of silently shifting boundaries. The one restriction: because months are coarser than the `date` subtype, the `CreateFinite`/`CreateUnbounded*` factories cannot be built *in SQL from column values* — constant and parameter ranges work as usual, and a column-dependent factory call fails translation with a clear error.

In practice the restriction only bites when a query constructs the range *from a column*:

```csharp
// Factories over constants and locals never reach the translator — EF evaluates
// them client-side and the result renders as a month-aligned daterange literal:
// r."BillingPeriod" && '[2024-01-01,2024-06-30]'::daterange
var from = new YearMonth(2024, 1); var to = new YearMonth(2024, 6);
reservations.Where(r => r.BillingPeriod.Overlaps(YearMonthRange.CreateFinite(from, to)));

// Building the range from a column would need month arithmetic in SQL — a closed
// upper bound must expand to first-of-next-month, which the element-wise bound
// conversion cannot express. Fails with the standard EF translation error:
reservations.Where(r => YearMonthRange.CreateUnboundedEnd(r.Day.ToYearMonth())
                                      .Contains(month));  // ⛔ InvalidOperationException
```

For column-driven construction, fall back to a `LocalDateRange` built from the date column — `daterange` construction in SQL is fully supported there.

## Verified against PostgreSQL

The library's core promise — identical results in memory and as SQL — is enforced by three test layers:

1. **In-memory unit suite** — every operation across the full shape matrix: all 5×5 shape combinations per binary operation, the four bound-inclusiveness permutations, discrete and continuous domains, normalization invariants, and literal round-trips.
2. **Translation suite** — asserts the exact SQL generated for every LINQ construct via `ToQueryString()`, without a database.
3. **Live-PostgreSQL parity suite** — a Testcontainers-based project executes the translated SQL against a real PostgreSQL instance and asserts agreement with the in-memory results: round-trips for every range and multirange column type, the timestamp normalization and precision rules at the Npgsql boundary, and operation-level parity for the full algebra.

The live suite is the authority on semantics, and the model bends to it rather than the other way around: it is what established the discrete `upper()` canonicalization compensation and PostgreSQL's directional multirange adjacency rule documented above — before any user could trip over them. The NodaTime satellite types run through the same three layers, including the `Instant` sub-microsecond precision reduction and the `±infinity` boundary mapping.

All three layers run in CI on every push and pull request — the badge at the top of this page is the current state of the whole suite, live database included.

## License

MIT — see [LICENSE](LICENSE).

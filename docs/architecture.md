# Architecture

## Overview

CodoMetis.ValueRanges is a .NET 10 class library providing type-safe range types that mirror PostgreSQL's six built-in range domains. Each range is a discriminated union of five sealed variants, making invalid states unrepresentable and pattern matching exhaustive by contract. A companion EF Core package (`CodoMetis.ValueRanges.EFCore.PostgreSQL`) bridges these types to `NpgsqlRange<T>` for LINQ-to-SQL translation.

## Range Types

| C# Type | PostgreSQL | Element Type | Discrete |
|---|---|---|---|
| `Int32Range` | `int4range` | `int` | ✓ |
| `Int64Range` | `int8range` | `long` | ✓ |
| `DateRange` | `daterange` | `DateOnly` | ✓ |
| `DecimalRange` | `numrange` | `decimal` | — |
| `DateTimeRange` | `tsrange` | `DateTime` | — |
| `DateTimeOffsetRange` | `tstzrange` | `DateTimeOffset` | — |

Discrete types (int, long, DateOnly) implement `NextValueAfter`/`PreviousValueBefore` to return the adjacent value. Continuous types leave them returning `null`. Discrete ranges canonicalize to closed `[lower, upper]` at construction (`Internals/DiscreteCanonical.cs`); continuous ranges default to half-open `[lower, upper)`.

## NodaTime Satellites

`CodoMetis.ValueRanges.NodaTime` adds three range types over NodaTime elements, following the identical discriminated-union pattern:

| C# Type | PostgreSQL | Element Type | Discrete |
|---|---|---|---|
| `LocalDateRange` | `daterange` | `LocalDate` | ✓ |
| `LocalDateTimeRange` | `tsrange` | `LocalDateTime` | — |
| `InstantRange` | `tstzrange` | `Instant` | — |

Because the whole algebra (extensions, engines, `RangeSet`, parsing defaults, JSON factory) is generic over `IRange<T>`/`IRangeFactory<TRange, T>`, the satellite only defines the unions themselves plus per-type `RangeAgg`/`RangeIntersectAgg` overloads (`NodaTimeRangeAggregateExtensions`) and `Interval`/`DateInterval` interop adapters. It accesses `Internals/` (`DiscreteCanonical`, `RangeFormat`) via `InternalsVisibleTo` — the public API stays closed to third parties, preserving the exhaustive-matching guarantee. Types live in the shared `CodoMetis.ValueRanges` namespace (a `NodaTime` namespace segment would shadow the NodaTime root namespace).

Satellite-specific construction rules: `LocalDate`/`LocalDateTime` bounds normalize to the ISO calendar (`WithCalendar`) so `CompareTo` can never see mixed calendars; text I/O uses NodaTime's culture-free ISO patterns, with PostgreSQL wire-form fallbacks (space separator, numeric offsets) on parse.

`CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime` registers three `RangeTypeDefinition`s and the aggregate overload class via `UseValueRangesNodaTime()`, which also chains Npgsql's `UseNodaTime()` (element mappings) and `UseValueRanges()`.

## Discriminated Union Pattern

Every range type is an abstract record with five sealed nested variants:

```
RangeType (abstract, private ctor)
├── EmptyRange     : IEmptyRange<T>       — contains no values
├── Finite         : IFiniteRange<T>      — [start, end] (bounded both sides)
├── UnboundedStart : IUnboundedStartRange<T> — (-∞, end]
├── UnboundedEnd   : IUnboundedEndRange<T>   — [start, +∞)
└── Infinity       : IInfinityRange<T>    — (-∞, +∞)
```

The private base constructor prevents external subtyping, so the compiler guarantees exhaustive switch expressions. Invalid ranges (inverted bounds, degenerate half-open) normalize to `EmptyRange` at construction time.

## Interface Hierarchy (`Core/`)

- **`IRange<T>`** — Marker interface. Carries `internal default methods` `IntersectWith<TRange>()` and `MergeWith<TRange>()` that dispatch per-shape to the engines in `Internals/`.
- **`IRangeFactory<TRange, T>`** — Abstract static factory: `Empty`, `Infinite`, `CreateFinite()`, `CreateUnboundedStart()`, `CreateUnboundedEnd()`, plus virtual `NextValueAfter`/`PreviousValueBefore`. Also implements `IParsable<TRange>` and `IFormattable` with PostgreSQL range literal syntax.
- **Structural interfaces** — `IFiniteRange<T>`, `IUnboundedStartRange<T>`, `IUnboundedEndRange<T>`, `IEmptyRange<T>`, `IInfinityRange<T>` — each provides its own concrete `IntersectWith`/`MergeWith` implementations (e.g., `IInfinityRange<T>` always returns the other operand for intersection, always returns `Infinite` for merge).

All type parameters are constrained to `struct, IComparable<T>, IEquatable<T>`.

## Extension Methods (`RangeExtensions.cs`)

Uses the C# 14 `extension` keyword. Two `extension<T>` blocks:

1. **Query operations** on `IRange<T>` — state checks (`IsEmpty`, `IsInfinity`, etc.), bound accessors (`LowerBound`/`UpperBound` returning `T?`, `LowerBoundInclusive`/`UpperBoundInclusive` — PostgreSQL `lower`/`upper`/`lower_inc`/`upper_inc`), containment, overlap, adjacency, directional comparisons
2. **Set operations** on `IRangeFactory<TRange, T>` — `Intersect` (returns `TRange`), `Merge` (convex hull, `range_merge`, returns `TRange`), `Union`/`Except` (return `RangeSet<TRange, T>`)

See `CodoMetis.ValueRanges/RangeExtensions.cs` for the full implementation.

## Aggregates (`RangeAggregateExtensions.cs`)

`RangeAgg()` (→ `RangeSet`, PostgreSQL `range_agg`) and `RangeIntersectAgg()` (→ `TRange?`, `null` on empty source, PostgreSQL `range_intersect_agg`) over `IEnumerable<TRange>`. Declared as **per-type overloads** (six each) because C# cannot infer the element type `T` from `TRange` alone — constraints do not participate in type inference. A generic private core holds the intersect-fold logic.

## RangeSet<TRange, T> (`RangeSet.cs`)

Immutable multirange counterpart of PostgreSQL's `int4multirange`, etc. A sealed class over `ImmutableArray<TRange>` with a strict invariant:

- Sorted by lower bound
- Pairwise disjoint, pairwise non-adjacent
- No empty elements
- Any `Infinity` input collapses the set to `Infinite` singleton

Key methods:
- `From(IEnumerable<TRange>)` — normalizes (filter → sort via `Internals/RangeSetHelpers.CompareByLowerBound` → greedy merge)
- Bulk ops (`Union`, `Intersect`, `Except`) use O(n+m) merge-join instead of nested loops
- Operators: `\|` for union, `&` for intersect, `-` for except; `==`/`!=` are structural equality (delegating to `Equals`)
- State checks `IsEmpty()`/`IsUnboundedStart()`/`IsUnboundedEnd()`, bound accessors (`LowerBound` = first element's lower, `UpperBound` = last element's upper), and `Merge()` (convex hull of first + last element)
- Set-operand comparisons `Contains(RangeSet)`/`Overlaps(RangeSet)` delegate per element (O(m log n))
- Positional comparisons (`IsAdjacentTo`, `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf`) mirror PostgreSQL exactly: they consult the **outermost elements only**, and adjacency is **directional through the outer edges** — the operand must end where the set begins or begin where the set ends; interior boundaries (even the inner side of the first/last element) never count. Verified against live PostgreSQL in the integration suite.
- `LowerBoundComparer` — static `IComparer<TRange>` for external sorting

See `CodoMetis.ValueRanges/RangeSet.cs` and `CodoMetis.ValueRanges/RangeLowerBoundComparer.cs`.

## JSON Serialization (`Serialization/`)

- `RangeJsonConverter<TRange, T>` — serializes to/from PostgreSQL range literal strings
- `RangeJsonConverterFactory` — auto-registers for any type implementing `IRangeFactory<TRange, T>` or `RangeSet<TRange, T>`
- Extension: `AddRangeConverters()` registers all at once

## EF Core PostgreSQL (`CodoMetis.ValueRanges.EFCore.PostgreSQL/`)

- **`ValueRangesMethodCallTranslator`** — translates LINQ methods to PostgreSQL operators (`@>`, `&&`, `<@`, `<<`, `>>`, `&<`, `&>`, `-|-`, `*`, `+`, `-`) and functions (`lower`, `upper`, `lower_inc`, `upper_inc`, `isempty`, `lower_inf`, `upper_inf`, `range_merge`), for ranges and multiranges
- **`ValueRangesAggregateMethodCallTranslator`** — translates `RangeAgg`/`RangeIntersectAgg` to `range_agg`/`range_intersect_agg` inside grouped queries, for every declaring class registered via `RangeTypeRegistry.RegisterAggregateExtensions`
- **Type mapping** — maps range types to PostgreSQL range columns, RangeSet to multirange columns
- **`RangeTypeRegistry`** (`Internal/`) — the single wiring point. Process-wide and additive: the six built-ins are registered up front; satellites contribute `RangeTypeDefinition`s at options-configuration time via `Register` (idempotent per range CLR type, thread-safe immutable-snapshot swap). Lookups: by range/set CLR type, by element type (the `IRange<T>`-typed-operand fallback — one range type per element type, enforced), and by store type name (first registration owns the name; BCL and NodaTime types share `daterange` etc., so store-name-only resolution stays with the BCL types)
- **Enable**: `options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());` — or `npgsql.UseValueRangesNodaTime()` from the NodaTime satellite, which implies it

## Engine Internals (`Internals/`)

- `IntersectEngine.cs`, `MergeEngine.cs` — per-shape intersection and merge logic
- `ExceptEngine.cs` — set difference with boundary inversion at cut points
- `DiscreteCanonical.cs` — canonicalizes discrete ranges to closed form
- `RangeBoundHelpers.cs`, `RangeFormat.cs`, `RangeSetHelpers.cs` — shared utilities

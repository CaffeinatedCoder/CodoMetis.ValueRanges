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

1. **Query operations** on `IRange<T>` — state checks (`IsEmpty`, `IsInfinity`, etc.), containment, overlap, adjacency, directional comparisons
2. **Set operations** on `IRangeFactory<TRange, T>` — `Intersect` (returns `TRange`), `Union`/`Except` (return `RangeSet<TRange, T>`)

See `CodoMetis.ValueRanges/RangeExtensions.cs` for the full implementation.

## RangeSet<TRange, T> (`RangeSet.cs`)

Immutable multirange counterpart of PostgreSQL's `int4multirange`, etc. A sealed class over `ImmutableArray<TRange>` with a strict invariant:

- Sorted by lower bound
- Pairwise disjoint, pairwise non-adjacent
- No empty elements
- Any `Infinity` input collapses the set to `Infinite` singleton

Key methods:
- `From(IEnumerable<TRange>)` — normalizes (filter → sort via `Internals/RangeSetHelpers.CompareByLowerBound` → greedy merge)
- Bulk ops (`Union`, `Intersect`, `Except`) use O(n+m) merge-join instead of nested loops
- Operators: `\|` for union, `&` for intersect, `-` for except
- `LowerBoundComparer` — static `IComparer<TRange>` for external sorting

See `CodoMetis.ValueRanges/RangeSet.cs` and `CodoMetis.ValueRanges/RangeLowerBoundComparer.cs`.

## JSON Serialization (`Serialization/`)

- `RangeJsonConverter<TRange, T>` — serializes to/from PostgreSQL range literal strings
- `RangeJsonConverterFactory` — auto-registers for any type implementing `IRangeFactory<TRange, T>` or `RangeSet<TRange, T>`
- Extension: `AddRangeConverters()` registers all at once

## EF Core PostgreSQL (`CodoMetis.ValueRanges.EFCore.PostgreSQL/`)

- **`ValueRangesMethodCallTranslator`** — translates LINQ methods to PostgreSQL operators (`@>`, `&&`, `<@`, `<<`, `>>`, `&<`, `&>`, `-|-`, `*`, `+`, `-`)
- **Type mapping** — maps range types to PostgreSQL range columns, RangeSet to multirange columns
- **Enable**: `options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());`

## Engine Internals (`Internals/`)

- `IntersectEngine.cs`, `MergeEngine.cs` — per-shape intersection and merge logic
- `ExceptEngine.cs` — set difference with boundary inversion at cut points
- `DiscreteCanonical.cs` — canonicalizes discrete ranges to closed form
- `RangeBoundHelpers.cs`, `RangeFormat.cs`, `RangeSetHelpers.cs` — shared utilities

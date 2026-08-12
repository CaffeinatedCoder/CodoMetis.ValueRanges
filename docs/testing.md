# Testing

## Running Tests

```bash
dotnet test                          # All tests (method-level parallel)
dotnet test --filter "ClassName=RangeContainsTests"   # Single class
dotnet test --filter "FullyQualifiedName~Contains_FiniteRange"  # Single method
```

Tests run in parallel at the **method level** (`MSTestSettings.cs`). No shared state between tests.

## Organization

One test file per operation, named `Range[Operation]Tests.cs`:

| File | Covers |
|---|---|
| `RangeContainsTests.cs` | Point and range containment |
| `RangeOverlapsTests.cs` | Overlap detection |
| `RangeIsAdjacentTests.cs` | Adjacency (discrete step-aware, continuous complementary inclusiveness) |
| `RangeIntersectTests.cs` | Intersection across all shape combinations |
| `RangeUnionTests.cs` | Union (merge overlapping, keep disjoint) |
| `RangeExceptTests.cs` | Set difference (0/1/2 element results) |
| `RangeContainedByTests.cs` | Symmetric containment alias |
| `RangeDoesNotExtendLeftOfTests.cs` / `RightOfTests.cs` | PostgreSQL `&<`/`&>` |
| `RangeStrictlyLeftOrRightOfTests.cs` | PostgreSQL `<<`/`>>` |
| `RangeBoundAccessorTests.cs` | `lower`/`upper`/`lower_inc`/`upper_inc` equivalents on ranges and sets |
| `RangeMergeSpanTests.cs` | `Merge` convex hull (`range_merge`), spanning gaps |
| `RangeAggregateTests.cs` | `RangeAgg`/`RangeIntersectAgg` (`range_agg`, `range_intersect_agg`) |
| `RangeSetComparisonTests.cs` | Multirange operator parity: state checks, set-operand ops, positional comparisons, `==`/`!=` |
| `RangeParseFormatTests.cs` | PostgreSQL range literal round-trips |
| `RangeSetTests.cs` | RangeSet construction, normalization, bulk ops |
| `RangeSetOptimizationTests.cs` | Performance-critical invariants |
| `RangeJsonConverterTests.cs` | JSON serialization round-trips |
| `TimeRangeTests.cs` | What is new with `TimeRange`: TimeOnly parse/format, half-open default, midnight-wrap sets (per-type file; the engines are covered by the per-operation files) |

`CodoMetis.ValueRanges.NodaTime.Tests/` covers what is new in the NodaTime satellite — type wiring, the one-day and one-month discrete steps, ISO calendar normalization (and `YearMonthRange`'s non-ISO rejection), wire-form parsing, `Interval`/`DateInterval`/month-alignment interop — with representative shape coverage; the engines themselves are exhaustively covered by the core tests.

EF Core translation tests live in `CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests/` — they assert generated SQL via `ToQueryString()` without a database. `CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests/` does the same for the NodaTime types, proving every translation path (operators, functions, factories, aggregates, multiranges, BCL coexistence).

`CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests/` executes against **live PostgreSQL** via Testcontainers (Docker required; tests report Inconclusive without it — except under `CI=true`, where a missing database **fails** the tests so the build badge can never be green with the live layer silently skipped): round-trips for every range/multirange type, timestamp normalization rules, and SQL-vs-in-memory parity assertions for the v4 operations. `TimeAndYearMonthIntegrationTests.cs` covers the v5 types: the custom `timerange` created via `HasPostgresRange` + `EnableUnmappedTypes`, and `YearMonthRange`'s month-aligned `daterange` storage form. These tests are the authority on PostgreSQL semantics — they caught the discrete `upper()` canonicalization offset and the directional multirange adjacency rule.

## Patterns

### Shape-Combination Matrix

Every binary operation test must cover all five range shapes: `Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange`, `Infinity`. Tests instantiate the "other" operand in each shape and verify the result type and value.

Example from `RangeContainsTests.cs`:
```csharp
// Tests Finite vs each shape of "other" — interior, left-of, right-of, overlapping
```

### Boundary Inclusiveness Permutations

For `Finite` ranges, tests cover all four inclusiveness combinations:
- `[start, end]` — both inclusive (default for discrete)
- `(start, end)` — both exclusive
- `[start, end)` — lower inclusive (default for continuous)
- `(start, end]` — upper inclusive

### Discrete vs Continuous Split

Tests are parameterized by range type:
- **Discrete** (`Int32Range`, `Int64Range`, `DateRange`; satellite: `LocalDateRange`, `YearMonthRange`) — canonicalization, adjacency with step awareness
- **Continuous** (`DecimalRange`, `DateTimeRange`, `DateTimeOffsetRange`, `TimeRange`; satellite: `LocalDateTimeRange`, `InstantRange`) — half-open defaults, equality via `IEquatable`

### Assertion Style

Use MSTest assertions (`Assert.IsTrue`, `Assert.IsFalse`, `Assert.AreEqual`, `Assert.AreSame`). For structural shape checks, cast to the specific interface (e.g., `IFiniteRange<int>`) and verify properties.

### RangeSet Type Aliases

Use local type aliases for readability in tests:
```csharp
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;
using DecimalSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DecimalRange, decimal>;
```

## What to Test

- **Every public method** on range types and `RangeSet`
- **All shape combinations** for binary operations (5×5 = 25 cases per operation)
- **Boundary inclusiveness** permutations for `Finite` ranges
- **Normalization invariants**: empty filtering, overlap merging, adjacency merging, Infinity collapse
- **Round-trips**: parse → format should produce equivalent ranges (exact string match for same-shape inputs)
- **Edge cases**: empty input to `RangeSet.From()`, single-element fast path, `Infinite.Except()` complement

## What Not to Test

- Framework internals (MSTest, System.Text.Json)
- Simple property getters on range variants
- Code paths that are provably unreachable (private constructors, sealed subtypes)

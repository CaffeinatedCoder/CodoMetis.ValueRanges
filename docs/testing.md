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
| `RangeJsonConverterTests.cs` | JSON serialization round-trips; null tokens rejected on read, null references written as `null`; variant-typed and `object`-typed declarations producing the same literal, with wrong-shape reads rejected |
| `RangeShapePredicateTests.cs` | The five `IRange<T>` shape predicates as a one-hot matrix over all five variants, plus degenerate-bound normalization |
| `NamedJsonConverterTests.cs` | The 24 pre-built named converters (ranges, multiranges and sets): the full inventory, each bound to the family it is named for, and each matching the factory's payload |
| `ParserResilienceTests.cs` | Hostile input in the request path: megabyte-scale malformed literals and 200k-element sets parse or are rejected within a `[Timeout]`, `TryParse` never throws, and rejection messages carry an excerpt of the input, never the payload |
| `TimeRangeTests.cs` | What is new with `TimeRange`: TimeOnly parse/format, half-open default, midnight-wrap sets (per-type file; the engines are covered by the per-operation files) |

Value set tests (v6) follow the same one-file-per-concern pattern with a `Set` prefix:

| File | Covers |
|---|---|
| `SetCanonicalFormTests.cs` | Dedupe/sort per family, the ordinal-vs-culture pin (umlauts prove `IComparable` is ignored on string families), numeric `10`-vs-`"10"` order, decimal-scale and instant deduplication, null rejection, empty singletons, collection expressions |
| `SetAlgebraTests.cs` | Contains/Overlaps/IsSubsetOf/IsSupersetOf/Union/Intersect/Except/Add/Remove, including instance-preservation on no-op results |
| `SetEqualityTests.cs` | Structural equality, operators, hash codes, null handling |
| `SetParseFormatTests.cs` | PostgreSQL array-literal round-trips, quoting/escaping, unquoted-`NULL` rejection, invariant-culture pins |
| `SetJsonConverterTests.cs` | JSON array round-trips via the shared converter factory, null-element rejection, wrapper-element delegation, and the `ElementJsonConverter` split — `null` for the primitive-backed families, defined for every wrapper arity, integer-backed wrappers writing numbers |
| `SetElementBridgeTests.cs` | The validated-wrapper arities: text bridge round-trips for string/Guid/int/long-backed test keys (`TestKeys.cs`), backing-value order pins |

`SetElementBridgeTests.cs` and `SetJsonConverterTests.cs` cover all ten core arities, one generator-shaped test key each (`TestKeys.cs`). The temporal assertions are all on sub-second components, because those are the digits a default-form bridge drops. Two element types there are deliberately non-conforming — `TestLossyStamp` swallows the format argument — and pin where that is caught: silently truncating in the pure model's array literal, loudly at the EF boundary.

Coverage of the arities *as a family* lives in the conventions suite, because it is discovery-driven: `ValueSetContractTests` closes every one over a representative element from `WrapperElements.cs` and runs the same three contract assertions it runs on the closed types, so an arity added without a probe or without honouring `NormalizeElement`/`CanonicalOrder`/`ElementJsonConverter` fails there.

`test/CodoMetis.ValueRanges.NodaTime.Tests/` covers what is new in the NodaTime satellite — type wiring, the one-day and one-month discrete steps, ISO calendar normalization (and `YearMonthRange`'s non-ISO rejection), wire-form parsing, `Interval`/`DateInterval`/month-alignment interop — with representative shape coverage; the engines themselves are exhaustively covered by the core tests. `NodaTimeSetTests.cs`/`NodaTimeSetJsonTests.cs` do the same for the five set types (calendar normalization, wire-form parsing, JSON with `ConfigureForNodaTime`). `NodaTimeSetElementBridgeTests.cs` covers the five NodaTime arities — ISO literal round-trips, ordering, algebra, JSON, and `CultureBoundDay`, an element that ignores the format argument and so leaks NodaTime's culture long form into the literal. `NodaTimeConverterRegistrationTests.cs` covers both JSON entry points: the `ElementJsonConverter` hook (all five sets round-tripping under plain `AddRangeConverters()` and under a bare factory, a registered converter still winning, identical output either way), and `AddNodaTimeRangeConverters()` (ISO 8601 element forms, all four ranges and all five sets, bare elements alongside a set, idempotence and order-independence against `AddRangeConverters()` and `ConfigureForNodaTime`).

EF Core translation tests live in `test/CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests/` — they assert generated SQL via `ToQueryString()` without a database. `SetModelMappingTests.cs`/`SetQueryTranslationTests.cs` cover the value set columns: every operator in constant, parameter, and column form, the `string[]`-coexistence and store-name negatives, the without-`UseValueRanges` model-build failure, and the wrapper element binding. `WrapperArityIntegrationTests.cs` closes the live-database half of the same gap: twelve of the fifteen arities had never touched PostgreSQL, so everything known about them was a claim about SQL text. It round-trips each one, asserts the stored column text (a bridge that coarsens *consistently on both legs* round-trips perfectly and stores the wrong thing, which is how the format-pinning defect hides), and runs `Contains` server-side both for a stored element and for a near miss one unit of precision away. Seeding the unpinned-`TimeSet<>` defect fails the round trip, the stored text and the near miss — but *not* the positive `Contains`, since probe and column truncate alike. Its element types are linked from `WrapperElements.cs` in the conventions project rather than copied, so the values that round-trip are the ones whose semantics `ValueSetContractTests` checks. `SqlLiteralTests.cs` pins what the wrapper bridge actually emits — the sub-second digits of a `DateTimeSet<T>` element, its `Kind` normalization, and a `DecimalSet<T>` element's scale — which is the only place that conversion is observable without a database. `WrapperSetTranslationTests.cs` does for the ten core arities what `NodaWrapperSetTranslationTests.cs` (below) does for the five NodaTime ones: the `Contains` literal per arity, the two set-operand forms, equality, cardinality, the `Union` refusal, and the `array_remove` shape — the only operand position where the element carries no array cast to type it. Its precision sweep is stated as *survival of a value*, not absence of a lossy shape, because an arity that stops pinning its format still emits a well-formed ISO literal: unpinning `TimeSet<>` yields `ARRAY['09:30:00.0000000']`, seven fraction digits and fifteen seconds short. `DateTime` and `DateOnly` cannot fail this way — their default form is rejected by the bridge's `ParseExact` — so `TimeOnly` and `decimal` are the two cases with no exception to catch. `QueryTranslationTests.cs` also pins the shape predicates on a single range column, including the two PostgreSQL has no function for — `IsInfinity` as `lower_inf AND upper_inf`, `IsFinite` as the negation of both plus `NOT isempty`. `test/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests/` does the same for the NodaTime types, proving every translation path (operators, functions, factories, aggregates, multiranges, sets, BCL coexistence). `NodaWrapperSetTranslationTests.cs` covers the NodaTime arities specifically — the highest-risk corner, since they carry the most custom bridge code and reach the registry through `RegisterFamily` rather than as closed definitions. Its operands go through `EF.Constant` on purpose: a captured variable is parameterized, and a parameter binds the converted primitive natively, which hides the entire literal path. That is not hypothetical — writing these tests is what caught `LocalDateSet<T>` rendering `ARRAY['Saturday, 15 June 2024']::date[]`, because its family registration had left `literalText` at the default.

`test/CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests/` executes against **live PostgreSQL** via Testcontainers (Docker required; tests report Inconclusive without it — except under `CI=true`, where a missing database **fails** the tests so the build badge can never be green with the live layer silently skipped): round-trips for every range/multirange type, timestamp normalization rules, and SQL-vs-in-memory parity assertions for the v4 operations. `TimeAndYearMonthIntegrationTests.cs` covers the v5 types: the custom `timerange` created via `HasPostgresRange` + `EnableUnmappedTypes`, and `YearMonthRange`'s month-aligned `daterange` storage form. `SetIntegrationTests.cs` (v6, Id block 8xxx) covers the value sets: canonical round-trips with stored-array-text pins, empty-vs-NULL, non-canonical foreign rows (operators match, `=` does not, reads normalize without rewriting the row), corrupt rows throwing on read, change detection, GIN-served `@>` (EXPLAIN pin), the `StringSet<TestKey>` wrapper hinge, and server-vs-in-memory parity for the whole set algebra. These tests are the authority on PostgreSQL semantics — they caught the discrete `upper()` canonicalization offset and the directional multirange adjacency rule.

`ShapeMatrixParityTests.cs` generalizes that authority: it asks PostgreSQL for all eight binary predicates over every ordered pair of range shapes — and, in a second sweep, the four value-producing operations (`Intersect`, `Merge`, `Union`, `Except`) against `*`, `range_merge`, `+` and `-` — `int4range`, `numrange` and `daterange`, some 2,400 comparisons — and requires the in-memory answer to match. The per-predicate tests elsewhere pick the shapes a human thought to try, which is how `IsAdjacentTo` (6.2.1), `IsStrictlyLeftOf` and `Except` (both 7.0.0) all shipped answering wrongly for a whole family of receivers: each was a switch on the receiver's shape whose inner switch handled the operand's, so the two directions drifted apart without any one test noticing. The matrix has no such blind spot. Since 7.0.0 the model agrees with the server on every pair, the empty-subset rule included, so the matrix carries no exclusions.

`MultirangeCoverageTests.cs` (Id block 91xx) closes the two coverage holes the 7.0.0 audit found. Five of the eleven `RangeSet<,>` instantiations — over `Int64Range`, `DecimalRange`, `DateTimeRange`, `DateTimeOffsetRange` and `LocalDateTimeRange` — had never been mapped to a column, so their store type, literal form and normalization on read were untested; they now round-trip with the stored column text pinned beside CLR equality, because a bridge that coarsens consistently on both legs round-trips perfectly and stores the wrong thing. The file also executes the two operand positions that carry no type of their own: a **constant element** against a polymorphic range operator (`anyrange @> anyelement` resolves without implicit coercion, which is why a bare `25` failed against `int8range`), and the **server-side range constructors**, whose bounds string `'[]'` and bare `NULL` bound are resolved by PostgreSQL's function-overload resolution — every previous use of `CreateFinite` in this suite had constant arguments and was folded client-side, so the generated SQL was a claim about the server that nothing ran.

`test/CodoMetis.ValueRanges.Conventions.Tests/` covers what the compiler cannot: defects that build, test and pack clean and only surface once a consumer installs the package. Four groups — **changelog consistency** (the shipped `Version` is documented at the root and in every package's changelog, no file runs ahead of the version property, per-package versions are a subset of the root's, sections newest-first), **packaging** (every package declares a `PackageReadmeFile` and packs a README from *its own* directory rather than a shared one, carries a description and tags; the shared props ship symbols and Source Link with `ContinuousIntegrationBuild` gated on CI), **value set contracts** (the `NormalizeElement`, `CanonicalOrder` and `ElementJsonConverter` rules from CLAUDE.md, asserted behaviourally: a set that breaks any of them fails to find an element it was built from, or writes objects where scalars belong), and **mapping parity** (every range and set type in both shipping assemblies resolves through `IRelationalTypeMappingSource` — including each wrapper arity closed over its representative element, which is a second way to be missing from the registry, since the families are registered by open generic definition rather than as closed definitions; plain arrays keep their native provider mapping).

Everything is discovered — projects by globbing `src/`, types by reflection — so a new package or type is covered without editing the tests. Two consequences worth knowing:

- Both discovery-driven classes assert a **floor** on how many types they find. This is not padding: the mapping parity tests were passing while iterating an empty list, because the range unions are abstract records and the predicate filtered abstract types out. The floor caught it on the first run.
- The probe values in `ValueSetContractTests` are load-bearing. Probes that ordinal and culture comparison order identically let the `CanonicalOrder` rule pass while broken — `"Zebra"`/`"apple"` split the two orders, and `TextKey` compares culture-sensitively on purpose, because an ordinal `IComparable` made the element agree with canonical order by accident.

Every test in this project has been verified by seeding the defect it claims to catch and confirming it — and only it — fails.

## Patterns

### The Two Oracles

Two tests check range algebra against something other than itself, and they answer different
questions. Neither replaces the other.

- **`ShapeMatrixParityTests`** (integration, needs Docker) asks *PostgreSQL* for every binary
  predicate and value operation over every ordered shape pair and diffs. This is the only check
  that the library agrees with the database rather than with its own idea of what a range means.
  It uses about ten hand-picked representatives per domain.
- **`SmallModelOracleTests`** (unit, no Docker) asks *set theory*. It enumerates every
  representable range over a tiny universe from its specification — around 110 per domain, all
  five shapes and all four inclusiveness combinations at every bound — derives the expected value
  set arithmetically from that specification, and checks all ~12,100 ordered pairs. About 460,000
  assertions across the discrete and continuous domains, in under 200 ms.

The small-model oracle is the stronger *bug detector* of the two, because hand-picked
representatives are how the first version of the 7.0.0 `Except` sweep came back with zero
disagreements on the exact defect it was written to catch — its operands happened to be disjoint,
so the failing pair never arose. It also covers the `RangeSet` arities, which is where two of the
five known bugs of this family lived: an operation lifted through `RangeSet.From([r])` must give
the lifted answer.

Two details make the small domain faithful, and a change to either breaks the model silently:

- **Bounds are only ever drawn from the interior of the universe**, so the outermost grid points
  are values no bound names. An unbounded side then reaches a point a finite side cannot, which is
  what lets the model tell `[1,6]` from `[1,+∞)` — a distinction `&<` and `&>` turn on.
- **The continuous domain uses a half-step grid**, so an exclusive bound is one grid point away
  from an inclusive one at the same value. `[1,2)` and `[2,3]` abut; `[1,2)` and `(2,3]` leave the
  point 2 between them.

The model has one axiom — that `Contains(T)` is correct — since results are read back through it.
That is pinned by its own test per domain rather than assumed, and `Contains(T)` is itself verified
against PostgreSQL's element `@>` in the integration suite.

### The Value Set Oracle

`SmallModelSetOracleTests` (in the conventions project, which references all four shipping
assemblies) does the same for the value set families — and there it is the *only* oracle. `Intersect`,
`Except` and `Add` are deliberately client-side only, because a PostgreSQL array has no
intersection, no difference and no sorted insert, so no amount of integration testing can
cross-check them.

For each of the 30 set types it builds **every subset** of that family's probe universe — 2^n, so
exhaustive over the whole value space rather than a sample — and checks:

- per subset: `Values`, `Count`, `IsEmpty`, `Contains`, `Add` and `Remove` for every probe
- per ordered pair: `Union`, `Intersect`, `Except`, `Overlaps`, the four subset/superset
  predicates, equality, and that equal sets hash equally
- per construction path: reversed input, duplicated input, `IEnumerable`, repeated `Add` from
  empty, `Parse(ToString())`, and a JSON round trip — all of which must land on the identical
  canonical array, because canonical form is a property of the value and not of the order it
  arrived in

Its one axiom is that `From(x)` on a single element normalizes correctly, since element
normalization is family-specific and internal (`TextKey` trims, the NodaTime calendar types convert
to ISO) and cannot be derived externally the way a range's values can be derived from its bounds.
That is pinned by `ValueSetContractTests.EverySetType_FindsAnElementItWasBuiltFrom`.

**What it cannot check:** the model reads the canonical *order* from `CanonicalComparer`, so it
verifies that every path agrees with the declared order, not that the declared order is the
specified one. Swapping `StringComparer.Ordinal` for `StringComparer.InvariantCulture` moves the
model with the implementation. `ValueSetContractTests.StringBackedFamilies_SortOrdinal` is the
independent check that closes it.

Probes and type discovery are shared with the contract tests through `SetProbes` — one table, so
the two suites cannot disagree about which families exist or what to feed them.

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
using DecimalRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DecimalRange, decimal>;
```
Alias names must not collide with the value set types (`DecimalSet`, `DateSet`, `TimeSet`, …):
test namespaces live under `CodoMetis.ValueRanges`, and namespace-member lookup beats file-level
aliases — prefer the `*RangeSet` naming.

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

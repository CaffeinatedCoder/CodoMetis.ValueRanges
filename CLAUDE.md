# CodoMetis.ValueRanges

In-memory range and multirange types for .NET 10, mirroring PostgreSQL's six built-in range domains (`int4range`, `int8range`, `numrange`, `daterange`, `tsrange`, `tstzrange`) plus two v5 additions: `TimeRange` over `TimeOnly` (maps to the custom `timerange` type — needs `HasPostgresRange` + `EnableUnmappedTypes` on the EF side) and, in the NodaTime satellite, `YearMonthRange` over `YearMonth` (stored as a month-aligned `daterange`). Each range type is a discriminated union of five sealed variants with exhaustive pattern matching.

Since v6 there is a second type family: **value sets** (`Sets/`) — immutable, canonical (deduplicated, sorted, no nulls) sets of scalar values stored as native PostgreSQL arrays (`StringSet`/`text[]`, `GuidSet`/`uuid[]`, …), plus a validated-wrapper arity for every family (`StringSet<T>` … `DateTimeOffsetSet<T>`, and five more in the NodaTime satellite) constrained only on BCL interfaces. EF maps them as scalars with plugin-owned mappings and translators (`@>`, `&&`, `<@`, `cardinality`, `array_cat`, `array_remove`) — never through EF's primitive-collection machinery. `Intersect`/`Except`/`Add` are deliberately client-side only: PostgreSQL's array type has no intersection, difference, or sorted insert.

## Stack
.NET 10 · C# 14 (extension methods) · MSTest 4.x · EF Core + Npgsql (PostgreSQL bridge)

## Structure
Shipping projects live under `src/`, test projects under `test/`. Never hard-code a positional
path (`../../`) to reach the repo root — walk up to the `CodoMetis.ValueRanges.slnx` marker file,
so moving a project cannot silently retarget a test.

- `src/CodoMetis.ValueRanges/` — Core library: range types, interfaces, set ops
- `src/CodoMetis.ValueRanges.NodaTime/` — NodaTime satellite: LocalDateRange, LocalDateTimeRange, InstantRange (namespace stays `CodoMetis.ValueRanges`; uses core internals via InternalsVisibleTo)
- `src/CodoMetis.ValueRanges.EFCore.PostgreSQL/` — EF Core provider for LINQ-to-SQL translation
- `src/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime/` — EF satellite: registers NodaTime RangeTypeDefinitions, `UseValueRangesNodaTime()`
- `test/CodoMetis.ValueRanges.Tests/` — Unit tests (one file per operation)
- `test/CodoMetis.ValueRanges.NodaTime.Tests/` — NodaTime satellite unit tests
- `test/CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests/` — EF Core SQL translation tests (no database)
- `test/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests/` — NodaTime EF translation tests (no database)
- `test/CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests/` — Live PostgreSQL via Testcontainers (needs Docker; Inconclusive without, but hard failure under CI=true). Covers BCL and NodaTime types. Authority on PostgreSQL semantics — run when changing translations or range algebra
- `test/CodoMetis.ValueRanges.Conventions.Tests/` — Repo-level conventions: changelog consistency, packaging metadata, release wiring, the SECURITY.md supported-versions table, value set contract compliance, EF mapping parity. Everything is discovered (projects by globbing `src/`, types by reflection), so adding a package or a type needs no edit here
- `test/consumer-smoke-test.sh` — The only check of *delivery* rather than code: packs the four packages, restores them into throwaway projects created outside the repository (inside it, `Directory.Build.props` would apply), compiles and runs real code against them, and asserts on printed output and translated SQL — never on the exit code alone. Two consumers: core only, and the NodaTime EF satellite, which pulls in all four through the nuspec chain. `NUGET_PACKAGES` is redirected to a private folder because NuGet resolves id+version from the global cache before any source, so a locally built package is otherwise shadowed by whatever build of that version was restored before; the generated `nuget.config` uses package source mapping so this repo's ids come from the local feed only and everything else from nuget.org. Runs on every PR in both modes (own feed; the pre-built `dist` the release workflow passes). Everything is discovered (projects by globbing `src/`, types by reflection), so adding a package or a type needs no edit here
- `docs/` — Agent docs (read relevant doc before starting work)

## Commands
```bash
dotnet build                          # Build everything
dotnet test                           # Run all tests
dotnet test --filter "ClassName=RangeContainsTests"   # Single test class
dotnet test --filter "FullyQualifiedName~Contains_FiniteRange"  # Single method
dotnet pack -c Release                # Pack NuGet packages (builds first)
```

The build treats warnings as errors (`Directory.Build.props`), including CS1591 on the public
surface of the shipping projects — the shipped `.xml` is what IntelliSense shows. Deliberate
internal-API usage (EF1001) is acknowledged with a file-level pragma next to the reason, never
repo-wide.

## Workflow
1. Read `docs/architecture.md` before modifying range types or interfaces
2. Explore the codebase — range operations are per-shape (Finite, UnboundedStart, etc.)
3. Run `dotnet test` after each change; tests are method-level parallel
4. **Prove every fix by reverting it.** Strip only the fix, confirm the new test fails for the
   expected reason, restore. A test that passes with and without the fix is not a regression
   test — and one written against broken code can assert nothing at all without looking any
   different. The same applies to a new convention test: seed the defect it claims to catch
5. Commit with conventional format: `feat:`, `fix:`, `refactor:`, `docs:`, `test:`, `build:`

## Docs
- `docs/architecture.md` — Discriminated union pattern, interface hierarchy, RangeSet internals
- `docs/testing.md` — Test organization, patterns, shape-combination matrix

## Critical Rules
- **NEVER** create external subtypes of range base records — the private constructor is what makes the five variants the only ranges that can exist, so a switch over them is complete *in fact*. C# never proved it (exhaustiveness analysis ignores a closed class hierarchy, so a switch expression still warns CS8509 and needs a throwing discard — see `docs/ranges.md#pattern-matching`), which is exactly why breaking the rule is unrecoverable: the guarantee is entirely a runtime one, and nothing would fail at compile time
- **ALWAYS** preserve RangeSet's invariant (sorted, disjoint, non-adjacent, no empties) on every code path that constructs or mutates a set
- **Do NOT** add new range types without verifying the generic engines cover them — a new type must implement `IRange<T>` + `IRangeFactory<TRange, T>` with the five sealed variants; the engines in `Internals/` dispatch per shape through the structural interfaces
- **Ranges — NEVER decide a binary operation by switching on the receiver's shape.** A binary relation is a function of the *pair* of shapes: read the bounds it actually compares, or switch on `(left, right)`. Switching on the receiver and handling the operand's shapes in an inner switch has now produced the same bug four times — `IsAdjacentTo` (fixed 6.2.1), `IsStrictlyLeftOf` and `Except` (both 7.0.0), and `RangeSet.Except(TRange)` with an infinity operand (8.0.0). The tell is an inner switch with a `_` fallback: it answers `false`, or returns the receiver unchanged, for exactly the operand shapes nobody wrote an arm for. **This applies to value-producing operations, not just predicates** — `Except` returned a well-formed range holding the wrong values, which is the harder one to notice. It hides well because the EF translation is correct, so the disagreement is between memory and the database rather than inside either. `ShapeMatrixParityTests` asks PostgreSQL for every ordered shape pair and is the check that catches it
- **Ranges — a shape dispatch's `_` arm MUST throw, never produce a value.** C# cannot prove a switch over interface patterns exhaustive, so the discard arm cannot be removed — the point is that it stops answering. Since 8.0.0 the three engines have one entry point each taking `IRange<T>` on both sides, switch over `(left, right)` with one arm per accepted pair, and throw `ShapePair.Unreachable` naming the pair for the rest, so an unhandled pair is a missing *line* and a loud failure rather than a plausible value. `EngineDispatchConventionTests` parses `src/**/Internals/` and enforces both halves — throwing discards, and entry points that are not typed by one operand's shape
- **EF Core**: new range types are wired exclusively through `RangeTypeRegistry.Register` (satellites call it from their options-builder extension); never bypass the registry
- **EF Core — a constant element operand must be typed.** The range operators are polymorphic (`anyrange @> anyelement`) and PostgreSQL resolves those without implicit coercions, so a bare numeric literal (which it types as `integer`) does not match `int8range`. Assert the whole operand in translation tests, never the `@> ` prefix, and execute at least one constant-operand query per element type against a live server
- **Value sets — ALWAYS** preserve canonical form (deduplicated, sorted by the family's canonical comparer, no nulls) on every construction path, including reads. String-backed families sort **ordinal** — never culture, never the element's own `IComparable`
- **Value sets — NEVER** implement `IEnumerable<T>` on a set type (EF would discover it as a primitive collection), and **never** let `SetTypeRegistry` match by store-type name (`text[]` belongs to the provider's native `string[]` mapping). New set types are wired exclusively through `SetTypeRegistry.Register`
- **Value sets**: `Count`/`IsEmpty` must stay instance properties (extension properties cannot appear in expression trees — CS9296 — and would be untranslatable)
- **Value sets — a set type that normalizes or validates elements in `From` MUST also override `IValueSet<T>.NormalizeElement`** (and, on the EF side, pass that same function as the definition's `normalizeValue`). `Contains`/`Add`/`Remove` take a bare element, so without it the probe is compared un-normalized against normalized storage — a silently wrong answer client-side and a wrong bound parameter server-side
- **Value sets — a set type that overrides `CanonicalComparer` MUST also override `IValueSet<T>.CanonicalOrder` to return it.** `Contains`/`Add`/`Remove` binary-search the canonical array; searching with an order the array was not sorted by misses elements that are present
- **Value sets — a set type whose element type System.Text.Json cannot serialize as a scalar MUST override `IValueSetFactory<TSet,T>.ElementJsonConverter`.** JSON delegates elements to the serializer, which property-dumps an unknown type on write and yields `default` on read — silently, both legs. The hook is consulted last (only when `GetTypeInfo(typeof(T)).Kind != None`), so it never overrides a registered converter. The primitive-backed families are natively serializable and leave it `null`; the wrapper arities and the five NodaTime sets define one. Integer-backed wrappers write a JSON **number**, not a string, and decimal-backed ones a number keeping scale — the token type must always match the primitive the wrapper wraps. Byte-for-byte parity additionally holds for the string, Guid, integer and decimal arities; for the temporal and NodaTime ones the text differs (seven fraction digits, `+` escaped) while the value does not, which is asserted in `SetJsonConverterTests` rather than fixed
- **Value sets — a wrapper arity over a type whose default text form is lossy MUST pin a round-trip format**, in `FormatValue` *and* in its `SetTypeRegistry` bridge entry, and parse it strictly. `TimeOnly.ToString(null, invariant)` is `09:30` and `DateTime`'s is `06/15/2024 10:30:00` — an arity built like `Int32Set<T>` stores every temporal truncated to the second, and loses `DateTimeKind`, silently on the way to the column. The four temporal arities and the five NodaTime ones do this; the string, Guid, integer and decimal ones correctly take the default
- **Value sets — `Union` translates to `array_cat`, which does NOT canonicalize.** Only order- and multiplicity-insensitive operators may compose on it: `@>`, `<@`, `&&`, and the proper subset/superset pair written as `<@ AND NOT @>` for exactly that reason. Everything multiplicity- or order-sensitive is refused rather than translated — `Count` in `ValueSetsMemberTranslator`, and `==`/`!=`/`Equals` in `ValueRangesQueryExpressionInterceptor`'s `UnionEqualityGuard` (8.0.0; it was the last known-wrong translation). Both refusals look *through* `array_remove`, which preserves canonical form rather than establishing it. Do not "fix" this by sorting in SQL: `ORDER BY` on `text` uses the database collation, not ordinal, so it would disagree with the client's canonical order — the same defect one layer down

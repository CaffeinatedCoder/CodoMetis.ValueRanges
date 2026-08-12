# CodoMetis.ValueRanges

In-memory range and multirange types for .NET 10, mirroring PostgreSQL's six built-in range domains (`int4range`, `int8range`, `numrange`, `daterange`, `tsrange`, `tstzrange`) plus two v5 additions: `TimeRange` over `TimeOnly` (maps to the custom `timerange` type — needs `HasPostgresRange` + `EnableUnmappedTypes` on the EF side) and, in the NodaTime satellite, `YearMonthRange` over `YearMonth` (stored as a month-aligned `daterange`). Each type is a discriminated union of five sealed variants with exhaustive pattern matching.

## Stack
.NET 10 · C# 14 (extension methods) · MSTest 4.x · EF Core + Npgsql (PostgreSQL bridge)

## Structure
- `CodoMetis.ValueRanges/` — Core library: range types, interfaces, set ops
- `CodoMetis.ValueRanges.NodaTime/` — NodaTime satellite: LocalDateRange, LocalDateTimeRange, InstantRange (namespace stays `CodoMetis.ValueRanges`; uses core internals via InternalsVisibleTo)
- `CodoMetis.ValueRanges.EFCore.PostgreSQL/` — EF Core provider for LINQ-to-SQL translation
- `CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime/` — EF satellite: registers NodaTime RangeTypeDefinitions, `UseValueRangesNodaTime()`
- `CodoMetis.ValueRanges.Tests/` — Unit tests (one file per operation)
- `CodoMetis.ValueRanges.NodaTime.Tests/` — NodaTime satellite unit tests
- `CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests/` — EF Core SQL translation tests (no database)
- `CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests/` — NodaTime EF translation tests (no database)
- `CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests/` — Live PostgreSQL via Testcontainers (needs Docker; Inconclusive without, but hard failure under CI=true). Covers BCL and NodaTime types. Authority on PostgreSQL semantics — run when changing translations or range algebra
- `docs/` — Agent docs (read relevant doc before starting work)

## Commands
```bash
dotnet build                          # Build everything
dotnet test                           # Run all tests
dotnet test --filter "ClassName=RangeContainsTests"   # Single test class
dotnet test --filter "FullyQualifiedName~Contains_FiniteRange"  # Single method
dotnet pack                           # Pack NuGet packages
```

## Workflow
1. Read `docs/architecture.md` before modifying range types or interfaces
2. Explore the codebase — range operations are per-shape (Finite, UnboundedStart, etc.)
3. Run `dotnet test` after each change; tests are method-level parallel
4. Commit with conventional format: `feat:`, `fix:`, `refactor:`, `docs:`

## Docs
- `docs/architecture.md` — Discriminated union pattern, interface hierarchy, RangeSet internals
- `docs/testing.md` — Test organization, patterns, shape-combination matrix

## Critical Rules
- **NEVER** create external subtypes of range base records — the private constructor enforces exhaustive pattern matching; breaking this removes compiler guarantees
- **ALWAYS** preserve RangeSet's invariant (sorted, disjoint, non-adjacent, no empties) on every code path that constructs or mutates a set
- **Do NOT** add new range types without verifying the generic engines cover them — a new type must implement `IRange<T>` + `IRangeFactory<TRange, T>` with the five sealed variants; the engines in `Internals/` dispatch per shape through the structural interfaces
- **EF Core**: new range types are wired exclusively through `RangeTypeRegistry.Register` (satellites call it from their options-builder extension); never bypass the registry

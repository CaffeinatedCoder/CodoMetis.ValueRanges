# CodoMetis.ValueRanges

In-memory range and multirange types for .NET 10, mirroring PostgreSQL's six built-in range domains (`int4range`, `int8range`, `numrange`, `daterange`, `tsrange`, `tstzrange`). Each type is a discriminated union of five sealed variants with exhaustive pattern matching.

## Stack
.NET 10 · C# 14 (extension methods) · MSTest 4.x · EF Core + Npgsql (PostgreSQL bridge)

## Structure
- `CodoMetis.ValueRanges/` — Core library: range types, interfaces, set ops
- `CodoMetis.ValueRanges.EFCore.PostgreSQL/` — EF Core provider for LINQ-to-SQL translation
- `CodoMetis.ValueRanges.Tests/` — Unit tests (one file per operation)
- `CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests/` — EF Core integration tests
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
- **Do NOT** add new range types without adding corresponding `IntersectEngine`/`MergeEngine` per-shape implementations in `Internals/`

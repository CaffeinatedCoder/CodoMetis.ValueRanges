# CodoMetis.ValueRanges

[![NuGet](https://img.shields.io/nuget/v/CodoMetis.ValueRanges)](https://www.nuget.org/packages/CodoMetis.ValueRanges)
[![Build & Tests](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/actions/workflows/dotnet.yml/badge.svg)](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/actions/workflows/dotnet.yml)
[![Context7](https://img.shields.io/badge/Context7-Indexed-3B82F6)](https://context7.com/caffeinatedcoder/codometis.valueranges)
[![dev.to](https://img.shields.io/badge/dev.to-Article-3B82F6)](https://dev.to/caffeinatedcoder/the-interval-is-the-thing-modelling-range-types-as-first-class-domain-objects-in-net-3jha)
[![hashnode](https://img.shields.io/badge/hashnode.dev-Article-3B82F6)](https://codometis.hashnode.dev/stop-modeling-time-with-two-columns-codometis-valueranges-brings-interval-logic-to-your-net-domain?utm_source=hashnode&utm_medium=feed)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)

Immutable **range types** (`[2025-06-01, 2025-06-08]`) and **value sets** (`{alpha, beta}`) for .NET,
carrying the complete interval and membership algebra in process — no ORM, no database driver. A
companion EF Core package translates the same calls to native PostgreSQL range, multirange and array
columns, so one expression means the same thing in a unit test and in SQL.

## Installation

```sh
dotnet add package CodoMetis.ValueRanges
```

> Requires .NET 10 or later. For LINQ-to-SQL translation, add
> [`CodoMetis.ValueRanges.EFCore.PostgreSQL`](docs/efcore.md); for NodaTime primitives, the
> [satellite packages](#nodatime).

## Quick start

```csharp
using CodoMetis.ValueRanges;

var stay = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 8));

stay.Contains(new DateOnly(2025, 6, 3));   // true
stay.Length;                               // 8 — days, counted inclusively
stay.ToString();                           // "[2025-06-01,2025-06-08]" — the PostgreSQL literal

// Unboundedness is a shape, not a sentinel: this value has no End property at all.
DateRange openEnded = DateRange.CreateUnboundedEnd(new DateOnly(2025, 6, 1));   // [2025-06-01, +∞)

// A disjoint union is a real value, not an error — RangeSet is a multirange in memory.
var blocked = stay.Union(DateRange.CreateFinite(new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 5)));
blocked.Count;        // 2
blocked.Contains(new DateOnly(2025, 6, 20));   // false — the gap
```

Value sets are the same idea one level down — canonical sets of scalar values, stored as native
PostgreSQL arrays:

```csharp
var tags = StringSet.From("beta", "alpha", "beta");   // {alpha,beta} — deduplicated, sorted
StringSet more = ["gamma", "alpha"];                  // collection expressions work

tags.Contains("alpha");        // true
tags.IsSubsetOf(more);         // false
tags.Union(more).ToString();   // "{alpha,beta,gamma}"
```

With the EF Core companion package, the same calls become SQL against a `daterange` column:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());

bookings.Where(b => b.Period.Contains(day));      // b."Period" @> @day
bookings.Where(b => b.Period.Overlaps(request));  // b."Period" && @request
bookings.OrderBy(b => b.Period.LowerBound());     // ORDER BY lower(b."Period")
```

**→ [Getting started](docs/getting-started.md)** takes this from an empty project to a first
translated query — entity, `DbContext`, migration, and the SQL that comes out — in about five
minutes.

## Five shapes, encoded in the type

Each range type is a **discriminated union** of five sealed variants:

| Variant          | Represents                  | Interval notation |
|------------------|-----------------------------|-----------------|
| `Finite`         | Bounded on both sides       | `[1, 10]`       |
| `UnboundedStart` | Unbounded on the left       | `(-∞, 10]`      |
| `UnboundedEnd`   | Unbounded on the right      | `[1, +∞)`       |
| `EmptyRange`     | The empty range (no values) | `∅`             |
| `Infinity`       | Unbounded on both ends      | `(-∞, +∞)`      |

The *shape* of a range is encoded in its static type. An `UnboundedEnd` range has no `End` property — the property does not exist at compile time. An `Empty` range carries no bound information whatsoever. Invalid states are unrepresentable by construction, and because the private base constructor admits no external subtype, these five are the only ranges that can exist — so a switch over them is complete in fact, though C# cannot prove it ([pattern matching](docs/ranges.md#pattern-matching)).

That is also what keeps *"there is no upper bound"* apart from *"the upper bound is the largest
representable value"* — see [Unboundedness is a shape, not a bound
value](docs/why.md#unboundedness-is-a-shape-not-a-bound-value).

## Supported types

| .NET type              | PostgreSQL equivalent | Element type     | Discrete |
|------------------------|-----------------------|------------------|----------|
| `Int32Range`           | `int4range`           | `int`            | ✓        |
| `Int64Range`           | `int8range`           | `long`           | ✓        |
| `DecimalRange`         | `numrange`            | `decimal`        | —        |
| `DateRange`            | `daterange`           | `DateOnly`       | ✓        |
| `DateTimeRange`        | `tsrange`             | `DateTime`       | —        |
| `DateTimeOffsetRange`  | `tstzrange`           | `DateTimeOffset` | —        |
| `TimeRange`            | `timerange` (custom)  | `TimeOnly`       | —        |

Each has a `RangeSet<TRange, T>` multirange counterpart, and there are ten value set families
(`StringSet`, `GuidSet`, `Int32Set`, … — [full table](docs/value-sets.md)) plus a validated-wrapper
arity for each, for domain types produced by Vogen, Metalama, StronglyTypedId or by hand.

Discrete types (`int`, `long`, `DateOnly`) know their step size. This matters for adjacency checks: `[1, 5]` and `[6, 10]` are adjacent for integers because there is no integer between 5 and 6.

`TimeRange` is a time-of-day range — opening hours, shifts, booking slots. A single range cannot cross midnight; a 22:00–06:00 window is two ranges, which is exactly what a two-element `RangeSet` (and its PostgreSQL multirange counterpart) represents. PostgreSQL has no built-in `timerange`, so the EF Core companion maps it to the custom type users conventionally create for this — see [TimeRange and the custom timerange type](docs/efcore.md#timerange-and-the-custom-timerange-type).

The element types are a deliberately vetted list, not a generic `IComparable<T>` constraint —
[why `double`, `float` and `Guid` are absent](docs/why.md#why-these-element-types).

### NodaTime

For projects that build on NodaTime's primitives instead of the BCL date/time types, the satellite package **[CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime)** provides the four NodaTime types that clear the same bar:

| .NET type              | PostgreSQL equivalent      | Element type     | Discrete |
|------------------------|----------------------------|------------------|----------|
| `LocalDateRange`       | `daterange`                | `LocalDate`      | ✓        |
| `LocalDateTimeRange`   | `tsrange`                  | `LocalDateTime`  | —        |
| `InstantRange`         | `tstzrange`                | `Instant`        | —        |
| `YearMonthRange`       | `daterange` (month-aligned) | `YearMonth`     | ✓        |

`YearMonthRange` (v5) is a month-granularity range for billing and reporting periods — discrete with a one-month step, so `[2024-01, 2024-03]` and `[2024-04, 2024-06]` are adjacent. It converts losslessly to the `LocalDateRange` covering exactly its months (`ToLocalDateRange()` / `ToYearMonthRange()`), which is also how the EF Core satellite stores it: as a month-aligned `daterange`, with every operator working server-side and reads validating alignment rather than silently shifting boundaries.

The same algebra, literals, JSON support and `RangeSet` multiranges apply unchanged, and the
satellite adds five value set families of its own. A companion EF Core package,
**[CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime)**,
maps them to the same PostgreSQL columns through `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
// implies UseNodaTime() and UseValueRanges() — BCL and NodaTime ranges coexist in one model
```

## Documentation

| Page | Covers |
|---|---|
| **[Getting started](docs/getting-started.md)** | From an empty project to a first translated query: entity, `DbContext`, migration, querying — and the in-memory-only path |
| **[Ranges and range sets](docs/ranges.md)** | Construction, pattern matching, the query and set algebra, `RangeSet` multiranges, the interface hierarchy |
| **[Value sets](docs/value-sets.md)** | Canonical scalar sets stored as native PostgreSQL arrays, validated wrapper elements, set ↔ range conversion |
| **[Literals, parsing, and JSON](docs/serialization.md)** | PostgreSQL literal round-trips, `ISpanParsable`, System.Text.Json registration and element converters |
| **[Entity Framework Core](docs/efcore.md)** | Mapping by convention, LINQ-to-SQL translation, the custom `timerange` type, and the exhaustive what-runs-where table |
| **[Migration guide](docs/migration.md)** | The source changes each major requires |
| **[Why this exists](docs/why.md)** | The design rationale, the landscape survey, and why the element type list is vetted rather than generic |
| **[Changelog](CHANGELOG.md)** | Every release in full, including the behavioural notes for each |

Working on the package itself: [architecture](docs/architecture.md), [testing](docs/testing.md), and
[CONTRIBUTING.md](CONTRIBUTING.md).

## Verified against PostgreSQL

The library's core promise — identical results in memory and as SQL — is enforced by three test layers:

1. **In-memory unit suite** — every operation across the full shape matrix: all 5×5 shape combinations per binary operation, the four bound-inclusiveness permutations, discrete and continuous domains, normalization invariants, and literal round-trips.
2. **Translation suite** — asserts the exact SQL generated for every LINQ construct via `ToQueryString()`, without a database.
3. **Live-PostgreSQL parity suite** — a Testcontainers-based project executes the translated SQL against a real PostgreSQL instance and asserts agreement with the in-memory results: round-trips for every range and multirange column type, the timestamp normalization and precision rules at the Npgsql boundary, and operation-level parity for the full algebra.

The live suite is the authority on semantics, and the model bends to it rather than the other way around: it is what established the discrete `upper()` canonicalization compensation, PostgreSQL's [directional multirange adjacency rule](docs/ranges.md#rangeset--multirange-support), and the confirmation that a multirange satisfying both `lower_inf` and `upper_inf` is still not the whole domain — all before any user could trip over them. The NodaTime satellite types run through the same three layers, including the `Instant` sub-microsecond precision reduction and the `±infinity` boundary mapping.

All three layers run in CI on every push and pull request — the badge at the top of this page is the current state of the whole suite, live database included. A fourth, repo-level layer checks the things that compile and pack cleanly and only fail once a package is installed: that every shipped version is documented, that each package ships its own README, and that the [value set contracts](docs/value-sets.md) hold for every set type that exists.

## Contributing and project practices

Bug reports and pull requests are welcome — [CONTRIBUTING.md](CONTRIBUTING.md) covers the setup and the quality bar this package holds itself to. Security reports go privately through [SECURITY.md](SECURITY.md).

Packages are published through GitHub Actions Trusted Publishing, carry Source Link metadata and symbol packages, and ship a CycloneDX SBOM per package, attached to each GitHub release.

A substantial portion of this codebase was written with AI assistance, under maintainer direction and review. [CONTRIBUTING.md](CONTRIBUTING.md#ai-assisted-development) explains what that means in practice, and how every change is verified before it ships.

## License

MIT — see [LICENSE](LICENSE).

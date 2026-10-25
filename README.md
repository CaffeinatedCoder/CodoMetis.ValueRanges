# CodoMetis.ValueRanges

[![Docs](https://img.shields.io/badge/docs-site-3B82F6)](https://caffeinatedcoder.github.io/CodoMetis.ValueRanges/)
[![NuGet](https://img.shields.io/nuget/v/CodoMetis.ValueRanges)](https://www.nuget.org/packages/CodoMetis.ValueRanges)
[![Build & Tests](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/actions/workflows/dotnet.yml/badge.svg)](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/actions/workflows/dotnet.yml)
[![Context7](https://img.shields.io/badge/Context7-Indexed-3B82F6)](https://context7.com/caffeinatedcoder/codometis.valueranges)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![Native AOT: core + NodaTime](https://img.shields.io/badge/Native%20AOT-core%20%2B%20NodaTime-3B82F6)](docs/serialization.md#native-aot)

Immutable **range types** (`[2025-06-01, 2025-06-08]`) and **value sets** (`{alpha, beta}`) for
.NET. You get the complete interval and membership algebra in your own process, with no ORM and no
database driver. An EF Core package runs the same calls as SQL against native PostgreSQL range,
multirange and array columns, so one expression means the same thing in a unit test and in SQL.

## Install

```sh
dotnet add package CodoMetis.ValueRanges
```

You need .NET 10 or later. To run your LINQ queries as SQL, add the EF Core package,
[`CodoMetis.ValueRanges.EFCore.PostgreSQL`](docs/efcore.md). It needs PostgreSQL 16 or later. For NodaTime types, add the
[NodaTime packages](#nodatime).

Good to know:

- **The core and NodaTime packages are Native AOT and trimming compatible.** They raise no trim or
  AOT warnings. In CI, a Native AOT app publishes against the packed packages and runs as a native
  binary. For JSON, your app needs a source-generated JSON context
  ([what it needs from you](docs/serialization.md#native-aot)).
- **The EF Core packages are not.** They build on EF Core's own reflection, and EF Core's AOT
  support is experimental.

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

Value sets are the same idea one level down. They hold scalar values, deduplicated and sorted, and
you store them in native PostgreSQL arrays:

```csharp
var tags = StringSet.From("beta", "alpha", "beta");   // {alpha,beta} — deduplicated, sorted
StringSet more = ["gamma", "alpha"];                  // collection expressions work

tags.Contains("alpha");        // true
tags.IsSubsetOf(more);         // false
tags.Union(more).ToString();   // "{alpha,beta,gamma}"
```

With the EF Core package, the same calls run as SQL against a `daterange` column:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());

bookings.Where(b => b.Period.Contains(day));      // b."Period" @> @day
bookings.Where(b => b.Period.Overlaps(request));  // b."Period" && @request
bookings.OrderBy(b => b.Period.LowerBound());     // ORDER BY lower(b."Period"); no start sorts last
```

**→ [Getting started](docs/getting-started.md)** takes you from an empty project to a first query
that runs as SQL, in about five minutes. It covers the entity, the `DbContext`, the migration, and
the SQL that comes out.

**→ [Walkthrough](docs/walkthrough.md)** follows one booking through a minimal API and PostgreSQL,
and shows what changes at each step.

## The five shapes of a range

Each range type is a **discriminated union**. A range is always one of five sealed types, called
its shape:

| Shape            | Represents                  | Interval notation |
|------------------|-----------------------------|-----------------|
| `Finite`         | Bounded on both sides       | `[1, 10]`       |
| `UnboundedStart` | Unbounded on the left       | `(-∞, 10]`      |
| `UnboundedEnd`   | Unbounded on the right      | `[1, +∞)`       |
| `EmptyRange`     | The empty range (no values) | `∅`             |
| `Infinity`       | Unbounded on both ends      | `(-∞, +∞)`      |

The shape is part of the static type, so you can't read a bound that isn't there:

- **An `UnboundedEnd` range has no `End` property.** The property does not exist at compile time.
- **An `EmptyRange` carries no bound information at all.**
- **You can't build an invalid state.** The types rule it out by construction.

These five are the only ranges that can exist. The base type's constructor is private, so no code
outside the library can add a sixth. A switch over the five is therefore complete in fact, though
C# can't prove it ([pattern matching](docs/ranges.md#pattern-matching)).

The shape is also what keeps *"there is no upper bound"* apart from *"the upper bound is the largest
representable value"*. See [Unboundedness is a shape, not a bound
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
| `Float64Range`         | `float8range` (custom) | `double`, finite only | —   |
| `Float32Range`         | `float4range` (custom) | `float`, finite only  | —   |

Each range type has a `RangeSet<TRange, T>` multirange counterpart. There are also twelve value set
families (`StringSet`, `GuidSet`, `Int32Set`, … — [full table](docs/value-sets.md)). Each family
has a version for validated wrapper types, such as the ones Vogen, Metalama or StronglyTypedId
generate, or ones you write by hand.

Discrete types (`int`, `long`, `DateOnly`) know their step size. That matters when you check
adjacency. For integers, `[1, 5]` and `[6, 10]` are adjacent, because there is no integer between 5
and 6.

`TimeRange` holds a time of day, for opening hours, shifts or booking slots.

Good to know:

- **A single `TimeRange` can't cross midnight.** A 22:00–06:00 window is two ranges. A two-element
  `RangeSet` holds exactly that, and so does its PostgreSQL multirange counterpart.
- **PostgreSQL has no built-in `timerange`.** The EF Core package maps `TimeRange` to the custom type
  that people conventionally create for this. See [TimeRange and the custom timerange
  type](docs/efcore.md#timerange-and-the-custom-timerange-type).
- **`Float64Range` and `Float32Range` hold finite numbers only.** Creating, parsing or loading a
  range with `NaN` or an infinity in it throws. Like `timerange`, PostgreSQL has no built-in
  `float8range` or `float4range`. See [Custom
  range types](docs/efcore.md#custom-range-types).
- **The element types are a deliberately vetted list, not a generic `IComparable<T>` constraint.**
  See [why these element types](docs/why.md#why-these-element-types), including why a `double` range
  is finite only and why there is no `Guid` range.

### NodaTime

If your project uses NodaTime's types instead of the BCL date and time types, add
**[CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime)**.
It provides the four NodaTime types that clear the same bar:

| .NET type              | PostgreSQL equivalent      | Element type     | Discrete |
|------------------------|----------------------------|------------------|----------|
| `LocalDateRange`       | `daterange`                | `LocalDate`      | ✓        |
| `LocalDateTimeRange`   | `tsrange`                  | `LocalDateTime`  | —        |
| `InstantRange`         | `tstzrange`                | `Instant`        | —        |
| `YearMonthRange`       | `daterange` (month-aligned) | `YearMonth`     | ✓        |

`YearMonthRange` (v5) is a range of whole months, for billing and reporting periods. It is discrete
with a one-month step, so `[2024-01, 2024-03]` and `[2024-04, 2024-06]` are adjacent. It converts
losslessly to the `LocalDateRange` that covers exactly its months, and back (`ToLocalDateRange()` /
`ToYearMonthRange()`).

The NodaTime types get the same algebra, literals, JSON support and `RangeSet` multiranges, with no
changes. The NodaTime package also adds five value set families of its own.

To store them, add the NodaTime EF Core package,
**[CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime)**.
It maps them to the same PostgreSQL columns through `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
// implies UseNodaTime() and UseValueRanges() — BCL and NodaTime ranges coexist in one model
```

Good to know:

- **`YearMonthRange` is stored as a month-aligned `daterange`.** The NodaTime EF Core package uses
  the same conversion to `LocalDateRange`, and every operator runs in the database. When it reads a
  value, it checks the month alignment instead of silently shifting the bounds.

## Documentation

| Page | Covers |
|---|---|
| **[Getting started](docs/getting-started.md)** | Go from an empty project to a first query that runs in PostgreSQL, or stay in memory with no database |
| **[Walkthrough](docs/walkthrough.md)** | Follow one booking through a minimal API and PostgreSQL, and see what changes at each step |
| **[Ranges and range sets](docs/ranges.md)** | Create ranges, match on their shape, ask questions about them and combine them, and hold several as one `RangeSet` |
| **[Value sets](docs/value-sets.md)** | Store sets of tags, codes, IDs or dates in PostgreSQL arrays, use your own element types, and convert sets to ranges |
| **[Literals, parsing, and JSON](docs/serialization.md)** | Turn ranges and sets into PostgreSQL literals and JSON and back, bind them from configuration, and publish with Native AOT |
| **[Entity Framework Core](docs/efcore.md)** | Store ranges and sets in PostgreSQL columns, run your LINQ queries as SQL, and see exactly what runs where |
| **[Migration guide](docs/migration.md)** | The changes your code needs for each new major version |
| **[Why this exists](docs/why.md)** | Why the library is shaped this way, what else exists, and why some element types are left out |
| **[Changelog](CHANGELOG.md)** | Every release in full, including what changed in behaviour |

You can also read these pages, with an API reference for all four packages, on the
[documentation site](https://caffeinatedcoder.github.io/CodoMetis.ValueRanges/). It opens on the
stable 10.x line, and the 11.x previews have pages of their own.

Two articles from June 2026 tell the story behind the library, on
[dev.to](https://dev.to/caffeinatedcoder/the-interval-is-the-thing-modelling-range-types-as-first-class-domain-objects-in-net-3jha)
and on
[Hashnode](https://codometis.hashnode.dev/stop-modeling-time-with-two-columns-codometis-valueranges-brings-interval-logic-to-your-net-domain).
They describe an earlier version, so use the guides for how it works today.

If you're working on the package itself, see [architecture](docs/architecture.md),
[testing](docs/testing.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

## Verified against PostgreSQL

The library promises you identical results in memory and as SQL. Three test layers hold it to that:

1. **In-memory unit suite.** It covers every operation across the full shape matrix: all 5×5 shape
   combinations per binary operation, the four bound-inclusiveness permutations, discrete and
   continuous element types, normalization rules, and literal round-trips.
2. **Translation suite.** It checks the exact SQL that every LINQ construct generates, through
   `ToQueryString()`, without a database.
3. **Live-PostgreSQL parity suite.** A Testcontainers-based project runs the translated SQL against
   a real PostgreSQL instance and checks that the results agree with the in-memory ones. CI runs it on
   PostgreSQL 16, the oldest supported version, and 18 for every change. It covers
   round-trips for every range and multirange column type, the timestamp normalization and precision
   rules at the Npgsql boundary, and operation-level parity for the full algebra.

The live suite is the authority on behaviour. When the model and PostgreSQL disagree, the model
changes. The live suite established three things before any user could trip over them:

- **The upper bound of a discrete range.** PostgreSQL stores `int4range`, `int8range` and
  `daterange` half-open, as `[lower, upper)`, while the model keeps them closed. So `UpperBound()`
  runs as `upper(x) - 1`.
- **The directional multirange adjacency rule.** PostgreSQL's
  [rule](docs/ranges.md#rangeset--multirange-support) for when a range is adjacent to a multirange.
- **A multirange that satisfies both `lower_inf` and `upper_inf` can still have a gap.** It is not
  the whole set of values.

The NodaTime types run through the same three layers. That includes the sub-microsecond precision
reduction for `Instant` and the `±infinity` boundary mapping.

All three layers run in CI on every push and pull request. The build badge at the top of this page
shows the current state of the whole suite, live database included.

A fourth, repo-level layer checks the things that compile and pack cleanly but fail only once a
package is installed:

- **Every shipped version is documented.**
- **Each package ships its own README.**
- **The [value set rules](docs/value-sets.md) hold for every set type that exists.**

## Contributing and project practices

Bug reports and pull requests are welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers the setup and
the quality bar this package holds itself to. Please send security reports privately, through
[SECURITY.md](SECURITY.md).

Packages are published through GitHub Actions Trusted Publishing. They carry Source Link metadata
and symbol packages. Each GitHub release has a CycloneDX SBOM attached for every package.

A substantial portion of this codebase was written with AI assistance, under the maintainer's
direction and review. [CONTRIBUTING.md](CONTRIBUTING.md#ai-assisted-development) explains what that
means in practice, and how every change is verified before it ships.

## License

MIT — see [LICENSE](LICENSE).

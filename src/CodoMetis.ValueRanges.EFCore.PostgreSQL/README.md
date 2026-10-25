# CodoMetis.ValueRanges.EFCore.PostgreSQL

This package lets you store the types of
[CodoMetis.ValueRanges](https://www.nuget.org/packages/CodoMetis.ValueRanges) in PostgreSQL with
Entity Framework Core and Npgsql. A range goes into a range column, a `RangeSet<TRange, T>` into a
multirange column, and a value set into a native array column. Your LINQ queries over them run in
the database as SQL, and they return the same results as the same code in memory.

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL
```

It needs PostgreSQL 16 or later. You turn it on with one line. You don't configure value converters,
comparers or column types:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());
```

## Which column each type gets

EF Core picks the column from the property type:

| Property type                   | Column type                              |
|---------------------------------|------------------------------------------|
| `Int32Range`                    | `int4range`                              |
| `RangeSet<Int32Range, int>`     | `int4multirange`                         |
| `DateRange`                     | `daterange`                              |
| `RangeSet<DateRange, DateOnly>` | `datemultirange`                         |
| `TimeRange`                     | `timerange` (custom type — see below)    |
| `Float64Range`                  | `float8range` (custom type — see below)  |
| `Float32Range`                  | `float4range` (custom type — see below)  |
| `Float64Set`                    | `double precision[]`                     |
| `Float32Set`                    | `real[]`                                 |
| `StringSet`                     | `text[]`                                 |
| `GuidSet`                       | `uuid[]`                                 |
| … and so on for every type      |                                          |

Sets over your own element types work too. A property such as `StringSet<AccessRight>` is
recognized from the open generic, so there is no registration per element type to forget. Plain
`string[]` and `List<Guid>` properties keep Npgsql's own array mapping, and they can sit in the same
model.

Good to know:

- **`TimeRange` needs two opt-ins.** PostgreSQL has no built-in `timerange`. You call
  `HasPostgresRange` to create the type, and `EnableUnmappedTypes` on the data source. The EF Core
  guide shows both in
  [TimeRange and the custom timerange type](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#timerange-and-the-custom-timerange-type).
- **`Float64Range` and `Float32Range` need the same two opt-ins.** Create the types with
  `HasPostgresRange("float8range", "double precision")`, without a `subtype_diff`, which overflows a
  GiST index over very large values, and `HasPostgresRange("float4range", "real")`. Rows holding `NaN` or an infinity throw when loaded. See
  [Custom range types](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#custom-range-types).
- **Sets are mapped as single values (scalars).** The package brings its own mappings and
  translators for them. EF Core's primitive-collection support is never involved.
- **Inside a `ToJson()` column, a value is stored as its PostgreSQL text.** This applies to a range,
  a multirange and a value set. A query into the document runs through a cast:
  `CAST(doc ->> 'Period' AS daterange) @> @day`.
- **That text is the database's form, not the JSON your API sends.** A value set is stored as
  `"{a,b}"`, and the `YearMonth` types as month-aligned dates. A temporal value is stored at the
  server's microsecond, truncated the way Npgsql truncates a parameter, with the extremes as
  `-infinity` and `infinity`. So the document, a column and a parameter all hold the same value.
- **`ExecuteUpdate` of a value set inside a JSON document throws.** You can set a range inside the
  document, and you can set the whole JSON property.
- **Scaffolding doesn't produce these types.** `dotnet ef dbcontext scaffold` maps range columns to
  `NpgsqlRange<T>` and array columns to plain arrays, because the package provides no design-time
  services. Change the property types yourself after scaffolding. Using these types is a decision
  about your model.

## Query ranges

```csharp
var day = new DateOnly(2024, 6, 15);

bookings.Where(b => b.Period.Contains(day));        // b."Period" @> @day
bookings.Where(b => b.Period.Overlaps(other));      // b."Period" && @other
bookings.Select(b => b.Period.Intersect(other));    // b."Period" * @other
bookings.OrderBy(b => b.Period.LowerBound());       // ORDER BY lower(b."Period"); no start sorts last
bookings.Select(b => b.Period.Merge(other));        // range_merge(b."Period", @other)
bookings.Select(b => b.BlockedDays.Gaps(month));    // datemultirange(@month) - b."BlockedDays"
bookings.Where(b => b.BlockedDays == someSet);      // b."BlockedDays" = @set

bookings.GroupBy(b => b.CustomerId)                 // range_agg(b."Period")
        .Select(g => g.Select(b => b.Period).RangeAgg());
```

Each comparison runs as one PostgreSQL operator:

| Method | Operator |
|---|---|
| `Contains` | `@>` |
| `Overlaps` | `&&` |
| `IsContainedBy` | `<@` |
| `IsStrictlyLeftOf` / `IsStrictlyRightOf` | `<<` / `>>` |
| `DoesNotExtendLeftOf` / `DoesNotExtendRightOf` | `&<` / `&>` |
| `CompareTo` against 0 | `<`, `<=`, `>`, `>=`, `=` |

`IsAdjacentTo` runs as `-|-`. All of these work on ranges and on a `RangeSet`, with either a range
or a multirange as the argument.

You can combine ranges in the database too:

- **`Intersect`** runs as `*`.
- **`Union` and `Except`** turn both sides into multiranges and run as `+` and `-`. That matches
  their `RangeSet` return type. So the union of two disjoint ranges comes back as a real
  two-element multirange, not an error.
- **The factories** `CreateFinite`, `CreateUnboundedStart` and `CreateUnboundedEnd` run as range
  constructor calls with a guard. The guard keeps the model's rule: bounds in the wrong order give
  an empty range.

The state checks map to PostgreSQL's range functions:

| Method | SQL |
|---|---|
| `IsEmpty()` | `isempty` |
| `IsUnboundedStart()` | `lower_inf` |
| `IsUnboundedEnd()` | `upper_inf` |
| `IsInfinity()` | `lower_inf AND upper_inf` |
| `IsFinite()` | `NOT lower_inf AND NOT upper_inf AND NOT isempty` |

Good to know:

- **Discrete ranges come back exactly as in memory.** PostgreSQL stores `int4range`, `int8range`
  and `daterange` half-open, as `[lower, upper)`. The model keeps them closed, as `[lower, upper]`.
  The package makes up for the difference. `UpperBound()` runs as `upper(x) - 1`, and
  `UpperBoundInclusive()` as `NOT upper_inf(x) AND NOT isempty(x)`. Results from the database
  always equal the results in memory, checked against a live PostgreSQL.
- **`LowerBound()` and `UpperBound()` return `T?`.** PostgreSQL's `lower` and `upper` return `NULL`
  when that side is unbounded or the range is empty. The in-memory methods return `null` in the
  same cases.
- **Aggregates over zero rows return `NULL` in SQL.** That is standard PostgreSQL behaviour. In
  memory, `RangeAgg()` over nothing returns the empty set. `RangeIntersectAgg()` returns `null` in
  both places.
- **`DateTimeRange` bounds are stored as wall-clock time.** They are written as `timestamp` with
  `DateTimeKind.Unspecified`. A UTC-kinded `DateTime` is *reinterpreted* as wall-clock time, not
  converted.
- **`DateTimeOffsetRange` bounds are stored in UTC.** They are normalized to UTC for `timestamptz`.
  You keep the instant, but the original offset doesn't come back.
- **`DateTime.MinValue` and `MaxValue` are stored as `-infinity` and `infinity`.** That is Npgsql's
  default rule. Such a bound is still a *finite* bound that happens to be infinite. It stays
  different from an *unbounded* side, so `upper_inf` stays `false`.

## Query value sets

```csharp
users.Where(u => u.Roles.Contains("admin"));        // u."Roles" @> ARRAY['admin']::text[]
users.Where(u => u.Roles.Overlaps(required));       // u."Roles" && @required
users.Where(u => u.Roles.IsSubsetOf(granted));      // u."Roles" <@ @granted
users.Where(u => u.Roles.Count > 2);                // cardinality(u."Roles") > 2
users.Where(u => u.Roles.Remove("admin").IsEmpty);  // cardinality(array_remove(…)) = 0
```

`Contains` always runs as `@>`, never as `= ANY`, so a plain GIN index on the column serves it.

Good to know:

- **`Union` is the one set operation whose SQL result isn't deduplicated and sorted.** It runs as
  `array_cat`, which joins two arrays without removing duplicates.
- **That is harmless in exactly two places.** The operators `@>`, `<@` and `&&` ignore duplicates. When
  you load the result, reading the set sorts and deduplicates it again.
- **Anything that depends on order or duplicates throws instead.** That covers equality, `Count`,
  `GroupBy`, `Distinct`, `OrderBy` and a value in `ExecuteUpdate`. The query throws rather than
  return a different answer from memory. The package checks this on the translated SQL.

## What runs in memory only

These operations give the right answer in memory, and an EF Core query doesn't translate them.
Some would need composed SQL that isn't written; for others, SQL would answer differently. The
table says which.
In a `Where`, the query fails to translate. It fails loudly, instead of quietly fetching the whole
table. In the final `Select`, EF Core computes them in memory after it loads the rows.

| Operation | Why |
|---|---|
| `Intersect`, `Except` on value sets | not translated: filtering the left array keeps its order, but is canonical only over canonical rows, as `Union` is |
| `Add` on value sets | inserting in order means sorting in SQL, by the database collation rather than ordinal |
| `Length` on the range types other than `Int32Range` and `Int64Range` | not translated: a temporal range open to `infinity` fails in PostgreSQL |
| `Values()` and `Values(step)` on a discrete range, `Split` on any range | not translated: each would be `generate_series` collected into an array |
| `Shift`, `Expand` on any range | not translated: rebuilding the range from `lower(x) + d` answers differently for the empty range, `time` past midnight and a bound moved past the last date .NET holds |
| `LocalDateRange.Month`, `Quarter`, `Year` and `IsoWeek`, and `YearMonthRange.Quarter` and `Year`, with an argument read from a column | not translated: a `LocalDate` at `-infinity` and the years before 4713 BC have no PostgreSQL date. Built from values your code passes in, the range is sent as one constant, so comparing a column against a month runs as SQL. On `DateRange` they run as SQL |
| `ToRangeSet()` / `ToInt32Set()` and the other bridge conversions | not translated: an array becomes a multirange by aggregating one-element ranges, and the reverse is enumeration |
| `Clamp(value)` on the range types other than `Int32Range`, `Int64Range` and `DecimalRange` | not translated: the same `LEAST`/`GREATEST` form has only been checked against memory on those three |
| the value set indexer, `set[0]` | a row another tool wrote may not be canonical, arrays start at 1, and an index past the end is `NULL` where memory throws |

Everything else on the range, multirange and array surface runs in the database. The EF Core guide
puts both halves side by side in
[What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where).

## Use NodaTime types

For NodaTime types, add
[CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime)
and call `npgsql.UseValueRangesNodaTime()` instead.

## Documentation

You can read every guide, with an API reference, on the [documentation site](https://caffeinatedcoder.github.io/CodoMetis.ValueRanges/).

- **The [EF Core guide](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)**
  shows every translation with the SQL it generates.
- **[Getting started](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/getting-started.md)**
  walks you through a first entity, migration and query.
- **The [walkthrough](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/walkthrough.md)**
  follows one booking through a minimal API and PostgreSQL, and shows what changes at each step.
- **The [changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.EFCore.PostgreSQL/CHANGELOG.md)**
  lists what changed in each version.

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

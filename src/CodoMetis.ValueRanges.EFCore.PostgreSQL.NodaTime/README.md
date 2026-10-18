# CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime

This package adds NodaTime to the
[CodoMetis.ValueRanges EF Core package](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL).
You store the range and value set types of
[CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime) in
PostgreSQL range, multirange and array columns, and query them with LINQ. Under the hood it goes
through `NpgsqlRange<T>` and native arrays, using `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`.
It needs PostgreSQL 16 or later.

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime
```

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
```

`UseValueRangesNodaTime()` turns on two things for you, so you don't call either one yourself:

- **`UseNodaTime()`**, the Npgsql NodaTime plugin. It maps the element types `LocalDate`,
  `LocalDateTime` and `Instant`.
- **`UseValueRanges()`**, the base package. Your BCL range and set types keep working in the same
  model.

It registers the range types **and** the five value set types. You don't configure anything per
property.

## Which column each type gets

### Ranges and multiranges

| Property type                            | Column type      |
|------------------------------------------|------------------|
| `LocalDateRange`                         | `daterange`      |
| `RangeSet<LocalDateRange, LocalDate>`    | `datemultirange` |
| `LocalDateTimeRange`                     | `tsrange`        |
| `RangeSet<LocalDateTimeRange, LocalDateTime>` | `tsmultirange` |
| `InstantRange`                           | `tstzrange`      |
| `RangeSet<InstantRange, Instant>`        | `tstzmultirange` |
| `YearMonthRange`                         | `daterange` (month-aligned) |
| `RangeSet<YearMonthRange, YearMonth>`    | `datemultirange` (month-aligned) |

### Value sets

The five value set types arrived in v6:

| Property type      | Column type                    |
|--------------------|--------------------------------|
| `LocalDateSet`     | `date[]`                       |
| `LocalDateTimeSet` | `timestamp without time zone[]` |
| `InstantSet`       | `timestamp with time zone[]`   |
| `LocalTimeSet`     | `time without time zone[]`     |
| `YearMonthSet`     | `date[]` (month-aligned)       |

Plain `LocalDate[]` and `List<Instant>` properties keep Npgsql's own array mapping, and they can
sit in the same model.

Good to know:

- **Scaffolding doesn't produce these types.** `dotnet ef dbcontext scaffold` still produces plain
  arrays, as with the base package. Change the property types yourself after scaffolding. Using a
  set type is a decision about your model.
- **Your own `NpgsqlDataSource` needs `UseNodaTime()` too.** When your application builds its own
  data source instead of letting EF Core create one, call `UseNodaTime()` on that data source
  builder as well. `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime` documents the same requirement.
- **A lookup by store type name alone finds Npgsql's own type.** With the Npgsql NodaTime plugin
  active, resolving a mapping by name only, such as a scaffolding-style `FindMapping("daterange")`,
  returns Npgsql's own `DateInterval` mapping. The range types always resolve by CLR type, which is
  unambiguous.

## Query ranges

The full range algebra runs in the database, exactly as in the base package:

- **The operators**: `@>`, `&&`, `<<`, `-|-`, and the rest.
- **The bound accessors**: `lower`, `upper`, `lower_inc`, `upper_inc`.
- **`Merge`**, as `range_merge`.
- **`Gap` and `Gaps`**, as multirange differences.
- **The aggregates** `RangeAgg` and `RangeIntersectAgg`.
- **`Intersect`, `Union` and `Except`.**
- **Multirange operations**, including `==` equality.
- **The factories** `CreateFinite`, `CreateUnboundedStart` and `CreateUnboundedEnd`, as range
  constructor calls with a guard.

```csharp
var day = new LocalDate(2024, 6, 15);

reservations.Where(r => r.Period.Contains(day));            // r."Period" @> DATE '2024-06-15'
reservations.OrderBy(r => r.Period.LowerBound());           // ORDER BY lower(r."Period")
reservations.Where(r => r.Window.Overlaps(other));          // r."Window" && @other
reservations.GroupBy(r => r.CustomerId)
            .Select(g => g.Select(r => r.Period).RangeAgg()); // range_agg(r."Period")
```

Good to know:

- **Discrete ranges come back exactly as in memory.** The package makes up for PostgreSQL's
  half-open `daterange` exactly as it does for `DateRange`. `LocalDateRange.UpperBound()` runs as
  `upper(x) - 1`. So results from the database always equal the results in memory, checked against
  a live PostgreSQL.

## Query value sets

The value set methods run as the array operators `@>`, `&&`, `<@`, `cardinality`, `array_remove`
and `array_cat`:

```csharp
reservations.Where(r => r.Holidays.Contains(day));           // r."Holidays" @> ARRAY['2024-06-15']::date[]
reservations.Where(r => r.Holidays.Overlaps(closures));      // r."Holidays" && @closures
reservations.Where(r => r.Holidays.IsSubsetOf(allowed));     // r."Holidays" <@ @allowed
reservations.Where(r => r.BillingMonths.Count > 2);          // cardinality(r."BillingMonths") > 2
reservations.Where(r => r.Holidays.Remove(day).IsEmpty);     // cardinality(array_remove(…)) = 0
```

`Contains` always runs as `@>`, never as `= ANY`, so a plain GIN index on the column serves it.

Good to know:

- **`Intersect`, `Except` and `Add` run in memory only.** PostgreSQL's array type has no
  intersection, difference or sorted insert. In a `Where` they fail to translate, by design. In a
  `Select`, EF Core computes them in memory after it loads the rows. The
  EF Core guide lists everything that runs in memory in
  [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where).
- **`Union` is the one set operation whose SQL result isn't deduplicated and sorted.** It runs as
  `array_cat`, which joins two arrays. That is harmless inside `@>`, `<@` and `&&`, which ignore
  duplicates. It is also harmless when you load the result, because reading the set sorts and
  deduplicates it again.
- **Anything that depends on order or duplicates throws instead.** That covers equality, `Count`,
  `GroupBy`, `Distinct`, `OrderBy` and a value in `ExecuteUpdate`. The query throws rather than
  return a different answer from memory.
- **`Remove` is safe everywhere.** `array_remove` leaves the array sorted and deduplicated.

## What gets stored

- **The value you write is the value stored.** The base package reinterprets `DateTimeKind` for
  `tsrange` and normalizes offsets to UTC for `tstzrange`. Neither happens here. `LocalDateTime` is
  wall-clock time by construction, and `Instant` is an instant by construction.
- **PostgreSQL stores microseconds.** NodaTime carries nanoseconds, so you lose any precision below
  the microsecond at the database boundary.
- **`Instant.MinValue` and `MaxValue` are stored as `-infinity` and `infinity`.** That is Npgsql's
  default rule. Such a bound is a finite bound that happens to be infinite. It stays different from
  an unbounded side, so `upper_inf` stays `false`.

### Months

- **`YearMonthRange` (v5) is stored as a month-aligned `daterange`.** `[2025-01, 2025-03]` becomes
  `[2025-01-01, 2025-04-01)`. So you need no custom database type, and every operator runs in the
  database.
- **Its bounds go through first-of-month dates.** `Contains(yearMonth)` runs as
  `@> DATE 'yyyy-MM-01'`. `UpperBound()` runs as `upper(x) - 1`, which lands on the last day of the
  end month and reads back as that month.
- **Reading a `YearMonthRange` checks month alignment.** A partial-month `daterange` throws instead
  of shifting quietly.
- **The factories can't build a `YearMonthRange` in SQL from column values.** Months are coarser
  than the `date` element type. Constant and parameter ranges work as usual.
- **`YearMonthSet` (v6) stores first-of-month dates in a `date[]`**, exactly as `YearMonthRange`
  does for its bounds. Reading it checks alignment and throws on a partial-month date, rather than
  shifting it quietly to its month.

### Other calendars

The same calendar rules apply in memory, as the
[NodaTime package README](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.NodaTime/README.md)
describes. In a query, they also reach a value you pass on its own, such as the argument to
`Contains`:

- **`LocalDateSet` and `LocalDateTimeSet` convert it to ISO before it is bound.** They hold ISO
  elements. So `Holidays.Contains(copticDate)` queries the ISO date that the Coptic date denotes.
  It doesn't read the Coptic year, month and day as if they were ISO.
- **`YearMonthSet` and `YearMonthRange` reject a non-ISO value outright.** A year-month in another
  calendar has no lossless ISO equivalent.
- **The versions over your own element type reject a non-ISO element (`ArgumentException`).**
  `LocalDateSet<T>`, `LocalDateTimeSet<T>` and `YearMonthSet<T>` can't convert your element. So a
  non-ISO one throws when you pass it on its own, as a parameter, an inlined constant or inside a
  JSON document. That matches what the set does when you create it. Before 9.0, such an element was
  bound as the ISO date its own calendar's fields spell.

## Documentation

The [EF Core guide](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)
covers both packages, with every translation and the SQL it generates.

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

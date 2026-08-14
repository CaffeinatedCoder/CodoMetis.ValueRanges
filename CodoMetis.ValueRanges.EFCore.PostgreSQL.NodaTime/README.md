# CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime

NodaTime support for the [CodoMetis.ValueRanges EF Core plugin](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL): maps the range and value-set types of [CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime) to PostgreSQL range, multirange and array columns, bridging through `NpgsqlRange<T>` and native arrays via `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`.

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

### Value sets (v6)

| Property type      | Column type                    |
|--------------------|--------------------------------|
| `LocalDateSet`     | `date[]`                       |
| `LocalDateTimeSet` | `timestamp without time zone[]` |
| `InstantSet`       | `timestamp with time zone[]`   |
| `LocalTimeSet`     | `time without time zone[]`     |
| `YearMonthSet`     | `date[]` (month-aligned)       |

## Usage

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime
```

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
```

`UseValueRangesNodaTime()` implies both `UseNodaTime()` (the Npgsql NodaTime plugin, which maps the element types `LocalDate`, `LocalDateTime` and `Instant`) and `UseValueRanges()` (the base plugin) — neither needs to be called separately, and the BCL-based range and set types keep working in the same model. It registers the range types **and** the five value set types; nothing further is configured per property.

Plain `LocalDate[]`/`List<Instant>` properties keep their native Npgsql array mapping and coexist in the same model, and scaffolding still produces plain arrays — opting into a set type is a model decision.

The full range algebra translates from LINQ to SQL exactly as documented for the base package — operators (`@>`, `&&`, `<<`, `-|-`, …), bound accessors (`lower`, `upper`, `lower_inc`, `upper_inc`), `Merge` (`range_merge`), the `RangeAgg`/`RangeIntersectAgg` aggregates, `Intersect`/`Union`/`Except`, multirange operations including `==` equality, and the `CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` factories as guarded range constructor calls:

```csharp
var day = new LocalDate(2024, 6, 15);

reservations.Where(r => r.Period.Contains(day));            // r."Period" @> DATE '2024-06-15'
reservations.OrderBy(r => r.Period.LowerBound());           // ORDER BY lower(r."Period")
reservations.Where(r => r.Window.Overlaps(other));          // r."Window" && @other
reservations.GroupBy(r => r.CustomerId)
            .Select(g => g.Select(r => r.Period).RangeAgg()); // range_agg(r."Period")
```

The value-set algebra translates to the array operators — `@>`, `&&`, `<@`, `cardinality`, `array_remove`, `array_cat`:

```csharp
reservations.Where(r => r.Holidays.Contains(day));           // r."Holidays" @> ARRAY['2024-06-15']::date[]
reservations.Where(r => r.Holidays.Overlaps(closures));      // r."Holidays" && @closures
reservations.Where(r => r.Holidays.IsSubsetOf(allowed));     // r."Holidays" <@ @allowed
reservations.Where(r => r.BillingMonths.Count > 2);          // cardinality(r."BillingMonths") > 2
reservations.Where(r => r.Holidays.Remove(day).IsEmpty);     // cardinality(array_remove(…)) = 0
```

`Contains` always translates as `@>` rather than `= ANY`, so a plain GIN index serves it. `Intersect`, `Except` and `Add` are client-side only — PostgreSQL's array type has no intersection, difference or sorted insert — and fail query translation by design.

## Notes

- **No normalization rules.** The base package documents `DateTimeKind` reinterpretation for `tsrange` and UTC offset normalization for `tstzrange`. Neither applies here: `LocalDateTime` is wall-clock time by construction and `Instant` is an instant by construction — the value written is the value stored.
- **Discrete canonicalization** is compensated exactly as for `DateRange`: `LocalDateRange.UpperBound()` translates to `upper(x) - 1`, so server results always equal the in-memory results (verified against live PostgreSQL).
- **Calendar normalization reaches bare operands.** `LocalDateSet`/`LocalDateTimeSet` hold ISO elements, so a probe in another calendar is normalized before it is bound — `Holidays.Contains(copticDate)` queries the ISO date it denotes, not that calendar's year/month/day read as if they were ISO. `YearMonthSet` and `YearMonthRange` reject a non-ISO operand outright, since a non-ISO year-month has no lossless ISO equivalent. The same rule applies in memory (see the core NodaTime README).
- **`YearMonthSet` (v6)** persists **first-of-month dates** in a `date[]`, exactly as `YearMonthRange` does for its bounds; reads validate alignment and throw on a partial-month date rather than silently shifting it to its month.
- **`Union` is the one set operation whose SQL result is not canonical** — it translates to `array_cat`, which concatenates. Harmless inside the operators above (all duplicate-insensitive) and on materialization (reads re-canonicalize), but `Count` over a union is refused rather than counting duplicates. `Remove` has no such caveat: `array_remove` leaves the array sorted and deduplicated.
- **`YearMonthRange` (v5)** is stored as a **month-aligned `daterange`**: `[2025-01, 2025-03]` becomes `[2025-01-01, 2025-04-01)`, so no custom database type is needed and every operator works server-side. Bound elements convert through first-of-month dates (`Contains(yearMonth)` → `@> DATE 'yyyy-MM-01'`); `UpperBound()`'s `upper(x) - 1` lands on the last day of the end month, which reads back as that month. Reads validate month alignment — a partial-month `daterange` throws instead of silently shifting. The factories cannot be constructed *in SQL from column values* (months are coarser than the `date` subtype); constant and parameter ranges work as usual.
- **Precision**: NodaTime carries nanoseconds, PostgreSQL stores microseconds — sub-microsecond precision is reduced at the database boundary.
- **`Instant.MinValue`/`MaxValue`** map to PostgreSQL `-infinity`/`infinity` by default (Npgsql rule): a finite bound that happens to be infinite, distinct from an unbounded side (`upper_inf` stays `false`).
- **External data sources**: when the application builds its own `NpgsqlDataSource` instead of letting EF create one, call `UseNodaTime()` on that data source builder as well — the same requirement `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime` documents.
- **Store-name lookups**: with the Npgsql NodaTime plugin active, resolving a mapping by store type name alone (e.g. scaffolding-style `FindMapping("daterange")`) is answered by Npgsql's own plugin (`DateInterval`). The range types always resolve by CLR type, which is unambiguous.
- Reverse engineering (`dotnet ef dbcontext scaffold`) does not produce these types — apply them manually after scaffolding, as with the base package.

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

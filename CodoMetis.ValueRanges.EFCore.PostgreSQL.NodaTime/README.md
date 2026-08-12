# CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime

NodaTime support for the [CodoMetis.ValueRanges EF Core plugin](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL): maps the range types of [CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime) to PostgreSQL range and multirange columns, bridging through `NpgsqlRange<T>` via `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`.

| Property type                            | Column type      |
|------------------------------------------|------------------|
| `LocalDateRange`                         | `daterange`      |
| `RangeSet<LocalDateRange, LocalDate>`    | `datemultirange` |
| `LocalDateTimeRange`                     | `tsrange`        |
| `RangeSet<LocalDateTimeRange, LocalDateTime>` | `tsmultirange` |
| `InstantRange`                           | `tstzrange`      |
| `RangeSet<InstantRange, Instant>`        | `tstzmultirange` |

## Usage

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime
```

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
```

`UseValueRangesNodaTime()` implies both `UseNodaTime()` (the Npgsql NodaTime plugin, which maps the element types `LocalDate`, `LocalDateTime` and `Instant`) and `UseValueRanges()` (the base plugin) — neither needs to be called separately, and the BCL-based range types keep working in the same model.

The full range algebra translates from LINQ to SQL exactly as documented for the base package — operators (`@>`, `&&`, `<<`, `-|-`, …), bound accessors (`lower`, `upper`, `lower_inc`, `upper_inc`), `Merge` (`range_merge`), the `RangeAgg`/`RangeIntersectAgg` aggregates, `Intersect`/`Union`/`Except`, multirange operations including `==` equality, and the `CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` factories as guarded range constructor calls:

```csharp
var day = new LocalDate(2024, 6, 15);

reservations.Where(r => r.Period.Contains(day));            // r."Period" @> DATE '2024-06-15'
reservations.OrderBy(r => r.Period.LowerBound());           // ORDER BY lower(r."Period")
reservations.Where(r => r.Window.Overlaps(other));          // r."Window" && @other
reservations.GroupBy(r => r.CustomerId)
            .Select(g => g.Select(r => r.Period).RangeAgg()); // range_agg(r."Period")
```

## Notes

- **No normalization rules.** The base package documents `DateTimeKind` reinterpretation for `tsrange` and UTC offset normalization for `tstzrange`. Neither applies here: `LocalDateTime` is wall-clock time by construction and `Instant` is an instant by construction — the value written is the value stored.
- **Discrete canonicalization** is compensated exactly as for `DateRange`: `LocalDateRange.UpperBound()` translates to `upper(x) - 1`, so server results always equal the in-memory results (verified against live PostgreSQL).
- **Precision**: NodaTime carries nanoseconds, PostgreSQL stores microseconds — sub-microsecond precision is reduced at the database boundary.
- **`Instant.MinValue`/`MaxValue`** map to PostgreSQL `-infinity`/`infinity` by default (Npgsql rule): a finite bound that happens to be infinite, distinct from an unbounded side (`upper_inf` stays `false`).
- **External data sources**: when the application builds its own `NpgsqlDataSource` instead of letting EF create one, call `UseNodaTime()` on that data source builder as well — the same requirement `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime` documents.
- **Store-name lookups**: with the Npgsql NodaTime plugin active, resolving a mapping by store type name alone (e.g. scaffolding-style `FindMapping("daterange")`) is answered by Npgsql's own plugin (`DateInterval`). The range types always resolve by CLR type, which is unambiguous.
- Reverse engineering (`dotnet ef dbcontext scaffold`) does not produce these types — apply them manually after scaffolding, as with the base package.

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

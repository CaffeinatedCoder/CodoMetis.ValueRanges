# Changelog — CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime

The changes that affect the NodaTime EF Core package. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [9.1.0] — 2026-10-17

No change in this package.

## [9.0.0] — 2026-10-11

Nothing was removed or renamed: [Migration from v8.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x).

### Breaking

- `Instant`, `LocalDateTime` and `LocalTime` in a SQL constant or JSON column carry the microsecond Npgsql binds, where they carried nine fraction digits the server rounded. The extremes (`Instant.MinValue`, `MaxValue`, and the `MinIsoValue` and `MaxIsoValue` of `LocalDate` and `LocalDateTime`) are `-infinity` and `infinity`, where they were invalid SQL such as `-9998-01-01` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- A non-ISO element of `LocalDateSet<T>`, `LocalDateTimeSet<T>` or `YearMonthSet<T>` throws as a query operand, where it was bound as the ISO date its fields spell, so a Julian January matched rows holding ISO January.

### Fixed

- The NodaTime types inside a `ToJson()` column save and query, as the core types do. `YearMonthRange`, its multirange, `YearMonthSet` and `YearMonthSet<T>` are written as month-aligned dates (`[2026-01-01,2026-06-30]`), which the `daterange` and `date[]` casts read, and reads check the alignment ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- A compiled model works through the public `YearMonthRangeJsonValueReaderWriter`, `YearMonthRangeSetJsonValueReaderWriter` and `YearMonthSetJsonValueReaderWriter`. `--nativeaot` generation isn't supported yet.
- A `LocalDateRange` or `LocalDateTimeRange` element operand in another calendar is converted to ISO, where it missed its row or threw inside Npgsql.

## [8.0.0] — 2026-08-17

No change in this package. The NodaTime ranges get the core package's `RangeSet.Except` fix.

## [7.0.0] — 2026-08-17

Nothing was removed or renamed: [Migration from v6.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x).

### Breaking

- The core package's three range fixes change what the NodaTime ranges answer in memory: `Contains` with an empty range, `IsStrictlyLeftOf` and `IsStrictlyRightOf` for a range unbounded at its start, and `Except` between ranges unbounded in opposite directions. No translation changed.

### Added

- **Mappings for the NodaTime wrapper sets**: `LocalDateSet<T>` to `date[]`, `LocalDateTimeSet<T>` to `timestamp[]`, `InstantSet<T>` to `timestamptz[]`, `LocalTimeSet<T>` to `time[]` and `YearMonthSet<T>` to a month-aligned `date[]`. `UseValueRangesNodaTime()` registers them.
- Each one writes and parses elements with its ISO pattern, so a corrupt element fails when stored or read. `YearMonthSet<T>` checks month alignment on read, as `YearMonthSet` does.

## [6.3.0] — 2026-08-16

No change in this package. `RangeSet<LocalDateRange, LocalDate>.IsFinite()` and `IsInfinity()` translate through the base package.

## [6.2.1] — 2026-08-16

No change in this package. The NodaTime ranges get the core package's `IsAdjacentTo` fix.

## [6.2.0] — 2026-08-16

No change in this package, but it inherits two changes.

### Breaking

- JSON: a `null` NodaTime range reads back as `null`, where it threw `JsonException`. See the NodaTime package's 6.2.0.

### Fixed

- `Count` over `Holidays.Union(other).Remove(d)` fails translation in a filter and runs in memory in a projection, where it counted shared elements twice.

## [6.1.0] — 2026-08-15

No change in this package.

## [6.0.0] — 2026-08-14

No breaking changes.

### Added

- **NodaTime value set columns**: `LocalDateSet` → `date[]`, `LocalDateTimeSet` → `timestamp[]`, `InstantSet` → `timestamptz[]`, `LocalTimeSet` → `time[]` (no `CREATE TYPE` needed) and `YearMonthSet` → a month-aligned `date[]`, with the set operations in SQL as for the core sets.

## [5.0.0] — 2026-08-13

No breaking changes.

### Added

- **`YearMonthRange` columns**, stored as a month-aligned `daterange`, so no custom database type is needed and every operator runs in SQL.

## [4.1.0] — 2026-08-12

The first release of this package.

### Added

- **NodaTime range columns**: `LocalDateRange` to `daterange`, `LocalDateTimeRange` to `tsrange` and `InstantRange` to `tstzrange`, and their multiranges, through `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`, with the range operations in SQL as for the core types.
- Turn it on with `npgsql.UseValueRangesNodaTime()`, which also turns on `UseNodaTime()` and `UseValueRanges()`.

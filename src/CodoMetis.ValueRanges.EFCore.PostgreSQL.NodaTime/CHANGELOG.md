# Changelog — CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime

Entries affecting the NodaTime EF Core satellite. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [6.1.0] — 2026-08-15

Version-alignment release — no changes to this package. 6.1.0 was a System.Text.Json audit
affecting the core and NodaTime packages.

## [6.0.0] — 2026-08-14

### Added

- **NodaTime value set columns** mapped to native arrays: `LocalDateSet` → `date[]`,
  `LocalDateTimeSet` → `timestamp[]`, `InstantSet` → `timestamptz[]`, `LocalTimeSet` → `time[]`
  (no `CREATE TYPE` needed, unlike `timerange`) and `YearMonthSet` → a month-aligned `date[]`.
- The set algebra translates to the array operators (`@>`, `&&`, `<@`, `cardinality`) exactly as
  for the BCL-backed sets.

No breaking changes.

## [5.0.0] — 2026-08-13

### Added

- **`YearMonthRange` storage** as a month-aligned `daterange` — no custom database type required,
  and every operator works server-side.

No breaking changes.

## [4.1.0] — 2026-08-12

Initial release of this package.

### Added

- Maps `LocalDateRange` to `daterange`, `LocalDateTimeRange` to `tsrange` and `InstantRange` to
  `tstzrange` (plus their `RangeSet<TRange, T>` multirange counterparts), bridging through
  `NpgsqlRange<T>` via `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`.
- The full range algebra translates from LINQ to SQL exactly as for the BCL-based types.
- Enabled with one line: `options.UseNpgsql(..., npgsql => npgsql.UseValueRangesNodaTime())`,
  which implies both `UseNodaTime()` and `UseValueRanges()`.

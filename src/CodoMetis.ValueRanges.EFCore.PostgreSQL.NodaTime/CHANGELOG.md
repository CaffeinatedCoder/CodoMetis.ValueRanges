# Changelog — CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime

Entries affecting the NodaTime EF Core satellite. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [6.4.0] — 2026-08-17

No source change in this package. The NodaTime range types are translated by the base plugin, so
the `IsStrictlyLeftOf`/`IsStrictlyRightOf` correction applies here too — see
`CodoMetis.ValueRanges` 6.4.0 for why in-memory and server-side evaluation disagreed for a range
unbounded at its start.

## [6.3.0] — 2026-08-16

No source change in this package. The NodaTime range sets are translated by the base plugin, so
`RangeSet<LocalDateRange, LocalDate>.IsFinite()` and `.IsInfinity()` translate here too — see
`CodoMetis.ValueRanges.EFCore.PostgreSQL` 6.3.0 for the operators and for why `IsInfinity` does not
translate to `lower_inf AND upper_inf`.

## [6.2.1] — 2026-08-16

No source change in this package. See `CodoMetis.ValueRanges` 6.2.1 for the `IsAdjacentTo`
asymmetry fix, which the NodaTime range types inherit.

## [6.2.0] — 2026-08-16

No source change in this package, but its behaviour changes with the packages it depends on.

### Changed

- **`Count` over a union reached through `Remove` is now refused rather than counted twice.** The
  NodaTime set types are translated by the base plugin's member translator, not one of their own,
  so `Holidays.Union(other).Remove(d).Count` had the same defect: `array_remove` preserves
  canonical form rather than establishing it, so `cardinality` ran over a concatenation and
  counted shared elements twice. Fixed in `CodoMetis.ValueRanges.EFCore.PostgreSQL` 6.2.0 and
  inherited here. In a predicate the expression now fails translation; in a projection it falls
  back to client evaluation and answers correctly.
- **⚠️ A null NodaTime range now reads back as `null` instead of throwing** — see
  `CodoMetis.ValueRanges.NodaTime` 6.2.0.

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

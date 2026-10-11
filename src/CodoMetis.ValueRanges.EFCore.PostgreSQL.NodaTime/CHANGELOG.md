# Changelog — CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime

Entries affecting the NodaTime EF Core satellite. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [9.0.0] — 2026-10-11

### Fixed

- **The NodaTime types inside an EF Core JSON column** could not be saved, as for the core types
  (see `CodoMetis.ValueRanges.EFCore.PostgreSQL` 9.0.0). `YearMonthRange`, its multirange and
  `YearMonthSet` are written in their store's month-aligned date form
  (`[2026-01-01,2026-06-30]`, `{2026-01-01,2026-06-01}`) rather than their own `2026-01` literal,
  because a query into the document casts the text to `daterange`/`date[]`, which only reads
  dates. Reads validate month alignment. `YearMonthRangeJsonValueReaderWriter`,
  `YearMonthRangeSetJsonValueReaderWriter` and `YearMonthSetJsonValueReaderWriter` are public for
  compiled models. The wrapper `YearMonthSet<T>` is written in the date form too; its own
  `{2026-02}` literal could not be cast, so every query into it failed.
- **NodaTime values written as text at the database's resolution.** In a JSON document and in a SQL
  constant, `Instant`, `LocalDateTime` and `LocalTime` carried all nine fraction digits, which the
  server rounds while Npgsql truncates a parameter toward 2000-01-01; and `Instant.MinValue` /
  `MaxValue`, `LocalDate` and `LocalDateTime` `MinIsoValue` / `MaxIsoValue` stayed finite where a
  parameter carries `-infinity` / `infinity` — `-9998-01-01` is not even valid SQL. Both now carry
  the value Npgsql would bind, including a `YearMonthRange` that ends in 9999-12. See
  `CodoMetis.ValueRanges.EFCore.PostgreSQL` 9.0.0.

## [8.0.0] — 2026-08-17

No source change in this package. See `CodoMetis.ValueRanges` 8.0.0 for the infinity-operand
subtraction fix and the shape-pair dispatch the NodaTime range types inherit.

## [7.0.0] — 2026-08-17

### Added

- **Mappings for the five NodaTime wrapper arities** — `LocalDateSet<T>` to `date[]`,
  `LocalDateTimeSet<T>` to `timestamp[]`, `InstantSet<T>` to `timestamptz[]`, `LocalTimeSet<T>`
  to `time[]`, and `YearMonthSet<T>` to a month-aligned `date[]`. `UseValueRangesNodaTime()`
  registers them through the base plugin's new `SetTypeRegistry.RegisterFamily` seam; nothing
  changes at the call site.

  Each family pins its ISO pattern as the format handed to the element's `IFormattable` and
  parses with the matching pattern, so a corrupt or decoratively formatted element fails loudly
  at the boundary. `YearMonthSet<T>` bridges through the first day of the month and validates the
  alignment on read, exactly as the closed `YearMonthSet` definition does.
### Changed

- **The range corrections in `CodoMetis.ValueRanges` 7.0.0 reach the NodaTime range types too** —
  they are translated by the base plugin and carry the core algebra, so empty-range containment,
  `IsStrictlyLeftOf`/`IsStrictlyRightOf` and `Except` between opposing unbounded operands all
  change what they answer in memory. No translation in this package changed; see the core
  changelog for why in-memory and server-side evaluation disagreed in each case.


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

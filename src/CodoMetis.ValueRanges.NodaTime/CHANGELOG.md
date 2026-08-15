# Changelog — CodoMetis.ValueRanges.NodaTime

Entries affecting the NodaTime satellite. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [6.1.0] — 2026-08-15

### Fixed

- **The NodaTime set types serialized their elements as objects.** With no converter registered for
  the element type, System.Text.Json's reflection path wrote
  `[{"Calendar":{…},"Year":2024,"Month":1,"Day":15,…}]` and read back `default` — silently, on both
  legs. All five sets now write ISO 8601 element forms through the family's own converter, and
  `AddRangeConverters()` alone is enough.

### Added

- `AddNodaTimeRangeConverters()` — registers the same element converters on the options, extending
  the ISO 8601 form to bare NodaTime properties sitting *alongside* a set, which the element hook
  does not reach. Idempotent, and composes with `ConfigureForNodaTime` in either registration
  order.

## [6.0.0] — 2026-08-14

### Added

- **Five NodaTime value set types** — `LocalDateSet`, `LocalDateTimeSet`, `InstantSet`,
  `LocalTimeSet` and the month-granularity `YearMonthSet`: immutable, canonical (deduplicated,
  sorted, null-free) sets with the full membership algebra, ISO-calendar normalization at
  construction, PostgreSQL array literals and JSON support.

No breaking changes.

## [5.0.0] — 2026-08-13

### Added

- **`YearMonthRange`** — a month-granularity range over NodaTime's `YearMonth` for billing and
  reporting periods. Discrete with a one-month step, so `[2024-01, 2024-03]` and `[2024-04,
  2024-06]` are adjacent and merge. Conversions to and from `LocalDateRange` and `DateInterval`
  included; non-ISO calendars are rejected at construction.

No breaking changes.

## [4.1.0] — 2026-08-12

Initial release of this package.

### Added

- `LocalDateRange` (`daterange`), `LocalDateTimeRange` (`tsrange`) and `InstantRange` (`tstzrange`)
  with the complete algebra, multiranges, literals and JSON support of the core package, plus
  conversions to and from NodaTime's own `Interval` and `DateInterval`.
- `LocalDateTime` is wall-clock time by construction and `Instant` is an instant by construction,
  so the `DateTimeKind` and offset-normalization caveats of the BCL-based types do not arise.

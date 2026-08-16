# Changelog — CodoMetis.ValueRanges.NodaTime

Entries affecting the NodaTime satellite. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [6.3.0] — 2026-08-16

### Added

- **`ISpanParsable<T>`** on `LocalDateRange`, `LocalDateTimeRange`, `InstantRange`,
  `YearMonthRange` and the five NodaTime set types, with `Parse`/`TryParse` overloads over
  `ReadOnlySpan<char>` beside the existing `string` ones. The NodaTime patterns were already
  applied to spans internally; this exposes that entry point.
- The core additions apply to the NodaTime types too, since they come from the shared generic
  surface: `RangeSet<LocalDateRange, LocalDate>` and its siblings gain `IsInfinity()`/`IsFinite()`
  and collection-expression support. See `CodoMetis.ValueRanges` 6.3.0.

## [6.2.1] — 2026-08-16

### Fixed

- **⚠️ `IsAdjacentTo` answered `false` whenever the receiver was unbounded**, and the NodaTime
  range types share the core predicate, so `LocalDateRange`, `LocalDateTimeRange`, `InstantRange`
  and `YearMonthRange` all had it — along with the `RangeSet` normalization built on top of it.
  Fixed in `CodoMetis.ValueRanges` 6.2.1 and inherited here. **Results change for any range or set
  with an unbounded element adjacent to its neighbour**, from a wrong answer to PostgreSQL's.

## [6.2.0] — 2026-08-16

No source change in this package, but its behaviour changes with the core package it depends on.

### Changed

- **⚠️ A null NodaTime range now reads back as `null` instead of throwing.** The range types here
  serialize through the core `RangeJsonConverterFactory` rather than converters of their own, so
  `LocalDateRange?`, `LocalDateTimeRange?`, `InstantRange?` and `YearMonthRange?` all had the core
  package's asymmetry: the property wrote as `null` and threw `JsonException` on the way back in.
  Fixed in `CodoMetis.ValueRanges` 6.2.0 and inherited here. `null` and `"empty"` stay distinct.
  **If you relied on the exception to reject a null where a non-nullable range was expected, that
  validation is gone.**

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

# Changelog — CodoMetis.ValueRanges.NodaTime

The changes that affect the NodaTime package. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [10.3.0]

### Changed

- Docs: each package's Project website link on NuGet opens the [documentation site](https://caffeinatedcoder.github.io/CodoMetis.ValueRanges/), and its README links the site and the [walkthrough](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/walkthrough.md).

## [10.2.0] — 2026-10-25

### Changed answers

- Ranges: `RangeAgg()` and `RangeIntersectAgg()` over `LocalDateRange`, `LocalDateTimeRange`, `InstantRange` and `YearMonthRange` skip a `null` range, as `range_agg` and `range_intersect_agg` skip a NULL row. With a `null` after another range, `RangeIntersectAgg()` returned `empty`, or threw after `(,)`, and `RangeAgg()` threw unless an input was `(,)` ([#363](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/363), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).

### Fixed

- Docs: [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where), the READMEs and the XML docs give each in-memory operation's real reason. `Shift` and `Expand` keep a bound at `infinity`, so that reason was stale. `Length`, `Values`, `Split`, the bridge conversions, `Clamp`, `Intersect` and `Except` would take composed SQL, not a missing operator. In-memory operations run in the final `Select` only.

## [10.1.0] — 2026-10-19

### Fixed

- NodaTime: `ToString` on `LocalDateSet`, `LocalTimeSet`, `LocalDateTimeSet`, `InstantSet` and `YearMonthSet`, and on their wrapper sets, runs in a `net11.0` Native AOT consumer, where the process ended with `SIGTRAP` on the first call ([Native AOT](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).

## [10.0.0] — 2026-10-19

Nothing was removed or renamed. Some calls answer differently, and text PostgreSQL refuses is refused here too: [Migration from v9.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v9x).

### Breaking

- ASP.NET Core: a minimal-API parameter typed as one shape, such as `LocalDateRange.Finite`, answers 400 for a literal of another shape, where it bound `null` and ran the handler ([Binding](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#bind-a-range-from-configuration)).
- Ranges: a record derived from a range type throws `InvalidOperationException` when it is constructed, where it compiled and `Contains` and `Overlaps` answered `false` for it ([Pattern matching](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#why-the-discard-arm-must-throw)).
- Literals: a range or multirange literal whose lower bound is above its upper bound, such as `[5,1)`, throws `FormatException`, where it read as `empty`. `CreateFinite` still returns `Empty` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).

### Added

- **`Parse` and `TryParse` on every shape** (`LocalDateRange.Finite.TryParse`, …), which accept only a literal of that shape ([Binding](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#bind-a-range-from-configuration)).
- **`IComparable` on `LocalDateRange`, `LocalDateTimeRange`, `InstantRange` and `YearMonthRange`**, in PostgreSQL's range order: empty first, then by lower bound, then by upper bound ([Sort ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#sort-ranges)).
- **A range with a `RangeSet` operand** on the four NodaTime range types, and **`RangeSet.IsContainedBy`**, as in the core package ([Compare a range with a set](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#compare-a-range-with-a-set)).
- **`RangeAgg` and `RangeIntersectAgg` over a sequence of `RangeSet`** of a NodaTime range type, through the core package's generic overloads ([Aggregates](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#aggregate-a-sequence-of-ranges)).
- **`Shift`** and **`Expand`** on the four NodaTime range types, by a `Period`, a `Duration` or a number of months. A whole month moves to a whole month ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#move-or-widen-a-range)).
- **`Split`** on the four NodaTime range types, by a `Period`, a `Duration`, a number of months or a calendar period, and **`Values(step)`** on `LocalDateRange` and `YearMonthRange` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#split-a-range-into-pieces)).
- **`LocalDateRange.Month`**, **`Quarter`**, **`Year`** and **`IsoWeek`**, and **`YearMonthRange.Quarter`** and **`Year`**. Weeks are ISO 8601 only, as `WeekYearRules.Iso` numbers them ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#create-a-month-a-quarter-a-year-or-a-week)).

### Fixed

- NodaTime: `LocalDateTimeRange` and `LocalDateTimeSet` read `Z` or an offset and keep the wall clock, where they threw. `InstantRange` and `InstantSet` refuse an offset beyond ±15:59:59 ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#dates-and-times-are-strict-iso-8601)).

## [9.1.0] — 2026-10-17

### Fixed

- Literals: whitespace at either end of a quoted or escaped range bound is dropped, as in PostgreSQL, so `[" 2026-01-01",2026-02-01)` parses, where it threw `FormatException` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: a NodaTime value set drops whitespace at either end of a quoted or escaped element, as PostgreSQL does, so `LocalDateSet.Parse("{\" 2026-01-01\"}", null)` is `{2026-01-01}`, where it threw `FormatException` ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).

## [9.0.0] — 2026-10-11

Nothing was removed or renamed. What existing calls write and accept changed: [Migration from v8.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x).

### Breaking

- JSON: `LocalDateRange`, `LocalDateTimeRange`, `InstantRange`, `YearMonthRange`, their variants, their multiranges and the five value set families write their literal with nothing registered, where they wrote a property dump that nothing could read back ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- JSON: the core package's other JSON changes apply here too: a union converter on the options handles variants and `object`, and an interface-declared property writes its literal and can't be read ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- NodaTime: a bare `LocalDate`, `LocalDateTime` or `YearMonth` in a non-ISO calendar throws `ArgumentException` on write, where a Julian 2024-01-01 was written as ISO 2024-01-01, thirteen days off. Sets and ranges are unaffected. Convert with `WithCalendar(CalendarSystem.Iso)` first ([NodaTime](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.NodaTime/README.md)).
- NodaTime: `LocalDateSet<T>`, `LocalDateTimeSet<T>` and `YearMonthSet<T>` throw `ArgumentException` for an element backed by a non-ISO value, in `From`, collection expressions, reads, and as the value passed to `Contains`, `Add` or `Remove` ([NodaTime](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.NodaTime/README.md)).
- NodaTime: `AddNodaTimeRangeConverters()` no longer outranks `ConfigureForNodaTime` when registered first. Any other converter for the type wins, in either order ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#element-converters)).
- Literals: `Parse` and `TryParse` ignore the format provider, as in the core package; the NodaTime element patterns were already fixed ISO. An empty format string, a quoted-empty range bound and backslashes in array elements follow the core package's new rules ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: the five wrapper sets parse element text with the family's ISO patterns first and hand your element the round-trip form, where they passed the text straight to the element's own `Parse` ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).

### Added

- **A `TypeConverter` on every type**, so configuration binding reads a NodaTime range or set from its literal ([TypeConverter](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **Native AOT and trimming**: the package builds with `IsAotCompatible` and is tagged `native-aot` and `trimming`, and the five wrapper sets register their own converters ([Native AOT](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).
- **The four ranges as dictionary keys**, written as the literal ([Dictionary keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **`Deconstruct`** on every `Finite` variant and on the unbounded variants of `LocalDateRange`, `LocalDateTimeRange` and `YearMonthRange`: `(start, end)` on `LocalDateRange` and `YearMonthRange`, and `(start, end, startInclusive, endInclusive)` on `LocalDateTimeRange` and `InstantRange` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`infinity` and `-infinity`** parse as the extremes of `Instant`, `LocalDate` and `LocalDateTime` (`MaxValue` and `MinValue`, `MaxIsoValue` and `MinIsoValue`), the ones Npgsql maps them to.

### Fixed

- `HH:mm` parses for a `LocalTime`, the time of a `LocalDateTime`, and an `Instant` with its offset or `Z`, where it threw `FormatException`.

## [8.0.0] — 2026-08-17

No signature changed: [Migration from v7.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v7x).

### Breaking

- `YearMonthRange.NextValueAfter` and `PreviousValueBefore` reject a non-ISO calendar, where they stepped in it and returned a value the type's constructors reject.
- `LocalDateRange`'s step functions return `null` at a Gregorian `9999-12-31`, where they threw. They now compare in ISO.
- `RangeSet.Except(TRange)` with an infinite range returns the empty set for the NodaTime ranges too, as in the core package.

## [7.0.0] — 2026-08-17

Nothing was removed or renamed: [Migration from v6.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x).

### Breaking

- The core package's three range fixes apply to the NodaTime ranges: the empty range is contained by every range, `IsStrictlyLeftOf` and `IsStrictlyRightOf` are right for a range unbounded at its start, and `Except` subtracts between ranges unbounded in opposite directions ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x)).

### Added

- **Wrapper sets**: `LocalDateSet<T>`, `LocalDateTimeSet<T>`, `InstantSet<T>`, `LocalTimeSet<T>` and `YearMonthSet<T>`, for your own element types over NodaTime values ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).

### Changed

- The wrapper sets ask their elements for the ISO pattern, since NodaTime's own text without a format is the culture's (`Saturday, 15 June 2024`). A wrapper that passes its `format` argument on needs nothing else ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- The wrapper sets can't convert an element's calendar, because the element type is yours, so ISO text is part of the element's contract. `YearMonthSet<T>` is stored as a month-aligned `date[]`, as `YearMonthSet` is.

## [6.3.0] — 2026-08-16

### Added

- **`ISpanParsable<T>`** on `LocalDateRange`, `LocalDateTimeRange`, `InstantRange`, `YearMonthRange` and the five value sets ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **`Length`**: `int?` days on `LocalDateRange`, `int?` months on `YearMonthRange`, `Duration?` on `InstantRange` and `Period?` on `LocalDateTimeRange` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`Values()`** on `LocalDateRange` and `YearMonthRange`, the two discrete NodaTime ranges.
- **`ToRangeSet()`** on `LocalDateSet` and `YearMonthSet`, and `ToLocalDateSet()` and `ToYearMonthSet()` back ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **`Clamp(value)`** on every NodaTime range, and **an indexer** on the five value sets.
- **From the core package**: `RangeSet` over a NodaTime range gets `IsInfinity()`, `IsFinite()` and collection expressions.

## [6.2.1] — 2026-08-16

### Breaking

- `IsAdjacentTo` returns `true` for an unbounded NodaTime range next to its neighbour, as in the core package, and `RangeSet` merges such neighbours ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).

## [6.2.0] — 2026-08-16

### Breaking

- JSON: a `null` `LocalDateRange`, `LocalDateTimeRange`, `InstantRange` or `YearMonthRange` reads back as `null`, where it threw `JsonException`. If you relied on that to reject a null, validate it yourself.

## [6.1.0] — 2026-08-15

### Added

- **`AddNodaTimeRangeConverters()`** registers the ISO 8601 element converters on the options, for bare NodaTime values next to a set. It works before or after `ConfigureForNodaTime`, and calling it twice is harmless.

### Fixed

- JSON: the five value sets write ISO 8601 elements, where they wrote objects such as `[{"Calendar":{…},"Year":2024,…}]` and read back `default`. `AddRangeConverters()` alone is enough.

## [6.0.0] — 2026-08-14

No breaking changes.

### Added

- **Value sets**: `LocalDateSet`, `LocalDateTimeSet`, `InstantSet`, `LocalTimeSet` and `YearMonthSet`, deduplicated, sorted and converted to the ISO calendar, with literals and JSON ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).

## [5.0.0] — 2026-08-13

No breaking changes.

### Added

- **`YearMonthRange`** over `YearMonth`, with a one-month step, so `[2024-01,2024-03]` and `[2024-04,2024-06]` are adjacent and merge. It converts to and from `LocalDateRange` and `DateInterval`, and rejects a non-ISO calendar.

## [4.1.0] — 2026-08-12

The first release of this package.

### Added

- **`LocalDateRange`** (`daterange`), **`LocalDateTimeRange`** (`tsrange`) and **`InstantRange`** (`tstzrange`), with the core package's operations, multiranges, literals and JSON, and conversions to and from NodaTime's `Interval` and `DateInterval`. A `LocalDateTime` is a wall-clock time and an `Instant` an instant, so the BCL types' `DateTimeKind` and offset caveats don't apply.

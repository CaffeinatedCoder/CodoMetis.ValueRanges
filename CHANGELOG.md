# Changelog

All notable changes to CodoMetis.ValueRanges and its packages.

The four packages share one version number (the `Version` property in `Directory.Build.props`) and
are released together, so a version appears here even when a package saw no change in it. Each
package also has its own `CHANGELOG.md` with the entries that affect it. Versions follow
[Semantic Versioning](https://semver.org/). Entries are newest first.

A version's section is the GitHub release's notes. Each line is one change: what happens now, what
to do about it, and a link to the guide with the detail. [CONTRIBUTING.md](CONTRIBUTING.md#changelog)
has the rules.

## [10.0.0] — 2026-10-17

Nothing was removed or renamed. Some calls answer differently, text that parsed is refused where PostgreSQL refuses it, and a wrapper element's text form is checked, so some calls that answered now throw: [Migration from v9.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v9x).

### Breaking

- Ranges: `IsUnboundedStart()` and `IsUnboundedEnd()` on a range answer `true` for the infinite range `(,)`, as PostgreSQL's `lower_inf` and `upper_inf`, the EF Core translation and `RangeSet` already did. They answered `false` in memory, so a query and the same predicate disagreed on every row holding `(,)`. To test for the half-unbounded shape alone, pattern-match on `IUnboundedStartRange<T>` or `IUnboundedEndRange<T>` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v9x)).
- Ranges: `RangeSet.Overlaps` with a range or a set answers `true` on a continuous domain when the operand stops exactly where a later element starts and an earlier element shares values with it, as PostgreSQL's `&&` does. `{[1,4),[5,8)}.Overlaps([3,5))` answered `false` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v9x)).
- EF Core: `Any` and `Count` over a captured list, array or other collection of ranges read it from `VALUES`, one parameter per range, and return the same answer as in memory. Without a `Contains` they read one multirange parameter, which merged overlapping and adjacent ranges and dropped empty ones. `EF.Parameter(wanted)` there throws and says to remove it ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v9x)).
- Literals: `GuidSet` and `GuidSet<T>` read PostgreSQL's `uuid_in` grammar: 32 hex digits, a hyphen allowed after any group of four, optional braces. `{6f96-19ff-8b86-d011-b42d-00c0-4fc9-64ff}` parses, where it threw; `{(6f9619ff-…)}` and whitespace inside a quoted element throw `FormatException`, where they parsed ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- Literals: `StringSet` and `StringSet<T>` refuse an element holding a NUL character (U+0000), which PostgreSQL `text` cannot hold: `From` and `Add` throw `ArgumentException`, `Parse`, the `TypeConverter` and JSON throw, and `TryParse` returns `false`, where the set was built and failed when saved ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- JSON: `Int16Set<T>`, `Int32Set<T>`, `Int64Set<T>` and `DecimalSet<T>` read a quoted number only under `JsonNumberHandling.AllowReadingFromString`, with System.Text.Json's grammar, and `GuidSet<T>` reads only the hyphenated form, exactly as the plain sets do. `["7"]`, `[" 7 "]` and `["{6f9619ff-…}"]` threw `JsonException` for the plain set and read for the wrapper ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#how-your-element-types-sort-and-serialize)).
- Value sets: every core wrapper set checks that an element's text form reads back as the same element, on every write and read, and throws naming the text-form contract. `Int32Set<T>` whose element formats `V + 1` wrote `{124}` for 123 and read back 124, silently ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#keep-the-text-form-of-the-underlying-type)).
- Literals: decimal text that names a number `decimal` can't hold, such as a 29th significant digit or `1e-30`, throws `FormatException` from `DecimalRange`, `RangeSet<DecimalRange>`, `DecimalSet` and `DecimalSet<T>`, in a literal, JSON and configuration, where it was rounded: `[1.00000000000000000000000000001,2)` read as `[1.0000000000000000000000000000,2)` and contained 1 ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).
- Literals: a range or multirange literal whose lower bound is greater than its upper bound, such as `[5,1)`, throws `FormatException` from `Parse`, JSON and the `TypeConverter`, and `TryParse` returns `false`, where it read as `empty` and vanished from a multirange. `CreateFinite` still returns `Empty` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).
- ASP.NET Core: a minimal-API parameter typed as one shape, such as `DateRange.Finite`, binds a literal of that shape and answers 400 for another shape, where it bound `null` and ran the handler for every literal that parsed. MVC was already correct ([Binding](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#bind-a-range-from-configuration)).

### Added

- **`Parse` and `TryParse` on every shape** (`Int32Range.Finite.TryParse`, …), which read only a literal of that shape and throw `FormatException` or return `false` for another ([Binding](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#bind-a-range-from-configuration)).

### Fixed

- EF Core: `Any` or `Count` with a predicate over an empty captured list of value sets, or of ranges beside a `Contains`, returns `false` or `0`, as in memory. It threw `InvalidOperationException` ("Empty collections are not supported as inline query roots") ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- Docs: an exclusive `infinity` or `-infinity` date bound excludes the date extreme, which stands for the infinity, so `[2026-01-01,infinity)` is `[2026-01-01,9999-12-30]` from a column and from the literal alike. Ask other tools to write an open end as absent or inclusive ([Domain width](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/architecture.md#domain-width-and-the-boundary)).
- Docs: a temporal literal with more than six fraction digits keeps them in memory, while PostgreSQL reads the same text rounded to the microsecond and the EF Core package stores it truncated, so the three can be 1 µs apart ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#dates-and-times-are-strict-iso-8601)).

## [9.1.0] — 2026-10-17

### Added

- EF Core: `wanted.Contains(b.Tags)` over a captured list, array or other collection of value sets runs as `IN (@p1, @p2, …)`, one parameter per set, and returns the same rows as in memory. It threw `InvalidCastException` in Npgsql. `EF.Parameter(wanted)` throws and says to remove it ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- EF Core: `wanted.Contains(b.Seats)` over a captured list, array or other collection of ranges runs as `IN (@p1, @p2, …)`, one parameter per range, and returns the same rows as in memory. It failed with `42809`, and with `InvalidCastException` for `TimeRange`. `EF.Parameter(wanted)` there throws and says to remove it ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-ranges)).

### Fixed

- Literals: whitespace at either end of a quoted or escaped range bound is dropped, as in PostgreSQL, so `[" 2026-01-01",2026-02-01)` parses, where it threw `FormatException` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: a date or time value set drops whitespace at either end of a quoted or escaped element, as PostgreSQL does, so `DateSet.Parse("{\" 2026-01-01\"}", null)` is `{2026-01-01}`, where it threw `FormatException`. A string set keeps the whitespace, as before ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- EF Core: `ExecuteUpdate` setting a value set inside a `ToJson()` column to `null` is still refused, now for its own reason: a `NULL` there would turn the whole document into `NULL`. The message blamed the JSON array a value would write. Set it with `SaveChanges`, or replace the whole JSON property ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#put-a-range-or-set-inside-a-json-column)).

## [9.0.0] — 2026-10-11

Nothing was removed or renamed. What existing calls write, accept and translate changed, and the compiler can't show you where: [Migration from v8.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x).

### Breaking

- JSON: every range, variant, `RangeSet` and value set writes its literal with nothing registered (`"[2026-01-01,2026-06-30]"`, `["a","b"]`), where it wrote a property dump such as `{"Length":181}` that nothing could read back. `AddRangeConverters()` is now redundant, and a converter on the options or on a property still wins ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- JSON: a converter you register on the options for a range type also handles values serialized through `object` or declared as a variant, which wrote the literal before ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- JSON: a property declared as `IRange<T>`, a shape interface or `IValueSet<T>` writes its runtime type's literal, where it wrote `{}` or a property dump. Reading one throws `NotSupportedException`, so declare the concrete type ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: `Parse` and `TryParse` ignore the format provider, and formatting without a format string, or with `""`, writes the invariant literal whatever the provider. `th-TH` read `2026-01-01` as the year 1483. To read culture-formatted text, parse the bounds yourself and use the factories ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: interpolating an `Int32Range`, `Int64Range` or `DecimalRange` is invariant under every culture. Under `de-DE`, `$"{range}"` wrote `[1,5,2)`, which parsed back as `[1,52)` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: temporal bounds and elements are strict ISO 8601 in `Parse`, `TryParse`, JSON, the `TypeConverter` and configuration binding. Dotted, slashed and named-month dates, a date alone for a date-time, and a `DateTimeOffset` without an offset throw `FormatException` (`[01.02.2027,05.03.2027)` read as January 2 to May 2). `HH:mm`, a space before the time and `infinity` still parse ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: a wrapper set parses element text as its underlying type first and hands your element the round-trip form, so a lenient element `Parse` no longer sees other text. An element that ignores the format specifier fails on read, where it was truncated ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- Literals: a closed temporal set reads its JSON elements with the same ISO grammar, unless you register a converter for the element type. System.Text.Json's own converter read an offset-less `DateTimeOffset` in the machine's offset ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: a `DateTime` bound or element with an offset keeps its wall clock as `Local` (`2026-01-01T09:30+05:30` is 09:30 on every machine), where it was converted into the parsing machine's zone. If you mean instants, use `DateTimeOffsetRange`, or convert with `ToUniversalTime()` first ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- Literals: decimal bounds and elements use PostgreSQL's numeric grammar, so `"1,000"`, `"(5)"` and currency symbols throw `FormatException`. This covers `DecimalRange`, `DecimalSet`, the numeric wrapper sets and their EF Core bridge (`DecimalSet<Price>.Parse("{\"1,5\"}")` was `{15}`) ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: the grammar is PostgreSQL's. A quoted-empty or blank range bound throws, where it meant unbounded (`["",5)` was `(,4]`). A backslash escapes outside quotes too (`{a\,b}` is the one element `a,b`). `{{a,b}}`, `{a}b}` and `{a"b}` throw, and only ASCII whitespace is skipped ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- NodaTime: a bare `LocalDate`, `LocalDateTime` or `YearMonth` in a non-ISO calendar throws `ArgumentException` on write, and so does such an element of `LocalDateSet<T>`, `LocalDateTimeSet<T>` or `YearMonthSet<T>`, in memory and as an EF Core operand. Both were stored thirteen days off. Convert with `WithCalendar(CalendarSystem.Iso)` first ([NodaTime](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.NodaTime/README.md)).
- NodaTime: `AddNodaTimeRangeConverters()` no longer outranks `ConfigureForNodaTime` when registered first. Any other converter for the type wins, in either order ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#element-converters)).
- EF Core: a query that builds on a server-computed value set `Union` throws `InvalidOperationException`, where it answered differently from memory: equality in a projection, `?:` or `??` around a union, `GroupBy`, `Distinct`, `OrderBy`, set operations, `Count` through `??`, and `ExecuteUpdate` to a union. Use `IsSubsetOf` and `IsSupersetOf`, or materialize first ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- EF Core: if you replace `IRelationalParameterBasedSqlProcessorFactory` after `UseValueRanges()`, every query with a server-side `Union` fails with `valueranges_union_without_sql_guard`. Wrap the registered factory instead ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- EF Core: temporal SQL constants carry the value Npgsql binds, truncated to the microsecond toward 2000-01-01, with the extremes as `-infinity` and `infinity`. A constant now matches the row a parameter matched, and a temporal set whose elements are less than a microsecond apart is stored with one of them ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).

### Added

- **A `TypeConverter` on every type**, so configuration binding reads `"Period": "[2026-01-01,2026-06-30]"`, where it bound `null` with no error. It is always invariant, an empty string is `null`, and a variant rejects a literal of another shape ([TypeConverter](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **Native AOT and trimming**: the core and NodaTime packages build with `IsAotCompatible`, and `RangeSet` and the wrapper sets no longer fail with "missing native code". A source-generated context lists a value set's element type as well as the set ([Native AOT](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).
- **Ranges and multiranges as dictionary keys**, written as the literal (`{"[2026-01-01,2026-06-30]":181}`), where they threw `NotSupportedException`. `DictionaryKeyPolicy` is not applied, and two spellings of one key collide; set `AllowDuplicateProperties = false` to reject that ([Dictionary keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **`Deconstruct` on every bounded variant**: `var (start, end) = finite` on a discrete type, and `(start, end, startInclusive, endInclusive)` on a continuous one ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **NodaTime: `infinity` and `-infinity`** parse as the extremes of `Instant`, `LocalDate` and `LocalDateTime`, the ones Npgsql maps them to ([NodaTime](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.NodaTime/README.md)).
- **Docs: an OpenAPI schema transformer** to copy into your API project. It describes ranges and multiranges as `type: string` with a pattern and value sets as arrays, where ASP.NET Core wrote `{}` ([OpenAPI](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#openapi)).

### Changed

- The range interfaces (`IRange<T>`, the shape interfaces and `IRangeFactory<TRange, T>`) are not an extension point. Implementing them outside the library is unsupported, and they may gain members in a minor version ([Architecture](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/architecture.md)).

### Fixed

- EF Core: a range, multirange or value set inside a `ToJson()` column saves, where `SaveChanges` threw `NullReferenceException`, and its operators translate (`CAST(doc ->> 'Period' AS daterange) @> @day`). The document holds the store's text, a persisted format: `"{a,b}"` for a value set, month-aligned dates for a `YearMonth` type. A compiled model works; `--nativeaot` doesn't yet ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- EF Core: `ExecuteUpdate` of a value set inside a `ToJson()` column throws, however the setter reaches it, because EF Core would store a JSON array the row could no longer load from. Setting the whole complex property still works ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- EF Core: a range built in a query from values that don't depend on it, such as `r.Period == DateRange.CreateFinite(…)`, translates, where it threw "does not have a type mapping assigned".
- EF Core: a temporal range's element operand gets the range's own mapping. `DateTimeOffsetRange.Contains(dto)` threw for any non-UTC offset, and `DateTimeRange.Contains` for a UTC `DateTime`.
- EF Core: a `LocalDateRange` or `LocalDateTimeRange` element operand in another calendar is converted to ISO, where it missed its row or threw inside Npgsql.
- EF Core: a `DateTimeSet<T>` element keeps its wall clock on the way to the column, even one the machine's time zone skips.
- NodaTime: `HH:mm` parses for a `LocalTime`, the time of a `LocalDateTime`, and an `Instant` with its offset or `Z`, as the BCL types parse it.
- `DecimalSet<T>` reads exponent notation (`{1e3}`, a JSON `1.5e1`), as `DecimalSet` does.

## [8.0.0] — 2026-08-17

No signature changed. Some existing calls answer differently, and one query that ran now throws: [Migration from v7.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v7x).

### Breaking

- EF Core: `==`, `!=` and `Equals` over a server-computed value set `Union` fail translation in a filter, where they returned wrong rows: `array_cat` keeps duplicates and the operands' order. Use `Contains`, `Overlaps`, `IsSubsetOf` or `IsSupersetOf`, or call `AsEnumerable()` first ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v7x)).
- `RangeSet.Except(TRange)` with an infinite range returns the empty set, where it returned the whole domain, and `Complement()` of the infinite set does too.
- `YearMonthRange.NextValueAfter` and `PreviousValueBefore` reject a non-ISO calendar, where they stepped in it and returned a value the type's constructors reject.
- `RangeSet.Contains(T)` on the infinite set returns `true`, where it threw `InvalidOperationException`. `LocalDateRange`'s step functions return `null` at a Gregorian `9999-12-31`, where they threw. Only code that caught these exceptions is affected.

### Added

- **Docs: a getting-started guide**, and the migration guide covers v7 and v8 ([Getting started](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/getting-started.md)).

### Changed

- An `IRange<T>` that is none of the five shapes makes `IRangeFactory.ToString` throw, where it was formatted as `"empty"` and stored as the empty range.
- A shape dispatch with no case for a pair of shapes throws `UnreachableException` naming the pair, instead of returning a value. No reachable call changed.
- EF Core: the wrapper-set bridge rejects an element type it doesn't know, instead of writing its `ToString()`.

### Fixed

- Docs: a switch over the five range shapes needs a `_` arm, which should throw. The docs said the compiler checks the cases, and the example failed to build with CS8509 ([Pattern matching](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- Docs: the value set table lists a wrapper set for every family, and the EF Core README's links reach `docs/efcore.md`.

## [7.0.0] — 2026-08-17

Nothing was removed or renamed. Three range fixes change what existing calls answer in memory: [Migration from v6.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x).

### Breaking

- `Contains` and `IsContainedBy` treat the empty range as contained by every range, as PostgreSQL's `@>` does, so `[1,5].Contains(Int32Range.Empty)` is `true`. For the old meaning, write `outer.Contains(inner) && !inner.IsEmpty()` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x)).
- `IsStrictlyLeftOf` and `IsStrictlyRightOf` give PostgreSQL's `<<` and `>>` answer for a range unbounded at its start: `(,5]` is left of `[10,20]`, where it returned `false`. A one-element `RangeSet` changes with it.
- `Except` subtracts when the two ranges are unbounded in opposite directions: `(,5].Except([1,))` is `{(,0]}`, where it returned `(,5]` unchanged. `RangeSet.Except` changes with it.
- JSON: `Int16Set<T>`, `Int32Set<T>`, `Int64Set<T>` and `DecimalSet<T>` honour `JsonNumberHandling.WriteAsString` and write strings, as their primitive sets do. Reads are unchanged ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x)).
- `DecimalRange.Length` returns `null` for a span wider than `decimal`, where it threw `OverflowException`.

### Added

- **A wrapper set for every family**: `Int16Set<T>`, `DecimalSet<T>`, `DateSet<T>`, `TimeSet<T>`, `DateTimeSet<T>` and `DateTimeOffsetSet<T>`, and in the NodaTime package `LocalDateSet<T>`, `LocalDateTimeSet<T>`, `InstantSet<T>`, `LocalTimeSet<T>` and `YearMonthSet<T>`, each mapped by EF Core to its array column. `DecimalSet<T>` writes JSON numbers with their scale ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **`SetTypeRegistry.RegisterFamily`**, for a package that adds wrapper set families.

### Changed

- The temporal wrapper sets ask their elements for a round-trip format (`yyyy-MM-dd` for `DateSet<T>`, `O` for the others, the ISO pattern in NodaTime), so a hand-written element must pass its `format` argument on. One that ignores it is rejected when stored ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- JSON: a temporal wrapper set writes the same value as its primitive set but not the same bytes: seven fraction digits, and `+` escaped. Give the element its own `[JsonConverter]` if the response shape matters ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- EF Core: `DateTimeSet<T>` and `DateTimeOffsetSet<T>` normalize as their closed sets do: `Unspecified` for `timestamp`, UTC for `timestamptz`.
- EF Core docs: `Count` on a value set, like `==`, assumes every row is canonical. A row another tool stored as `{b,a,b}` counts 3 in SQL and loads as `{a,b}`. No behaviour changed ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).

### Fixed

- EF Core: `Int64Range.Contains(25)` with a constant runs, where PostgreSQL rejected it (`42883: operator does not exist: int8range @> integer`). The constant now carries `::bigint`, and no other SQL changed.

## [6.3.0] — 2026-08-16

### Added

- **`RangeSet.IsInfinity()` and `IsFinite()`**. `IsInfinity()` is true only for the set that covers the whole domain, which `{(,5],[10,)}` is not, though it is unbounded at both ends. Both translate ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **Collection expressions for `RangeSet<TRange, T>`** (`RangeSet<Int32Range, int> set = [a, b];`), normalized as `From` normalizes. Also `From(params ReadOnlySpan<TRange>)` and the builder `RangeSet.Create<TRange, T>` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`ISpanParsable<T>`** on every range type, `RangeSet` and every value set: `Parse` and `TryParse` over a `ReadOnlySpan<char>` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **`Length`** on every range: a count for a discrete type, both ends included, and a span for a continuous one. It is `0` for the empty range and `null` when unbounded, and runs in memory only ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`Values()`** on the discrete ranges (`Int32Range`, `Int64Range`, `DateRange`, `LocalDateRange`, `YearMonthRange`) lists the values in order. An unbounded range throws at the call ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`ToRangeSet()`** on `Int32Set`, `Int64Set`, `DateSet`, `LocalDateSet` and `YearMonthSet`, and `ToInt32Set()`, `ToDateSet()` and the others back: `{1,2,3,7}` becomes `{[1,3],[7,7]}`. In memory only ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **`Clamp(value)`** on every range: the contained value nearest the argument, or `null` for the empty range ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **An indexer** on every value set (`set[0]`).
- **`IRangeFactory<TRange, T>.IsDiscrete`**, for generic code that needs to know whether the element type has a step.
- **EF Core: `RangeSet.IsFinite()` and `IsInfinity()` translate**, to `NOT lower_inf(x) AND NOT upper_inf(x) AND NOT isempty(x)` and to equality with `'{(,)}'` ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).

## [6.2.1] — 2026-08-16

### Breaking

- `IsAdjacentTo` returns `true` for an unbounded range next to its neighbour, as PostgreSQL's `-|-` does: `(,0].IsAdjacentTo([1,3])` returned `false`. `RangeSet.From` and `Union` now merge such neighbours, so `From([(,0], [1,)])` is `{(,)}` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- A literal that fails to parse, through `Parse` or JSON, quotes at most 64 characters of the input and its length, and no longer chains the element parser's exception. If you match the message or read `InnerException`, update that. Exception types are unchanged.

## [6.2.0] — 2026-08-16

### Breaking

- JSON: a `null` range reads back as `null`, where it threw `JsonException`. If you relied on that to reject a null, validate it yourself. `null` and `"empty"` stay different.

### Fixed

- EF Core: `Count` over `Union(…).Remove(x)` fails translation in a filter and runs in memory in a projection, where it counted shared elements twice.

## [6.1.0] — 2026-08-15

### Breaking

- JSON: a wrapper set whose element has no `[JsonConverter]` writes the underlying values (`["users.read"]`, `[1,2]`), where it wrote objects such as `[{"Value":"users.read"}]` or `[{}]`. Reads run the element's `Parse`, and a registered converter still wins ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).

### Added

- **`IValueSetFactory<TSet, T>.ElementJsonConverter`**, the element converter a set uses when none is registered.
- **`RangeVariantJsonConverter<TVariant, TRange, T>`**.
- **NodaTime: `AddNodaTimeRangeConverters()`**, for bare NodaTime values next to a set. It works before or after `ConfigureForNodaTime`.

### Fixed

- JSON: the NodaTime sets write ISO 8601 elements, where they wrote objects and read back `default`.
- JSON: a `null` range property is written as `null`, where it threw `NullReferenceException`. Reading `null` still throws; write `"empty"` for the empty range.
- JSON: a range serialized through `object` works, where it threw `ArgumentException`.

## [6.0.0] — 2026-08-14

Value sets, a second type family. No breaking changes.

### Added

- **Value sets**: `StringSet`, `GuidSet`, `Int16Set`, `Int32Set`, `Int64Set`, `DecimalSet`, `DateSet`, `TimeSet`, `DateTimeSet` and `DateTimeOffsetSet`, deduplicated and sorted, with no nulls. The wrapper sets `StringSet<T>`, `GuidSet<T>`, `Int32Set<T>` and `Int64Set<T>` hold your own element types and need only BCL interfaces on them ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **NodaTime value sets**: `LocalDateSet`, `LocalDateTimeSet`, `InstantSet`, `LocalTimeSet` and `YearMonthSet`.
- **Set operations**: `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, `IsProperSubsetOf`, `IsProperSupersetOf`, `Union`, `Remove`, `Count` and `IsEmpty`, and `Intersect`, `Except` and `Add` in memory only. Also array literals, JSON and collection expressions.
- **EF Core: value set columns** as native arrays (`StringSet` → `text[]`, …), with wrapper sets found automatically. The operations translate to `@>`, `&&`, `<@`, `cardinality`, `array_cat` and `array_remove`; `Contains` is `@>`, so a GIN index serves it. `Count` over a `Union` fails translation ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).

## [5.0.0] — 2026-08-13

New range types beyond PostgreSQL's built-ins. No breaking changes.

### Added

- **`TimeRange`** over `TimeOnly`, for the custom `timerange` type. EF Core needs `HasPostgresRange` and `EnableUnmappedTypes` for it ([TimeRange](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#timerange-and-the-custom-timerange-type)).
- **NodaTime: `YearMonthRange`** over `YearMonth`, stored as a month-aligned `daterange`, with conversions to and from `LocalDateRange` and `DateInterval`. A non-ISO calendar is rejected.

## [4.1.0] — 2026-08-12

### Added

- **`CodoMetis.ValueRanges.NodaTime`**: `LocalDateRange` (`daterange`), `LocalDateTimeRange` (`tsrange`) and `InstantRange` (`tstzrange`), with conversions to and from NodaTime's `Interval` and `DateInterval`.
- **`CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime`**: maps them through `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`, turned on with `npgsql.UseValueRangesNodaTime()`.

### Changed

- EF Core: range types are registered through `RangeTypeRegistry.Register`, so other packages can add theirs. Your code doesn't change.

## [4.0.0] — 2026-08-10

Every PostgreSQL range and multirange operator now runs in memory and in SQL: [Migration from v3.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v3x).

### Breaking

- `==` and `!=` on `RangeSet` compare values, as the range records and SQL `=` do. Use `ReferenceEquals` where you meant identity.
- `DoesNotExtendRightOf` and `DoesNotExtendLeftOf` match PostgreSQL for infinite bounds: `+∞ ≤ +∞`, so an unbounded range no longer always returns `false`.

### Added

- **Bound accessors**: `LowerBound()` and `UpperBound()` return `T?`, `null` where PostgreSQL returns `NULL`, and `LowerBoundInclusive()` and `UpperBoundInclusive()`, on ranges and `RangeSet`. EF Core translates them to `lower`, `upper`, `lower_inc` and `upper_inc`.
- **`Merge`**: the smallest range covering both, gap included (`range_merge`).
- **Aggregates**: `RangeAgg()` and `RangeIntersectAgg()`, translated to `range_agg` and `range_intersect_agg` inside a `GroupBy`.
- **`RangeSet` operators**: `Contains`, `Overlaps`, `IsAdjacentTo`, `IsStrictlyLeftOf`, `IsStrictlyRightOf`, `DoesNotExtendLeftOf`, `DoesNotExtendRightOf`, `IsEmpty()`, `IsUnboundedStart()` and `IsUnboundedEnd()`, each translated to its multirange operator.

### Fixed

- `RangeSet.Infinite.Contains(range)` and `Overlaps(range)` work for a range with a finite bound, where they threw `InvalidOperationException`.

## [3.1.0] — 2026-06-17

### Added

- **`RangeSet<TRange, T>.LowerBoundComparer`**, the set's lower-bound ordering as an `IComparer<TRange>`, also as `RangeLowerBoundComparer<TRange, T>.Instance`.

### Changed

- `RangeSet` is faster: `Contains` and `Overlaps` are O(log n), and `Union`, `Intersect` and `Except` merge two sorted lists. No result changed.

### Fixed

- A quoted range bound unescapes `\"` and `\\`, so an element whose text holds a quote or a backslash round-trips.

## [3.0.0] — 2026-06-11

[Migration from v2.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v2x).

### Breaking

- `IsEmpty`, `IsFinite`, `IsInfinity`, `IsUnboundedStart` and `IsUnboundedEnd` are methods, so add parentheses at every call. As methods they translate to SQL.

### Added

- **`CodoMetis.ValueRanges.EFCore.PostgreSQL`**: maps the range types to PostgreSQL range and multirange columns and translates the range operations to SQL, turned on with `npgsql.UseValueRanges()` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).

## [2.0.0] — 2026-06-11

[Migration from v1.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v1x).

### Breaking

- `ToString()` returns the PostgreSQL literal (`[1,10]`), where it returned the record's `Finite { Start = 1, … }`. Update code that logs, displays or compares that text.

### Added

- **`Parse` and `TryParse`** for PostgreSQL range literals.

## [1.0.0] — 2026-06-10

The first release.

### Added

- **The six PostgreSQL range types** as unions of five shapes (`Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange`, `Infinity`), with the range operations and `RangeSet<TRange, T>`, a multirange that keeps itself normalized.

[9.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v9.0.0
[8.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v8.0.0
[7.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v7.0.0
[6.3.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.3.0
[6.2.1]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.2.1
[6.2.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.2.0
[6.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.1.0
[6.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.0.0
[5.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v5.0.0
[4.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v4.1.0
[4.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v4.0.0
[3.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v3.1.0
[3.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v3.0.0
[2.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v2.0.0
[1.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v1.0.0

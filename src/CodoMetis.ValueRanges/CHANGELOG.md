# Changelog — CodoMetis.ValueRanges

The changes that affect the core package. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [10.2.0]

### Changed answers

- Ranges: `RangeAgg()` and `RangeIntersectAgg()` skip a `null` range or set, as `range_agg` and `range_intersect_agg` skip a NULL row. With a `null` after another range, `RangeIntersectAgg()` returned `empty`, or threw after `(,)`, and `RangeAgg()` threw unless an input was `(,)` ([#363](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/363), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).
- Ranges: `RangeSet.From`, a collection expression and `RangeSet.Create` throw `ArgumentException` for a `null` range, where they built a set holding `null`, whose `IsEmpty()` was `false`. `Union` and `Except` called on a `null` range throw too ([#357](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/357), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).
- Ranges: a predicate called on a `null` range, or given a `null` range or set, returns `false`, as a query does over a NULL column. `Contains(Empty)`, `IsContainedBy((,))` and `(,).Contains(null)` returned `true`, and `Contains(T)` and a `null` set operand threw ([#357](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/357), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).

### Fixed

- Docs: [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where), the READMEs and the XML docs give each in-memory operation's real reason. `Shift` and `Expand` keep a bound at `infinity`, so that reason was stale. `Length`, `Values`, `Split`, the bridge conversions, `Clamp`, `Intersect` and `Except` would take composed SQL, not a missing operator. In-memory operations run in the final `Select` only.

## [10.1.0] — 2026-10-19

One call answers differently, on the .NET 11 runtime only: [Changed answers in 10.1.0](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1010).

### Changed answers

- Literals: a time, `DateTime` or `DateTimeOffset` element with an hour of 24 throws `FormatException` on the .NET 11 runtime too, where it returned midnight: `{24:00:00}` read as `{00:00:00}` and `T24:00:00` as the next day. On .NET 10 it always threw ([#300](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/300), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1010)).

### Fixed

- Value sets: `ToString` on every value set runs in a `net11.0` Native AOT consumer, where the process ended with `SIGTRAP` on the first call ([Native AOT](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).

## [10.0.0] — 2026-10-19

Nothing was removed or renamed. Some calls answer differently, and text PostgreSQL refuses is refused here too: [Migration from v9.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v9x).

### Breaking

- Ranges: `IsUnboundedStart()` and `IsUnboundedEnd()` return `true` for the infinite range `(,)`, as PostgreSQL and `RangeSet` do, where they returned `false` in memory. Match `IUnboundedStartRange<T>` or `IUnboundedEndRange<T>` for the half-unbounded shape alone ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-isunboundedstart-and-isunboundedend-on-the-infinite-range)).
- Ranges: `RangeSet.Overlaps` on a continuous domain returns `true` when the operand ends where a later element starts and shares values with an earlier one, as `&&` does. `{[1,4),[5,8)}.Overlaps([3,5))` returned `false` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-rangesetoverlaps-on-a-continuous-element-type)).
- Ranges: a record derived from a range type throws `InvalidOperationException` when it is constructed, where it compiled and `Contains` and `Overlaps` answered `false` for it ([Pattern matching](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#why-the-discard-arm-must-throw)).
- Value sets: every core wrapper set checks that an element's text form reads back as the same element, and throws if it doesn't, where the element was stored as a different value ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#keep-the-text-form-of-the-underlying-type)).
- Literals: integer or decimal text with a NUL throws `FormatException`, in a range, a set and JSON, where a trailing NUL was ignored and `31\0` read as 31 ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#keep-nul-out-of-string-elements)).
- Literals: `StringSet` and `StringSet<T>` refuse an element holding a NUL: `From` and `Add` throw `ArgumentException` and parsing fails, where the set was built and failed when saved ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- Literals: `GuidSet` and `GuidSet<T>` read PostgreSQL's `uuid_in` grammar. `{6f96-19ff-8b86-d011-b42d-00c0-4fc9-64ff}` parses, where it threw; `{(6f9619ff-…)}` and whitespace inside a quoted element throw `FormatException`, where they parsed ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- Literals: a `DateTimeOffset` offset other than `Z`, `±HH`, `±HH:mm` or `±HH:mm:ss` throws `FormatException`, where `+0530` and `+5:30` were read ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#write-temporal-text-as-strict-iso-8601)).
- Literals: decimal text with more precision than `decimal` holds, such as a 29th significant digit or `1e-30`, throws `FormatException`, where it was rounded ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).
- Literals: a range or multirange literal whose lower bound is above its upper bound, such as `[5,1)`, throws `FormatException`, where it read as `empty`. `CreateFinite` still returns `Empty` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).
- JSON: the integer, decimal and Guid wrapper sets read only the JSON their plain sets read: a quoted number needs `AllowReadingFromString`, and a Guid is hyphenated ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#how-your-element-types-sort-and-serialize)).
- ASP.NET Core: a minimal-API parameter typed as one shape, such as `DateRange.Finite`, answers 400 for a literal of another shape, where it bound `null` and ran the handler ([Binding](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#bind-a-range-from-configuration)).

### Added

- **`Float64Range`, `Float64Set` and `Float64Set<T>`** over finite `double` values. `NaN` and the infinities throw, and the text is what PostgreSQL's `float8out` prints ([Why doubles are finite only](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md#why-doubles-are-finite-only)).
- **`Float32Range`, `Float32Set` and `Float32Set<T>`** over finite `float` values, with the text `float4out` prints. There is no `Half` family, as PostgreSQL has no two-byte float ([Why doubles are finite only](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md#why-doubles-are-finite-only)).
- **`IComparable` on every range type and on `RangeSet`**, in PostgreSQL's range and multirange order: empty first, then by lower bound, then by upper bound ([Sort ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#sort-ranges)).
- **`Parse` and `TryParse` on every shape** (`Int32Range.Finite.TryParse`, …), which accept only a literal of that shape ([Binding](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#bind-a-range-from-configuration)).
- **A range with a `RangeSet` operand** for `Contains`, `Overlaps`, `IsAdjacentTo` and the other range operators, and **`RangeSet.IsContainedBy`** ([Compare a range with a set](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#compare-a-range-with-a-set)).
- **`RangeAgg` and `RangeIntersectAgg` over a sequence of `RangeSet`**, as PostgreSQL's `range_agg` and `range_intersect_agg` over multiranges ([Aggregates](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#aggregate-a-sequence-of-ranges)).
- **`Gap`** between two ranges, and **`Gaps()`** and **`Gaps(window)`** on a `RangeSet`, in memory and in EF Core queries ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#find-the-gap-between-two-ranges)).
- **`Shift`** and **`Expand`** on every range type, which move a range by a step or grow it on both sides, in memory only ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#move-or-widen-a-range)).
- **`Split`** on every range type, by a step or at calendar period boundaries, and **`Values(step)`** on the discrete types, in memory only ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#split-a-range-into-pieces)).
- **`DateRange.Month`**, **`Quarter`**, **`Year`** and **`IsoWeek`**, by number or by any day inside the period. Weeks are ISO 8601 only, whatever the culture ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#create-a-month-a-quarter-a-year-or-a-week)).

### Fixed

- Literals: integer text reads PostgreSQL 16's `0x`, `0o` and `0b` prefixes and underscores between digits, where `[0x1F,)` and `{1_000}` threw `FormatException` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).
- Literals: value sets read a dimension decoration such as `[0:1]={1,2}`, and `{{}}` as `{}` as PostgreSQL 17 does, where both threw `FormatException` ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- Literals: `DateTime` and `DateTimeOffset` text reads an offset up to ±15:59:59, as PostgreSQL does, where `+15:00` threw `FormatException` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#dates-and-times-are-strict-iso-8601)).
- Docs: an exclusive `infinity` date bound excludes the date extreme, so `[2026-01-01,infinity)` is `[2026-01-01,9999-12-30]` ([Domain width](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/architecture.md#domain-width-and-the-boundary)).
- Docs: a temporal literal with more than six fraction digits can be 1 µs from what PostgreSQL reads and the EF Core package stores ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#dates-and-times-are-strict-iso-8601)).

## [9.1.0] — 2026-10-17

### Fixed

- Literals: whitespace at either end of a quoted or escaped range bound is dropped, as in PostgreSQL, so `[" 2026-01-01",2026-02-01)` parses, where it threw `FormatException` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: a date or time value set drops whitespace at either end of a quoted or escaped element, as PostgreSQL does, so `DateSet.Parse("{\" 2026-01-01\"}", null)` is `{2026-01-01}`, where it threw `FormatException`. A string set keeps the whitespace, as before ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).

## [9.0.0] — 2026-10-11

Nothing was removed or renamed. What existing calls write and accept changed: [Migration from v8.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x).

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
- Literals: decimal bounds and elements use PostgreSQL's numeric grammar, so `"1,000"`, `"(5)"` and currency symbols throw `FormatException`. This covers `DecimalRange`, `DecimalSet` and the numeric wrapper sets (`DecimalSet<Price>.Parse("{\"1,5\"}")` was `{15}`) ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: the grammar is PostgreSQL's. A quoted-empty or blank range bound throws, where it meant unbounded (`["",5)` was `(,4]`). A backslash escapes outside quotes too (`{a\,b}` is the one element `a,b`). `{{a,b}}`, `{a}b}` and `{a"b}` throw, and only ASCII whitespace is skipped ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).

### Added

- **A `TypeConverter` on every type**, so configuration binding reads `"Period": "[2026-01-01,2026-06-30]"`, where it bound `null` with no error. It is always invariant, an empty string is `null`, and a variant rejects a literal of another shape ([TypeConverter](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **Native AOT and trimming**: the package builds with `IsAotCompatible` and is tagged `native-aot` and `trimming`, and `RangeSet` and the wrapper sets no longer fail with "missing native code". A source-generated context lists a value set's element type as well as the set ([Native AOT](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).
- **Ranges and multiranges as dictionary keys**, written as the literal (`{"[2026-01-01,2026-06-30]":181}`), where they threw `NotSupportedException`. `DictionaryKeyPolicy` is not applied, and two spellings of one key collide; set `AllowDuplicateProperties = false` to reject that ([Dictionary keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **`Deconstruct` on every bounded variant**: `var (start, end) = finite` on a discrete type, and `(start, end, startInclusive, endInclusive)` on a continuous one ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **Docs: an OpenAPI schema transformer** to copy into your API project. It describes ranges and multiranges as `type: string` with a pattern and value sets as arrays, where ASP.NET Core wrote `{}` ([OpenAPI](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#openapi)).

### Changed

- The range interfaces (`IRange<T>`, the shape interfaces and `IRangeFactory<TRange, T>`) are not an extension point. Implementing them outside the library is unsupported, and they may gain members in a minor version ([Architecture](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/architecture.md)).

### Fixed

- `DecimalSet<T>` reads exponent notation (`{1e3}`, a JSON `1.5e1`), as `DecimalSet` does.

## [8.0.0] — 2026-08-17

No signature changed. Some existing calls answer differently: [Migration from v7.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v7x).

### Breaking

- `RangeSet.Except(TRange)` with an infinite range returns the empty set, where it returned the whole domain, and `Complement()` of the infinite set does too.
- `RangeSet.Contains(T)` on the infinite set returns `true`, where it threw `InvalidOperationException`. Only code that caught the exception is affected.

### Changed

- An `IRange<T>` that is none of the five shapes makes `IRangeFactory.ToString` throw, where it was formatted as `"empty"` and stored as the empty range.
- A shape dispatch with no case for a pair of shapes throws `UnreachableException` naming the pair, instead of returning a value. No reachable call changed.

### Fixed

- Docs: a switch over the five range shapes needs a `_` arm, which should throw. This package's README said the compiler checks the cases, and the example failed to build with CS8509 ([Pattern matching](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).

## [7.0.0] — 2026-08-17

Nothing was removed or renamed. Three range fixes change what existing calls answer: [Migration from v6.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x).

### Breaking

- `Contains` and `IsContainedBy` treat the empty range as contained by every range, as PostgreSQL's `@>` does, so `[1,5].Contains(Int32Range.Empty)` is `true`. For the old meaning, write `outer.Contains(inner) && !inner.IsEmpty()` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x)).
- `IsStrictlyLeftOf` and `IsStrictlyRightOf` give PostgreSQL's `<<` and `>>` answer for a range unbounded at its start: `(,5]` is left of `[10,20]`, where it returned `false`. A one-element `RangeSet` changes with it.
- `Except` subtracts when the two ranges are unbounded in opposite directions: `(,5].Except([1,))` is `{(,0]}`, where it returned `(,5]` unchanged. `RangeSet.Except` changes with it.
- JSON: `Int16Set<T>`, `Int32Set<T>`, `Int64Set<T>` and `DecimalSet<T>` honour `JsonNumberHandling.WriteAsString` and write strings, as their primitive sets do. Reads are unchanged ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x)).
- `DecimalRange.Length` returns `null` for a span wider than `decimal`, where it threw `OverflowException`.

### Added

- **A wrapper set for every family**: `Int16Set<T>`, `DecimalSet<T>`, `DateSet<T>`, `TimeSet<T>`, `DateTimeSet<T>` and `DateTimeOffsetSet<T>`. `DecimalSet<T>` writes JSON numbers with their scale ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).

### Changed

- The temporal wrapper sets ask their elements for a round-trip format (`yyyy-MM-dd` for `DateSet<T>`, `O` for the others), so a hand-written element must pass its `format` argument on. One that ignores it is rejected when stored ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- JSON: a temporal wrapper set writes the same value as its primitive set but not the same bytes: seven fraction digits, and `+` escaped. Give the element its own `[JsonConverter]` if the response shape matters ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).

## [6.3.0] — 2026-08-16

### Added

- **`RangeSet.IsInfinity()` and `IsFinite()`**. `IsInfinity()` is true only for the set that covers the whole domain, which `{(,5],[10,)}` is not, though it is unbounded at both ends ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **Collection expressions for `RangeSet<TRange, T>`** (`RangeSet<Int32Range, int> set = [a, b];`), normalized as `From` normalizes. Also `From(params ReadOnlySpan<TRange>)` and the builder `RangeSet.Create<TRange, T>` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`ISpanParsable<T>`** on every range type, `RangeSet` and every value set: `Parse` and `TryParse` over a `ReadOnlySpan<char>` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **`Length`** on every range: a count for a discrete type, both ends included, and a span for a continuous one. It is `0` for the empty range and `null` when unbounded, and runs in memory only ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`Values()`** on `Int32Range`, `Int64Range` and `DateRange` lists the values in order. An unbounded range throws at the call ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`ToRangeSet()`** on `Int32Set`, `Int64Set` and `DateSet`, and `ToInt32Set()`, `ToInt64Set()` and `ToDateSet()` back: `{1,2,3,7}` becomes `{[1,3],[7,7]}`. In memory only ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **`Clamp(value)`** on every range: the contained value nearest the argument, or `null` for the empty range ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **An indexer** on every value set (`set[0]`).
- **`IRangeFactory<TRange, T>.IsDiscrete`**, for generic code that needs to know whether the element type has a step.

## [6.2.1] — 2026-08-16

### Breaking

- `IsAdjacentTo` returns `true` for an unbounded range next to its neighbour, as PostgreSQL's `-|-` does: `(,0].IsAdjacentTo([1,3])` returned `false`. `RangeSet.From` and `Union` now merge such neighbours, so `From([(,0], [1,)])` is `{(,)}` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- A literal that fails to parse, through `Parse` or JSON, quotes at most 64 characters of the input and its length, and no longer chains the element parser's exception. If you match the message or read `InnerException`, update that. Exception types are unchanged.

## [6.2.0] — 2026-08-16

### Breaking

- JSON: a `null` range reads back as `null`, where it threw `JsonException`. If you relied on that to reject a null, validate it yourself. `null` and `"empty"` stay different.

## [6.1.0] — 2026-08-15

### Breaking

- JSON: a wrapper set whose element has no `[JsonConverter]` writes the underlying values (`["users.read"]`, `[1,2]`), where it wrote objects such as `[{"Value":"users.read"}]` or `[{}]`. Reads run the element's `Parse`, and a registered converter still wins ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).

### Added

- **`IValueSetFactory<TSet, T>.ElementJsonConverter`**, the element converter a set uses when none is registered.
- **`RangeVariantJsonConverter<TVariant, TRange, T>`**.

### Fixed

- JSON: a `null` range property is written as `null`, where it threw `NullReferenceException`. Reading `null` still throws; write `"empty"` for the empty range.
- JSON: a range serialized through `object` works, where it threw `ArgumentException`.

## [6.0.0] — 2026-08-14

Value sets, a second type family. No breaking changes.

### Added

- **Value sets**: `StringSet`, `GuidSet`, `Int16Set`, `Int32Set`, `Int64Set`, `DecimalSet`, `DateSet`, `TimeSet`, `DateTimeSet` and `DateTimeOffsetSet`, deduplicated and sorted, with no nulls. The wrapper sets `StringSet<T>`, `GuidSet<T>`, `Int32Set<T>` and `Int64Set<T>` hold your own element types and need only BCL interfaces on them ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **Set operations**: `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, `IsProperSubsetOf`, `IsProperSupersetOf`, `Union`, `Remove`, `Count` and `IsEmpty`, and `Intersect`, `Except` and `Add` in memory only. Also array literals, JSON and collection expressions.

## [5.0.0] — 2026-08-13

No breaking changes.

### Added

- **`TimeRange`** over `TimeOnly`, for the custom `timerange` type. It is half-open by default, so `[09:00,12:00)` and `[12:00,17:00)` join like shifts, and a window across midnight is two ranges ([TimeRange](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#timerange-and-the-custom-timerange-type)).

## [4.1.0] — 2026-08-12

No change in this package. 4.1.0 added the two NodaTime packages.

## [4.0.0] — 2026-08-10

[Migration from v3.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v3x).

### Breaking

- `==` and `!=` on `RangeSet` compare values, as the range records and SQL `=` do. Use `ReferenceEquals` where you meant identity.
- `DoesNotExtendRightOf` and `DoesNotExtendLeftOf` match PostgreSQL for infinite bounds: `+∞ ≤ +∞`, so an unbounded range no longer always returns `false`.

### Added

- **Bound accessors**: `LowerBound()` and `UpperBound()` return `T?`, `null` where PostgreSQL returns `NULL`, and `LowerBoundInclusive()` and `UpperBoundInclusive()`, on ranges and `RangeSet`.
- **`Merge`**: the smallest range covering both, gap included (`range_merge`).
- **Aggregates**: `RangeAgg()` and `RangeIntersectAgg()` over a sequence of ranges.
- **`RangeSet` operators**: `Contains`, `Overlaps`, `IsAdjacentTo`, `IsStrictlyLeftOf`, `IsStrictlyRightOf`, `DoesNotExtendLeftOf`, `DoesNotExtendRightOf`, `IsEmpty()`, `IsUnboundedStart()` and `IsUnboundedEnd()`.

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

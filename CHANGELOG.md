# Changelog

All notable changes to CodoMetis.ValueRanges and its satellite packages.

The four published packages share one version number (the `Version` property in
`Directory.Build.props`) and are released together, so a version appears here even when a given
package saw no source change in it. Each package additionally carries its own `CHANGELOG.md`,
filtered to the entries that affect it.

Versions follow [Semantic Versioning](https://semver.org/). Entries are newest-first.

## [9.0.0] — 2026-10-11

Wire-format corrections, and the machinery that proves them. It started from three gaps measured
by wrapping a `DateRange` in a downstream value object and putting it on an ASP.NET Core API —
JSON with nothing registered, dictionary keys, OpenAPI — and a value-semantics review of the result
found more of the same kind, each one answering wrongly without throwing. **Nothing was removed or
resignatured** — package validation passes against the 8.0.0 baseline.

This is a major for the reason 7.0.0 and 8.0.0 were: several existing calls now *answer*
differently, and a caller cannot discover that from a compile error. A floating `8.*` reference
must not pick any of these up silently.

**What can break you, in order of likelihood:**

1. **`JsonSerializer.Serialize(range)` with default options writes the literal**, not
   `{"Length":181}`. The old form could not be read back by anything, this package included, but a
   consumer that only ever wrote it — into a response, a document, a log — sees its shape change.
2. **A culture provider no longer reaches the literal.** `Parse(s, culture)` reads the invariant
   literal, and formatting without a format string writes it. Code that parsed culture-formatted
   text through `Parse(s, de-DE)` deliberately must parse the bounds itself.
3. **Temporal literals are strict ISO 8601.** `[01.02.2027,05.03.2027)` used to parse — month
   first, as January 2 to May 2 — and a `DateTimeOffset` without an offset took the parsing
   machine's. Dotted, slashed and named-month dates, date-only text for a date-time and an
   offset-less `DateTimeOffset` are now a `FormatException`; `HH:mm` and PostgreSQL's own output
   still parse.
4. **Lenient decimal forms are refused.** `"1,000"`, `"(5)"` and currency symbols in a
   `DecimalRange` or `DecimalSet` literal are a `FormatException`, as they are in PostgreSQL.
5. **Interpolating an integer or decimal range is invariant under every culture.** Only code that
   depended on `[1,5,2)` — which parsed back as a different range — is affected.
6. **The EF Core provider writes temporal values as text at the database's resolution.** A SQL
   constant now carries the microsecond Npgsql would bind, so a query that compared a constant
   against its own row and missed now matches; and a temporal set whose elements are less than a
   microsecond apart is stored with one of them, as the database could only ever hold one.
7. **EF Core queries composing on a server-computed value set `Union` throw where they answered
   wrongly** — equality in a projection or on a projected union, a conditional or `??` around one,
   `GroupBy`, `Distinct`, `OrderBy`, set operations, `Count` through `??`, and `ExecuteUpdate` to a
   union. Each returned rows that disagreed with memory, or stored a non-canonical array.
8. **The literal grammar follows PostgreSQL's.** A quoted-empty or whitespace-only range bound is
   refused instead of read as unbounded; a backslash escapes in unquoted array elements and range
   bounds (`{a\,b}` is one element); `{{a,b}}`, `{a}b}` and `{a"b}` are refused; only ASCII
   whitespace is skipped. An empty format string writes the literal, as `null` does.
9. **A `DateTime` literal with an offset keeps its wall clock.** `2026-01-01T09:30+05:30` is 09:30
   (`Local`) on every machine; it was converted into the parsing machine's zone.
10. **JSON precedence and coverage changed in three places.** A union converter registered on the
    options now reaches values serialized through `object` or as a variant; a property declared as
    a range or set interface writes the literal (it wrote `{}`); and a NodaTime date in a non-ISO
    calendar — bare, or as a wrapper-set element — is refused, where it was written thirteen days
    off.

Everything else is additive: a `TypeConverter` on every type, ranges as dictionary keys, ranges
and sets inside EF Core JSON columns, Native AOT, `Deconstruct` on every bounded variant, and an
OpenAPI transformer to copy. The
[migration guide](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x) has the call-site edits.

### Changed

- **⚠️ Every range, variant, multirange and value set serializes as its literal with nothing
  registered.** The converters were opt-in (`AddRangeConverters()`), and System.Text.Json's fallback
  for a type with no converter is a property dump: `DateRange` wrote `{"Length":181}`, a `RangeSet`
  `[{"Length":5}]`, a `StringSet` `{"Values":["a","b"],"Count":2,"IsEmpty":false}`, and an
  `object`-typed range every public property of its variant. None of them read back — the abstract
  union threw `NotSupportedException`, the rest `JsonException` — and nothing threw on the way
  out, so an ASP.NET Core host that forgot the registration wrote wrong responses and refused every
  request body with a 400. Each type now carries `[JsonConverter]`, so `"[2026-01-01,2026-06-30]"`,
  `"{[1,5],[7,10]}"` and `["a","b"]` are the wire form by default.
  - System.Text.Json does not inherit `[JsonConverter]`, so each sealed variant carries its own —
    a value reached through `object` is a variant, and an attribute on the union alone would have
    left it on the property dump.
  - The attribute is the lowest-precedence route: a converter registered on the options or on a
    property still wins. `AddRangeConverters()` stays valid and is now redundant.
  - Source-generated `JsonSerializerContext`s honour it too. Unions, variants and the non-generic
    sets name a closed converter type, which the generator instantiates directly; `RangeSet` and the
    wrapper arities name `RangeJsonConverterFactory`, since an attribute cannot name an open generic
    converter. A context serializing a value set must also list the element type — the set
    converter resolves the element's contract through the options.
  - **Payload change** for anyone who wrote the old form: a response or document that carried
    `{"Length":…}` now carries the literal.

- **⚠️ Temporal bounds and elements parse as strict ISO 8601.** `DateRange`, `DateTimeRange`,
  `DateTimeOffsetRange`, `TimeRange`, `DateSet`, `DateTimeSet`, `DateTimeOffsetSet`, `TimeSet` and
  the temporal wrapper arities read element text with the lenient BCL `Parse`, which reads the
  invariant culture month first: `[01.02.2027,05.03.2027)` was January 2 to May 2, `9:30 AM` was
  09:30, and a `DateTimeOffset` without an offset took whichever machine parsed it — all silently.
  They now accept exactly what this library writes and what PostgreSQL prints in its ISO
  `DateStyle`: `yyyy-MM-dd`; `HH:mm` or `HH:mm:ss` with any number of fraction digits, truncated to
  100 ns; `T` or a space between them; for a `DateTime` an optional `Z` (`Utc`) or `±HH`,
  `±HH:mm` or `±HH:mm:ss` (`Local`) with the wall clock kept as written — **the offset is no longer
  applied**: it was converted into the parsing machine's zone, so `2026-01-01T09:30+05:30` was
  05:00 in Berlin and 04:00 on a UTC server, and a `tsrange` stores the wall clock; PostgreSQL's own
  `timestamp` input drops the offset the same way, and a local value now round-trips exactly, even
  across a DST gap — for a
  `DateTimeOffset` a required `Z`, `±HH`, `±HH:mm` or `±HH:mm:ss` (a historical local-mean-time
  offset, kept as the instant at offset zero); and `infinity` / `-infinity` as `MaxValue` /
  `MinValue`, what Npgsql materializes for them. Everything else is a `FormatException` through
  `Parse`, `TryParse`, JSON, the `TypeConverter` and configuration binding. The wrapper arities —
  BCL and NodaTime — read the text as their primitive first and hand the element its round-trip
  form, so a lenient generated element `Parse` never sees non-ISO text, and an element that
  ignores the format specifier fails on read instead of truncating silently. The NodaTime types
  were already strict; they now also read `infinity` for `Instant`, `LocalDate` and
  `LocalDateTime`, the types Npgsql maps it for. **A closed temporal set reads its JSON elements with
  the same grammar** — System.Text.Json's own converter read an offset-less `DateTimeOffset`, or a
  bare date, in the parsing machine's offset — unless a converter is registered for the element
  type, which still wins; the written form is System.Text.Json's, unchanged. `TemporalLiteralGrammarTests` pins the
  grammar; `TemporalLiteralStrictnessContractTests` (conventions) refuses a non-ISO element through
  every entry point of all 26 temporal range and set types.

- **The range interfaces are declared not to be an extension point.** `IRange<T>`, the shape
  interfaces and `IRangeFactory<TRange, T>` are the shared vocabulary of the shipped range types;
  implementing them outside the library is unsupported, and they may gain members in a minor
  version. Nothing changes for code that consumes them. The XML docs and `docs/architecture.md`
  state the policy.

### Fixed

- **⚠️ Interpolating a `DecimalRange` under a comma-decimal culture produced a different range.**
  `Int32Range`, `Int64Range` and `DecimalRange` have no `FormatValue` override, and the default
  formatted a `null` provider — what string interpolation, `string.Format` and logging renderers
  pass — in the *current* culture. Under `de-DE`, `$"{DecimalRange [1.5,2)}"` was `[1,5,2)`, which
  the parser reads back as **`[1,52)`**, silently; `[1.5,2.25)` came back as `[1,5225)`. Under
  `sv-SE` a negative bound took U+2212 as its minus sign and no longer parsed. A `null` provider
  now means the invariant culture in both default `FormatValue`s, as every override already did.
  `ToString()`, JSON, the `TypeConverter` and the EF bridge were never affected — they pass the
  invariant culture explicitly. `LiteralCultureContractTests` (conventions) formats every shape,
  multirange and set family under `de-DE`, `sv-SE`, `fa-IR` and `th-TH`.

- **⚠️ Decimal bounds and elements parse with PostgreSQL's numeric grammar.** `DecimalRange` and
  `DecimalSet` parsed with `NumberStyles.Any`, which also accepts group separators, currency
  symbols and parenthesised negatives: `["1,000",2000]` read as `[1000,2000]`, `"(5)"` as −5 —
  literals PostgreSQL rejects. It is also what let `5,2` read as 52 above, and what made an
  explicit comma-decimal provider misread silently: `DecimalRange.Parse("[1.5,2.5)", de-DE)` was
  `[15,25)`. They now parse with `NumberStyles.Float` — sign, digits, point, exponent — and those
  inputs are a `FormatException`. Integer types already used `NumberStyles.Integer`. **If you
  relied on the lenient forms, they now fail.** The wrapper arities are covered too: `DecimalSet<T>`
  and the integer arities handed the raw element text to the element's own `Parse`, and a generated
  one (`decimal.Parse(s, provider)`, `NumberStyles.Number`) accepts group separators, so
  `DecimalSet<Price>.Parse("{\"1,5\"}")` was `{15}` — through `TryParse`, configuration binding and
  a JSON string too — while `DecimalSet` refused it. They now parse with the family's grammar first
  and hand the element its invariant text, so its validation still runs and scale is kept; the
  decimal JSON writer and the EF Core bridge read element text the same way.
  `WrapperGrammarContractTests` (conventions) holds every wrapper family to its primitive sibling.

- **⚠️ An empty format string wrote a culture-formatted literal.** .NET reads `""` as the general
  format, exactly like `null`, but the literal checked only for `null`: `ToString("", de-DE)` on
  `DecimalRange [1.5,2)` wrote `[1,5,2)`, a `DateRange` with `("", th-TH)` Buddhist-era short
  dates. An empty format now writes the invariant literal everywhere `null` does.
  `LiteralProviderContractTests` and `LiteralCultureContractTests` sweep `""` alongside `null`.

- **⚠️ A quoted empty range bound read as an unbounded one.** `Int32Range.Parse("[\"\",5)")` was
  `(,4]` and `("","")` was `(,)`, silently, where PostgreSQL answers `invalid input syntax`; a
  whitespace-only bound (`[ ,5)`) was trimmed to nothing the same way. Bounds now follow PostgreSQL's
  `range_in`: only an absent bound is infinite, quotes may wrap any part of a bound, and a backslash
  escapes the next character inside quotes or out. Applies to every range type and multirange.

- **⚠️ Array literals departed from PostgreSQL's `array_in`.** `StringSet.Parse(@"{a\,b}")` was two
  elements, `a\` and `b`, where PostgreSQL reads the one element `a,b`; `{{a,b}}` was the set
  `{"{a","b}"}` where PostgreSQL reads a two-dimensional array; `{a}b}` was `{"a}b"}` where
  PostgreSQL errors. Unquoted elements now honour backslash escapes, an unescaped `{`, `}` or `"`
  inside one is a `FormatException`, and whitespace inside an element is kept. Both literal parsers
  skip only the ASCII whitespace PostgreSQL skips. `LiteralGrammarParityTests` (integration) asks a
  live server for the same answer.

- **The NodaTime types refused `HH:mm`.** `TimeRange.Parse("[09:00,17:00)")` parsed but
  `LocalTimeSet.Parse("{09:00}")` threw. `HH:mm` is now accepted wherever the BCL types accept it —
  a `LocalTime`, the time part of a `LocalDateTime`, an `Instant` with its offset or `Z`.

- **⚠️ A converter registered on the options for a range union reached union-typed declarations
  only.** System.Text.Json resolves a converter by the exact type, and a range reached through
  `object` or declared as a variant is handed over as its variant, whose own `[JsonConverter]`
  wrote the literal — so with a custom `Int32Range` converter, `Serialize(range)` wrote the custom
  form and `Serialize<object>(range)` wrote `"[1,2]"`. The variant converter now hands values and
  dictionary keys, on both legs, to whatever System.Text.Json would choose for the union from
  `Converters`, unless that is this package's own; a read goes through the union and narrows.
  **Payload change** for anyone who registered a union converter and serialized ranges through
  `object`.

- **⚠️ A property declared as a range or set interface was written as a property dump.**
  `IRange<int>` wrote `{}` and `IValueSet<string>` `{"Values":["a"]}`, with no error, and neither
  read back. The interfaces now carry `InterfaceDeclaredJsonConverter`, which writes through the
  runtime type's contract (the literal, or a converter registered for that type) and refuses a read
  with `NotSupportedException` naming the remedy: declare the concrete type.

- **⚠️ NodaTime: `AddNodaTimeRangeConverters()` shadowed `ConfigureForNodaTime`, and wrote bare
  non-ISO values in their own calendar's fields.** Registered first, its element converters won for
  all five NodaTime types, contrary to the documented "either order"; and a Julian
  `LocalDate(2024, 1, 1)` — ISO 2024-01-14 — was written `"2024-01-01"` and read back thirteen days
  off. Any other converter registered for the type now wins wherever it sits, and a bare
  `LocalDate`, `LocalDateTime` or `YearMonth` in a non-ISO calendar is refused on write with
  `ArgumentException`, as NodaTime's own converter refuses it. **Convert with
  `WithCalendar(CalendarSystem.Iso)` first.**

- **⚠️ NodaTime wrapper sets stored an element in another calendar as a different date.**
  NodaTime's ISO patterns format a value in its own calendar's fields, so a `LocalDateSet<T>` over a
  Julian `LocalDate(2024, 1, 1)` — ISO 2024-01-14 — wrote `{2024-01-01}` and `["2024-01-01"]`, and
  the EF Core bridge bound, inlined and stored ISO 2024-01-01: thirteen days off, with no error and
  an unequal round trip. `LocalDateTimeSet<T>` and `YearMonthSet<T>` likewise; against live
  PostgreSQL a Julian-January parameter matched a row holding ISO January. The closed sets convert
  such a value to ISO; a wrapper arity can only build its element by parsing, so it now **refuses**
  it with `ArgumentException` — in `From`, collection expressions and reads, and as the probe of
  `Contains`/`Add`/`Remove` — and the EF Core bridge refuses one as a bare operand. **If you wrapped
  non-ISO values, convert them with `WithCalendar(CalendarSystem.Iso)` first.**

- **⚠️ A culture provider no longer changes the literal.** `Parse(s, provider)` handed the provider
  to the element parser, and `ToString(null, provider)` to the element formatter. `th-TH` uses the
  Buddhist calendar, so `DateRange.Parse("[2026-01-01,2026-06-30]", th-TH)` returned
  **`[1483-01-01,1483-06-30]`** and `ToString(null, th-TH)` wrote `2569`; `ar-SA` read dates as
  Hijri; `de-DE` read the decimal `1.5` as fifteen. Nothing threw. The literal is PostgreSQL's
  text and has no culture, so:
  - `Parse`/`TryParse` (string and span, every range, `RangeSet` and value set) read the invariant
    literal and ignore the provider, as `Guid.Parse` does.
  - Without a format string, formatting writes the invariant literal whatever the provider. A
    provider still applies alongside an explicit format (`ToString("yyyy-MM-dd", th-TH)`), which
    asks for display text.
  - The library's own paths — JSON, the `TypeConverter`, EF Core — always passed the invariant
    culture and are unaffected, as is ASP.NET Core model binding, which passes it too.
  `LiteralProviderContractTests` (conventions) parses and formats every shape, multirange and set
  family under `de-DE`, `sv-SE`, `fa-IR`, `th-TH` and `ar-SA`. **If you parsed culture-formatted
  text through `Parse(s, culture)` deliberately, parse the elements yourself and build the range
  with the factories.**

- **Ranges, multiranges and value sets inside an EF Core JSON column** (`ToJson()`) could not be
  saved — the plugin's type mappings had no `JsonValueReaderWriter`, and `SaveChanges` threw a
  bare `NullReferenceException` from inside EF. Each mapping now writes the store type's text,
  which also makes queries into the document translate:
  `CAST(doc ->> 'Period' AS daterange) @> @day`.
  - **The document holds the store's representation, not the API JSON**, and it is a persisted
    format: a value set is `"{a,b}"` (an array in API JSON), the `YearMonth` types — wrapper
    arity included — their month-aligned dates, since that is what the cast reads.
  - **Temporal values are stored as a parameter would bind them**: truncated to the microsecond
    toward 2000-01-01 as Npgsql truncates, the extremes Npgsql maps as `-infinity` / `infinity`.
    Writing every digit and letting the server round them half-to-even made the document hold a
    different value from the column, so `Doc.Period == period` could miss the row it was saved
    from and `Contains` disagreed at the bounds. Materializing from the document now gives what
    materializing from a native column gives.
  - **`ExecuteUpdate` of a value set inside the document is refused**: EF Core writes it with
    `to_jsonb`, storing a JSON array the document cannot hold, after which the row no longer loads.
    The refusal reads the translated `UPDATE`, so a join or `EF.Property` cannot reach past it.
    Ranges, multiranges, a whole complex property holding a set, and the whole JSON property can be
    set.
  - `SaveChanges` (complex and owned, nested, null and empty kept apart) and the operators inside
    the document were checked against a live server, `ExecuteUpdate` to `null` fails in Npgsql for
    any type, and EF Core refuses `ExecuteUpdate` on owned JSON types — `docs/efcore.md` has the
    matrix.

  `JsonColumnTests` (integration) round-trips and queries every type inside one jsonb column;
  `TemporalStoreParityTests` holds the temporal rule for 18 types and seven probes (remainders
  above, below and at 500 ns, before 2000, the extremes, sub-microsecond ranges);
  `ExecuteUpdateJsonTests` the update paths. `MappingParityTests` requires a reader/writer on every
  mapping.
- **⚠️ Temporal SQL constants carried more precision than the database, and set arrays duplicates.**
  The EF Core provider rendered a temporal range, multirange or set constant with seven fraction
  digits (nine for NodaTime), which the server rounds half-to-even, while a parameter is truncated
  toward 2000-01-01 — so a constant could disagree with a parameter for the same value, and EF 10
  inlines evaluatable expressions as constants without `EF.Constant`. `DateTime.MinValue` rendered
  as a finite date where a parameter carries `-infinity`. Constants now carry the value Npgsql
  would bind — a set's element constant too, which for `InstantSet` and `LocalTimeSet` was Npgsql's
  own nine-digit literal, so `set.Contains(EF.Constant(x))` missed the element `x` was stored as.
  And a temporal set whose elements truncate to one microsecond was bound with both,
  so the stored array's `cardinality` disagreed with the set's `Count`; it is now deduplicated on
  every path. A range's *element* operand (`range.Contains(x)`, a constructor argument) has the
  range's own mapping now too: a constant is the stored value rather than Npgsql's literal, and a
  parameter is normalized like the bounds — `DateTimeOffsetRange.Contains(dto)` threw "only offset
  0 (UTC) is supported" for any non-UTC offset, and `DateTimeRange.Contains` likewise for a
  UTC-kinded `DateTime`.

- **A range built inline in a query failed to translate.** `r.Period == DateRange.CreateFinite(new
  DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31))` — or `Int32Range.Parse("[1,5]", null)`, or any
  factory call over values that depend on nothing in the query — threw "does not have a type mapping
  assigned" under `==`, `!=`, `Equals` and inside a conditional. EF Core evaluates such a call on the
  client and inlines the result as a constant typed by its sealed variant, for which no mapping
  exists. The query interceptor now retypes it to its range type; the literal goes through the
  store text like any other constant. `Contains`, `Overlaps`, captured variables and `EF.Constant`
  were never affected. `InlineRangeConstantExecutionTests` (integration) runs one per range type.

- **⚠️ A server-computed value set `Union` is refused wherever it would answer wrongly (EF Core).**
  `Union` translates to `array_cat`, which concatenates; 8.0.0's LINQ-level refusal caught only
  `==`/`Equals` in predicates and `Count` over a bare union, and its claim that a projection is
  client-evaluated was false. Against live PostgreSQL, with `{a,c} ∪ {a,b}` and target `{a,b,c}`: a
  projected comparison answered `false`, a ternary or `??` around the union matched 0 rows,
  `GroupBy` made 2 groups where memory makes 1, `Distinct().Count()` said 2, and `ExecuteUpdate`
  stored `{a,c,a,b}`. The refusal now runs on the final SQL tree, through a decorator around the
  provider's `IRelationalParameterBasedSqlProcessorFactory`, and follows a union through
  `array_remove`, `CASE`, `COALESCE` and subquery columns. It may still feed `@>`, `<@`, `&&`,
  `Remove`, `IsEmpty`, or the outermost projection, which materializes canonical.
  `UnionCanonicalityTests` (integration) executes every shape and the legal compositions.

- **A NodaTime range's element operand in another calendar was compared by its own fields (EF
  Core).** `LocalDateRange` and `LocalDateTimeRange` passed no ISO normalization for an operand, so
  `Contains(EF.Constant(julianDate))` rendered the Julian year, month and day as if they were ISO
  and missed its row, and a `LocalDate` parameter threw inside Npgsql. The operand is now converted
  to ISO, as the bounds are and as the NodaTime sets already did.

### Added

- **A `TypeConverter` on every range, variant, multirange and value set.** None had one, so
  `TypeDescriptor` could not convert them from a string, and `Microsoft.Extensions.Configuration`
  binding of `"Period": "[2026-01-01,2026-06-30]"` into a `DateRange` property produced **`null`
  with no error**. Each type now converts to and from its literal (`RangeTypeConverter`,
  `RangeVariantTypeConverter`, `RangeSetTypeConverter`, `ValueSetTypeConverter`, and
  `LiteralTypeConverter` for the generic types).
  - Always invariant: the culture argument is ignored, since the literal is a wire format.
  - An empty string converts to `null` (absent), never to the empty range, which is `empty`.
  - Unlike `[JsonConverter]`, `[TypeConverter]` *is* inherited, so without their own a variant
    would have silently used the union's and returned whatever shape the literal had. Each variant
    carries a narrowing converter that refuses another shape.
  - ASP.NET Core model binding is unaffected — minimal APIs and MVC bind through the static
    `TryParse` these types already have.
- **Native AOT and trimming.** `RangeSet` and the validated-wrapper sets failed under Native AOT
  with "missing native code": their JSON converters and `TypeConverter`s were built with
  `MakeGenericType` over instantiations the AOT compiler never generated, and whether one existed
  depended on unrelated code elsewhere in the program. Each generic type now registers its own
  closed converters, which are compiled with it, so even a type a program only deserializes works.
  `RangeJsonConverterFactory` and `LiteralTypeConverter` no longer use `MakeGenericType` or walk
  interfaces, `ValueSetJsonConverter` resolves elements through `JsonTypeInfo<T>`, and both
  libraries build with `IsAotCompatible` — no trim or AOT warnings in a consumer's publish. The
  consumer smoke test gained a NativeAOT consumer that publishes and runs a native binary; seeding
  out `RangeSet`'s own registration fails exactly the range set cases there. The core and NodaTime
  READMEs now say so, and so do their NuGet tags (`native-aot`, `trimming`); `PackagingConventionTests`
  holds both claims to the project's `IsAotCompatible` in both directions. The EF Core packages make
  no such claim. A source-generated context must list a value set's element type as well as the
  set; leaving it out is a `NotSupportedException` naming the element, even for the families that
  bring their own element converter (`SetSourceGenerationTests` pins both halves, and the NativeAOT
  consumer runs the documented context for a primitive set and a range through `object`).
- **Ranges and multiranges as dictionary keys.** `Dictionary<DateRange, int>` threw
  `NotSupportedException` in both directions, registered or not. `RangeJsonConverter`,
  `RangeVariantJsonConverter` and `RangeSetJsonConverter` now override
  `ReadAsPropertyName`/`WriteAsPropertyName`, writing the same literal as the value form
  (`{"[2026-01-01,2026-06-30]":181}`). Keys canonicalize on read like values, and a malformed or
  wrong-shape key is the same `JsonException`. `DictionaryKeyPolicy` is not applied — a naming
  policy would rewrite the literal into text that does not parse. Value sets are not keys: their
  value form is a JSON array, not a string. Because keys canonicalize, two spellings of one key —
  `"[1,5]"` and `"[1,6)"` — collide: by default the later value silently wins, as for any duplicate
  key, while .NET 10's `AllowDuplicateProperties = false` refuses the collision, since it checks the
  parsed key. `docs/serialization.md` recommends the latter wherever a dropped entry matters.
- **`Deconstruct` on every bounded variant.** The positional variants had one from the compiler, but
  every `Finite` is written by hand — its constructor is internal, so the factories stay the only
  way to build one — and so are the unbounded variants of `LocalDateRange`, `LocalDateTimeRange`
  and `YearMonthRange`, whose constructors normalize the calendar. `var (start, end) = finite`
  compiled on no range type. Each now deconstructs into exactly what its constructor takes, as a
  positional record would:
  - `(start, end)` on a discrete type. Both bounds are inclusive — the canonical form — so they pass
    straight back to `CreateFinite`, whose default there is closed.
  - `(start, end, startInclusive, endInclusive)` on a continuous type, deliberately with no
    two-value form: passed back to `CreateFinite`, `(1.5,2]` would come out as its half-open
    default `[1.5,2)`.

  `VariantDeconstructContractTests` (conventions) holds every bounded variant of every discovered
  range type to its constructor's parameters, and checks the values written against the properties
  under both inclusiveness settings.
- **Docs: an OpenAPI schema transformer.** ASP.NET Core's generator describes a range, multirange
  or value set body as `{}` — it cannot see through a custom converter, and .NET 10 has no public
  hook for a converter to declare its schema. `docs/serialization.md#openapi` carries a
  `IOpenApiSchemaTransformer` to copy into an API project: ranges and multiranges as `type: string`
  with a literal pattern, value sets as arrays of unique elements. It ships in no package; the
  repository compiles and tests it against `Microsoft.AspNetCore.OpenApi` 10 and holds the doc to
  the tested copy verbatim. Query and route parameters were already `type: string`.
- **Tests:** `TypeConverterContractTests` (conventions) converts every shape through the union's and
  the variant's converter under `de-DE`, and every set family. A new
  `CodoMetis.ValueRanges.AspNetCore.Tests` project runs configuration binding and a real ASP.NET
  Core host (MVC and minimal APIs) with no ValueRanges registration. `JsonDefaultContractTests` (conventions) serializes every shape of every discovered
  range type — through the union, through `object`, inside a `RangeSet`, and as a dictionary key by
  union, variant and multirange — and every value set family in both arities, with nothing
  registered, against the registered output. The consumer smoke test asserts the unregistered wire
  form against the packed package.

## [8.0.0] — 2026-08-17

Six corrections and the machinery that found them. **Nothing was removed or resignatured** —
package validation passes against the 7.0.0 baseline — and no signature changed anywhere.

This is a major for the same reason 7.0.0 was: several corrections change what existing calls
*answer*, and a caller cannot discover that from a compile error. One goes further and changes a
query that compiled and ran into one that throws. Upgrading is a code change for anyone on the
affected paths, and a floating `7.0.*` reference must not pick that up silently.

**What can break you, in order of likelihood:**

1. **`==`/`!=`/`Equals` over a server-computed value set `Union` now fails translation.** Those
   queries were returning wrong rows; they now throw with a message naming the alternatives.
2. **`RangeSet.Except(TRange)` with an infinity operand** returned the whole domain and now returns
   the empty set, `Complement()` on the infinite set with it.
3. **`YearMonthRange.NextValueAfter`/`PreviousValueBefore`** now reject a non-ISO calendar instead
   of stepping in it and returning a value the type's own constructors refuse.
4. **`RangeSet.Contains(T)` on the infinite set** threw and now answers `true`; **the NodaTime step
   functions** threw at a Gregorian-spelled domain maximum and now answer `null`. Both were loud
   failures, so only code catching them is affected.

Everything else here is internal or additive. The engines now decide on the shape *pair* rather than
the receiver's shape, and a pair with no arm throws instead of returning something plausible — the
fourth instance of the trap 7.0.0 documented, and the structural change that makes a fifth loud. The
release also adds four exhaustive oracles, three convention tests and a hand triage of every discard
arm in the codebase; those found four of the six corrections and are listed under **Added**.

### Fixed

- **⚠️ Comparing a server-computed value set `Union` for equality is now refused instead of
  answering wrongly.** `Union` translates to `array_cat`, which concatenates: the result carries
  duplicates and keeps each operand's ordering, while array equality is sensitive to both. Against
  a live server `{a,c} ∪ {a,b} = {a,b,c}` is `false` for the repeated `a`, and — the half that
  surprises — `{a,c} ∪ {b} = {a,b,c}` is `false` too, where nothing repeats and only the order
  differs. In memory both are `true`. Nothing threw; the row simply did not match.

  `==`, `!=` and `Equals` over a union, or over anything built on one that did not restore
  canonical form, now fail translation with an exception naming the alternatives. This matches how
  `Count` over a union has been refused since 6.2.0, and it was the last known-wrong translation in
  the package. The order- and multiplicity-insensitive operators are unaffected and still compose
  on a union — `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, `IsEmpty`, and the proper
  subset/superset pair, which is written as `<@ AND NOT @>` precisely so it stays insensitive.

  The refusal covers only the contexts EF must translate in full — `Where`, `Any`, `All`, the
  predicate overloads, the ordering and grouping keys. In a **projection** it still works, because
  EF falls back to client evaluation there and computes against the materialized set. That scoping
  is what keeps equality in step with the `Count` refusal, which reaches the same outcome by a
  different route: returning `null` from a translator hands the decision to EF. Materializing with
  `AsEnumerable()` remains the way to ask for the in-memory answer in any context.

  Not "fixed" by canonicalizing in SQL, deliberately: PostgreSQL has no array_distinct, and sorting
  inside the query orders `text` by the database collation rather than ordinally — silently
  disagreeing with the client's canonical order, which is the same defect one layer down.
  **If a query relied on this comparison it was already returning wrong rows; it now throws.**
- **The NodaTime step functions missed a domain maximum spelled in another calendar.**
  `LocalDateRange.NextValueAfter` compared against `LocalDate.MaxIsoValue` with `==`, and NodaTime's
  equality includes the calendar system. Of its nineteen calendars only ISO and Gregorian can
  represent `9999-12-31` at all — Gregorian shares ISO's arithmetic but is a distinct
  `CalendarSystem` instance, so the Gregorian spelling of the domain maximum compared unequal, the
  guard was skipped and `PlusDays(1)` ran off the end of the domain. `(gregorianMax, +∞)` came back
  as a throw where it is the empty range. Each type is now fixed the way its own documented policy
  already says: `LocalDateRange` normalizes to ISO before comparing, `YearMonthRange` rejects
  non-ISO outright rather than stepping in the caller's calendar and returning a value its own
  constructors would refuse.
- **`IRangeFactory.ToString` formatted an unrecognised range as `"empty"`.** All five shapes are
  named above the fallback, so it is reachable only through an `IRange<T>` implementation that is
  none of them — which the sealed-variant rule forbids and the type system permits, the interface
  being public. `"empty"` was the worst available answer: that text is what `Parse` round-trips,
  what the EF literal sends to PostgreSQL and what the shape matrix compares against the server, so
  such a range would have been stored, queried and asserted as the empty range with nothing raised.
  It now throws. Found by triaging every discard arm outside `Internals/` — 60 of them, recorded in
  `docs/discard-triage.md`, of which this was the only defect.
- **`BridgedElementTypeMapping` produced a `string` element's literal by accident.** `string` is not
  `IFormattable`, so it missed every named arm and reached the fallback, where `ToString()` returned
  it unchanged — the right answer for the wrong reason, and the arm that would have silently handed
  PostgreSQL whatever `ToString` produced for a genuinely unknown element type. The string case is
  named and the fallback refuses.
- **`RangeSet.Contains(T)` threw on the infinite set.**
  `RangeSet<Int32Range, int>.Infinite.Contains(value)` raised
  `InvalidOperationException: Range shape 'Infinity' has no lower bound` for every value, where the
  answer is `true` — the infinite set contains everything, and `Int32Range.Infinite.Contains(value)`
  has always said so. The set locates the candidate element by binary-searching the sorted lower
  bounds, and its single element has no lower bound to search on; every other query on the set
  short-circuits the infinite case first, and this one did not. Loud rather than silent, unlike the
  rest of this release, and found by the new oracle below on its first run.
- **`RangeSet.Except(TRange)` returned the whole domain when subtracting an infinity range.**
  `RangeSet<Int32Range, int>.Infinite.Except(Int32Range.Infinite)` answered `{(,)}` where `X \ (-∞,
  +∞)` is the empty set for every `X`, and `Complement()` on the infinite set was wrong through the
  same path. The set-minus-set overload has always guarded its infinite operand and the single-range
  overload answers it through its `Contains` guard, so — as with the empty-range containment bug in
  7.0.0 — the three overloads were answering the same question two different ways. The engine's
  discard arm supplied `∞` for the one pair it was never given: `(Infinity, Infinity)`.

### Changed

- **The `Intersect`, `Merge` and `Except` engines dispatch on the shape pair.** Each had three
  entry points typed by the receiver's shape, and each of those switched over the operand's shape
  with a discard that rebuilt the receiver or returned `Empty` — the structure all four bugs shared.
  They are now one entry point per engine taking `IRange<T>` on both sides, switching over
  `(left, right)` with one arm per accepted pair, so a pair nobody handled is a missing *line*
  rather than something a fallback absorbs. No public signature changed and no behaviour changed
  beyond the fix above; the 3,300-comparison shape matrix agrees with PostgreSQL as before.
- **A shape dispatch with no arm for its operands throws `UnreachableException` naming the pair.**
  C# cannot prove a switch over interface patterns exhaustive, so the discard arm cannot be removed
  — but it can stop producing values. Every one of the four bugs was a fallback returning something
  well-formed: a wrong boolean, or a range of the right shape carrying the wrong values, which looks
  correct in a debugger and disagrees only with the database. These paths are unreachable behind the
  callers' existing guards; if a future change breaks one, the first test to reach it now says which
  pair is missing.

### Added

- **`EngineDispatchConventionTests`**, which parses the shipping sources and enforces both halves of
  the rule: a switch that dispatches on range shape must throw from its discard arm, and an engine's
  entry points must take `IRange<T>` on both sides rather than one operand's shape. Both are
  discovered by globbing `src/`, and both were verified by seeding the defect they claim to catch.
- **`SmallModelOracleTests`** — a second oracle beside the PostgreSQL shape matrix, asking set
  theory instead of the database, and needing no Docker. Every representable range over a tiny
  universe is enumerated from its specification — around 110 per domain, all five shapes and all
  four inclusiveness combinations at every bound — the expected value set is derived arithmetically
  from that specification, and every one of the ~12,100 ordered pairs is checked for all eight
  binary predicates, the four value-producing operations, and the same questions asked again at the
  `RangeSet` arities. About 460,000 assertions per run over the discrete and continuous domains, in
  under 200 ms.

  It exists because hand-picked representatives are how the first version of the 7.0.0 `Except`
  sweep reported zero disagreements on the exact defect it was written to catch. All five bugs of
  this family were seeded back in to prove it catches them: the adjacency asymmetry (6.2.1),
  `IsStrictlyLeftOf` on an unbounded start and `Except` between opposing unbounded operands
  (7.0.0), and both fixes above. The model has one axiom — that `Contains(T)` is correct, since
  results are read back through it — and that is pinned by its own test rather than assumed.
- **An equality sweep in `SmallModelOracleTests`**, closing the last surface the oracles reached at
  the multirange and value-set arities but not at the range's own. Two ranges are equal exactly when
  they hold the same values, and equal ranges hash alike — the law `DiscreteCanonical` exists to
  uphold, whose summary says the bounds are closed "so that structural record equality coincides
  with set equality", and which nothing checked. Over the integers `(1,5)`, `[2,5)`, `(1,4]` and
  `[2,4]` are four spellings of one range and equality has to see through all of them; the
  enumeration holds every spelling at every bound. `Equals(object)`, the `IEquatable<T>` path and
  the `==`/`!=` operators are checked separately because they can diverge, along with reflexivity
  and comparison against `null` and against another type.

  Seeded both halves: a `Finite.Equals` that forgets its upper bound, and a `GetHashCode` on
  reference identity, which reports `[1,1]` and `[1,2)` — the same discrete range spelled twice —
  as hashing apart. The first attempt at the equality seed would not compile, since defining
  `Equals` on a record without `GetHashCode` is CS8851, so the compiler already guards that half.
- **The bound accessors and `Clamp` joined the sweep**, having been argued total during the discard
  triage rather than checked. Grounded in three independent links: nullness from the
  specification's shape, inclusivity cross-checked against `Contains`, and `Clamp` against the
  bounds those establish — predicting a bound's value directly does not work, because a discrete
  range canonicalizes and an exclusive continuous bound is a value the range does not contain.
- **`DiscreteDomainBoundaryTests`**, covering what no oracle can reach. At the edge of a discrete
  domain the successor does not exist, so `(max, +∞)`, `(-∞, min)`, `(max, max]` and `[min, min)` are
  all the empty range; the guards that enforce that — in `DiscreteCanonical` and each type's two
  unbounded factories — were load-bearing and untested. This is the exact hole Guava's `Range` has
  had open since 2014 (#1767). The three small-model oracles cannot see it and not by oversight:
  they run over eight to fifteen grid points with bounds drawn from the interior, which is what
  makes them faithful, so exhaustive-over-a-small-model and boundary testing are complements. Types
  are discovered and domain extremes tabulated, so a discrete type added without bounds fails rather
  than being skipped. Proven by reverting each guard; the inclusive forms at the same bounds are
  asserted non-empty so the checks cannot pass by everything being empty.
- **`CalendarBoundaryTests`**, pinning the fix above along with its premise — that only ISO and
  Gregorian reach the ISO extremes — so a NodaTime release that widened another calendar would
  surface here.
- **`DomainBoundaryDivergenceTests`**, recording where the model and PostgreSQL deliberately part.
  The boundary is the one place a `ShapeMatrixParityTests` row would be wrong to write, and it is
  wrong even for `int4range`/`int8range`, whose element domains match `int` and `long` exactly:
  the disagreement is about the *sentinel*, not the domain. Here `±∞` mean the absence of a bound
  over a domain that stops at `int.MinValue`/`int.MaxValue`; in PostgreSQL they sit outside the
  element type. So `int4range(2147483647, NULL, '()')` raises `22003` on the server where the model
  answers empty, and `int4range(NULL, -2147483648, '()')` is non-empty there where the model says
  empty. Neither side is wrong, but a range at the boundary is not round-trip-safe — now asserted
  against a live server rather than left to be discovered.
- **`ValueSetNullContractTests`**, pinning by discovery that canonical form's exclusion of nulls is
  enforced by *refusing*, never by dropping. Silently discarding a null is the value-set shape of the
  fallback that produced five range bugs — an input nobody wrote a case for, answered plausibly:
  three supplied elements would yield a set of two, indistinguishable from a duplicate. Every entry
  point is checked — both `From` overloads, `Add`, `Remove`, `Contains`, an unquoted `NULL` in the
  PostgreSQL array literal, and a JSON `null` element — and all of them already refused, so nothing
  changed in the library.

  What is new is that the rule is enforced over *discovered* families rather than by hand. Exactly
  one qualifies today: every wrapper element is a `readonly record struct` and every NodaTime
  element is a struct, so `string` in `StringSet` is the only nullable element type among the 30.
  A family added later over a reference type is covered without anyone remembering to come back.
  Nulls stay out of `SetProbes`, which feeds sweeps that build valid sets — they need their own test,
  not a probe entry.
- **`Reflect.InvokeGeneric`**, so the three discovery-driven suites report a failed assertion
  directly instead of behind a `TargetInvocationException` frame that says nothing.
- **`ShapeMatrixCoverageTests`**, which fails when a binary range operation has no row in
  `ShapeMatrixParityTests`. The matrix found three of the five bugs in the receiver-shaped-dispatch
  family, and its weakness is that it is a list: an operation added to `RangeExtensions` without a
  row there is simply not swept, and until now nothing said so — the suite stayed green while the
  one check that would catch the next instance quietly stopped covering it.

  Operations are discovered by reflection (C# 14 extension members lower to static methods, so the
  twelve show up as `(receiver, IRange<T>)` pairs) and coverage is parsed out of the matrix's own
  source. The two forms are checked separately by return type: a predicate must appear in the table
  of `(name, operator)` pairs, a value-producing operation must be invoked against its SQL
  counterpart. That split is load-bearing — the matrix's dispatch switch calls all eight predicates,
  so an invocation alone would mark one covered after its row was deleted. All three ways coverage
  can be lost were seeded: a new operation with no row, a deleted predicate row, and a dropped
  value-operation sweep.
- **`SmallModelMultirangeOracleTests`**, reaching the multi-element algorithms that no oracle
  touched: the greedy merge, the sorted merge behind `Union`, the two-pointer merge-join behind
  `Except` and the single-pass gap walk behind `Complement` — the most intricate code here, each
  carrying a hand-written correctness argument in its doc comment, and covered by worked examples
  only. Over eight grid points every subset is a representable multirange, so all 256 subsets and
  all 65,536 ordered pairs is exhaustive over the whole multirange value space.

  It differs from the single-range sweep in two ways that matter. It compares results **element by
  element** against the canonical run decomposition rather than only probing membership, so an
  unmerged neighbour or a stray empty fails — the `RangeSet` invariant was load-bearing and
  unasserted, because `Contains(value)` cannot see it while `Count`, `Equals`, `GetHashCode`,
  `ToString` and the EF multirange literal all can. And it feeds `From` a deliberately
  non-canonical decomposition, since building from maximal runs asks normalization to do nothing;
  a seeded defect disabling adjacency merging passed every other check in the file until that path
  existed. Found no defects; three seeded ones — that adjacency arm, an inclusiveness flip in the
  complement walk, and a merge-join pointer advancing too far — are each caught.

  Discrete domains only, and not incidentally: a run of consecutive grid points is the canonical
  decomposition exactly when consecutive points are contiguous, which is false of the reals.
- **`SmallModelSetOracleTests`** — the same treatment for the value set families, where it matters
  more: `Intersect`, `Except` and `Add` are deliberately client-side only, because a PostgreSQL
  array has no intersection, no difference and no sorted insert, so unlike the ranges there is no
  second implementation anywhere to cross-check against. For all 30 set types — core and NodaTime,
  plain and wrapper arity — it builds *every* subset of the probe universe (2^n, so exhaustive over
  the whole value space rather than a sample) and checks `Values`, `Count`, `IsEmpty`, `Contains`,
  `Add`, `Remove`, all five relational predicates, `Union`, `Intersect`, `Except`, equality and
  hashing over every ordered pair, plus five construction paths per subset — reversed input,
  duplicated input, `IEnumerable`, repeated `Add`, and both round trips.

  It found no defects, which is the honest result; seven were seeded in to show it would have.
  Its model reads the canonical *order* from `CanonicalComparer`, so it verifies that every path
  agrees with the declared order but not that the order is the specified one — a limit recorded on
  the class and closed by the test below.
- **`ValueSetContractTests.StringBackedFamilies_SortOrdinal`**, pinning the one ordering claim no
  self-consistency check can make: `StringSet` and its wrapper arity must order `Zebra` before
  `apple`. Ordinal puts `Z` (90) first and every culture puts `apple` first, so swapping
  `StringComparer.Ordinal` for `StringComparer.InvariantCulture` used to leave the whole suite
  green while changing what the client considers sorted — and PostgreSQL's ordering of a `text[]`
  is not the current culture's.
- **A shared `SetProbes`** holding the value set probe table and type discovery, so the contract
  tests and the new oracle cannot drift apart on which families exist or what to feed them. Two
  tables would let a family lose its probes in one suite and go unexercised there while the other
  stayed green — the failure mode a discovery-driven suite has to defend against hardest.
- **`SetOperatorParityTests`**, the value set counterpart of the range shape matrix. The sets had
  an exhaustive in-memory oracle and example-based integration tests, and nothing in between:
  nothing asked PostgreSQL to confirm `@>`, `<@`, `&&` and `cardinality` over the whole value
  space. It now sweeps every ordered pair of subsets of a four-element universe — through EF, so
  the translation is inside the loop — for three families chosen for what each can break that the
  others cannot: `StringSet` (`text[]`, where the client's ordinal order and the database's
  collation disagree), `Int64Set` (the element literals PostgreSQL will not coerce), and
  `StringSet<TestKey>` (the wrapper arities, which reach the store through a text bridge). The
  three compositions on `Union` that this release kept legal are swept alongside, so that claim is
  now checked against a server rather than restated.

  Two seeds. Dropping the `NOT <@` half of `IsProperSupersetOf` in the translator fails all three
  families on exactly the equal-set rows; and refusing to translate `Union` — which sends the whole
  projection to client evaluation, where every comparison in the sweep would still pass, since the
  model would be agreeing with itself — fails on the assertion that the operators are in the SQL.
- **`RangeAggregateParityTests`**. `RangeAgg`/`RangeIntersectAgg` were example-tested on both sides
  and swept on neither, and nothing had ever fed either aggregate an empty or an unbounded range —
  the two inputs whose handling is a choice rather than a consequence. Every subset of an
  eight-range universe holding both is now one group, over a discrete and a continuous domain, and
  the server's fold has to equal the client's. The empty and infinity results are additionally
  written down as literals rather than compared, because "whatever the other one does" is not a
  specification: `range_agg` drops empties and absorbs into `(,)`, `range_intersect_agg` empties
  the fold at the first empty input and treats `(,)` as the identity, and the two implementations
  agree on all of it. The one divergence — a PostgreSQL aggregate over zero rows is `NULL` where
  `RangeAgg` answers the empty set — is now executed rather than only documented.

  Seeding `RangeIntersectAgg` to skip empty inputs is what exposed the first draft comparing the
  server against a fold re-derived in the test rather than against the shipped aggregate; the sweep
  now calls the real ones, and the seed fails 26 of its 510 comparisons.
- **`ContinuousMultirangeParityTests`**, closing the hole the multirange oracle documents. That
  oracle is exhaustive and discrete *by construction* — its model is a set of grid points, and
  decomposition into maximal runs is canonical only where consecutive points are contiguous — so
  continuous adjacency deciding a merge went unswept. PostgreSQL has no such difficulty: its
  multirange constructor merges adjacent ranges by comparing bounds, which is the same rule stated
  the same way, so it is a usable oracle exactly where a point set is not. Every subset of six
  fragments and every ordered pair of those subsets now goes through `+`, `-`, `*` and the
  complement against `nummultirange`, about 12,500 comparisons.

  Seeded twice. A blunt break of continuous adjacency fails this sweep and nine worked examples, so
  it proves only that the sweep works. Making the lower-bound comparer blind to inclusivity is the
  seed that shows what it adds: the entire core suite — both oracles, every algebra test, every
  worked example — notices only through the comparer's own unit test, while its *consequence*,
  unions that quietly stop merging, is caught by nothing at the value level. This sweep reports it
  in hundreds of pairs.
- **`RegistryConcurrencyTests`**. The two registries are the only mutable process-wide state in the
  packages, and their design is sound — immutable snapshots, atomic replacement under a lock — but
  the write is a read-modify-write over the whole snapshot, so the lock is load-bearing and was the
  one part a later edit could drop with nothing noticing. Each registry is now hammered through
  both of its mutating entry points at once, with readers alongside checking the built-in
  registrations stay visible, over four rounds. Removing both locks fails both tests on 5 runs of
  5; a single round let the set registry through about one time in three, which is why there are
  four.

### Documentation

No behaviour changed here, but one of these corrects a claim that shipped on a NuGet page, so it
ships in this release rather than after it.

- **⚠️ The pattern-matching guidance was wrong, and the documented snippet did not compile.** Every
  page showing a switch over the five range shapes said the compiler enforces the coverage. It does
  not: the example warns `CS8509`, which is a build *error* under `TreatWarningsAsErrors` — as in
  this repository — so a reader's first copy-paste failed to build while the prose beside it
  promised a guarantee.

  Adding a `null` arm does not help, and that is the diagnostic worth recording: the complaint moves
  from `'_'` to `'not null'`, which is C# declining to accept the five subtype patterns as covering
  a non-null range at all. Exhaustiveness analysis does not reason about a closed class hierarchy,
  so the private base constructor buys the runtime guarantee and none of the compile-time one.

  The narrower true statement — no external subtype can be declared, so the five arms are complete
  in fact and the discard is unreachable — now appears alongside a discard arm that **throws**. That
  is the same rule the library already imposes on its own shape dispatches, for the same reason: a
  fallback returning a plausible value is how four of this release's family of bugs stayed hidden.
  Corrected in the README, `docs/ranges.md`, `docs/architecture.md`, `CLAUDE.md` and the core
  package's own README, which is where it reached NuGet.

- **The value set table listed six families as having no wrapper arity.** 7.0.0 added all eleven and
  completed the set; the table had not moved. The EF package's README also linked twice into
  root-README anchors that have never existed there.

- **A getting-started guide** (`docs/getting-started.md`) — in-memory first, then entity, migration
  and a translated query — and the migration guide now covers v7 and v8, having stopped at v3.x
  while the package shipped 8.0.0. The README leads with installation and code instead of
  positioning, which moves to `docs/why.md`.

## [7.0.0] — 2026-08-17

Two workstreams land together: the validated-wrapper arities now exist for every value set family
instead of four of them, and an audit of the range and multirange types corrected five defects.

**Nothing was removed or resignatured** — package validation passes against the 6.3.0 baseline — and
the value set surface only grew. What makes this a major is the range half: three of its five
corrections change what existing calls *answer*, silently, and a silent change of answer is the kind
a caller cannot discover from a compile error.

Those three shared one shape: the EF translation was correct and the in-memory implementation was
not, so the same expression gave one answer when it ran in PostgreSQL and another when it ran in
memory. Nothing threw. If you evaluate range operations only server-side, or only in memory, you saw
consistent (in these cases consistently wrong) results either way; the disagreement was visible only
to code that did both. The remaining two were loud rather than silent — a query PostgreSQL refused
to run, and a property that threw.

Two of the three are the same mistake, and it is the third time this repository has made it: an
operation that dispatches on the *receiver's* shape and handles the *operand's* shapes in an inner
switch, where the missing arm falls through to a default that answers `false` or returns the
receiver unchanged. `IsAdjacentTo` had it in 6.2.1; `IsStrictlyLeftOf` and `Except` have it here.

The audit's durable output is `ShapeMatrixParityTests`, which asks PostgreSQL for all eight binary
predicates *and* the four value-producing operations over every ordered pair of range shapes, and
requires the model to match — some 3,300 comparisons, no exclusions.

### Added

- **Eleven new wrapper arities**, completing the set: `Int16Set<T>`, `DecimalSet<T>`, `DateSet<T>`,
  `TimeSet<T>`, `DateTimeSet<T>` and `DateTimeOffsetSet<T>` in the core package, and
  `LocalDateSet<T>`, `LocalDateTimeSet<T>`, `InstantSet<T>`, `LocalTimeSet<T>` and
  `YearMonthSet<T>` in the NodaTime satellite. Every value set family now has one, so a
  domain type backed by any supported primitive can be stored as a native PostgreSQL array
  without the domain type referencing this package.

  As before, `TElement` is constrained only on BCL interfaces — `struct`, `IEquatable<T>`,
  `IComparable<T>`, `IFormattable`, `IParsable<T>` — which is what Vogen, Metalama,
  StronglyTypedId and hand-written wrappers already emit.

- **`SetTypeRegistry.RegisterFamily`**, the seam the NodaTime satellite registers its arities
  through. A wrapper family cannot be registered as a closed definition, because its element type
  is whatever the consumer supplies.

### Changed

- **The temporal arities ask their elements for a round-trip format** rather than accepting the
  element's default text form. This is the one place the wrapper contract is stricter than for the
  existing four arities, and it is not cosmetic: `TimeOnly` renders as `09:30` with a null format,
  `DateTime` as `06/15/2024 10:30:00`, so an arity built the way `Int32Set<T>` is built would have
  stored every timestamp truncated to the second, and every `DateTimeKind` lost — silently, on the
  way to the column.

  Concretely, the contract for these six families is that the element's `ToString("O", …)` (or
  `"yyyy-MM-dd"` for `DateSet<T>`, and the ISO pattern for the NodaTime arities) is exactly the
  backing primitive's. A wrapper that forwards its `format` argument — the generated shape —
  satisfies it with no extra work. One that swallows the argument is rejected at the persistence
  boundary with an error naming the type and the contract, rather than storing a truncated value.

- **`DateTimeSet<T>` and `DateTimeOffsetSet<T>` normalize at the provider boundary** exactly as
  their closed siblings do: wall-clock `DateTimeKind.Unspecified` for `timestamp`, UTC for
  `timestamptz`.

### Fixed

- **⚠️ The numeric wrapper arities ignored `JsonSerializerOptions.NumberHandling`.** `Int16Set<T>`,
  `Int32Set<T>`, `Int64Set<T>` and `DecimalSet<T>` write their elements' JSON tokens themselves, so
  System.Text.Json never gets to apply the setting on their behalf — and they did not consult it.
  Under `JsonNumberHandling.WriteAsString` an arity emitted a bare number where its primitive
  sibling emitted a string:

  ```
  Int64Set              ["9007199254740993"]
  Int64Set<OrderId>     [9007199254740993]     ← before
  ```

  `WriteAsString` is switched on almost exclusively because the consumer is JavaScript, where a
  bare number above 2^53 is rounded on arrival — 9007199254740993 arrives as 9007199254740992. So
  swapping a closed set for its arity silently reintroduced, at the client only, the corruption the
  setting was turned on to prevent. **Payloads change for anyone serializing a numeric wrapper arity
  under `WriteAsString`**, from a number to the string their primitive sibling was already writing.
  Reads are unaffected — the numeric converters have always accepted a JSON string unconditionally.


- **`DecimalRange.Length` threw `OverflowException` for a range wider than `decimal` itself.**
  `DecimalRange.CreateFinite(decimal.MinValue, decimal.MaxValue).Length` raised instead of
  answering; the span is twice `decimal.MaxValue` and there is no wider type to compute it in. It
  now returns `null`, which is the answer `Int64Range.Length` already gave for a count above
  `long.MaxValue` and documented as "too large to represent". Only a range straddling zero can
  reach it, and the refusal is exact — a span of exactly `decimal.MaxValue` still measures.

  This was the only measure in the family that could fail, and a property that throws breaks
  debugger evaluation and LINQ projections as much as it breaks the caller.

- **⚠️ `Except` subtracted nothing when the two operands were unbounded in opposite directions.**
  `((-∞,5]).Except([1,+∞))` returned `{(-∞,5]}` — the receiver, unchanged — where the answer is
  `{(-∞,0]}`, and symmetrically `([1,+∞)).Except((-∞,5])` returned `{[1,+∞)}` instead of `{[6,+∞)}`.
  `RangeSet.Except` reaches the same engine through its merge-join and had it too.

  This is the most damaging of the five, because the result is a **well-formed range of the right
  shape carrying the wrong values** — nothing to notice at a glance, and a subtraction that quietly
  keeps what it was asked to remove. Every element type and both discrete and continuous domains
  were affected.

  `ExceptEngine` dispatched on the receiver's shape; each unbounded receiver's inner switch had an
  arm for a finite operand and one for an operand unbounded the *same* way, but none for the
  opposing one, so the `_` fallback rebuilt the receiver. That fallback is reachable *only* for the
  opposing-unbounded pair — an empty operand is filtered by the `Overlaps` guard and an infinite one
  by the `Contains` guard — so it was wrong on every call that reached it.

- **⚠️ `Contains` and `IsContainedBy` now agree that the empty range is contained by everything.**
  `[1,5].Contains(Int32Range.Empty)` returned `false` and now returns `true`, as does
  `Int32Range.Empty.IsContainedBy(anything)` and `Int32Range.Empty.Contains(Int32Range.Empty)`.

  ∅ ⊆ S for every S: "every value of the inner range is also in the outer" is vacuously satisfied
  when the inner range has no values. PostgreSQL's `@>` answers the same, so the previous behaviour
  put the two sides of the wire in disagreement — `r.Period.Contains(DateRange.Empty)` matched no
  row in memory and every row in SQL.

  It also disagreed with this library. `RangeSet.Contains(RangeSet)` has always answered `true` for
  an empty operand by iterating zero elements, and `RangeSet.From` drops empty elements — so
  `RangeSet.Empty` and `Int32Range.Empty` are each other's normalized form, and the two overloads
  were answering the same question two ways.

  **Migration.** Only comparisons with an explicitly empty operand change. Code that relied on
  `Contains` to mean "contains and is non-empty" should say so: `outer.Contains(inner) &&
  !inner.IsEmpty()`. `Overlaps` is unchanged and still `false` for an empty operand — overlap needs
  a shared value — so a guard that wanted "shares something" was always better written with it.

- **⚠️ `Int64Range.Contains(value)` produced SQL PostgreSQL refused to run**, whenever the value was
  a constant rather than a captured variable. The range operators are polymorphic
  (`anyrange @> anyelement`), and PostgreSQL resolves polymorphic operators without applying
  implicit coercions — so a bare `25`, which it types as `integer`, does not match `int8range`:

  ```
  WHERE t."Tickets" @> 25          →  42883: operator does not exist: int8range @> integer
  WHERE t."Tickets" @> 25::bigint  →  runs
  ```

  Constant element operands now carry an explicit cast when their store type is not the one
  PostgreSQL infers from a bare numeric literal. `Int64Range` and
  `RangeSet<Int64Range, long>` were the only types affected: every other element type renders
  self-describing literal text (`DATE '2024-06-15'`, `TIMESTAMP '…'`), and `integer`/`numeric`
  literals already arrive as the type their subtype wants — so no other emitted SQL changes.

  The translation test for this asserted `@> ` and stopped there, and no test executed the query,
  which is exactly the pair of gaps that let it ship.

- **⚠️ `IsStrictlyLeftOf` answered `false` for every range unbounded at its *start***, and
  `IsStrictlyRightOf` for every such operand. `<<` compares the receiver's **upper** bound with the
  operand's **lower** bound, so `(-∞, 5]` — which has a perfectly finite upper bound — is strictly
  left of `[10, 20]`. The implementation switched on the receiver's shape and handled only
  `IFiniteRange<T>` there, while its inner switch handled unbounded *operands*:

  ```
  ((-∞,5]).IsStrictlyLeftOf([10,20])     false     ← before
  '(,5]'::int4range << '[10,20]'         true      ← PostgreSQL, and now the model
  ```

  The disagreement was **between the two sides of the wire**: the EF translation emits `<<` and
  was always right, so the same predicate answered `true` when it ran in the database and `false`
  when it ran in memory — over the same two values. `RangeSet.IsStrictlyLeftOf`/`RightOf` inherited
  it through their outermost element, so a one-element multirange `{(,5]}` was affected too.

  This is the same receiver-vs-operand asymmetry as the `IsAdjacentTo` bug fixed in 6.2.1, in the
  one other predicate whose answer depends on the receiver's shape. It is now decided by reading
  the two bounds rather than by switching on the receiver, so the two directions cannot drift
  again. **Behaviour changes for anyone comparing an unbounded-start range with `IsStrictlyLeftOf`
  or `IsStrictlyRightOf` in memory** — from a wrong `false` to the answer PostgreSQL already gave.

### Known difference

- **A temporal arity's JSON is not byte-identical to its closed sibling's**, though the token type
  and the value are the same and each payload deserializes into the other's type. The round-trip
  format always writes seven fraction digits where System.Text.Json trims them, and the default
  encoder escapes `+`:

  ```
  DateTimeOffsetSet     ["2024-06-15T10:30:00+02:00"]
  DateTimeOffsetSet<T>  ["2024-06-15T10:30:00.0000000+02:00"]
  ```

  The string, Guid, integer and decimal arities remain byte-identical to their siblings. Give the
  element type its own `[JsonConverter]` if an existing response shape matters.

- **`Count` on a value set carries the canonical-writers precondition, as `==` does.** The
  documentation claimed only `==` did. `Count` translates to `cardinality`, which ignores order but
  not duplicates, so a row another tool stored as `{b,a,b}` reads back as the two-element set
  `{a,b}` while the server counts three. No behaviour changed — `docs/efcore.md` is corrected and
  the live suite now pins it. `IsEmpty` is unaffected.

## [6.3.0] — 2026-08-16

Three additions that close gaps in the existing surface rather than extending the model. No
breaking changes.

### Added

- **`RangeSet.IsInfinity()` and `RangeSet.IsFinite()`** — the two shape predicates a range had and
  its multirange counterpart did not. `IsFinite()` is true for a non-empty set bounded at both
  ends; `IsInfinity()` is true only for the set covering the whole domain.

  `IsInfinity()` is deliberately **not** `IsUnboundedStart() && IsUnboundedEnd()`. That equivalence
  holds for a single range, because a range is contiguous, and fails for a set:
  `{(,5],[10,)}` is unbounded at both ends and does not contain 7. The EF translation reflects
  the same distinction — the range predicate maps to `lower_inf(x) AND upper_inf(x)`, the set
  predicate to equality against the infinite multirange literal, which is exact because
  PostgreSQL canonicalizes multiranges the way the model does. `IsFinite()` maps to
  `NOT lower_inf AND NOT upper_inf AND NOT isempty` for both.

- **Collection expressions for `RangeSet<TRange, T>`** — `RangeSet<Int32Range, int> set = [a, b];`,
  matching what the nineteen value set types and arities already supported. Elements are normalized exactly as
  `From` normalizes them: empties dropped, sorted by lower bound, overlapping and adjacent
  neighbours merged, any infinity collapsing the set. A `From(params ReadOnlySpan<TRange>)`
  overload comes with it, alongside the existing `From(IEnumerable<TRange>)`.

  The builder is exposed as a non-generic `RangeSet.Create<TRange, T>`, because a
  `[CollectionBuilder]` target cannot be generic. Prefer the collection expression: C# does not
  infer type arguments from constraints, so a direct call has to name both `TRange` and `T`, which
  is longer than `RangeSet<TRange, T>.From` already was.

- **`ISpanParsable<T>` on every parsable type** — the eleven range types, `RangeSet<TRange, T>`, and
  all nineteen value set types and arities, with public `Parse`/`TryParse` overloads over
  `ReadOnlySpan<char>` beside the existing `string` ones. The literal grammars were already parsed
  over spans internally, so this exposes the parser that was always there and lets a caller parse
  a slice of a larger buffer without allocating a substring first. `IParsable<T>` remains
  satisfied — `ISpanParsable<T>` extends it.

  One consequence worth knowing if you write generic code over these types: where a type parameter
  is constrained to `IRangeFactory`/`IValueSetFactory`, both `Parse` overloads are now visible, and
  a `string` argument binds to the span overload through the implicit conversion. Every type's two
  overloads are the same call, so results do not change.

- **`Length`** on every range type — the measure of what it covers. A discrete domain counts its
  values inclusive of both ends (`[2024-01-01, 2024-01-31]` measures 31 days, `[1, 10]` measures
  10 integers), a continuous one measures the span between the bounds. The empty range measures
  zero and every unbounded shape measures `null`: the two are different answers and stay
  distinguishable. The type follows the domain — `long?` for `Int32Range`/`Int64Range`, `int?`
  days for `DateRange`, `TimeSpan?` for the timestamp ranges, `decimal?` for `DecimalRange`, and
  `Duration?`/`Period?` for the NodaTime ranges, which distinguish elapsed time from a calendar
  quantity. Client-side only; it does not translate to SQL.

- **`Values()`** on the discrete range types — enumerates the contained values ascending,
  inclusive of both bounds. Declared only by `Int32Range`, `Int64Range`, `DateRange`,
  `LocalDateRange` and `YearMonthRange`, so asking a continuous range for its values is a compile
  error rather than a runtime failure. An unbounded range throws immediately rather than at the
  first iteration, so the failure points at the call rather than at the `foreach`.

- **A bridge between the value sets and the range sets** over the same discrete domain:
  `Int32Set`/`Int64Set`/`DateSet` (plus `LocalDateSet`/`YearMonthSet` in the NodaTime satellite)
  gain `ToRangeSet()`, which collapses runs of consecutive values — `{1,2,3,7}` becomes
  `{[1,3],[7,7]}` — and the matching `ToInt32Set()`/`ToDateSet()`/… expand back. The two shapes
  describe the same membership; which to store is a question of density, and a thousand
  consecutive dates are better served by one `daterange` than by a thousand-element array. Both
  directions are client-side: PostgreSQL converts between arrays and multiranges only through
  `unnest` and a custom aggregate.

- **`Clamp(value)`** on every range — the contained value nearest the argument, or `null` for the
  empty range. An unbounded side never constrains.

- **An indexer on the value set types**, `set[0]`, matching what `RangeSet` already offered.

- **`IRangeFactory<TRange, T>.IsDiscrete`** — a defaulted virtual static reporting whether the
  domain has a step, for generic code that cannot see which concrete type it holds. It cannot be
  derived from `NextValueAfter`, which returns `null` both for a continuous domain and for the
  last value of a discrete one; a convention test now holds the two to agreement.

## [6.2.1] — 2026-08-16

### Changed

- **Parse and JSON rejections no longer echo the whole input.** A malformed range, multirange or
  array literal used to come back in the `FormatException` — and a bad bound or element chained the
  BCL's exception, whose message embeds the text again — so a megabyte of hostile input became a
  megabyte of exception message, copied into every log sink and, in development, returned to the
  client. Messages now carry a 64-character excerpt plus the input's length, and element failures
  report the inner parser's reason as an excerpt instead of chaining it. **If you matched on the
  full message text or read `InnerException` on these failures, that shape has changed.** Exception
  types are unchanged: `FormatException`, `OverflowException`, `JsonException`; `TryParse` still
  never throws.
- **Tests:** a parser resilience suite (`ParserResilienceTests`) now backs the "denial of service
  through parsing" line in SECURITY.md — megabyte-scale malformed literals and 200,000-element sets
  are accepted or rejected in bounded time, `TryParse` never throws, and no rejection echoes the
  payload.

### Fixed

- **⚠️ `IsAdjacentTo` answered `false` whenever the receiver was unbounded, and normalization
  inherited it.** The predicate switched on the receiver's shape and handled only
  `IFiniteRange<T>`; every other shape fell through to `false`. Its *inner* switch did handle
  unbounded operands, so the relation was asymmetric — `[1,3].IsAdjacentTo((,0])` was `true` while
  `(,0].IsAdjacentTo([1,3])` was `false`. PostgreSQL's `-|-` is symmetric and answers `true` for
  both; the XML doc asserted the broken behaviour as if it were intended, which is why reading the
  code confirmed it.

  The consequence was not confined to the predicate. `RangeSet.From` and `RangeSet.Union` merge
  neighbours with `current.IsAdjacentTo(next)` after sorting by lower bound, so an unbounded-start
  element is *always* the receiver and always took the broken direction. Sets were built violating
  the invariant they document:

  ```
  RangeSet.From([(,0], [1,)])          was {(,0],[1,)}   now {(,)}
  blocks.Union(blocks.Complement())    was {(,0],[1,)}   now the Infinite set
  ```

  Two sets that should be equal compared unequal depending on how they were built, and a set
  covering the whole domain did not equal `RangeSet.Infinite`. **Results change for any range or
  set involving an unbounded element adjacent to its neighbour** — always from a wrong answer to
  the one PostgreSQL gives. Model-versus-server agreement is now pinned by the live suite for
  every affected shape pair. Applies to the NodaTime range types, which share the predicate.

## [6.2.0] — 2026-08-16

### Changed

- **⚠️ A null range now reads back as `null` instead of throwing.** Writing a null `Int32Range?`
  property emitted `{"Seats":null}`, and reading that same document threw `JsonException` — the
  package could not read a payload it had just written, so an ASP.NET Core API could return a
  body it was unable to accept. `null` is now left to System.Text.Json in both directions, as for
  any other reference type, matching what `RangeSet` and the value sets already did.
  `null` and the empty range stay distinct: absent is `null`, empty is the literal `"empty"`.
  **If you relied on the exception to reject a null where a non-nullable range was expected, that
  validation is gone** — the property now receives `null`, exactly as any other reference-typed
  property would. Malformed literals are still rejected.

### Fixed

- **`Count` over a union wrapped in `Remove` counted shared elements twice.** `Union` translates
  to `array_cat`, which concatenates rather than deduplicating, so `Count` over a server-computed
  union has always been refused rather than answered. The refusal matched only the outermost call,
  and `array_remove` *preserves* canonical form rather than establishing it — so
  `Tags.Union(Other).Remove(x).Count` slipped through to `cardinality` over the concatenation.
  Against live PostgreSQL, `{a,c}` unioned with `{a,b}` counted **4** where the in-memory
  expression is `{a,b,c}` — **3**. A `Where` on that count filtered on a number that was quietly
  too large; a `Select` returned it. Canonicality-preserving wrappers are now looked through, so
  the expression is refused in a predicate and falls back to client evaluation in a projection,
  where it answers 3.

## [6.1.0] — 2026-08-15

A System.Text.Json audit and the three defects it found. All three shared one shape: the
serializer fell back to reflection where the library expected a converter, and the result was
silence rather than an exception. Every fix replaces a crash or a wrong answer.

### Fixed

- **Value set elements without a converter serialized as objects.** A validated wrapper —
  `StringSet<PermissionKey>`, `GuidSet<TenantId>`, … — whose element type carried no
  `[JsonConverter]` was handed to the reflection path, which wrote `[{"Value":"users.read"}]`, or
  `[{}]` for the generator-typical record struct over a private field. The `[{}]` form destroyed
  data on read, and both disagreed with the `{users.read}` stored in PostgreSQL. Elements now go
  through the family's own text form, and reads re-run the element's `IParsable` validation.
  **Payload change:** string- and Guid-backed sets now write `["users.read"]`, integer-backed ones
  write `[1,2]` — indistinguishable from the primitive each wraps. If you persisted or published
  the old object form, that payload shape changes. A registered converter still wins.
- **The NodaTime sets had the identical failure** — `[{"Calendar":{…},"Year":2024,…}]` on write,
  `default` on read.
- **Nullable range properties threw.** `HandleNull` routed nulls into the write path, which
  dereferenced them: serializing an object with a null `Int32Range?` threw
  `NullReferenceException`. It now writes `null`. Reads still reject a null token — use `"empty"`.
- **Ranges reached through `object` threw.** `Serialize<object>(range)`, an `object`-typed property
  and heterogeneous collections all present the union's sealed variant, for which the converter
  could not be constructed, and a reflection `ArgumentException` escaped.

### Added

- `IValueSetFactory<TSet, T>.ElementJsonConverter` — a defaulted virtual static hook consulted only
  when the element type has no registered converter. The interface stays closed to external
  implementation.
- `RangeVariantJsonConverter<TVariant, TRange, T>`.
- `AddNodaTimeRangeConverters()` in the NodaTime satellite, for bare NodaTime values sitting next
  to a set, which the element hook does not reach. Composes with `ConfigureForNodaTime` in either
  registration order.

## [6.0.0] — 2026-08-14

**Value sets — a second type family.** The model was never "ranges" narrowly; it is immutable,
canonical-at-construction value domains with PostgreSQL-native storage shapes. `RangeSet` has
embodied that for multiranges since v2; v6 applies it one level down, to canonical sets of scalar
values stored as native PostgreSQL arrays.

### Added

- **Ten closed set types** in the core package — `StringSet`, `GuidSet`, `Int16Set`, `Int32Set`,
  `Int64Set`, `DecimalSet`, `DateSet`, `TimeSet`, `DateTimeSet`, `DateTimeOffsetSet` — plus the
  **validated-wrapper arities** `StringSet<T>`, `GuidSet<T>`, `Int32Set<T>`, `Int64Set<T>` for
  generator-produced domain values (Vogen, Metalama, StronglyTypedId, hand-written), constrained
  only on BCL interfaces so domain types never reference this package.
- **Five NodaTime set types** in the satellite: `LocalDateSet`, `LocalDateTimeSet`, `InstantSet`,
  `LocalTimeSet`, and the month-granularity `YearMonthSet`.
- **Membership algebra** — `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`,
  `IsProperSubsetOf`, `IsProperSupersetOf`, `Union`, `Remove`, `Count`, `IsEmpty`, plus
  client-side-only `Intersect`, `Except` and `Add` — with PostgreSQL array literals, JSON support
  and collection expressions.
- **EF Core mapping by convention** to native array columns, wrapper instantiations recognized
  automatically, translating to `@>`, `&&`, `<@`, `cardinality`, `array_cat` and `array_remove`.
  Containment always translates as `@>`, so a plain GIN index serves it.

No breaking changes; the major marks the package growing a second type family.

## [5.0.0] — 2026-08-13

### Added

- **`TimeRange`** (core) — a time-of-day range over `TimeOnly`, matching the most common custom
  range type in PostgreSQL practice (`CREATE TYPE timerange AS RANGE (subtype = time)`). Its EF
  mapping needs `HasPostgresRange` plus `EnableUnmappedTypes` on the database side.
- **`YearMonthRange`** (NodaTime satellite) — a month-granularity range over `YearMonth` for
  billing and reporting periods, stored by the EF satellite as a month-aligned `daterange`, so no
  custom database type is required. Conversions to and from `LocalDateRange` and `DateInterval`
  included.

Both carry the complete algebra, multiranges, literals, JSON support and aggregate overloads of
the existing eight. No breaking changes; the major marks the model growing beyond the PostgreSQL
built-ins.

## [4.1.0] — 2026-08-12

### Added

- **`CodoMetis.ValueRanges.NodaTime`** — `LocalDateRange` (`daterange`), `LocalDateTimeRange`
  (`tsrange`) and `InstantRange` (`tstzrange`), with conversions to and from NodaTime's own
  `Interval` and `DateInterval`.
- **`CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime`** — maps them via
  `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`, enabled with `npgsql.UseValueRangesNodaTime()`.

### Changed

- The EF plugin's internal type registry became extensible so satellites can register their own
  range types. No source changes required in consuming code.

## [4.0.0] — 2026-08-10

**PostgreSQL feature-matrix completion** — every remaining range and multirange operator and
function gained an in-memory implementation and a LINQ-to-SQL translation.

### Added

- **Bound accessors** — `LowerBound()`/`UpperBound()` returning `T?` with PostgreSQL's `NULL`
  semantics, and `LowerBoundInclusive()`/`UpperBoundInclusive()` mirroring `lower_inc`/`upper_inc`,
  on ranges and on `RangeSet`.
- **`Merge`** — the smallest single range spanning both operands including any gap
  (`range_merge`).
- **Aggregates** — `RangeAgg()` and `RangeIntersectAgg()` (`range_agg`,
  `range_intersect_agg`), translated inside `GroupBy` projections.
- **Multirange operator parity** — `Contains`, `Overlaps`, `IsAdjacentTo`,
  `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf` on `RangeSet`, plus the state
  checks `IsEmpty()`, `IsUnboundedStart()`, `IsUnboundedEnd()`.
- **Live-PostgreSQL integration suite** via Testcontainers, asserting the translated SQL agrees
  with the in-memory results.

### Changed

- **Breaking — `==`/`!=` on `RangeSet` is now structural equality**, consistent with the range
  records and with the SQL `=` the provider generates. Call sites that relied on reference
  identity must switch to `ReferenceEquals`.
- **Breaking — `DoesNotExtendRightOf`/`LeftOf` now match PostgreSQL for infinite bounds.** An
  infinite bound compares equal to another infinite bound (`+∞ ≤ +∞`), so an unbounded receiver no
  longer always returns `false`. Results against finite-bounded or empty operands are unchanged.

### Fixed

- `RangeSet.Infinite.Contains(range)` and `.Overlaps(range)` threw `InvalidOperationException` for
  operands with a finite bound.

## [3.1.0] — 2026-06-17

### Changed

- **Performance** — `RangeSet<TRange, T>` now exploits its sorted, disjoint, non-adjacent
  invariant: `Contains`/`Overlaps` go from O(n) to O(log n), `Union`/`Intersect`/`Except` become
  merge-joins over two pre-sorted streams, `Except` from `Infinite` becomes a single-pass
  complement walk, and single-element `From` takes a zero-allocation fast path. No public API or
  result changed.

### Added

- `RangeSet<TRange, T>.LowerBoundComparer`, exposing the set's internal lower-bound ordering as a
  public `IComparer<TRange>` singleton (also `RangeLowerBoundComparer<TRange, T>.Instance`).

### Fixed

- Quoted range bounds now unescape `\"` → `"` and `\\` → `\` on parse, so element types whose
  stringification contains quotes or backslashes round-trip correctly.

## [3.0.0] — 2026-06-11

### Added

- **`CodoMetis.ValueRanges.EFCore.PostgreSQL`** — the EF Core (Npgsql) plugin, mapping the range
  types to PostgreSQL range columns and translating the algebra from LINQ to SQL.

### Changed

- **Breaking — the state checks are extension methods, not extension properties.** `IsEmpty`,
  `IsFinite`, `IsInfinity`, `IsUnboundedStart` and `IsUnboundedEnd` need parentheses at every call
  site. Extension properties cannot appear in LINQ expression trees, which blocked SQL
  translation; as methods they translate.

## [2.0.0] — 2026-06-11

### Added

- `Parse`/`TryParse` and `ToString` support for PostgreSQL range literals.

### Changed

- **Breaking — `ToString()` returns a PostgreSQL range literal** (`[1,10]`) rather than the default
  C# record representation (`Finite { Start = 1, End = 10, … }`). Code depending on the old format
  for logging, display, serialization or string comparison must be updated.

## [1.0.0] — 2026-06-10

Initial release: the six PostgreSQL range domains as discriminated unions of five sealed variants
(`Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange`, `Infinity`), the interval algebra, and
`RangeSet<TRange, T>` as an always-normalized multirange.

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

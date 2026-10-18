# Migration guide

This page lists the source changes your code needs when you move to a new major version. The
[changelog](../CHANGELOG.md) describes each release in full. This page collects only the parts that
need an edit where you call the library.

After 10.0.0, a minor can change an answer too: a fix where a call returned a wrong value without
throwing. Such a minor opens its changelog entry with "Changed answers", and has a section here,
"Changed answers in x.y.0", that says how to find the calls whose answer changed.

## Migration from v1.x

### Update code that depends on `ToString()`

If your code depends on the text that `ToString()` returns, update it to the new literal format.

In v1.x, calling `.ToString()` on any kind of range returned the default C# record text:

```
Finite { Start = 1, End = 10, StartInclusive = True, EndInclusive = True }
```

From v2.0.0, `ToString()` returns the PostgreSQL range literal:

```
[1,10]
```

This matters if you used the text for logging, display, serialization or string comparison. If you
need the structured form, rebuild it from the properties of each kind of range with pattern matching.

## Migration from v2.x

### Add parentheses to the state checks

Add parentheses wherever you call `IsEmpty`, `IsFinite`, `IsInfinity`, `IsUnboundedStart` or
`IsUnboundedEnd`. They were extension properties in v2.x, and they are extension methods since
v3.0.0:

```csharp
// v2.x
if (range.IsEmpty) { … }

// v3.0.0
if (range.IsEmpty()) { … }
```

The compiler flags every call you need to change, so the edit is mechanical. The reason is EF Core.
Extension properties cannot appear in LINQ expression trees, so EF Core cannot turn them into SQL.
As extension methods, they run fully in the database through the EF Core package. See
[Entity Framework Core](efcore.md).

## Migration from v3.x

### Check code that compares `RangeSet` values with `==`

If you used `==` to test whether two `RangeSet` variables hold the same instance, replace it with
`ReferenceEquals(a, b)`.

Since v4, `operator ==` and `!=` on `RangeSet<TRange, T>` compare values, through `Equals`. This
matches the range types themselves, which are records, and the SQL `=` that the EF Core package
generates. Before v4, `==` on two sets compared references. Recompiling against v4 switches those
calls to value comparison without a warning.

### Check `DoesNotExtendRightOf`/`LeftOf` with infinite bounds

Check your calls where the range you call these methods on is unbounded. Before v4, they always
returned `false` for such a range.

In v4, an infinite bound compares equal to another infinite bound, exactly like PostgreSQL's `&<`
and `&>` operators. So `[5, +∞).DoesNotExtendRightOf([100, +∞))` is now `true`, because +∞ ≤ +∞.
When the range you pass has finite bounds or is empty, the results are unchanged.

Everything else in v4 only adds features, so no other source changes are needed.

## Migration from v4.x and v5.x

**No source changes.** Both majors only add features. Each one was a major version to mark the
model growing, not to signal a break: v5 added `TimeRange` and `YearMonthRange`, and v6 added the
value sets. You can recompile against either without an edit.

## Migration from v6.x

v7.0.0 changed what several existing calls return. Nothing was removed, and no signature changed, so
the compiler flags none of these. For each one, decide whether your code depended on the old result.

### Check `Contains` with an empty range

If your code relied on `Contains` to mean "contains and is not empty", say so explicitly:

```csharp
outer.Contains(inner) && !inner.IsEmpty()
```

The empty range is now contained by every range. `[1,5].Contains(Int32Range.Empty)` returned `false`
and now returns `true`. So do `Int32Range.Empty.IsContainedBy(anything)` and
`Int32Range.Empty.Contains(Int32Range.Empty)`. The empty set is a subset of every set (∅ ⊆ S), and
PostgreSQL's `@>` returns the same. The old behaviour gave a different result in memory than in SQL.

Good to know:

- **Only comparisons with an explicitly empty range change.** Every other result stays the same.
- **`Overlaps` is unchanged.** It still returns `false` when the range you pass is empty. A check that
  meant "shares a value" was always better written with `Overlaps`.

### `IsStrictlyLeftOf`/`IsStrictlyRightOf` are now correct for unbounded-start ranges

If you worked around the old result, remove the workaround. Otherwise no edit is needed.

`((-∞,5]).IsStrictlyLeftOf([10,20])` returned `false` and now returns `true`. The `<<` operator
compares the *upper* bound of the range you call it on with the *lower* bound of the range you pass.
`(-∞, 5]` has a perfectly finite upper bound. The EF Core translation always emitted `<<` and was
always right. So the same check returned `true` in the database and `false` in memory. A `RangeSet`
with one range had the same bug.

### Check `Except` with ranges unbounded in opposite directions

Review every `Except` call where the two ranges are unbounded in *opposite* directions. Look hardest
at this change. The old result was a well-formed range of the right shape, holding the wrong values.
The subtraction quietly kept part of what it was asked to remove.

`((-∞,5]).Except([1,+∞))` returned `{(-∞,5]}`, which is the range you called it on, unchanged. The
correct result is `{(-∞,0]}`. The mirrored pair went wrong in the same way. Only ranges unbounded in
opposite directions were affected, but every element type was. `RangeSet.Except` runs the same
code, so it was affected too.

### `DecimalRange.Length` returns `null` instead of throwing

If you catch `OverflowException` from `DecimalRange.Length`, check for `null` instead. Only code that
catches that exception is affected.

A range wider than `decimal` itself used to throw `OverflowException`, and now returns `null`. Only
a range that straddles zero can be that wide. This matches `Int64Range.Length`, which already
returned `null` for a span too large to count.

### Check the JSON of numeric wrapper sets under `WriteAsString`

A wrapper set is a set over your own element type, such as `Int64Set<OrderId>`. The changelog and
the API docs call these wrapper arities.

If a client of yours depends on the old JSON numbers, give the element type its own
`[JsonConverter]`. Under `JsonNumberHandling.WriteAsString`, `Int16Set<T>`, `Int32Set<T>`,
`Int64Set<T>` and `DecimalSet<T>` used to write a bare JSON number. The sets over the plain types
wrote a string:

```
Int64Set              ["9007199254740993"]
Int64Set<OrderId>     [9007199254740993]     ← v6
Int64Set<OrderId>     ["9007199254740993"]   ← v7
```

**Your JSON changes if you serialize a numeric wrapper set with that setting.** Reading is
unaffected, because the numeric converters have always accepted a JSON string.

### Make temporal wrapper elements forward the format

If you hand-write an element type for a temporal wrapper set, make its `ToString(format, provider)`
pass its `format` argument on. Wrappers from a generator already do this. Hand-written ones may not.

This applies to the temporal wrapper sets added in v7: `DateSet<T>`, `TimeSet<T>`,
`DateTimeSet<T>`, `DateTimeOffsetSet<T>`, and the NodaTime ones. Their default text form loses
data. `TimeOnly` prints as `09:30`, and `DateTime` as `06/15/2024 10:30:00`. When a wrapper ignores
the argument, storing it fails with an error that names the type and the rule. That way a truncated
value never reaches the database. See
[Keep the text form of the underlying type](value-sets.md#keep-the-text-form-of-the-underlying-type).

### One thing that starts working

A `Contains` call with a *constant* element now runs, and no edit is needed. `Int64Range.Contains(25)`
used to emit SQL that PostgreSQL would not run (`42883: operator does not exist: int8range @> integer`).
Constant element values now carry an explicit cast, so queries that threw now execute.

## Migration from v7.x

v8.0.0 is a major version for the same reason as v7. Several fixes change what existing calls
return, and one turns a query that ran into one that throws. No signature changed anywhere.

### Rewrite equality over a database-computed `Union`

If a query compares a value set `Union` computed in the database with `==`, `!=` or `Equals`,
rewrite it. The query now throws when EF Core translates it. This is the only change here that turns
working code into an exception. It comes first because it is the most likely to affect you:

```csharp
// v7: ran, and returned wrong rows.  v8: throws at translation.
db.Rows.Where(r => r.Tags.Union(other) == expected);
```

**If your query relied on this comparison, it was already returning wrong rows.** The exception
message names the alternatives, and you have two:

- **Use the operators that ignore order and duplicates.** These still work on a union: `Contains`,
  `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, `IsEmpty`, and the proper subset and superset pair.
- **Load the rows first with `AsEnumerable()`**, to get the in-memory result on purpose.

Here is why the old query returned wrong rows. `Union` runs in the database as `array_cat`, which
concatenates the arrays instead of deduplicating and sorting them. The result keeps duplicates and
the order of each input, and array equality is sensitive to both. Against a live server,
`{a,c} ∪ {a,b} = {a,b,c}` was `false` because of the repeated `a`. `{a,c} ∪ {b} = {a,b,c}` was
`false` too, where nothing repeats and only the order differs. In memory, both are `true`.

Good to know:

- **8.0 covered only some queries, and 9.0.0 covers the rest.** The 8.0 check covered only `Where`,
  `Any`, `All`, the predicate overloads, and the ordering and grouping keys. It assumed that EF Core
  evaluates a comparison in a projection in memory. EF Core runs it in the database instead, so the
  projection returned wrong results. 9.0.0 closes that gap and every other query shape. See
  [the v8.x section](#rewrite-the-remaining-queries-over-a-database-computed-union).

### `RangeSet.Except` with an infinite range, and `Complement()`

`RangeSet<Int32Range, int>.Infinite.Except(Int32Range.Infinite)` returned `{(,)}`, every value of the
element type. The correct result is the empty set, because `X \ (-∞, +∞)` is empty for every `X`.
`Complement()` on the infinite set went wrong through the same code. Both now return the empty set.

### Pass ISO values to the `YearMonthRange` step functions

Pass ISO calendar values to `NextValueAfter` and `PreviousValueBefore`, or convert them first. The
two functions now reject a non-ISO value outright.

Before, they stepped in whatever calendar you passed. They could return a value that the type's own
constructors reject. `LocalDateRange` behaves differently: it converts values to ISO instead of
rejecting them.

### Two calls that threw now return a value

If you catch an exception from either of these calls, you can remove the catch. Both used to fail
loudly, so only code that catches them is affected:

- **`RangeSet<Int32Range, int>.Infinite.Contains(value)`** threw `InvalidOperationException` for every
  value. It now returns `true`, because the infinite set contains everything.
- **The NodaTime step functions** threw at the largest value of the element type when it was written
  in the Gregorian calendar. They now return `null`.

### If you implement `IRange<T>` yourself

Don't. The rule that the five sealed kinds of range are the only ones has always forbidden it.

One change still matters here. `IRangeFactory.ToString` used to format a range it did not recognise
as `"empty"`. That is the literal PostgreSQL reads back as the empty range. So such a value was
stored and queried as the empty range, and nothing threw. It now throws.

## Migration from v9.x

No signature changed. Some calls answer differently, and each one now agrees with PostgreSQL where
the two sides disagreed before. The compiler can't show you where, so check these:

- **`IsUnboundedStart()` and `IsUnboundedEnd()` answer `true` for the infinite range.** A query
  and the same predicate in memory already disagreed on it
  ([details](#check-isunboundedstart-and-isunboundedend-on-the-infinite-range)).
- **`RangeSet.Overlaps` on a continuous domain answers `true` for more operands.** A query and the
  same predicate in memory already disagreed on them
  ([details](#check-rangesetoverlaps-on-a-continuous-domain)).

### Check `IsUnboundedStart()` and `IsUnboundedEnd()` on the infinite range

Check code that calls `IsUnboundedStart()` or `IsUnboundedEnd()` on a single range to pick out the
`(-∞, end]` or `[start, +∞)` shape, and could meet the infinite range `(,)`.

Before 10.0.0 the two methods tested the variant, so `(,)` answered `false` to both. PostgreSQL's
`lower_inf` and `upper_inf`, which they translate to, answer `true` for it, and so does a
`RangeSet` holding it. A query through EF Core therefore matched every row holding `(,)` where the
same predicate in memory did not. Now the range answers as the server does:

```csharp
Int32Range.Infinite.IsUnboundedStart();   // v9: false   v10: true
Int32Range.Infinite.IsUnboundedEnd();     // v9: false   v10: true
Int32Range.CreateUnboundedStart(5).IsUnboundedEnd();   // false in both
```

If you meant the shape, pattern-match on it. The variant interfaces are exact, and the compiler
checks them:

```csharp
if (range is IUnboundedStartRange<int> s) { … s.End … }   // (-∞, end] only, never (,)
```

`IsInfinity()` and `IsFinite()` are unchanged: `(,)` is the only range that answers `IsInfinity()`,
and it never answered `IsFinite()`.

### Check `RangeSet.Overlaps` on a continuous domain

Check code that calls `Overlaps` on a `RangeSet` over `decimal`, `DateTime`, `DateTimeOffset`,
`TimeOnly`, `LocalDateTime` or `Instant` with a range or set operand, and acts on `false`.

Before 10.0.0, `Overlaps` answered `false` when the operand stopped exactly where a later element
started, with one side exclusive, although an earlier element shared values with it. It tested only
the element that starts where the operand stops. PostgreSQL's `&&` answered `true` for the same
values, so a query through EF Core and the same call in memory disagreed on those rows.

```csharp
var set = RangeSet<DecimalRange, decimal>.From([DecimalRange.CreateFinite(1, 4), DecimalRange.CreateFinite(5, 8)]);
// {[1,4),[5,8)}

set.Overlaps(DecimalRange.CreateFinite(3, 5));              // v9: false   v10: true — shares [3,4)
set.Overlaps(DecimalRange.CreateUnboundedStart(5, false));  // v9: false   v10: true — (,5) covers [1,4)
set.Intersect(DecimalRange.CreateFinite(3, 5));             // {[3,4)} in both, which is why true is right
```

Discrete types (`Int32Range`, `Int64Range`, `DateRange`, `LocalDateRange`, `YearMonthRange`) are
unaffected: their closed canonical form has no exclusive end to stop on.

## Migration from v8.x

No signature changed. What changed is what existing calls write, accept and run as SQL. Check these
in order, starting with the most likely to affect you:

- **Your JSON changes shape if you never called `AddRangeConverters()`.** Every type now writes its
  literal by default ([details](#check-json-payloads-if-you-never-called-addrangeconverters)).
- **Input that used to parse now throws `FormatException`.** This covers non-ISO temporal text, a
  `DateTimeOffset` without an offset, culture-formatted numbers, and literals that PostgreSQL itself
  rejects ([culture](#parse-culture-formatted-text-yourself),
  [ISO 8601](#write-temporal-text-as-strict-iso-8601)).
- **Queries that ran now throw `InvalidOperationException`.** This covers any query over a value set
  `Union` computed in the database, other than projecting it or testing containment and overlap.
  Those queries were already returning wrong rows
  ([details](#rewrite-the-remaining-queries-over-a-database-computed-union)).
- **A `DateTime` with an offset keeps its wall clock.** It no longer converts into the machine's time
  zone. NodaTime values in a non-ISO calendar are rejected on write
  ([ISO 8601](#write-temporal-text-as-strict-iso-8601), [JSON](#check-three-changes-to-json-converters)).
- **Temporal constants and JSON-column values reach the database truncated to the microsecond**, as a
  parameter always did ([details](#temporal-values-reach-the-database-truncated-to-the-microsecond)).

### Check JSON payloads if you never called `AddRangeConverters()`

If you already call `AddRangeConverters()`, or register `RangeJsonConverterFactory`, your JSON does
not change, and you can keep the call. Every range type, each of its kinds of range,
`RangeSet<TRange, T>` and every value set now carries its own converter.

If you did not, your JSON changes from a property dump to the literal:

| Type | 8.x without registration | 9.0 |
|---|---|---|
| `DateRange` | `{"Length":181}` | `"[2026-01-01,2026-06-30]"` |
| range through `object` | `{"Start":…,"End":…,"StartInclusive":true,…}` | the literal |
| `RangeSet<Int32Range, int>` | `[{"Length":5}]` | `"{[1,5]}"` |
| `StringSet` | `{"Values":["a","b"],"Count":2,"IsEmpty":false}` | `["a","b"]` |

To keep an old shape for a property, put your own `[JsonConverter]` on it, or register one on the
options. Both take precedence over the type's attribute.

None of the old forms could be read back. So only code that writes and never reads can depend on
them, such as a client reading `Length` from a response, or a stored document nobody deserializes.

### Parse culture-formatted text yourself

If you parsed culture-specific text, such as `[01.01.2025,31.03.2025]` with `de-DE`, parse the bounds
yourself and pass them to the factories:

```csharp
var de    = CultureInfo.GetCultureInfo("de-DE");
var range = DateRange.CreateFinite(DateOnly.Parse("01.01.2025", de), DateOnly.Parse("31.03.2025", de));
```

`Parse(s, provider)` and `TryParse(s, provider, …)` now read the invariant literal and ignore the
provider. `ToString(null, provider)` always writes the invariant literal. So do
`ToString("", provider)`, string interpolation and `string.Format`. A provider still applies when you
pass an explicit, non-empty format string.

Before, the provider reached each bound:

- **`DateRange.Parse(s, th-TH)`** was 543 years early.
- **`DecimalRange.Parse("[1.5,2.5)", de-DE)`** was `[15,25)`.
- **`$"{decimalRange}"` under `de-DE`** wrote `[1,5,2)`, which parsed back as `[1,52)`.

Good to know:

- **Decimal bounds now parse with PostgreSQL's `numeric` grammar.** `"1,000"`, `"(5)"` and currency
  symbols throw `FormatException`. Before, they parsed as 1000, −5 and 5.
- **The wrapper sets follow the same grammar.** `DecimalSet<T>` and the integer ones read the element
  text with the plain type's grammar before your element's `Parse` sees it. So a generated `Parse`
  that accepts group separators no longer turns `{"1,5"}` into fifteen.
- **`DecimalSet<T>` now reads exponent notation** (`{1e3}`, or a JSON `1.5e1`), as `DecimalSet` does.

### Write temporal text as strict ISO 8601

Write every temporal bound and element as ISO 8601, the way this library and PostgreSQL write it.
This applies to `Parse`, `TryParse`, JSON, the `TypeConverter` and configuration binding. The BCL
parser used before read whatever the invariant culture could, month first. These inputs now throw
`FormatException`:

| Input | Was read as | Write instead |
|---|---|---|
| `01.02.2027`, `01/02/2027`, `Feb 1 2027` | January 2 | `2027-02-01` |
| `2027-02-01` for a `DateTime` or `DateTimeOffset` | midnight | `2027-02-01T00:00` |
| a `DateTimeOffset` without an offset | the parsing machine's offset | append `Z` or the offset |
| `9:30 AM`, `9:30` | 09:30 | `09:30` |
| the same forms as JSON elements of a `DateTimeOffsetSet`, `DateTimeSet` or `TimeSet` | as above — System.Text.Json's converter read the offset-less ones in the machine's offset | as above, or register a converter for the element type |

**If a payload carries offsets because it means instants, use `DateTimeOffsetRange` (`tstzrange`).**
Or convert explicitly with `ToUniversalTime()` before you build a `DateTimeRange`.

A `DateTime` bound or element with an offset, such as `2026-01-01T09:30+05:30`, now keeps the wall
clock as written, with `DateTimeKind.Local`. That is what PostgreSQL's `timestamp` input does. It
used to be converted into the parsing machine's time zone: 05:00 in Berlin, 04:00 on a UTC server.
A `tsrange` stores that wall clock. Offsets are `±HH`, `±HH:mm` or `±HH:mm:ss`. `+0530` and `+5:30`
are rejected.

Good to know:

- **These forms are accepted:** `HH:mm` without seconds, PostgreSQL's space-separated output, any
  number of fraction digits (truncated to 100 ns), and `infinity` / `-infinity`.
- **An empty or blank bound is no longer infinite.** A bound that is a quoted `""` or only
  whitespace, such as `["",5)` or `( , )`, now throws `FormatException`, as in PostgreSQL. Only an
  absent bound, as in `(,5)`, is infinite.
- **A backslash outside quotes escapes the next character**, in a range bound and in an array element
  alike, as in PostgreSQL. `StringSet.Parse(@"{a\,b}")` is the one element `a,b`. It used to be the
  two elements `a\` and `b`.
- **An array literal that PostgreSQL rejects, or reads as multi-dimensional, throws
  `FormatException`.** Examples are `{{a,b}}`, `{a}b}` and `{a"b}`. `{{a,b}}` used to be the set
  `{"{a","b}"}`.
- **Only ASCII whitespace is skipped** around elements and bounds. A no-break space is now kept as
  element text, and rejected inside a range bound. It used to be trimmed.
- **The temporal wrapper sets read the text as their plain type first.** So a lenient generated
  element `Parse` no longer sees non-ISO text. An element that ignores the format specifier now
  fails on read, instead of silently truncating.
- **The NodaTime types were already strict, and now also accept `HH:mm`** wherever the BCL types do:
  a `LocalTime`, the time part of a `LocalDateTime`, and an `Instant` written with its offset or `Z`.
  So `LocalTimeSet.Parse("{09:00}")` reads like `TimeRange.Parse("[09:00,17:00)")` instead of
  throwing.

### Temporal values reach the database truncated to the microsecond

The EF Core package now writes a temporal SQL constant, and a temporal value inside a JSON column, as
the value Npgsql would bind. That is the value truncated to the microsecond toward 2000-01-01, with
the extremes written as `-infinity` / `infinity`. A constant used to carry seven fraction digits,
which the server rounded. So it could miss the row that a parameter matched.

If you need to `ExecuteUpdate` a value set inside a JSON column, set the whole complex property that
contains the set instead. Updating the value set itself now throws, however the setter reaches the
property. It would have written a document that the row could no longer be loaded from.

Good to know:

- **Elements less than a microsecond apart are stored as one.** A temporal set with such elements is
  now stored with one of them. The column could only ever hold one value, and the stored array's
  `cardinality` disagreed with the set's `Count`.

### Rewrite the remaining queries over a database-computed `Union`

If a query uses a value set `Union` computed in the database in one of these ways, rewrite it. Use
`IsSubsetOf` and `IsSupersetOf` together for set equality, or load the rows first. These queries now
throw `InvalidOperationException`, and the message names the fix:

```csharp
db.Rows.Select(r => r.Tags.Union(x) == target);                    // was: false where memory says true
db.Rows.Where(r => (r.Id > 0 ? r.Tags.Union(x) : r.Tags) == target); // and the ?? form: was 0 rows
db.Rows.GroupBy(r => r.Tags.Union(x));                             // was: one group per operand order
db.Rows.Select(r => r.Tags.Union(x)).Distinct();                   // was: duplicates kept
db.Rows.OrderBy(r => r.Tags.Union(x));
db.Rows.Where(r => (r.OptionalTags ?? r.Tags.Union(x)).Count == 3); // was: duplicates counted
db.Rows.ExecuteUpdate(s => s.SetProperty(r => r.Tags, r => r.Tags.Union(x)));  // was: stored {a,c,a,b}
```

**A query that ran into one of these was already returning wrong rows.** `Union` runs in the
database as `array_cat`, a concatenation. 8.0 rejected `==`/`Equals` over it in predicates, and
`Count` over a bare union. Everything else ran and returned a different result from memory.

These keep working: projecting the union itself, and combining it with `Contains`, `Overlaps`, the
subset/superset family, `Remove` and `IsEmpty`.

**If you ran an `ExecuteUpdate` of a union**, the rows it wrote compare and count wrongly on the
server until they are rewritten. Load them and save them through `SaveChanges` to make them
deduplicated and sorted again.

**If you replace `IRelationalParameterBasedSqlProcessorFactory`** after `UseValueRanges()`, wrap the
registered factory instead. This applies whether you use `ReplaceService` or another library does it.
Otherwise every query with a `Union` that runs in the database now fails with
`function valueranges_union_without_sql_guard(…) does not exist`. The check runs from that service,
and a translated `Union` only becomes `array_cat` once the check has run.

### Check three changes to JSON converters

These change which converter writes a value, and which properties get one:

- **A converter you register on the options for a range type now also handles its kinds of range.**
  Before, a range serialized through `object`, an `object` collection, or a property typed as one kind
  of range skipped your converter and wrote the literal. Now it goes through your converter, so one
  payload no longer mixes two JSON forms. If you relied on the mixed form, declare those properties as
  a type your converter does not handle.
- **A property declared as `IRange<T>`, a shape interface or `IValueSet<T>`** now writes the literal
  (or array) of its runtime type. It used to write `{}` or a property dump. Reading one still cannot
  work, because the concrete type is unknown, and now throws `NotSupportedException`. Declare the
  concrete type instead.
- **NodaTime: convert non-ISO values with `WithCalendar(CalendarSystem.Iso)` before you write them.** A
  bare `LocalDate`, `LocalDateTime` or `YearMonth` in a non-ISO calendar is now rejected on write
  (`ArgumentException`). Before, it was written in its own calendar's fields and read back days off.
  The same holds for an element of `LocalDateSet<T>`, `LocalDateTimeSet<T>` or `YearMonthSet<T>`, in
  memory and through EF Core. Also, `AddNodaTimeRangeConverters()` no longer takes precedence over
  `ConfigureForNodaTime` when you register it first.

### Ranges can be dictionary keys

`Dictionary<DateRange, …>` used to throw `NotSupportedException`. It now writes the literal as the
property name. Nothing to change, unless you caught that exception.

### Things that start working

No edit is needed. They are listed so you can remove a workaround:

- **Configuration binding** of a range, multirange or value set property from its literal used to bind
  `null`, with no error. Every type now carries a `TypeConverter`, so the property binds.
- **A range, multirange or value set inside an EF Core `ToJson()` column** could not be saved.
  `SaveChanges` threw `NullReferenceException` from inside EF Core. Now the value is stored as the
  literal, and queries into the document run in the database.
- **Native AOT.** `RangeSet` and the wrapper sets failed with "missing native code". The core and
  NodaTime packages now build with `IsAotCompatible`. Your project needs only the source-generated
  JSON context that AOT always needs.

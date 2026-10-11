# Migration guide

Source changes required when moving between majors. Each release is also described in full in
the [changelog](../CHANGELOG.md); this page collects only the parts that need an edit at the
call site.

## Migration from v1.x

### `ToString()` now returns a PostgreSQL range literal

In v1.x, calling `.ToString()` on any range variant returned the default C# record representation:

```
Finite { Start = 1, End = 10, StartInclusive = True, EndInclusive = True }
```

From v2.0.0, `ToString()` returns the PostgreSQL range literal:

```
[1,10]
```

If your code depended on the old format for logging, display, serialization, or string comparison, update it to use the new literal format or, if you need the structural representation, reconstruct it from the variant's properties via pattern matching.

## Migration from v2.x

### State-check methods now require parentheses

`IsEmpty`, `IsFinite`, `IsInfinity`, `IsUnboundedStart`, and `IsUnboundedEnd` were extension properties in v2.x. In v3.0.0 they are extension methods — add parentheses at every call site:

```csharp
// v2.x
if (range.IsEmpty) { … }

// v3.0.0
if (range.IsEmpty()) { … }
```

The change is mechanical and the compiler will flag every affected site. The motivation is EF Core compatibility: extension properties cannot appear in LINQ expression trees, preventing SQL translation. As extension methods they are fully translated by the EF Core companion package — see [Entity Framework Core](efcore.md).

## Migration from v3.x

### `RangeSet` `==` is now structural

`RangeSet<TRange, T>` defines `operator ==`/`!=` as value equality, delegating to `Equals` — consistent with the range types themselves (records) and with the SQL `=` the EF Core provider generates. Code that compared sets with `==` previously got reference equality; recompiling against v4 silently changes those call sites to value comparison. If you relied on reference identity, switch to `ReferenceEquals(a, b)`.

### `DoesNotExtendRightOf`/`LeftOf` now match PostgreSQL for infinite bounds

An unbounded receiver previously always returned `false`. In v4, an infinite bound compares equal to another infinite bound — `[5, +∞).DoesNotExtendRightOf([100, +∞))` is now `true` (`+∞ ≤ +∞`), matching the `&<`/`&>` operators exactly. Results against finite-bounded or empty operands are unchanged.

Everything else in v4 is additive — no other source changes are required.

## Migration from v4.x and v5.x

**No source changes.** Both majors are additive, and each was bumped to mark the model growing
rather than to signal a break: v5 added `TimeRange` and `YearMonthRange`, v6 added the value set
family. Recompiling against either requires no edit.

## Migration from v6.x

v7.0.0 changed what several existing calls *answer*. Nothing was removed or resignatured, so the
compiler will not flag any of these — each one needs a decision about whether your code depended on
the old result.

### The empty range is now contained by everything

`[1,5].Contains(Int32Range.Empty)` returned `false` and now returns `true`, as does
`Int32Range.Empty.IsContainedBy(anything)` and `Int32Range.Empty.Contains(Int32Range.Empty)`. ∅ ⊆ S
for every S, which is also what PostgreSQL's `@>` answers — the previous behaviour put memory and
SQL in disagreement.

Only comparisons with an explicitly empty operand change. Code that relied on `Contains` to mean
"contains and is non-empty" should now say so:

```csharp
outer.Contains(inner) && !inner.IsEmpty()
```

`Overlaps` is unchanged and still `false` for an empty operand, so a guard that meant "shares a
value" was always better written with it.

### `IsStrictlyLeftOf`/`IsStrictlyRightOf` now answer correctly for unbounded-start ranges

`((-∞,5]).IsStrictlyLeftOf([10,20])` returned `false` and now returns `true`. `<<` compares the
receiver's *upper* bound with the operand's *lower* bound, and `(-∞, 5]` has a perfectly finite
upper bound. The EF translation always emitted `<<` and was always right, so the same predicate
answered `true` in the database and `false` in memory. One-element `RangeSet`s inherited it.

No edit is required unless you compensated for the wrong answer.

### `Except` no longer keeps what it was asked to remove

`((-∞,5]).Except([1,+∞))` returned `{(-∞,5]}` — the receiver, unchanged — where the answer is
`{(-∞,0]}`; symmetrically for the mirrored pair. It only affected operands unbounded in *opposite*
directions, and `RangeSet.Except` reached the same engine. Every element type was affected.

This is the one to look hardest at, because the old result was a well-formed range of the right
shape carrying the wrong values — a subtraction that quietly kept part of what it removed.

### `DecimalRange.Length` returns `null` instead of throwing

A range wider than `decimal` itself (only one straddling zero can be) raised `OverflowException`
and now returns `null`, matching what `Int64Range.Length` already did for an uncountable span. Only
code catching that exception is affected.

### Numeric wrapper arities honour `JsonNumberHandling.WriteAsString`

`Int16Set<T>`, `Int32Set<T>`, `Int64Set<T>` and `DecimalSet<T>` wrote a bare JSON number under
`WriteAsString` where their primitive siblings wrote a string:

```
Int64Set              ["9007199254740993"]
Int64Set<OrderId>     [9007199254740993]     ← v6
Int64Set<OrderId>     ["9007199254740993"]   ← v7
```

**Payloads change for anyone serializing a numeric wrapper arity under that setting.** Reads are
unaffected — the numeric converters have always accepted a JSON string. If a consumer pinned the old
shape, give the element type its own `[JsonConverter]`.

### Temporal wrapper elements must forward the format specifier

The six temporal arities added in v7 (`DateSet<T>`, `TimeSet<T>`, `DateTimeSet<T>`,
`DateTimeOffsetSet<T>`, and the NodaTime ones) require the element's `ToString(format, provider)` to
forward its `format` argument, because the default text form is lossy — `TimeOnly` renders as
`09:30`, `DateTime` as `06/15/2024 10:30:00`. A wrapper that swallows the argument is rejected at the
persistence boundary with an error naming the type and the contract, rather than storing a truncated
value. Generator-produced wrappers already forward it; hand-written ones may not. See [the text-form
contract](value-sets.md#the-text-form-contract).

### One thing that starts working

`Int64Range.Contains(25)` with a *constant* operand emitted SQL PostgreSQL refused to run
(`42883: operator does not exist: int8range @> integer`). Constant element operands now carry an
explicit cast. No edit needed — queries that threw now execute.

## Migration from v7.x

v8.0.0 is a major for the same reason: several corrections change what existing calls answer, and one
changes a query that ran into one that throws. No signature changed anywhere.

### Equality over a server-computed value set `Union` now fails translation

This is the only change here that turns working code into an exception, and it is first because it is
the most likely to reach you:

```csharp
// v7: ran, and returned wrong rows.  v8: throws at translation.
db.Rows.Where(r => r.Tags.Union(other) == expected);
```

`Union` translates to `array_cat`, which concatenates rather than canonicalizes — the result carries
duplicates and keeps each operand's ordering, while array equality is sensitive to both. Against a
live server `{a,c} ∪ {a,b} = {a,b,c}` was `false` for the repeated `a`, and `{a,c} ∪ {b} = {a,b,c}`
was `false` too, where nothing repeats and only the order differs. In memory both are `true`.

`==`, `!=` and `Equals` over a union now throw with a message naming the alternatives. **If a query
relied on this comparison it was already returning wrong rows.** Your options:

- Use the order- and multiplicity-insensitive operators, which still compose on a union:
  `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, `IsEmpty`, and the proper subset/superset
  pair.
- Materialize first with `AsEnumerable()` to get the in-memory answer deliberately.

The 8.0 refusal covered only `Where`, `Any`, `All`, the predicate overloads, and the ordering and
grouping keys, on the claim that EF evaluates a comparison in a projection on the client. It does
not — it translates it, and the projection answered wrongly. 9.0.0 closes that and every other
shape; see [the v8.x section](#a-server-computed-value-set-union-is-refused-wherever-it-would-answer-wrongly).

### `RangeSet.Except` with an infinity operand, and `Complement()`

`RangeSet<Int32Range, int>.Infinite.Except(Int32Range.Infinite)` answered `{(,)}` — the whole domain
— where `X \ (-∞, +∞)` is the empty set for every `X`. `Complement()` on the infinite set was wrong
through the same path. Both now return the empty set.

### `YearMonthRange` step functions reject a non-ISO calendar

`NextValueAfter`/`PreviousValueBefore` stepped in whatever calendar they were handed and could return
a value the type's own constructors refuse. They now reject a non-ISO argument outright. Pass ISO
values, or convert first — `LocalDateRange` normalizes to ISO instead of rejecting.

### Two calls that threw now answer

Both were loud failures, so only code catching them is affected:

- `RangeSet<Int32Range, int>.Infinite.Contains(value)` raised `InvalidOperationException` for every
  value and now answers `true` — the infinite set contains everything.
- The NodaTime step functions threw at a Gregorian-spelled domain maximum and now answer `null`.

### If you implement `IRange<T>` yourself

Don't — the sealed-variant rule forbids it and always has. But note that `IRangeFactory.ToString`
used to format an unrecognised range as `"empty"`, which is the literal PostgreSQL round-trips, so
such a value would have been stored and queried as the empty range with nothing raised. It now
throws.

## Migration from v8.x

No signature changed. What changed is what existing calls write, accept and translate — check these,
in order of how likely they are to reach you:

- **Your JSON payloads change shape** if you never registered `AddRangeConverters()`: every type now
  writes its literal by default ([details](#ranges-multiranges-and-value-sets-serialize-as-literals-without-registration)).
- **Input that parsed now throws `FormatException`**: non-ISO temporal text, an offset-less
  `DateTimeOffset`, culture-formatted numbers, and literals PostgreSQL itself refuses
  ([culture](#the-literal-ignores-the-culture), [ISO 8601](#temporal-literals-are-strict-iso-8601)).
- **Queries that ran now throw `InvalidOperationException`**: anything over a server-computed value set
  `Union` other than projecting it or testing containment and overlap — those queries were already
  returning wrong rows ([details](#a-server-computed-value-set-union-is-refused-wherever-it-would-answer-wrongly)).
- **A `DateTime` with an offset keeps its wall clock** instead of converting into the machine's zone,
  and NodaTime values in a non-ISO calendar are refused on write ([ISO 8601](#temporal-literals-are-strict-iso-8601),
  [JSON](#three-json-precedence-and-coverage-changes)).
- **Temporal constants and JSON-column values reach the database truncated to the microsecond**, as a
  parameter always did ([details](#temporal-values-reach-the-database-as-text-at-its-resolution)).

### Ranges, multiranges and value sets serialize as literals without registration

Every range type, its variants, `RangeSet<TRange, T>` and every value set now carries its
converter. If you already called `AddRangeConverters()` (or registered `RangeJsonConverterFactory`),
nothing changes on the wire and the call can stay.

If you did not, your payloads change shape, from a property dump to the literal:

| Type | 8.x without registration | 9.0 |
|---|---|---|
| `DateRange` | `{"Length":181}` | `"[2026-01-01,2026-06-30]"` |
| range through `object` | `{"Start":…,"End":…,"StartInclusive":true,…}` | the literal |
| `RangeSet<Int32Range, int>` | `[{"Length":5}]` | `"{[1,5]}"` |
| `StringSet` | `{"Values":["a","b"],"Count":2,"IsEmpty":false}` | `["a","b"]` |

None of the old forms could be read back, so only write-only consumers — a client reading `Length`
from a response, a stored document nobody deserializes — can depend on them. To keep an old shape
for a property, put your own `[JsonConverter]` on it or register one on the options; both outrank the
type's attribute.

### The literal ignores the culture

`Parse(s, provider)` and `TryParse(s, provider, …)` now read the invariant literal and ignore the
provider, and `ToString(null, provider)` — or `ToString("", provider)`, interpolation, or
`string.Format` — always writes the invariant literal. A provider still applies with an explicit,
non-empty format string.

Before, the provider reached each bound: `DateRange.Parse(s, th-TH)` was 543 years early,
`DecimalRange.Parse("[1.5,2.5)", de-DE)` was `[15,25)`, and `$"{decimalRange}"` under `de-DE` wrote
`[1,5,2)`, which parsed back as `[1,52)`. If you relied on culture-specific parsing — reading
`[01.01.2025,31.03.2025]` with `de-DE`, say — parse the bounds yourself and use the factories:

```csharp
var de    = CultureInfo.GetCultureInfo("de-DE");
var range = DateRange.CreateFinite(DateOnly.Parse("01.01.2025", de), DateOnly.Parse("31.03.2025", de));
```

Decimal bounds also parse with PostgreSQL's `numeric` grammar now: `"1,000"`, `"(5)"` and currency
symbols are a `FormatException` rather than 1000, −5 and 5. That includes the wrapper arities:
`DecimalSet<T>` and the integer ones read the element text with the primitive family's grammar before
the element's own `Parse` sees it, so a generated `Parse` that accepts group separators no longer
turns `{"1,5"}` into fifteen, and `DecimalSet<T>` now reads exponent notation (`{1e3}`, a JSON
`1.5e1`) as `DecimalSet` does.

### Temporal literals are strict ISO 8601

A temporal bound or element — through `Parse`, `TryParse`, JSON, the `TypeConverter` or configuration
binding — must be ISO 8601, as this library and PostgreSQL write it. The BCL parser used before read
whatever the invariant culture could, month first. These are now a `FormatException`:

| Input | Was read as | Write instead |
|---|---|---|
| `01.02.2027`, `01/02/2027`, `Feb 1 2027` | January 2 | `2027-02-01` |
| `2027-02-01` for a `DateTime` or `DateTimeOffset` | midnight | `2027-02-01T00:00` |
| a `DateTimeOffset` without an offset | the parsing machine's offset | append `Z` or the offset |
| `9:30 AM`, `9:30` | 09:30 | `09:30` |
| the same forms as JSON elements of a `DateTimeOffsetSet`, `DateTimeSet` or `TimeSet` | as above — System.Text.Json's converter read the offset-less ones in the machine's offset | as above, or register a converter for the element type |

A `DateTime` bound or element with an offset — `2026-01-01T09:30+05:30` — now keeps the wall clock
as written, with `DateTimeKind.Local`, as PostgreSQL's `timestamp` input does. It used to be
converted into the parsing machine's zone: 05:00 in Berlin, 04:00 on a UTC server, and a `tsrange`
stores that wall clock. If a payload carries offsets because it means instants, the type it wants is
`DateTimeOffsetRange` (`tstzrange`); convert explicitly (`ToUniversalTime()`) before building a
`DateTimeRange`. Offsets are `±HH`, `±HH:mm` or `±HH:mm:ss`; `+0530` and `+5:30` are refused.

A range bound that is a quoted `""` or only whitespace — `["",5)`, `( , )` — is now a `FormatException`
rather than an infinite bound, as in PostgreSQL; only an absent bound (`(,5)`) is infinite. A backslash
outside quotes now escapes the next character, in a range bound and in an array element alike, as
it does in PostgreSQL: `StringSet.Parse(@"{a\,b}")` is the one element `a,b`, where it was the two
elements `a\` and `b`. An array literal PostgreSQL refuses or reads as multi-dimensional — `{{a,b}}`,
`{a}b}`, `{a"b}` — is a `FormatException`; `{{a,b}}` used to be the set `{"{a","b}"}`. Only ASCII
whitespace is skipped around elements and bounds; a no-break space is now kept as element text (and
refused inside a range bound), where it was trimmed.

`HH:mm` without seconds, PostgreSQL's space-separated output, any number of fraction digits (truncated to 100 ns) and `infinity` / `-infinity` are accepted.
The temporal wrapper arities read the text as their primitive first, so a lenient generated element
`Parse` no longer sees non-ISO text, and an element that ignores the format specifier now fails on
read instead of truncating silently. The NodaTime types were already strict; they now also accept
`HH:mm` wherever the BCL types do — a `LocalTime`, the time part of a `LocalDateTime`, and an
`Instant` written with its offset or `Z` — so `LocalTimeSet.Parse("{09:00}")` reads like
`TimeRange.Parse("[09:00,17:00)")` instead of throwing.

### Temporal values reach the database as text at its resolution

The EF Core provider writes a temporal SQL constant, and a temporal value inside a JSON column, as
the value Npgsql would bind: truncated to the microsecond toward 2000-01-01, with the extremes as
`-infinity` / `infinity`. A constant used to carry seven fraction digits that the server rounded, so
it could miss the row a parameter matched. A temporal set whose elements are less than a microsecond
apart is now stored with one of them — the column could only ever hold one value, and the stored
array's `cardinality` disagreed with the set's `Count`. `ExecuteUpdate` of a value set inside a JSON
column is refused, however the setter reaches the property (it would have written a document the
row no longer loads from); setting a whole complex property that contains the set still works.

### A server-computed value set `Union` is refused wherever it would answer wrongly

`Union` translates to `array_cat`, a concatenation. 8.0 refused `==`/`Equals` over it in predicates
and `Count` over a bare union; everything else ran and answered differently from memory. These now
throw `InvalidOperationException` naming the remedy:

```csharp
db.Rows.Select(r => r.Tags.Union(x) == target);                    // was: false where memory says true
db.Rows.Where(r => (r.Id > 0 ? r.Tags.Union(x) : r.Tags) == target); // and the ?? form: was 0 rows
db.Rows.GroupBy(r => r.Tags.Union(x));                             // was: one group per operand order
db.Rows.Select(r => r.Tags.Union(x)).Distinct();                   // was: duplicates kept
db.Rows.OrderBy(r => r.Tags.Union(x));
db.Rows.Where(r => (r.OptionalTags ?? r.Tags.Union(x)).Count == 3); // was: duplicates counted
db.Rows.ExecuteUpdate(s => s.SetProperty(r => r.Tags, r => r.Tags.Union(x)));  // was: stored {a,c,a,b}
```

**A query that ran into one of these was already returning wrong rows**, and an `ExecuteUpdate` of
a union left rows that compare and count wrongly on the server until rewritten — load and save them
through `SaveChanges` to canonicalize. Use `IsSubsetOf`/`IsSupersetOf` (both together are set
equality), or materialize first. Projecting the union itself, and composing it with `Contains`,
`Overlaps`, the subset/superset family, `Remove` and `IsEmpty`, keep working.

### Three JSON precedence and coverage changes

- **A converter you register on the options for a range type now reaches its variants.** A range
  serialized through `object`, an `object` collection or a variant-typed property used to bypass it
  and write the literal; it now goes through your converter, so one payload no longer mixes two
  wire forms. If you relied on the mixed form, declare those properties as a type your converter
  does not claim.
- **A property declared as `IRange<T>`, a shape interface or `IValueSet<T>`** writes the literal (or
  array) of its runtime type, where it wrote `{}` or a property dump. Reading one still cannot work —
  the concrete type is unknown — and now throws `NotSupportedException`; declare the concrete type.
- **NodaTime:** `AddNodaTimeRangeConverters()` no longer outranks `ConfigureForNodaTime` when
  registered first, and a bare `LocalDate`, `LocalDateTime` or `YearMonth` in a non-ISO calendar is
  refused on write (`ArgumentException`), where it was written in its own calendar's fields and read
  back days off. The same holds for an element of `LocalDateSet<T>`, `LocalDateTimeSet<T>` or
  `YearMonthSet<T>`, in memory and through EF Core. Convert with `WithCalendar(CalendarSystem.Iso)`
  first.

### Ranges can be dictionary keys

`Dictionary<DateRange, …>` used to throw `NotSupportedException`; it now writes the literal as the
property name. Nothing to migrate unless you caught that exception.

### Things that start working

No edit needed; listed so a workaround can go:

- **Configuration binding** of a range, multirange or value set property from its literal bound
  `null` with no error. Every type now carries a `TypeConverter`, so it binds.
- **A range, multirange or value set inside an EF Core `ToJson()` column** could not be saved —
  `SaveChanges` threw `NullReferenceException` from inside EF. It is stored as the literal, and
  queries into the document translate.
- **Native AOT.** `RangeSet` and the validated-wrapper sets failed with "missing native code"; the
  core and NodaTime packages now build with `IsAotCompatible`, and a consumer needs only the
  source-generated JSON context AOT always needs.

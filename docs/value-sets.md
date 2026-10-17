# Value sets

A value set holds a set of tags, codes, IDs or dates, and you can store it in a native PostgreSQL
array column. Every set is immutable and deduplicated, sorted, and free of nulls.

```csharp
var tags = StringSet.From("beta", "alpha", "beta");   // {alpha,beta} — deduplicated, sorted
StringSet more = ["gamma", "alpha"];                  // collection expressions work

tags.Contains("alpha");      // true
tags.Overlaps(more);         // true  — shares "alpha"
tags.IsSubsetOf(more);       // false
tags.IsProperSubsetOf(more); // false — proper containment excludes equality
tags.Union(more);            // {alpha,beta,gamma}
tags.Remove("beta");         // {alpha}
tags.Count;                  // 2
tags.IsEmpty;                // false
```

You also get `Intersect`, `Except` and `Add`. Two sets with the same values are equal, whichever
order you gave the values in.

Good to know:

- **`Intersect`, `Except` and `Add` run in memory only.** PostgreSQL has no array intersection or
  difference operator, and it cannot insert a value at its sorted position.
- **An operation that changes nothing returns the same instance.**

## Pick a set type

Each set type has a matching PostgreSQL array type:

| .NET type | Element type | PostgreSQL column | With your own element type |
|---|---|---|---|
| `StringSet` | `string` | `text[]` | `StringSet<TElement>` |
| `GuidSet` | `Guid` | `uuid[]` | `GuidSet<TElement>` |
| `Int16Set` | `short` | `smallint[]` | `Int16Set<TElement>` |
| `Int32Set` | `int` | `integer[]` | `Int32Set<TElement>` |
| `Int64Set` | `long` | `bigint[]` | `Int64Set<TElement>` |
| `DecimalSet` | `decimal` | `numeric[]` | `DecimalSet<TElement>` |
| `DateSet` | `DateOnly` | `date[]` | `DateSet<TElement>` |
| `TimeSet` | `TimeOnly` | `time[]` | `TimeSet<TElement>` |
| `DateTimeSet` | `DateTime` | `timestamp[]` | `DateTimeSet<TElement>` |
| `DateTimeOffsetSet` | `DateTimeOffset` | `timestamptz[]` | `DateTimeOffsetSet<TElement>` |

The right-hand column holds your own element types, such as a typed key. See
[Use your own element types](#use-your-own-element-types).

The NodaTime package adds five more, each with a version for your own element types:

- **`LocalDateSet`** stores as `date[]`.
- **`LocalDateTimeSet`** stores as `timestamp[]`.
- **`InstantSet`** stores as `timestamptz[]`.
- **`LocalTimeSet`** stores as `time[]`. That is a built-in array type, so unlike `timerange` you
  don't need `CREATE TYPE`.
- **`YearMonthSet`** stores as a `date[]` of first-of-month dates.

A set relates to its array column the way `RangeSet<DateRange, DateOnly>` relates to a
`datemultirange`. The .NET type is the idea (a set), and the column is how it is stored (an array):

| .NET type | A set of… | PostgreSQL type |
|---|---|---|
| `RangeSet<DateRange, DateOnly>` | ranges | `datemultirange` |
| `StringSet` | values | `text[]` |

Good to know:

- **NodaTime dates are converted to the ISO calendar.** `LocalDate` and `LocalDateTime` elements
  are converted when you create the set. `YearMonth` elements must already be ISO, which matches
  the range types.
- **Every family has had a version for your own element types since 7.0.0.** The temporal ones
  have one extra requirement: your `ToString` must honour the format they ask for. See
  [Keep the text form of the underlying type](#keep-the-text-form-of-the-underlying-type).

## Why every set is sorted and deduplicated

Every way you create a set sorts it and removes duplicates: `From`, parsing, JSON, and loading it
from the database. This form is called canonical, and you get two things from it:

- **Change detection is cheap and exact.** EF Core's `ValueComparer` compares sets with a plain
  equality check, and never reports a change that isn't one.
- **SQL `=` means set equality.** Two stored arrays are equal exactly when the sets are.

The sort order is chosen on purpose:

- **String-backed sets sort ordinal.** They never use a culture-sensitive comparison. The order is
  part of what gets stored, not a display order. With a culture sort, two machines could store the
  same set in two different ways.
- **Every other set sorts by the element's own comparison.** That is numeric, chronological, or
  `Guid.CompareTo`.

PostgreSQL's array operators already behave like set operators. `@>`, `<@` and `&&` ignore order
and duplicates, so `ARRAY[1,1] <@ ARRAY[1]` is true. Only `=` compares arrays as sequences. Keeping
every set in canonical form makes `=` agree with the rest. PostgreSQL does the same thing for
discrete ranges and multiranges.

Good to know:

- **If you need a list, use an array.** When order matters or duplicates must stay, Npgsql already
  maps `T[]` and `List<T>` for you.

## Use your own element types

A set can hold your own types, such as typed keys or strongly typed IDs. Your type doesn't need to
reference this package. It only needs a few standard .NET interfaces, which validated-value
generators emit out of the box:

```csharp
// A generator-shaped wrapper: struct, IEquatable (record), IFormattable, IParsable.
public readonly record struct AccessRight : IFormattable, IParsable<AccessRight>
{
    private readonly string _value;
    private AccessRight(string value) => _value = value;

    public static AccessRight Parse(string s, IFormatProvider? provider)
        => Validate(s) ? new(s.Trim().ToLowerInvariant()) : throw new FormatException(…);
    public string ToString(string? format, IFormatProvider? formatProvider) => _value;
    // TryParse elided
}

StringSet<AccessRight> rights = [AccessRight.Parse("users.read", null)];
```

Every set family has a version like this: `StringSet<T>`, `GuidSet<T>`, `Int16Set<T>`,
`Int32Set<T>`, `Int64Set<T>`, `DecimalSet<T>`, `DateSet<T>`, `TimeSet<T>`, `DateTimeSet<T>`,
`DateTimeOffsetSet<T>`. The NodaTime package adds `LocalDateSet<T>`, `LocalDateTimeSet<T>`,
`InstantSet<T>`, `LocalTimeSet<T>` and `YearMonthSet<T>`.

Your type needs these interfaces:

- **`IFormattable`** gives the text that is stored.
- **`IParsable<TSelf>`** runs your validation again whenever a value is read. So if the database
  holds bad data, loading it throws, and no invalid value gets into your code.
- **`IComparable<TElement>`** is needed by every version except `StringSet<T>`. The sort order
  comes from the underlying type. `StringSet<T>` sorts the text ordinal and ignores your type's
  own comparison.

You don't write a private constructor, a source generator integration or a value converter. To
read a value, the set calls `TElement.Parse(text, invariant)`. To write one, it calls
`element.ToString(format, invariant)`. That is why Vogen, Metalama, StronglyTypedId and
hand-written wrappers all work the same way. A wrapper without `IParsable` doesn't work, whatever
generated it.

Good to know:

- **A lenient `Parse` never sees non-ISO text.** The temporal families first parse the text
  strictly as the underlying type. Then they pass your `Parse` that type's round-trip form.

### Why the element must be a struct

Every version requires `TElement` to be a `struct`. The rest of the library would accept a class:
`IValueSet<T>` and the core only require `IEquatable<T>`, and the plain `StringSet` holds
`string`. The struct rule is there for one reason.

**A sorted set searches its array with a binary search.** If the element were a class, your code
would keep a reference to an object inside that array. If you changed that object, the array would
no longer be sorted. `Contains` would then miss an element that is present, with no exception, and
the result would look like a genuine miss. With a struct, the array holds copies, so there is
nothing for your code to change.

The library prevents this kind of silent wrong answer everywhere else, and `CanonicalOrder`
already guards against it for a different cause. The alternative was to accept any `notnull` type
and document "elements must be immutable". That would replace a guarantee the compiler checks with
a second rule nothing can check, next to the text-form rule below.

**If your type is a class**, declare a `readonly record struct` for storage and convert at the
boundary. The struct only has to carry the underlying value's text. It doesn't have to be the type
your domain model uses as its identity.

### Keep the text form of the underlying type

One rule can't be written as a constraint, so it is a convention: *your element's text must be
exactly the text of the value inside it*. A decorative format such as `"CUST-{value}"` fails when
the value is saved, with an error that names this rule.

Each family asks your `ToString` for a particular format:

| Version | The format the family asks for |
|---|---|
| `StringSet<T>`, `GuidSet<T>`, the integer versions, `DecimalSet<T>` | the element's default (`null`) |
| `DateSet<T>` | `"yyyy-MM-dd"` |
| `TimeSet<T>`, `DateTimeSet<T>`, `DateTimeOffsetSet<T>` | `"O"` |
| the five NodaTime versions | the family's ISO pattern |

The temporal families ask for a specific format because the default one loses data. A `TimeOnly`
prints as `09:30`, and a `DateTime` as `06/15/2024 10:30:00`. With the default, every timestamp
would be stored cut off at the second, and would lose its `DateTimeKind`. NodaTime's default
output is the culture's long form, such as `Saturday, 15 June 2024`, so the NodaTime versions ask
for a format too.

A wrapper that passes its `format` argument on to the value inside already does this. That is what
the generators emit:

```csharp
public string ToString(string? format, IFormatProvider? provider)
    => _value.ToString(format, provider ?? CultureInfo.InvariantCulture);
```

A wrapper that ignores the argument and returns its own form is rejected. Its values are never
stored cut off.

Reading works the other way round, and the family decides it, not your element. Each version first
reads the text with the grammar of its underlying type:

- **`DecimalSet<T>`** uses PostgreSQL's `numeric` grammar.
- **The integer versions** read a signed integer.
- **The temporal and NodaTime versions** read strict ISO 8601.

Only then does your `Parse` get the parsed value's invariant text, so your validation still runs.
Your `Parse` might accept more than that. A generated `Parse` built on `decimal.Parse` with
`NumberStyles.Number`, for example, accepts group separators and rejects exponents. Even so,
`DecimalSet<Money>` reads `{"1,5"}` as a `FormatException` and `{1e3}` as 1000, exactly as
`DecimalSet` does. That holds for `Parse`, `TryParse`, JSON and configuration binding alike.

### How your element types sort and serialize

String-backed versions sort ordinal over the text. They ignore your type's own `IComparable` on
purpose, because generated implementations usually use culture-sensitive string comparison.

The same text goes into JSON:

- **`StringSet<AccessRight>`** writes `["users.read"]`.
- **`Int32Set<OrderId>`** writes `[1,2]`.
- **`DecimalSet<Money>`** writes `[12.50]`, keeping the scale.
- **`DateTimeSet<AuditStamp>`** writes ISO 8601 strings.

Reading JSON calls `Parse`, so your validation also runs on incoming payloads. If you want a
different JSON shape, give your type its own `[JsonConverter]`. It takes precedence.

For the string, Guid, integer and decimal versions, the JSON is **byte for byte the same** as for
the plain set. You can swap `Int32Set` for `Int32Set<OrderId>` and API clients see no difference.

The four temporal versions and the five NodaTime ones write the same token type and the same value,
but not the same bytes. They always write seven fraction digits, where System.Text.Json trims them.
And the default encoder escapes `+` in a string the converter writes itself:

```
DateTimeOffsetSet     ["2024-06-15T10:30:00+02:00"]
DateTimeOffsetSet<T>  ["2024-06-15T10:30:00.0000000+02:00"]
```

Each payload still reads into the other's type. The full round-trip form is deliberate. The array
literal and the EF Core package already use it, and it keeps fractions of a second. If you need to
keep an existing API response shape, give your type its own `[JsonConverter]`.

## Why these element types

The same choices as [for ranges](why.md#why-these-element-types) apply, with one difference. Ranges
have no `Guid`, because "every GUID between these two" means nothing. Sets do have `Guid`, because
membership is exactly what you ask of a collection of IDs.

These element types are left out on purpose:

- **`bool`** would be a set over just two values.
- **`float` and `double`** have NaN, which breaks ordering and equality. That is the same silent
  failure as for ranges.
- **`byte[]`** has nested elements of varying length, with no cheap canonical order.
- **`TimeSpan`** can't represent a PostgreSQL `interval` without loss, because an `interval` is a
  months, days and microseconds triple.

## Parse and print literals

`ToString()` gives you the PostgreSQL array literal, quoted the way the server quotes it. `Parse`
and `TryParse` read it back and put the set in canonical form:

```csharp
StringSet.From("a b", "plain").ToString();   // {"a b",plain}
Int32Set.Parse("{2,1,2}", null);             // {1,2} — normalizes on parse
StringSet.Parse("{a,NULL}", null);           // FormatException — sets never contain null
StringSet.Parse(@"{a\,b}", null);            // {"a,b"} — a backslash escapes, quoted or not
StringSet.Parse("{{a,b}}", null);            // FormatException — a two-dimensional array is not a set
```

The grammar is PostgreSQL's `array_in`.

Good to know:

- **Whitespace around an unquoted element is dropped.** Whitespace inside it is kept, so `{ a b }`
  is `{"a b"}`.
- **Some text is rejected.** An unescaped `{`, `}` or `"` inside an unquoted element fails, and so
  does text after a quoted element.
- **Only ASCII whitespace counts.** A no-break space is part of the element, as it is on the
  server.
- **Whitespace inside quotes depends on the element type,** as it does on the server. A string
  keeps it, so `{" ab"}` is `{" ab"}`. A number, date or time drops it from both ends, so
  `DateSet.Parse("{\" 2026-01-01\"}", null)` is `{2026-01-01}`. A backslash-escaped space works
  the same way, and so does a date or time in JSON.
- **Before 9.1, a date or time set kept it.** `{" 2026-01-01"}` failed (`FormatException`).

## Serialize to JSON

You don't register anything. Every set type carries its own converter, as the ranges do. A set
becomes a plain JSON array, such as `["alpha","beta"]`.

The elements are serialized by System.Text.Json, so your element converters apply. Reading a set
puts it in canonical form and rejects null elements. If the serializer doesn't know your element
type, the family's own [element converter](serialization.md#element-converters) handles it.

## Convert between sets and ranges

For integers and dates, the same values can be written two ways: `{1,2,3,7}` and `{[1,3],[7,7]}`
contain exactly the same values. You can convert between them:

```csharp
Int32Set.From(1, 2, 3, 7).ToRangeSet();   // { [1, 3], [7, 7] }
DateSet.From(fri, sat, sun, mon).ToRangeSet();  // { [fri, mon] } — one range

rangeSet.ToInt32Set();   // back to individual values
rangeSet.ToDateSet();
```

Which one to store depends on how dense your data is. A thousand consecutive dates are one
`daterange`, or a `date[]` with a thousand elements. Checking `@>` against the range column is much
cheaper. Sparse data goes the other way: a range for every single value costs more than the values
do.

Good to know:

- **Only the discrete families convert.** That is `Int32Set`, `Int64Set` and `DateSet`, and
  `LocalDateSet` and `YearMonthSet` in the NodaTime package. Continuous types have no step, so a
  range of them has no list of values to expand to.
- **Both directions run in memory only.** PostgreSQL converts between arrays and multiranges only
  through `unnest` and a custom aggregate, so neither conversion runs as SQL.
- **An unbounded range set throws instead of hanging.** Expanding one would never finish.

## When to use something else

A value set is for catalogs of values: tags, codes, keys, dates. The elements are data, not
references to other rows.

- **If the elements are rows in another table, use a junction table.** PostgreSQL can't put a
  foreign key on array elements, so a set can't give you referential integrity.
- **If order or duplicates matter, use a list.** Npgsql already maps `T[]` and `List<T>`.

# Value sets


A value set is an immutable, **canonical** set of scalar values: deduplicated, sorted, never containing null, with structural equality. It relates to a PostgreSQL array column exactly as `RangeSet<DateRange, DateOnly>` relates to `datemultirange` — the CLR type models the domain concept (a set), the column is its storage encoding (an array):

| CLR type | Canonical set of… | PostgreSQL shape |
|---|---|---|
| `RangeSet<DateRange, DateOnly>` | ranges | `datemultirange` |
| `StringSet` | values | `text[]` |

| .NET type | Element type | PostgreSQL column | Wrapper arity |
|---|---|---|---|
| `StringSet` | `string` | `text[]` | `StringSet<TElement>` |
| `GuidSet` | `Guid` | `uuid[]` | `GuidSet<TElement>` |
| `Int16Set` | `short` | `smallint[]` | — |
| `Int32Set` | `int` | `integer[]` | `Int32Set<TElement>` |
| `Int64Set` | `long` | `bigint[]` | `Int64Set<TElement>` |
| `DecimalSet` | `decimal` | `numeric[]` | — |
| `DateSet` | `DateOnly` | `date[]` | — |
| `TimeSet` | `TimeOnly` | `time[]` | — |
| `DateTimeSet` | `DateTime` | `timestamp[]` | — |
| `DateTimeOffsetSet` | `DateTimeOffset` | `timestamptz[]` | — |

The NodaTime satellite adds `LocalDateSet` (`date[]`), `LocalDateTimeSet` (`timestamp[]`), `InstantSet` (`timestamptz[]`), `LocalTimeSet` (`time[]` — a built-in array type, so unlike `timerange` no `CREATE TYPE` is needed) and `YearMonthSet` (month-aligned `date[]`). `LocalDate`/`LocalDateTime` elements normalize to the ISO calendar at construction; `YearMonth` elements must already be ISO, mirroring the range types.

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

`Intersect`, `Except` and `Add` are also available; they evaluate client-side only (PostgreSQL has no native array intersection or difference operator, and cannot insert at a sorted position), and operations that change nothing return the same instance.

## Canonical form is the contract

Every construction path deduplicates and sorts — `From`, parsing, JSON, and materialization from the database. This is load-bearing twice: the EF `ValueComparer` collapses to a cheap equality with no false diffs in change detection, and SQL `=` on the stored array coincides with set equality.

The order rules are deliberate:

- **String-backed sets sort ordinal** — never a culture-sensitive comparison. Canonical form is a cross-writer storage contract, not a display order; a culture sort would make two machines disagree about the same set.
- **Everything else sorts by the element's own comparison** (numeric, chronological, `Guid.CompareTo`).

PostgreSQL itself motivates the design: its array *query* algebra is already set-semantic — `@>`, `<@` and `&&` ignore both order and duplicates (`ARRAY[1,1] <@ ARRAY[1]` is true) — while only `=` compares arrays as sequences. Canonical form closes that split, the same way PostgreSQL itself canonicalizes discrete ranges and multiranges. Arrays that need to be *lists* (ordered, duplicates preserved) are a different concept — and one Npgsql already maps natively as `T[]`/`List<T>`.

## Validated wrapper elements

The wrapper arities carry domain values — typed keys, strongly typed IDs — without the domain type referencing this package. `TElement` is constrained only on BCL interfaces, which validated-value generators emit out of the box:

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

Every value set family has an arity: `StringSet<T>`, `GuidSet<T>`, `Int16Set<T>`, `Int32Set<T>`, `Int64Set<T>`, `DecimalSet<T>`, `DateSet<T>`, `TimeSet<T>`, `DateTimeSet<T>`, `DateTimeOffsetSet<T>`, and in the NodaTime satellite `LocalDateSet<T>`, `LocalDateTimeSet<T>`, `InstantSet<T>`, `LocalTimeSet<T>` and `YearMonthSet<T>`.

`IFormattable` supplies the element's backing text on the way out; `IParsable<TSelf>` **re-runs the element's validation on the way in**, so materializing corrupt data throws instead of smuggling invalid values into the domain. Every arity except `StringSet<T>` additionally requires `IComparable<TElement>` (canonical order delegates to the backing primitive).

### The text-form contract

One contract cannot be expressed in constraints and is convention instead: *the element's text form must be exactly the backing primitive's*. A decorative format (`"CUST-{value}"`) fails loudly at the persistence boundary with an error naming the contract.

*Which* text form differs by family, and the difference matters:

| Arity | The format the family asks for |
|---|---|
| `StringSet<T>`, `GuidSet<T>`, the integer arities, `DecimalSet<T>` | the element's default (`null`) |
| `DateSet<T>` | `"yyyy-MM-dd"` |
| `TimeSet<T>`, `DateTimeSet<T>`, `DateTimeOffsetSet<T>` | `"O"` |
| the five NodaTime arities | the family's ISO pattern |

The temporal families pin a format because the default one loses data: a `TimeOnly` renders as `09:30` and a `DateTime` as `06/15/2024 10:30:00`, so an arity that took the element's default would store every timestamp truncated to the second, and every `DateTimeKind` with it. NodaTime's null-format output is the culture's form — `Saturday, 15 June 2024` — for the same reason.

In practice this asks nothing extra of a wrapper that forwards its `format` argument to the value it wraps, which is what the generators emit:

```csharp
public string ToString(string? format, IFormatProvider? provider)
    => _value.ToString(format, provider ?? CultureInfo.InvariantCulture);
```

A wrapper that swallows the argument and returns its own form is rejected rather than silently truncated.

### Ordering and JSON

String-backed wrappers sort ordinal over their text form — deliberately not the element's own `IComparable`, whose generated implementations typically delegate to culture-sensitive string comparison.

That same text form carries into JSON, so a wrapper set is indistinguishable on the wire from the primitive set it replaces — `StringSet<AccessRight>` writes `["users.read"]`, `Int32Set<OrderId>` writes `[1,2]`, `DecimalSet<Money>` writes `[12.50]` with the scale intact, `DateTimeSet<AuditStamp>` writes ISO 8601 strings — and reads run `Parse`, so the validation above applies to deserialized payloads too. Give the element type its own `[JsonConverter]` if you want a different shape; it takes precedence.

## Why these element types

The same vetting as [for ranges](../README.md#why-these-element-types) applies, with one notable difference: `Guid` is absent from ranges ("every GUID between these two" has no domain meaning) but present in sets — *membership* is exactly the question ID collections ask. Excluded, deliberately: `bool` (a set over a two-value domain), `float`/`double` (NaN breaks total order and equality — the same quiet failure as for ranges), `byte[]` (nested variable-length elements have no cheap canonical order), and `TimeSpan` (PostgreSQL `interval` is a months/days/microseconds triple that `TimeSpan` cannot represent losslessly).

## Literals, parsing, JSON

`ToString()` produces the PostgreSQL array literal, with the same quoting rules the server uses; `Parse`/`TryParse` accept it back, normalizing to canonical form:

```csharp
StringSet.From("a b", "plain").ToString();   // {"a b",plain}
Int32Set.Parse("{2,1,2}", null);             // {1,2} — normalizes on parse
StringSet.Parse("{a,NULL}", null);           // FormatException — sets never contain null
```

JSON serialization goes through the same converter factory as the ranges (`options.AddRangeConverters()`) and produces plain JSON arrays (`["alpha","beta"]`), delegating element serialization to System.Text.Json — element converters apply. Reads normalize and reject null elements. Element types the serializer does not know natively are covered by the family's own [element converter](serialization.md#element-converters).

## Converting between sets and ranges

Over a discrete domain the same membership has two shapes: `{1,2,3,7}` and `{[1,3],[7,7]}` contain exactly the same values. Which one to store is a question of density, and the conversion moves between them:

```csharp
Int32Set.From(1, 2, 3, 7).ToRangeSet();   // { [1, 3], [7, 7] }
DateSet.From(fri, sat, sun, mon).ToRangeSet();  // { [fri, mon] } — one range

rangeSet.ToInt32Set();   // back to individual values
rangeSet.ToDateSet();
```

A thousand consecutive dates are one `daterange` and a thousand-element `date[]`, and `@>` against the range column is the cheaper question by a wide margin. Sparse data goes the other way, where ranges of one value each cost more than the values do.

Only the discrete families convert — `Int32Set`, `Int64Set`, `DateSet`, and `LocalDateSet`/`YearMonthSet` in the NodaTime satellite. The continuous domains have no step, so there is no set of values to expand to. Both directions run client-side and neither translates: PostgreSQL converts between arrays and multiranges only through `unnest` and a custom aggregate. Expanding an unbounded range set throws rather than hanging.

## When not to use a set

A value set is for *value catalogs*: tags, codes, keys, dates — elements that are data, not entity references. If the elements are rows in another table and you need referential integrity, PostgreSQL cannot put a foreign key on array elements (a long-standing limitation); use a junction table. If you need order-as-data or duplicates, you want a list, which Npgsql's native `T[]`/`List<T>` mapping already serves.


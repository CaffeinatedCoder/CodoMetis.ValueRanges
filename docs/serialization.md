# Literals, parsing, and JSON

You can turn every range type, every `RangeSet<TRange, T>` and every value set into text and back.
The text is the PostgreSQL literal, the same text the server itself reads and prints. They also
round-trip through System.Text.Json with no setup: a range or `RangeSet` as that literal, and a
value set as a JSON array.

## Write and read literals

All range types and `RangeSet<TRange, T>` implement `IParsable<T>` and `IFormattable`. Their text
form is the PostgreSQL range literal, the same syntax PostgreSQL uses.

### Print a range as a literal

```csharp
Int32Range.CreateFinite(1, 10).ToString()              // "[1,10]"
Int32Range.CreateFinite(1, 10, endInclusive: false)
          .ToString()                                  // "[1,9]"  (a discrete type canonicalizes)
Int32Range.CreateUnboundedStart(5).ToString()          // "(,4]"
Int32Range.CreateUnboundedEnd(5).ToString()            // "[5,)"
Int32Range.Infinite.ToString()                         // "(,)"
Int32Range.Empty.ToString()                            // "empty"

DateRange.CreateFinite(new DateOnly(2025, 1, 1),
                       new DateOnly(2025, 3, 31)).ToString()
// "[2025-01-01,2025-03-31]"

DateTimeOffsetRange.CreateFinite(
    new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.FromHours(1)),
    new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.FromHours(1))).ToString()
// "[2024-06-01T00:00:00.0000000+01:00,2024-07-01T00:00:00.0000000+01:00)"
```

`ToString()` and `IFormattable.ToString(format, provider)` both give you the PostgreSQL range
literal.

The optional `format` is passed on to the element type, so you control how each bound is printed:

```csharp
((IFormattable)DateRange.CreateFinite(new DateOnly(2025, 1, 1),
                                      new DateOnly(2025, 3, 31)))
    .ToString("MMM d yyyy", CultureInfo.InvariantCulture)
// "[Jan 1 2025,Mar 31 2025]"
```

That is display text, not a literal. It doesn't parse back, because temporal literals are ISO 8601
only (see [Dates and times are strict ISO 8601](#dates-and-times-are-strict-iso-8601)).

**The literal never depends on a culture.** Without a format string, you get the invariant literal,
whatever provider you pass and whatever the current culture is. That holds for `ToString()`,
`ToString(null, provider)` and `ToString("", provider)`, for string interpolation, `string.Format`
and logging templates. An empty format is the general format, as everywhere in .NET. A provider
applies only together with an explicit format, because then you are asking for display text
rather than a literal:

```csharp
var thai = CultureInfo.GetCultureInfo("th-TH");
((IFormattable)range).ToString(null, thai);           // "[2025-01-01,2025-03-31]" — the literal
((IFormattable)range).ToString("yyyy-MM-dd", thai);   // "[2568-01-01,2568-03-31]" — Buddhist-era display text
```

`RangeSet<TRange, T>` prints as a PostgreSQL multirange literal:

```csharp
IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(7, 10)])
      .ToString()    // "{[1,5],[7,10]}"

IntSet.Empty.ToString()    // "{}"
IntSet.Infinite.ToString() // "{(,)}"
```

Good to know:

- **Before 9.0, a provider reached the element formatter even without a format.** `th-TH` wrote
  Buddhist-era years. `de-DE` wrote a decimal comma that collides with the bound separator
  (`[1,5,2)`). A null provider meant the *current* culture for `Int32Range`, `Int64Range` and
  `DecimalRange`.

### Parse a literal

Every concrete range type has static `Parse` and `TryParse` methods. They accept any valid
PostgreSQL range literal:

```csharp
var r1 = Int32Range.Parse("[1,10]", null);     // Finite [1, 10]
var r2 = Int32Range.Parse("(,5]", null);       // UnboundedStart (−∞, 5]
var r3 = Int32Range.Parse("[3,)", null);        // UnboundedEnd [3, +∞)
var r4 = Int32Range.Parse("(,)", null);         // Infinity (−∞, +∞)
var r5 = Int32Range.Parse("empty", null);       // Empty

if (Int32Range.TryParse(userInput, null, out var range))
    Console.WriteLine(range);
```

`Parse` and `TryParse` read the invariant literal and **ignore the provider**, as `Guid.Parse`
does. The text is PostgreSQL's, and it has no culture.

Decimal bounds follow PostgreSQL's `numeric` grammar: sign, digits, point and exponent. A group
separator or a currency symbol fails (`FormatException`).

A decimal bound or element is read exactly or not at all. Text that names a number `decimal` can't
hold, such as a 29th significant digit or `1e-30`, fails (`FormatException`) rather than being
rounded to the nearest value. PostgreSQL keeps every digit, so a rounded value would be a different
number from the server's. Before 10.0, `[1.00000000000000000000000000001,2)` read as
`[1.0000000000000000000000000000,2)` and contained 1, where the server's range doesn't.

A literal whose lower bound is greater than its upper bound fails (`FormatException`), as it does
in PostgreSQL. `TryParse` returns `false`, and JSON and the `TypeConverter` throw. Equal bounds that
aren't both inclusive are fine and read as `empty`, so `[5,5)` is `empty` and `[5,1)` fails. Before
10.0, `[5,1)` read as `empty`, and an inverted range inside a multirange literal disappeared from the
set.

Discrete types are normalized when you parse them. `"[1,10)"` and `"[1,9]"` hold the same values,
and both parse to the same closed range `[1, 9]`:

```csharp
Int32Range.Parse("[1,10)", null).ToString()  // "[1,9]"
```

`RangeSet<TRange, T>` parses multirange literals the same way:

```csharp
var set = RangeSet<Int32Range, int>.Parse("{[1,5],[7,10]}", null);
set.Count;   // 2
set[0];      // [1, 5]
set[1];      // [7, 10]
```

Good to know:

- **Before 9.0, the provider reached the element parser.**
  `DateRange.Parse("[2026-01-01,2026-06-30]", th-TH)` returned a range in 1483, and
  `DecimalRange.Parse("[1.5,2.5)", de-DE)` returned `[15,25)`. Neither threw.

### Dates and times are strict ISO 8601

Temporal bounds and elements accept strict ISO 8601 only. That means the forms this library writes
and the forms PostgreSQL prints in its default ISO `DateStyle`, and nothing else:

- **A date** is `yyyy-MM-dd`.
- **A time** is `HH:mm`, or `HH:mm:ss` with any number of fraction digits. The fraction is
  truncated to the 100 ns the BCL types hold. Go and Java write nine digits.
- **A date and a time** are joined by `T` or a space.
- **A `DateTime`** takes an optional `Z`, read as `Utc`, or an offset `±HH`, `±HH:mm` or
  `±HH:mm:ss`, read as `Local`. The wall clock is kept as written, and the offset is never applied.
- **A `DateTimeOffset`** needs a `Z`, `±HH`, `±HH:mm` or `±HH:mm:ss`. The last form is how
  PostgreSQL writes its historical local-mean-time offsets. You get that instant at offset zero.
- **`infinity` and `-infinity`** read as `MaxValue` and `MinValue`. That is what Npgsql gives you
  when it reads them.
- **An exclusive `infinity` excludes the extreme.** `[2026-01-01,infinity)` reads as
  `[2026-01-01,9999-12-30]` for `DateRange` and `LocalDateRange`, as the same value read from a
  column does. The extreme stands for the infinity, so PostgreSQL's range also holds a 9999-12-31 that
  has no value of its own here ([Domain width](architecture.md#domain-width-and-the-boundary)).
- **More than six fraction digits are three values.** `10:00:00.1234567` is .1234567 here.
  PostgreSQL reads the same text rounded to the microsecond, .123457, and the EF Core package stores
  it truncated, .123456, as Npgsql binds it. They are at most 1 µs apart. Your own values keep every
  digit through their literal and JSON, so round to the microsecond yourself if a value must equal
  what the server reads from its text ([Domain width](architecture.md#domain-width-and-the-boundary)).

The `DateTime` rule is PostgreSQL's own rule for `timestamp` input, and it is what a `tsrange`
stores. So one literal is one value on every machine, and a local value round-trips with its kind.

The NodaTime types follow the same grammar, `HH:mm` included. An `Instant` still needs its offset
or `Z`.

A closed temporal set reads its JSON array with this grammar too. A converter you register for the
element type still wins. Writing is unchanged: the set writes the elements in System.Text.Json's
form.

Good to know:

- **Before 9.0, a lenient BCL parser read temporal text.** It read `[01.02.2027,05.03.2027)` month
  first, as January 2 to May 2. A `DateTimeOffset` without an offset took the parsing machine's
  offset.
- **Before 9.0, System.Text.Json's own converter read a closed temporal set's JSON.** That
  converter is ISO 8601 too. But it reads a `DateTimeOffset` without an offset, or a bare date, in
  the parsing machine's offset. So one payload held different instants on different servers.

### Parse from a span

Every type you can parse implements `ISpanParsable<T>`. So `Parse` and `TryParse` also take a
`ReadOnlySpan<char>`:

```csharp
ReadOnlySpan<char> line = "period=[2024-01-01,2024-12-31];rate=4.5".AsSpan();

var period = DateRange.Parse(line[7..29], null);   // no substring allocated
var tags   = StringSet.Parse("{a,b}".AsSpan(), null);
var blocks = RangeSet<Int32Range, int>.Parse("{[1,5],[7,10]}".AsSpan(), null);
```

The literals are parsed over spans internally either way. The span overload saves the substring
allocation when what you have is a slice of a larger buffer.

`ISpanParsable<T>` extends `IParsable<T>`. So the `string` overloads keep working unchanged, and so
does any generic code constrained on `IParsable<T>`.

Good to know:

- **In generic code, a `string` argument can bind to the span overload.** Where a type parameter is
  constrained to `IRangeFactory` or `IValueSetFactory`, both overloads are visible. A `string`
  argument then binds to the span one through the implicit conversion. Each type's two overloads
  are the same call, so your results don't change.

### Quote a bound

PostgreSQL lets you quote a bound, so it can hold commas, brackets or other characters that would
otherwise confuse the parser:

```csharp
Int32Range.Parse("[\"1\",\"10\"]", null);   // [1, 10]
```

A bound is read the way PostgreSQL's `range_in` reads it:

- **A double quote** opens or closes a quoted section anywhere in a bound, so `"1"2` is `12`.
- **A doubled quote** inside a quoted section is a literal `"`.
- **A backslash** takes the next character literally, inside quotes or out, so `[\1,5)` is
  `[1,5)`.
- **Whitespace at either end of a bound** is dropped, inside quotes or out. So
  `[" 2026-01-01",2026-02-01)` is `[2026-01-01,2026-02-01)`.

Good to know:

- **Only an absent bound is infinite.** `(,5)` is unbounded below. `["",5)` and `[ ,5)` are not:
  their lower bound is present and empty. No element type reads an empty bound, so they fail
  (`FormatException`), as PostgreSQL does with `invalid input syntax for type integer: ""`.
- **Before 9.0, an empty quoted bound read as infinite.** `["",5)` read as `(,4]`, and `("","")`
  as `(,)`.
- **Only ASCII whitespace is dropped:** space, tab, newline, carriage return, vertical tab and form
  feed. A no-break space stays in the bound, so the bound fails, as it does in PostgreSQL.
- **Before 9.1, whitespace inside quotes was kept.** `[" 2026-01-01",2026-02-01)` failed
  (`FormatException`), where PostgreSQL reads it.

### Bind a range from configuration

You can bind a range, a `RangeSet` or a value set from configuration:

```json
{ "Billing": { "Period": "[2026-01-01,2026-06-30]", "Blocks": "{[1,5],[7,10]}", "Tags": "{alpha,beta}" } }
```

```csharp
var billing = configuration.GetSection("Billing").Get<BillingOptions>();   // Period is a DateRange
```

This works because every type carries a `TypeConverter` to and from the same literal. Anything
built on `TypeDescriptor` reads it, and `Microsoft.Extensions.Configuration` binding is the main
one.

The converter always parses and formats invariantly. It ignores the culture it is handed, because
the literal is the same text everywhere.

Good to know:

- **An empty string converts to `null`.** It never becomes the empty range, which is the literal
  `empty`.
- **A malformed literal fails the bind.** You get a `FormatException` inside the binder's
  `InvalidOperationException`.
- **A property typed as one shape rejects the others.** A `DateRange.Finite` property fails on a
  literal of another shape.
- **Before 9.0 there was no converter.** Configuration binding of a range property produced `null`,
  with no error. ASP.NET Core model binding was never affected: minimal APIs and MVC bind query,
  route and form values through the static `TryParse`.

## Serialize to JSON

Ranges serialize as JSON strings that hold the PostgreSQL literal. That keeps them compact, and
they round-trip. The System.Text.Json converters live in the `CodoMetis.ValueRanges.Serialization`
namespace. They cover every range type and its multirange counterpart.

### What you need to register

Nothing:

```csharp
JsonSerializer.Serialize(DateRange.CreateFinite(new DateOnly(2026, 1, 1),
                                                new DateOnly(2026, 6, 30)));
// "[2026-01-01,2026-06-30]"
```

Every range type carries its converter as a `[JsonConverter]` attribute. So does each of its five
shapes (`Finite`, `UnboundedStart`, …), `RangeSet<TRange, T>` and every value set. So plain
`JsonSerializer.Serialize(range)` writes the literal, and an ASP.NET Core host binds request bodies
with no setup.

The attribute is the lowest-precedence way for System.Text.Json to pick a converter. A converter
you register on the options or put on a property still wins.

`AddRangeConverters()` and `RangeJsonConverterFactory` are still there and give the same output.
Registering them does no harm, and you no longer need to:

```csharp
using CodoMetis.ValueRanges.Serialization;

var options = new JsonSerializerOptions().AddRangeConverters();   // same wire form as without
```

Good to know:

- **Before 9.0 the converters were opt-in.** Without the registration, System.Text.Json fell back
  to dumping public properties. `DateRange` wrote `{"Length":181}`, and none of it could be read
  back.

### Use source generation

A `JsonSerializerContext` honours the attributes too. If it serializes a value set, list the set's
element type as well, even `string`:

```csharp
[JsonSerializable(typeof(Booking))]          // holds a DateRange and a StringSet
[JsonSerializable(typeof(string))]           // the StringSet's element type
internal partial class ApiJsonContext : JsonSerializerContext;
```

The element type must be listed because the set converter resolves the element's contract through
the options. For Native AOT, see [Native AOT](#native-aot).

Good to know:

- **Most types name a closed converter.** The range types, their shapes and the non-generic value
  sets name a closed converter type, which the generator creates directly.
- **The generic types name a factory.** `RangeSet<TRange, T>` and the sets for your own element
  types name `RangeJsonConverterFactory`, because an attribute can't name an open generic
  converter.

### Round-trip a range through JSON

```csharp
var range = Int32Range.CreateFinite(1, 10);
string json = JsonSerializer.Serialize(range);   // "\"[1,10]\""

var back = JsonSerializer.Deserialize<Int32Range>(json);
// back == Int32Range.CreateFinite(1, 10)

// Multirange
var set = RangeSet<Int32Range, int>.From([
    Int32Range.CreateFinite(1, 5),
    Int32Range.CreateFinite(7, 10)
]);
string setJson = JsonSerializer.Serialize(set);   // "\"{[1,5],[7,10]}\""

// Works with every range type and its multirange counterpart
var dates = JsonSerializer.Serialize(
    DateRange.CreateFinite(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));
// "\"[2025-01-01,2025-12-31]\""
```

`null` round-trips as `null`, in both directions, for every type here: ranges, shapes, `RangeSet`
and the value sets. It behaves exactly as for any other reference-typed property.

`null` stays distinct from the empty range, which is the literal `"empty"`. A missing value and an
empty range are different facts with different JSON text, and neither is read as the other. A
malformed literal still fails (`JsonException`).

Good to know:

- **Before 6.2, the range converters rejected a null *token* on read**, while writing `null` on the
  way out. So a payload the package produced could not be read back. If you relied on that
  exception to reject a null where you expected a non-nullable range, the property now receives
  `null` instead.

### Declare a range as `object`, a shape or an interface

A range's shapes serialize to the same literal as the range type. So a range reached through
`object` is not a special case. That includes a boxed value, an `object`-typed property and a
mixed collection:

```csharp
JsonSerializer.Serialize<object>(Int32Range.CreateFinite(1, 5));   // "\"[1,5]\""
JsonSerializer.Serialize(new List<object> { range, dateRange });   // ["[1,5]","[2024-01-01,2024-03-01]"]
```

Reading into a property typed as a shape works too. A literal of the wrong shape fails instead of
widening: `"empty"` is not an `Int32Range.Finite`, so reading it throws `JsonException`.

A value declared as one of the package's interfaces writes the same JSON as its concrete type:

```csharp
record Booking(IRange<int> Seats, IValueSet<string> Tags);

JsonSerializer.Serialize(new Booking(Int32Range.CreateFinite(1, 4), StringSet.From("a")));
// {"Seats":"[1,4]","Tags":["a"]}
JsonSerializer.Deserialize<Booking>(json);   // NotSupportedException: declare the concrete type
```

That covers `IRange<T>`, the shape interfaces such as `IFiniteRange<T>`, and `IValueSet<T>`. The
value is written through its runtime type's converter, so a converter you registered for that type
applies. Reading into an interface throws `NotSupportedException`, because the interface doesn't
say which type to construct. Declare the concrete type instead; it reads the same JSON. `null`
round-trips as for any reference type.

**Your own converter for a range type also reaches its shapes.** System.Text.Json picks a
converter by the exact type it is handed. A range reached through `object` (a boxed value, an
`object` collection, a dictionary of `object`) is handed over as its shape. So is a range declared
as a shape (`Int32Range.Finite`). The shape's own attribute would write the literal. So the shape's
converter first looks in `Converters` for the converter System.Text.Json would choose for the range
type. That is the first one that handles it: a `JsonConverter<Int32Range>`, or a factory that
produces one. Unless that converter is this package's own, the shape's converter hands the value to
it. That applies to values and dictionary keys, on write and on read. A read goes through the range
type and then narrows, so a property typed as a shape still rejects a value of another shape
(`JsonException`). One payload therefore has one JSON form per type, however you declare its
values.

Good to know:

- **A converter on a property stays scoped to that property.**
- **A converter registered for a shape type itself wins for that shape**, as for any type.
- **A source-generated context must list the runtime type** of a value declared as an interface.
  Otherwise the write fails and names the type.
- **Before 9.0, a converter on the options for `Int32Range` reached only values declared as
  `Int32Range`.** `Serialize(range)` used it, while `Serialize<object>(range)`, a `List<object>` and a
  shape-typed property wrote the literal.
- **Before 9.0, the interfaces carried no converter.** System.Text.Json dumped the interface's
  members: `IRange<int>` wrote `{}` and `IValueSet<string>` wrote `{"Values":["a"]}`. Neither
  threw, and neither read back.

### Use a range as a dictionary key

A range, a shape or a `RangeSet` can be a dictionary key:

```csharp
var days = new Dictionary<DateRange, int>
{
    [DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30))] = 181
};
JsonSerializer.Serialize(days);   // {"[2026-01-01,2026-06-30]":181}
```

The property name is the same literal as the value. It is read back through the same parse. So it
is normalized (`"[1,10)"` is the key `[1,9]`), and a malformed key fails (`JsonException`).

`JsonSerializerOptions.DictionaryKeyPolicy` isn't applied to these keys. System.Text.Json applies
it to string keys only, and a naming policy would turn a literal into text that no longer parses.

**Two spellings can be one key.** Because keys are normalized, `{"[1,5]":"a","[1,6)":"b"}` names
the key `[1,5]` twice. For a `RangeSet`, so does `{"{[1,5]}":…,"{[1,3],[4,5]}":…}`. System.Text.Json
treats that like any duplicate key. By default the later value silently replaces the earlier one,
as it would for `{"k":1,"k":2}`.

Where a dropped entry matters, set `AllowDuplicateProperties = false` (.NET 10). Configuration,
permissions and anything keyed by a period a client chose are examples. It checks duplicates on the
parsed key, so two spellings of one key fail (`JsonException`), like a literal duplicate:

```csharp
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.AllowDuplicateProperties = false);
```

Good to know:

- **Value sets can't be keys.** Their JSON is an array, not a string.

### When a literal does not parse

A literal that doesn't parse fails with a `JsonException`. The message names the target type and
quotes the input: `Cannot parse '[2026-13-01,2026-14-01]' as DateRange.` It quotes at most 64
characters, plus the input's length when it is longer.

The excerpt is there on purpose:

- **It makes a 400 diagnosable.** System.Text.Json's own messages name only the JSON path.
- **It is bounded.** A hostile payload can't become an equally large exception message in every
  log sink. `ParserResilienceTests` holds that bound.
- **It follows the BCL.** `int.Parse` quotes its input too.

Good to know:

- **Your clients may see the excerpt.** If your host returns exception messages to clients (MVC
  does by default, through `JsonOptions.AllowInputFormatterExceptionMessages`), the client sees an
  excerpt of what it sent itself.

### Element converters

A value set serializes as a plain JSON array, and System.Text.Json serializes its elements. So your
element converters decide how the elements look, whether you register them on the options, on the
property or on the element type.

If System.Text.Json knows nothing about the element type, that would go wrong silently. It would
write an object of the element's properties, and read `default`. A set family prevents that by
supplying a fallback converter:

```csharp
static JsonConverter<LocalDate>? IValueSetFactory<LocalDateSet, LocalDate>.ElementJsonConverter
    => /* ISO 8601, the same text form the array literals use */;
```

The fallback is used last: only when System.Text.Json has no scalar converter for the element type
at all. So a converter you register by any of the three usual routes still wins.

- **The sets over primitive types** serialize natively and leave the fallback at the default
  `null`.
- **Every set for your own element types defines one**, because its element type is whatever you
  supply. Each writes the same token type as its plain sibling. The integer versions write a JSON
  number, and `DecimalSet<T>` a JSON number that keeps the element's scale. The string, Guid,
  temporal and NodaTime versions write a JSON string holding the family's text form.
- **The five NodaTime sets define one too.**

For the non-temporal families that makes the JSON byte for byte the same: `Int32Set<OrderId>` and
`Int32Set` are indistinguishable, and they accept the same payloads: a quoted number only under
`JsonNumberHandling.AllowReadingFromString`, a Guid only in its hyphenated form. The temporal
versions write the same value in slightly different text (seven fraction digits, and `+` escaped
by the default encoder). See [value sets](value-sets.md#how-your-element-types-sort-and-serialize).

That fallback is why the NodaTime sets need no configuration:

```csharp
JsonSerializer.Serialize(LocalDateSet.From(new LocalDate(2024, 1, 1)));   // ["2024-01-01"]
```

The NodaTime package also ships `AddNodaTimeRangeConverters()`, which registers the same element
converters on the options. Use it to extend the ISO 8601 form to bare NodaTime properties *next to*
a set, which the fallback doesn't reach. See the
[NodaTime package README](../src/CodoMetis.ValueRanges.NodaTime/README.md#json).

Good to know:

- **Those converters are a fallback too.** Any other converter on the options for the same type
  wins, in either registration order. So NodaTime.Serialization.SystemTextJson stays in charge.
- **A non-ISO calendar fails on write.** A bare `LocalDate`, `LocalDateTime` or `YearMonth` in a
  calendar other than ISO throws `ArgumentException`, as NodaTime's own converter does.

## Native AOT

Every type works under Native AOT and trimming. You register nothing beyond what source generation
always needs. Both packages build with `IsAotCompatible` and raise no trim or AOT warnings when you
publish your project. A NativeAOT consumer in this repository's smoke test exercises them.

Use a source-generated context, as for any AOT JSON. List the types you serialize. For a value set,
also list its element type, because the set converter resolves the element's contract through the
options:

```csharp
[JsonSerializable(typeof(Booking))]                     // holds a DateRange and a RangeSet<DateRange, DateOnly>
[JsonSerializable(typeof(StringSet<PermissionKey>))]
[JsonSerializable(typeof(PermissionKey))]               // the set's element type
internal partial class ApiJsonContext : JsonSerializerContext;
```

If you leave the element type out, the first use fails loudly, in both directions. System.Text.Json
throws a `NotSupportedException` that names it:
`JsonTypeInfo metadata for type 'PermissionKey' was not provided by TypeInfoResolver …`. That holds
even for the families that bring their own element converter: the sets for your own element types
and the NodaTime sets. The set converter asks the options for the element's contract first. It
uses its own converter only for an element the options know but can't write as a scalar.

Under Native AOT, System.Text.Json also turns off reflection-based serialization. So
`JsonSerializer.Serialize(value)` with default options throws `InvalidOperationException` for every
type, these included, whether typed or through `object`. The attributes take effect through the
context: pass its options or its type info.

The generic types, `RangeSet<TRange, T>` and the sets for your own element types, get their
converters from a registration each type makes for itself. They never use `MakeGenericType`, so a
type your program only ever deserializes works too. If trimming ever removes that registration, the
converter lookup throws a `NotSupportedException` that names the type. Register its closed
converter on the options to fix it:

```csharp
options.Converters.Add(new ValueSetJsonConverter<StringSet<PermissionKey>, PermissionKey>());
```

Good to know:

- **Before 9.0, `RangeSet` and the sets for your own element types failed under Native AOT**
  with "missing native code". Their converters were built by reflection over instantiations the AOT compiler never
  generated.

## OpenAPI

ASP.NET Core's OpenAPI generator (`Microsoft.AspNetCore.OpenApi`) describes a range, multirange or
value set in a request or response body as `{}`, an unconstrained value. The generator builds
schemas from System.Text.Json's contract, and it can't see through a custom converter. .NET 10 has
no public API for a converter to declare its schema.

Query, route and header parameters are fine. They bind through `TryParse`, and ASP.NET Core
describes them as `type: string` on its own.

The package ships no OpenAPI dependency. Instead, copy this schema transformer into your API
project. This repository compiles and tests it, verbatim, against `Microsoft.AspNetCore.OpenApi`
10:

```csharp
using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.Core;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Describes CodoMetis.ValueRanges types in ASP.NET Core's OpenAPI document as they appear on the
/// wire. Without it a range, multirange or value set is described as <c>{}</c>: the generator
/// cannot see through a custom JSON converter. Register it with
/// <c>builder.Services.AddOpenApi(o => o.AddSchemaTransformer&lt;ValueRangesSchemaTransformer&gt;());</c>
/// </summary>
public sealed class ValueRangesSchemaTransformer : IOpenApiSchemaTransformer
{
    // Mirrors what the parsers accept: surrounding whitespace, "empty" in any case, quoted bounds.
    private const string RangePattern      = @"^\s*([Ee][Mm][Pp][Tt][Yy]|[\[(][\s\S]*,[\s\S]*[\])])\s*$";
    private const string MultirangePattern = @"^\s*\{[\s\S]*\}\s*$";

    public async Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RangeSet<,>))
        {
            var element = type.GetGenericArguments()[1];
            schema.Type        = JsonSchemaType.String;
            schema.Pattern     = MultirangePattern;
            schema.Description = $"A PostgreSQL multirange literal over {element.Name}: ranges in braces, "
                               + "e.g. {[1,5],[7,10]}, or {} for none.";
        }
        else if (FactoryElement(type, typeof(IRangeFactory<,>)) is { } rangeElement)
        {
            schema.Type        = JsonSchemaType.String;
            schema.Pattern     = RangePattern;
            schema.Description = $"A PostgreSQL range literal over {rangeElement.Name}: [ ] include a bound, "
                               + "( ) exclude it, an omitted bound is unbounded, e.g. [1,10) or [5,); "
                               + "or 'empty'.";
        }
        else if (FactoryElement(type, typeof(IValueSetFactory<,>)) is { } setElement)
        {
            schema.Type        = JsonSchemaType.Array;
            schema.UniqueItems = true;
            schema.Items       = await context.GetOrCreateSchemaAsync(setElement, cancellationToken: cancellationToken);
            schema.Description = $"A set of distinct {setElement.Name} values.";
        }
    }

    // A range variant (DateRange.Finite, …) inherits its union's IRangeFactory, so it is described too.
    private static Type? FactoryElement(Type type, Type openFactory)
        => Array.Find(type.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == openFactory)
               ?.GetGenericArguments()[1];
}
```

Then register it:

```csharp
builder.Services.AddOpenApi(o => o.AddSchemaTransformer<ValueRangesSchemaTransformer>());
```

What it produces:

| Type | Schema |
|---|---|
| a range type or variant (`DateRange`, `DateRange.Finite`, …) | `type: string`, a `pattern` for the range literal, a description naming the element type |
| `RangeSet<TRange, T>` | `type: string`, a `pattern` for the multirange literal |
| a value set (`StringSet`, `Int32Set<OrderId>`, …) | `type: array`, `uniqueItems: true`, `items` from the element type's own schema |

The patterns are loose on purpose. They accept everything the parsers accept: surrounding
whitespace, `empty` in any case, and quoted bounds that contain commas. A pattern that rejected a
valid literal would make a validating client reject good input. The patterns show a reader the
shape, and the server's parser has the final say.

Good to know:

- **An element type with its own `[JsonConverter]` keeps its own schema.** A wrapper element with
  its own converter is described by whatever that element's schema is. That is again `{}`, unless
  you describe it too.

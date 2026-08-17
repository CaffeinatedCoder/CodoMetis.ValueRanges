# Literals, parsing, and JSON

Every range type, `RangeSet<TRange, T>` and every value set round-trips through the PostgreSQL
literal syntax — the same text the server puts on the wire — and through System.Text.Json.

## Parsing and Formatting

All range types and `RangeSet<TRange, T>` implement `IParsable<T>` and `IFormattable`. The canonical string representation is the PostgreSQL range literal format — the same syntax PostgreSQL uses on the wire.

### Formatting

`ToString()` (and `IFormattable.ToString(format, provider)`) produces PostgreSQL range literals:

```csharp
Int32Range.CreateFinite(1, 10).ToString()              // "[1,10]"
Int32Range.CreateFinite(1, 10, endInclusive: false)
          .ToString()                                  // "[1,10)"
Int32Range.CreateUnboundedStart(5).ToString()          // "(,5]"
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

The optional `format` parameter is forwarded to the element type, so you can control how individual bound values are rendered:

```csharp
((IFormattable)DateRange.CreateFinite(new DateOnly(2025, 1, 1),
                                      new DateOnly(2025, 3, 31)))
    .ToString("MMM d yyyy", CultureInfo.InvariantCulture)
// "[Jan 1 2025,Mar 31 2025]"
```

`RangeSet<TRange, T>` formats as a PostgreSQL multirange literal:

```csharp
IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(7, 10)])
      .ToString()    // "{[1,5],[7,10]}"

IntSet.Empty.ToString()    // "{}"
IntSet.Infinite.ToString() // "{(,)}"
```

### Parsing

Every concrete range type exposes `Parse` and `TryParse` static methods that accept any valid PostgreSQL range literal:

```csharp
var r1 = Int32Range.Parse("[1,10]", null);     // Finite [1, 10]
var r2 = Int32Range.Parse("(,5]", null);       // UnboundedStart (−∞, 5]
var r3 = Int32Range.Parse("[3,)", null);        // UnboundedEnd [3, +∞)
var r4 = Int32Range.Parse("(,)", null);         // Infinity (−∞, +∞)
var r5 = Int32Range.Parse("empty", null);       // Empty

if (Int32Range.TryParse(userInput, null, out var range))
    Console.WriteLine(range);
```

Discrete types canonicalize on parse — `"[1,10)"` is equivalent to `"[1,9]"` and both parse to the same closed `[1, 9]` range:

```csharp
Int32Range.Parse("[1,10)", null).ToString()  // "[1,9]"
```

`RangeSet<TRange, T>` parses multirange literals in the same way:

```csharp
var set = RangeSet<Int32Range, int>.Parse("{[1,5],[7,10]}", null);
set.Count;   // 2
set[0];      // [1, 5]
set[1];      // [7, 10]
```

### Parsing from a span

Every parsable type implements `ISpanParsable<T>`, so `Parse` and `TryParse` also take a `ReadOnlySpan<char>`. The literal grammars are parsed over spans internally either way — the overload just removes the substring allocation when what you have is a slice of a larger buffer:

```csharp
ReadOnlySpan<char> line = "period=[2024-01-01,2024-12-31];rate=4.5".AsSpan();

var period = DateRange.Parse(line[7..29], null);   // no substring allocated
var tags   = StringSet.Parse("{a,b}".AsSpan(), null);
var blocks = RangeSet<Int32Range, int>.Parse("{[1,5],[7,10]}".AsSpan(), null);
```

`ISpanParsable<T>` extends `IParsable<T>`, so the `string` overloads and any generic code constrained on `IParsable<T>` keep working unchanged. One thing to know if you write generic code over these types: where a type parameter is constrained to `IRangeFactory` or `IValueSetFactory`, both overloads are now visible and a `string` argument binds to the span one through the implicit conversion. Each type's two overloads are the same call, so results are unaffected.

### Quoted bounds

PostgreSQL allows quoting individual bounds to embed commas, brackets, or other characters that would otherwise confuse the parser:

```csharp
Int32Range.Parse("[\"1\",\"10\"]", null);   // [1, 10]
```

Inside quotes, `\"` is unescaped to `"` and `\\` to `\`, matching PostgreSQL's quoted-bound syntax. The no-quote fast path stays allocation-free; unescaping only runs when a backslash is actually present inside the quotes.

## JSON Serialization

The `CodoMetis.ValueRanges.Serialization` namespace provides `System.Text.Json` converters for all range types and their multirange counterparts. Ranges serialize as JSON strings in PostgreSQL literal format — compact and round-trippable.

### Registration

Register all converters at once using the `AddRangeConverters()` extension:

```csharp
using CodoMetis.ValueRanges.Serialization;

var options = new JsonSerializerOptions().AddRangeConverters();
```

Or use the factory for automatic registration on any range/multirange type:

```csharp
var options = new JsonSerializerOptions
{
    Converters = { new RangeJsonConverterFactory() }
};
```

In ASP.NET Core, add it to your serializer configuration:

```csharp
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.AddRangeConverters());
```

### Usage

```csharp
var range = Int32Range.CreateFinite(1, 10);
string json = JsonSerializer.Serialize(range, options);   // "\"[1,10]\""

var back = JsonSerializer.Deserialize<Int32Range>(json, options);
// back == Int32Range.CreateFinite(1, 10)

// Multirange
var set = RangeSet<Int32Range, int>.From([
    Int32Range.CreateFinite(1, 5),
    Int32Range.CreateFinite(7, 10)
]);
string setJson = JsonSerializer.Serialize(set, options);   // "\"{[1,5],[7,10]}\""

// Works with all six range types and their multirange counterparts
var dates = JsonSerializer.Serialize(
    DateRange.CreateFinite(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)), options);
// "\"[2025-01-01,2025-12-31]\""
```

`null` round-trips as `null`, in both directions and for every type here — ranges, variants, `RangeSet` and the value sets — exactly as any other reference-typed property does. It stays distinct from the empty range, which is the literal `"empty"`: a missing value and an empty interval are different facts with different wire forms, and neither is read as the other. A malformed literal is still rejected with `JsonException`.

> Before 6.2, the range converters rejected a null *token* on read while writing `null` on the way out — so a payload the package produced could not be read back. If you depended on that exception to reject a null where a non-nullable range was expected, the property now receives `null` instead.

The union's sealed variants serialize to the same literal, so a range reached through `object` — a boxed value, an `object`-typed property, a heterogeneous collection — is not a special case:

```csharp
JsonSerializer.Serialize<object>(Int32Range.CreateFinite(1, 5), options);   // "\"[1,5]\""
JsonSerializer.Serialize(new List<object> { range, dateRange }, options);   // ["[1,5]","[2024-01-01,2024-03-01]"]
```

Reading into a variant-typed declaration works too, and refuses a literal of the wrong shape: `"empty"` is not an `Int32Range.Finite`, so it throws `JsonException` rather than widening. A property declared as the `IRange<T>` interface is *not* covered — the interface carries no factory to parse back through; declare it as the union type.

### Element converters

Value sets serialize as plain JSON arrays and delegate their elements to System.Text.Json, which keeps element converters authoritative — registered on the options, on the property, or on the element type. For element types the serializer knows nothing about, that delegation would silently produce an object of the element's properties on write and `default` on read. A set family closes that hole by supplying a fallback:

```csharp
static JsonConverter<LocalDate>? IValueSetFactory<LocalDateSet, LocalDate>.ElementJsonConverter
    => /* ISO 8601, the same text form the array literals use */;
```

It is consulted last — only when System.Text.Json has no scalar converter for the element type at all — so registering one by any of the three normal routes still wins. The primitive-backed families serialize natively and leave it at the default `null`. Every wrapper arity defines one, because its element type is whatever you supply, and each writes the token type its primitive sibling writes: the integer arities a JSON number, `DecimalSet<T>` a JSON number keeping the element's scale, and the string, Guid, temporal and NodaTime arities a JSON string holding the family's text form. For the non-temporal families that makes the payload byte-identical — `Int32Set<OrderId>` and `Int32Set` are indistinguishable. The temporal arities write the same value in slightly different text (seven fraction digits, and `+` escaped by the default encoder); see [value sets](value-sets.md#ordering-and-json). The five NodaTime sets define one too, which is why they need no configuration:

```csharp
var options = new JsonSerializerOptions().AddRangeConverters();

JsonSerializer.Serialize(LocalDateSet.From(new LocalDate(2024, 1, 1)), options);   // ["2024-01-01"]
```

The satellite also ships `AddNodaTimeRangeConverters()`, which registers the same element converters on the options. That extends the ISO 8601 form to bare NodaTime properties sitting *alongside* a set, which the fallback does not reach — see the [satellite README](../src/CodoMetis.ValueRanges.NodaTime/README.md#json).


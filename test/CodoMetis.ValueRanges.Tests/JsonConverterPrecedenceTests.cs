using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Since 9.0.0 every range, variant, multirange and value set carries its converter as a
/// type-level <c>[JsonConverter]</c>, so the literal is the wire form with no registration. These
/// tests pin what that attribute must not change: it is the lowest-precedence way a converter can
/// be chosen, so one registered on the options or on a property still wins. The per-type sweep
/// over every shape and family lives in the conventions project (<c>JsonDefaultContractTests</c>).
/// </summary>
[TestClass]
public class JsonConverterPrecedenceTests
{
    private static readonly DateRange FirstHalf2026 =
        DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

    /// <summary>
    /// The case that was measured: with nothing registered, <c>DateRange</c> wrote
    /// <c>{"Length":181}</c> and reading the literal threw.
    /// </summary>
    [TestMethod]
    public void Unregistered_DateRange_WritesTheLiteral_AndReadsItBack()
    {
        var json = JsonSerializer.Serialize(FirstHalf2026);

        Assert.AreEqual("\"[2026-01-01,2026-06-30]\"", json);
        Assert.AreEqual(FirstHalf2026, JsonSerializer.Deserialize<DateRange>(json));
    }

    /// <summary>ASP.NET Core's defaults: the web options, a range in a body property.</summary>
    [TestMethod]
    public void Unregistered_WebDefaults_RoundTripABody()
    {
        var body = new Booking(FirstHalf2026, StringSet.From("b", "a"));

        var json = JsonSerializer.Serialize(body, JsonSerializerOptions.Web);

        Assert.AreEqual("""{"period":"[2026-01-01,2026-06-30]","tags":["a","b"]}""", json);
        Assert.AreEqual(body, JsonSerializer.Deserialize<Booking>(json, JsonSerializerOptions.Web));
    }

    [TestMethod]
    public void ConverterOnTheOptions_WinsOverTheTypeAttribute()
    {
        var options = new JsonSerializerOptions { Converters = { new MarkerConverter() } };

        Assert.AreEqual("\"marker\"", JsonSerializer.Serialize(FirstHalf2026, options));
    }

    [TestMethod]
    public void ConverterOnTheProperty_WinsOverTheTypeAttribute()
        => Assert.AreEqual("""{"Period":"marker"}""", JsonSerializer.Serialize(new Annotated(FirstHalf2026)));

    /// <summary>
    /// <c>AddRangeConverters()</c> remains valid and changes nothing: the factory on the options
    /// produces the same converter the attribute names.
    /// </summary>
    [TestMethod]
    public void AddRangeConverters_IsRedundantButHarmless()
    {
        var registered = new JsonSerializerOptions().AddRangeConverters();
        var set        = RangeSet<DateRange, DateOnly>.From([FirstHalf2026]);

        Assert.AreEqual(JsonSerializer.Serialize(FirstHalf2026), JsonSerializer.Serialize(FirstHalf2026, registered));
        Assert.AreEqual(JsonSerializer.Serialize(set), JsonSerializer.Serialize(set, registered));
    }

    /// <summary>
    /// A source-generated context — the trimming- and AOT-compatible path — honours the type-level
    /// attribute too. The unions, variants and non-generic sets name a closed converter type, which
    /// the generator instantiates directly; <c>RangeSet</c> and the wrapper arities name the factory.
    /// </summary>
    [TestMethod]
    public void SourceGeneratedContext_UsesTheLiteral()
    {
        var ctx      = PrecedenceJsonContext.Default;
        var set      = RangeSet<DateRange, DateOnly>.From([FirstHalf2026]);
        var finite   = (DateRange.Finite)FirstHalf2026;
        var keys     = StringSet<TestPermission>.From(TestPermission.Parse("users.read", null));

        Assert.AreEqual("\"[2026-01-01,2026-06-30]\"", JsonSerializer.Serialize(FirstHalf2026, ctx.DateRange));
        Assert.AreEqual("\"[2026-01-01,2026-06-30]\"", JsonSerializer.Serialize(finite, ctx.Finite));
        Assert.AreEqual("\"{[2026-01-01,2026-06-30]}\"", JsonSerializer.Serialize(set, ctx.RangeSetDateRangeDateOnly));
        Assert.AreEqual("""["users.read"]""", JsonSerializer.Serialize(keys, ctx.StringSetTestPermission));
        Assert.AreEqual("""["a","b"]""", JsonSerializer.Serialize(StringSet.From("b", "a"), ctx.StringSet));

        Assert.AreEqual(FirstHalf2026, JsonSerializer.Deserialize("\"[2026-01-01,2026-06-30]\"", ctx.DateRange));
        Assert.AreEqual(set, JsonSerializer.Deserialize("\"{[2026-01-01,2026-06-30]}\"", ctx.RangeSetDateRangeDateOnly));
    }

    // -----------------------------------------------------------------------
    // A union converter on the options reaches the union's variants
    // -----------------------------------------------------------------------

    private static readonly Int32Range OneTwo = Int32Range.CreateFinite(1, 2);

    private static JsonSerializerOptions WithTaggedConverter() => new() { Converters = { new TaggedInt32RangeConverter() } };

    /// <summary>
    /// System.Text.Json resolves a converter by the exact type it is handed, which for a value
    /// reached through <see langword="object"/>, an <c>object</c> collection or a variant-typed
    /// declaration is the variant — and the variant's own <c>[JsonConverter]</c> answered with the
    /// literal. One payload carried two wire forms for the same type.
    /// </summary>
    [TestMethod]
    public void UnionConverterOnTheOptions_ReachesEveryDeclaration()
    {
        var options = WithTaggedConverter();
        var finite  = (Int32Range.Finite)OneTwo;

        Assert.AreEqual("\"tag:[1,2]\"", JsonSerializer.Serialize(OneTwo, options), "declared as the union");
        Assert.AreEqual("\"tag:[1,2]\"", JsonSerializer.Serialize(finite, options), "declared as the variant");
        Assert.AreEqual("\"tag:[1,2]\"", JsonSerializer.Serialize<object>(OneTwo, options), "through object");
        Assert.AreEqual("""["tag:[1,2]"]""", JsonSerializer.Serialize(new List<object> { OneTwo }, options), "List<object>");
        Assert.AreEqual("""{"a":"tag:[1,2]"}""",
            JsonSerializer.Serialize(new Dictionary<string, object> { ["a"] = OneTwo }, options), "dictionary value");
        Assert.AreEqual("""{"Period":"tag:[1,2]"}""", JsonSerializer.Serialize(new VariantHolder(finite), options),
            "variant-typed property");
        Assert.AreEqual("""{"Period":"tag:[1,2]"}""", JsonSerializer.Serialize(new ObjectHolder(OneTwo), options),
            "object-typed property");
    }

    [TestMethod]
    public void UnionConverterOnTheOptions_ReadsIntoAVariant_AndNarrows()
    {
        var options = WithTaggedConverter();

        Assert.AreEqual(OneTwo, JsonSerializer.Deserialize<Int32Range.Finite>("\"tag:[1,2]\"", options));
        Assert.AreEqual(new VariantHolder((Int32Range.Finite)OneTwo),
            JsonSerializer.Deserialize<VariantHolder>("""{"Period":"tag:[1,2]"}""", options));

        // The literal is not the wire form under this converter, so it is not accepted either.
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Int32Range.Finite>("\"[1,2]\"", options));

        // Narrowing stays the variant's, as without a registration: empty is not a Finite.
        var narrowed = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Int32Range.Finite>("\"tag:empty\"", options));
        StringAssert.Contains(narrowed.Message, "EmptyRange");
    }

    [TestMethod]
    public void UnionConverterOnTheOptions_ReachesVariantTypedDictionaryKeys()
    {
        var options = WithTaggedConverter();
        var keyed   = new Dictionary<Int32Range.Finite, int> { [(Int32Range.Finite)OneTwo] = 1 };

        var json = JsonSerializer.Serialize(keyed, options);

        Assert.AreEqual("""{"tag:[1,2]":1}""", json);
        Assert.AreEqual(OneTwo, JsonSerializer.Deserialize<Dictionary<Int32Range.Finite, int>>(json, options)!.Keys.Single());
    }

    /// <summary>
    /// The variant follows the union's resolution exactly, so first match still decides: with the
    /// package's factory ahead of the custom converter, the union gets the literal — and so does
    /// the variant.
    /// </summary>
    [TestMethod]
    public void UnionConverterOnTheOptions_VariantFollowsFirstMatch()
    {
        var options = new JsonSerializerOptions { Converters = { new RangeJsonConverterFactory(), new TaggedInt32RangeConverter() } };

        Assert.AreEqual("\"[1,2]\"", JsonSerializer.Serialize(OneTwo, options));
        Assert.AreEqual("\"[1,2]\"", JsonSerializer.Serialize<object>(OneTwo, options));
    }

    /// <summary>
    /// Already correct, pinned: <c>RangeSet</c> has no variants, so its runtime type is its declared
    /// type and a converter on the options reaches it through <see langword="object"/> too.
    /// </summary>
    [TestMethod]
    public void RangeSetConverterOnTheOptions_ReachesObjectDeclarations()
    {
        var options = new JsonSerializerOptions { Converters = { new TaggedRangeSetConverter() } };
        var set     = RangeSet<Int32Range, int>.From([OneTwo]);

        Assert.AreEqual("\"set\"", JsonSerializer.Serialize(set, options));
        Assert.AreEqual("\"set\"", JsonSerializer.Serialize<object>(set, options));
        Assert.AreEqual("""["set"]""", JsonSerializer.Serialize(new List<object> { set }, options));
    }

    internal sealed record VariantHolder(Int32Range.Finite Period);

    internal sealed record ObjectHolder(object Period);

    /// <summary>A round-trippable custom wire form: the literal behind a prefix.</summary>
    private sealed class TaggedInt32RangeConverter : JsonConverter<Int32Range>
    {
        public override Int32Range Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, Int32Range value, JsonSerializerOptions options)
            => writer.WriteStringValue($"tag:{value}");

        public override Int32Range ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString()!);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, Int32Range value, JsonSerializerOptions options)
            => writer.WritePropertyName($"tag:{value}");

        private static Int32Range Parse(string s)
            => s.StartsWith("tag:", StringComparison.Ordinal)
                   ? Int32Range.Parse(s["tag:".Length..], null)
                   : throw new JsonException($"Expected a tagged literal, got '{s}'.");
    }

    private sealed class TaggedRangeSetConverter : JsonConverter<RangeSet<Int32Range, int>>
    {
        public override RangeSet<Int32Range, int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, RangeSet<Int32Range, int> value, JsonSerializerOptions options)
            => writer.WriteStringValue("set");
    }

    internal sealed record Booking(DateRange Period, StringSet Tags);

    internal sealed record Annotated([property: JsonConverter(typeof(MarkerConverter))] DateRange Period);

    private sealed class MarkerConverter : JsonConverter<DateRange>
    {
        public override DateRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, DateRange value, JsonSerializerOptions options)
            => writer.WriteStringValue("marker");
    }
}

[JsonSerializable(typeof(DateRange))]
[JsonSerializable(typeof(DateRange.Finite))]
[JsonSerializable(typeof(RangeSet<DateRange, DateOnly>))]
[JsonSerializable(typeof(StringSet<TestPermission>))]
[JsonSerializable(typeof(StringSet))]
// A set converter resolves its element contract through the options, so a context must list the
// element type, even a primitive one. Ranges and multiranges write their own strings.
[JsonSerializable(typeof(TestPermission))]
[JsonSerializable(typeof(string))]
internal sealed partial class PrecedenceJsonContext : JsonSerializerContext;

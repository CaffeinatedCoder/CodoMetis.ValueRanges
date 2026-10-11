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

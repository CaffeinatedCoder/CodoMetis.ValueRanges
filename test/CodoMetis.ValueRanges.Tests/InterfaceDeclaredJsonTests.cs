using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// A value declared as one of the range or value set <em>interfaces</em>. System.Text.Json
/// resolves the declared type, and an interface carried no converter, so it fell back to a
/// property dump of the interface's members: <c>IRange&lt;int&gt;</c> wrote <c>{}</c> and
/// <c>IValueSet&lt;string&gt;</c> wrote <c>{"Values":["a"]}</c>, silently, and neither read back.
/// Writes now go through the runtime type's own converter; reads are refused with the remedy,
/// since an interface does not say which type to construct.
/// </summary>
[TestClass]
public class InterfaceDeclaredJsonTests
{
    private static readonly Int32Range OneTwo = Int32Range.CreateFinite(1, 2);

    [TestMethod]
    public void RangeInterfaceProperty_WritesTheLiteral()
    {
        Assert.AreEqual("""{"Range":"[1,2]"}""", JsonSerializer.Serialize(new RangeHolder(OneTwo)));
        Assert.AreEqual("""{"Range":"empty"}""", JsonSerializer.Serialize(new RangeHolder(Int32Range.Empty)));
        Assert.AreEqual("""["[1,2]","(,)"]""",
            JsonSerializer.Serialize(new List<IRange<int>> { OneTwo, Int32Range.Infinite }));
    }

    /// <summary>Each shape interface is a declared type of its own; an attribute is not inherited.</summary>
    [TestMethod]
    public void ShapeInterfaces_WriteTheLiteral()
    {
        AssertWritesTheLiteral<IFiniteRange<int>>((Int32Range.Finite)OneTwo);
        AssertWritesTheLiteral<IUnboundedStartRange<int>>((Int32Range.UnboundedStart)Int32Range.CreateUnboundedStart(5));
        AssertWritesTheLiteral<IUnboundedEndRange<int>>((Int32Range.UnboundedEnd)Int32Range.CreateUnboundedEnd(5));
        AssertWritesTheLiteral<IEmptyRange<int>>((Int32Range.EmptyRange)Int32Range.Empty);
        AssertWritesTheLiteral<IInfinityRange<int>>((Int32Range.Infinity)Int32Range.Infinite);
    }

    private static void AssertWritesTheLiteral<TInterface>(TInterface value)
        => Assert.AreEqual($"\"{value}\"", JsonSerializer.Serialize(value), typeof(TInterface).Name);

    /// <summary>The package's factory on the options serves the interfaces the same way.</summary>
    [TestMethod]
    public void UnderAddRangeConverters_TheSameWireForm()
    {
        var options = new JsonSerializerOptions().AddRangeConverters();

        Assert.AreEqual("""{"Range":"[1,2]"}""", JsonSerializer.Serialize(new RangeHolder(OneTwo), options));
        Assert.AreEqual("""{"Tags":["a"]}""", JsonSerializer.Serialize(new SetHolder(StringSet.From("a")), options));
        Assert.ThrowsExactly<NotSupportedException>(() => JsonSerializer.Deserialize<RangeHolder>("""{"Range":"[1,2]"}""", options));
    }

    [TestMethod]
    public void SetInterfaceProperty_WritesTheArray()
    {
        Assert.AreEqual("""{"Tags":["a","b"]}""", JsonSerializer.Serialize(new SetHolder(StringSet.From("b", "a"))));
        Assert.AreEqual("[1,2]", JsonSerializer.Serialize<IValueSet<int>>(Int32Set.From(2, 1)));
    }

    [TestMethod]
    public void InterfaceDeclarations_RefuseToRead_NamingTheRemedy()
    {
        var range = Assert.ThrowsExactly<NotSupportedException>(
            () => JsonSerializer.Deserialize<RangeHolder>("""{"Range":"[1,2]"}"""));
        StringAssert.Contains(range.Message, "IRange<Int32>");
        StringAssert.Contains(range.Message, "Declare the property as the concrete type");

        var set = Assert.ThrowsExactly<NotSupportedException>(
            () => JsonSerializer.Deserialize<SetHolder>("""{"Tags":["a"]}"""));
        StringAssert.Contains(set.Message, "IValueSet<String>");

        Assert.ThrowsExactly<NotSupportedException>(() => JsonSerializer.Deserialize<IFiniteRange<int>>("\"[1,2]\""));
    }

    /// <summary>Null is System.Text.Json's, as for any reference type: written and read as null.</summary>
    [TestMethod]
    public void Null_RoundTrips()
    {
        Assert.AreEqual("""{"Range":null}""", JsonSerializer.Serialize(new RangeHolder(null)));
        Assert.IsNull(JsonSerializer.Deserialize<RangeHolder>("""{"Range":null}""")!.Range);
        Assert.IsNull(JsonSerializer.Deserialize<SetHolder>("""{"Tags":null}""")!.Tags);
    }

    /// <summary>
    /// The write follows the runtime type's resolution, so a converter on the options for the
    /// range type applies here too, as it does to every other declaration of that type.
    /// </summary>
    [TestMethod]
    public void Write_FollowsTheRuntimeTypesConverter()
    {
        var options = new JsonSerializerOptions { Converters = { new MarkerConverter() } };

        Assert.AreEqual("""{"Range":"marker"}""", JsonSerializer.Serialize(new RangeHolder(OneTwo), options));
    }

    /// <summary>
    /// The source-generated path honours the interface attribute too. The runtime type must be in
    /// the context — the write asks the options for its contract, and fails loudly naming it if not.
    /// </summary>
    [TestMethod]
    public void SourceGeneratedContext_WritesTheLiteral()
    {
        var ctx = InterfaceJsonContext.Default;

        Assert.AreEqual("""{"Range":"[1,2]"}""", JsonSerializer.Serialize(new RangeHolder(OneTwo), ctx.RangeHolder));
        Assert.AreEqual("""{"Tags":["a","b"]}""", JsonSerializer.Serialize(new SetHolder(StringSet.From("b", "a")), ctx.SetHolder));
        Assert.ThrowsExactly<NotSupportedException>(
            () => JsonSerializer.Serialize(new RangeHolder(Int32Range.Empty), ctx.RangeHolder));
    }

    internal sealed record RangeHolder(IRange<int>? Range);

    internal sealed record SetHolder(IValueSet<string>? Tags);

    private sealed class MarkerConverter : JsonConverter<Int32Range>
    {
        public override Int32Range Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, Int32Range value, JsonSerializerOptions options)
            => writer.WriteStringValue("marker");
    }
}

[JsonSerializable(typeof(InterfaceDeclaredJsonTests.RangeHolder))]
[JsonSerializable(typeof(InterfaceDeclaredJsonTests.SetHolder))]
// The runtime types the holders are written with. Int32Range.EmptyRange is deliberately absent.
[JsonSerializable(typeof(Int32Range.Finite))]
[JsonSerializable(typeof(StringSet))]
[JsonSerializable(typeof(string))]
internal sealed partial class InterfaceJsonContext : JsonSerializerContext;

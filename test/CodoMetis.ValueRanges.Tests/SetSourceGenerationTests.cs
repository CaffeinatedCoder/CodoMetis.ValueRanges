using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The metadata a source-generated <see cref="JsonSerializerContext"/> needs for a value set: the set
/// and its element type, as <c>docs/serialization.md</c> documents. Both halves of that are pinned —
/// the documented context works, and one that omits the element fails loudly, naming the element,
/// rather than writing or reading anything.
/// </summary>
/// <remarks>
/// The set converter asks the options for the element's contract first, so the omission fails even
/// for a family that supplies its own element converter (the validated-wrapper arities and the
/// NodaTime sets): that fallback is consulted only for an element the options know but have no
/// scalar converter for. This is behaviour of the context's metadata, which is the same under the
/// JIT, so it is tested here; the NativeAOT consumer in <c>test/consumer-smoke-test.sh</c> is what
/// checks that the code is compiled.
/// </remarks>
[TestClass]
public class SetSourceGenerationTests
{
    [TestMethod]
    public void DocumentedContext_RoundTripsAPrimitiveSet()
    {
        var tags = StringSet.From("beta", "alpha");

        var json = JsonSerializer.Serialize(tags, SetAndElementJsonContext.Default.StringSet);

        Assert.AreEqual("""["alpha","beta"]""", json);
        Assert.AreEqual(tags, JsonSerializer.Deserialize(json, SetAndElementJsonContext.Default.StringSet));
    }

    /// <summary><c>TestIntId</c> has no converter of its own, so the family's element converter writes it.</summary>
    [TestMethod]
    public void DocumentedContext_RoundTripsAWrapperSet()
    {
        var ids = Int32Set<TestIntId>.From(TestIntId.Parse("7", null), TestIntId.Parse("3", null));

        var json = JsonSerializer.Serialize(ids, SetAndElementJsonContext.Default.Int32SetTestIntId);

        Assert.AreEqual("[3,7]", json);
        Assert.AreEqual(ids, JsonSerializer.Deserialize(json, SetAndElementJsonContext.Default.Int32SetTestIntId));
    }

    [TestMethod]
    public void OmittedElementType_IsRefusedNamingIt_ForAPrimitiveSet()
    {
        var write = Assert.ThrowsExactly<NotSupportedException>(
            () => JsonSerializer.Serialize(StringSet.From("a"), SetOnlyJsonContext.Default.StringSet));
        var read = Assert.ThrowsExactly<NotSupportedException>(
            () => JsonSerializer.Deserialize("""["a"]""", SetOnlyJsonContext.Default.StringSet));

        StringAssert.Contains(write.Message, "System.String");
        StringAssert.Contains(read.Message, "System.String");
    }

    [TestMethod]
    public void OmittedElementType_IsRefusedNamingIt_EvenWithAFamilyElementConverter()
    {
        var write = Assert.ThrowsExactly<NotSupportedException>(
            () => JsonSerializer.Serialize(Int32Set<TestIntId>.From(TestIntId.Parse("7", null)),
                                           SetOnlyJsonContext.Default.Int32SetTestIntId));
        var read = Assert.ThrowsExactly<NotSupportedException>(
            () => JsonSerializer.Deserialize("[7]", SetOnlyJsonContext.Default.Int32SetTestIntId));

        StringAssert.Contains(write.Message, nameof(TestIntId));
        StringAssert.Contains(read.Message, nameof(TestIntId));
    }
}

[JsonSerializable(typeof(StringSet))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Int32Set<TestIntId>))]
[JsonSerializable(typeof(TestIntId))]
internal sealed partial class SetAndElementJsonContext : JsonSerializerContext;

// The set alone — what a consumer writes first, and what the docs say is not enough.
[JsonSerializable(typeof(StringSet))]
[JsonSerializable(typeof(Int32Set<TestIntId>))]
internal sealed partial class SetOnlyJsonContext : JsonSerializerContext;

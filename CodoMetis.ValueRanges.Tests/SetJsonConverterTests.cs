using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class SetJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddRangeConverters();

    [TestMethod]
    public void Serialize_Int32Set_AsCanonicalJsonArray()
        => Assert.AreEqual("[1,2]", JsonSerializer.Serialize(Int32Set.From(2, 1), Options));

    [TestMethod]
    public void Serialize_StringSet_AsJsonArray()
        => Assert.AreEqual("[\"a\",\"b\"]", JsonSerializer.Serialize(StringSet.From("b", "a"), Options));

    [TestMethod]
    public void Serialize_Empty_AsEmptyArray()
        => Assert.AreEqual("[]", JsonSerializer.Serialize(StringSet.Empty, Options));

    [TestMethod]
    public void Deserialize_UnsortedWithDuplicates_Normalizes()
    {
        var set = JsonSerializer.Deserialize<Int32Set>("[3,1,3]", Options);

        CollectionAssert.AreEqual(new[] { 1, 3 }, set!.Values.ToArray());
    }

    [TestMethod]
    public void Deserialize_EmptyArray_ReturnsEmptySet()
        => Assert.AreSame(Int32Set.Empty, JsonSerializer.Deserialize<Int32Set>("[]", Options));

    [TestMethod]
    public void Deserialize_NullElement_Throws()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<StringSet>("[\"a\",null]", Options));

    [TestMethod]
    public void Deserialize_NonArray_Throws()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Int32Set>("\"not an array\"", Options));

    [TestMethod]
    public void RoundTrip_GuidSet()
    {
        var original = GuidSet.From(Guid.NewGuid(), Guid.NewGuid());

        var roundTripped = JsonSerializer.Deserialize<GuidSet>(JsonSerializer.Serialize(original, Options), Options);

        Assert.AreEqual(original, roundTripped);
    }

    [TestMethod]
    public void RoundTrip_DateSet()
    {
        var original = DateSet.From(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 24));

        var roundTripped = JsonSerializer.Deserialize<DateSet>(JsonSerializer.Serialize(original, Options), Options);

        Assert.AreEqual(original, roundTripped);
    }

    [TestMethod]
    public void WrapperSet_DelegatesElementSerialization()
    {
        // TestPermission carries its own JsonConverter (string form) — the set serializes as a
        // plain array of those strings, matching a legacy jsonb text-array shape.
        var set = StringSet<TestPermission>.From(
            TestPermission.Parse("users.write", CultureInfo.InvariantCulture),
            TestPermission.Parse("users.read", CultureInfo.InvariantCulture));

        var json = JsonSerializer.Serialize(set, Options);

        Assert.AreEqual("[\"users.read\",\"users.write\"]", json);
        Assert.AreEqual(set, JsonSerializer.Deserialize<StringSet<TestPermission>>(json, Options));
    }

    [TestMethod]
    public void Deserialize_WrapperSet_InvalidElement_Throws()
        => Assert.ThrowsExactly<FormatException>(
            () => JsonSerializer.Deserialize<StringSet<TestPermission>>("[\"no-dot\"]", Options));
}

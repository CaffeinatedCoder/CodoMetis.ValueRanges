using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class SetJsonConverterTests
{
    // -----------------------------------------------------------------------
    // ElementJsonConverter — the fallback for element types System.Text.Json cannot
    // serialize as a scalar. The primitive-backed families are natively serializable and
    // define none; the wrapper arities carry arbitrary element types and always do.
    // -----------------------------------------------------------------------

    private static JsonConverter<T>? HookFor<TSet, T>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
        => TSet.ElementJsonConverter;

    [TestMethod]
    public void ElementJsonConverter_IsNull_ForPrimitiveBackedFamilies()
    {
        Assert.IsNull(HookFor<StringSet, string>());
        Assert.IsNull(HookFor<GuidSet, Guid>());
        Assert.IsNull(HookFor<Int16Set, short>());
        Assert.IsNull(HookFor<Int32Set, int>());
        Assert.IsNull(HookFor<Int64Set, long>());
        Assert.IsNull(HookFor<DecimalSet, decimal>());
        Assert.IsNull(HookFor<DateSet, DateOnly>());
        Assert.IsNull(HookFor<TimeSet, TimeOnly>());
        Assert.IsNull(HookFor<DateTimeSet, DateTime>());
        Assert.IsNull(HookFor<DateTimeOffsetSet, DateTimeOffset>());
    }

    [TestMethod]
    public void ElementJsonConverter_IsDefined_ForEveryWrapperArity()
    {
        Assert.IsNotNull(HookFor<StringSet<TestPermission>, TestPermission>());
        Assert.IsNotNull(HookFor<GuidSet<TestId>, TestId>());
        Assert.IsNotNull(HookFor<Int32Set<TestIntId>, TestIntId>());
        Assert.IsNotNull(HookFor<Int64Set<TestLongId>, TestLongId>());
    }

    // -----------------------------------------------------------------------
    // Wrapper arities whose element carries no converter of its own. TestPermission has
    // one; TestId/TestIntId/TestLongId deliberately do not — they are the Vogen shape,
    // a record struct over a private field, which System.Text.Json renders as {}.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperSet_GuidBacked_UsesTheBackingTextForm()
    {
        var set = GuidSet<TestId>.From(
            TestId.FromGuid(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            TestId.FromGuid(Guid.Parse("11111111-1111-1111-1111-111111111111")));

        var json = JsonSerializer.Serialize(set, Options);

        Assert.AreEqual(
            "[\"11111111-1111-1111-1111-111111111111\",\"22222222-2222-2222-2222-222222222222\"]", json);
        Assert.AreEqual(set, JsonSerializer.Deserialize<GuidSet<TestId>>(json, Options));
    }

    [TestMethod]
    public void WrapperSet_IntegerBacked_SerializesAsNumbers()
    {
        var ints  = Int32Set<TestIntId>.From(new TestIntId(3), new TestIntId(1));
        var longs = Int64Set<TestLongId>.From(new TestLongId(9_000_000_000L));

        Assert.AreEqual("[1,3]", JsonSerializer.Serialize(ints, Options));
        Assert.AreEqual("[9000000000]", JsonSerializer.Serialize(longs, Options));

        Assert.AreEqual(ints,  JsonSerializer.Deserialize<Int32Set<TestIntId>>("[1,3]", Options));
        Assert.AreEqual(longs, JsonSerializer.Deserialize<Int64Set<TestLongId>>("[9000000000]", Options));
    }

    /// <summary>
    /// The wrapper must be indistinguishable from the primitive it wraps on the wire — that is
    /// what makes swapping <c>Int32Set</c> for <c>Int32Set&lt;TElement&gt;</c> a non-event for
    /// API consumers.
    /// </summary>
    [TestMethod]
    public void WrapperSet_ProducesTheSameJsonAsItsPrimitiveSibling()
    {
        Assert.AreEqual(
            JsonSerializer.Serialize(Int32Set.From(1, 3), Options),
            JsonSerializer.Serialize(Int32Set<TestIntId>.From(new TestIntId(1), new TestIntId(3)), Options));

        var guid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.AreEqual(
            JsonSerializer.Serialize(GuidSet.From(guid), Options),
            JsonSerializer.Serialize(GuidSet<TestId>.From(TestId.FromGuid(guid)), Options));
    }

    [TestMethod]
    public void WrapperSet_IntegerBacked_ReadsNumbersWrittenAsStrings()
        => Assert.AreEqual(
            Int32Set<TestIntId>.From(new TestIntId(1), new TestIntId(3)),
            JsonSerializer.Deserialize<Int32Set<TestIntId>>("[\"1\",\"3\"]", Options));

    [TestMethod]
    public void WrapperSet_IntegerBacked_NonNumericToken_Throws()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Int32Set<TestIntId>>("[{\"Value\":1}]", Options));

    [TestMethod]
    public void WrapperSet_ElementValidation_RunsOnTheJsonPath()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<GuidSet<TestId>>("[\"not-a-guid\"]", Options));

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

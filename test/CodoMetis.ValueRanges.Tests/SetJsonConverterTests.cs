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
        Assert.IsNotNull(HookFor<Int16Set<TestSmallId>, TestSmallId>());
        Assert.IsNotNull(HookFor<Int32Set<TestIntId>, TestIntId>());
        Assert.IsNotNull(HookFor<Int64Set<TestLongId>, TestLongId>());
        Assert.IsNotNull(HookFor<DecimalSet<TestMoney>, TestMoney>());
        Assert.IsNotNull(HookFor<DateSet<TestDay>, TestDay>());
        Assert.IsNotNull(HookFor<TimeSet<TestSlot>, TestSlot>());
        Assert.IsNotNull(HookFor<DateTimeSet<TestStamp>, TestStamp>());
        Assert.IsNotNull(HookFor<DateTimeOffsetSet<TestOffsetStamp>, TestOffsetStamp>());
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

        Assert.AreEqual(
            JsonSerializer.Serialize(Int16Set.From((short)3, (short)1), Options),
            JsonSerializer.Serialize(Int16Set<TestSmallId>.From(new TestSmallId(3), new TestSmallId(1)), Options));

        var day = new DateOnly(2024, 6, 15);
        Assert.AreEqual(
            JsonSerializer.Serialize(DateSet.From(day), Options),
            JsonSerializer.Serialize(DateSet<TestDay>.From(new TestDay(day)), Options));
    }

    /// <summary>
    /// A JSON number with a fractional part, which the integer converter cannot express: routed
    /// through <see cref="long"/> on either leg, <c>12.50</c> becomes <c>12</c> or throws. The
    /// scale is preserved, so the payload matches what System.Text.Json writes for the
    /// <see cref="decimal"/> the element wraps.
    /// </summary>
    [TestMethod]
    public void WrapperSet_DecimalBacked_SerializesAsNumbersKeepingScale()
    {
        var money = DecimalSet<TestMoney>.From(new TestMoney(12.50m), new TestMoney(1.5m));

        Assert.AreEqual("[1.5,12.50]", JsonSerializer.Serialize(money, Options));
        Assert.AreEqual(money, JsonSerializer.Deserialize<DecimalSet<TestMoney>>("[1.5,12.50]", Options));
    }

    /// <summary>
    /// The temporal arities write the family's round-trip text form as a JSON string — the same
    /// token type System.Text.Json uses for the primitives they wrap, and the same text the
    /// array literal carries. The sub-second digits are the assertion: the element's default
    /// form would have dropped them.
    /// </summary>
    [TestMethod]
    public void WrapperSet_TemporalBacked_SerializesAsRoundTripStrings()
    {
        var precise = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified).AddTicks(1_234_567);
        var stamps  = DateTimeSet<TestStamp>.From(new TestStamp(precise));

        Assert.AreEqual("[\"2024-06-15T10:30:00.1234567\"]", JsonSerializer.Serialize(stamps, Options));
        Assert.AreEqual(stamps, JsonSerializer.Deserialize<DateTimeSet<TestStamp>>(
            "[\"2024-06-15T10:30:00.1234567\"]", Options));

        var slots = TimeSet<TestSlot>.From(new TestSlot(new TimeOnly(9, 30, 15, 250)));

        Assert.AreEqual("[\"09:30:15.2500000\"]", JsonSerializer.Serialize(slots, Options));
        Assert.AreEqual(slots, JsonSerializer.Deserialize<TimeSet<TestSlot>>("[\"09:30:15.2500000\"]", Options));

        var days = DateSet<TestDay>.From(new TestDay(new DateOnly(2024, 6, 15)));

        Assert.AreEqual("[\"2024-06-15\"]", JsonSerializer.Serialize(days, Options));
    }

    /// <summary>
    /// Where the wrapper's payload is <em>not</em> byte-identical to its primitive sibling's.
    /// The token type and the value agree, and each parses to the other's value, but the text
    /// differs in two ways: the round-trip format always writes seven fraction digits where
    /// System.Text.Json trims them, and the default encoder escapes <c>+</c> in a string it
    /// writes itself, which the native <see cref="DateTimeOffset"/> writer does not.
    /// </summary>
    /// <remarks>
    /// Asserted rather than fixed. Matching the native writer byte for byte would mean the
    /// element converter reproducing System.Text.Json's temporal formatting for an element type
    /// it only knows through <see cref="IFormattable"/>, and the round-trip form is the one the
    /// array literal and the EF bridge already share. Swapping a closed temporal set for its
    /// arity therefore changes the bytes of an API response, though not its meaning — the
    /// non-temporal arities are byte-identical, which
    /// <see cref="WrapperSet_ProducesTheSameJsonAsItsPrimitiveSibling"/> pins.
    /// </remarks>
    [TestMethod]
    public void WrapperSet_TemporalBacked_DiffersFromItsPrimitiveSiblingInTextOnly()
    {
        var stamp = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(2));

        var primitive = JsonSerializer.Serialize(DateTimeOffsetSet.From(stamp), Options);
        var wrapper   = JsonSerializer.Serialize(
            DateTimeOffsetSet<TestOffsetStamp>.From(new TestOffsetStamp(stamp)), Options);

        Assert.AreEqual("[\"2024-06-15T10:30:00+02:00\"]", primitive);
        Assert.AreEqual("[\"2024-06-15T10:30:00.0000000\\u002B02:00\"]", wrapper);

        // Different bytes, same value: each payload deserializes into the other's type.
        Assert.AreEqual(
            DateTimeOffsetSet.From(stamp),
            JsonSerializer.Deserialize<DateTimeOffsetSet>(wrapper, Options));
        Assert.AreEqual(
            DateTimeOffsetSet<TestOffsetStamp>.From(new TestOffsetStamp(stamp)),
            JsonSerializer.Deserialize<DateTimeOffsetSet<TestOffsetStamp>>(primitive, Options));
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

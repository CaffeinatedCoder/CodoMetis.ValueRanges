using System.Text.Json;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions().AddRangeConverters();

    // -----------------------------------------------------------------------
    // Serialize
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Serialize_Empty_ProducesEmptyLiteral()
    {
        var json = JsonSerializer.Serialize(Int32Range.Empty, Options);
        Assert.AreEqual("\"empty\"", json);
    }

    [TestMethod]
    public void Serialize_Infinity_ProducesInfinityLiteral()
    {
        var json = JsonSerializer.Serialize(Int32Range.Infinite, Options);
        Assert.AreEqual("\"(,)\"", json);
    }

    [TestMethod]
    public void Serialize_FiniteInt32Range()
    {
        var range = Int32Range.CreateFinite(1, 10);
        var json  = JsonSerializer.Serialize(range, Options);
        Assert.AreEqual("\"[1,10]\"", json);
    }

    [TestMethod]
    public void Serialize_FiniteDecimalRange_HalfOpen()
    {
        var range = DecimalRange.CreateFinite(1.5m, 9.9m);
        var json  = JsonSerializer.Serialize(range, Options);
        Assert.AreEqual("\"[1.5,9.9)\"", json);
    }

    [TestMethod]
    public void Serialize_DateRange_UsesIso8601()
    {
        var range = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var json  = JsonSerializer.Serialize(range, Options);
        Assert.AreEqual("\"[2024-01-01,2024-12-31]\"", json);
    }

    [TestMethod]
    public void Serialize_AsPropertyInAnonymousObject()
    {
        var obj  = new { Period = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)) };
        var json = JsonSerializer.Serialize(obj, Options);
        Assert.IsTrue(json.Contains("\"Period\":\"[2024-01-01,2024-12-31]\""), json);
    }

    // -----------------------------------------------------------------------
    // Deserialize
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Deserialize_Empty_ReturnsEmptyRange()
    {
        var result = JsonSerializer.Deserialize<Int32Range>("\"empty\"", Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<Int32Range.EmptyRange>(result);
    }

    [TestMethod]
    public void Deserialize_Infinity_ReturnsInfinityRange()
    {
        var result = JsonSerializer.Deserialize<Int32Range>("\"(,)\"", Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<Int32Range.Infinity>(result);
    }

    [TestMethod]
    public void Deserialize_FiniteInt32Range()
    {
        var result = JsonSerializer.Deserialize<Int32Range>("\"[1,10]\"", Options);
        var finite = Assert.IsInstanceOfType<Int32Range.Finite>(result!);
        Assert.AreEqual(1,  finite.Start);
        Assert.AreEqual(10, finite.End);
    }

    [TestMethod]
    public void Deserialize_FiniteDecimalRange_HalfOpen()
    {
        var result = JsonSerializer.Deserialize<DecimalRange>("\"[1.5,9.9)\"", Options);
        var finite = Assert.IsInstanceOfType<DecimalRange.Finite>(result!);
        Assert.AreEqual(1.5m, finite.Start);
        Assert.AreEqual(9.9m, finite.End);
        Assert.IsTrue(finite.StartInclusive);
        Assert.IsFalse(finite.EndInclusive);
    }

    [TestMethod]
    public void Deserialize_DateRange()
    {
        var result = JsonSerializer.Deserialize<DateRange>("\"[2024-01-01,2024-12-31]\"", Options);
        var finite = Assert.IsInstanceOfType<DateRange.Finite>(result!);
        Assert.AreEqual(new DateOnly(2024, 1,  1),  finite.Start);
        Assert.AreEqual(new DateOnly(2024, 12, 31), finite.End);
    }

    // -----------------------------------------------------------------------
    // null round-trips as null, and stays distinct from the empty range
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Deserialize_NullJson_ReturnsNull()
        => Assert.IsNull(JsonSerializer.Deserialize<Int32Range>("null", Options));

    [TestMethod]
    public void NullRangeProperty_RoundTrips()
    {
        // The defect this replaces: the property wrote as null and threw on the way back in,
        // so an API could return a payload it was unable to accept.
        var json = JsonSerializer.Serialize(new NullableHolder(null), Options);

        Assert.AreEqual("{\"Range\":null}", json);
        Assert.IsNull(JsonSerializer.Deserialize<NullableHolder>(json, Options)!.Range);
    }

    [TestMethod]
    public void NullAndEmpty_StayDistinctAcrossARoundTrip()
    {
        // Absent and empty are different facts, and reading null as null does not blur them —
        // which was the stated reason for rejecting the null token.
        var withNull  = JsonSerializer.Deserialize<NullableHolder>("{\"Range\":null}", Options)!;
        var withEmpty = JsonSerializer.Deserialize<NullableHolder>("{\"Range\":\"empty\"}", Options)!;

        Assert.IsNull(withNull.Range);
        Assert.IsInstanceOfType<Int32Range.EmptyRange>(withEmpty.Range!);
    }

    private sealed record NullableHolder(Int32Range? Range);

    [TestMethod]
    public void Serialize_NullRangeProperty_WritesNull()
        => Assert.AreEqual("{\"Range\":null}", JsonSerializer.Serialize(new NullableHolder(null), Options));

    [TestMethod]
    public void Serialize_NullRange_TopLevel_WritesNull()
        => Assert.AreEqual("null", JsonSerializer.Serialize<Int32Range>(null!, Options));

    [TestMethod]
    public void Serialize_NullRangeInCollection_WritesNull()
        => Assert.AreEqual("[\"[1,10]\",null]",
            JsonSerializer.Serialize(new[] { Int32Range.CreateFinite(1, 10), null }, Options));

    [TestMethod]
    public void Serialize_NullRangeSetProperty_WritesNull()
        => Assert.AreEqual("{\"Set\":null}",
            JsonSerializer.Serialize(new NullableSetHolder(null), Options));

    private sealed record NullableSetHolder(RangeSet<Int32Range, int>? Set);

    [TestMethod]
    public void Deserialize_InvalidLiteral_ThrowsJsonException()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Int32Range>("\"not-a-range\"", Options));

    // -----------------------------------------------------------------------
    // RangeSet serialization
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Serialize_EmptySet()
    {
        var json = JsonSerializer.Serialize(RangeSet<Int32Range, int>.Empty, Options);
        Assert.AreEqual("\"{}\"", json);
    }

    [TestMethod]
    public void Serialize_TwoElementSet()
    {
        var set  = RangeSet<Int32Range, int>.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(7, 10)
        ]);
        var json = JsonSerializer.Serialize(set, Options);
        Assert.AreEqual("\"{[1,5],[7,10]}\"", json);
    }

    [TestMethod]
    public void Deserialize_EmptySet()
    {
        var result = JsonSerializer.Deserialize<RangeSet<Int32Range, int>>("\"{}\"", Options);
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Deserialize_TwoElementSet()
    {
        var result = JsonSerializer.Deserialize<RangeSet<Int32Range, int>>("\"{[1,5],[7,10]}\"", Options);
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
    }

    // -----------------------------------------------------------------------
    // Roundtrip via JsonSerializer
    // -----------------------------------------------------------------------

    [TestMethod]
    public void JsonRoundtrip_AllInt32RangeVariants()
    {
        Int32Range[] cases =
        [
            Int32Range.Empty,
            Int32Range.Infinite,
            Int32Range.CreateFinite(1, 100),
            Int32Range.CreateUnboundedStart(end: 50, endInclusive: true),
            Int32Range.CreateUnboundedEnd(start: 10, startInclusive: true)
        ];

        foreach (var original in cases)
        {
            var json   = JsonSerializer.Serialize(original, Options);
            var parsed = JsonSerializer.Deserialize<Int32Range>(json, Options);
            Assert.AreEqual(original, parsed, $"Roundtrip failed for: {json}");
        }
    }

    [TestMethod]
    public void JsonRoundtrip_DecimalRange()
    {
        var original = DecimalRange.CreateFinite(0.001m, 999.999m, startInclusive: true, endInclusive: false);
        var json     = JsonSerializer.Serialize(original, Options);
        var parsed   = JsonSerializer.Deserialize<DecimalRange>(json, Options);
        Assert.AreEqual(original, parsed);
    }

    [TestMethod]
    public void JsonRoundtrip_RangeSet()
    {
        var original = RangeSet<DecimalRange, decimal>.From([
            DecimalRange.CreateFinite(0m, 10m),
            DecimalRange.CreateFinite(20m, 30m)
        ]);
        var json   = JsonSerializer.Serialize(original, Options);
        var parsed = JsonSerializer.Deserialize<RangeSet<DecimalRange, decimal>>(json, Options);
        Assert.AreEqual(original, parsed);
    }

    // -----------------------------------------------------------------------
    // Union variants — System.Text.Json resolves converters by the type it is handed,
    // which for object-typed and variant-typed declarations is the variant, not the union
    // -----------------------------------------------------------------------

    private sealed record ObjectHolder(object Range);

    private sealed record FiniteHolder(Int32Range.Finite Range);

    private sealed record NullableFiniteHolder(Int32Range.Finite? Range);

    [TestMethod]
    public void Serialize_ObjectTyped_UsesRangeLiteral()
    {
        Assert.AreEqual("\"[1,5]\"",   JsonSerializer.Serialize<object>(Int32Range.CreateFinite(1, 5), Options));
        Assert.AreEqual("\"empty\"",   JsonSerializer.Serialize<object>(Int32Range.Empty, Options));
        Assert.AreEqual("\"(,)\"",     JsonSerializer.Serialize<object>(Int32Range.Infinite, Options));
        Assert.AreEqual("\"(,50]\"",   JsonSerializer.Serialize<object>(Int32Range.CreateUnboundedStart(50, true), Options));
        Assert.AreEqual("\"[10,)\"",   JsonSerializer.Serialize<object>(Int32Range.CreateUnboundedEnd(10, true), Options));
    }

    [TestMethod]
    public void Serialize_ObjectTypedProperty_UsesRangeLiteral()
        => Assert.AreEqual("{\"Range\":\"[1,5]\"}",
            JsonSerializer.Serialize(new ObjectHolder(Int32Range.CreateFinite(1, 5)), Options));

    [TestMethod]
    public void Serialize_ObjectTypedCollection_UsesRangeLiterals()
        => Assert.AreEqual("[\"[1,5]\",\"[2024-01-01,2024-03-01]\"]",
            JsonSerializer.Serialize(new List<object>
            {
                Int32Range.CreateFinite(1, 5),
                DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 1))
            }, Options));

    [TestMethod]
    public void Serialize_VariantTypedProperty_UsesRangeLiteral()
        => Assert.AreEqual("{\"Range\":\"[1,5]\"}",
            JsonSerializer.Serialize(new FiniteHolder((Int32Range.Finite)Int32Range.CreateFinite(1, 5)), Options));

    [TestMethod]
    public void Serialize_NullVariantProperty_WritesNull()
        => Assert.AreEqual("{\"Range\":null}",
            JsonSerializer.Serialize(new NullableFiniteHolder(null), Options));

    [TestMethod]
    public void JsonRoundtrip_VariantTyped()
    {
        var original = (Int32Range.Finite)Int32Range.CreateFinite(1, 5);

        var json = JsonSerializer.Serialize(original, Options);

        Assert.AreEqual("\"[1,5]\"", json);
        Assert.AreEqual(original, JsonSerializer.Deserialize<Int32Range.Finite>(json, Options));
    }

    [TestMethod]
    public void Deserialize_VariantTyped_EachShape()
    {
        Assert.IsNotNull(JsonSerializer.Deserialize<Int32Range.EmptyRange>("\"empty\"", Options));
        Assert.IsNotNull(JsonSerializer.Deserialize<Int32Range.Infinity>("\"(,)\"", Options));
        Assert.IsNotNull(JsonSerializer.Deserialize<Int32Range.UnboundedStart>("\"(,50]\"", Options));
        Assert.IsNotNull(JsonSerializer.Deserialize<Int32Range.UnboundedEnd>("\"[10,)\"", Options));
    }

    [TestMethod]
    public void Deserialize_VariantTyped_WrongShape_ThrowsJsonException()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Int32Range.Finite>("\"empty\"", Options));

    [TestMethod]
    public void Deserialize_VariantTyped_InvalidLiteral_ThrowsJsonException()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Int32Range.Finite>("\"not-a-range\"", Options));

    [TestMethod]
    public void Deserialize_VariantTyped_Null_ReturnsNull()
        => Assert.IsNull(JsonSerializer.Deserialize<Int32Range.Finite>("null", Options));

    [TestMethod]
    public void NullVariantProperty_RoundTrips()
    {
        var json = JsonSerializer.Serialize(new NullableVariantHolder(null), Options);

        Assert.AreEqual("{\"Range\":null}", json);
        Assert.IsNull(JsonSerializer.Deserialize<NullableVariantHolder>(json, Options)!.Range);
    }

    private sealed record NullableVariantHolder(Int32Range.Finite? Range);

    // -----------------------------------------------------------------------
    // Per-type converters (explicit registration)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ExplicitConverter_Int32Range_Roundtrip()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new Int32RangeJsonConverter());

        var original = Int32Range.CreateFinite(3, 7);
        var json     = JsonSerializer.Serialize(original, opts);
        var parsed   = JsonSerializer.Deserialize<Int32Range>(json, opts);
        Assert.AreEqual(original, parsed);
    }

    [TestMethod]
    public void ExplicitConverter_DateRangeSet_Roundtrip()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new DateRangeSetJsonConverter());

        var original = RangeSet<DateRange, DateOnly>.From([
            DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31)),
            DateRange.CreateFinite(new DateOnly(2024, 7, 1), new DateOnly(2024, 9, 30))
        ]);
        var json   = JsonSerializer.Serialize(original, opts);
        var parsed = JsonSerializer.Deserialize<RangeSet<DateRange, DateOnly>>(json, opts);
        Assert.AreEqual(original, parsed);
    }
}

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

    [TestMethod]
    public void Deserialize_NullJson_ThrowsJsonException()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Int32Range>("null", Options));

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

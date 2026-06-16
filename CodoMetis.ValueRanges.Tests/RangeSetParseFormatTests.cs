using System.Globalization;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeSetParseFormatTests
{
    // -----------------------------------------------------------------------
    // IFormattable
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToString_EmptySet_ReturnsBraces()
    {
        Assert.AreEqual("{}", RangeSet<Int32Range, int>.Empty.ToString());
    }

    [TestMethod]
    public void ToString_InfiniteSet_ReturnsInfinityElement()
    {
        Assert.AreEqual("{(,)}", RangeSet<Int32Range, int>.Infinite.ToString());
    }

    [TestMethod]
    public void ToString_SingleRange_WrapsInBraces()
    {
        var set = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 5)]);
        Assert.AreEqual("{[1,5]}", set.ToString());
    }

    [TestMethod]
    public void ToString_TwoRanges_CommaSeparated()
    {
        var set = RangeSet<Int32Range, int>.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(7, 10)
        ]);
        Assert.AreEqual("{[1,5],[7,10]}", set.ToString());
    }

    [TestMethod]
    public void ToString_DateSet_UsesIso8601()
    {
        var set = RangeSet<DateRange, DateOnly>.From([
            DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30)),
            DateRange.CreateFinite(new DateOnly(2024, 9, 1), new DateOnly(2024, 12, 31))
        ]);
        Assert.AreEqual("{[2024-01-01,2024-06-30],[2024-09-01,2024-12-31]}", set.ToString());
    }

    // -----------------------------------------------------------------------
    // IParsable
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Parse_EmptyBraces_ReturnsEmptySet()
    {
        var result = RangeSet<Int32Range, int>.Parse("{}", CultureInfo.InvariantCulture);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Parse_InfinityElement_ReturnsInfiniteSet()
    {
        var result = RangeSet<Int32Range, int>.Parse("{(,)}", CultureInfo.InvariantCulture);
        Assert.AreEqual(1, result.Count);
        Assert.IsInstanceOfType<Int32Range.Infinity>(result[0]);
    }

    [TestMethod]
    public void Parse_SingleRange()
    {
        var result = RangeSet<Int32Range, int>.Parse("{[1,5]}", CultureInfo.InvariantCulture);
        Assert.AreEqual(1, result.Count);
        var f = Assert.IsInstanceOfType<Int32Range.Finite>(result[0]);
        Assert.AreEqual(1, f.Start);
        Assert.AreEqual(5, f.End);
    }

    [TestMethod]
    public void Parse_TwoRanges()
    {
        var result = RangeSet<Int32Range, int>.Parse("{[1,5],[7,10]}", CultureInfo.InvariantCulture);
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Parse_WithWhitespace_IsHandled()
    {
        var result = RangeSet<Int32Range, int>.Parse("{ [1,5] , [7,10] }", CultureInfo.InvariantCulture);
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Parse_MissingBraces_ThrowsFormatException()
        => Assert.ThrowsExactly<FormatException>(
            () => RangeSet<Int32Range, int>.Parse("[1,5],[7,10]", CultureInfo.InvariantCulture));

    [TestMethod]
    public void TryParse_Valid_ReturnsTrue()
    {
        var ok = RangeSet<Int32Range, int>.TryParse(
            "{[1,5],[7,10]}", CultureInfo.InvariantCulture, out var result);
        Assert.IsTrue(ok);
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void TryParse_Invalid_ReturnsFalseWithEmpty()
    {
        var ok = RangeSet<Int32Range, int>.TryParse(
            "not-valid", CultureInfo.InvariantCulture, out var result);
        Assert.IsFalse(ok);
        Assert.AreEqual(0, result.Count);
    }

    // -----------------------------------------------------------------------
    // Roundtrip
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Roundtrip_EmptySet()
    {
        var original = RangeSet<Int32Range, int>.Empty;
        var parsed   = RangeSet<Int32Range, int>.Parse(original.ToString()!, CultureInfo.InvariantCulture);
        Assert.AreEqual(original, parsed);
    }

    [TestMethod]
    public void Roundtrip_MultipleRanges()
    {
        var original = RangeSet<DecimalRange, decimal>.From([
            DecimalRange.CreateFinite(0m, 1m),
            DecimalRange.CreateFinite(5m, 10m),
            DecimalRange.CreateUnboundedEnd(start: 100m)
        ]);
        var s      = original.ToString()!;
        var parsed = RangeSet<DecimalRange, decimal>.Parse(s, CultureInfo.InvariantCulture);
        Assert.AreEqual(original, parsed);
    }

    [TestMethod]
    public void Roundtrip_OverlappingInputNormalized_ParsedBack()
    {
        // Overlapping inputs are merged on From; result roundtrips
        var set    = RangeSet<Int32Range, int>.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(4, 8)  // overlaps, merges to [1,8]
        ]);
        var parsed = RangeSet<Int32Range, int>.Parse(set.ToString()!, CultureInfo.InvariantCulture);
        Assert.AreEqual(set, parsed);
    }
}

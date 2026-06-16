using System.Globalization;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeParseFormatTests
{
    // -----------------------------------------------------------------------
    // IFormattable — all five variants, discrete and continuous
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToString_Empty_ReturnsEmptyLiteral()
    {
        Assert.AreEqual("empty", Int32Range.Empty.ToString());
        Assert.AreEqual("empty", DecimalRange.Empty.ToString());
    }

    [TestMethod]
    public void ToString_Infinity_ReturnsUnboundedLiteral()
    {
        Assert.AreEqual("(,)", Int32Range.Infinite.ToString());
        Assert.AreEqual("(,)", DateRange.Infinite.ToString());
    }

    [TestMethod]
    public void ToString_FiniteDiscrete_ClosedBounds()
    {
        var range = Int32Range.CreateFinite(1, 10);
        Assert.AreEqual("[1,10]", range.ToString());
    }

    [TestMethod]
    public void ToString_FiniteContinuous_HalfOpen()
    {
        var range = DecimalRange.CreateFinite(1.5m, 9.9m); // default [start, end)
        Assert.AreEqual("[1.5,9.9)", range.ToString());
    }

    [TestMethod]
    public void ToString_FiniteContinuous_ClosedBounds()
    {
        var range = DecimalRange.CreateFinite(1m, 5m, startInclusive: true, endInclusive: true);
        Assert.AreEqual("[1,5]", range.ToString());
    }

    [TestMethod]
    public void ToString_FiniteContinuous_OpenBounds()
    {
        var range = DecimalRange.CreateFinite(1m, 5m, startInclusive: false, endInclusive: false);
        Assert.AreEqual("(1,5)", range.ToString());
    }

    [TestMethod]
    public void ToString_UnboundedStart_Discrete()
    {
        var range = Int32Range.CreateUnboundedStart(end: 10, endInclusive: true);
        Assert.AreEqual("(,10]", range.ToString());
    }

    [TestMethod]
    public void ToString_UnboundedEnd_Discrete()
    {
        var range = Int32Range.CreateUnboundedEnd(start: 5, startInclusive: true);
        Assert.AreEqual("[5,)", range.ToString());
    }

    [TestMethod]
    public void ToString_UnboundedStart_Continuous_Exclusive()
    {
        var range = DecimalRange.CreateUnboundedStart(end: 10m, endInclusive: false);
        Assert.AreEqual("(,10)", range.ToString());
    }

    [TestMethod]
    public void ToString_DateRange_UsesIso8601()
    {
        var range = DateRange.CreateFinite(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31));
        Assert.AreEqual("[2024-01-01,2024-12-31]", range.ToString());
    }

    [TestMethod]
    public void ToString_DateTimeRange_UsesRoundTripFormat()
    {
        var dt    = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var range = DateTimeRange.CreateFinite(dt, dt.AddHours(8));
        var s     = range.ToString();
        Assert.IsTrue(s.StartsWith("[2024-06-15T10:30:00.0000000Z,"));
    }

    [TestMethod]
    public void ToString_DateTimeOffsetRange_UsesRoundTripFormat()
    {
        var dto   = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(2));
        var range = DateTimeOffsetRange.CreateFinite(dto, dto.AddHours(8));
        var s     = range.ToString();
        Assert.IsTrue(s.StartsWith("[2024-06-15T10:30:00.0000000+02:00,"));
    }

    // -----------------------------------------------------------------------
    // IParsable — roundtrips for all five variants
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Parse_Empty_ReturnsEmptyRange()
    {
        var result = Int32Range.Parse("empty", CultureInfo.InvariantCulture);
        Assert.IsInstanceOfType<Int32Range.EmptyRange>(result);
    }

    [TestMethod]
    public void Parse_Empty_CaseInsensitive()
    {
        var result = Int32Range.Parse("EMPTY", CultureInfo.InvariantCulture);
        Assert.IsInstanceOfType<Int32Range.EmptyRange>(result);
    }

    [TestMethod]
    public void Parse_Infinity_ReturnsInfinityRange()
    {
        var result = Int32Range.Parse("(,)", CultureInfo.InvariantCulture);
        Assert.IsInstanceOfType<Int32Range.Infinity>(result);
    }

    [TestMethod]
    public void Parse_FiniteDiscrete_ClosedBounds()
    {
        var result = Int32Range.Parse("[1,10]", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<Int32Range.Finite>(result);
        Assert.AreEqual(1, finite.Start);
        Assert.AreEqual(10, finite.End);
    }

    [TestMethod]
    public void Parse_FiniteContinuous_HalfOpen()
    {
        var result = DecimalRange.Parse("[1.5,9.9)", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<DecimalRange.Finite>(result);
        Assert.AreEqual(1.5m, finite.Start);
        Assert.AreEqual(9.9m, finite.End);
        Assert.IsTrue(finite.StartInclusive);
        Assert.IsFalse(finite.EndInclusive);
    }

    [TestMethod]
    public void Parse_FiniteContinuous_Open()
    {
        var result = DecimalRange.Parse("(0,100)", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<DecimalRange.Finite>(result);
        Assert.IsFalse(finite.StartInclusive);
        Assert.IsFalse(finite.EndInclusive);
    }

    [TestMethod]
    public void Parse_UnboundedStart()
    {
        var result = Int32Range.Parse("(,10]", CultureInfo.InvariantCulture);
        var us     = Assert.IsInstanceOfType<Int32Range.UnboundedStart>(result);
        Assert.AreEqual(10, us.End);
    }

    [TestMethod]
    public void Parse_UnboundedEnd()
    {
        var result = Int32Range.Parse("[5,)", CultureInfo.InvariantCulture);
        var ue     = Assert.IsInstanceOfType<Int32Range.UnboundedEnd>(result);
        Assert.AreEqual(5, ue.Start);
    }

    [TestMethod]
    public void Parse_DateRange_Iso8601()
    {
        var result = DateRange.Parse("[2024-01-01,2024-12-31]", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<DateRange.Finite>(result);
        Assert.AreEqual(new DateOnly(2024, 1, 1),  finite.Start);
        Assert.AreEqual(new DateOnly(2024, 12, 31), finite.End);
    }

    [TestMethod]
    public void Parse_Int64Range_LargeValues()
    {
        var result = Int64Range.Parse("[9000000000,9999999999]", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<Int64Range.Finite>(result);
        Assert.AreEqual(9_000_000_000L, finite.Start);
        Assert.AreEqual(9_999_999_999L, finite.End);
    }

    [TestMethod]
    public void Parse_InvalidFormat_ThrowsFormatException()
        => Assert.ThrowsExactly<FormatException>(
            () => Int32Range.Parse("not-a-range", CultureInfo.InvariantCulture));

    [TestMethod]
    public void Parse_MissingComma_ThrowsFormatException()
        => Assert.ThrowsExactly<FormatException>(
            () => Int32Range.Parse("[15]", CultureInfo.InvariantCulture));

    // -----------------------------------------------------------------------
    // TryParse
    // -----------------------------------------------------------------------

    [TestMethod]
    public void TryParse_ValidInput_ReturnsTrueWithResult()
    {
        var success = Int32Range.TryParse("[1,5]", CultureInfo.InvariantCulture, out var result);
        Assert.IsTrue(success);
        Assert.IsInstanceOfType<Int32Range.Finite>(result);
    }

    [TestMethod]
    public void TryParse_InvalidInput_ReturnsFalseWithEmpty()
    {
        var success = Int32Range.TryParse("garbage", CultureInfo.InvariantCulture, out var result);
        Assert.IsFalse(success);
        Assert.IsInstanceOfType<Int32Range.EmptyRange>(result);
    }

    [TestMethod]
    public void TryParse_NullInput_ReturnsFalse()
    {
        var success = Int32Range.TryParse(null, CultureInfo.InvariantCulture, out var result);
        Assert.IsFalse(success);
        Assert.IsInstanceOfType<Int32Range.EmptyRange>(result);
    }

    // -----------------------------------------------------------------------
    // Roundtrip: ToString → Parse → ToString
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Roundtrip_AllVariants_Int32Range()
    {
        Int32Range[] cases =
        [
            Int32Range.Empty,
            Int32Range.Infinite,
            Int32Range.CreateFinite(1, 10),
            Int32Range.CreateUnboundedStart(end: 20, endInclusive: true),
            Int32Range.CreateUnboundedEnd(start: 5, startInclusive: true)
        ];

        foreach (var original in cases)
        {
            var s      = original.ToString();
            var parsed = Int32Range.Parse(s, CultureInfo.InvariantCulture);
            Assert.AreEqual(original, parsed, $"Roundtrip failed for: {s}");
        }
    }

    [TestMethod]
    public void Roundtrip_AllVariants_DecimalRange()
    {
        DecimalRange[] cases =
        [
            DecimalRange.Empty,
            DecimalRange.Infinite,
            DecimalRange.CreateFinite(1.5m, 9.9m),
            DecimalRange.CreateFinite(0m, 100m, startInclusive: false, endInclusive: true),
            DecimalRange.CreateUnboundedStart(end: 50.5m, endInclusive: false),
            DecimalRange.CreateUnboundedEnd(start: 0.01m, startInclusive: true)
        ];

        foreach (var original in cases)
        {
            var s      = original.ToString();
            var parsed = DecimalRange.Parse(s, CultureInfo.InvariantCulture);
            Assert.AreEqual(original, parsed, $"Roundtrip failed for: {s}");
        }
    }

    [TestMethod]
    public void Roundtrip_DateRange()
    {
        var original = DateRange.CreateFinite(new DateOnly(2020, 3, 1), new DateOnly(2025, 12, 31));
        var s        = original.ToString();
        var parsed   = DateRange.Parse(s, CultureInfo.InvariantCulture);
        Assert.AreEqual(original, parsed);
    }

    [TestMethod]
    public void Roundtrip_DateTimeRange()
    {
        var start    = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var original = DateTimeRange.CreateFinite(start, start.AddDays(30));
        var s        = original.ToString();
        var parsed   = DateTimeRange.Parse(s, CultureInfo.InvariantCulture);
        Assert.AreEqual(original, parsed);
    }

    [TestMethod]
    public void Roundtrip_DateTimeOffsetRange()
    {
        var start    = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.FromHours(1));
        var original = DateTimeOffsetRange.CreateFinite(start, start.AddDays(30));
        var s        = original.ToString();
        var parsed   = DateTimeOffsetRange.Parse(s, CultureInfo.InvariantCulture);
        Assert.AreEqual(original, parsed);
    }

    // -----------------------------------------------------------------------
    // Discrete canonicalization is preserved through parse
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Parse_DiscreteRange_HalfOpenInput_CanonicalizesToClosed()
    {
        // [1,10) should canonicalize to [1,9] for Int32Range
        var result = Int32Range.Parse("[1,10)", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<Int32Range.Finite>(result);
        Assert.AreEqual(1, finite.Start);
        Assert.AreEqual(9, finite.End);
        Assert.IsTrue(finite.StartInclusive);
        Assert.IsTrue(finite.EndInclusive);
    }

    [TestMethod]
    public void Parse_DiscreteRange_OpenStart_CanonicalizesToClosed()
    {
        // (0,10] should canonicalize to [1,10] for Int32Range
        var result = Int32Range.Parse("(0,10]", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<Int32Range.Finite>(result);
        Assert.AreEqual(1, finite.Start);
        Assert.AreEqual(10, finite.End);
    }
}

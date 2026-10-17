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

    /// <summary>
    /// Each structural check of the literal on its own: the length, both brackets, and nothing after
    /// the upper bound. A literal that fails only one of them must still be refused, by
    /// <c>FormatException</c> and by <c>TryParse</c> answering false.
    /// </summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("[")]
    [DataRow("{1,2)")]
    [DataRow("[1,2}")]
    [DataRow("[1,2,3)")]
    [DataRow("[1,2)3)")]
    public void Parse_MalformedLiteral_IsRefused(string literal)
    {
        Assert.ThrowsExactly<FormatException>(() => Int32Range.Parse(literal, CultureInfo.InvariantCulture));
        Assert.IsFalse(Int32Range.TryParse(literal, CultureInfo.InvariantCulture, out _));
    }

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

    /// <summary>
    /// Interpolation passes a null provider. Under de-DE that used to format the decimal bounds with
    /// a comma, and <c>[1,5,2)</c> parses back as <c>[1,52)</c> — a different range, silently.
    /// </summary>
    [TestMethod]
    public void Interpolation_IsTheInvariantLiteral_UnderACommaDecimalCulture()
    {
        var range    = DecimalRange.CreateFinite(1.5m, 2m, endInclusive: false);
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var text = $"{range}";

            Assert.AreEqual("[1.5,2)", text);
            Assert.AreEqual(range, DecimalRange.Parse(text, null));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// Decimal bounds parse with PostgreSQL's numeric grammar — sign, digits, point, exponent. They
    /// used <c>NumberStyles.Any</c>, which also accepted group separators, currency symbols and
    /// parenthesised negatives that PostgreSQL rejects, and that is what let <c>"5,2"</c> read as 52.
    /// </summary>
    [TestMethod]
    [DataRow("[\"1,000\",2000]")]
    [DataRow("[\"(5)\",10]")]
    [DataRow("[\"¤5\",10]")]
    public void DecimalBound_OutsidePostgresNumericGrammar_IsRejected(string literal)
        => Assert.ThrowsExactly<FormatException>(() => DecimalRange.Parse(literal, null));

    [TestMethod]
    public void DecimalBound_ExponentStillParses()
        => Assert.AreEqual(DecimalRange.CreateFinite(100m, 2000m, endInclusive: true), DecimalRange.Parse("[1e2,2E3]", null));

    /// <summary>
    /// An explicit comma-decimal provider used to read <c>1.5</c> as fifteen, silently: <c>.</c> is
    /// de-DE's group separator. The literal is invariant, so the provider no longer reaches it.
    /// </summary>
    [TestMethod]
    public void DecimalBound_UnderACommaDecimalProvider_ReadsTheInvariantLiteral()
        => Assert.AreEqual(DecimalRange.CreateFinite(1.5m, 2.5m, endInclusive: false),
                           DecimalRange.Parse("[1.5,2.5)", CultureInfo.GetCultureInfo("de-DE")));

    /// <summary>
    /// A provider still applies alongside an explicit format, which asks for display text, not a
    /// literal — and only there.
    /// </summary>
    [TestMethod]
    public void Provider_AppliesOnlyWithAnExplicitFormat()
    {
        var range = DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var thai  = CultureInfo.GetCultureInfo("th-TH");

        Assert.AreEqual("[2026-01-01,2026-06-30]", ((IFormattable)range).ToString(null, thai));
        Assert.AreEqual("[2569-01-01,2569-06-30]", ((IFormattable)range).ToString("yyyy-MM-dd", thai));
    }

    // -----------------------------------------------------------------------
    // The examples in docs/serialization.md#formatting, held exactly
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every example in the guide's Formatting section prints the literal its comment shows.
    /// </summary>
    /// <remarks>
    /// Found by the oracle catalogue (claim J02): the guide showed
    /// <c>CreateUnboundedStart(5).ToString()</c> as <c>"(,5]"</c>, but <c>endInclusive</c> defaults
    /// to <see langword="false"/>, so the range is <c>(,5)</c> and canonicalizes to <c>(,4]</c>; and
    /// it showed <c>CreateFinite(1, 10, endInclusive: false)</c> as <c>"[1,10)"</c>, which an
    /// <c>Int32Range</c> canonicalizes to <c>"[1,9]"</c> on construction. A reader copying either
    /// example would have taken a value to be in the range that is not. The guide now shows the
    /// canonical literals, and this test is what keeps every line of that block true.
    /// </remarks>
    [TestMethod]
    public void FormattingExamples_InTheSerializationGuide_PrintExactlyTheirComments()
    {
        Assert.AreEqual("[1,10]", Int32Range.CreateFinite(1, 10).ToString());
        Assert.AreEqual("[1,9]",  Int32Range.CreateFinite(1, 10, endInclusive: false).ToString());
        Assert.AreEqual("(,4]",   Int32Range.CreateUnboundedStart(5).ToString());
        Assert.AreEqual("[5,)",   Int32Range.CreateUnboundedEnd(5).ToString());
        Assert.AreEqual("(,)",    Int32Range.Infinite.ToString());
        Assert.AreEqual("empty",  Int32Range.Empty.ToString());
    }

    // -----------------------------------------------------------------------
    // Whitespace inside a quoted or escaped bound (#45)
    // -----------------------------------------------------------------------

    /// <summary>
    /// A quoted bound with whitespace at either end reads as the bound without it, as PostgreSQL
    /// reads it: <c>range_in</c> keeps the whitespace and every element input function strips it.
    /// </summary>
    /// <remarks>
    /// Until 9.1.0 only whitespace outside quotes was dropped, so the quoted space reached the strict
    /// ISO grammar and the literal threw, where <c>'[" 2026-01-01",2026-02-01)'::daterange</c> is
    /// <c>[2026-01-01,2026-02-01)</c>.
    /// </remarks>
    [TestMethod]
    public void Parse_QuotedBoundWithSurroundingWhitespace_ReadsTheTrimmedBound()
    {
        var expected = DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), endInclusive: false);

        Assert.AreEqual(expected, DateRange.Parse("[\" 2026-01-01\",2026-02-01)", CultureInfo.InvariantCulture));
        Assert.AreEqual(expected, DateRange.Parse("[\"2026-01-01\t\",\"\n2026-02-01 \")", CultureInfo.InvariantCulture));
        Assert.AreEqual(expected, DateRange.Parse("[\\ 2026-01-01,2026-02-01\\ )", CultureInfo.InvariantCulture));
        Assert.AreEqual(
            TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0), endInclusive: false),
            TimeRange.Parse("[\" 09:00 \",17:00)", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Only ASCII whitespace is stripped, as in PostgreSQL, and a bound that is nothing but
    /// whitespace is still present and empty — refused, never read as unbounded.
    /// </summary>
    [TestMethod]
    [DataRow("[\"\u00A02026-01-01\",2026-02-01)")]
    [DataRow("[\"2026-01-01\u2003\",2026-02-01)")]
    [DataRow("[\" \",2026-02-01)")]
    [DataRow("[\"\t\n\",2026-02-01)")]
    [DataRow("[\\ ,2026-02-01)")]
    public void Parse_QuotedBound_NonAsciiOrOnlyWhitespace_IsRefused(string literal)
        => Assert.ThrowsExactly<FormatException>(() => DateRange.Parse(literal, CultureInfo.InvariantCulture));

    /// <summary>
    /// The other half of the provider rule: with an explicit format the caller asks for display text,
    /// and both the format and the provider reach each bound. The literal half — no format, any
    /// provider, the invariant literal — is swept by <c>LiteralProviderContractTests</c>; this half
    /// was held by no test of an element type that uses the default <c>FormatValue</c> or a pinned
    /// temporal format, which the 10.0.0 mutation run showed by dropping either with everything green.
    /// </summary>
    [TestMethod]
    public void ExplicitFormat_ReachesEachBound_WithTheProvider()
    {
        var de = CultureInfo.GetCultureInfo("de-DE");

        Assert.AreEqual("[1.000,2.000]", ((IFormattable)Int32Range.CreateFinite(1000, 2000)).ToString("N0", de));
        Assert.AreEqual("[1.000,2.000]", ((IFormattable)Int64Range.CreateFinite(1000L, 2000L)).ToString("N0", de));
        Assert.AreEqual("[1,50,2,00)", ((IFormattable)DecimalRange.CreateFinite(1.5m, 2m)).ToString("0.00", de)); // display text: not quoted, not parseable
        Assert.AreEqual("[2026,2027]", ((IFormattable)DateRange.CreateFinite(new(2026, 1, 1), new(2027, 1, 1))).ToString("yyyy", de));
        Assert.AreEqual("[2026,2027)", ((IFormattable)DateTimeRange.CreateFinite(new(2026, 1, 1), new(2027, 1, 1))).ToString("yyyy", de));
        Assert.AreEqual("[2026,2027)", ((IFormattable)DateTimeOffsetRange.CreateFinite(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero))).ToString("yyyy", de));
        Assert.AreEqual("[09,10)", ((IFormattable)TimeRange.CreateFinite(new(9, 0), new(10, 0))).ToString("HH", de));
    }
}

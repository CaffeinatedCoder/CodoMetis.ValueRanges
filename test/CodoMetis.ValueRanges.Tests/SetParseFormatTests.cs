using System.Globalization;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class SetParseFormatTests
{
    // -----------------------------------------------------------------------
    // ToString — PostgreSQL array literal syntax
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToString_Empty_ReturnsEmptyBraces()
    {
        Assert.AreEqual("{}", Int32Set.Empty.ToString());
        Assert.AreEqual("{}", StringSet.Empty.ToString());
    }

    [TestMethod]
    public void ToString_Numbers_CanonicalOrderInvariant()
        => Assert.AreEqual("{1,2,10}", Int32Set.From(10, 2, 1).ToString());

    [TestMethod]
    public void ToString_Decimals_InvariantCulture()
        // Under a comma-decimal culture (de-DE) a current-culture bug would print "1,5".
        => Assert.AreEqual("{1.5,2.25}", DecimalSet.From(2.25m, 1.5m).ToString());

    [TestMethod]
    public void ToString_SimpleStrings_Unquoted()
        => Assert.AreEqual("{a,b}", StringSet.From("b", "a").ToString());

    [TestMethod]
    public void ToString_StringWithSpace_Quoted()
        => Assert.AreEqual("{\"a b\"}", StringSet.From("a b").ToString());

    [TestMethod]
    public void ToString_StringWithComma_Quoted()
        => Assert.AreEqual("{\"a,b\"}", StringSet.From("a,b").ToString());

    [TestMethod]
    public void ToString_StringWithQuote_EscapedAndQuoted()
        => Assert.AreEqual("{\"a\\\"b\"}", StringSet.From("a\"b").ToString());

    [TestMethod]
    public void ToString_StringWithBackslash_EscapedAndQuoted()
        => Assert.AreEqual("{\"a\\\\b\"}", StringSet.From(@"a\b").ToString());

    [TestMethod]
    public void ToString_EmptyString_Quoted()
        => Assert.AreEqual("{\"\"}", StringSet.From("").ToString());

    [TestMethod]
    public void ToString_LiteralNullWord_Quoted()
        => Assert.AreEqual("{\"NULL\"}", StringSet.From("NULL").ToString());

    [TestMethod]
    public void ToString_Dates_Iso8601()
    {
        var set = DateSet.From(new DateOnly(2024, 12, 24), new DateOnly(2024, 1, 1));

        Assert.AreEqual("{2024-01-01,2024-12-24}", set.ToString());
    }

    // -----------------------------------------------------------------------
    // Parse
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Parse_Empty_ReturnsEmptySingleton()
        => Assert.AreSame(Int32Set.Empty, Int32Set.Parse("{}", CultureInfo.InvariantCulture));

    [TestMethod]
    public void Parse_UnsortedWithDuplicates_Normalizes()
    {
        var set = Int32Set.Parse("{2,1,2}", CultureInfo.InvariantCulture);

        CollectionAssert.AreEqual(new[] { 1, 2 }, set.Values.ToArray());
    }

    [TestMethod]
    public void Parse_ToleratesWhitespace()
    {
        var set = Int32Set.Parse(" { 1 , 2 } ", CultureInfo.InvariantCulture);

        CollectionAssert.AreEqual(new[] { 1, 2 }, set.Values.ToArray());
    }

    [TestMethod]
    public void Parse_QuotedStrings_UnescapesAndPreserves()
    {
        var set = StringSet.Parse("{\"a b\",\"a\\\"b\",\"a\\\\b\",\"\"}", CultureInfo.InvariantCulture);

        CollectionAssert.AreEqual(new[] { "", "a b", "a\"b", @"a\b" }, set.Values.ToArray());
    }

    [TestMethod]
    public void Parse_QuotedNullWord_IsAValue()
    {
        var set = StringSet.Parse("{\"NULL\"}", CultureInfo.InvariantCulture);

        Assert.IsTrue(set.Contains("NULL"));
    }

    [TestMethod]
    public void Parse_UnquotedNull_Throws()
        => Assert.ThrowsExactly<FormatException>(
            () => StringSet.Parse("{a,NULL}", CultureInfo.InvariantCulture));

    [TestMethod]
    public void Parse_MissingBraces_Throws()
        => Assert.ThrowsExactly<FormatException>(
            () => Int32Set.Parse("1,2", CultureInfo.InvariantCulture));

    [TestMethod]
    public void Parse_EmptyUnquotedElement_Throws()
        => Assert.ThrowsExactly<FormatException>(
            () => Int32Set.Parse("{1,,2}", CultureInfo.InvariantCulture));

    [TestMethod]
    public void Parse_UnterminatedQuote_Throws()
        => Assert.ThrowsExactly<FormatException>(
            () => StringSet.Parse("{\"a}", CultureInfo.InvariantCulture));

    [TestMethod]
    public void Parse_InvalidElement_Throws()
        => Assert.ThrowsExactly<FormatException>(
            () => Int32Set.Parse("{a}", CultureInfo.InvariantCulture));

    [TestMethod]
    public void TryParse_Invalid_ReturnsFalseAndEmpty()
    {
        Assert.IsFalse(Int32Set.TryParse("nonsense", CultureInfo.InvariantCulture, out var result));
        Assert.AreSame(Int32Set.Empty, result);
    }

    [TestMethod]
    public void TryParse_Valid_ReturnsTrue()
    {
        Assert.IsTrue(Int32Set.TryParse("{1,2}", CultureInfo.InvariantCulture, out var result));
        Assert.AreEqual(Int32Set.From(1, 2), result);
    }

    // -----------------------------------------------------------------------
    // Round trips
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RoundTrip_Strings_IncludingSpecialCharacters()
    {
        var original = StringSet.From("plain", "with space", "with,comma", "with\"quote", @"with\backslash", "", "NULL");

        var parsed = StringSet.Parse(original.ToString(), CultureInfo.InvariantCulture);

        Assert.AreEqual(original, parsed);
    }

    [TestMethod]
    public void RoundTrip_AllClosedTypes()
    {
        Assert.AreEqual(Int16Set.From((short)1, (short)2), Int16Set.Parse(Int16Set.From((short)1, (short)2).ToString(), null));
        Assert.AreEqual(Int32Set.From(1, 2), Int32Set.Parse(Int32Set.From(1, 2).ToString(), null));
        Assert.AreEqual(Int64Set.From(1L, 2L), Int64Set.Parse(Int64Set.From(1L, 2L).ToString(), null));
        Assert.AreEqual(DecimalSet.From(1.5m), DecimalSet.Parse(DecimalSet.From(1.5m).ToString(), null));

        var guids = GuidSet.From(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Assert.AreEqual(guids, GuidSet.Parse(guids.ToString(), null));

        var dates = DateSet.From(new DateOnly(2024, 1, 1));
        Assert.AreEqual(dates, DateSet.Parse(dates.ToString(), null));

        var times = TimeSet.From(new TimeOnly(9, 30, 15));
        Assert.AreEqual(times, TimeSet.Parse(times.ToString(), null));

        var timestamps = DateTimeSet.From(new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Unspecified));
        Assert.AreEqual(timestamps, DateTimeSet.Parse(timestamps.ToString(), null));

        var instants = DateTimeOffsetSet.From(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.FromHours(2)));
        Assert.AreEqual(instants, DateTimeOffsetSet.Parse(instants.ToString(), null));
    }

    /// <summary>Decimal elements parse with PostgreSQL's numeric grammar; a group separator is not part of it.</summary>
    [TestMethod]
    public void DecimalElement_WithAGroupSeparator_IsRejected()
        => Assert.ThrowsExactly<FormatException>(() => DecimalSet.Parse("{\"1,000\"}", null));

    // The wrapper arity reads the same grammar, whatever its element's Parse accepts. TestMoney is
    // the generated shape — decimal.Parse with NumberStyles.Number — which allows group separators
    // and no exponent: the opposite of PostgreSQL's numeric grammar on both counts.

    /// <summary>
    /// A group separator is refused by the wrapper arity too. Before 9.0.0 the raw text reached
    /// <c>TestMoney.Parse</c>, and <c>{"1,5"}</c> was fifteen, silently.
    /// </summary>
    [TestMethod]
    public void WrapperDecimalElement_WithAGroupSeparator_IsRejected()
    {
        Assert.ThrowsExactly<FormatException>(() => DecimalSet<TestMoney>.Parse("{\"1,5\"}", null));
        Assert.ThrowsExactly<FormatException>(() => DecimalSet<TestMoney>.Parse("{\"1,000.5\"}", null));
        Assert.IsFalse(DecimalSet<TestMoney>.TryParse("{\"1,000\"}", null, out _));
    }

    /// <summary>Exponent notation is numeric grammar, and reads as it does for the primitive family.</summary>
    [TestMethod]
    public void WrapperDecimalElement_InExponentNotation_ReadsLikeThePrimitive()
    {
        Assert.AreEqual(DecimalSet<TestMoney>.From(new TestMoney(1000m)), DecimalSet<TestMoney>.Parse("{1e3}", null));
        Assert.AreEqual(DecimalSet.Parse("{1.5E-1}", null).ToString(), DecimalSet<TestMoney>.Parse("{1.5E-1}", null).ToString());
    }

    /// <summary>Parsing with the family grammar first must not cost the element its scale.</summary>
    [TestMethod]
    public void WrapperDecimalElement_KeepsItsScale()
        => Assert.AreEqual("{1.50,12.500}", DecimalSet<TestMoney>.Parse("{12.500,1.50}", null).ToString());

    // -----------------------------------------------------------------------
    // The array_in grammar — every expectation below is what PostgreSQL 17 answers for the same
    // text cast to text[] (LiteralGrammarParityTests in the integration project asks a live
    // server). Before 9.0.0 the unquoted element ran to the next comma or quote with no escapes
    // and no structural characters, so {a\,b} was two elements and {{a,b}} a one-dimensional set
    // of "{a" and "b}" — both silently.
    // -----------------------------------------------------------------------

    /// <summary>A backslash escapes the next character in an unquoted element too.</summary>
    [TestMethod]
    [DataRow(@"{a\,b}", "a,b")]
    [DataRow(@"{a\\b}", @"a\b")]
    [DataRow("{\\\"a}", "\"a")]
    [DataRow(@"{a\ }", "a ")]
    [DataRow(@"{\ a}", " a")]
    [DataRow(@"{a b\ }", "a b ")]
    [DataRow(@"{a\}b}", "a}b")]
    [DataRow(@"{\{a}", "{a")]
    public void Parse_UnquotedElement_HonoursBackslashEscapes(string literal, string expected)
        => Assert.AreEqual(StringSet.From(expected), StringSet.Parse(literal, null));

    /// <summary>
    /// An escaped <c>NULL</c> is the four-character string, as a quoted one is: only a bare,
    /// unescaped <c>NULL</c> is the null marker.
    /// </summary>
    [TestMethod]
    public void Parse_EscapedNullWord_IsAValue()
        => Assert.AreEqual(StringSet.From("NULL"), StringSet.Parse(@"{N\ULL}", null));

    /// <summary>Whitespace around an unquoted element is dropped; whitespace inside it is kept.</summary>
    [TestMethod]
    public void Parse_UnquotedElement_KeepsInnerWhitespace()
    {
        Assert.AreEqual(StringSet.From("a b"), StringSet.Parse("{ a b }", null));
        Assert.AreEqual(StringSet.From("a", "b"), StringSet.Parse("{\"a\" , \"b\"}", null));

        // Only ASCII whitespace surrounds an element; a no-break space is element text, as in PostgreSQL.
        Assert.AreEqual(StringSet.From("\u00A0a"), StringSet.Parse("{\u00A0a}", null));
    }

    /// <summary>
    /// A quoted or escaped string element keeps its surrounding whitespace, as PostgreSQL's
    /// <c>textin</c> does: <c>'{" ab "}'::text[]</c> is <c>{" ab "}</c>. Only the temporal families
    /// strip it (#70), so the trim must never move into the shared array parser.
    /// </summary>
    [TestMethod]
    public void Parse_QuotedOrEscapedStringElement_KeepsSurroundingWhitespace()
    {
        Assert.AreEqual(StringSet.From(" ab "), StringSet.Parse("{\" ab \"}", null));
        Assert.AreEqual(StringSet.From("\tab"), StringSet.Parse("{\"\tab\"}", null));
        Assert.AreEqual(StringSet.From(" ab"), StringSet.Parse("{\\ ab}", null));
        Assert.AreEqual(StringSet.From(" "), StringSet.Parse("{\" \"}", null));
        Assert.AreEqual("{\" ab \"}", StringSet.Parse("{\" ab \"}", null).ToString());
    }

    /// <summary>
    /// A quoted or escaped temporal element with whitespace at either end reads as the element
    /// without it, as PostgreSQL's <c>date_in</c>, <c>timestamp_in</c>, <c>timestamptz_in</c> and
    /// <c>time_in</c> read it.
    /// </summary>
    /// <remarks>
    /// Until 9.1.0 the whitespace reached the strict ISO grammar and the literal threw, where
    /// <c>'{" 2026-01-01"}'::date[]</c> is <c>{2026-01-01}</c> (#70).
    /// </remarks>
    [TestMethod]
    public void Parse_QuotedTemporalElementWithSurroundingWhitespace_ReadsTheTrimmedElement()
    {
        Assert.AreEqual(DateSet.From(new DateOnly(2026, 1, 1)), DateSet.Parse("{\" 2026-01-01\"}", null));
        Assert.AreEqual(DateSet.From(new DateOnly(2026, 1, 1)), DateSet.Parse("{\\\t2026-01-01}", null));
        Assert.AreEqual(TimeSet.From(new TimeOnly(9, 0)), TimeSet.Parse("{\"09:00\n\"}", null));
        Assert.AreEqual(
            DateTimeSet.From(new DateTime(2026, 1, 1, 10, 0, 0)),
            DateTimeSet.Parse("{\" 2026-01-01 10:00:00 \"}", null));
        Assert.AreEqual(
            DateTimeOffsetSet.From(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero)),
            DateTimeOffsetSet.Parse("{\"2026-01-01 10:00:00+00\\ \"}", null));
        Assert.AreEqual(
            DateSet.From(new DateOnly(2026, 1, 1)),
            System.Text.Json.JsonSerializer.Deserialize<DateSet>("[\" 2026-01-01\"]"));
    }

    /// <summary>
    /// Only ASCII whitespace is stripped from a temporal element, and an element of only whitespace
    /// is still rejected — as PostgreSQL rejects both.
    /// </summary>
    [TestMethod]
    [DataRow("{\"\u00A02026-01-01\"}")]
    [DataRow("{\"2026-01-01\u2003\"}")]
    [DataRow("{\" \"}")]
    [DataRow("{\\ }")]
    public void Parse_TemporalElement_NonAsciiOrOnlyWhitespace_IsRejected(string literal)
        => Assert.ThrowsExactly<FormatException>(() => DateSet.Parse(literal, null));

    /// <summary>
    /// Text PostgreSQL refuses, or reads as something a one-dimensional set cannot be. A nested
    /// brace is a multi-dimensional array; an unescaped brace or quote inside an unquoted element,
    /// text after a quoted one, a dangling escape and a missing element are malformed.
    /// </summary>
    [TestMethod]
    [DataRow("{{a,b}}")]
    [DataRow("{{a},{b}}")]
    [DataRow("{a}b}")]
    [DataRow("{a{b}")]
    [DataRow("{a\"b}")]
    [DataRow("{a\"}")]
    [DataRow("{a\"b\"c}")]
    [DataRow("{\"a\"b}")]
    [DataRow("{\"x\" y}")]
    [DataRow(@"{a\}")]
    [DataRow("{a,}")]
    [DataRow("{,a}")]
    [DataRow("{a,,b}")]
    [DataRow("{\"a\",}")]
    public void Parse_MalformedArrayLiteral_IsRejected(string literal)
    {
        Assert.ThrowsExactly<FormatException>(() => StringSet.Parse(literal, null));
        Assert.IsFalse(StringSet.TryParse(literal, null, out _));
    }

    /// <summary>
    /// Whatever the grammar reads, the formatter must still quote everything that needs it:
    /// <c>Parse(Format(x)) == x</c> for strings made of every character the grammar treats
    /// specially, alone and in combination.
    /// </summary>
    [TestMethod]
    public void RoundTrip_EveryStructuralCharacter()
    {
        string[] atoms = ["\\", "{", "}", "\"", ",", " ", "\t", "\n", "\u00A0", "NULL", "null", "a", ""];

        var strings = new List<string>();
        foreach (var first in atoms)
        foreach (var second in atoms)
        foreach (var third in atoms)
            strings.Add(first + second + third);

        Assert.IsGreaterThan(1000, strings.Count);

        foreach (var chunk in strings.Chunk(50))
        {
            var original = StringSet.From(chunk);
            var literal  = original.ToString();

            Assert.AreEqual(original, StringSet.Parse(literal, null), $"Round trip changed {literal}");
        }
    }
}

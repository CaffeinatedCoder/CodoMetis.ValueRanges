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
}

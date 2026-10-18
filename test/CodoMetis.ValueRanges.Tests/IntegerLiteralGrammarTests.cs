using System.Globalization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The integer bound and element grammar is PostgreSQL 16's <c>int2</c>/<c>int4</c>/<c>int8</c> input:
/// decimal, or <c>0x</c>, <c>0o</c> or <c>0b</c> digits, with single underscores between digits. Until
/// 10.0.0 only the decimal form was read, so <c>[0x1F,)</c> and <c>{1_000}</c> threw (#127).
/// </summary>
/// <remarks>
/// Every row is PostgreSQL's answer, measured on 16 and 18 (they agree) with
/// <c>pg_input_is_valid(text, 'int2' / 'int4' / 'int8')</c> and <c>text::int8::text</c>; the last
/// column is the <c>int8</c> value, or <see langword="null"/> where <c>int8</c> refuses the text.
/// <c>LiteralCorpusProbeTests</c> (integration) asks a live server about the same forms in a bound
/// and an element for every integer range, multirange and set type.
/// </remarks>
[TestClass]
public class IntegerLiteralGrammarTests
{
    [TestMethod]
    [DataRow("0x1F", true, true, "31")]
    [DataRow("0X1F", true, true, "31")]
    [DataRow("0x1f", true, true, "31")]
    [DataRow("-0x1F", true, true, "-31")]
    [DataRow("+0x1F", true, true, "31")]
    [DataRow("0x", false, false, null)]
    [DataRow("0x_1F", true, true, "31")]
    [DataRow("0x1_F", true, true, "31")]
    [DataRow("0x1__F", false, false, null)]
    [DataRow("0x1F_", false, false, null)]
    [DataRow("_1", false, false, null)]
    [DataRow("1_", false, false, null)]
    [DataRow("1__000", false, false, null)]
    [DataRow("1_000", true, true, "1000")]
    [DataRow("1_000_000", false, true, "1000000")]
    [DataRow("0_1", true, true, "1")]
    [DataRow("01_0", true, true, "10")]
    [DataRow("0o17", true, true, "15")]
    [DataRow("0O17", true, true, "15")]
    [DataRow("0o8", false, false, null)]
    [DataRow("0o_17", true, true, "15")]
    [DataRow("0b101", true, true, "5")]
    [DataRow("0B101", true, true, "5")]
    [DataRow("0b2", false, false, null)]
    [DataRow("0b_1", true, true, "1")]
    [DataRow("00x1", false, false, null)]
    [DataRow("0x 1F", false, false, null)]
    [DataRow(" 0x1F ", true, true, "31")]
    [DataRow("0x7FFFFFFF", false, true, "2147483647")]
    [DataRow("0x80000000", false, false, "2147483648")]
    [DataRow("-0x80000000", false, true, "-2147483648")]
    [DataRow("-0x80000001", false, false, "-2147483649")]
    [DataRow("0xFFFFFFFF", false, false, "4294967295")]
    [DataRow("0x0", true, true, "0")]
    [DataRow("-0", true, true, "0")]
    [DataRow("0x-1", false, false, null)]
    [DataRow("- 0x1", false, false, null)]
    [DataRow("0xG", false, false, null)]
    [DataRow("1e3", false, false, null)]
    [DataRow("0x1F.0", false, false, null)]
    [DataRow("\u0663", false, false, null)]
    [DataRow("0x1F\u00A0", false, false, null)]
    [DataRow("0x000000000000001F", true, true, "31")]
    [DataRow("1_0x", false, false, null)]
    [DataRow("0b", false, false, null)]
    [DataRow("0o", false, false, null)]
    [DataRow("-_1", false, false, null)]
    [DataRow("+_1", false, false, null)]
    [DataRow("0_x1", false, false, null)]
    [DataRow("0x7FFFFFFFFFFFFFFF", false, false, "9223372036854775807")]
    [DataRow("-0x8000000000000000", false, false, "-9223372036854775808")]
    [DataRow("0x8000000000000000", false, false, null)]
    [DataRow("0x7FFF", true, true, "32767")]
    [DataRow("-0x8000", true, true, "-32768")]
    [DataRow("0x8000", false, true, "32768")]
    [DataRow("-0b1000000000000000", true, true, "-32768")]
    [DataRow("0o77777", true, true, "32767")]
    public void ParseValue_ReadsWhatPostgresReads(string text, bool int2, bool int4, string? int8)
    {
        Assert.AreEqual(int8, Read(() => Int64Range.ParseValue(text, null)), "Int64Range");
        Assert.AreEqual(int8, Read(() => Int64Set.ParseValue(text, null)), "Int64Set");
        Assert.AreEqual(int4 ? int8 : null, Read(() => Int32Range.ParseValue(text, null)), "Int32Range");
        Assert.AreEqual(int4 ? int8 : null, Read(() => Int32Set.ParseValue(text, null)), "Int32Set");
        Assert.AreEqual(int2 ? int8 : null, Read(() => Int16Set.ParseValue(text, null)), "Int16Set");
    }

    /// <summary>
    /// The forms reach every literal: a range bound, a multirange bound and a set element, each read
    /// as the number it spells.
    /// </summary>
    [TestMethod]
    public void Literals_ReadTheForms()
    {
        Assert.AreEqual(Int32Range.CreateUnboundedEnd(31), Int32Range.Parse("[0x1F,)", CultureInfo.InvariantCulture));
        Assert.AreEqual(Int64Range.CreateFinite(-15, 1_000), Int64Range.Parse("[-0o17,1_000]", CultureInfo.InvariantCulture));
        Assert.AreEqual(Int32Set.From(1000, 15, 5), Int32Set.Parse("{1_000,0o17,0b101}", CultureInfo.InvariantCulture));
        Assert.AreEqual(Int16Set.From(short.MinValue), Int16Set.Parse("{-0x8000}", CultureInfo.InvariantCulture));
        Assert.AreEqual(
            RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 2)]),
            RangeSet<Int32Range, int>.Parse("{[0b1,0x2]}", CultureInfo.InvariantCulture));
    }

    /// <summary>A magnitude too large for the type overflows, as the decimal form always has.</summary>
    [TestMethod]
    [DataRow("0x80000000")]
    [DataRow("-0x80000001")]
    [DataRow("0b1_0000_0000_0000_0000_0000_0000_0000_0000")]
    public void ParseValue_Overflows_BeyondTheType(string text)
        => Assert.ThrowsExactly<OverflowException>(() => Int32Range.ParseValue(text, null));

    private static string? Read<T>(Func<T> parse) where T : IFormattable
    {
        try
        {
            return parse().ToString(null, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return null;
        }
    }
}

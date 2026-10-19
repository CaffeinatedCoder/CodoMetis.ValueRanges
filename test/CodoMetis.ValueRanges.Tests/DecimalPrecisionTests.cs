using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Decimal text is read exactly or refused (#115): <c>decimal.Parse</c> rounds a 29th significant
/// digit, a scale beyond 28 and <c>1e-30</c> to the nearest representable value without a word, where
/// PostgreSQL keeps every digit, so a range or set in memory held a different number from the server's
/// for the same text. Every entry point of <see cref="DecimalRange"/>, <c>RangeSet&lt;DecimalRange&gt;</c>,
/// <see cref="DecimalSet"/> and <c>DecimalSet&lt;T&gt;</c> refuses such text with <see cref="FormatException"/>
/// (<c>JsonException</c> from JSON, <c>false</c> from <c>TryParse</c>), and reads what decimal holds exactly.
/// </summary>
[TestClass]
public sealed class DecimalPrecisionTests
{
    private static readonly JsonSerializerOptions ReadingFromString =
        new() { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    /// <summary>Texts that name a number decimal cannot hold: a 29th significant digit, a scale of 29 or 30, a precision the magnitude leaves no room for.</summary>
    [TestMethod]
    [DataRow("1.00000000000000000000000000001")]
    [DataRow("0.00000000000000000000000000001")]
    [DataRow("1e-30")]
    [DataRow("0.5e-28")]
    [DataRow("79228162514264337593543950335.1")]
    [DataRow("1234567890123456789012345678.95")]
    [DataRow("-1.00000000000000000000000000001")]
    public void TextDecimalWouldRound_IsRefused_AtEveryEntryPoint(string text)
    {
        var error = Assert.ThrowsExactly<FormatException>(() => DecimalRange.Parse($"[{text},)", null));
        StringAssert.Contains(error.Message, "More precision than decimal holds");
        Assert.ThrowsExactly<FormatException>(() => DecimalRange.Parse($"[{text},)".AsSpan(), null));
        Assert.IsFalse(DecimalRange.TryParse($"[{text},)", null, out _));
        Assert.IsFalse(DecimalRange.TryParse($"(,{text}]".AsSpan(), null, out _));
        Assert.ThrowsExactly<FormatException>(() => RangeSet<DecimalRange, decimal>.Parse($"{{[{text},)}}", null));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalRange>($"\"[{text},)\""));
        Assert.ThrowsExactly<FormatException>(() => TypeDescriptor.GetConverter(typeof(DecimalRange)).ConvertFromInvariantString($"[{text},)"));

        Assert.ThrowsExactly<FormatException>(() => DecimalSet.Parse($"{{{text}}}", null));
        Assert.ThrowsExactly<FormatException>(() => DecimalSet.Parse($"{{\"{text}\"}}".AsSpan(), null));
        Assert.IsFalse(DecimalSet.TryParse($"{{{text},1}}", null, out _));
        Assert.ThrowsExactly<FormatException>(() => DecimalSet<TestMoney>.Parse($"{{{text}}}", null));
        Assert.ThrowsExactly<FormatException>(() => TypeDescriptor.GetConverter(typeof(DecimalSet)).ConvertFromInvariantString($"{{{text}}}"));

        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalSet>($"[{text}]"));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalSet<TestMoney>>($"[{text}]"));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalSet>($"[\"{text}\"]", ReadingFromString));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalSet<TestMoney>>($"[\"{text}\"]", ReadingFromString));
    }

    /// <summary>The check is on the value: every spelling of a number decimal holds exactly still reads, scale included.</summary>
    [TestMethod]
    [DataRow("1.000000000000000000000000001", "1.000000000000000000000000001")]
    [DataRow("1.0000000000000000000000000000", "1.0000000000000000000000000000")]
    [DataRow("1.00000000000000000000000000000", "1.0000000000000000000000000000")]
    [DataRow("1e-28", "0.0000000000000000000000000001")]
    [DataRow("79228162514264337593543950335", "79228162514264337593543950335")]
    [DataRow("1e3", "1000")]
    [DataRow("+1.50", "1.50")]
    [DataRow("007.50", "7.50")]
    [DataRow("-0", "0")]
    [DataRow(".5", "0.5")]
    [DataRow("5.", "5")]
    [DataRow("12.5E-1", "1.25")]
    public void TextDecimalHoldsExactly_Reads(string text, string expected)
    {
        var value = decimal.Parse(expected, CultureInfo.InvariantCulture);

        Assert.AreEqual(DecimalRange.CreateUnboundedEnd(value), DecimalRange.Parse($"[{text},)", null));
        Assert.AreEqual(DecimalSet.From(value), DecimalSet.Parse($"{{{text}}}", null));
        Assert.AreEqual(DecimalSet.From(value), DecimalSet.Parse($"{{\" {text} \"}}", null));
        Assert.AreEqual(DecimalSet.From(value), JsonSerializer.Deserialize<DecimalSet>($"[\"{text}\"]", ReadingFromString));

        // Scale survives, as the literal and the JSON number are the text's own.
        Assert.AreEqual(expected, DecimalSet.Parse($"{{{text}}}", null).Values[0].ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A JSON number is read from its own text, so the primitive set agrees with its wrapper and keeps scale.</summary>
    [TestMethod]
    public void JsonNumber_IsReadFromItsOwnText()
    {
        Assert.AreEqual("12.50", JsonSerializer.Deserialize<DecimalSet>("[12.50]")!.Values[0].ToString(CultureInfo.InvariantCulture));
        Assert.AreEqual(DecimalSet.From(1000m), JsonSerializer.Deserialize<DecimalSet>("[1e3]"));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalSet>("[\"1.5\"]"));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalSet>("[true]"));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalSet>("[1e30]"));
    }

    /// <summary>
    /// The exponent costs nothing (#276): until 10.0.0 it went into <c>BigInteger.Pow</c>, so a ten-character
    /// element cost work that grew faster than its exponent, and <c>{0e-10000000}</c> ran for minutes. The
    /// allocation bound fails, rather than hangs, against that code: the rows it survives come first.
    /// </summary>
    [TestMethod]
    public void Exponent_CostsNoWorkProportionalToItself()
    {
        DecimalSet.TryParse("{1e-1}", null, out _);
        foreach (var text in new[] { "1e-1000000", "0e-1000000", "5e-100000", "0.1e1000000" })
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            DecimalSet.TryParse($"{{{text}}}", null, out _);
            DecimalRange.TryParse($"[{text},)", null, out _);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsLessThan(16 * 1024, allocated, $"{text} allocated {allocated} bytes");
        }

        Assert.AreEqual(DecimalSet.From(0m), DecimalSet.Parse("{0e-10000000}", null));
        Assert.AreEqual(DecimalRange.CreateUnboundedStart(0m), DecimalRange.Parse("(,0e10000000)", null));
        var error = Assert.ThrowsExactly<FormatException>(() => DecimalSet.Parse("{1e-10000000}", null));
        StringAssert.Contains(error.Message, "More precision than decimal holds");
    }

    /// <summary>
    /// An exponent beyond <see cref="int"/> is refused with <see cref="FormatException"/>, as <c>Parse</c>
    /// documents, unless the digits are zero (#276): it was read with <c>int.Parse</c>, which threw
    /// <see cref="OverflowException"/> for text <c>decimal.Parse</c> had already accepted.
    /// </summary>
    [TestMethod]
    public void ExponentBeyondInt32_IsReadOrRefusedAsFormat()
    {
        Assert.AreEqual(DecimalSet.From(0m), DecimalSet.Parse("{0e99999999999}", null));
        Assert.AreEqual(DecimalSet.From(0m), DecimalSet.Parse("{-0.000e-99999999999}", null));
        Assert.ThrowsExactly<FormatException>(() => DecimalSet.Parse("{1e-99999999999}", null));
        Assert.ThrowsExactly<FormatException>(() => DecimalRange.Parse("[1e-99999999999,)", null));
    }

    /// <summary>
    /// The text's length costs no stack (#276): until 10.0.0 its digits were buffered on the stack at its
    /// length, so an element <c>decimal.Parse</c> accepts — leading or trailing zeros — overflowed it, which
    /// no handler catches: <c>TryParse</c> and JSON killed the process. Against that code this test kills the
    /// host rather than fails; it runs on a 256 KB thread so the input it needs stays small.
    /// </summary>
    [TestMethod]
    public void LongElement_CostsNoStackProportionalToItsLength()
    {
        var zeros = new string('0', 1_000_000);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.AreEqual(DecimalSet.From(1m), DecimalSet.Parse($"{{1.{zeros}}}", null));
                Assert.AreEqual(DecimalSet.From(5m), DecimalSet.Parse($"{{{zeros}5}}", null));
                Assert.IsFalse(DecimalSet.TryParse($"{{0.{zeros}1}}", null, out _));
                Assert.IsFalse(DecimalRange.TryParse($"[1.{zeros}1,)", null, out _));
                Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DecimalSet>($"[0.{zeros}1]"));
            }
            catch (Exception e)
            {
                failure = e;
            }
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        if (failure is not null) throw failure;
    }

    /// <summary>A magnitude that does not fit still overflows, as before: only excess precision was folded in silently.</summary>
    [TestMethod]
    public void MagnitudeBeyondDecimal_StillOverflows()
    {
        Assert.ThrowsExactly<OverflowException>(() => DecimalRange.Parse("[1e30,)", null));
        Assert.ThrowsExactly<OverflowException>(() => DecimalSet.Parse("{79228162514264337593543950336}", null));
    }
}

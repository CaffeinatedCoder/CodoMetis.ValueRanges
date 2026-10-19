using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// No numeric bound or element is spelled with U+0000: PostgreSQL text cannot hold one
/// (<c>('31'||chr(0))::int</c> is "null character not permitted"). The BCL number parsers ignore
/// trailing NULs, so each reader has to refuse it itself. #233 did that for integer text; until #251
/// the decimal family read <c>31\0</c> as 31 in every entry point, and the integer wrapper sets read a
/// quoted <c>"31\u0000"</c> from JSON while the plain sets threw.
/// </summary>
[TestClass]
public class NumericNulTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions FromStrings = new() { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    [TestMethod]
    [DataRow("31\0")]
    [DataRow("1e2\0")]
    [DataRow("1.5\0")]
    [DataRow("31\0\0")]
    public void DecimalFamily_RejectsANul(string text)
    {
        Rejects("DecimalRange.ParseValue", () => DecimalRange.ParseValue(text, null));
        Rejects("DecimalSet.ParseValue", () => DecimalSet.ParseValue(text, null));
        Rejects("DecimalSet<TestMoney>.ParseValue", () => DecimalSet<TestMoney>.ParseValue(text, null));
        Refuses("DecimalRange literal", DecimalRange.TryParse($"[{text},40)", Invariant, out _));
        Refuses("DecimalRange.Finite literal", DecimalRange.Finite.TryParse($"[{text},40)", Invariant, out _));
        Refuses("RangeSet<DecimalRange> literal", RangeSet<DecimalRange, decimal>.TryParse($"{{[{text},40)}}", Invariant, out _));
        Refuses("DecimalSet literal", DecimalSet.TryParse($"{{\"{text}\"}}", Invariant, out _));
        Refuses("DecimalSet<TestMoney> literal", DecimalSet<TestMoney>.TryParse($"{{\"{text}\"}}", Invariant, out _));
        RejectsJson<DecimalRange>($"\"[{Json(text)},40)\"");
        RejectsJson<DecimalSet>($"[\"{Json(text)}\"]");
        RejectsJson<DecimalSet<TestMoney>>($"[\"{Json(text)}\"]");
    }

    [TestMethod]
    [DataRow("31\0")]
    [DataRow("-5\0")]
    public void IntegerFamily_RejectsANul_InJsonToo(string text)
    {
        RejectsJson<Int16Set>($"[\"{Json(text)}\"]");
        RejectsJson<Int32Set>($"[\"{Json(text)}\"]");
        RejectsJson<Int64Set>($"[\"{Json(text)}\"]");
        RejectsJson<Int16Set<TestSmallId>>($"[\"{Json(text)}\"]");
        RejectsJson<Int32Set<TestIntId>>($"[\"{Json(text)}\"]");
        RejectsJson<Int64Set<TestLongId>>($"[\"{Json(text)}\"]");
        RejectsJson<Int32Range>($"\"[{Json(text)},40)\"");
    }

    /// <summary>The float families validate their own grammar first; held here so a later shortcut keeps it.</summary>
    [TestMethod]
    [DataRow("31\0")]
    [DataRow("1.5\0")]
    public void FloatFamilies_RejectANul(string text)
    {
        Rejects("Float64Range.ParseValue", () => Float64Range.ParseValue(text, null));
        Rejects("Float32Range.ParseValue", () => Float32Range.ParseValue(text, null));
        Refuses("Float64Set literal", Float64Set.TryParse($"{{\"{text}\"}}", Invariant, out _));
        Refuses("Float32Set<TestWeight> literal", Float32Set<TestWeight>.TryParse($"{{\"{text}\"}}", Invariant, out _));
        RejectsJson<Float64Set>($"[\"{Json(text)}\"]");
        RejectsJson<Float64Set<TestScore>>($"[\"{Json(text)}\"]");
        RejectsJson<Float32Set<TestWeight>>($"[\"{Json(text)}\"]");
    }

    private static string Json(string text) => text.Replace("\0", "\\u0000", StringComparison.Ordinal);

    private static void Rejects(string what, Action parse)
    {
        try
        {
            parse();
        }
        catch (FormatException)
        {
            return;
        }

        Assert.Fail($"{what} read a NUL.");
    }

    private static void Refuses(string what, bool read) => Assert.IsFalse(read, $"{what} read a NUL.");

    private static void RejectsJson<T>(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, FromStrings);
            Assert.Fail($"{typeof(T).Name} JSON {json} read as {value}.");
        }
        catch (JsonException)
        {
        }
    }
}

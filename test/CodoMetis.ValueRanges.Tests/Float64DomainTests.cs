using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// <see cref="Float64Range"/> and <see cref="Float64Set"/> hold finite doubles only, and write them as
/// PostgreSQL's <c>float8out</c> does (#153). PostgreSQL sorts <c>NaN</c> above every number and .NET
/// below, so the guard is on every construction path and every scalar operand: a <c>NaN</c> that got in
/// anywhere would answer differently on each side, silently. The texts are the ones PostgreSQL 16 and 18
/// printed for the same doubles.
/// </summary>
[TestClass]
public sealed class Float64DomainTests
{
    private static readonly double[] NotFinite = [double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    private static readonly JsonSerializerOptions ReadingFromString =
        new() { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [TestMethod]
    public void EveryFactory_RefusesNaNAndTheInfinities()
    {
        foreach (var value in NotFinite)
        {
            Assert.ThrowsExactly<ArgumentException>(() => Float64Range.CreateFinite(value, 5));
            Assert.ThrowsExactly<ArgumentException>(() => Float64Range.CreateFinite(5, value));
            Assert.ThrowsExactly<ArgumentException>(() => Float64Range.CreateFinite(value, value, true, true));
            Assert.ThrowsExactly<ArgumentException>(() => Float64Range.CreateUnboundedStart(value));
            Assert.ThrowsExactly<ArgumentException>(() => Float64Range.CreateUnboundedEnd(value));
            Assert.ThrowsExactly<ArgumentException>(() => new Float64Range.UnboundedStart(value, true));
            Assert.ThrowsExactly<ArgumentException>(() => new Float64Range.UnboundedEnd(value, false));

            Assert.ThrowsExactly<ArgumentException>(() => Float64Set.From(1, value));
            Assert.ThrowsExactly<ArgumentException>(() => Float64Set.From(new List<double> { value }));
            Assert.ThrowsExactly<ArgumentException>(() => Float64Set<TestScore>.From(new TestScore(value)));
            Assert.ThrowsExactly<ArgumentException>(() => Float64Set<TestScore>.From(new List<TestScore> { new(1), new(value) }));
        }
    }

    /// <summary>
    /// <c>[5,NaN)</c> is the row that shows why: under <c>CompareTo</c> it is inverted and would be the empty
    /// range, while PostgreSQL holds it as a range containing <c>1e308</c>.
    /// </summary>
    [TestMethod]
    public void FivetoNaN_IsRefused_NotEmpty()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Float64Range.CreateFinite(5, double.NaN));
        StringAssert.Contains(error.Message, "finite numbers only");
        Assert.AreEqual("end", error.ParamName);
    }

    /// <summary>
    /// Every scalar operand, on every shape: the empty range and set compare nothing, and the infinite range
    /// and set contain everything without comparing, so a guard inside the comparison would miss them.
    /// <c>[0,)</c> contains <c>NaN</c> in PostgreSQL and not under <c>CompareTo</c>.
    /// </summary>
    [TestMethod]
    public void EveryScalarOperand_IsRefused_OnEveryShape()
    {
        Float64Range[] ranges =
        [
            Float64Range.Empty, Float64Range.Infinite, Float64Range.CreateFinite(0, 5),
            Float64Range.CreateUnboundedEnd(0), Float64Range.CreateUnboundedStart(0)
        ];
        RangeSet<Float64Range, double>[] sets =
        [
            RangeSet<Float64Range, double>.Empty, RangeSet<Float64Range, double>.Infinite,
            RangeSet<Float64Range, double>.From([Float64Range.CreateFinite(0, 1), Float64Range.CreateFinite(2, 3)])
        ];

        foreach (var value in NotFinite)
        {
            foreach (var range in ranges)
            {
                Assert.ThrowsExactly<ArgumentException>(() => range.Contains(value), $"{range}.Contains({value})");
                Assert.ThrowsExactly<ArgumentException>(() => range.Clamp(value), $"{range}.Clamp({value})");
            }

            foreach (var set in sets)
                Assert.ThrowsExactly<ArgumentException>(() => set.Contains(value), $"{set}.Contains({value})");

            foreach (var set in new[] { Float64Set.Empty, Float64Set.From(1, 2) })
            {
                Assert.ThrowsExactly<ArgumentException>(() => set.Contains(value), $"{set}.Contains({value})");
                Assert.ThrowsExactly<ArgumentException>(() => set.Add(value), $"{set}.Add({value})");
                Assert.ThrowsExactly<ArgumentException>(() => set.Remove(value), $"{set}.Remove({value})");
            }

            foreach (var set in new[] { Float64Set<TestScore>.Empty, Float64Set<TestScore>.From(new TestScore(1)) })
            {
                Assert.ThrowsExactly<ArgumentException>(() => set.Contains(new TestScore(value)));
                Assert.ThrowsExactly<ArgumentException>(() => set.Add(new TestScore(value)));
                Assert.ThrowsExactly<ArgumentException>(() => set.Remove(new TestScore(value)));
            }
        }
    }

    /// <summary>The spellings <c>float8in</c> takes that this library refuses, and numbers it reports out of range.</summary>
    [TestMethod]
    [DataRow("NaN")]
    [DataRow("nan")]
    [DataRow("Infinity")]
    [DataRow("-Infinity")]
    [DataRow("inf")]
    [DataRow("+inf")]
    [DataRow("∞")]
    [DataRow("0x10")]
    [DataRow("0x1p4")]
    [DataRow("1e309")]
    [DataRow("-1e309")]
    [DataRow("1e-400")]
    [DataRow("2.4703282292062327e-324")]
    [DataRow("1,5")]
    [DataRow("1_000")]
    [DataRow("1e")]
    [DataRow(".")]
    [DataRow("e5")]
    [DataRow(" 1.5")]
    public void NonFiniteOrOutOfRangeText_IsRefused_AtEveryEntryPoint(string text)
    {
        Assert.ThrowsExactly<FormatException>(() => Float64Range.Parse($"[{text},)", null));
        Assert.IsFalse(Float64Range.TryParse($"[{text},)", null, out _));
        Assert.IsFalse(Float64Range.TryParse($"(,{text}]".AsSpan(), null, out _));
        Assert.IsFalse(Float64Range.TryParse($"[5,{text})", null, out _));
        Assert.ThrowsExactly<FormatException>(() => RangeSet<Float64Range, double>.Parse($"{{[{text},)}}", null));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float64Range>($"\"[{text},)\""));
        Assert.ThrowsExactly<FormatException>(() => TypeDescriptor.GetConverter(typeof(Float64Range)).ConvertFromInvariantString($"[{text},)"));

        Assert.ThrowsExactly<FormatException>(() => Float64Set.Parse($"{{\"{text}\"}}", null));
        Assert.IsFalse(Float64Set.TryParse($"{{1,\"{text}\"}}", null, out _));
        Assert.ThrowsExactly<FormatException>(() => Float64Set<TestScore>.Parse($"{{\"{text}\"}}", null));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float64Set>($"[{JsonSerializer.Serialize(text)}]", ReadingFromString));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float64Set<TestScore>>($"[{JsonSerializer.Serialize(text)}]", ReadingFromString));
    }

    /// <summary>JSON's own non-finite tokens, which System.Text.Json reads under <c>AllowNamedFloatingPointLiterals</c>.</summary>
    [TestMethod]
    [DataRow("\"NaN\"")]
    [DataRow("\"Infinity\"")]
    [DataRow("\"-Infinity\"")]
    public void NamedFloatingPointLiteral_IsRefused_InJson(string token)
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float64Set>($"[{token}]", ReadingFromString));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float64Set<TestScore>>($"[{token}]", ReadingFromString));
    }

    /// <summary>What <c>float8in</c> reads and so does this library: sign, digits, point, exponent, whitespace around.</summary>
    [TestMethod]
    [DataRow("1.5", 1.5)]
    [DataRow(" 1.5 ", 1.5)]
    [DataRow("\t+.5\n", 0.5)]
    [DataRow("5.", 5.0)]
    [DataRow("1E+2", 100.0)]
    [DataRow("1e-320", 1e-320)]
    [DataRow("2.4703282292062328e-324", double.Epsilon)]
    [DataRow("-0", -0.0)]
    [DataRow("1.7976931348623157e308", double.MaxValue)]
    public void FiniteText_Reads(string text, double expected)
    {
        Assert.AreEqual(Float64Range.CreateUnboundedEnd(expected), Float64Range.Parse($"[{text},)", null));
        Assert.AreEqual(Float64Set.From(expected), Float64Set.Parse($"{{\"{text}\"}}", null));
        Assert.AreEqual(expected, Float64Set<TestScore>.Parse($"{{\"{text}\"}}", null)[0].Value);
    }

    /// <summary>
    /// The text <c>float8out</c> prints, measured on PostgreSQL 16 and 18. .NET's shortest form differs in
    /// layout for most of these, in digits for <c>1e23</c>, and for 2^-25 and 2^-958 does not read back at all.
    /// </summary>
    [TestMethod]
    [DataRow(0.0, "0")]
    [DataRow(-0.0, "-0")]
    [DataRow(1.5, "1.5")]
    [DataRow(100.0, "100")]
    [DataRow(1e14, "100000000000000")]
    [DataRow(1e15, "1e+15")]
    [DataRow(123456789012345678.0, "1.2345678901234568e+17")]
    [DataRow(0.0001, "0.0001")]
    [DataRow(0.00001, "1e-05")]
    [DataRow(1e100, "1e+100")]
    [DataRow(1e23, "9.999999999999999e+22")]
    [DataRow(56125240608817456.0, "5.6125240608817456e+16")]
    [DataRow(2.9802322387695312e-08, "2.9802322387695312e-08")]
    [DataRow(4.1045368012983762e-289, "4.1045368012983762e-289")]
    [DataRow(double.Epsilon, "5e-324")]
    [DataRow(double.MaxValue, "1.7976931348623157e+308")]
    [DataRow(double.MinValue, "-1.7976931348623157e+308")]
    [DataRow(2.2250738585072014e-308, "2.2250738585072014e-308")]
    public void Format_IsThePostgresText_AndReadsBackAsTheValue(double value, string expected)
    {
        Assert.AreEqual($"[{expected},{expected}]", Float64Range.CreateFinite(value, value, true, true).ToString());
        Assert.AreEqual($"{{{expected}}}", Float64Set.From(value).ToString());
        Assert.AreEqual($"{{{expected}}}", Float64Set<TestScore>.From(new TestScore(value)).ToString());

        var back = Float64Set.Parse($"{{{expected}}}", null)[0];
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(back), $"{expected} read back as {back:R}");
    }

    /// <summary>
    /// System.Text.Json writes .NET's shortest form, which reads 2^-25 back one unit lower; the set writes
    /// the PostgreSQL text, so the value survives a JSON round trip. The text is a JSON number.
    /// </summary>
    [TestMethod]
    public void Json_RoundTripsTheTwoDoublesDotNetFormatsWrong()
    {
        var set  = Float64Set.From(2.9802322387695312e-08, 4.1045368012983762e-289, 1e23, -0.5);
        var json = JsonSerializer.Serialize(set);

        Assert.AreEqual("[-0.5,4.1045368012983762e-289,2.9802322387695312e-08,9.999999999999999e+22]", json);
        Assert.AreEqual(set, JsonSerializer.Deserialize<Float64Set>(json));
        Assert.AreEqual(json, JsonSerializer.Serialize(Float64Set<TestScore>.From(set.Values.Select(v => new TestScore(v)))));
        CollectionAssert.AreEqual(set.Values.ToArray(),
            JsonSerializer.Deserialize<Float64Set<TestScore>>(json)!.Values.Select(score => score.Value).ToArray());

        // System.Text.Json escapes the '+' inside a string, as it does for every string it writes.
        var quoted = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString };
        Assert.AreEqual("[\"-0.5\",\"9.999999999999999e\\u002B22\"]", JsonSerializer.Serialize(Float64Set.From(-0.5, 1e23), quoted));
        Assert.AreEqual(Float64Set.From(-0.5, 1e23), JsonSerializer.Deserialize<Float64Set>(JsonSerializer.Serialize(Float64Set.From(-0.5, 1e23), quoted), quoted));
    }

    /// <summary>The IEEE difference; past <see cref="double.MaxValue"/> it is infinity, an answer rather than a bound.</summary>
    [TestMethod]
    public void Length_IsTheIeeeDifference_AndMayBeInfinite()
    {
        Assert.AreEqual(4.0, Float64Range.CreateFinite(1, 5).Length);
        Assert.AreEqual(4.0, Float64Range.CreateFinite(1, 5, false, true).Length);
        Assert.AreEqual(0.0, Float64Range.Empty.Length);
        Assert.AreEqual(0.0, Float64Range.CreateFinite(3, 3, true, true).Length);
        Assert.IsNull(Float64Range.Infinite.Length);
        Assert.IsNull(Float64Range.CreateUnboundedEnd(0).Length);
        Assert.AreEqual(double.PositiveInfinity, Float64Range.CreateFinite(double.MinValue, double.MaxValue, true, true).Length);
    }

    /// <summary>
    /// Continuous and never canonicalized, as in PostgreSQL: no double lies strictly between 1 and the next
    /// double up, yet the open range between them is not empty.
    /// </summary>
    [TestMethod]
    public void OpenRangeBetweenNeighbours_IsNotEmpty()
    {
        var next  = Math.BitIncrement(1.0);
        var range = Float64Range.CreateFinite(1, next, false, false);

        Assert.IsInstanceOfType<Float64Range.Finite>(range);
        Assert.AreEqual("(1,1.0000000000000002)", range.ToString());
        Assert.IsFalse(range.Contains(1));
        Assert.IsFalse(range.Contains(next));
    }

    /// <summary><c>-0</c> equals <c>0</c> on both sides, in equality, hashing and order.</summary>
    [TestMethod]
    public void NegativeZero_EqualsZero()
    {
        Assert.AreEqual(Float64Range.CreateFinite(-0.0, 1), Float64Range.CreateFinite(0.0, 1));
        Assert.AreEqual(Float64Range.CreateFinite(-0.0, 1).GetHashCode(), Float64Range.CreateFinite(0.0, 1).GetHashCode());
        Assert.AreEqual(Float64Range.Empty, Float64Range.CreateFinite(0.0, -0.0));
        Assert.IsTrue(Float64Range.CreateFinite(-0.0, 0.0, true, true).Contains(0.0));
        Assert.AreEqual(Float64Set.From(0.0), Float64Set.From(-0.0));
        Assert.AreEqual(Float64Set.From(0.0).GetHashCode(), Float64Set.From(-0.0).GetHashCode());
        Assert.AreEqual(1, Float64Set.From(-0.0, 0.0).Count);
        Assert.AreEqual("{-0}", Float64Set.From(-0.0, 0.0).ToString());
    }

    /// <summary>A format string applies as to any number: it is display text, not the literal.</summary>
    [TestMethod]
    public void ExplicitFormat_AppliesToTheBounds()
    {
        IFormattable range = Float64Range.CreateFinite(1.5, 1e15);

        Assert.AreEqual("[1.500E+000,1.000E+015)", range.ToString("E3", CultureInfo.InvariantCulture));
        Assert.AreEqual("[1,50,1000000000000000,00)", range.ToString("F2", CultureInfo.GetCultureInfo("de-DE")));
        Assert.AreEqual("[1.5,1e+15)", range.ToString(null, CultureInfo.GetCultureInfo("de-DE")));
    }

    /// <summary>Shift, Expand and Split take finite operands, like every other float operand.</summary>
    [TestMethod]
    public void ShiftExpandSplit_RefuseANonFiniteStep()
    {
        var range = Float64Range.CreateFinite(0, 10);
        foreach (var value in NotFinite)
        {
            Assert.ThrowsExactly<ArgumentException>(() => range.Shift(value));
            Assert.ThrowsExactly<ArgumentException>(() => range.Expand(value));
            Assert.ThrowsExactly<ArgumentException>(() => range.Split(value));
            Assert.ThrowsExactly<ArgumentException>(() => Float64Range.Empty.Shift(value));
        }
    }

    /// <summary>
    /// IEEE addition overflows to an infinity rather than throwing; a bound moved past the finite values
    /// throws <see cref="OverflowException"/>, as for the other range types, and never becomes a bound.
    /// </summary>
    [TestMethod]
    public void ShiftAndExpand_PastTheFiniteValues_Throw()
    {
        var top = Float64Range.CreateFinite(1, double.MaxValue, true, true);

        Assert.ThrowsExactly<OverflowException>(() => top.Shift(double.MaxValue));
        Assert.ThrowsExactly<OverflowException>(() => Float64Range.CreateUnboundedStart(double.MaxValue).Expand(double.MaxValue));
        Assert.ThrowsExactly<OverflowException>(() => Float64Range.CreateFinite(double.MinValue, 0).Shift(-double.MaxValue));
        Assert.AreEqual(Float64Range.CreateFinite(0.30000000000000004, 1.2), Float64Range.CreateFinite(0.1, 1).Shift(0.2));
        Assert.AreEqual(Float64Range.Empty, Float64Range.CreateFinite(1, 2).Expand(-0.5));
    }

    /// <summary>
    /// Each boundary is Start + n × step, rounded once, so the error does not build up; a step too small to
    /// move the start at its magnitude throws at the call; a boundary past MaxValue ends the split.
    /// </summary>
    [TestMethod]
    public void Split_RoundsEachBoundaryOnce_AndStopsAtTheFiniteEdge()
    {
        var tenths = Float64Range.CreateFinite(0, 1).Split(0.1).ToList();
        Assert.HasCount(10, tenths);
        Assert.AreEqual(0.7000000000000001, tenths[7].LowerBound());   // 7 * 0.1, not 0.1 added seven times

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Float64Range.CreateFinite(1e17, 2e17).Split(1));

        var ulp  = Math.ScaleB(1.0, 971);
        var edge = Float64Range.CreateFinite(double.MaxValue - 3 * ulp, double.MaxValue, true, true).Split(2 * ulp).ToList();
        Assert.HasCount(2, edge);
        Assert.AreEqual(double.MaxValue, edge[^1].UpperBound());
    }

    /// <summary>PostgreSQL's range order over finite doubles; -0 and 0 compare equal, as they are.</summary>
    [TestMethod]
    public void CompareTo_IsTheRangeOrder()
    {
        Assert.IsLessThan(0, Float64Range.Empty.CompareTo(Float64Range.CreateFinite(-1e308, 0)));
        Assert.IsLessThan(0, Float64Range.CreateFinite(1, 5).CompareTo(Float64Range.CreateFinite(1, 5, true, true)));
        Assert.IsLessThan(0, Float64Range.CreateUnboundedStart(0).CompareTo(Float64Range.CreateFinite(double.MinValue, 0)));
        Assert.AreEqual(0, Float64Range.CreateFinite(-0.0, 1).CompareTo(Float64Range.CreateFinite(0.0, 1)));
        Assert.IsGreaterThan(0, Float64Range.CreateFinite(0, 1).CompareTo(null));
    }
}

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// <see cref="Float32Range"/> and <see cref="Float32Set"/> hold finite doubles only, and write them as
/// PostgreSQL's <c>float4out</c> does (#153). PostgreSQL sorts <c>NaN</c> above every number and .NET
/// below, so the guard is on every construction path and every scalar operand: a <c>NaN</c> that got in
/// anywhere would answer differently on each side, silently. The texts are the ones PostgreSQL 16 and 18
/// printed for the same doubles.
/// </summary>
[TestClass]
public sealed class Float32DomainTests
{
    private static readonly float[] NotFinite = [float.NaN, float.PositiveInfinity, float.NegativeInfinity];

    private static readonly JsonSerializerOptions ReadingFromString =
        new() { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [TestMethod]
    public void EveryFactory_RefusesNaNAndTheInfinities()
    {
        foreach (var value in NotFinite)
        {
            Assert.ThrowsExactly<ArgumentException>(() => Float32Range.CreateFinite(value, 5));
            Assert.ThrowsExactly<ArgumentException>(() => Float32Range.CreateFinite(5, value));
            Assert.ThrowsExactly<ArgumentException>(() => Float32Range.CreateFinite(value, value, true, true));
            Assert.ThrowsExactly<ArgumentException>(() => Float32Range.CreateUnboundedStart(value));
            Assert.ThrowsExactly<ArgumentException>(() => Float32Range.CreateUnboundedEnd(value));
            Assert.ThrowsExactly<ArgumentException>(() => new Float32Range.UnboundedStart(value, true));
            Assert.ThrowsExactly<ArgumentException>(() => new Float32Range.UnboundedEnd(value, false));

            Assert.ThrowsExactly<ArgumentException>(() => Float32Set.From(1, value));
            Assert.ThrowsExactly<ArgumentException>(() => Float32Set.From(new List<float> { value }));
            Assert.ThrowsExactly<ArgumentException>(() => Float32Set<TestWeight>.From(new TestWeight(value)));
            Assert.ThrowsExactly<ArgumentException>(() => Float32Set<TestWeight>.From(new List<TestWeight> { new(1), new(value) }));
        }
    }

    /// <summary>
    /// <c>[5,NaN)</c> is the row that shows why: under <c>CompareTo</c> it is inverted and would be the empty
    /// range, while PostgreSQL holds it as a range containing <c>1e308</c>.
    /// </summary>
    [TestMethod]
    public void FivetoNaN_IsRefused_NotEmpty()
    {
        var error = Assert.ThrowsExactly<ArgumentException>(() => Float32Range.CreateFinite(5, float.NaN));
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
        Float32Range[] ranges =
        [
            Float32Range.Empty, Float32Range.Infinite, Float32Range.CreateFinite(0, 5),
            Float32Range.CreateUnboundedEnd(0), Float32Range.CreateUnboundedStart(0)
        ];
        RangeSet<Float32Range, float>[] sets =
        [
            RangeSet<Float32Range, float>.Empty, RangeSet<Float32Range, float>.Infinite,
            RangeSet<Float32Range, float>.From([Float32Range.CreateFinite(0, 1), Float32Range.CreateFinite(2, 3)])
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

            foreach (var set in new[] { Float32Set.Empty, Float32Set.From(1, 2) })
            {
                Assert.ThrowsExactly<ArgumentException>(() => set.Contains(value), $"{set}.Contains({value})");
                Assert.ThrowsExactly<ArgumentException>(() => set.Add(value), $"{set}.Add({value})");
                Assert.ThrowsExactly<ArgumentException>(() => set.Remove(value), $"{set}.Remove({value})");
            }

            foreach (var set in new[] { Float32Set<TestWeight>.Empty, Float32Set<TestWeight>.From(new TestWeight(1)) })
            {
                Assert.ThrowsExactly<ArgumentException>(() => set.Contains(new TestWeight(value)));
                Assert.ThrowsExactly<ArgumentException>(() => set.Add(new TestWeight(value)));
                Assert.ThrowsExactly<ArgumentException>(() => set.Remove(new TestWeight(value)));
            }
        }
    }

    /// <summary>The spellings <c>float4in</c> takes that this library refuses, and numbers it reports out of range.</summary>
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
    [DataRow("1e39")]
    [DataRow("-1e39")]
    [DataRow("3.4028236e38")]
    [DataRow("1e-46")]
    [DataRow("1,5")]
    [DataRow("1_000")]
    [DataRow("1e")]
    [DataRow(".")]
    [DataRow("e5")]
    [DataRow(" 1.5")]
    public void NonFiniteOrOutOfRangeText_IsRefused_AtEveryEntryPoint(string text)
    {
        Assert.ThrowsExactly<FormatException>(() => Float32Range.Parse($"[{text},)", null));
        Assert.IsFalse(Float32Range.TryParse($"[{text},)", null, out _));
        Assert.IsFalse(Float32Range.TryParse($"(,{text}]".AsSpan(), null, out _));
        Assert.IsFalse(Float32Range.TryParse($"[5,{text})", null, out _));
        Assert.ThrowsExactly<FormatException>(() => RangeSet<Float32Range, float>.Parse($"{{[{text},)}}", null));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float32Range>($"\"[{text},)\""));
        Assert.ThrowsExactly<FormatException>(() => TypeDescriptor.GetConverter(typeof(Float32Range)).ConvertFromInvariantString($"[{text},)"));

        Assert.ThrowsExactly<FormatException>(() => Float32Set.Parse($"{{\"{text}\"}}", null));
        Assert.IsFalse(Float32Set.TryParse($"{{1,\"{text}\"}}", null, out _));
        Assert.ThrowsExactly<FormatException>(() => Float32Set<TestWeight>.Parse($"{{\"{text}\"}}", null));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float32Set>($"[{JsonSerializer.Serialize(text)}]", ReadingFromString));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float32Set<TestWeight>>($"[{JsonSerializer.Serialize(text)}]", ReadingFromString));
    }

    /// <summary>JSON's own non-finite tokens, which System.Text.Json reads under <c>AllowNamedFloatingPointLiterals</c>.</summary>
    [TestMethod]
    [DataRow("\"NaN\"")]
    [DataRow("\"Infinity\"")]
    [DataRow("\"-Infinity\"")]
    public void NamedFloatingPointLiteral_IsRefused_InJson(string token)
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float32Set>($"[{token}]", ReadingFromString));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Float32Set<TestWeight>>($"[{token}]", ReadingFromString));
    }

    /// <summary>What <c>float4in</c> reads and so does this library: sign, digits, point, exponent, whitespace around.</summary>
    [TestMethod]
    [DataRow("1.5", 1.5f)]
    [DataRow(" 1.5 ", 1.5f)]
    [DataRow("\t+.5\n", 0.5f)]
    [DataRow("5.", 5.0f)]
    [DataRow("1E+2", 100.0f)]
    [DataRow("1e-40", 1e-40f)]
    [DataRow("1e-45", float.Epsilon)]
    [DataRow("-0", -0.0f)]
    [DataRow("3.4028235e38", float.MaxValue)]
    public void FiniteText_Reads(string text, float expected)
    {
        Assert.AreEqual(Float32Range.CreateUnboundedEnd(expected), Float32Range.Parse($"[{text},)", null));
        Assert.AreEqual(Float32Set.From(expected), Float32Set.Parse($"{{\"{text}\"}}", null));
        Assert.AreEqual(expected, Float32Set<TestWeight>.Parse($"{{\"{text}\"}}", null)[0].Value);
    }

    /// <summary>
    /// The text <c>float4out</c> prints, measured on PostgreSQL 16 and 18. .NET's shortest form differs in
    /// layout from <c>1e+06</c> up and in digits for <c>-90185504</c>; a float formatted through
    /// <see cref="double"/> is <c>0.10000000149011612f</c> for <c>0.1f</c>.
    /// </summary>
    [TestMethod]
    [DataRow(0f, "0")]
    [DataRow(-0f, "-0")]
    [DataRow(1.5f, "1.5")]
    [DataRow(0.1f, "0.1")]
    [DataRow(0.3f, "0.3")]
    [DataRow(123456.7f, "123456.7")]
    [DataRow(1e6f, "1e+06")]
    [DataRow(1e7f, "1e+07")]
    [DataRow(16777216f, "1.6777216e+07")]
    [DataRow(-90185504f, "-9.0185504e+07")]
    [DataRow(0.0001f, "0.0001")]
    [DataRow(0.00001f, "1e-05")]
    [DataRow(3.14159265f, "3.1415927")]
    [DataRow(float.Epsilon, "1e-45")]
    [DataRow(1.17549435e-38f, "1.1754944e-38")]
    [DataRow(float.MaxValue, "3.4028235e+38")]
    [DataRow(float.MinValue, "-3.4028235e+38")]
    public void Format_IsThePostgresText_AndReadsBackAsTheValue(float value, string expected)
    {
        Assert.AreEqual($"[{expected},{expected}]", Float32Range.CreateFinite(value, value, true, true).ToString());
        Assert.AreEqual($"{{{expected}}}", Float32Set.From(value).ToString());
        Assert.AreEqual($"{{{expected}}}", Float32Set<TestWeight>.From(new TestWeight(value)).ToString());

        var back = Float32Set.Parse($"{{{expected}}}", null)[0];
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(back), $"{expected} read back as {back:R}");
    }

    /// <summary>
    /// The set writes the PostgreSQL text as a JSON number — <c>1e+06</c> and <c>-9.0185504e+07</c>, where
    /// System.Text.Json would write <c>1000000</c> and <c>-90185500</c> — and reads it back as the same floats.
    /// </summary>
    [TestMethod]
    public void Json_WritesThePostgresText_AndRoundTrips()
    {
        var set  = Float32Set.From(0.1f, -90185504f, 1e6f, -0.5f);
        var json = JsonSerializer.Serialize(set);

        Assert.AreEqual("[-9.0185504e+07,-0.5,0.1,1e+06]", json);
        Assert.AreEqual(set, JsonSerializer.Deserialize<Float32Set>(json));
        Assert.AreEqual(json, JsonSerializer.Serialize(Float32Set<TestWeight>.From(set.Values.Select(v => new TestWeight(v)))));
        CollectionAssert.AreEqual(set.Values.ToArray(),
            JsonSerializer.Deserialize<Float32Set<TestWeight>>(json)!.Values.Select(score => score.Value).ToArray());

        // System.Text.Json escapes the '+' inside a string, as it does for every string it writes.
        var quoted = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString };
        Assert.AreEqual("[\"-0.5\",\"1e\\u002B06\"]", JsonSerializer.Serialize(Float32Set.From(-0.5f, 1e6f), quoted));
        Assert.AreEqual(Float32Set.From(-0.5f, 1e6f), JsonSerializer.Deserialize<Float32Set>(JsonSerializer.Serialize(Float32Set.From(-0.5f, 1e6f), quoted), quoted));
    }

    /// <summary>The IEEE difference; past <see cref="float.MaxValue"/> it is infinity, an answer rather than a bound.</summary>
    [TestMethod]
    public void Length_IsTheIeeeDifference_AndMayBeInfinite()
    {
        Assert.AreEqual(4f, Float32Range.CreateFinite(1, 5).Length);
        Assert.AreEqual(4f, Float32Range.CreateFinite(1, 5, false, true).Length);
        Assert.AreEqual((float?)0f, Float32Range.Empty.Length);
        Assert.AreEqual((float?)0f, Float32Range.CreateFinite(3, 3, true, true).Length);
        Assert.IsNull(Float32Range.Infinite.Length);
        Assert.IsNull(Float32Range.CreateUnboundedEnd(0).Length);
        Assert.AreEqual((float?)float.PositiveInfinity, Float32Range.CreateFinite(float.MinValue, float.MaxValue, true, true).Length);
    }

    /// <summary>
    /// Continuous and never canonicalized, as in PostgreSQL: no float lies strictly between 1 and the next
    /// float up, yet the open range between them is not empty.
    /// </summary>
    [TestMethod]
    public void OpenRangeBetweenNeighbours_IsNotEmpty()
    {
        var next  = MathF.BitIncrement(1f);
        var range = Float32Range.CreateFinite(1, next, false, false);

        Assert.IsInstanceOfType<Float32Range.Finite>(range);
        Assert.AreEqual("(1,1.0000001)", range.ToString());
        Assert.IsFalse(range.Contains(1));
        Assert.IsFalse(range.Contains(next));
    }

    /// <summary><c>-0</c> equals <c>0</c> on both sides, in equality, hashing and order.</summary>
    [TestMethod]
    public void NegativeZero_EqualsZero()
    {
        Assert.AreEqual(Float32Range.CreateFinite(-0.0f, 1), Float32Range.CreateFinite(0.0f, 1));
        Assert.AreEqual(Float32Range.CreateFinite(-0.0f, 1).GetHashCode(), Float32Range.CreateFinite(0.0f, 1).GetHashCode());
        Assert.AreEqual(Float32Range.Empty, Float32Range.CreateFinite(0.0f, -0.0f));
        Assert.IsTrue(Float32Range.CreateFinite(-0.0f, 0.0f, true, true).Contains(0.0f));
        Assert.AreEqual(Float32Set.From(0.0f), Float32Set.From(-0.0f));
        Assert.AreEqual(Float32Set.From(0.0f).GetHashCode(), Float32Set.From(-0.0f).GetHashCode());
        Assert.AreEqual(1, Float32Set.From(-0.0f, 0.0f).Count);
        Assert.AreEqual("{-0}", Float32Set.From(-0.0f, 0.0f).ToString());
    }

    /// <summary>A format string applies as to any number: it is display text, not the literal.</summary>
    [TestMethod]
    public void ExplicitFormat_AppliesToTheBounds()
    {
        IFormattable range = Float32Range.CreateFinite(1.5f, 1e7f);

        Assert.AreEqual("[1.500E+000,1.000E+007)", range.ToString("E3", CultureInfo.InvariantCulture));
        Assert.AreEqual("[1,50,10000000,00)", range.ToString("F2", CultureInfo.GetCultureInfo("de-DE")));
        Assert.AreEqual("[1.5,1e+07)", range.ToString(null, CultureInfo.GetCultureInfo("de-DE")));
    }

    /// <summary>Shift, Expand and Split take finite operands, like every other float operand.</summary>
    [TestMethod]
    public void ShiftExpandSplit_RefuseANonFiniteStep()
    {
        var range = Float32Range.CreateFinite(0f, 10f);
        foreach (var value in NotFinite)
        {
            Assert.ThrowsExactly<ArgumentException>(() => range.Shift(value));
            Assert.ThrowsExactly<ArgumentException>(() => range.Expand(value));
            Assert.ThrowsExactly<ArgumentException>(() => range.Split(value));
        }
    }

    /// <summary>A bound moved past float.MaxValue throws, never becomes an infinity; each moved bound is one float addition.</summary>
    [TestMethod]
    public void ShiftAndExpand_PastTheFiniteValues_Throw()
    {
        Assert.ThrowsExactly<OverflowException>(() => Float32Range.CreateFinite(1f, float.MaxValue, true, true).Shift(float.MaxValue));
        Assert.ThrowsExactly<OverflowException>(() => Float32Range.CreateFinite(float.MinValue, 0f).Expand(float.MaxValue));
        Assert.AreEqual(Float32Range.CreateFinite(0.1f + 0.2f, 1.2f), Float32Range.CreateFinite(0.1f, 1f).Shift(0.2f));
    }

    /// <summary>Each boundary is Start + n × step in float, rounded once; a step that cannot move the start throws.</summary>
    [TestMethod]
    public void Split_RoundsEachBoundaryOnce_AndStopsAtTheFiniteEdge()
    {
        var tenths = Float32Range.CreateFinite(0f, 1f).Split(0.1f).ToList();
        Assert.HasCount(10, tenths);
        Assert.AreEqual(7 * 0.1f, tenths[7].LowerBound());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Float32Range.CreateFinite(1e9f, 2e9f).Split(1f));

        var ulp  = MathF.ScaleB(1f, 104);
        var edge = Float32Range.CreateFinite(float.MaxValue - 3 * ulp, float.MaxValue, true, true).Split(2 * ulp).ToList();
        Assert.HasCount(2, edge);
        Assert.AreEqual(float.MaxValue, edge[^1].UpperBound());
    }

    /// <summary>PostgreSQL's range order over finite floats; -0 and 0 compare equal.</summary>
    [TestMethod]
    public void CompareTo_IsTheRangeOrder()
    {
        Assert.IsLessThan(0, Float32Range.Empty.CompareTo(Float32Range.CreateFinite(-1e38f, 0f)));
        Assert.IsLessThan(0, Float32Range.CreateFinite(1f, 5f).CompareTo(Float32Range.CreateFinite(1f, 5f, true, true)));
        Assert.AreEqual(0, Float32Range.CreateFinite(-0f, 1f).CompareTo(Float32Range.CreateFinite(0f, 1f)));
        Assert.IsGreaterThan(0, Float32Range.CreateFinite(0f, 1f).CompareTo(null));
    }
}

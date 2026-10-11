using System.Text.Json;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Ranges and multiranges as dictionary keys. Before 9.0.0 the converters did not override
/// <c>ReadAsPropertyName</c>/<c>WriteAsPropertyName</c>, so <c>Dictionary&lt;DateRange, int&gt;</c>
/// threw <see cref="NotSupportedException"/> in both directions, registered or not. The key is the
/// same literal as the value form. The sweep over every range type and shape lives in the
/// conventions project (<c>JsonDefaultContractTests</c>).
/// </summary>
[TestClass]
public class JsonDictionaryKeyTests
{
    private static readonly DateRange H1 = DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
    private static readonly DateRange H2 = DateRange.CreateFinite(new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31));

    [TestMethod]
    public void RangeKeys_WriteTheLiteral_AndReadBack()
    {
        var byHalf = new Dictionary<DateRange, int> { [H1] = 181, [H2] = 184 };

        var json = JsonSerializer.Serialize(byHalf);

        Assert.AreEqual("""{"[2026-01-01,2026-06-30]":181,"[2026-07-01,2026-12-31]":184}""", json);
        CollectionAssert.AreEquivalent(byHalf, JsonSerializer.Deserialize<Dictionary<DateRange, int>>(json));
    }

    [TestMethod]
    public void RangeKeys_RoundTripUnderAddRangeConverters()
    {
        var options = new JsonSerializerOptions().AddRangeConverters();
        var byHalf  = new Dictionary<DateRange, int> { [H1] = 181, [DateRange.Empty] = 0, [DateRange.Infinite] = -1 };

        var json = JsonSerializer.Serialize(byHalf, options);

        Assert.AreEqual("""{"[2026-01-01,2026-06-30]":181,"empty":0,"(,)":-1}""", json);
        CollectionAssert.AreEquivalent(byHalf, JsonSerializer.Deserialize<Dictionary<DateRange, int>>(json, options));
    }

    [TestMethod]
    public void VariantKeys_RoundTrip()
    {
        var finite = new Dictionary<DateRange.Finite, string> { [(DateRange.Finite)H1] = "h1" };

        var json = JsonSerializer.Serialize(finite);

        Assert.AreEqual("""{"[2026-01-01,2026-06-30]":"h1"}""", json);
        CollectionAssert.AreEquivalent(finite, JsonSerializer.Deserialize<Dictionary<DateRange.Finite, string>>(json));
    }

    [TestMethod]
    public void RangeSetKeys_RoundTrip()
    {
        var year = RangeSet<DateRange, DateOnly>.From([H1, H2]);
        var keyed = new Dictionary<RangeSet<DateRange, DateOnly>, int> { [year] = 365 };

        var json = JsonSerializer.Serialize(keyed);

        // H1 and H2 are adjacent, so the canonical multirange is one range.
        Assert.AreEqual("""{"{[2026-01-01,2026-12-31]}":365}""", json);
        CollectionAssert.AreEquivalent(keyed, JsonSerializer.Deserialize<Dictionary<RangeSet<DateRange, DateOnly>, int>>(json));
    }

    /// <summary>
    /// A key reads through the same canonicalizing parse as a value: <c>[1,10)</c> is <c>[1,9]</c>.
    /// </summary>
    [TestMethod]
    public void RangeKey_IsCanonicalizedOnRead()
    {
        var read = JsonSerializer.Deserialize<Dictionary<Int32Range, int>>("""{"[1,10)":1}""")!;

        Assert.AreEqual(Int32Range.CreateFinite(1, 9), read.Keys.Single());
    }

    /// <summary>
    /// A naming policy would rewrite the literal into text that no longer parses, so it is not
    /// applied — System.Text.Json applies <c>DictionaryKeyPolicy</c> only to string keys, and the
    /// converter does not opt in.
    /// </summary>
    [TestMethod]
    public void DictionaryKeyPolicy_IsNotAppliedToARangeKey()
    {
        var options = new JsonSerializerOptions { DictionaryKeyPolicy = new PrefixPolicy() };
        var keyed   = new Dictionary<DateRange, int> { [H1] = 1 };

        var json = JsonSerializer.Serialize(keyed, options);

        Assert.AreEqual("""{"[2026-01-01,2026-06-30]":1}""", json);
        CollectionAssert.AreEquivalent(keyed, JsonSerializer.Deserialize<Dictionary<DateRange, int>>(json, options));
    }

    [TestMethod]
    public void MalformedKey_IsAJsonException()
    {
        var ex = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<DateRange, int>>("""{"[2026-13-01,2026-14-01]":1}"""));

        StringAssert.Contains(ex.Message, "DateRange");
    }

    [TestMethod]
    public void MalformedRangeSetKey_IsAJsonException()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<RangeSet<Int32Range, int>, int>>("""{"{[1,":1}"""));

    [TestMethod]
    public void KeyOfTheWrongShape_IsRefusedForAVariantKey()
    {
        var ex = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<DateRange.Finite, int>>("""{"empty":1}"""));

        StringAssert.Contains(ex.Message, "EmptyRange");
    }

    private sealed class PrefixPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) => "policy_" + name;
    }
}

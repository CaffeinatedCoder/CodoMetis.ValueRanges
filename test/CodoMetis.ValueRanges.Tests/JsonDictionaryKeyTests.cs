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
    /// Canonicalization makes two different property names one key, and System.Text.Json treats
    /// that as any duplicate key: by default the later value silently replaces the earlier one.
    /// Pinned, not chosen — the converter only parses the name; what happens to a duplicate is the
    /// dictionary converter's rule, the same as for <c>{"k":1,"k":2}</c>.
    /// </summary>
    [TestMethod]
    public void TwoSpellingsOfOneKey_Collide_AndTheLaterValueWins()
    {
        var read = JsonSerializer.Deserialize<Dictionary<Int32Range, string>>("""{"[1,5]":"first","[1,6)":"second"}""")!;

        Assert.AreEqual(1, read.Count);
        Assert.AreEqual("second", read[Int32Range.CreateFinite(1, 5)]);
    }

    /// <summary>
    /// <c>AllowDuplicateProperties = false</c> (.NET 10) refuses the collision, because the
    /// duplicate check runs on the parsed key rather than on the property name's text — which is
    /// what makes it the setting to use for configuration, permission or other keyed data where a
    /// silently replaced entry matters.
    /// </summary>
    [TestMethod]
    public void TwoSpellingsOfOneKey_AreRefused_WhenDuplicatePropertiesAreDisallowed()
    {
        var options = new JsonSerializerOptions { AllowDuplicateProperties = false };

        Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<DateRange, string>>(
                """{"[2026-01-01,2026-01-31]":"a","[2026-01-01,2026-02-01)":"b"}""", options));
        Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<Dictionary<RangeSet<Int32Range, int>, string>>(
                """{"{[1,5]}":"a","{[1,3],[4,5]}":"b"}""", options));
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

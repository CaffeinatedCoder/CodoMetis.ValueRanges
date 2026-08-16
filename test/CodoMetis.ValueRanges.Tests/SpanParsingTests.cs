using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// <see cref="ISpanParsable{TSelf}"/> across the three parsable families — ranges, range sets
/// and value sets. The literal grammars were already parsed over spans internally; these tests
/// pin the public entry points to that, and pin the interface itself, which is what generic
/// code constrains on.
/// </summary>
[TestClass]
public class SpanParsingTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Reaches the type only through the interface — no concrete overload in sight.</summary>
    private static T ParseVia<T>(ReadOnlySpan<char> s) where T : ISpanParsable<T>
        => T.Parse(s, Invariant);

    /// <summary>The <c>TryParse</c> counterpart of <see cref="ParseVia{T}"/>.</summary>
    private static bool TryParseVia<T>(ReadOnlySpan<char> s, [MaybeNullWhen(false)] out T result)
        where T : ISpanParsable<T>
        => T.TryParse(s, Invariant, out result);

    // -------------------------------------------------------------------------
    // Slices — the reason the overload exists
    // -------------------------------------------------------------------------

    /// <summary>
    /// A slice of a larger buffer is the case the string overload cannot serve without
    /// allocating a substring first.
    /// </summary>
    [TestMethod]
    public void Range_ParsesFromASliceWithoutTheSurroundingText()
    {
        const string buffer = "period=[1,10];rest";
        var          slice  = buffer.AsSpan(7, 6);

        Assert.AreEqual("[1,10]", slice.ToString(), "slice arithmetic");
        Assert.AreEqual(Int32Range.CreateFinite(1, 10), Int32Range.Parse(slice, Invariant));
    }

    [TestMethod]
    public void ValueSet_ParsesFromASliceWithoutTheSurroundingText()
    {
        const string buffer = "tags={a,b};rest";
        var          slice  = buffer.AsSpan(5, 5);

        Assert.AreEqual("{a,b}", slice.ToString(), "slice arithmetic");
        Assert.AreEqual(StringSet.From("a", "b"), StringSet.Parse(slice, Invariant));
    }

    [TestMethod]
    public void RangeSet_ParsesFromASliceWithoutTheSurroundingText()
    {
        const string buffer = "blocked={[1,3],[10,12]};rest";
        var          slice  = buffer.AsSpan(8, 15);

        Assert.AreEqual("{[1,3],[10,12]}", slice.ToString(), "slice arithmetic");
        Assert.AreEqual(
            IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(10, 12)]),
            IntSet.Parse(slice, Invariant));
    }

    // -------------------------------------------------------------------------
    // Agreement with the string overload
    // -------------------------------------------------------------------------

    [TestMethod]
    [DataRow("[1,10]")]
    [DataRow("empty")]
    [DataRow("(,)")]
    [DataRow("(,5]")]
    [DataRow("[5,)")]
    public void Range_SpanAndStringOverloads_Agree(string literal)
        => Assert.AreEqual(Int32Range.Parse(literal, Invariant), Int32Range.Parse(literal.AsSpan(), Invariant));

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{a,b}")]
    [DataRow("{\"a b\",c}")]
    public void ValueSet_SpanAndStringOverloads_Agree(string literal)
        => Assert.AreEqual(StringSet.Parse(literal, Invariant), StringSet.Parse(literal.AsSpan(), Invariant));

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{[1,3]}")]
    [DataRow("{[1,3],[10,12]}")]
    public void RangeSet_SpanAndStringOverloads_Agree(string literal)
        => Assert.AreEqual(IntSet.Parse(literal, Invariant), IntSet.Parse(literal.AsSpan(), Invariant));

    // -------------------------------------------------------------------------
    // TryParse
    // -------------------------------------------------------------------------

    [TestMethod]
    public void TryParse_Span_RejectsAMalformedLiteral()
    {
        Assert.IsFalse(Int32Range.TryParse("[1,".AsSpan(), Invariant, out var range));
        Assert.AreEqual(Int32Range.Empty, range);

        Assert.IsFalse(StringSet.TryParse("{a,b".AsSpan(), Invariant, out var set));
        Assert.AreEqual(StringSet.Empty, set);
    }

    [TestMethod]
    public void TryParse_Span_AcceptsAWellFormedLiteral()
    {
        Assert.IsTrue(Int32Range.TryParse("[1,10]".AsSpan(), Invariant, out var range));
        Assert.AreEqual(Int32Range.CreateFinite(1, 10), range);

        Assert.IsTrue(StringSet.TryParse("{a,b}".AsSpan(), Invariant, out var set));
        Assert.AreEqual(StringSet.From("a", "b"), set);
    }

    // -------------------------------------------------------------------------
    // Through the interface, for every family
    // -------------------------------------------------------------------------

    [TestMethod]
    public void EveryRangeType_IsReachableThroughISpanParsable()
    {
        Assert.AreEqual(Int32Range.CreateFinite(1, 10), ParseVia<Int32Range>("[1,10]"));
        Assert.AreEqual(Int64Range.CreateFinite(1L, 10L), ParseVia<Int64Range>("[1,10]"));
        // The continuous types default endInclusive to false, so a ']' literal has to say so.
        Assert.AreEqual(
            DecimalRange.CreateFinite(1.5m, 2.5m, true, true),
            ParseVia<DecimalRange>("[1.5,2.5]"));
        Assert.AreEqual(
            DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
            ParseVia<DateRange>("[2024-01-01,2024-12-31]"));
        Assert.AreEqual(
            TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0), true, true),
            ParseVia<TimeRange>("[09:00:00,17:00:00]"));
    }

    [TestMethod]
    public void EverySetFamily_IsReachableThroughISpanParsable()
    {
        Assert.AreEqual(Int32Set.From(1, 2), ParseVia<Int32Set>("{1,2}"));
        Assert.AreEqual(StringSet.From("a", "b"), ParseVia<StringSet>("{a,b}"));
        Assert.AreEqual(DecimalSet.From(1.5m, 2.25m), ParseVia<DecimalSet>("{1.5,2.25}"));
        Assert.AreEqual(
            DateSet.From(new DateOnly(2024, 1, 1)),
            ParseVia<DateSet>("{2024-01-01}"));
    }

    [TestMethod]
    public void RangeSet_IsReachableThroughISpanParsable()
        => Assert.AreEqual(
            IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(10, 12)]),
            ParseVia<IntSet>("{[1,3],[10,12]}"));

    [TestMethod]
    public void TryParse_ThroughISpanParsable_ReportsFailure()
    {
        Assert.IsFalse(TryParseVia<Int32Range>("[1,", out _));
        Assert.IsFalse(TryParseVia<StringSet>("{a,b", out _));

        Assert.IsTrue(TryParseVia<Int32Range>("[1,10]", out var range));
        Assert.AreEqual(Int32Range.CreateFinite(1, 10), range);
    }

    /// <summary>
    /// <see cref="ISpanParsable{TSelf}"/> extends <see cref="IParsable{TSelf}"/>, so widening
    /// the constraint must not cost the string entry point.
    /// </summary>
    [TestMethod]
    public void IParsable_StillSatisfied()
    {
        static T ParseString<T>(string s) where T : IParsable<T> => T.Parse(s, Invariant);

        Assert.AreEqual(Int32Range.CreateFinite(1, 10), ParseString<Int32Range>("[1,10]"));
        Assert.AreEqual(StringSet.From("a"), ParseString<StringSet>("{a}"));
        Assert.AreEqual(IntSet.From([Int32Range.CreateFinite(1, 3)]), ParseString<IntSet>("{[1,3]}"));
    }
}

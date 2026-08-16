using IntRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;
using DateRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DateRange, System.DateOnly>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Converting between the two shapes a discrete domain can take. <c>{1,2,3,7}</c> and
/// <c>{[1,3],[7,7]}</c> describe the same membership, and the conversion must preserve exactly
/// that — every value on one side is contained on the other, and nothing else is.
/// </summary>
[TestClass]
public class SetRangeBridgeTests
{
    // -------------------------------------------------------------------------
    // Set → RangeSet
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ToRangeSet_CollapsesConsecutiveRuns()
    {
        var result = Int32Set.From(1, 2, 3, 7).ToRangeSet();

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 3), result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(7, 7), result[1]);
    }

    [TestMethod]
    public void ToRangeSet_UnsortedInput_StillCollapses()
    {
        // The set canonicalizes on construction, so the runs are visible regardless of input order.
        var result = Int32Set.From(7, 3, 1, 2).ToRangeSet();

        Assert.AreEqual(IntRangeSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 7)]), result);
    }

    [TestMethod]
    public void ToRangeSet_AllConsecutive_IsOneRange()
    {
        var result = Int32Set.From(1, 2, 3, 4, 5).ToRangeSet();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5), result[0]);
    }

    /// <summary>A gap of one value is still a gap — the runs must not merge across it.</summary>
    [TestMethod]
    public void ToRangeSet_SingleValueGap_SplitsTheRuns()
    {
        var result = Int32Set.From(1, 2, 4, 5).ToRangeSet();

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 2), result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(4, 5), result[1]);
        Assert.IsFalse(result.Contains(3));
    }

    [TestMethod]
    public void ToRangeSet_NoneConsecutive_IsOneRangePerValue()
    {
        var result = Int32Set.From(1, 5, 9).ToRangeSet();

        Assert.AreEqual(3, result.Count);
        foreach (var range in result) Assert.AreEqual(1L, range.Length);
    }

    [TestMethod]
    public void ToRangeSet_Empty_IsTheEmptyRangeSet()
        => Assert.AreSame(IntRangeSet.Empty, Int32Set.Empty.ToRangeSet());

    [TestMethod]
    public void ToRangeSet_Dates_CollapsesALongWeekend()
    {
        var weekend = DateSet.From(
            new DateOnly(2024, 5, 3), new DateOnly(2024, 5, 4),
            new DateOnly(2024, 5, 5), new DateOnly(2024, 5, 6));

        var result = weekend.ToRangeSet();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(4, result[0].Length);
    }

    // -------------------------------------------------------------------------
    // RangeSet → Set
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ToInt32Set_ExpandsEveryRange()
    {
        var ranges = IntRangeSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 7)]);

        Assert.AreEqual(Int32Set.From(1, 2, 3, 7), ranges.ToInt32Set());
    }

    [TestMethod]
    public void ToDateSet_ExpandsInclusiveOfBothBounds()
    {
        var ranges = DateRangeSet.From([
            DateRange.CreateFinite(new DateOnly(2024, 2, 28), new DateOnly(2024, 3, 1))
        ]);

        Assert.AreEqual(
            DateSet.From(new DateOnly(2024, 2, 28), new DateOnly(2024, 2, 29), new DateOnly(2024, 3, 1)),
            ranges.ToDateSet());
    }

    [TestMethod]
    public void ToInt32Set_Empty_IsTheEmptySet()
        => Assert.AreEqual(Int32Set.Empty, IntRangeSet.Empty.ToInt32Set());

    [TestMethod]
    public void ToInt32Set_Unbounded_Throws()
    {
        Assert.ThrowsExactly<NotSupportedException>(
            () => IntRangeSet.From([Int32Range.CreateUnboundedEnd(1)]).ToInt32Set());

        Assert.ThrowsExactly<NotSupportedException>(() => IntRangeSet.Infinite.ToInt32Set());
    }

    /// <summary>
    /// A set whose first elements are finite and last is unbounded must be refused before any
    /// value is produced, not partway through.
    /// </summary>
    [TestMethod]
    public void ToInt32Set_MixedBoundedAndUnbounded_ThrowsWithoutPartialResult()
    {
        var mixed = IntRangeSet.From([
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateUnboundedEnd(100)
        ]);

        Assert.ThrowsExactly<NotSupportedException>(() => mixed.ToInt32Set());
    }

    // -------------------------------------------------------------------------
    // Round trips
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RoundTrip_SetToRangesAndBack_IsTheOriginal()
    {
        foreach (var original in new[]
                 {
                     Int32Set.From(1, 2, 3, 7),
                     Int32Set.From(1, 5, 9),
                     Int32Set.From(42),
                     Int32Set.Empty,
                     Int32Set.From(-3, -2, -1, 0, 1)
                 })
            Assert.AreEqual(original, original.ToRangeSet().ToInt32Set(), $"round trip of '{original}'");
    }

    [TestMethod]
    public void RoundTrip_RangesToSetAndBack_IsTheOriginal()
    {
        var ranges = IntRangeSet.From([
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(10, 12)
        ]);

        Assert.AreEqual(ranges, ranges.ToInt32Set().ToRangeSet());
    }

    /// <summary>
    /// The conversion preserves membership, which is the only thing it is allowed to preserve —
    /// probed across the gap rather than asserted on the representation.
    /// </summary>
    [TestMethod]
    public void Conversion_PreservesMembershipExactly()
    {
        var set    = Int32Set.From(1, 2, 3, 7, 8);
        var ranges = set.ToRangeSet();

        for (var probe = -2; probe <= 12; probe++)
            Assert.AreEqual(set.Contains(probe), ranges.Contains(probe), $"membership of {probe} changed");
    }

    // -------------------------------------------------------------------------
    // Indexer
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Indexer_ReturnsElementsInCanonicalOrder()
    {
        var set = Int32Set.From(10, 2, 1);

        Assert.AreEqual(1, set[0]);
        Assert.AreEqual(2, set[1]);
        Assert.AreEqual(10, set[2]);
    }

    [TestMethod]
    public void Indexer_StringSet_IsOrdinalOrder()
    {
        // Ordinal puts 'Z' (90) before 'a' (97); a culture sort would not.
        var set = StringSet.From("apple", "Zebra");

        Assert.AreEqual("Zebra", set[0]);
        Assert.AreEqual("apple", set[1]);
    }

    [TestMethod]
    public void Indexer_OutOfRange_Throws()
        => Assert.ThrowsExactly<IndexOutOfRangeException>(() => _ = Int32Set.From(1)[5]);
}

using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;
using DateRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DateRange, System.DateOnly>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Covers <c>Gap</c> on a range and <c>Gaps</c> on a set: the range between two ranges, the
/// inner complement of a set, and the uncovered parts of a window. The worked examples are the
/// ones <c>docs/ranges.md</c> shows; <c>SmallModelOracleTests</c> and
/// <c>SmallModelMultirangeOracleTests</c> hold every shape pair against set theory.
/// </summary>
[TestClass]
public class RangeGapTests
{
    // -------------------------------------------------------------------------
    // Gap between two ranges
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Gap_DisjointFinite_IsTheRangeBetween_InEitherOrder()
    {
        var a = Int32Range.CreateFinite(1, 3);
        var b = Int32Range.CreateFinite(10, 12);

        Assert.AreEqual(Int32Range.CreateFinite(4, 9), a.Gap(b));
        Assert.AreEqual(Int32Range.CreateFinite(4, 9), b.Gap(a));
    }

    [TestMethod]
    public void Gap_OverlappingOrAdjacent_IsEmpty()
    {
        var a = Int32Range.CreateFinite(1, 5);

        Assert.AreEqual(Int32Range.Empty, a.Gap(Int32Range.CreateFinite(3, 9)));
        Assert.AreEqual(Int32Range.Empty, a.Gap(Int32Range.CreateFinite(6, 9)), "[1,5] and [6,9] are adjacent");
        Assert.AreEqual(DecimalRange.Empty, DecimalRange.CreateFinite(1m, 3m).Gap(DecimalRange.CreateFinite(3m, 5m)),
                        "[1,3) and [3,5) are adjacent");
    }

    [TestMethod]
    public void Gap_WithAnEmptyOperand_IsEmpty()
    {
        Assert.AreEqual(Int32Range.Empty, Int32Range.CreateFinite(1, 3).Gap(Int32Range.Empty));
        Assert.AreEqual(Int32Range.Empty, Int32Range.Empty.Gap(Int32Range.CreateFinite(1, 3)));
        Assert.AreEqual(Int32Range.Empty, Int32Range.Empty.Gap(Int32Range.Empty));
    }

    [TestMethod]
    public void Gap_Continuous_BoundsTakeTheOppositeInclusiveness()
    {
        // [1,3) and (3,5] share no value and are not adjacent: the single value 3 lies between.
        var left  = DecimalRange.CreateFinite(1m, 3m);
        var right = DecimalRange.CreateFinite(3m, 5m, false, true);

        Assert.AreEqual(DecimalRange.CreateFinite(3m, 3m, true, true), left.Gap(right));

        // [1,3] and (5,7]: the gap is (3,5], open where [1,3] is closed and closed where (5,7] is open.
        Assert.AreEqual(DecimalRange.CreateFinite(3m, 5m, false, true),
                        DecimalRange.CreateFinite(1m, 3m, true, true).Gap(DecimalRange.CreateFinite(5m, 7m, false, true)));
    }

    [TestMethod]
    public void Gap_UnboundedOperands_GapOnTheBoundedSide()
    {
        var before = Int32Range.CreateUnboundedStart(3, true);  // (-∞,3]
        var after  = Int32Range.CreateUnboundedEnd(10);          // [10,+∞)

        Assert.AreEqual(Int32Range.CreateFinite(4, 9), before.Gap(after));
        Assert.AreEqual(Int32Range.CreateFinite(4, 9), after.Gap(before));
        Assert.AreEqual(Int32Range.CreateFinite(4, 9), before.Gap(Int32Range.CreateFinite(10, 20)));
        Assert.AreEqual(Int32Range.Empty, Int32Range.Infinite.Gap(Int32Range.CreateFinite(1, 3)));
    }

    [TestMethod]
    public void Gap_Dates_BetweenTwoBookings()
    {
        var first  = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 8));
        var second = DateRange.CreateFinite(new DateOnly(2025, 6, 12), new DateOnly(2025, 6, 15));

        Assert.AreEqual(DateRange.CreateFinite(new DateOnly(2025, 6, 9), new DateOnly(2025, 6, 11)), first.Gap(second));
    }

    // -------------------------------------------------------------------------
    // Gaps of a set
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Gaps_AreTheInnerComplement_WithoutTheUnboundedEnds()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 10), Int32Range.CreateFinite(20, 30), Int32Range.CreateFinite(35, 40)]);

        Assert.AreEqual(IntSet.From([Int32Range.CreateFinite(11, 19), Int32Range.CreateFinite(31, 34)]), set.Gaps());
        Assert.AreEqual(set.Gaps().Count + 2, set.Complement().Count,
                        "the complement adds exactly the two unbounded stretches");
    }

    [TestMethod]
    public void Gaps_OfFewerThanTwoElements_IsEmpty()
    {
        Assert.AreSame(IntSet.Empty, IntSet.Empty.Gaps());
        Assert.AreSame(IntSet.Empty, IntSet.From([Int32Range.CreateFinite(1, 5)]).Gaps());
        Assert.AreSame(IntSet.Empty, IntSet.Infinite.Gaps());
    }

    [TestMethod]
    public void Gaps_SetOpenAtBothEnds_HasItsHole()
    {
        var gapped = IntSet.From([Int32Range.CreateUnboundedStart(5, true), Int32Range.CreateUnboundedEnd(10)]);

        Assert.AreEqual(IntSet.From([Int32Range.CreateFinite(6, 9)]), gapped.Gaps());
    }

    [TestMethod]
    public void Gaps_InAWindow_AreTheFreeSlots()
    {
        DateOnly June(int day) => new(2025, 6, day);

        var booked = DateRangeSet.From([DateRange.CreateFinite(June(3), June(5)), DateRange.CreateFinite(June(20), June(25))]);
        var june   = DateRange.CreateFinite(June(1), June(30));

        Assert.AreEqual(DateRangeSet.From([DateRange.CreateFinite(June(1), June(2)),
                                      DateRange.CreateFinite(June(6), June(19)),
                                      DateRange.CreateFinite(June(26), June(30))]),
                        booked.Gaps(june));
        Assert.AreEqual(booked.Complement().Intersect(june), booked.Gaps(june));
    }

    [TestMethod]
    public void Gaps_InAWindow_CoveredOrEmptyWindow_IsEmpty()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 10)]);

        Assert.AreEqual(IntSet.Empty, set.Gaps(Int32Range.CreateFinite(2, 9)));
        Assert.AreEqual(IntSet.Empty, set.Gaps(Int32Range.Empty));
        Assert.AreEqual(IntSet.From([Int32Range.CreateUnboundedEnd(11)]), set.Gaps(Int32Range.CreateUnboundedEnd(5)),
                        "an unbounded window keeps its open side where the set leaves it uncovered");
        Assert.AreEqual(IntSet.From([Int32Range.CreateFinite(1, 5)]), IntSet.Empty.Gaps(Int32Range.CreateFinite(1, 5)));
    }

    // -------------------------------------------------------------------------
    // The documented examples, as printed
    // -------------------------------------------------------------------------

    [TestMethod]
    public void GettingStarted_FreeDaysBetweenStays_PrintAsDocumented()
    {
        var june    = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 8));
        var july    = DateRange.CreateFinite(new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 5));
        var blocked = june.Union(july);
        var summer  = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 8, 31));

        Assert.AreEqual("[2025-06-09,2025-06-30]", june.Gap(july).ToString());
        Assert.AreEqual("{[2025-06-09,2025-06-30]}", blocked.Gaps().ToString());
        Assert.AreEqual("{[2025-06-09,2025-06-30],[2025-07-06,2025-08-31]}", blocked.Gaps(summer).ToString());
    }

    [TestMethod]
    public void RangesGuide_FreeSlots_AsDocumented()
    {
        var booked = IntSet.From([Int32Range.CreateFinite(1, 10), Int32Range.CreateFinite(20, 30), Int32Range.CreateFinite(35, 40)]);

        Assert.AreEqual("{[11,19],[31,34]}", booked.Gaps().ToString());
        Assert.AreEqual("{(,0],[11,19],[31,34],[41,)}", booked.Complement().ToString());
        Assert.AreEqual("{[11,19],[31,34],[41,50]}", booked.Gaps(Int32Range.CreateFinite(5, 50)).ToString());
        Assert.AreEqual("{[11,19],[31,34],[41,)}", booked.Gaps(Int32Range.CreateUnboundedEnd(5)).ToString());
        Assert.AreEqual(Int32Range.Empty, Int32Range.CreateFinite(1, 3).Gap(Int32Range.CreateFinite(4, 6)));
    }
}

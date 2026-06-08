namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeContainsTests
{
    [TestMethod]
    public void Contains_Value_InclusiveBothEnds_BoundaryAndInterior_ReturnsTrue()
    {
        var range = Int32Range.Closed(1, 10, true, true); // [1, 10]

        Assert.IsTrue(range.Contains(1));
        Assert.IsTrue(range.Contains(5));
        Assert.IsTrue(range.Contains(10));
    }

    [TestMethod]
    public void Contains_Value_InclusiveBothEnds_OutsideBoundary_ReturnsFalse()
    {
        var range = Int32Range.Closed(1, 10, true, true); // [1, 10]

        Assert.IsFalse(range.Contains(0));
        Assert.IsFalse(range.Contains(11));
    }

    [TestMethod]
    public void Contains_Value_ExclusiveBothEnds_InteriorIsContained()
    {
        var range = Int32Range.Closed(1, 10, false, false); // (1, 10)

        Assert.IsTrue(range.Contains(2));
        Assert.IsTrue(range.Contains(9));
    }

    [TestMethod]
    public void Contains_Value_ExclusiveBothEnds_BoundaryNotContained()
    {
        var range = Int32Range.Closed(1, 10, false, false); // (1, 10)

        Assert.IsFalse(range.Contains(1));
        Assert.IsFalse(range.Contains(10));
    }

    [TestMethod]
    public void Contains_Value_LowerInclusiveUpperExclusive_BoundaryBehaviour()
    {
        var range = Int32Range.Closed(1, 10, true, false); // [1, 10)

        Assert.IsTrue(range.Contains(1));
        Assert.IsFalse(range.Contains(10));
    }

    [TestMethod]
    public void Contains_Value_LowerExclusiveUpperInclusive_BoundaryBehaviour()
    {
        var range = Int32Range.Closed(1, 10, false, true); // (1, 10]

        Assert.IsFalse(range.Contains(1));
        Assert.IsTrue(range.Contains(10));
    }

    [TestMethod]
    public void Contains_Value_OpenStartInclusive_BelowUpperBound_ReturnsTrue()
    {
        var range = Int32Range.WithOpenStart(10, true); // (-∞, 10]

        Assert.IsTrue(range.Contains(10));
        Assert.IsTrue(range.Contains(int.MinValue));
        Assert.IsTrue(range.Contains(-100));
    }

    [TestMethod]
    public void Contains_Value_OpenStartInclusive_AboveUpperBound_ReturnsFalse()
    {
        var range = Int32Range.WithOpenStart(10, true); // (-∞, 10]

        Assert.IsFalse(range.Contains(11));
    }

    [TestMethod]
    public void Contains_Value_OpenStartExclusive_AtUpperBound_ReturnsFalse()
    {
        var range = Int32Range.WithOpenStart(10, false); // (-∞, 10)

        Assert.IsFalse(range.Contains(10));
        Assert.IsTrue(range.Contains(9));
    }

    [TestMethod]
    public void Contains_Value_OpenEndInclusive_AboveLowerBound_ReturnsTrue()
    {
        var range = Int32Range.WithOpenEnd(5, true); // [5, ∞)

        Assert.IsTrue(range.Contains(5));
        Assert.IsTrue(range.Contains(int.MaxValue));
        Assert.IsTrue(range.Contains(100));
    }

    [TestMethod]
    public void Contains_Value_OpenEndInclusive_BelowLowerBound_ReturnsFalse()
    {
        var range = Int32Range.WithOpenEnd(5, true); // [5, ∞)

        Assert.IsFalse(range.Contains(4));
    }

    [TestMethod]
    public void Contains_Value_OpenEndExclusive_AtLowerBound_ReturnsFalse()
    {
        var range = Int32Range.WithOpenEnd(5, false); // (5, ∞)

        Assert.IsFalse(range.Contains(5));
        Assert.IsTrue(range.Contains(6));
    }

    [TestMethod]
    public void Contains_Value_EmptyRange_AlwaysReturnsFalse()
    {
        var range = Int32Range.EmptyRange();

        Assert.IsFalse(range.Contains(0));
        Assert.IsFalse(range.Contains(5));
    }

    [TestMethod]
    public void Contains_Range_InnerStrictlyInside_ReturnsTrue()
    {
        var outer = Int32Range.Closed(1, 10, true, true); // [1, 10]
        var inner = Int32Range.Closed(3, 8,  true, true); // [3, 8]

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_EqualRangesBothInclusive_ReturnsTrue()
    {
        var outer = Int32Range.Closed(1, 10, true, true);
        var inner = Int32Range.Closed(1, 10, true, true);

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OuterInclusiveInnerExclusive_SameBounds_ReturnsTrue()
    {
        var outer = Int32Range.Closed(1, 10, true,  true);  // [1, 10]
        var inner = Int32Range.Closed(1, 10, false, false); // (1, 10) — stricter inner is contained

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OuterExclusiveInnerInclusive_SameLowerBound_ReturnsFalse()
    {
        var outer = Int32Range.Closed(1, 10, false, false); // (1, 10)
        var inner = Int32Range.Closed(1, 5,  true,  true);  // [1, 5] — inner claims 1, outer doesn't

        Assert.IsFalse(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OuterExclusiveInnerInclusive_SameUpperBound_ReturnsFalse()
    {
        var outer = Int32Range.Closed(1, 10, false, false); // (1, 10)
        var inner = Int32Range.Closed(5, 10, true,  true);  // [5, 10] — inner claims 10, outer doesn't

        Assert.IsFalse(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_InnerExtendsBeyondOuter_ReturnsFalse()
    {
        var outer = Int32Range.Closed(1, 10);
        var inner = Int32Range.Closed(1, 15);

        Assert.IsFalse(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenStartOuter_FiniteInner_EndCoversInner_ReturnsTrue()
    {
        // (-∞, 10] contains [3, 7] — matches Postgres '(,10]'::int4range @> '[3,7]'
        var outer = Int32Range.WithOpenStart(10, true);  // (-∞, 10]
        var inner = Int32Range.Closed(3, 7, true, true); // [3, 7]

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenStartOuter_FiniteInner_InnerExceedsUpperBound_ReturnsFalse()
    {
        // (-∞, 10] does NOT contain [3, 12]
        var outer = Int32Range.WithOpenStart(10, true);   // (-∞, 10]
        var inner = Int32Range.Closed(3, 12, true, true); // [3, 12]

        Assert.IsFalse(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenStartOuter_FiniteInner_ExclusiveUpperBoundDoesNotCoverInclusiveInner_ReturnsFalse()
    {
        // (-∞, 10) does NOT contain [3, 10] — outer is exclusive at 10, inner is inclusive
        var outer = Int32Range.WithOpenStart(10, false);  // (-∞, 10)
        var inner = Int32Range.Closed(3, 10, true, true); // [3, 10]

        Assert.IsFalse(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenStartOuter_OpenStartInner_OuterEndCoversInnerEnd_ReturnsTrue()
    {
        // (-∞, 10] contains (-∞, 7]
        var outer = Int32Range.WithOpenStart(10, true); // (-∞, 10]
        var inner = Int32Range.WithOpenStart(7,  true); // (-∞, 7]

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenStartOuter_OpenEndInner_AlwaysReturnsFalse()
    {
        // (-∞, 10] can never contain [3, +∞) — inner is unbounded on the right
        var outer = Int32Range.WithOpenStart(10, true); // (-∞, 10]
        var inner = Int32Range.WithOpenEnd(3, true);    // [3, +∞)

        Assert.IsFalse(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenEndOuter_FiniteInner_StartCoversInner_ReturnsTrue()
    {
        // [1, +∞) contains [3, 7]
        var outer = Int32Range.WithOpenEnd(1, true);     // [1, +∞)
        var inner = Int32Range.Closed(3, 7, true, true); // [3, 7]

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenEndOuter_FiniteInner_InnerBelowLowerBound_ReturnsFalse()
    {
        // (5, +∞) does NOT contain [3, 7] — inner starts at 3, outer starts after 5
        var outer = Int32Range.WithOpenEnd(5, false);    // (5, +∞)
        var inner = Int32Range.Closed(3, 7, true, true); // [3, 7]

        Assert.IsFalse(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenEndOuter_OpenEndInner_OuterStartCoversInnerStart_ReturnsTrue()
    {
        // [1, +∞) contains [5, +∞)
        var outer = Int32Range.WithOpenEnd(1, true); // [1, +∞)
        var inner = Int32Range.WithOpenEnd(5, true); // [5, +∞)

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Range_OpenEndOuter_OpenStartInner_AlwaysReturnsFalse()
    {
        // [1, +∞) can never contain (-∞, 7] — inner is unbounded on the left
        var outer = Int32Range.WithOpenEnd(1, true);   // [1, +∞)
        var inner = Int32Range.WithOpenStart(7, true); // (-∞, 7]

        Assert.IsFalse(outer.Contains(inner));
    }
}
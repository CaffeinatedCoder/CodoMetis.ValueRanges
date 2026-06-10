using CodoMetis.ValueRanges.Core;
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;
using DecimalSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DecimalRange, decimal>;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeSetTests
{
    // -------------------------------------------------------------------------
    // From / normalization
    // -------------------------------------------------------------------------

    [TestMethod]
    public void From_EmptyInput_ReturnsEmptySingleton()
    {
        var result = IntSet.From([]);

        Assert.AreEqual(0, result.Count);
        Assert.AreSame(IntSet.Empty, result);
    }

    [TestMethod]
    public void From_AllEmptyRanges_ReturnsEmptySingleton()
    {
        var result = IntSet.From([Int32Range.Empty, Int32Range.Empty]);

        Assert.AreSame(IntSet.Empty, result);
    }

    [TestMethod]
    public void From_SingleRange_ReturnsSingleElementSet()
    {
        var range = Int32Range.CreateFinite(1, 5);

        var result = IntSet.From([range]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(range, result[0]);
    }

    [TestMethod]
    public void From_ContainsInfinity_ReturnsInfiniteSingleton()
    {
        var result = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.Infinite]);

        Assert.AreSame(IntSet.Infinite, result);
    }

    [TestMethod]
    public void From_OverlappingRanges_MergesIntoOne()
    {
        var result = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(3, 8)]);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1, merged.Start);
        Assert.AreEqual(8, merged.End);
    }

    [TestMethod]
    public void From_AdjacentDiscreteRanges_MergesIntoOne()
    {
        var result = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(6, 10)]);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1,  merged.Start);
        Assert.AreEqual(10, merged.End);
    }

    [TestMethod]
    public void From_ContinuousAdjacentRanges_XorInclusiveness_MergesIntoOne()
    {
        var result = DecimalSet.From([
            DecimalRange.CreateFinite(1m, 5m,  true, false), // [1, 5)
            DecimalRange.CreateFinite(5m, 10m, true, true)   // [5, 10]
        ]);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<decimal>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1m,  merged.Start);
        Assert.AreEqual(10m, merged.End);
    }

    [TestMethod]
    public void From_ContinuousTouchingRanges_BothExclusive_StaySeparate()
    {
        // [1, 5) and (5, 10] — the point 5 is missing from both; not adjacent, not overlapping.
        var result = DecimalSet.From([
            DecimalRange.CreateFinite(1m, 5m,  true,  false), // [1, 5)
            DecimalRange.CreateFinite(5m, 10m, false, true)   // (5, 10]
        ]);

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void From_UnsortedDisjointRanges_SortsByLowerBound()
    {
        var late  = Int32Range.CreateFinite(7, 10);
        var early = Int32Range.CreateFinite(1, 3);

        var result = IntSet.From([late, early]);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(early, result[0]);
        Assert.AreEqual(late,  result[1]);
    }

    [TestMethod]
    public void From_ChainOfAdjacentRanges_MergesAll()
    {
        var result = IntSet.From([
            Int32Range.CreateFinite(7, 9),
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(4, 6)
        ]);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1, merged.Start);
        Assert.AreEqual(9, merged.End);
    }

    [TestMethod]
    public void From_UnboundedStartAndDisjointFinite_SortsUnboundedStartFirst()
    {
        var finite    = Int32Range.CreateFinite(10, 20);
        var openStart = Int32Range.CreateUnboundedStart(5, true); // (-∞, 5]

        var result = IntSet.From([finite, openStart]);

        Assert.AreEqual(2, result.Count);
        Assert.IsInstanceOfType<IUnboundedStartRange<int>>(result[0]);
        Assert.AreEqual(finite, result[1]);
    }

    // -------------------------------------------------------------------------
    // Sentinels
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Empty_HasCountZero()
    {
        Assert.AreEqual(0, IntSet.Empty.Count);
    }

    [TestMethod]
    public void Infinite_IsSingleInfinityElement()
    {
        Assert.AreEqual(1, IntSet.Infinite.Count);
        Assert.IsInstanceOfType<IInfinityRange<int>>(IntSet.Infinite[0]);
    }

    // -------------------------------------------------------------------------
    // Equality
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Equals_SameNormalizedContentFromDifferentInputs_ReturnsTrue()
    {
        // [1, 10] built once directly and once from two adjacent pieces.
        var a = IntSet.From([Int32Range.CreateFinite(1, 10)]);
        var b = IntSet.From([Int32Range.CreateFinite(6, 10), Int32Range.CreateFinite(1, 5)]);

        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equals_DifferentContent_ReturnsFalse()
    {
        var a = IntSet.From([Int32Range.CreateFinite(1, 10)]);
        var b = IntSet.From([Int32Range.CreateFinite(1, 9)]);

        Assert.AreNotEqual(a, b);
    }

    // -------------------------------------------------------------------------
    // Query operations
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Contains_Value_InsideAnElement_ReturnsTrue()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        Assert.IsTrue(set.Contains(2));
        Assert.IsTrue(set.Contains(7));
    }

    [TestMethod]
    public void Contains_Value_InGapBetweenElements_ReturnsFalse()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        Assert.IsFalse(set.Contains(5));
    }

    [TestMethod]
    public void Contains_Range_EntirelyWithinOneElement_ReturnsTrue()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        Assert.IsTrue(set.Contains(Int32Range.CreateFinite(7, 8)));
    }

    [TestMethod]
    public void Contains_Range_SpanningGap_ReturnsFalse()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        Assert.IsFalse(set.Contains(Int32Range.CreateFinite(2, 8)));
    }

    [TestMethod]
    public void Overlaps_RangeReachingIntoElement_ReturnsTrue()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        Assert.IsTrue(set.Overlaps(Int32Range.CreateFinite(2, 8)));
    }

    [TestMethod]
    public void Overlaps_RangeEntirelyInGap_ReturnsFalse()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        Assert.IsFalse(set.Overlaps(Int32Range.CreateFinite(4, 6)));
    }

    // -------------------------------------------------------------------------
    // Union
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Union_Range_DisjointNonAdjacent_GrowsSet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3)]);

        var result = set.Union(Int32Range.CreateFinite(7, 9));

        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Union_Range_BridgingTwoElements_MergesThemAll()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        var result = set.Union(Int32Range.CreateFinite(4, 6)); // adjacent to both

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1, merged.Start);
        Assert.AreEqual(9, merged.End);
    }

    [TestMethod]
    public void Union_Range_Empty_ReturnsSameSet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3)]);

        Assert.AreSame(set, set.Union(Int32Range.Empty));
    }

    [TestMethod]
    public void Union_Range_Infinity_ReturnsInfiniteSingleton()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3)]);

        Assert.AreSame(IntSet.Infinite, set.Union(Int32Range.Infinite));
    }

    [TestMethod]
    public void Union_Set_InterleavedSets_MergesAdjacentAcrossSets()
    {
        var a = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(10, 15)]);
        var b = IntSet.From([Int32Range.CreateFinite(4, 5), Int32Range.CreateFinite(20, 25)]);

        var result = a.Union(b);

        // [1,3] and [4,5] are adjacent for int and merge to [1,5].
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5),   result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(10, 15), result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(20, 25), result[2]);
    }

    [TestMethod]
    public void Union_Set_WithEmptySet_ReturnsSameSet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3)]);

        Assert.AreSame(set, set.Union(IntSet.Empty));
        Assert.AreSame(set, IntSet.Empty.Union(set));
    }

    // -------------------------------------------------------------------------
    // Intersect
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Intersect_Range_NoOverlap_ReturnsEmptySet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        var result = set.Intersect(Int32Range.CreateFinite(4, 6));

        Assert.AreSame(IntSet.Empty, result);
    }

    [TestMethod]
    public void Intersect_Range_ClippingTwoElements_ReturnsTwoClippedElements()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(8, 12)]);

        var result = set.Intersect(Int32Range.CreateFinite(3, 10));

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(3, 5),  result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(8, 10), result[1]);
    }

    [TestMethod]
    public void Intersect_Range_Infinity_ReturnsEqualSet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        var result = set.Intersect(Int32Range.Infinite);

        Assert.AreEqual(set, result);
    }

    [TestMethod]
    public void Intersect_Set_OverlappingSets_ReturnsCommonValues()
    {
        var a = IntSet.From([Int32Range.CreateFinite(1, 10), Int32Range.CreateFinite(20, 30)]);
        var b = IntSet.From([Int32Range.CreateFinite(5, 25)]);

        var result = a.Intersect(b);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(5, 10),  result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(20, 25), result[1]);
    }

    [TestMethod]
    public void Intersect_Set_NoCommonValues_ReturnsEmptySet()
    {
        var a = IntSet.From([Int32Range.CreateFinite(1, 3)]);
        var b = IntSet.From([Int32Range.CreateFinite(7, 9)]);

        Assert.AreSame(IntSet.Empty, a.Intersect(b));
    }

    // -------------------------------------------------------------------------
    // Except
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Except_Range_NoOverlap_ReturnsEqualSet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        var result = set.Except(Int32Range.CreateFinite(4, 6));

        Assert.AreEqual(set, result);
    }

    [TestMethod]
    public void Except_Range_FullyCoversOneElement_RemovesIt()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        var result = set.Except(Int32Range.CreateFinite(6, 10));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 3), result[0]);
    }

    [TestMethod]
    public void Except_Range_InteriorOfOneElement_SplitsItAndGrowsSet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 10), Int32Range.CreateFinite(20, 30)]);

        var result = set.Except(Int32Range.CreateFinite(4, 6));

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 3),   result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(7, 10),  result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(20, 30), result[2]);
    }

    [TestMethod]
    public void Except_Range_Empty_ReturnsSameSet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3)]);

        Assert.AreSame(set, set.Except(Int32Range.Empty));
    }

    [TestMethod]
    public void Except_Set_MultipleRemovals_ReturnsRemainder()
    {
        var set   = IntSet.From([Int32Range.CreateFinite(1, 20)]);
        var holes = IntSet.From([Int32Range.CreateFinite(3, 5), Int32Range.CreateFinite(8, 10)]);

        var result = set.Except(holes);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 2),   result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(6, 7),   result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(11, 20), result[2]);
    }

    // -------------------------------------------------------------------------
    // Complement
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Complement_SingleFiniteElement_ReturnsUnboundedStartAndUnboundedEnd()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 5)]);

        var result = set.Complement();

        Assert.AreEqual(2, result.Count);
        var left  = result[0] as IUnboundedStartRange<int>;
        var right = result[1] as IUnboundedEndRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);
        Assert.AreEqual(0, left.End);    // canonical: (-∞, 1) ≡ (-∞, 0]
        Assert.AreEqual(6, right.Start); // canonical: (5, ∞) ≡ [6, ∞)
    }

    [TestMethod]
    public void Complement_TwoElements_ReturnsGapAndUnboundedStretches()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        var result = set.Complement();

        Assert.AreEqual(3, result.Count);
        Assert.IsInstanceOfType<IUnboundedStartRange<int>>(result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(4, 6), result[1]); // the gap
        Assert.IsInstanceOfType<IUnboundedEndRange<int>>(result[2]);
    }

    [TestMethod]
    public void Complement_EmptySet_ReturnsInfinite()
    {
        Assert.AreSame(IntSet.Infinite, IntSet.Empty.Complement());
    }

    [TestMethod]
    public void Complement_InfiniteSet_ReturnsEmpty()
    {
        Assert.AreSame(IntSet.Empty, IntSet.Infinite.Complement());
    }

    [TestMethod]
    public void Complement_IsItsOwnInverse()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);

        Assert.AreEqual(set, set.Complement().Complement());
    }

    // -------------------------------------------------------------------------
    // IReadOnlyList behavior
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Enumeration_VisitsElementsInLowerBoundOrder()
    {
        var set = IntSet.From([Int32Range.CreateFinite(7, 9), Int32Range.CreateFinite(1, 3)]);

        var elements = set.ToList();

        Assert.AreEqual(2, elements.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 3), elements[0]);
        Assert.AreEqual(Int32Range.CreateFinite(7, 9), elements[1]);
    }
}

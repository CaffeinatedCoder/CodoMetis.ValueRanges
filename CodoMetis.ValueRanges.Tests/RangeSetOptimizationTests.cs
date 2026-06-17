using System.Globalization;
using CodoMetis.ValueRanges.Core;
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;
using DecimalSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DecimalRange, decimal>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Edge cases that specifically exercise the optimized code paths in
/// <see cref="RangeSet{TRange,T}"/>: binary-search queries on multi-element sets,
/// merge-join set operations, the O(|other|) complement walk, and the new
/// <see cref="RangeSet{TRange,T}.LowerBoundComparer"/>. These complement the
/// behavioral tests in <see cref="RangeSetTests"/>.
/// </summary>
[TestClass]
public class RangeSetOptimizationTests
{
    // -------------------------------------------------------------------------
    // Binary-search query paths (Contains / Overlaps on multi-element sets)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Contains_Value_LargeSet_MiddleElement_FoundByBinarySearch()
    {
        // Five disjoint elements; the value 22 lives in the middle element.
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(10, 15),
            Int32Range.CreateFinite(20, 25),
            Int32Range.CreateFinite(30, 35),
            Int32Range.CreateFinite(40, 45)
        ]);

        Assert.IsTrue(set.Contains(22));
        Assert.IsTrue(set.Contains(20)); // lower bound of the middle element
        Assert.IsTrue(set.Contains(25)); // upper bound of the middle element
    }

    [TestMethod]
    public void Contains_Value_LargeSet_GapBetweenElements_ReturnsFalse()
    {
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(10, 15),
            Int32Range.CreateFinite(20, 25)
        ]);

        // 7 lives in the gap between [1,5] and [10,15]; the binary-search candidate is
        // [1,5] (last element with lower ≤ 7), which does not contain 7.
        Assert.IsFalse(set.Contains(7));
        // 17 lives in the gap between [10,15] and [20,25].
        Assert.IsFalse(set.Contains(17));
        // 100 is past every element; no candidate exists.
        Assert.IsFalse(set.Contains(100));
    }

    [TestMethod]
    public void Contains_Value_SetWithUnboundedStart_FoundByBinarySearch()
    {
        // The UnboundedStart element sorts first and has lower bound = -∞, so the binary
        // search must treat it as always ≤ the value.
        var set = IntSet.From([
            Int32Range.CreateUnboundedStart(5, true),  // (-∞, 5]
            Int32Range.CreateFinite(10, 15),
            Int32Range.CreateFinite(20, 25)
        ]);

        Assert.IsTrue(set.Contains(-100));
        Assert.IsTrue(set.Contains(5));
        Assert.IsFalse(set.Contains(7)); // gap between (-∞, 5] and [10, 15]
        Assert.IsTrue(set.Contains(12));
    }

    [TestMethod]
    public void Contains_Range_LargeSet_FindsContainingElementByBinarySearch()
    {
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(10, 15),
            Int32Range.CreateFinite(20, 25),
            Int32Range.CreateFinite(30, 35)
        ]);

        // The query [21, 24] is contained in the third element — the binary search finds
        // it as the last element with lower bound ≤ 21.
        Assert.IsTrue(set.Contains(Int32Range.CreateFinite(21, 24)));
        // Spanning a gap is never contained.
        Assert.IsFalse(set.Contains(Int32Range.CreateFinite(12, 22)));
    }

    [TestMethod]
    public void Contains_Range_UnboundedStartQuery_OnlyFirstElementCanContain()
    {
        var set = IntSet.From([
            Int32Range.CreateUnboundedStart(10, true), // (-∞, 10]
            Int32Range.CreateFinite(20, 30)
        ]);

        // (-∞, 5] is contained by the first element (-∞, 10].
        Assert.IsTrue(set.Contains(Int32Range.CreateUnboundedStart(5, true)));
        // (-∞, 15] extends past the first element's upper bound, so it is not contained.
        Assert.IsFalse(set.Contains(Int32Range.CreateUnboundedStart(15, true)));
    }

    [TestMethod]
    public void Contains_Range_InfinityQuery_OnlyInfiniteSetContains()
    {
        var finite = IntSet.From([Int32Range.CreateFinite(1, 10)]);
        Assert.IsFalse(finite.Contains(Int32Range.Infinite));

        Assert.IsTrue(IntSet.Infinite.Contains(Int32Range.Infinite));
    }

    [TestMethod]
    public void Contains_Range_EmptyQuery_ReturnsFalse()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 10)]);
        Assert.IsFalse(set.Contains(Int32Range.Empty));
    }

    [TestMethod]
    public void Overlaps_Range_LargeSet_FindsOverlappingElementByBinarySearch()
    {
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(10, 15),
            Int32Range.CreateFinite(20, 25),
            Int32Range.CreateFinite(30, 35)
        ]);

        // [22, 28] overlaps the third element; the binary search locates it as the last
        // element with lower bound ≤ 28.
        Assert.IsTrue(set.Overlaps(Int32Range.CreateFinite(22, 28)));
        // [16, 18] sits entirely in the gap between [10,15] and [20,25]; the candidate is
        // [10,15] (last with lower ≤ 18), which does not overlap.
        Assert.IsFalse(set.Overlaps(Int32Range.CreateFinite(16, 18)));
    }

    [TestMethod]
    public void Overlaps_Range_UnboundedEndQuery_ChecksLastElement()
    {
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(10, 15)
        ]);

        // [20, +∞) starts after every element ends — the last element [10, 15] does not
        // overlap it, so no element does.
        Assert.IsFalse(set.Overlaps(Int32Range.CreateUnboundedEnd(20, true)));
        // [12, +∞) overlaps [10, 15] (and would overlap any later element if present).
        Assert.IsTrue(set.Overlaps(Int32Range.CreateUnboundedEnd(12, true)));
    }

    [TestMethod]
    public void Overlaps_Range_InfinityQuery_AnyNonEmptySetOverlaps()
    {
        Assert.IsTrue(IntSet.From([Int32Range.CreateFinite(1, 5)]).Overlaps(Int32Range.Infinite));
        Assert.IsFalse(IntSet.Empty.Overlaps(Int32Range.Infinite));
    }

    // -------------------------------------------------------------------------
    // Set operations involving the Infinite singleton
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Intersect_Set_WithInfinite_ReturnsOtherSet()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(10, 15)]);

        Assert.AreEqual(set, set.Intersect(IntSet.Infinite));
        Assert.AreEqual(set, IntSet.Infinite.Intersect(set));
        Assert.AreEqual(IntSet.Infinite, IntSet.Infinite.Intersect(IntSet.Infinite));
    }

    [TestMethod]
    public void Intersect_Set_OneSideEmpty_ReturnsEmpty()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 5)]);

        Assert.AreSame(IntSet.Empty, set.Intersect(IntSet.Empty));
        Assert.AreSame(IntSet.Empty, IntSet.Empty.Intersect(set));
    }

    [TestMethod]
    public void Union_Set_WithInfinite_ReturnsInfiniteSingleton()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(10, 15)]);

        Assert.AreSame(IntSet.Infinite, set.Union(IntSet.Infinite));
        Assert.AreSame(IntSet.Infinite, IntSet.Infinite.Union(set));
        Assert.AreSame(IntSet.Infinite, IntSet.Infinite.Union(IntSet.Infinite));
    }

    [TestMethod]
    public void Except_Set_OtherIsInfinite_ReturnsEmpty()
    {
        var set = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(10, 15)]);

        Assert.AreSame(IntSet.Empty, set.Except(IntSet.Infinite));
    }

    [TestMethod]
    public void Except_Set_ThisIsInfinite_ReturnsComplementOfOther()
    {
        // (-∞, +∞) \ {[3, 5], [8, 10]} = (-∞, 3) ∪ (5, 8) ∪ (10, +∞)
        //                                = (-∞, 2] ∪ [6, 7] ∪ [11, +∞)   for int
        var holes = IntSet.From([
            Int32Range.CreateFinite(3, 5),
            Int32Range.CreateFinite(8, 10)
        ]);

        var result = IntSet.Infinite.Except(holes);

        Assert.AreEqual(3, result.Count);
        var left   = result[0] as IUnboundedStartRange<int>;
        var middle = result[1] as IFiniteRange<int>;
        var right  = result[2] as IUnboundedEndRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(middle);
        Assert.IsNotNull(right);
        Assert.AreEqual(2,  left.End);
        Assert.AreEqual(6,  middle.Start);
        Assert.AreEqual(7,  middle.End);
        Assert.AreEqual(11, right.Start);
    }

    [TestMethod]
    public void Except_Set_ThisIsInfinite_OtherHasUnboundedStart_ReturnsUnboundedEndOnly()
    {
        // (-∞, +∞) \ (-∞, 5] = (5, +∞) = [6, +∞) for int
        var holes = IntSet.From([Int32Range.CreateUnboundedStart(5, true)]);

        var result = IntSet.Infinite.Except(holes);

        Assert.AreEqual(1, result.Count);
        var right = result[0] as IUnboundedEndRange<int>;
        Assert.IsNotNull(right);
        Assert.AreEqual(6, right.Start);
        Assert.IsTrue(right.StartInclusive);
    }

    [TestMethod]
    public void Except_Set_ThisIsInfinite_OtherHasUnboundedEnd_ReturnsUnboundedStartOnly()
    {
        // (-∞, +∞) \ [5, +∞) = (-∞, 5) = (-∞, 4] for int
        var holes = IntSet.From([Int32Range.CreateUnboundedEnd(5, true)]);

        var result = IntSet.Infinite.Except(holes);

        Assert.AreEqual(1, result.Count);
        var left = result[0] as IUnboundedStartRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(4, left.End);
        Assert.IsTrue(left.EndInclusive);
    }

    // -------------------------------------------------------------------------
    // Merge-join Intersect on interleaved multi-element sets
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Intersect_Set_InterleavedFiniteElements_ReturnsAllPairwiseOverlaps()
    {
        // a = [1, 10] [20, 30] [40, 50]
        // b = [5, 25] [35, 45]
        // Intersections: [5, 10] [20, 25] [40, 45]
        var a = IntSet.From([
            Int32Range.CreateFinite(1, 10),
            Int32Range.CreateFinite(20, 30),
            Int32Range.CreateFinite(40, 50)
        ]);
        var b = IntSet.From([
            Int32Range.CreateFinite(5, 25),
            Int32Range.CreateFinite(35, 45)
        ]);

        var result = a.Intersect(b);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(5, 10),  result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(20, 25), result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(40, 45), result[2]);
    }

    [TestMethod]
    public void Intersect_Set_ContinuousInclusiveness_PreservesStricterBound()
    {
        // a = [1.0, 10.0] [20.0, 30.0]
        // b = (5.0, 25.0]
        // Intersections: (5.0, 10.0] [20.0, 25.0]
        var a = DecimalSet.From([
            DecimalRange.CreateFinite(1m, 10m, startInclusive: true,  endInclusive: true),
            DecimalRange.CreateFinite(20m, 30m, startInclusive: true, endInclusive: true)
        ]);
        var b = DecimalSet.From([
            DecimalRange.CreateFinite(5m, 25m, startInclusive: false, endInclusive: true)
        ]);

        var result = a.Intersect(b);

        Assert.AreEqual(2, result.Count);
        var first  = result[0] as IFiniteRange<decimal>;
        var second = result[1] as IFiniteRange<decimal>;
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(5m,  first.Start);
        Assert.IsFalse(first.StartInclusive); // stricter of b's exclusive and a's inclusive
        Assert.AreEqual(10m, first.End);
        Assert.IsTrue(first.EndInclusive);
        Assert.AreEqual(20m, second.Start);
        Assert.IsTrue(second.StartInclusive);
        Assert.AreEqual(25m, second.End);
        Assert.IsTrue(second.EndInclusive);
    }

    // -------------------------------------------------------------------------
    // Merge-walk Except on multi-element sets
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Except_Set_BothMultiElement_InterleavedHoles_ReturnsRemainingPieces()
    {
        // this   = [1, 30]
        // holes  = [5, 8] [15, 18] [25, 28]
        // result = [1, 4] [9, 14] [19, 24] [29, 30]
        var set = IntSet.From([Int32Range.CreateFinite(1, 30)]);
        var holes = IntSet.From([
            Int32Range.CreateFinite(5, 8),
            Int32Range.CreateFinite(15, 18),
            Int32Range.CreateFinite(25, 28)
        ]);

        var result = set.Except(holes);

        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 4),   result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(9, 14),  result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(19, 24), result[2]);
        Assert.AreEqual(Int32Range.CreateFinite(29, 30), result[3]);
    }

    [TestMethod]
    public void Except_Set_HoleSpansMultipleElements_RemovesThemAllInOneWalk()
    {
        // this   = [1, 5] [10, 15] [20, 25] [30, 35]
        // holes  = [8, 27]
        // result = [1, 5] [30, 35]   (the middle two elements are fully consumed)
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(10, 15),
            Int32Range.CreateFinite(20, 25),
            Int32Range.CreateFinite(30, 35)
        ]);
        var holes = IntSet.From([Int32Range.CreateFinite(8, 27)]);

        var result = set.Except(holes);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5),   result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(30, 35), result[1]);
    }

    [TestMethod]
    public void Except_Set_HolePartiallyOverlapsLastTouchedElement_LeavesRemainder()
    {
        // this   = [1, 5] [10, 15] [20, 25] [30, 35]
        // holes  = [8, 22]   — fully consumes [10, 15] but only trims [20, 25] to [23, 25]
        // result = [1, 5] [23, 25] [30, 35]
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(10, 15),
            Int32Range.CreateFinite(20, 25),
            Int32Range.CreateFinite(30, 35)
        ]);
        var holes = IntSet.From([Int32Range.CreateFinite(8, 22)]);

        var result = set.Except(holes);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5),   result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(23, 25), result[1]); // canonical (22, 25]
        Assert.AreEqual(Int32Range.CreateFinite(30, 35), result[2]);
    }

    [TestMethod]
    public void Except_Set_ThisHasUnboundedStart_TrimmedCorrectly()
    {
        // this   = (-∞, 100]
        // holes  = [10, 20] [50, 60]
        // result = (-∞, 9] [21, 49] [61, 100]
        var set = IntSet.From([Int32Range.CreateUnboundedStart(100, true)]);
        var holes = IntSet.From([
            Int32Range.CreateFinite(10, 20),
            Int32Range.CreateFinite(50, 60)
        ]);

        var result = set.Except(holes);

        Assert.AreEqual(3, result.Count);
        Assert.IsInstanceOfType<IUnboundedStartRange<int>>(result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(21, 49),   result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(61, 100),  result[2]);
    }

    [TestMethod]
    public void Except_Set_ThisHasUnboundedEnd_TrimmedCorrectly()
    {
        // this   = [1, +∞)
        // holes  = [10, 20] [50, 60]
        // result = [1, 9] [21, 49] [61, +∞)
        var set = IntSet.From([Int32Range.CreateUnboundedEnd(1, true)]);
        var holes = IntSet.From([
            Int32Range.CreateFinite(10, 20),
            Int32Range.CreateFinite(50, 60)
        ]);

        var result = set.Except(holes);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 9), result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(21, 49), result[1]);
        Assert.IsInstanceOfType<IUnboundedEndRange<int>>(result[2]);
    }

    // -------------------------------------------------------------------------
    // Complement — exercises the new O(|other|) ComplementOfSet walk
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Complement_ThreeFiniteElements_ReturnsTwoGapsAndTwoUnboundedStretches()
    {
        // set      = [1, 3] [7, 9] [15, 20]
        // complement = (-∞, 1) [4, 6] [10, 14] [21, +∞)
        //            = (-∞, 0] [4, 6] [10, 14] [21, +∞)  for int
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(7, 9),
            Int32Range.CreateFinite(15, 20)
        ]);

        var result = set.Complement();

        Assert.AreEqual(4, result.Count);
        Assert.IsInstanceOfType<IUnboundedStartRange<int>>(result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(4, 6),   result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(10, 14), result[2]);
        Assert.IsInstanceOfType<IUnboundedEndRange<int>>(result[3]);
    }

    [TestMethod]
    public void Complement_SetWithUnboundedStart_ReturnsUnboundedEndOnly()
    {
        // set = (-∞, 5]; complement = (5, +∞) = [6, +∞) for int
        var set = IntSet.From([Int32Range.CreateUnboundedStart(5, true)]);

        var result = set.Complement();

        Assert.AreEqual(1, result.Count);
        var right = result[0] as IUnboundedEndRange<int>;
        Assert.IsNotNull(right);
        Assert.AreEqual(6, right.Start);
        Assert.IsTrue(right.StartInclusive);
    }

    [TestMethod]
    public void Complement_SetWithUnboundedEnd_ReturnsUnboundedStartOnly()
    {
        // set = [5, +∞); complement = (-∞, 5) = (-∞, 4] for int
        var set = IntSet.From([Int32Range.CreateUnboundedEnd(5, true)]);

        var result = set.Complement();

        Assert.AreEqual(1, result.Count);
        var left = result[0] as IUnboundedStartRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(4, left.End);
        Assert.IsTrue(left.EndInclusive);
    }

    [TestMethod]
    public void Complement_SetWithUnboundedStartAndFinite_ReturnsGapAndUnboundedEnd()
    {
        // set = (-∞, 5] [10, 15]; complement = (5, 10) [16, +∞) = [6, 9] [16, +∞) for int
        var set = IntSet.From([
            Int32Range.CreateUnboundedStart(5, true),
            Int32Range.CreateFinite(10, 15)
        ]);

        var result = set.Complement();

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(6, 9), result[0]);
        Assert.IsInstanceOfType<IUnboundedEndRange<int>>(result[1]);
    }

    [TestMethod]
    public void Complement_Continuous_PreservesFlippedInclusiveness()
    {
        // set = [1.0, 5.0) [10.0, 15.0]
        // complement = (-∞, 1.0) [5.0, 10.0) (15.0, +∞)
        //            = (-∞, 1.0) [5.0, 10.0) (15.0, +∞)
        var set = DecimalSet.From([
            DecimalRange.CreateFinite(1m, 5m, startInclusive: true,  endInclusive: false),
            DecimalRange.CreateFinite(10m, 15m, startInclusive: true, endInclusive: true)
        ]);

        var result = set.Complement();

        Assert.AreEqual(3, result.Count);
        var left   = result[0] as IUnboundedStartRange<decimal>;
        var middle = result[1] as IFiniteRange<decimal>;
        var right  = result[2] as IUnboundedEndRange<decimal>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(middle);
        Assert.IsNotNull(right);
        Assert.AreEqual(1m,  left.End);
        Assert.IsFalse(left.EndInclusive);    // flipped from set's inclusive lower
        Assert.AreEqual(5m,  middle.Start);
        Assert.IsTrue(middle.StartInclusive); // flipped from set's exclusive upper
        Assert.AreEqual(10m, middle.End);
        Assert.IsFalse(middle.EndInclusive);  // flipped from set's inclusive lower
        Assert.AreEqual(15m, right.Start);
        Assert.IsFalse(right.StartInclusive); // flipped from set's inclusive upper
    }

    [TestMethod]
    public void Complement_MultiElement_IsItsOwnInverse()
    {
        var set = IntSet.From([
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(7, 9),
            Int32Range.CreateFinite(15, 20),
            Int32Range.CreateFinite(30, 35)
        ]);

        Assert.AreEqual(set, set.Complement().Complement());
    }

    // -------------------------------------------------------------------------
    // Union — merge of two pre-sorted streams
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Union_Set_BothMultiElement_Interleaved_MergesAdjacentAcrossSets()
    {
        // a = [1, 3] [10, 15] [20, 25]
        // b = [4, 5] [12, 13] [26, 30]
        // [1, 3] and [4, 5] are adjacent (int) → [1, 5]
        // [10, 15] absorbs [12, 13]
        // [20, 25] and [26, 30] are adjacent → [20, 30]
        // Result: [1, 5] [10, 15] [20, 30]
        var a = IntSet.From([
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(10, 15),
            Int32Range.CreateFinite(20, 25)
        ]);
        var b = IntSet.From([
            Int32Range.CreateFinite(4, 5),
            Int32Range.CreateFinite(12, 13),
            Int32Range.CreateFinite(26, 30)
        ]);

        var result = a.Union(b);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5),   result[0]);
        Assert.AreEqual(Int32Range.CreateFinite(10, 15), result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(20, 30), result[2]);
    }

    [TestMethod]
    public void Union_Set_WithUnboundedStarts_MergesIntoSingleUnboundedStart()
    {
        // a = (-∞, 5] [10, 15]
        // b = (-∞, 3] [20, 25]
        // Result: (-∞, 5] [10, 15] [20, 25]
        var a = IntSet.From([
            Int32Range.CreateUnboundedStart(5, true),
            Int32Range.CreateFinite(10, 15)
        ]);
        var b = IntSet.From([
            Int32Range.CreateUnboundedStart(3, true),
            Int32Range.CreateFinite(20, 25)
        ]);

        var result = a.Union(b);

        Assert.AreEqual(3, result.Count);
        Assert.IsInstanceOfType<IUnboundedStartRange<int>>(result[0]);
        var left = result[0] as IUnboundedStartRange<int>;
        Assert.AreEqual(5, left!.End); // the later of the two upper bounds
        Assert.AreEqual(Int32Range.CreateFinite(10, 15), result[1]);
        Assert.AreEqual(Int32Range.CreateFinite(20, 25), result[2]);
    }

    [TestMethod]
    public void Union_Set_UnboundedStartAndUnboundedEnd_MergesToInfinite()
    {
        // a = (-∞, 5]; b = [3, +∞); they overlap, so the union is (-∞, +∞).
        var a = IntSet.From([Int32Range.CreateUnboundedStart(5, true)]);
        var b = IntSet.From([Int32Range.CreateUnboundedEnd(3, true)]);

        Assert.AreSame(IntSet.Infinite, a.Union(b));
    }

    // -------------------------------------------------------------------------
    // RangeSet.LowerBoundComparer — the new public IComparer<TRange>
    // -------------------------------------------------------------------------

    [TestMethod]
    public void LowerBoundComparer_SortsRangesByLowerBound()
    {
        var unsorted = new List<Int32Range>
        {
            Int32Range.CreateFinite(20, 30),
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(10, 15)
        };

        unsorted.Sort(RangeSet<Int32Range, int>.LowerBoundComparer);

        CollectionAssert.AreEqual(
            new[]
            {
                Int32Range.CreateFinite(1, 5),
                Int32Range.CreateFinite(10, 15),
                Int32Range.CreateFinite(20, 30)
            },
            unsorted);
    }

    [TestMethod]
    public void LowerBoundComparer_UnboundedStartSortsBeforeFinite()
    {
        var unsorted = new List<Int32Range>
        {
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateUnboundedStart(10, true),
            Int32Range.CreateFinite(20, 30)
        };

        unsorted.Sort(RangeSet<Int32Range, int>.LowerBoundComparer);

        Assert.IsInstanceOfType<IUnboundedStartRange<int>>(unsorted[0]);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5),   unsorted[1]);
        Assert.AreEqual(Int32Range.CreateFinite(20, 30), unsorted[2]);
    }

    [TestMethod]
    public void LowerBoundComparer_InclusiveBeforeExclusiveAtSameValue()
    {
        // Continuous ranges can share a lower-bound value with different inclusiveness.
        // [5, ...) sorts before (5, ...).
        var unsorted = new List<DecimalRange>
        {
            DecimalRange.CreateFinite(5m, 10m, startInclusive: false, endInclusive: true),
            DecimalRange.CreateFinite(5m, 10m, startInclusive: true,  endInclusive: true)
        };

        unsorted.Sort(RangeSet<DecimalRange, decimal>.LowerBoundComparer);

        Assert.IsTrue(((IFiniteRange<decimal>)unsorted[0]).StartInclusive);
        Assert.IsFalse(((IFiniteRange<decimal>)unsorted[1]).StartInclusive);
    }

    [TestMethod]
    public void LowerBoundComparer_NullsSortFirst()
    {
        // The comparer accepts nulls (sorts them first) for safety when used with sorting
        // APIs that may encounter null elements. A lambda bridge sidesteps the
        // IComparer<T> / IComparer<T?> variance annotation mismatch on the Sort overload.
        var list = new List<Int32Range?>
        {
            Int32Range.CreateFinite(1, 5),
            null,
            Int32Range.CreateFinite(10, 15)
        };

        list.Sort((x, y) => RangeLowerBoundComparer<Int32Range, int>.Instance.Compare(x, y));

        Assert.IsNull(list[0]);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5),   list[1]);
        Assert.AreEqual(Int32Range.CreateFinite(10, 15), list[2]);
    }

    // -------------------------------------------------------------------------
    // Quoted-bound parsing — regression coverage for the UnquoteValue fix
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Parse_QuotedIntegerBounds_ParsesCorrectly()
    {
        // PostgreSQL allows quoting bounds to embed commas, brackets, or other characters
        // that would confuse the parser. This exercises the common UnquoteValue path
        // (outer-quote stripping). Backslash-escape unescaping (\" → ", \\ → \) is the
        // additional behavior added alongside this test; it only matters for custom range
        // types whose bound stringification can contain quotes or backslashes, so it is
        // not exercised directly here.
        var result = Int32Range.Parse("[\"1\",\"10\"]", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<Int32Range.Finite>(result);
        Assert.AreEqual(1,  finite.Start);
        Assert.AreEqual(10, finite.End);
    }
}

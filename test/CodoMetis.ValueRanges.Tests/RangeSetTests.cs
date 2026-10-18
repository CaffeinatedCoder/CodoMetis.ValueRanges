using CodoMetis.ValueRanges.Core;
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;
using DecimalRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DecimalRange, decimal>;

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

    /// <summary>
    /// The span overload and the sequence overload are separate entry points into
    /// normalization; both must land on the same invariant. The sequence side is fed through
    /// a lazy <c>Select</c> so it cannot take the materialized-collection fast path.
    /// </summary>
    [TestMethod]
    public void From_SpanAndSequenceOverloads_NormalizeIdentically()
    {
        Int32Range[] input =
        [
            Int32Range.CreateFinite(10, 12),
            Int32Range.Empty,
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(2, 5)
        ];

        var fromSpan     = IntSet.From(input.AsSpan());
        var fromSequence = IntSet.From(input.Select(range => range));

        Assert.AreEqual(fromSequence, fromSpan);
        Assert.AreEqual(2, fromSpan.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5), fromSpan[0]);
        Assert.AreEqual(Int32Range.CreateFinite(10, 12), fromSpan[1]);
    }

    // -------------------------------------------------------------------------
    // Collection expressions
    // -------------------------------------------------------------------------

    /// <summary>
    /// A collection expression runs through <c>RangeSet.Create</c>, so it must normalize
    /// exactly like <c>From</c> rather than wrapping the elements as written: these overlap
    /// and arrive out of order.
    /// </summary>
    [TestMethod]
    public void CollectionExpression_Normalizes()
    {
        IntSet set = [Int32Range.CreateFinite(10, 12), Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(2, 5)];

        Assert.AreEqual(2, set.Count);
        Assert.AreEqual(Int32Range.CreateFinite(1, 5), set[0]);
        Assert.AreEqual(Int32Range.CreateFinite(10, 12), set[1]);
    }

    [TestMethod]
    public void CollectionExpression_Empty_ReturnsEmptySingleton()
    {
        IntSet set = [];

        Assert.AreSame(IntSet.Empty, set);
    }

    [TestMethod]
    public void CollectionExpression_ContainsInfinity_ReturnsInfiniteSingleton()
    {
        IntSet set = [Int32Range.CreateFinite(1, 5), Int32Range.Infinite];

        Assert.AreSame(IntSet.Infinite, set);
    }

    [TestMethod]
    public void CollectionExpression_OverDecimalRanges_UsesTheSameBuilder()
    {
        DecimalRangeSet set = [DecimalRange.CreateFinite(1.5m, 2.5m), DecimalRange.CreateFinite(2.0m, 3.0m)];

        Assert.AreEqual(1, set.Count);
        Assert.AreEqual(DecimalRange.CreateFinite(1.5m, 3.0m), set[0]);
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
        var result = DecimalRangeSet.From([
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
        var result = DecimalRangeSet.From([
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

    /// <summary>
    /// The invariant says pairwise non-adjacent, and an unbounded element is not exempt.
    /// Normalization sorts by lower bound, so an unbounded-start element is always the *left*
    /// operand of the neighbour check — the direction that used to answer false unconditionally.
    /// </summary>
    [TestMethod]
    public void From_UnboundedStartAdjacentToItsSuccessor_Merges()
    {
        var result = IntSet.From([
            Int32Range.CreateUnboundedStart(0, true),  // (-∞, 0]
            Int32Range.CreateFinite(1, 3)              // [1, 3]
        ]);

        Assert.AreEqual(1, result.Count, $"expected a single merged element, got '{result}'");
        Assert.AreEqual(Int32Range.CreateUnboundedStart(3, true), result[0]);
    }

    [TestMethod]
    public void From_UnboundedStartMeetingUnboundedEnd_CollapsesToInfinite()
    {
        var result = IntSet.From([
            Int32Range.CreateUnboundedStart(0, true),  // (-∞, 0]
            Int32Range.CreateUnboundedEnd(1)           // [1, +∞)
        ]);

        Assert.AreSame(IntSet.Infinite, result, $"the two halves cover every int, got '{result}'");
    }

    /// <summary>
    /// A set unioned with its own complement is the whole domain — the identity that fails
    /// loudest when normalization leaves an adjacent pair unmerged.
    /// </summary>
    [TestMethod]
    public void Union_WithOwnComplement_IsTheInfiniteSet()
    {
        var blocks = IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(20, 22)]);

        var covered = blocks.Union(blocks.Complement());

        Assert.AreSame(IntSet.Infinite, covered, $"got '{covered}'");
        Assert.IsTrue(covered.IsInfinity());
    }

    /// <summary>
    /// Equality is over normalized form, so two spellings of the same set must not depend on
    /// which construction path built them.
    /// </summary>
    [TestMethod]
    public void From_AdjacentUnboundedHalves_EqualsTheInfiniteSet()
    {
        var built = IntSet.From([
            Int32Range.CreateUnboundedStart(0, true),
            Int32Range.CreateUnboundedEnd(1)
        ]);

        Assert.AreEqual(IntSet.Infinite, built);
        Assert.AreEqual(IntSet.Infinite.GetHashCode(), built.GetHashCode());
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

    /// <summary>
    /// The infinite set contains every value. Its single element has no lower bound to
    /// binary-search on, and every other query on the set guards that case first — this one did
    /// not, so it threw where <c>Int32Range.Infinite.Contains(value)</c> answers true.
    /// </summary>
    [TestMethod]
    public void Contains_Value_OnTheInfiniteSet_IsAlwaysTrue()
    {
        Assert.IsTrue(IntSet.Infinite.Contains(0));
        Assert.IsTrue(IntSet.Infinite.Contains(int.MinValue));
        Assert.IsTrue(IntSet.Infinite.Contains(int.MaxValue));

        // Which is what the single-range overload has always answered.
        Assert.IsTrue(Int32Range.Infinite.Contains(0));

        // And the set built from an infinity element, which normalizes to the same singleton.
        Assert.IsTrue(IntSet.From([Int32Range.CreateFinite(1, 3), Int32Range.Infinite]).Contains(99));
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

    // On a continuous domain an operand can stop exactly where a later element starts without
    // sharing a value with it, while an earlier element does share values. Choosing the candidate
    // by the bound value alone picked that later element and answered false (#106); PostgreSQL's
    // && answers true. A discrete domain cannot reach this: [3,5) canonicalizes to [3,4].

    [TestMethod]
    public void Overlaps_ContinuousRangeEndingWhereALaterElementStarts_ReturnsTrue()
    {
        var set = DecimalRangeSet.From([DecimalRange.CreateFinite(1m, 4m), DecimalRange.CreateFinite(5m, 8m)]);

        Assert.IsTrue(set.Overlaps(DecimalRange.CreateFinite(3m, 5m)), "[3,5) shares [3,4) with the first element");
        Assert.IsTrue(set.Overlaps(DecimalRange.CreateUnboundedStart(5m, false)), "(,5) covers the first element");
        Assert.IsTrue(set.Overlaps(DecimalRangeSet.From([DecimalRange.CreateFinite(3m, 5m)])), "the set overload probes the same way");
        Assert.IsTrue(set.Overlaps(DecimalRange.CreateFinite(3m, 5m, false, true)), "(3,5] shares 5 with the second element");
    }

    [TestMethod]
    public void Overlaps_ContinuousRangeBetweenElements_ReturnsFalse()
    {
        var set = DecimalRangeSet.From([DecimalRange.CreateFinite(1m, 4m), DecimalRange.CreateFinite(5m, 8m)]);

        Assert.IsFalse(set.Overlaps(DecimalRange.CreateFinite(4m, 5m)), "[4,5) sits in the gap");
        Assert.IsFalse(set.Overlaps(DecimalRange.CreateFinite(4m, 5m, true, false)));
        Assert.IsFalse(set.Overlaps(DecimalRange.CreateFinite(0m, 1m)), "[0,1) stops where the first element starts");
        Assert.IsFalse(set.Overlaps(DecimalRange.CreateUnboundedStart(1m, false)));
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

    // -------------------------------------------------------------------------
    // Continuous bound ties (found by the 10.0.0 mutation run)
    // -------------------------------------------------------------------------

    /// <summary>
    /// At equal lower values an inclusive bound sorts before an exclusive one — it starts earlier,
    /// by the one point it claims. Discrete types canonicalize every lower bound to inclusive, so only
    /// a continuous type reaches this tie, and the small-model multirange oracle (discrete only)
    /// cannot; the 10.0.0 mutation run flipped it with every unit test green.
    /// </summary>
    [TestMethod]
    public void LowerBoundComparer_AtAContinuousTie_PutsTheInclusiveStartFirst()
    {
        var comparer  = RangeLowerBoundComparer<DecimalRange, decimal>.Instance;
        var inclusive = DecimalRange.CreateFinite(1m, 2m, true, false);
        var exclusive = DecimalRange.CreateFinite(1m, 2m, false, false);

        Assert.IsLessThan(0, comparer.Compare(inclusive, exclusive));
        Assert.IsGreaterThan(0, comparer.Compare(exclusive, inclusive));
    }

    /// <summary>
    /// The merge-join behind <c>Except</c> skips the subtrahend's elements that end strictly before
    /// the current element starts. <c>[0,1)</c> does end strictly before <c>[1,5]</c> — it leaves 1
    /// out — and if it is not skipped it stalls the join, which stops at the first non-overlapping
    /// element and so never reaches <c>[2,3]</c>. Discrete types cannot reach this: canonical form
    /// makes both bounds of the meeting point inclusive.
    /// </summary>
    [TestMethod]
    public void Except_ContinuousElementEndingExclusivelyAtTheStart_DoesNotStallTheJoin()
    {
        var set   = DecimalRangeSet.From([DecimalRange.CreateFinite(1m, 5m, true, true)]);
        var other = DecimalRangeSet.From([DecimalRange.CreateFinite(0m, 1m, true, false), DecimalRange.CreateFinite(2m, 3m, true, true)]);

        var difference = set.Except(other);

        Assert.AreEqual(DecimalRangeSet.From([DecimalRange.CreateFinite(1m, 2m, true, false), DecimalRange.CreateFinite(3m, 5m, false, true)]), difference);
    }

    /// <summary>
    /// Equal sets must hash equally, which a constant would satisfy; distinct sets should not, or every
    /// dictionary and hash set keyed by a multirange degrades to a list. Over a handful of distinct
    /// sets a content-blind hash collides on every pair, a content hash on essentially none.
    /// </summary>
    [TestMethod]
    public void GetHashCode_DependsOnTheElements()
    {
        IntSet[] sets =
        [
            IntSet.From([Int32Range.CreateFinite(1, 5)]),
            IntSet.From([Int32Range.CreateFinite(1, 6)]),
            IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(7, 9)]),
            IntSet.From([Int32Range.CreateUnboundedEnd(3)]),
        ];

        Assert.AreEqual(sets.Length, sets.Select(set => set.GetHashCode()).Distinct().Count());
    }
}

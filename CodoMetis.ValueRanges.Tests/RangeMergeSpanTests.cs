using CodoMetis.ValueRanges.Core;
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Covers <c>Merge</c> — the PostgreSQL <c>range_merge</c> equivalent: the smallest single
/// range containing both operands, spanning any gap between them. Distinct from
/// <c>Union</c>, which keeps disjoint operands as separate set elements.
/// </summary>
[TestClass]
public class RangeMergeSpanTests
{
    // -------------------------------------------------------------------------
    // Finite × Finite
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Merge_OverlappingFinite_ReturnsHull()
    {
        var result = Int32Range.CreateFinite(1, 5).Merge(Int32Range.CreateFinite(3, 9));

        var finite = (IFiniteRange<int>)result;
        Assert.AreEqual(1, finite.Start);
        Assert.AreEqual(9, finite.End);
    }

    [TestMethod]
    public void Merge_DisjointFinite_SpansTheGap()
    {
        var result = Int32Range.CreateFinite(1, 3).Merge(Int32Range.CreateFinite(10, 12));

        var finite = (IFiniteRange<int>)result;
        Assert.AreEqual(1, finite.Start);
        Assert.AreEqual(12, finite.End);
    }

    [TestMethod]
    public void Merge_ContainedOperand_ReturnsOuter()
    {
        var outer = Int32Range.CreateFinite(1, 10);

        Assert.AreEqual(outer, outer.Merge(Int32Range.CreateFinite(3, 5)));
    }

    [TestMethod]
    public void Merge_Continuous_MostPermissiveInclusivenessWins()
    {
        // [1, 5) merged with (1, 9] — equal lower bounds: inclusive wins; upper: later wins.
        var result = DecimalRange.CreateFinite(1m, 5m, true, false)
                                 .Merge(DecimalRange.CreateFinite(1m, 9m, false, true));

        var finite = (IFiniteRange<decimal>)result;
        Assert.AreEqual(1m, finite.Start);
        Assert.IsTrue(finite.StartInclusive);
        Assert.AreEqual(9m, finite.End);
        Assert.IsTrue(finite.EndInclusive);
    }

    // -------------------------------------------------------------------------
    // Empty and Infinity operands
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Merge_EmptyOperands_AreIgnored()
    {
        var range = Int32Range.CreateFinite(1, 5);

        Assert.AreEqual(range, range.Merge(Int32Range.Empty));
        Assert.AreEqual(range, Int32Range.Empty.Merge(range));
        Assert.AreEqual(Int32Range.Empty, Int32Range.Empty.Merge(Int32Range.Empty));
    }

    [TestMethod]
    public void Merge_InfinityOperand_ReturnsInfinite()
    {
        Assert.AreEqual(Int32Range.Infinite, Int32Range.CreateFinite(1, 5).Merge(Int32Range.Infinite));
        Assert.AreEqual(Int32Range.Infinite, Int32Range.Infinite.Merge(Int32Range.CreateFinite(1, 5)));
    }

    // -------------------------------------------------------------------------
    // Unbounded shapes
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Merge_UnboundedStartAndFinite_KeepsUnboundedStart()
    {
        var result = Int32Range.CreateUnboundedStart(3, true).Merge(Int32Range.CreateFinite(10, 12));

        var shape = (IUnboundedStartRange<int>)result;
        Assert.AreEqual(12, shape.End);
    }

    [TestMethod]
    public void Merge_FiniteAndUnboundedEnd_KeepsUnboundedEnd()
    {
        var result = Int32Range.CreateFinite(1, 3).Merge(Int32Range.CreateUnboundedEnd(10));

        var shape = (IUnboundedEndRange<int>)result;
        Assert.AreEqual(1, shape.Start);
    }

    [TestMethod]
    public void Merge_UnboundedStartAndUnboundedEnd_EvenDisjoint_ReturnsInfinite()
    {
        // (,3] and [10,) leave a gap — the hull still spans the whole domain.
        var result = Int32Range.CreateUnboundedStart(3, true).Merge(Int32Range.CreateUnboundedEnd(10));

        Assert.AreEqual(Int32Range.Infinite, result);
    }

    // -------------------------------------------------------------------------
    // RangeSet.Merge
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeSetMerge_EmptySet_ReturnsEmptyRange()
    {
        Assert.AreEqual(Int32Range.Empty, IntSet.Empty.Merge());
    }

    [TestMethod]
    public void RangeSetMerge_SingleElement_ReturnsThatElement()
    {
        var range = Int32Range.CreateFinite(1, 5);

        Assert.AreEqual(range, IntSet.From([range]).Merge());
    }

    [TestMethod]
    public void RangeSetMerge_MultipleElements_SpansFirstToLast()
    {
        var set = IntSet.From([
            Int32Range.CreateFinite(20, 22),
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(10, 12)
        ]);

        var finite = (IFiniteRange<int>)set.Merge();
        Assert.AreEqual(1, finite.Start);
        Assert.AreEqual(22, finite.End);
    }

    [TestMethod]
    public void RangeSetMerge_InfiniteSet_ReturnsInfinite()
    {
        Assert.AreEqual(Int32Range.Infinite, IntSet.Infinite.Merge());
    }

    [TestMethod]
    public void RangeSetMerge_UnboundedEdges_ProduceInfinite()
    {
        var set = IntSet.From([
            Int32Range.CreateUnboundedStart(3, true),
            Int32Range.CreateUnboundedEnd(10)
        ]);

        Assert.AreEqual(Int32Range.Infinite, set.Merge());
    }
}

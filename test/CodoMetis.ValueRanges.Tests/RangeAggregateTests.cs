using CodoMetis.ValueRanges.Core;
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Covers <c>RangeAgg</c> and <c>RangeIntersectAgg</c> — the in-memory counterparts of the
/// PostgreSQL <c>range_agg</c> and <c>range_intersect_agg</c> aggregates.
/// </summary>
[TestClass]
public class RangeAggregateTests
{
    // -------------------------------------------------------------------------
    // RangeAgg
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeAgg_MergesOverlapsAndKeepsDisjointElements()
    {
        var result = new[]
        {
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(3, 8),   // overlaps the first — merged
            Int32Range.CreateFinite(20, 25)  // disjoint — kept separate
        }.RangeAgg();

        Assert.AreEqual(IntSet.From([Int32Range.CreateFinite(1, 8), Int32Range.CreateFinite(20, 25)]), result);
    }

    [TestMethod]
    public void RangeAgg_EmptySource_ReturnsEmptySet()
    {
        Assert.AreEqual(IntSet.Empty, Array.Empty<Int32Range>().RangeAgg());
    }

    [TestMethod]
    public void RangeAgg_DropsEmptyInputs()
    {
        var result = new[] { Int32Range.Empty, Int32Range.CreateFinite(1, 3) }.RangeAgg();

        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void RangeAgg_WorksForContinuousTypes()
    {
        var result = new[]
        {
            DecimalRange.CreateFinite(1m, 5m),
            DecimalRange.CreateFinite(5m, 9m) // adjacent at 5 with complementary inclusiveness
        }.RangeAgg();

        Assert.AreEqual(1, result.Count);
        var merged = (IFiniteRange<decimal>)result[0];
        Assert.AreEqual(1m, merged.Start);
        Assert.AreEqual(9m, merged.End);
    }

    // -------------------------------------------------------------------------
    // RangeIntersectAgg
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeIntersectAgg_ReturnsCommonIntersection()
    {
        var result = new[]
        {
            Int32Range.CreateFinite(1, 10),
            Int32Range.CreateFinite(5, 15),
            Int32Range.CreateFinite(0, 8)
        }.RangeIntersectAgg();

        Assert.AreEqual(Int32Range.CreateFinite(5, 8), result);
    }

    [TestMethod]
    public void RangeIntersectAgg_DisjointInputs_ReturnsEmptyRange()
    {
        var result = new[]
        {
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(10, 12)
        }.RangeIntersectAgg();

        Assert.AreEqual(Int32Range.Empty, result);
    }

    [TestMethod]
    public void RangeIntersectAgg_EmptySource_ReturnsNull()
    {
        Assert.IsNull(Array.Empty<Int32Range>().RangeIntersectAgg());
    }

    [TestMethod]
    public void RangeIntersectAgg_SingleElement_ReturnsThatElement()
    {
        var range = Int32Range.CreateFinite(1, 5);

        Assert.AreEqual(range, new[] { range }.RangeIntersectAgg());
    }

    [TestMethod]
    public void RangeIntersectAgg_InfinityIsNeutral()
    {
        var range = Int32Range.CreateFinite(1, 5);

        Assert.AreEqual(range, new[] { Int32Range.Infinite, range }.RangeIntersectAgg());
    }
}

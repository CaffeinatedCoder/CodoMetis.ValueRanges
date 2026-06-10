using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeUnionTests
{
    [TestMethod]
    public void Union_TwoFiniteRanges_PartialOverlap_ReturnsSingleSpanningRange()
    {
        var r1 = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]
        var r2 = Int32Range.CreateFinite(5, 15, true, true); // [5, 15]

        var result = r1.Union(r2);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1,  merged.Start);
        Assert.AreEqual(15, merged.End);
        Assert.IsTrue(merged.StartInclusive);
        Assert.IsTrue(merged.EndInclusive);
    }

    [TestMethod]
    public void Union_TwoFiniteRanges_MixedInclusiveness_PreservesMorePermissiveBounds()
    {
        var r1 = Int32Range.CreateFinite(1, 10, false, true);  // (1, 10]
        var r2 = Int32Range.CreateFinite(1, 10, true,  false); // [1, 10)

        var result = r1.Union(r2);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1,  merged.Start);
        Assert.AreEqual(10, merged.End);
        Assert.IsTrue(merged.StartInclusive); // more permissive
        Assert.IsTrue(merged.EndInclusive); // more permissive
    }

    [TestMethod]
    public void Union_DiscreteAdjacentRanges_ReturnsSingleContiguousRange()
    {
        var r1 = Int32Range.CreateFinite(1, 5,  true, true); // [1, 5]
        var r2 = Int32Range.CreateFinite(6, 10, true, true); // [6, 10]

        var result = r1.Union(r2);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1,  merged.Start);
        Assert.AreEqual(10, merged.End);
    }

    [TestMethod]
    public void Union_ContinuousAdjacentRanges_XorInclusiveness_ReturnsSingleContiguousRange()
    {
        var r1 = DecimalRange.CreateFinite(1m, 5m,  true, false); // [1, 5)
        var r2 = DecimalRange.CreateFinite(5m, 10m, true, true);  // [5, 10]

        var result = r1.Union(r2);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IFiniteRange<decimal>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(1m,  merged.Start);
        Assert.AreEqual(10m, merged.End);
        Assert.IsTrue(merged.StartInclusive);
        Assert.IsTrue(merged.EndInclusive);
    }

    [TestMethod]
    public void Union_Disjoint_NonAdjacent_ReturnsTwoElementSet()
    {
        var r1 = Int32Range.CreateFinite(1, 5);
        var r2 = Int32Range.CreateFinite(7, 10);

        var result   = r1.Union(r2);
        var reversed = r2.Union(r1);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(r1, result[0]);
        Assert.AreEqual(r2, result[1]);

        // Order is normalized regardless of operand order.
        Assert.AreEqual(result, reversed);
    }

    [TestMethod]
    public void Union_WithEmpty_ReturnsSingleElementSet()
    {
        var r1 = Int32Range.CreateFinite(1, 5);

        var result = r1.Union(Int32Range.Empty);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(r1, result[0]);
    }

    [TestMethod]
    public void Union_BothEmpty_ReturnsEmptySet()
    {
        var result = Int32Range.Empty.Union(Int32Range.Empty);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Union_WithInfinity_ReturnsInfiniteSet()
    {
        var r1 = Int32Range.CreateFinite(1, 5);

        var result = r1.Union(Int32Range.Infinite);

        Assert.AreEqual(1, result.Count);
        Assert.IsInstanceOfType<IInfinityRange<int>>(result[0]);
    }

    [TestMethod]
    public void Union_OpenStartAndFinite_ReturnsOpenStartAtLaterEnd()
    {
        var openStart = Int32Range.CreateUnboundedStart(5, true);   // (-∞, 5]
        var finite    = Int32Range.CreateFinite(2, 10, true, true); // [2, 10]

        var result = openStart.Union(finite);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IUnboundedStartRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(10, merged.End);
        Assert.IsTrue(merged.EndInclusive);
    }

    [TestMethod]
    public void Union_OpenEndAndFinite_ReturnsOpenEndAtEarlierStart()
    {
        var openEnd = Int32Range.CreateUnboundedEnd(8, true);     // [8, ∞)
        var finite  = Int32Range.CreateFinite(3, 12, true, true); // [3, 12]

        var result = openEnd.Union(finite);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IUnboundedEndRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(3, merged.Start);
        Assert.IsTrue(merged.StartInclusive);
    }

    [TestMethod]
    public void Union_TwoOpenStart_ReturnsOpenStartAtLaterEnd()
    {
        var s1 = Int32Range.CreateUnboundedStart(5,  true);  // (-∞, 5]
        var s2 = Int32Range.CreateUnboundedStart(10, false); // (-∞, 10) ≡ (-∞, 9]

        var result = s1.Union(s2);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IUnboundedStartRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(9, merged.End);  // canonical: (-∞, 10) ≡ (-∞, 9]
        Assert.IsTrue(merged.EndInclusive);
    }

    [TestMethod]
    public void Union_TwoOpenEnd_ReturnsOpenEndAtEarlierStart()
    {
        var e1 = Int32Range.CreateUnboundedEnd(3, false); // (3, ∞) ≡ [4, ∞)
        var e2 = Int32Range.CreateUnboundedEnd(7, true);  // [7, ∞)

        var result = e1.Union(e2);

        Assert.AreEqual(1, result.Count);
        var merged = result[0] as IUnboundedEndRange<int>;
        Assert.IsNotNull(merged);
        Assert.AreEqual(4, merged.Start);  // canonical: (3, ∞) ≡ [4, ∞)
        Assert.IsTrue(merged.StartInclusive);
    }

    [TestMethod]
    public void Union_OverlappingOpenStartAndOpenEnd_ReturnsInfinity()
    {
        var openStart = Int32Range.CreateUnboundedStart(5, true);
        var openEnd   = Int32Range.CreateUnboundedEnd(3, true);

        var result   = openStart.Union(openEnd);
        var reversed = openEnd.Union(openStart);

        Assert.AreEqual(1, result.Count);
        Assert.IsInstanceOfType<IInfinityRange<int>>(result[0]);
        Assert.AreEqual(result, reversed);
    }

    [TestMethod]
    public void Union_DisjointOpenStartAndOpenEnd_ReturnsTwoElementSet()
    {
        // (-∞, 5] ∪ [10, ∞) — disjoint with the gap [6, 9]; previously swallowed by the
        // Empty sentinel, now correctly represented as a two-element set.
        var openStart = Int32Range.CreateUnboundedStart(5, true); // (-∞, 5]
        var openEnd   = Int32Range.CreateUnboundedEnd(10, true);  // [10, ∞)
        
        var result = openStart.Union(openEnd);

        Assert.AreEqual(2, result.Count);
        Assert.IsInstanceOfType<IUnboundedStartRange<int>>(result[0]);
        Assert.IsInstanceOfType<IUnboundedEndRange<int>>(result[1]);
    }
}

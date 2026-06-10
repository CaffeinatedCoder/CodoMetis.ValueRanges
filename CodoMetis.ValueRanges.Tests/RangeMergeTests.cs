using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeMergeTests
{
    [TestMethod]
    public void Merge_TwoFiniteRanges_PartialOverlap_ReturnsSpanningRange()
    {
        var r1 = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]
        var r2 = Int32Range.CreateFinite(5, 15, true, true); // [5, 15]

        var result = r1.Merge(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1,  result.Start);
        Assert.AreEqual(15, result.End);
        Assert.IsTrue(result.StartInclusive);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Merge_TwoFiniteRanges_MixedInclusiveness_PreservesMorePermissiveBounds()
    {
        var r1 = Int32Range.CreateFinite(1, 10, false, true);  // (1, 10]
        var r2 = Int32Range.CreateFinite(1, 10, true,  false); // [1, 10)

        var result = r1.Merge(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1,  result.Start);
        Assert.AreEqual(10, result.End);
        Assert.IsTrue(result.StartInclusive); // more permissive
        Assert.IsTrue(result.EndInclusive); // more permissive
    }

    [TestMethod]
    public void Merge_DiscreteAdjacentRanges_ReturnsContiguousRange()
    {
        var r1 = Int32Range.CreateFinite(1, 5,  true, true); // [1, 5]
        var r2 = Int32Range.CreateFinite(6, 10, true, true); // [6, 10]

        var result = r1.Merge(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1,  result.Start);
        Assert.AreEqual(10, result.End);
    }

    [TestMethod]
    public void Merge_ContinuousAdjacentRanges_XorInclusiveness_ReturnsContiguousRange()
    {
        var r1 = DecimalRange.CreateFinite(1m, 5m,  true, false); // [1, 5)
        var r2 = DecimalRange.CreateFinite(5m, 10m, true, true);  // [5, 10]

        var result = r1.Merge(r2) as IFiniteRange<decimal>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1m,  result.Start);
        Assert.AreEqual(10m, result.End);
        Assert.IsTrue(result.StartInclusive);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Merge_Disjoint_NonAdjacent_ReturnsEmpty()
    {
        var r1 = Int32Range.CreateFinite(1, 5);
        var r2 = Int32Range.CreateFinite(7, 10);

        Assert.IsInstanceOfType<IEmptyRange<int>>(r1.Merge(r2));
        Assert.IsInstanceOfType<IEmptyRange<int>>(r2.Merge(r1));
    }

    [TestMethod]
    public void Merge_OpenStartAndFinite_ReturnsOpenStartAtLaterEnd()
    {
        var openStart = Int32Range.CreateOpenStart(5, true);    // (-∞, 5]
        var finite    = Int32Range.CreateFinite(2, 10, true, true); // [2, 10]

        var result = openStart.Merge(finite) as IUnboundedStartRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(10, result.End);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Merge_OpenEndAndFinite_ReturnsOpenEndAtEarlierStart()
    {
        var openEnd = Int32Range.CreateOpenEnd(8, true);      // [8, ∞)
        var finite  = Int32Range.CreateFinite(3, 12, true, true); // [3, 12]

        var result = openEnd.Merge(finite) as IUnboundedEndRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Start);
        Assert.IsTrue(result.StartInclusive);
    }

    [TestMethod]
    public void Merge_TwoOpenStart_ReturnsOpenStartAtLaterEnd()
    {
        var s1 = Int32Range.CreateOpenStart(5,  true);  // (-∞, 5]
        var s2 = Int32Range.CreateOpenStart(10, false); // (-∞, 10) ≡ (-∞, 9]

        var result = s1.Merge(s2) as IUnboundedStartRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(9, result.End);  // canonical: (-∞, 10) ≡ (-∞, 9]
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Merge_TwoOpenEnd_ReturnsOpenEndAtEarlierStart()
    {
        var e1 = Int32Range.CreateOpenEnd(3, false); // (3, ∞) ≡ [4, ∞)
        var e2 = Int32Range.CreateOpenEnd(7, true);  // [7, ∞)

        var result = e1.Merge(e2) as IUnboundedEndRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(4, result.Start);  // canonical: (3, ∞) ≡ [4, ∞)
        Assert.IsTrue(result.StartInclusive);
    }

    [TestMethod]
    public void Merge_OpenStartAndOpenEnd_ReturnsInfinity()
    {
        var openStart = Int32Range.CreateOpenStart(5, true);
        var openEnd   = Int32Range.CreateOpenEnd(3, true);

        Assert.IsInstanceOfType<IInfinityRange<int>>(openStart.Merge(openEnd));
        Assert.IsInstanceOfType<IInfinityRange<int>>(openEnd.Merge(openStart));
    }

    [TestMethod]
    public void Union_IsIdenticalToMerge()
    {
        var r1 = Int32Range.CreateFinite(1, 10, true, true);
        var r2 = Int32Range.CreateFinite(5, 15, true, true);

        var mergeResult = r1.Merge(r2);
        var unionResult = r1.Union(r2);

        Assert.AreEqual(mergeResult, unionResult);
    }
}
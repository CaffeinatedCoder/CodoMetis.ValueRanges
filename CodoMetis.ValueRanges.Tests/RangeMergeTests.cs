namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeMergeTests
{
    [TestMethod]
    public void Merge_TwoFiniteRanges_PartialOverlap_ReturnsSpanningRange()
    {
        var r1 = Int32Range.Closed(1, 10, true, true); // [1, 10]
        var r2 = Int32Range.Closed(5, 15, true, true); // [5, 15]

        var result = r1.Merge(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1,  result.LowerBound);
        Assert.AreEqual(15, result.UpperBound);
        Assert.IsTrue(result.LowerBoundInclusive);
        Assert.IsTrue(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Merge_TwoFiniteRanges_MixedInclusiveness_PreservesMorePermissiveBounds()
    {
        var r1 = Int32Range.Closed(1, 10, false, true);  // (1, 10]
        var r2 = Int32Range.Closed(1, 10, true,  false); // [1, 10)

        var result = r1.Merge(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1,  result.LowerBound);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsTrue(result.LowerBoundInclusive); // more permissive
        Assert.IsTrue(result.UpperBoundInclusive); // more permissive
    }

    [TestMethod]
    public void Merge_DiscreteAdjacentRanges_ReturnsContiguousRange()
    {
        var r1 = Int32Range.Closed(1, 5,  true, true); // [1, 5]
        var r2 = Int32Range.Closed(6, 10, true, true); // [6, 10]

        var result = r1.Merge(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1,  result.LowerBound);
        Assert.AreEqual(10, result.UpperBound);
    }

    [TestMethod]
    public void Merge_ContinuousAdjacentRanges_XorInclusiveness_ReturnsContiguousRange()
    {
        var r1 = DecimalRange.Closed(1m, 5m,  true, false); // [1, 5)
        var r2 = DecimalRange.Closed(5m, 10m, true, true);  // [5, 10]

        var result = r1.Merge(r2) as IFiniteRange<decimal>;

        Assert.IsNotNull(result);
        Assert.AreEqual(1m,  result.LowerBound);
        Assert.AreEqual(10m, result.UpperBound);
        Assert.IsTrue(result.LowerBoundInclusive);
        Assert.IsTrue(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Merge_Disjoint_NonAdjacent_ReturnsNull()
    {
        var r1 = Int32Range.Closed(1, 5);
        var r2 = Int32Range.Closed(7, 10);

        Assert.IsNull(r1.Merge(r2));
        Assert.IsNull(r2.Merge(r1));
    }

    [TestMethod]
    public void Merge_OpenStartAndFinite_ReturnsOpenStartAtLaterEnd()
    {
        var openStart = Int32Range.WithOpenStart(5, true);    // (-∞, 5]
        var finite    = Int32Range.Closed(2, 10, true, true); // [2, 10]

        var result = openStart.Merge(finite) as IOpenStartRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsTrue(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Merge_OpenEndAndFinite_ReturnsOpenEndAtEarlierStart()
    {
        var openEnd = Int32Range.WithOpenEnd(8, true);      // [8, ∞)
        var finite  = Int32Range.Closed(3, 12, true, true); // [3, 12]

        var result = openEnd.Merge(finite) as IOpenEndRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.LowerBound);
        Assert.IsTrue(result.LowerBoundInclusive);
    }

    [TestMethod]
    public void Merge_TwoOpenStart_ReturnsOpenStartAtLaterEnd()
    {
        var s1 = Int32Range.WithOpenStart(5,  true);
        var s2 = Int32Range.WithOpenStart(10, false);

        var result = s1.Merge(s2) as IOpenStartRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsFalse(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Merge_TwoOpenEnd_ReturnsOpenEndAtEarlierStart()
    {
        var e1 = Int32Range.WithOpenEnd(3, false);
        var e2 = Int32Range.WithOpenEnd(7, true);

        var result = e1.Merge(e2) as IOpenEndRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.LowerBound);
        Assert.IsFalse(result.LowerBoundInclusive);
    }

    [TestMethod]
    public void Merge_OpenStartAndOpenEnd_ReturnsNull_CannotExpressUnbounded()
    {
        // The union would cover the entire number line; the type system cannot represent this
        var openStart = Int32Range.WithOpenStart(5, true);
        var openEnd   = Int32Range.WithOpenEnd(3, true);

        Assert.IsNull(openStart.Merge(openEnd));
        Assert.IsNull(openEnd.Merge(openStart));
    }

    [TestMethod]
    public void Union_IsIdenticalToMerge()
    {
        var r1 = Int32Range.Closed(1, 10, true, true);
        var r2 = Int32Range.Closed(5, 15, true, true);

        var mergeResult = r1.Merge(r2);
        var unionResult = r1.Union(r2);

        Assert.AreEqual(mergeResult, unionResult);
    }
}
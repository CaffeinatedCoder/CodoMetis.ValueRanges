namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeIntersectTests
{
    [TestMethod]
    public void Intersect_TwoFiniteRanges_PartialOverlap_ReturnsCorrectIntersection()
    {
        var r1 = Int32Range.Closed(1, 10, true, true); // [1, 10]
        var r2 = Int32Range.Closed(5, 15, true, true); // [5, 15]

        var result = r1.Intersect(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.LowerBound);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsTrue(result.LowerBoundInclusive);
        Assert.IsTrue(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Intersect_TwoFiniteRanges_MixedInclusiveness_PreservesStricterBounds()
    {
        var r1 = Int32Range.Closed(1, 10, true,  true);  // [1, 10]
        var r2 = Int32Range.Closed(5, 10, false, false); // (5, 10)

        var result = r1.Intersect(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.LowerBound);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsFalse(result.LowerBoundInclusive); // stricter: exclusive
        Assert.IsFalse(result.UpperBoundInclusive); // stricter: exclusive
    }

    [TestMethod]
    public void Intersect_TwoFiniteRanges_NoOverlap_ReturnsNull()
    {
        var r1 = Int32Range.Closed(1, 5);
        var r2 = Int32Range.Closed(6, 10);

        Assert.IsNull(r1.Intersect(r2));
        Assert.IsNull(r2.Intersect(r1));
    }

    [TestMethod]
    public void Intersect_TwoFiniteRanges_TouchingExclusively_ReturnsNull()
    {
        var r1 = Int32Range.Closed(1, 5,  true,  false); // [1, 5)
        var r2 = Int32Range.Closed(5, 10, false, true);  // (5, 10]

        Assert.IsNull(r1.Intersect(r2));
    }

    [TestMethod]
    public void Intersect_OpenEndAndOpenStart_ReturnsFiniteOverlap()
    {
        var openEnd   = Int32Range.WithOpenEnd(5, true);    // [5, ∞)
        var openStart = Int32Range.WithOpenStart(10, true); // (-∞, 10]

        var result = openEnd.Intersect(openStart) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.LowerBound);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsTrue(result.LowerBoundInclusive);
        Assert.IsTrue(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Intersect_OpenEndAndOpenStart_ExclusiveBoundaries_ReturnsNarrowedFinite()
    {
        var openEnd   = Int32Range.WithOpenEnd(5, false);    // (5, ∞)
        var openStart = Int32Range.WithOpenStart(10, false); // (-∞, 10)

        var result = openEnd.Intersect(openStart) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.LowerBound);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsFalse(result.LowerBoundInclusive);
        Assert.IsFalse(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Intersect_TwoOpenStart_ReturnsOpenStartAtEarlierEnd()
    {
        var s1 = Int32Range.WithOpenStart(10, true);  // (-∞, 10]
        var s2 = Int32Range.WithOpenStart(20, false); // (-∞, 20)

        var result = s1.Intersect(s2) as IOpenStartRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsTrue(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Intersect_TwoOpenEnd_ReturnsOpenEndAtLaterStart()
    {
        var e1 = Int32Range.WithOpenEnd(3, true);  // [3, ∞)
        var e2 = Int32Range.WithOpenEnd(7, false); // (7, ∞)

        var result = e1.Intersect(e2) as IOpenEndRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(7, result.LowerBound);
        Assert.IsFalse(result.LowerBoundInclusive);
    }

    [TestMethod]
    public void Intersect_OpenStartAndFinite_ReturnsFiniteClippedByFinite()
    {
        var openStart = Int32Range.WithOpenStart(10, true);   // (-∞, 10]
        var finite    = Int32Range.Closed(5, 15, true, true); // [5, 15]

        var result = openStart.Intersect(finite) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.LowerBound);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsTrue(result.LowerBoundInclusive);
        Assert.IsTrue(result.UpperBoundInclusive);
    }

    [TestMethod]
    public void Intersect_OpenEndAndFinite_ReturnsFiniteClippedByFinite()
    {
        var openEnd = Int32Range.WithOpenEnd(5, true);      // [5, ∞)
        var finite  = Int32Range.Closed(1, 10, true, true); // [1, 10]

        var result = openEnd.Intersect(finite) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.LowerBound);
        Assert.AreEqual(10, result.UpperBound);
        Assert.IsTrue(result.LowerBoundInclusive);
        Assert.IsTrue(result.UpperBoundInclusive);
    }
}
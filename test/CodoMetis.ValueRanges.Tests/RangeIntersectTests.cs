using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeIntersectTests
{
    [TestMethod]
    public void Intersect_TwoFiniteRanges_PartialOverlap_ReturnsCorrectIntersection()
    {
        var r1 = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]
        var r2 = Int32Range.CreateFinite(5, 15, true, true); // [5, 15]

        var result = r1.Intersect(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.Start);
        Assert.AreEqual(10, result.End);
        Assert.IsTrue(result.StartInclusive);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Intersect_TwoFiniteRanges_MixedInclusiveness_PreservesStricterBounds()
    {
        var r1 = Int32Range.CreateFinite(1, 10, true,  true);  // [1, 10]
        var r2 = Int32Range.CreateFinite(5, 10, false, false); // (5, 10) ≡ [6, 9]

        var result = r1.Intersect(r2) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(6, result.Start); // canonical: (5, …) ≡ [6, …]
        Assert.AreEqual(9, result.End);   // canonical: (…, 10) ≡ […, 9]
        Assert.IsTrue(result.StartInclusive);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Intersect_TwoFiniteRanges_NoOverlap_ReturnsEmpty()
    {
        var r1 = Int32Range.CreateFinite(1, 5);
        var r2 = Int32Range.CreateFinite(6, 10);

        Assert.IsInstanceOfType<IEmptyRange<int>>(r1.Intersect(r2));
        Assert.IsInstanceOfType<IEmptyRange<int>>(r2.Intersect(r1));
    }

    [TestMethod]
    public void Intersect_TwoFiniteRanges_TouchingExclusively_ReturnsEmpty()
    {
        var r1 = Int32Range.CreateFinite(1, 5,  true,  false); // [1, 5)
        var r2 = Int32Range.CreateFinite(5, 10, false, true);  // (5, 10]

        Assert.IsInstanceOfType<IEmptyRange<int>>(r1.Intersect(r2));
    }

    [TestMethod]
    public void Intersect_OpenEndAndOpenStart_ReturnsFiniteOverlap()
    {
        var openEnd   = Int32Range.CreateUnboundedEnd(5, true);    // [5, ∞)
        var openStart = Int32Range.CreateUnboundedStart(10, true); // (-∞, 10]

        var result = openEnd.Intersect(openStart) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.Start);
        Assert.AreEqual(10, result.End);
        Assert.IsTrue(result.StartInclusive);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Intersect_OpenEndAndOpenStart_ExclusiveBoundaries_ReturnsNarrowedFinite()
    {
        var openEnd   = Int32Range.CreateUnboundedEnd(5, false);    // (5, ∞) ≡ [6, ∞)
        var openStart = Int32Range.CreateUnboundedStart(10, false); // (-∞, 10) ≡ (-∞, 9]

        var result = openEnd.Intersect(openStart) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(6, result.Start); // canonical: (5, …) ≡ [6, …]
        Assert.AreEqual(9, result.End);   // canonical: (…, 10) ≡ […, 9]
        Assert.IsTrue(result.StartInclusive);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Intersect_TwoOpenStart_ReturnsOpenStartAtEarlierEnd()
    {
        var s1 = Int32Range.CreateUnboundedStart(10, true);  // (-∞, 10]
        var s2 = Int32Range.CreateUnboundedStart(20, false); // (-∞, 20)

        var result = s1.Intersect(s2) as IUnboundedStartRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(10, result.End);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Intersect_TwoOpenEnd_ReturnsOpenEndAtLaterStart()
    {
        var e1 = Int32Range.CreateUnboundedEnd(3, true);  // [3, ∞)
        var e2 = Int32Range.CreateUnboundedEnd(7, false); // (7, ∞) ≡ [8, ∞)

        var result = e1.Intersect(e2) as IUnboundedEndRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(8, result.Start); // canonical: (7, ∞) ≡ [8, ∞)
        Assert.IsTrue(result.StartInclusive);
    }

    [TestMethod]
    public void Intersect_OpenStartAndFinite_ReturnsFiniteClippedByFinite()
    {
        var openStart = Int32Range.CreateUnboundedStart(10, true);       // (-∞, 10]
        var finite    = Int32Range.CreateFinite(5, 15, true, true); // [5, 15]

        var result = openStart.Intersect(finite) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.Start);
        Assert.AreEqual(10, result.End);
        Assert.IsTrue(result.StartInclusive);
        Assert.IsTrue(result.EndInclusive);
    }

    [TestMethod]
    public void Intersect_OpenEndAndFinite_ReturnsFiniteClippedByFinite()
    {
        var openEnd = Int32Range.CreateUnboundedEnd(5, true);          // [5, ∞)
        var finite  = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]

        var result = openEnd.Intersect(finite) as IFiniteRange<int>;

        Assert.IsNotNull(result);
        Assert.AreEqual(5,  result.Start);
        Assert.AreEqual(10, result.End);
        Assert.IsTrue(result.StartInclusive);
        Assert.IsTrue(result.EndInclusive);
    }
}
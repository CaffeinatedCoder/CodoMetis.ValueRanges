namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeOverlapsTests
{
    [TestMethod]
    public void Overlaps_Finite_OverlappingRanges_ReturnsTrue()
    {
        var r1 = Int32Range.CreateFinite(1, 5,  true, true); // [1, 5]
        var r2 = Int32Range.CreateFinite(4, 10, true, true); // [4, 10]

        Assert.IsTrue(r1.Overlaps(r2));
        Assert.IsTrue(r2.Overlaps(r1));
    }

    [TestMethod]
    public void Overlaps_Finite_TouchingAtBoundary_BothInclusive_ReturnsTrue()
    {
        var r1 = Int32Range.CreateFinite(1, 5,  true, true); // [1, 5]
        var r2 = Int32Range.CreateFinite(5, 10, true, true); // [5, 10]

        Assert.IsTrue(r1.Overlaps(r2));
        Assert.IsTrue(r2.Overlaps(r1));
    }

    [TestMethod]
    public void Overlaps_Finite_TouchingAtBoundary_LeftExclusive_ReturnsFalse()
    {
        var r1 = Int32Range.CreateFinite(1, 5,  true, false); // [1, 5)
        var r2 = Int32Range.CreateFinite(5, 10, true, true);  // [5, 10]

        Assert.IsFalse(r1.Overlaps(r2));
        Assert.IsFalse(r2.Overlaps(r1));
    }

    [TestMethod]
    public void Overlaps_Finite_TouchingAtBoundary_RightExclusive_ReturnsFalse()
    {
        var r1 = Int32Range.CreateFinite(1, 5,  true,  true); // [1, 5]
        var r2 = Int32Range.CreateFinite(5, 10, false, true); // (5, 10]

        Assert.IsFalse(r1.Overlaps(r2));
        Assert.IsFalse(r2.Overlaps(r1));
    }

    [TestMethod]
    public void Overlaps_Finite_TouchingAtBoundary_BothExclusive_ReturnsFalse()
    {
        var r1 = Int32Range.CreateFinite(1, 5,  true,  false); // [1, 5)
        var r2 = Int32Range.CreateFinite(5, 10, false, true);  // (5, 10]

        Assert.IsFalse(r1.Overlaps(r2));
    }

    [TestMethod]
    public void Overlaps_Finite_Disjoint_ReturnsFalse()
    {
        var r1 = Int32Range.CreateFinite(1, 5);
        var r2 = Int32Range.CreateFinite(7, 10);

        Assert.IsFalse(r1.Overlaps(r2));
        Assert.IsFalse(r2.Overlaps(r1));
    }

    [TestMethod]
    public void Overlaps_Empty_AlwaysReturnsFalse()
    {
        var empty  = Int32Range.Empty;
        var finite = Int32Range.CreateFinite(1, 10);

        Assert.IsFalse(empty.Overlaps(finite));
        Assert.IsFalse(finite.Overlaps(empty));
    }

    [TestMethod]
    public void Overlaps_OpenStart_FiniteOverlapping_ReturnsTrue()
    {
        var openStart = Int32Range.CreateUnboundedStart(5, true); // (-∞, 5]
        var finite    = Int32Range.CreateFinite(4, 10);          // [4, 10)

        Assert.IsTrue(openStart.Overlaps(finite));
        Assert.IsTrue(finite.Overlaps(openStart));
    }

    [TestMethod]
    public void Overlaps_OpenStart_FiniteEntirelyRight_ReturnsFalse()
    {
        var openStart = Int32Range.CreateUnboundedStart(5, false);   // (-∞, 5)
        var finite    = Int32Range.CreateFinite(5, 10, true, true); // [5, 10]

        Assert.IsFalse(openStart.Overlaps(finite));
        Assert.IsFalse(finite.Overlaps(openStart));
    }

    [TestMethod]
    public void Overlaps_OpenEnd_FiniteOverlapping_ReturnsTrue()
    {
        var openEnd = Int32Range.CreateUnboundedEnd(5, true); // [5, ∞)
        var finite  = Int32Range.CreateFinite(1, 8);         // [1, 8)

        Assert.IsTrue(openEnd.Overlaps(finite));
        Assert.IsTrue(finite.Overlaps(openEnd));
    }

    [TestMethod]
    public void Overlaps_OpenEnd_FiniteEntirelyLeft_ReturnsFalse()
    {
        var openEnd = Int32Range.CreateUnboundedEnd(5, true);     // [5, ∞)
        var finite  = Int32Range.CreateFinite(1, 4, true, true); // [1, 4]

        Assert.IsFalse(openEnd.Overlaps(finite));
        Assert.IsFalse(finite.Overlaps(openEnd));
    }

    [TestMethod]
    public void Overlaps_TwoOpenStart_AlwaysTrue()
    {
        var s1 = Int32Range.CreateUnboundedStart(5,  true);
        var s2 = Int32Range.CreateUnboundedStart(20, false);

        Assert.IsTrue(s1.Overlaps(s2));
        Assert.IsTrue(s2.Overlaps(s1));
    }

    [TestMethod]
    public void Overlaps_TwoOpenEnd_AlwaysTrue()
    {
        var e1 = Int32Range.CreateUnboundedEnd(1,  true);
        var e2 = Int32Range.CreateUnboundedEnd(20, false);

        Assert.IsTrue(e1.Overlaps(e2));
        Assert.IsTrue(e2.Overlaps(e1));
    }

    [TestMethod]
    public void Overlaps_OpenStartAndOpenEnd_OverlappingRegion_ReturnsTrue()
    {
        var openStart = Int32Range.CreateUnboundedStart(10, true); // (-∞, 10]
        var openEnd   = Int32Range.CreateUnboundedEnd(5, true);    // [5, ∞)

        // They share [5, 10]
        Assert.IsTrue(openStart.Overlaps(openEnd));
        Assert.IsTrue(openEnd.Overlaps(openStart));
    }

    [TestMethod]
    public void Overlaps_OpenStartAndOpenEnd_NoOverlapExclusiveBoundary_ReturnsFalse()
    {
        var openStart = Int32Range.CreateUnboundedStart(5, false); // (-∞, 5)
        var openEnd   = Int32Range.CreateUnboundedEnd(5, false);   // (5, ∞)

        // No shared point
        Assert.IsFalse(openStart.Overlaps(openEnd));
        Assert.IsFalse(openEnd.Overlaps(openStart));
    }
}
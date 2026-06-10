namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeStrictlyLeftOrRightOfTests
{
    [TestMethod]
    public void IsStrictlyLeftOf_ClearGap_ReturnsTrue()
    {
        var left  = Int32Range.CreateFinite(1, 5);
        var right = Int32Range.CreateFinite(7, 10);

        Assert.IsTrue(left.IsStrictlyLeftOf(right));
        Assert.IsTrue(right.IsStrictlyRightOf(left));
    }

    [TestMethod]
    public void IsStrictlyLeftOf_TouchingLeftExclusiveRightInclusive_ReturnsTrue()
    {
        var left  = Int32Range.CreateFinite(1, 5,  true, false); // [1, 5)
        var right = Int32Range.CreateFinite(5, 10, true, true);  // [5, 10]

        Assert.IsTrue(left.IsStrictlyLeftOf(right));
        Assert.IsTrue(right.IsStrictlyRightOf(left));
    }

    [TestMethod]
    public void IsStrictlyLeftOf_TouchingLeftInclusiveRightExclusive_ReturnsTrue()
    {
        var left  = Int32Range.CreateFinite(1, 5,  true,  true); // [1, 5]
        var right = Int32Range.CreateFinite(5, 10, false, true); // (5, 10]

        Assert.IsTrue(left.IsStrictlyLeftOf(right));
    }

    [TestMethod]
    public void IsStrictlyLeftOf_TouchingBothInclusive_ReturnsFalse()
    {
        var left  = Int32Range.CreateFinite(1, 5,  true, true); // [1, 5]
        var right = Int32Range.CreateFinite(5, 10, true, true); // [5, 10]

        Assert.IsFalse(left.IsStrictlyLeftOf(right));
    }

    [TestMethod]
    public void IsStrictlyLeftOf_Overlapping_ReturnsFalse()
    {
        var left  = Int32Range.CreateFinite(1, 7);
        var right = Int32Range.CreateFinite(5, 10);

        Assert.IsFalse(left.IsStrictlyLeftOf(right));
    }

    [TestMethod]
    public void IsStrictlyLeftOf_FiniteVsOpenEnd_StrictlyLeft_ReturnsTrue()
    {
        var finite  = Int32Range.CreateFinite(1, 4, true, true); // [1, 4]
        var openEnd = Int32Range.CreateOpenEnd(5, true);     // [5, ∞)

        Assert.IsTrue(finite.IsStrictlyLeftOf(openEnd));
    }

    [TestMethod]
    public void IsStrictlyLeftOf_FiniteVsOpenEnd_Touching_BothExclusive_ReturnsTrue()
    {
        var finite  = Int32Range.CreateFinite(1, 5, true, false); // [1, 5)
        var openEnd = Int32Range.CreateOpenEnd(5, false);     // (5, ∞)

        Assert.IsTrue(finite.IsStrictlyLeftOf(openEnd));
    }

    [TestMethod]
    public void IsStrictlyLeftOf_OpenStartIsNeverStrictlyLeft_ReturnsFalse()
    {
        // UnboundedStart ranges extend to -∞, so they can never be strictly left of anything
        var openStart = Int32Range.CreateOpenStart(5, true);
        var finite    = Int32Range.CreateFinite(10, 20);

        Assert.IsFalse(openStart.IsStrictlyLeftOf(finite));
    }
}
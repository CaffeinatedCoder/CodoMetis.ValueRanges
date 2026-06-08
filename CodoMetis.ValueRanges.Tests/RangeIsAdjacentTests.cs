namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeIsAdjacentTests
{
    [TestMethod]
    public void IsAdjacentTo_Continuous_XorInclusiveness_IsAdjacent()
    {
        // [1, 5) and [5, 10] — they meet at 5 with XOR inclusiveness
        var left  = DecimalRange.Closed(1m, 5m,  true, false);
        var right = DecimalRange.Closed(5m, 10m, true, true);

        Assert.IsTrue(left.IsAdjacentTo(right));
        Assert.IsTrue(right.IsAdjacentTo(left));
    }

    [TestMethod]
    public void IsAdjacentTo_Continuous_BothInclusive_NotAdjacent_TheyOverlap()
    {
        // [1, 5] and [5, 10] — they share point 5, so they overlap, not adjacent
        var left  = DecimalRange.Closed(1m, 5m,  true, true);
        var right = DecimalRange.Closed(5m, 10m, true, true);

        Assert.IsFalse(left.IsAdjacentTo(right));
    }

    [TestMethod]
    public void IsAdjacentTo_Continuous_BothExclusive_NotAdjacent_ThereIsGap()
    {
        // [1, 5) and (5, 10] — no value is claimed at 5, so there is a gap
        var left  = DecimalRange.Closed(1m, 5m,  true,  false);
        var right = DecimalRange.Closed(5m, 10m, false, true);

        Assert.IsFalse(left.IsAdjacentTo(right));
    }

    [TestMethod]
    public void IsAdjacentTo_Discrete_OneStepApart_BothInclusive_IsAdjacent()
    {
        // [1, 5] and [6, 10] — no integer exists between 5 and 6, so adjacent for int
        var left  = Int32Range.Closed(1, 5,  true, true);
        var right = Int32Range.Closed(6, 10, true, true);

        Assert.IsTrue(left.IsAdjacentTo(right));
        Assert.IsTrue(right.IsAdjacentTo(left));
    }

    [TestMethod]
    public void IsAdjacentTo_Discrete_TwoStepsApart_NotAdjacent()
    {
        // [1, 5] and [7, 10] — there is a gap (6 is missing)
        var left  = Int32Range.Closed(1, 5,  true, true);
        var right = Int32Range.Closed(7, 10, true, true);

        Assert.IsFalse(left.IsAdjacentTo(right));
    }

    [TestMethod]
    public void IsAdjacentTo_Overlapping_ReturnsFalse()
    {
        var left  = Int32Range.Closed(1, 5);
        var right = Int32Range.Closed(4, 10);

        Assert.IsFalse(left.IsAdjacentTo(right));
        Assert.IsFalse(right.IsAdjacentTo(left));
    }

    [TestMethod]
    public void IsAdjacentTo_Finite_AdjacentToOpenStart_AtUpperBound()
    {
        // (-∞, 5) and [5, 10] — adjacent because XOR inclusiveness at 5
        var openStart = Int32Range.WithOpenStart(5, false);   // (-∞, 5)
        var finite    = Int32Range.Closed(5, 10, true, true); // [5, 10]

        // IsAdjacentTo is only implemented when the receiver is IFiniteRange
        Assert.IsTrue(finite.IsAdjacentTo(openStart));
    }

    [TestMethod]
    public void IsAdjacentTo_Finite_AdjacentToOpenEnd_AtLowerBound()
    {
        // [1, 5] and (5, ∞) — adjacent because XOR inclusiveness at 5
        var finite  = Int32Range.Closed(1, 5, true, true); // [1, 5]
        var openEnd = Int32Range.WithOpenEnd(5, false);    // (5, ∞)

        // IsAdjacentTo is only implemented when the receiver is IFiniteRange
        Assert.IsTrue(finite.IsAdjacentTo(openEnd));
    }

    [TestMethod]
    public void IsAdjacentTo_Discrete_FiniteAdjacentToOpenStart_OneStepApart()
    {
        // (-∞, 4] and [6, 10] with int — gap is one step (4+1=5, not 6), so NOT adjacent
        var openStart = Int32Range.WithOpenStart(4, true);    // (-∞, 4]
        var finite    = Int32Range.Closed(6, 10, true, true); // [6, 10]

        Assert.IsFalse(finite.IsAdjacentTo(openStart));

        // But (-∞, 5] and [6, 10] IS adjacent
        var openStart2 = Int32Range.WithOpenStart(5, true);
        Assert.IsTrue(finite.IsAdjacentTo(openStart2));
    }
}
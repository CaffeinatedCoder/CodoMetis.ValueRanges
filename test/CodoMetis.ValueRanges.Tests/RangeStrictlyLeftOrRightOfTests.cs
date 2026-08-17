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
        var openEnd = Int32Range.CreateUnboundedEnd(5, true);     // [5, ∞)

        Assert.IsTrue(finite.IsStrictlyLeftOf(openEnd));
    }

    [TestMethod]
    public void IsStrictlyLeftOf_FiniteVsOpenEnd_Touching_BothExclusive_ReturnsTrue()
    {
        var finite  = Int32Range.CreateFinite(1, 5, true, false); // [1, 5)
        var openEnd = Int32Range.CreateUnboundedEnd(5, false);     // (5, ∞)

        Assert.IsTrue(finite.IsStrictlyLeftOf(openEnd));
    }

    /// <summary>
    /// <c>&lt;&lt;</c> compares the receiver's <em>upper</em> bound with the operand's
    /// <em>lower</em> bound, so an <c>UnboundedStart</c> receiver is decided by its finite
    /// upper bound like any other: <c>(-∞, 5]</c> ends at 5 and is strictly left of
    /// <c>[10, 20]</c>. Being unbounded at the <em>other</em> end is irrelevant.
    /// </summary>
    /// <remarks>
    /// This asserted the opposite until 6.4.0, on the reasoning that "UnboundedStart ranges
    /// extend to -∞, so they can never be strictly left of anything" — which names the wrong
    /// bound. PostgreSQL's <c>&lt;&lt;</c> answers <see langword="true"/> here, so the query
    /// that ran server-side and the same expression evaluated in memory disagreed.
    /// </remarks>
    [TestMethod]
    public void IsStrictlyLeftOf_OpenStartWithFiniteUpperBound_ReturnsTrue()
    {
        var openStart = Int32Range.CreateUnboundedStart(5, true); // (-∞, 5]
        var finite    = Int32Range.CreateFinite(10, 20);

        Assert.IsTrue(openStart.IsStrictlyLeftOf(finite));
        Assert.IsTrue(finite.IsStrictlyRightOf(openStart));
    }

    /// <summary>
    /// The boundary rule applies to an <c>UnboundedStart</c> receiver exactly as to a finite one:
    /// sharing the meeting point disqualifies it, and one exclusive side is enough.
    /// </summary>
    [TestMethod]
    public void IsStrictlyLeftOf_OpenStartTouchingOperand_FollowsInclusivity()
    {
        var closedAt5 = DecimalRange.CreateUnboundedStart(5m, true);  // (-∞, 5]
        var openAt5   = DecimalRange.CreateUnboundedStart(5m, false); // (-∞, 5)

        Assert.IsFalse(closedAt5.IsStrictlyLeftOf(DecimalRange.CreateFinite(5m, 9m, true, false)));  // [5, 9)
        Assert.IsTrue(closedAt5.IsStrictlyLeftOf(DecimalRange.CreateFinite(5m, 9m, false, false)));  // (5, 9)
        Assert.IsTrue(openAt5.IsStrictlyLeftOf(DecimalRange.CreateFinite(5m, 9m, true, false)));     // [5, 9)

        // …and against an unbounded-end operand, whose lower bound is finite.
        Assert.IsFalse(closedAt5.IsStrictlyLeftOf(DecimalRange.CreateUnboundedEnd(5m)));             // [5, ∞)
        Assert.IsTrue(closedAt5.IsStrictlyLeftOf(DecimalRange.CreateUnboundedEnd(5m, false)));       // (5, ∞)
    }

    /// <summary>
    /// The whole 5×5 shape matrix in both directions, which is what catches a predicate that
    /// switches on the receiver's shape and handles the operand's separately. The receiver is
    /// disqualified only by having no upper bound, the operand only by having no lower bound,
    /// and an empty range on either side always loses.
    /// </summary>
    /// <remarks>
    /// Every expectation here was taken from PostgreSQL's <c>&lt;&lt;</c> on the same literals.
    /// </remarks>
    [TestMethod]
    public void IsStrictlyLeftOf_AllFiveShapes_BothDirections()
    {
        var empty     = Int32Range.Empty;
        var finiteLow = Int32Range.CreateFinite(1, 5);
        var finiteHigh = Int32Range.CreateFinite(10, 20);
        var openStart = Int32Range.CreateUnboundedStart(5); // (-∞, 5]
        var openEnd   = Int32Range.CreateUnboundedEnd(10);  // [10, ∞)
        var infinite  = Int32Range.Infinite;

        // Receivers with a finite upper bound, against operands with a finite lower bound.
        Assert.IsTrue(finiteLow.IsStrictlyLeftOf(finiteHigh));
        Assert.IsTrue(finiteLow.IsStrictlyLeftOf(openEnd));
        Assert.IsTrue(openStart.IsStrictlyLeftOf(finiteHigh));
        Assert.IsTrue(openStart.IsStrictlyLeftOf(openEnd));

        // No upper bound on the receiver — nothing can be strictly left of anything.
        Assert.IsFalse(openEnd.IsStrictlyLeftOf(finiteHigh));
        Assert.IsFalse(infinite.IsStrictlyLeftOf(finiteHigh));

        // No lower bound on the operand — nothing can be strictly left of it.
        Assert.IsFalse(finiteLow.IsStrictlyLeftOf(openStart));
        Assert.IsFalse(finiteLow.IsStrictlyLeftOf(infinite));

        // Empty loses from either side, in either role.
        Assert.IsFalse(empty.IsStrictlyLeftOf(finiteHigh));
        Assert.IsFalse(finiteLow.IsStrictlyLeftOf(empty));
        Assert.IsFalse(empty.IsStrictlyLeftOf(empty));

        // IsStrictlyRightOf is the same relation with the operands swapped, so every case above
        // must answer identically when asked the other way round.
        Int32Range[] shapes = [empty, finiteLow, finiteHigh, openStart, openEnd, infinite];
        foreach (var a in shapes)
        foreach (var b in shapes)
        {
            Assert.AreEqual(
                a.IsStrictlyLeftOf(b), b.IsStrictlyRightOf(a),
                $"{a} << {b} disagrees with {b} >> {a}");
        }
    }
}
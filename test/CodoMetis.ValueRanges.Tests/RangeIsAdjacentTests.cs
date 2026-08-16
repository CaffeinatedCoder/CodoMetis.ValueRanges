namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeIsAdjacentTests
{
    [TestMethod]
    public void IsAdjacentTo_Continuous_XorInclusiveness_IsAdjacent()
    {
        // [1, 5) and [5, 10] — they meet at 5 with XOR inclusiveness
        var left  = DecimalRange.CreateFinite(1m, 5m,  true, false);
        var right = DecimalRange.CreateFinite(5m, 10m, true, true);

        Assert.IsTrue(left.IsAdjacentTo(right));
        Assert.IsTrue(right.IsAdjacentTo(left));
    }

    [TestMethod]
    public void IsAdjacentTo_Continuous_BothInclusive_NotAdjacent_TheyOverlap()
    {
        // [1, 5] and [5, 10] — they share point 5, so they overlap, not adjacent
        var left  = DecimalRange.CreateFinite(1m, 5m,  true, true);
        var right = DecimalRange.CreateFinite(5m, 10m, true, true);

        Assert.IsFalse(left.IsAdjacentTo(right));
    }

    [TestMethod]
    public void IsAdjacentTo_Continuous_BothExclusive_NotAdjacent_ThereIsGap()
    {
        // [1, 5) and (5, 10] — no value is claimed at 5, so there is a gap
        var left  = DecimalRange.CreateFinite(1m, 5m,  true,  false);
        var right = DecimalRange.CreateFinite(5m, 10m, false, true);

        Assert.IsFalse(left.IsAdjacentTo(right));
    }

    [TestMethod]
    public void IsAdjacentTo_Discrete_OneStepApart_BothInclusive_IsAdjacent()
    {
        // [1, 5] and [6, 10] — no integer exists between 5 and 6, so adjacent for int
        var left  = Int32Range.CreateFinite(1, 5,  true, true);
        var right = Int32Range.CreateFinite(6, 10, true, true);

        Assert.IsTrue(left.IsAdjacentTo(right));
        Assert.IsTrue(right.IsAdjacentTo(left));
    }

    [TestMethod]
    public void IsAdjacentTo_Discrete_TwoStepsApart_NotAdjacent()
    {
        // [1, 5] and [7, 10] — there is a gap (6 is missing)
        var left  = Int32Range.CreateFinite(1, 5,  true, true);
        var right = Int32Range.CreateFinite(7, 10, true, true);

        Assert.IsFalse(left.IsAdjacentTo(right));
    }

    [TestMethod]
    public void IsAdjacentTo_Overlapping_ReturnsFalse()
    {
        var left  = Int32Range.CreateFinite(1, 5);
        var right = Int32Range.CreateFinite(4, 10);

        Assert.IsFalse(left.IsAdjacentTo(right));
        Assert.IsFalse(right.IsAdjacentTo(left));
    }

    [TestMethod]
    public void IsAdjacentTo_Finite_AdjacentToOpenStart_AtUpperBound()
    {
        // (-∞, 5) and [5, 10] — adjacent because XOR inclusiveness at 5
        var openStart = Int32Range.CreateUnboundedStart(5, false);   // (-∞, 5)
        var finite    = Int32Range.CreateFinite(5, 10, true, true); // [5, 10]

        // IsAdjacentTo is only implemented when the receiver is IFiniteRange
        Assert.IsTrue(finite.IsAdjacentTo(openStart));
    }

    [TestMethod]
    public void IsAdjacentTo_Finite_AdjacentToOpenEnd_AtLowerBound()
    {
        // [1, 5] and (5, ∞) — adjacent because XOR inclusiveness at 5
        var finite  = Int32Range.CreateFinite(1, 5, true, true); // [1, 5]
        var openEnd = Int32Range.CreateUnboundedEnd(5, false);    // (5, ∞)

        // IsAdjacentTo is only implemented when the receiver is IFiniteRange
        Assert.IsTrue(finite.IsAdjacentTo(openEnd));
    }

    [TestMethod]
    public void IsAdjacentTo_Discrete_FiniteAdjacentToOpenStart_OneStepApart()
    {
        // (-∞, 4] and [6, 10] with int — gap is one step (4+1=5, not 6), so NOT adjacent
        var openStart = Int32Range.CreateUnboundedStart(4, true);    // (-∞, 4]
        var finite    = Int32Range.CreateFinite(6, 10, true, true); // [6, 10]

        Assert.IsFalse(finite.IsAdjacentTo(openStart));

        // But (-∞, 5] and [6, 10] IS adjacent
        var openStart2 = Int32Range.CreateUnboundedStart(5, true);
        Assert.IsTrue(finite.IsAdjacentTo(openStart2));
    }

    // -------------------------------------------------------------------------
    // Symmetry — the receiver's shape must not decide the answer
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adjacency is a symmetric relation, and PostgreSQL's <c>-|-</c> is symmetric too
    /// (confirmed directly against the server). Answering on the receiver's shape rather than
    /// on the pair breaks both.
    /// </summary>
    [TestMethod]
    public void IsAdjacentTo_IsSymmetric_AcrossEveryShapePair()
    {
        Int32Range[] shapes =
        [
            Int32Range.Empty,
            Int32Range.Infinite,
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(4, 6),
            Int32Range.CreateUnboundedStart(0, true),  // (-∞, 0]
            Int32Range.CreateUnboundedEnd(1),          // [1, +∞)
            Int32Range.CreateUnboundedEnd(4),          // [4, +∞)
            Int32Range.CreateUnboundedEnd(7)           // [7, +∞)
        ];

        foreach (var a in shapes)
        foreach (var b in shapes)
            Assert.AreEqual(
                a.IsAdjacentTo(b), b.IsAdjacentTo(a),
                $"'{a}' -|- '{b}' = {a.IsAdjacentTo(b)} but '{b}' -|- '{a}' = {b.IsAdjacentTo(a)}");
    }

    /// <summary>
    /// The concrete pairs PostgreSQL was asked about directly, all of which it answers
    /// <see langword="true"/> in both directions.
    /// </summary>
    [TestMethod]
    public void IsAdjacentTo_UnboundedReceiver_MatchesPostgres()
    {
        var openStart = Int32Range.CreateUnboundedStart(0, true);  // (-∞, 0]
        var finite    = Int32Range.CreateFinite(1, 3);             // [1, 3]
        var openEnd   = Int32Range.CreateUnboundedEnd(4);          // [4, +∞)
        var meeting   = Int32Range.CreateUnboundedEnd(1);          // [1, +∞)

        Assert.IsTrue(openStart.IsAdjacentTo(finite), "(-∞,0] -|- [1,3]");
        Assert.IsTrue(finite.IsAdjacentTo(openStart), "[1,3] -|- (-∞,0]");

        Assert.IsTrue(openEnd.IsAdjacentTo(finite), "[4,+∞) -|- [1,3]");
        Assert.IsTrue(finite.IsAdjacentTo(openEnd), "[1,3] -|- [4,+∞)");

        Assert.IsTrue(openStart.IsAdjacentTo(meeting), "(-∞,0] -|- [1,+∞)");
        Assert.IsTrue(meeting.IsAdjacentTo(openStart), "[1,+∞) -|- (-∞,0]");
    }

    [TestMethod]
    public void IsAdjacentTo_Continuous_UnboundedReceiver_NeedsXorInclusiveness()
    {
        var openStart = DecimalRange.CreateUnboundedStart(5m, false); // (-∞, 5)
        var claiming  = DecimalRange.CreateFinite(5m, 10m, true, true);  // [5, 10]
        var leaving   = DecimalRange.CreateFinite(5m, 10m, false, true); // (5, 10]

        Assert.IsTrue(openStart.IsAdjacentTo(claiming), "one side claims 5");
        Assert.IsFalse(openStart.IsAdjacentTo(leaving), "neither side claims 5 — a gap");

        var openEnd  = DecimalRange.CreateUnboundedEnd(5m, true);        // [5, +∞)
        var upToFive = DecimalRange.CreateFinite(1m, 5m, true, false);   // [1, 5)

        Assert.IsTrue(openEnd.IsAdjacentTo(upToFive));
        Assert.IsFalse(openEnd.IsAdjacentTo(DecimalRange.CreateFinite(1m, 5m, true, true)), "both claim 5 — overlap");
    }

    /// <summary>
    /// Widening the receiver must not make overlapping or degenerate operands adjacent.
    /// </summary>
    [TestMethod]
    public void IsAdjacentTo_UnboundedReceiver_StillFalseWhenOverlappingOrDegenerate()
    {
        var openStart = Int32Range.CreateUnboundedStart(0, true);  // (-∞, 0]
        var openEnd   = Int32Range.CreateUnboundedEnd(4);          // [4, +∞)

        // Two ranges running to the same infinity always overlap.
        Assert.IsFalse(openStart.IsAdjacentTo(Int32Range.CreateUnboundedStart(9, true)));
        Assert.IsFalse(openEnd.IsAdjacentTo(Int32Range.CreateUnboundedEnd(9)));

        // A gap, not a meeting point.
        Assert.IsFalse(openStart.IsAdjacentTo(openEnd));

        Assert.IsFalse(openStart.IsAdjacentTo(Int32Range.Infinite));
        Assert.IsFalse(openStart.IsAdjacentTo(Int32Range.Empty));
        Assert.IsFalse(Int32Range.Infinite.IsAdjacentTo(openStart));
        Assert.IsFalse(Int32Range.Empty.IsAdjacentTo(openStart));
    }
}
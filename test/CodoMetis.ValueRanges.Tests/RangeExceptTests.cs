using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeExceptTests
{
    [TestMethod]
    public void Except_NoOverlap_ReturnsOriginalRange()
    {
        var range = Int32Range.CreateFinite(1, 5,  true, true); // [1, 5]
        var other = Int32Range.CreateFinite(7, 10, true, true); // [7, 10]

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(range, result[0]);
    }

    [TestMethod]
    public void Except_OtherFullyContainsRange_ReturnsEmptySet()
    {
        var range = Int32Range.CreateFinite(3, 8,  true, true); // [3, 8]
        var other = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]

        var result = range.Except(other);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Except_OtherEqualsRange_ReturnsEmptySet()
    {
        var range = Int32Range.CreateFinite(1, 10, true, true);
        var other = Int32Range.CreateFinite(1, 10, true, true);

        var result = range.Except(other);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Except_LeftTrim_OtherCoversLowerPart_ReturnsRightPiece()
    {
        // [1, 10] \ [-5, 5] → (5, 10] ≡ [6, 10] for int
        var range = Int32Range.CreateFinite(1,  10, true, true); // [1, 10]
        var other = Int32Range.CreateFinite(-5, 5,  true, true); // [-5, 5] — starts before range

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        var left = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(6,  left.Start);  // canonical: (5, 10] ≡ [6, 10]
        Assert.AreEqual(10, left.End);
        Assert.IsTrue(left.StartInclusive);
        Assert.IsTrue(left.EndInclusive);
    }

    [TestMethod]
    public void Except_LeftTrim_OtherUpperExclusive_FlipsToInclusive()
    {
        // [1, 10] \ [-5, 5) → [5, 10]
        var range = Int32Range.CreateFinite(1,  10, true, true);  // [1, 10]
        var other = Int32Range.CreateFinite(-5, 5,  true, false); // [-5, 5) — starts before range

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        var left = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(5, left.Start);
        Assert.IsTrue(left.StartInclusive); // flipped from other's upper exclusive
    }

    [TestMethod]
    public void Except_RightTrim_OtherCoversUpperPart_ReturnsLeftPiece()
    {
        // [1, 10] \ [6, 15] → [1, 6) ≡ [1, 5] for int
        var range = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]
        var other = Int32Range.CreateFinite(6, 15, true, true); // [6, 15] — ends after range

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        var left = result[0] as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(1, left.Start);
        Assert.AreEqual(5, left.End);  // canonical: [1, 6) ≡ [1, 5]
        Assert.IsTrue(left.StartInclusive);
        Assert.IsTrue(left.EndInclusive);
    }

    [TestMethod]
    public void Except_InteriorSplit_OtherStrictlyInside_ReturnsTwoPieces()
    {
        // [1, 10] \ [4, 6] → [1, 4) and (6, 10] ≡ [1, 3] and [7, 10] for int
        var range = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]
        var other = Int32Range.CreateFinite(4, 6,  true, true); // [4, 6]

        var result = range.Except(other);

        Assert.AreEqual(2, result.Count);
        var left  = result[0] as IFiniteRange<int>;
        var right = result[1] as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        Assert.AreEqual(1, left.Start);
        Assert.AreEqual(3, left.End);  // canonical: [1, 4) ≡ [1, 3]
        Assert.IsTrue(left.StartInclusive);
        Assert.IsTrue(left.EndInclusive);

        Assert.AreEqual(7,  right.Start);  // canonical: (6, 10] ≡ [7, 10]
        Assert.AreEqual(10, right.End);
        Assert.IsTrue(right.StartInclusive);
        Assert.IsTrue(right.EndInclusive);
    }

    [TestMethod]
    public void Except_InteriorSplit_OtherExclusive_FlipsBoundaryInclusiveness()
    {
        // [1, 10] \ (4, 6) → [1, 4] and [6, 10] ≡ [1, 4] and [6, 10] for int
        // (4, 6) ≡ [5, 5] for int, so flipped bounds land on 4 and 6 (already inclusive)
        var range = Int32Range.CreateFinite(1, 10, true,  true);  // [1, 10]
        var other = Int32Range.CreateFinite(4, 6,  false, false); // (4, 6) ≡ [5, 5]

        var result = range.Except(other);

        Assert.AreEqual(2, result.Count);
        var left  = result[0] as IFiniteRange<int>;
        var right = result[1] as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        Assert.AreEqual(4, left.End);
        Assert.IsTrue(left.EndInclusive); // canonical: [1, 4]

        Assert.AreEqual(6, right.Start);
        Assert.IsTrue(right.StartInclusive); // canonical: [6, 10]
    }

    [TestMethod]
    public void Except_OpenStart_NoOverlap_ReturnsOriginalRange()
    {
        var range = Int32Range.CreateUnboundedStart(5, true);    // (-∞, 5]
        var other = Int32Range.CreateFinite(7, 10, true, true);  // [7, 10]

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(range, result[0]);
    }

    [TestMethod]
    public void Except_OpenStart_OtherTrimsRightEnd_ReturnsNewOpenStart()
    {
        // (-∞, 10] \ [7, 15] → (-∞, 7) ≡ (-∞, 6] for int
        var range = Int32Range.CreateUnboundedStart(10, true);  // (-∞, 10]
        var other = Int32Range.CreateFinite(7, 15, true, true); // [7, 15]

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        var left = result[0] as IUnboundedStartRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(6, left.End);  // canonical: (-∞, 7) ≡ (-∞, 6]
        Assert.IsTrue(left.EndInclusive);
    }

    [TestMethod]
    public void Except_OpenStart_OtherExclusiveLower_FlipsToInclusiveOnResult()
    {
        // (-∞, 10] \ (7, 15] → (-∞, 7] ≡ (-∞, 7] for int (already canonical)
        var range = Int32Range.CreateUnboundedStart(10, true);   // (-∞, 10]
        var other = Int32Range.CreateFinite(7, 15, false, true); // (7, 15] ≡ [8, 15]

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        var left = result[0] as IUnboundedStartRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(7, left.End);  // canonical: (-∞, 7]
        Assert.IsTrue(left.EndInclusive);
    }

    [TestMethod]
    public void Except_OpenStart_OtherInterior_ReturnsSplitIntoOpenStartAndFinite()
    {
        // (-∞, 10] \ [3, 7] → (-∞, 3) and (7, 10] ≡ (-∞, 2] and [8, 10] for int
        var range = Int32Range.CreateUnboundedStart(10, true); // (-∞, 10]
        var other = Int32Range.CreateFinite(3, 7, true, true); // [3, 7]

        var result = range.Except(other);

        Assert.AreEqual(2, result.Count);
        var left  = result[0] as IUnboundedStartRange<int>;
        var right = result[1] as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        // Left piece: (-∞, 3) ≡ (-∞, 2] for int
        Assert.AreEqual(2, left.End);
        Assert.IsTrue(left.EndInclusive);

        // Right piece: (7, 10] ≡ [8, 10] for int
        Assert.AreEqual(8,  right.Start);
        Assert.AreEqual(10, right.End);
        Assert.IsTrue(right.StartInclusive);
        Assert.IsTrue(right.EndInclusive);
    }

    [TestMethod]
    public void Except_OpenEnd_NoOverlap_ReturnsOriginalRange()
    {
        var range = Int32Range.CreateUnboundedEnd(10, true);   // [10, ∞)
        var other = Int32Range.CreateFinite(1, 5, true, true); // [1, 5]

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(range, result[0]);
    }

    [TestMethod]
    public void Except_OpenEnd_OtherTrimmedFromLeft_ReturnsNewOpenEnd()
    {
        // [5, ∞) \ [1, 8] → (8, ∞) ≡ [9, ∞) for int
        var range = Int32Range.CreateUnboundedEnd(5, true);    // [5, ∞)
        var other = Int32Range.CreateFinite(1, 8, true, true); // [1, 8]

        var result = range.Except(other);

        Assert.AreEqual(1, result.Count);
        var left = result[0] as IUnboundedEndRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(9, left.Start);  // canonical: (8, ∞) ≡ [9, ∞)
        Assert.IsTrue(left.StartInclusive);
    }

    [TestMethod]
    public void Except_OpenEnd_OtherInterior_ReturnsSplitIntoFiniteAndOpenEnd()
    {
        // [5, ∞) \ [8, 12] → [5, 8) and (12, ∞) ≡ [5, 7] and [13, ∞) for int
        var range = Int32Range.CreateUnboundedEnd(5, true);     // [5, ∞)
        var other = Int32Range.CreateFinite(8, 12, true, true); // [8, 12]

        var result = range.Except(other);

        Assert.AreEqual(2, result.Count);
        var left  = result[0] as IFiniteRange<int>;
        var right = result[1] as IUnboundedEndRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        // Left piece: [5, 8) ≡ [5, 7] for int
        Assert.AreEqual(5, left.Start);
        Assert.AreEqual(7, left.End);
        Assert.IsTrue(left.StartInclusive);
        Assert.IsTrue(left.EndInclusive);

        // Right piece: (12, ∞) ≡ [13, ∞) for int
        Assert.AreEqual(13, right.Start);
        Assert.IsTrue(right.StartInclusive);
    }

    [TestMethod]
    public void Except_Infinity_OtherFinite_ReturnsSplitIntoOpenStartAndOpenEnd()
    {
        // (-∞, ∞) \ [3, 7] → (-∞, 3) and (7, ∞) ≡ (-∞, 2] and [8, ∞) for int
        var other = Int32Range.CreateFinite(3, 7, true, true); // [3, 7]

        var result = Int32Range.Infinite.Except(other);

        Assert.AreEqual(2, result.Count);
        var left  = result[0] as IUnboundedStartRange<int>;
        var right = result[1] as IUnboundedEndRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        Assert.AreEqual(2, left.End);  // canonical: (-∞, 3) ≡ (-∞, 2]
        Assert.IsTrue(left.EndInclusive);

        Assert.AreEqual(8, right.Start);  // canonical: (7, ∞) ≡ [8, ∞)
        Assert.IsTrue(right.StartInclusive);
    }

    /// <summary>
    /// Two operands unbounded in *opposite* directions: each one's bounded edge cuts the other.
    /// </summary>
    /// <remarks>
    /// This returned the receiver unchanged until 7.0.0 — subtracting nothing at all.
    /// <c>ExceptEngine</c> dispatched on the receiver's shape, and the inner switch for each
    /// unbounded receiver had an arm for a *finite* operand and one for an operand unbounded the
    /// *same* way, but none for the opposing one; the `_` fallback rebuilt the receiver. It is the
    /// only pair of shapes that reaches that arm, since an empty operand is filtered by the
    /// `Overlaps` guard and an infinite one by the `Contains` guard, so the fallback was wrong
    /// every single time it ran.
    /// </remarks>
    [TestMethod]
    public void Except_OpposingUnboundedOperands_TrimsAtTheOperandsBoundedEdge()
    {
        // (-∞, 5] ∖ [1, ∞) — everything at or below 5 that is not at or above 1, i.e. (-∞, 0].
        var upTo5   = Int32Range.CreateUnboundedStart(5, true);
        var from1   = Int32Range.CreateUnboundedEnd(1, true);
        var trimmed = upTo5.Except(from1);

        Assert.AreEqual(1, trimmed.Count);
        var head = trimmed[0] as IUnboundedStartRange<int>;
        Assert.IsNotNull(head);
        Assert.AreEqual(0, head.End);      // canonical: (-∞, 1) ≡ (-∞, 0]
        Assert.IsTrue(head.EndInclusive);

        // The mirror: [1, ∞) ∖ (-∞, 5] leaves [6, ∞).
        var tailSet = from1.Except(upTo5);

        Assert.AreEqual(1, tailSet.Count);
        var tail = tailSet[0] as IUnboundedEndRange<int>;
        Assert.IsNotNull(tail);
        Assert.AreEqual(6, tail.Start);    // canonical: (5, ∞) ≡ [6, ∞)
        Assert.IsTrue(tail.StartInclusive);
    }

    /// <summary>
    /// The same pair on a continuous domain, where inclusivity rather than a step decides the
    /// cut — and where the boundary value itself must survive on exactly one side.
    /// </summary>
    [TestMethod]
    public void Except_OpposingUnboundedOperands_Continuous_InvertsTheBoundary()
    {
        var upTo9 = DecimalRange.CreateUnboundedStart(9m, true);  // (-∞, 9]
        var from5 = DecimalRange.CreateUnboundedEnd(5m, true);    // [5, ∞)

        var head = upTo9.Except(from5);
        Assert.AreEqual(1, head.Count);
        Assert.AreEqual("(,5)", head[0].ToString());              // 5 belongs to the subtrahend

        var tail = from5.Except(upTo9);
        Assert.AreEqual(1, tail.Count);
        Assert.AreEqual("(9,)", tail[0].ToString());              // 9 belongs to the subtrahend
    }

    /// <summary>
    /// <see cref="RangeSet{TRange,T}.Except(RangeSet{TRange,T})"/> reaches the same engine through
    /// its merge-join, so the multirange path had the defect too.
    /// </summary>
    [TestMethod]
    public void Except_Set_OpposingUnboundedElements_TrimsCorrectly()
    {
        var upTo5 = RangeSet<Int32Range, int>.From([Int32Range.CreateUnboundedStart(5, true)]);
        var from1 = RangeSet<Int32Range, int>.From([Int32Range.CreateUnboundedEnd(1, true)]);

        Assert.AreEqual("{(,0]}", upTo5.Except(from1).ToString());
        Assert.AreEqual("{[6,)}", from1.Except(upTo5).ToString());
    }

    /// <summary>
    /// X \ (-∞, +∞) is the empty set for every X, the infinite set included. The single-range
    /// overload has always answered this through its Contains guard and the set-minus-set overload
    /// through its own infinite-operand guard; <see cref="RangeSet{TRange,T}.Except(TRange)"/> had
    /// neither, so ∞ \ ∞ reached the engine as a pair no arm covered and the fallback answered ∞.
    /// </summary>
    [TestMethod]
    public void Except_InfinityOperand_LeavesNothing()
    {
        Assert.IsTrue(RangeSet<Int32Range, int>.Infinite.Except(Int32Range.Infinite).IsEmpty());
        Assert.IsTrue(RangeSet<DecimalRange, decimal>.Infinite.Except(DecimalRange.Infinite).IsEmpty());

        // The same operand against a bounded set, which reached the engine by a different path.
        var bounded = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 5, true, true)]);
        Assert.IsTrue(bounded.Except(Int32Range.Infinite).IsEmpty());

        // And the single-range and set-minus-set overloads it now agrees with.
        Assert.IsTrue(Int32Range.Infinite.Except(Int32Range.Infinite).IsEmpty());
        Assert.IsTrue(RangeSet<Int32Range, int>.Infinite.Except(RangeSet<Int32Range, int>.Infinite).IsEmpty());
    }

    /// <summary>
    /// Complement is defined as <c>Infinite.Except(this)</c>, so the infinite set's complement
    /// runs through the same path and must be empty rather than the whole domain.
    /// </summary>
    [TestMethod]
    public void Complement_OfTheInfiniteSet_IsEmpty()
    {
        Assert.IsTrue(RangeSet<Int32Range, int>.Infinite.Complement().IsEmpty());
        Assert.AreEqual(RangeSet<Int32Range, int>.Infinite,
                        RangeSet<Int32Range, int>.Infinite.Complement().Complement());
    }
}

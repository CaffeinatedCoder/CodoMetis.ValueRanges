namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeExceptTests
{
    [TestMethod]
    public void Except_NoOverlap_ReturnsOriginalRange()
    {
        var range = Int32Range.Closed(1, 5,  true, true); // [1, 5]
        var other = Int32Range.Closed(7, 10, true, true); // [7, 10]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        Assert.AreEqual(range, result.Value.Left);
        Assert.IsNull(result.Value.Right);
    }

    [TestMethod]
    public void Except_OtherFullyContainsRange_ReturnsNull()
    {
        var range = Int32Range.Closed(3, 8,  true, true); // [3, 8]
        var other = Int32Range.Closed(1, 10, true, true); // [1, 10]

        Assert.IsNull(range.Except(other));
    }

    [TestMethod]
    public void Except_OtherEqualsRange_ReturnsNull()
    {
        var range = Int32Range.Closed(1, 10, true, true);
        var other = Int32Range.Closed(1, 10, true, true);

        Assert.IsNull(range.Except(other));
    }

    [TestMethod]
    public void Except_LeftTrim_OtherCoversLowerPart_ReturnsRightPiece()
    {
        // [1, 10] \ [-5, 5] → (5, 10]
        var range = Int32Range.Closed(1,  10, true, true); // [1, 10]
        var other = Int32Range.Closed(-5, 5,  true, true); // [-5, 5] — starts before range

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left = result.Value.Left as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(5,  left.LowerBound);
        Assert.AreEqual(10, left.UpperBound);
        Assert.IsFalse(left.LowerBoundInclusive); // flipped from other's upper inclusive
        Assert.IsTrue(left.UpperBoundInclusive);
        Assert.IsNull(result.Value.Right);
    }

    [TestMethod]
    public void Except_LeftTrim_OtherUpperExclusive_FlipsToInclusive()
    {
        // [1, 10] \ [-5, 5) → [5, 10]
        var range = Int32Range.Closed(1,  10, true, true);  // [1, 10]
        var other = Int32Range.Closed(-5, 5,  true, false); // [-5, 5) — starts before range

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left = result.Value.Left as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(5, left.LowerBound);
        Assert.IsTrue(left.LowerBoundInclusive); // flipped from other's upper exclusive
    }

    [TestMethod]
    public void Except_RightTrim_OtherCoversUpperPart_ReturnsLeftPiece()
    {
        // [1, 10] \ [6, 15] → [1, 6)
        var range = Int32Range.Closed(1, 10, true, true); // [1, 10]
        var other = Int32Range.Closed(6, 15, true, true); // [6, 15] — ends after range

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left = result.Value.Left as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(1, left.LowerBound);
        Assert.AreEqual(6, left.UpperBound);
        Assert.IsTrue(left.LowerBoundInclusive);
        Assert.IsFalse(left.UpperBoundInclusive); // flipped from other's lower inclusive
        Assert.IsNull(result.Value.Right);
    }

    [TestMethod]
    public void Except_InteriorSplit_OtherStrictlyInside_ReturnsTwoPieces()
    {
        // [1, 10] \ [4, 6] → [1, 4) and (6, 10]
        var range = Int32Range.Closed(1, 10, true, true); // [1, 10]
        var other = Int32Range.Closed(4, 6,  true, true); // [4, 6]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left  = result.Value.Left as IFiniteRange<int>;
        var right = result.Value.Right as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        Assert.AreEqual(1, left.LowerBound);
        Assert.AreEqual(4, left.UpperBound);
        Assert.IsTrue(left.LowerBoundInclusive);
        Assert.IsFalse(left.UpperBoundInclusive);

        Assert.AreEqual(6,  right.LowerBound);
        Assert.AreEqual(10, right.UpperBound);
        Assert.IsFalse(right.LowerBoundInclusive);
        Assert.IsTrue(right.UpperBoundInclusive);
    }

    [TestMethod]
    public void Except_InteriorSplit_OtherExclusive_FlipsBoundaryInclusiveness()
    {
        // [1, 10] \ (4, 6) → [1, 4] and [6, 10]
        var range = Int32Range.Closed(1, 10, true,  true);  // [1, 10]
        var other = Int32Range.Closed(4, 6,  false, false); // (4, 6)

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left  = result.Value.Left as IFiniteRange<int>;
        var right = result.Value.Right as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        Assert.AreEqual(4, left.UpperBound);
        Assert.IsTrue(left.UpperBoundInclusive); // flipped from other's exclusive lower

        Assert.AreEqual(6, right.LowerBound);
        Assert.IsTrue(right.LowerBoundInclusive); // flipped from other's exclusive upper
    }

    [TestMethod]
    public void Except_OpenStart_NoOverlap_ReturnsOriginalRange()
    {
        var range = Int32Range.WithOpenStart(3, true);    // (-∞, 3]
        var other = Int32Range.Closed(5, 10, true, true); // [5, 10]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        Assert.AreEqual(range, result.Value.Left);
        Assert.IsNull(result.Value.Right);
    }

    [TestMethod]
    public void Except_OpenStart_OtherTrimsRightEnd_ReturnsNewOpenStart()
    {
        // (-∞, 10] \ [7, 15] → (-∞, 7)
        var range = Int32Range.WithOpenStart(10, true);   // (-∞, 10]
        var other = Int32Range.Closed(7, 15, true, true); // [7, 15]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left = result.Value.Left as IOpenStartRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(7, left.UpperBound);
        Assert.IsFalse(left.UpperBoundInclusive); // flipped from other's inclusive lower
        Assert.IsNull(result.Value.Right);
    }

    [TestMethod]
    public void Except_OpenStart_OtherExclusiveLower_FlipsToInclusiveOnResult()
    {
        // (-∞, 10] \ (7, 15] → (-∞, 7]
        var range = Int32Range.WithOpenStart(10, true);    // (-∞, 10]
        var other = Int32Range.Closed(7, 15, false, true); // (7, 15]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left = result.Value.Left as IOpenStartRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(7, left.UpperBound);
        Assert.IsTrue(left.UpperBoundInclusive); // flipped from other's exclusive lower
    }

    [TestMethod]
    public void Except_OpenStart_OtherInterior_ReturnsSplitIntoOpenStartAndFinite()
    {
        // (-∞, 10] \ [3, 7] → (-∞, 3) and (7, 10]
        var range = Int32Range.WithOpenStart(10, true);  // (-∞, 10]
        var other = Int32Range.Closed(3, 7, true, true); // [3, 7]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left  = result.Value.Left as IOpenStartRange<int>;
        var right = result.Value.Right as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        // Left piece: (-∞, 3)
        Assert.AreEqual(3, left.UpperBound);
        Assert.IsFalse(left.UpperBoundInclusive); // flipped from other's inclusive lower

        // Right piece: (7, 10]
        Assert.AreEqual(7,  right.LowerBound);
        Assert.AreEqual(10, right.UpperBound);
        Assert.IsFalse(right.LowerBoundInclusive); // flipped from other's inclusive upper
        Assert.IsTrue(right.UpperBoundInclusive);
    }

    [TestMethod]
    public void Except_OpenEnd_NoOverlap_ReturnsOriginalRange()
    {
        var range = Int32Range.WithOpenEnd(10, true);    // [10, ∞)
        var other = Int32Range.Closed(1, 5, true, true); // [1, 5]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        Assert.AreEqual(range, result.Value.Left);
        Assert.IsNull(result.Value.Right);
    }

    [TestMethod]
    public void Except_OpenEnd_OtherTrimmedFromLeft_ReturnsNewOpenEnd()
    {
        // [5, ∞) \ [1, 8] → (8, ∞)
        var range = Int32Range.WithOpenEnd(5, true);     // [5, ∞)
        var other = Int32Range.Closed(1, 8, true, true); // [1, 8]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left = result.Value.Left as IOpenEndRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(8, left.LowerBound);
        Assert.IsFalse(left.LowerBoundInclusive); // flipped from other's inclusive upper
        Assert.IsNull(result.Value.Right);
    }

    [TestMethod]
    public void Except_OpenEnd_OtherInterior_ReturnsSplitIntoFiniteAndOpenEnd()
    {
        // [5, ∞) \ [8, 12] → [5, 8) and (12, ∞)
        var range = Int32Range.WithOpenEnd(5, true);      // [5, ∞)
        var other = Int32Range.Closed(8, 12, true, true); // [8, 12]

        var result = range.Except(other);

        Assert.IsNotNull(result);
        var left  = result.Value.Left as IFiniteRange<int>;
        var right = result.Value.Right as IOpenEndRange<int>;
        Assert.IsNotNull(left);
        Assert.IsNotNull(right);

        // Left piece: [5, 8)
        Assert.AreEqual(5, left.LowerBound);
        Assert.AreEqual(8, left.UpperBound);
        Assert.IsTrue(left.LowerBoundInclusive);
        Assert.IsFalse(left.UpperBoundInclusive); // flipped from other's inclusive lower

        // Right piece: (12, ∞)
        Assert.AreEqual(12, right.LowerBound);
        Assert.IsFalse(right.LowerBoundInclusive); // flipped from other's inclusive upper
    }
}
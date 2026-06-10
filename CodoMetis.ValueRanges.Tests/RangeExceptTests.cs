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

        Assert.AreEqual(range, result.Left);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_OtherFullyContainsRange_ReturnsEmpty()
    {
        var range = Int32Range.CreateFinite(3, 8,  true, true); // [3, 8]
        var other = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]

        var result = range.Except(other);

        Assert.IsInstanceOfType<IEmptyRange<int>>(result.Left);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_OtherEqualsRange_ReturnsEmpty()
    {
        var range = Int32Range.CreateFinite(1, 10, true, true);
        var other = Int32Range.CreateFinite(1, 10, true, true);

        var result = range.Except(other);

        Assert.IsInstanceOfType<IEmptyRange<int>>(result.Left);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_LeftTrim_OtherCoversLowerPart_ReturnsRightPiece()
    {
        // [1, 10] \ [-5, 5] → (5, 10] ≡ [6, 10] for int
        var range = Int32Range.CreateFinite(1,  10, true, true); // [1, 10]
        var other = Int32Range.CreateFinite(-5, 5,  true, true); // [-5, 5] — starts before range

        var result = range.Except(other);

        var left = result.Left as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(6,  left.Start);  // canonical: (5, 10] ≡ [6, 10]
        Assert.AreEqual(10, left.End);
        Assert.IsTrue(left.StartInclusive);
        Assert.IsTrue(left.EndInclusive);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_LeftTrim_OtherUpperExclusive_FlipsToInclusive()
    {
        // [1, 10] \ [-5, 5) → [5, 10]
        var range = Int32Range.CreateFinite(1,  10, true, true);  // [1, 10]
        var other = Int32Range.CreateFinite(-5, 5,  true, false); // [-5, 5) — starts before range

        var result = range.Except(other);

        var left = result.Left as IFiniteRange<int>;
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

        var left = result.Left as IFiniteRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(1, left.Start);
        Assert.AreEqual(5, left.End);  // canonical: [1, 6) ≡ [1, 5]
        Assert.IsTrue(left.StartInclusive);
        Assert.IsTrue(left.EndInclusive);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_InteriorSplit_OtherStrictlyInside_ReturnsTwoPieces()
    {
        // [1, 10] \ [4, 6] → [1, 4) and (6, 10] ≡ [1, 3] and [7, 10] for int
        var range = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]
        var other = Int32Range.CreateFinite(4, 6,  true, true); // [4, 6]

        var result = range.Except(other);

        var left  = result.Left as IFiniteRange<int>;
        var right = result.Right as IFiniteRange<int>;
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

        var left  = result.Left as IFiniteRange<int>;
        var right = result.Right as IFiniteRange<int>;
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
        var range = Int32Range.CreateUnboundedStart(3, true);    // (-∞, 3]
        var other = Int32Range.CreateFinite(5, 10, true, true); // [5, 10]

        var result = range.Except(other);

        Assert.AreEqual(range, result.Left);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_OpenStart_OtherTrimsRightEnd_ReturnsNewOpenStart()
    {
        // (-∞, 10] \ [7, 15] → (-∞, 7) ≡ (-∞, 6] for int
        var range = Int32Range.CreateUnboundedStart(10, true);       // (-∞, 10]
        var other = Int32Range.CreateFinite(7, 15, true, true); // [7, 15]

        var result = range.Except(other);

        var left = result.Left as IUnboundedStartRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(6, left.End);  // canonical: (-∞, 7) ≡ (-∞, 6]
        Assert.IsTrue(left.EndInclusive);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_OpenStart_OtherExclusiveLower_FlipsToInclusiveOnResult()
    {
        // (-∞, 10] \ (7, 15] → (-∞, 7] ≡ (-∞, 7] for int (already canonical)
        var range = Int32Range.CreateUnboundedStart(10, true);        // (-∞, 10]
        var other = Int32Range.CreateFinite(7, 15, false, true); // (7, 15] ≡ [8, 15]

        var result = range.Except(other);

        var left = result.Left as IUnboundedStartRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(7, left.End);  // canonical: (-∞, 7]
        Assert.IsTrue(left.EndInclusive);
    }

    [TestMethod]
    public void Except_OpenStart_OtherInterior_ReturnsSplitIntoOpenStartAndFinite()
    {
        // (-∞, 10] \ [3, 7] → (-∞, 3) and (7, 10] ≡ (-∞, 2] and [8, 10] for int
        var range = Int32Range.CreateUnboundedStart(10, true);      // (-∞, 10]
        var other = Int32Range.CreateFinite(3, 7, true, true); // [3, 7]

        var result = range.Except(other);

        var left  = result.Left as IUnboundedStartRange<int>;
        var right = result.Right as IFiniteRange<int>;
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
        var range = Int32Range.CreateUnboundedEnd(10, true);    // [10, ∞)
        var other = Int32Range.CreateFinite(1, 5, true, true); // [1, 5]

        var result = range.Except(other);

        Assert.AreEqual(range, result.Left);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_OpenEnd_OtherTrimmedFromLeft_ReturnsNewOpenEnd()
    {
        // [5, ∞) \ [1, 8] → (8, ∞) ≡ [9, ∞) for int
        var range = Int32Range.CreateUnboundedEnd(5, true);         // [5, ∞)
        var other = Int32Range.CreateFinite(1, 8, true, true); // [1, 8]

        var result = range.Except(other);

        var left = result.Left as IUnboundedEndRange<int>;
        Assert.IsNotNull(left);
        Assert.AreEqual(9, left.Start);  // canonical: (8, ∞) ≡ [9, ∞)
        Assert.IsTrue(left.StartInclusive);
        Assert.IsNull(result.Right);
    }

    [TestMethod]
    public void Except_OpenEnd_OtherInterior_ReturnsSplitIntoFiniteAndOpenEnd()
    {
        // [5, ∞) \ [8, 12] → [5, 8) and (12, ∞) ≡ [5, 7] and [13, ∞) for int
        var range = Int32Range.CreateUnboundedEnd(5, true);          // [5, ∞)
        var other = Int32Range.CreateFinite(8, 12, true, true); // [8, 12]

        var result = range.Except(other);

        var left  = result.Left as IFiniteRange<int>;
        var right = result.Right as IUnboundedEndRange<int>;
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
}

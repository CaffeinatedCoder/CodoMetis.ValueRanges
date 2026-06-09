namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeDoesNotExtendRightOfTests
{
    [TestMethod]
    public void DoesNotExtendRightOf_ReceiverEndsClearlyShorter_ReturnsTrue()
    {
        // int8range(1,20) &< int8range(18,20) → t  (Postgres example)
        var left  = Int32Range.CreateFinite(1,  20, true, false); // [1, 20)
        var right = Int32Range.CreateFinite(18, 20, true, false); // [18, 20)

        Assert.IsTrue(left.DoesNotExtendRightOf(right));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_SameUpperBound_BothInclusive_ReturnsTrue()
    {
        // [1, 10] &< [5, 10] — same upper bound, both inclusive → does not extend right
        var left  = Int32Range.CreateFinite(1, 10, true, true);
        var right = Int32Range.CreateFinite(5, 10, true, true);

        Assert.IsTrue(left.DoesNotExtendRightOf(right));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_SameUpperBound_ReceiverInclusiveOtherExclusive_ReturnsFalse()
    {
        // [1, 10] &< [5, 10) — receiver claims 10, other doesn't → receiver extends further right
        var left  = Int32Range.CreateFinite(1, 10, true, true);  // [1, 10]
        var right = Int32Range.CreateFinite(5, 10, true, false); // [5, 10)

        Assert.IsFalse(left.DoesNotExtendRightOf(right));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_SameUpperBound_BothExclusive_ReturnsTrue()
    {
        // [1, 10) &< [5, 10) — same exclusive upper bound → does not extend right
        var left  = Int32Range.CreateFinite(1, 10, true, false);
        var right = Int32Range.CreateFinite(5, 10, true, false);

        Assert.IsTrue(left.DoesNotExtendRightOf(right));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_ReceiverEndsLater_ReturnsFalse()
    {
        var left  = Int32Range.CreateFinite(1, 15, true, true);
        var right = Int32Range.CreateFinite(5, 10, true, true);

        Assert.IsFalse(left.DoesNotExtendRightOf(right));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_OpenEnd_AlwaysReturnsFalse()
    {
        // An OpenEnd range extends to +∞, so it always extends right of anything finite
        var openEnd = Int32Range.CreateOpenEnd(1, true); // [1, +∞)
        var finite  = Int32Range.CreateFinite(1, 100, true, true);

        Assert.IsFalse(openEnd.DoesNotExtendRightOf(finite));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_FiniteVsOpenEnd_ReturnsTrue()
    {
        // Any finite range does not extend right of an OpenEnd range
        var finite  = Int32Range.CreateFinite(1, 100, true, true);
        var openEnd = Int32Range.CreateOpenEnd(1, true);

        Assert.IsTrue(finite.DoesNotExtendRightOf(openEnd));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_OpenStartVsOpenStart_SameBound_BothInclusive_ReturnsTrue()
    {
        // (-∞, 10] &< (-∞, 10] — same upper bound, both inclusive
        var left  = Int32Range.CreateOpenStart(10, true);
        var right = Int32Range.CreateOpenStart(10, true);

        Assert.IsTrue(left.DoesNotExtendRightOf(right));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_OpenStartVsOpenStart_ReceiverEndsLater_ReturnsFalse()
    {
        // (-∞, 15] &< (-∞, 10] → false
        var left  = Int32Range.CreateOpenStart(15, true);
        var right = Int32Range.CreateOpenStart(10, true);

        Assert.IsFalse(left.DoesNotExtendRightOf(right));
    }
}
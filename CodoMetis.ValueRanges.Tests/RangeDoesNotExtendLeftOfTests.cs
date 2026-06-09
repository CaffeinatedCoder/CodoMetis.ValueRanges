namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeDoesNotExtendLeftOfTests
{
    [TestMethod]
    public void DoesNotExtendLeftOf_ReceiverStartsClearlyLater_ReturnsTrue()
    {
        // int8range(7,20) &> int8range(5,10) → t  (Postgres example)
        var left  = Int32Range.CreateFinite(7, 20, true, false); // [7, 20)
        var right = Int32Range.CreateFinite(5, 10, true, false); // [5, 10)

        Assert.IsTrue(left.DoesNotExtendLeftOf(right));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_SameLowerBound_BothInclusive_ReturnsTrue()
    {
        // [5, 20] &> [5, 10] — same lower bound, both inclusive
        var left  = Int32Range.CreateFinite(5, 20, true, true);
        var right = Int32Range.CreateFinite(5, 10, true, true);

        Assert.IsTrue(left.DoesNotExtendLeftOf(right));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_SameLowerBound_ReceiverInclusiveOtherExclusive_ReturnsFalse()
    {
        // [5, 20] &> (5, 10] — receiver claims 5, other starts after 5 → receiver extends further left
        var left  = Int32Range.CreateFinite(5, 20, true,  true); // [5, 20]
        var right = Int32Range.CreateFinite(5, 10, false, true); // (5, 10]

        Assert.IsFalse(left.DoesNotExtendLeftOf(right));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_SameLowerBound_BothExclusive_ReturnsTrue()
    {
        // (5, 20] &> (5, 10] — same exclusive lower bound
        var left  = Int32Range.CreateFinite(5, 20, false, true);
        var right = Int32Range.CreateFinite(5, 10, false, true);

        Assert.IsTrue(left.DoesNotExtendLeftOf(right));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_ReceiverStartsEarlier_ReturnsFalse()
    {
        var left  = Int32Range.CreateFinite(1, 20, true, true);
        var right = Int32Range.CreateFinite(5, 10, true, true);

        Assert.IsFalse(left.DoesNotExtendLeftOf(right));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_OpenStart_AlwaysReturnsFalse()
    {
        // An OpenStart range extends to -∞, so it always extends left of anything finite
        var openStart = Int32Range.CreateOpenStart(10, true);
        var finite    = Int32Range.CreateFinite(1, 5, true, true);

        Assert.IsFalse(openStart.DoesNotExtendLeftOf(finite));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_FiniteVsOpenStart_ReturnsTrue()
    {
        // Any finite range does not extend left of an OpenStart range
        var finite    = Int32Range.CreateFinite(1, 10, true, true);
        var openStart = Int32Range.CreateOpenStart(10, true);

        Assert.IsTrue(finite.DoesNotExtendLeftOf(openStart));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_OpenEndVsOpenEnd_SameBound_BothInclusive_ReturnsTrue()
    {
        // [5, +∞) &> [5, +∞) — same lower bound, both inclusive
        var left  = Int32Range.CreateOpenEnd(5, true);
        var right = Int32Range.CreateOpenEnd(5, true);

        Assert.IsTrue(left.DoesNotExtendLeftOf(right));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_OpenEndVsOpenEnd_ReceiverStartsEarlier_ReturnsFalse()
    {
        // [1, +∞) &> [5, +∞) → false
        var left  = Int32Range.CreateOpenEnd(1, true);
        var right = Int32Range.CreateOpenEnd(5, true);

        Assert.IsFalse(left.DoesNotExtendLeftOf(right));
    }
}
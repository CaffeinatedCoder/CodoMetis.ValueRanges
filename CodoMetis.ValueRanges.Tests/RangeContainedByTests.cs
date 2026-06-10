namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class RangeContainedByTests
{
    [TestMethod]
    public void IsContainedBy_InnerStrictlyInsideOuter_ReturnsTrue()
    {
        var inner = Int32Range.CreateFinite(3, 7,  true, true); // [3, 7]
        var outer = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]

        Assert.IsTrue(inner.IsContainedBy(outer));
    }

    [TestMethod]
    public void IsContainedBy_EqualRanges_ReturnsTrue()
    {
        var a = Int32Range.CreateFinite(1, 10, true, true);
        var b = Int32Range.CreateFinite(1, 10, true, true);

        Assert.IsTrue(a.IsContainedBy(b));
        Assert.IsTrue(b.IsContainedBy(a));
    }

    [TestMethod]
    public void IsContainedBy_InnerExceedsOuter_ReturnsFalse()
    {
        var inner = Int32Range.CreateFinite(1, 15, true, true); // [1, 15]
        var outer = Int32Range.CreateFinite(1, 10, true, true); // [1, 10]

        Assert.IsFalse(inner.IsContainedBy(outer));
    }

    [TestMethod]
    public void IsContainedBy_IsSymmetricInverseOfContains()
    {
        var inner = Int32Range.CreateFinite(3, 7,  true, true);
        var outer = Int32Range.CreateFinite(1, 10, true, true);

        // outer.Contains(inner) ↔ inner.IsContainedBy(outer)
        Assert.AreEqual(outer.Contains(inner), inner.IsContainedBy(outer));
    }

    [TestMethod]
    public void IsContainedBy_FiniteContainedByOpenStart_ReturnsTrue()
    {
        var inner     = Int32Range.CreateFinite(1, 7,  true, true); // [1, 7]
        var openStart = Int32Range.CreateUnboundedStart(10, true);   // (-∞, 10]

        Assert.IsTrue(inner.IsContainedBy(openStart));
    }

    [TestMethod]
    public void IsContainedBy_FiniteContainedByOpenEnd_ReturnsTrue()
    {
        var inner   = Int32Range.CreateFinite(5, 15, true, true); // [5, 15]
        var openEnd = Int32Range.CreateUnboundedEnd(1, true);      // [1, +∞)

        Assert.IsTrue(inner.IsContainedBy(openEnd));
    }
}
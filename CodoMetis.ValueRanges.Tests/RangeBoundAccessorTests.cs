using DateSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DateRange, System.DateOnly>;
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Covers <c>LowerBound</c>/<c>UpperBound</c>/<c>LowerBoundInclusive</c>/<c>UpperBoundInclusive</c> —
/// the PostgreSQL <c>lower</c>/<c>upper</c>/<c>lower_inc</c>/<c>upper_inc</c> equivalents — on all
/// five range shapes and on <see cref="RangeSet{TRange,T}"/>.
/// </summary>
[TestClass]
public class RangeBoundAccessorTests
{
    // -------------------------------------------------------------------------
    // Discrete range (Int32Range) — canonicalized to closed [lower, upper]
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Finite_Discrete_ReturnsBothBounds_Inclusive()
    {
        var range = Int32Range.CreateFinite(1, 10); // [1, 10]

        Assert.AreEqual(1, range.LowerBound());
        Assert.AreEqual(10, range.UpperBound());
        Assert.IsTrue(range.LowerBoundInclusive());
        Assert.IsTrue(range.UpperBoundInclusive());
    }

    [TestMethod]
    public void Finite_Discrete_ExclusiveInput_CanonicalizedBounds()
    {
        var range = Int32Range.CreateFinite(1, 10, false, false); // (1, 10) ≡ [2, 9]

        Assert.AreEqual(2, range.LowerBound());
        Assert.AreEqual(9, range.UpperBound());
        Assert.IsTrue(range.LowerBoundInclusive());
        Assert.IsTrue(range.UpperBoundInclusive());
    }

    [TestMethod]
    public void UnboundedStart_Discrete_LowerIsNull()
    {
        var range = Int32Range.CreateUnboundedStart(5, endInclusive: true); // (,5]

        Assert.IsNull(range.LowerBound());
        Assert.AreEqual(5, range.UpperBound());
        Assert.IsFalse(range.LowerBoundInclusive());
        Assert.IsTrue(range.UpperBoundInclusive());
    }

    [TestMethod]
    public void UnboundedEnd_Discrete_UpperIsNull()
    {
        var range = Int32Range.CreateUnboundedEnd(3); // [3,)

        Assert.AreEqual(3, range.LowerBound());
        Assert.IsNull(range.UpperBound());
        Assert.IsTrue(range.LowerBoundInclusive());
        Assert.IsFalse(range.UpperBoundInclusive());
    }

    [TestMethod]
    public void Empty_AllAccessorsReturnNullOrFalse()
    {
        var range = Int32Range.Empty;

        Assert.IsNull(range.LowerBound());
        Assert.IsNull(range.UpperBound());
        Assert.IsFalse(range.LowerBoundInclusive());
        Assert.IsFalse(range.UpperBoundInclusive());
    }

    [TestMethod]
    public void Infinity_AllAccessorsReturnNullOrFalse()
    {
        var range = Int32Range.Infinite;

        Assert.IsNull(range.LowerBound());
        Assert.IsNull(range.UpperBound());
        Assert.IsFalse(range.LowerBoundInclusive());
        Assert.IsFalse(range.UpperBoundInclusive());
    }

    // -------------------------------------------------------------------------
    // Continuous range (DecimalRange) — inclusiveness preserved as constructed
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Finite_Continuous_HalfOpenDefault()
    {
        var range = DecimalRange.CreateFinite(1.5m, 9.9m); // [1.5, 9.9)

        Assert.AreEqual(1.5m, range.LowerBound());
        Assert.AreEqual(9.9m, range.UpperBound());
        Assert.IsTrue(range.LowerBoundInclusive());
        Assert.IsFalse(range.UpperBoundInclusive());
    }

    [TestMethod]
    public void Finite_Continuous_AllInclusivenessPermutations()
    {
        foreach (var (startInclusive, endInclusive) in
                 new[] { (true, true), (true, false), (false, true), (false, false) })
        {
            var range = DecimalRange.CreateFinite(1m, 2m, startInclusive, endInclusive);

            Assert.AreEqual(startInclusive, range.LowerBoundInclusive());
            Assert.AreEqual(endInclusive, range.UpperBoundInclusive());
        }
    }

    [TestMethod]
    public void UnboundedShapes_Continuous_PreserveInclusiveness()
    {
        var unboundedStart = DecimalRange.CreateUnboundedStart(5m, endInclusive: false); // (,5)
        var unboundedEnd   = DecimalRange.CreateUnboundedEnd(3m, startInclusive: false); // (3,)

        Assert.IsNull(unboundedStart.LowerBound());
        Assert.AreEqual(5m, unboundedStart.UpperBound());
        Assert.IsFalse(unboundedStart.UpperBoundInclusive());

        Assert.AreEqual(3m, unboundedEnd.LowerBound());
        Assert.IsNull(unboundedEnd.UpperBound());
        Assert.IsFalse(unboundedEnd.LowerBoundInclusive());
    }

    [TestMethod]
    public void DateRange_ReturnsDateBounds()
    {
        var range = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        Assert.AreEqual(new DateOnly(2024, 1, 1), range.LowerBound());
        Assert.AreEqual(new DateOnly(2024, 12, 31), range.UpperBound());
    }

    // -------------------------------------------------------------------------
    // RangeSet — first element's lower bound, last element's upper bound
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeSet_MultiElement_SpansFirstToLast()
    {
        var set = IntSet.From([
            Int32Range.CreateFinite(5, 7),
            Int32Range.CreateFinite(1, 3)
        ]); // {[1,3], [5,7]}

        Assert.AreEqual(1, set.LowerBound());
        Assert.AreEqual(7, set.UpperBound());
        Assert.IsTrue(set.LowerBoundInclusive());
        Assert.IsTrue(set.UpperBoundInclusive());
    }

    [TestMethod]
    public void RangeSet_Empty_AllAccessorsReturnNullOrFalse()
    {
        Assert.IsNull(IntSet.Empty.LowerBound());
        Assert.IsNull(IntSet.Empty.UpperBound());
        Assert.IsFalse(IntSet.Empty.LowerBoundInclusive());
        Assert.IsFalse(IntSet.Empty.UpperBoundInclusive());
    }

    [TestMethod]
    public void RangeSet_Infinite_AllAccessorsReturnNullOrFalse()
    {
        Assert.IsNull(IntSet.Infinite.LowerBound());
        Assert.IsNull(IntSet.Infinite.UpperBound());
        Assert.IsFalse(IntSet.Infinite.LowerBoundInclusive());
        Assert.IsFalse(IntSet.Infinite.UpperBoundInclusive());
    }

    [TestMethod]
    public void RangeSet_UnboundedStartElement_LowerIsNull()
    {
        var set = DateSet.From([
            DateRange.CreateUnboundedStart(new DateOnly(2024, 3, 1), endInclusive: true),
            DateRange.CreateFinite(new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 30))
        ]); // {(,2024-03-01], [2024-06-01,2024-06-30]}

        Assert.IsNull(set.LowerBound());
        Assert.AreEqual(new DateOnly(2024, 6, 30), set.UpperBound());
        Assert.IsFalse(set.LowerBoundInclusive());
        Assert.IsTrue(set.UpperBoundInclusive());
    }
}

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Covers <c>Deconstruct</c> on the <c>Finite</c> variants, which are not positional records and
/// so do not get one from the compiler. These tests are mostly a compile check of the syntax;
/// <c>VariantDeconstructContractTests</c> (conventions) holds every variant of every type to its
/// constructor.
/// </summary>
[TestClass]
public class RangeDeconstructTests
{
    [TestMethod]
    public void Finite_Discrete_DeconstructsIntoClosedBounds()
    {
        var finite = (Int32Range.Finite)Int32Range.CreateFinite(1, 10, endInclusive: false); // [1, 10) ≡ [1, 9]

        var (start, end) = finite;

        Assert.AreEqual(1, start);
        Assert.AreEqual(9, end);
    }

    [TestMethod]
    public void Finite_Discrete_RoundTripsThroughCreateFinite()
    {
        var range = DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), startInclusive: false);

        var (start, end) = (DateRange.Finite)range;

        Assert.AreEqual(range, DateRange.CreateFinite(start, end));
    }

    [TestMethod]
    public void Finite_Continuous_DeconstructsIntoBoundsAndInclusiveness()
    {
        var finite = (DecimalRange.Finite)DecimalRange.CreateFinite(1.5m, 2m, startInclusive: false, endInclusive: true);

        var (start, end, startInclusive, endInclusive) = finite;

        Assert.AreEqual(1.5m, start);
        Assert.AreEqual(2m, end);
        Assert.IsFalse(startInclusive);
        Assert.IsTrue(endInclusive);
        Assert.AreEqual(finite, DecimalRange.CreateFinite(start, end, startInclusive, endInclusive));
    }

    [TestMethod]
    public void Finite_PositionalPattern_MatchesTheBounds()
    {
        var range = DateTimeRange.CreateFinite(new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));

        if (range is not DateTimeRange.Finite(var start, var end, true, false))
        {
            Assert.Fail($"{range} did not match Finite(start, end, true, false).");
            return;
        }

        Assert.AreEqual(new DateTime(2026, 1, 1), start);
        Assert.AreEqual(new DateTime(2026, 1, 2), end);
    }
}

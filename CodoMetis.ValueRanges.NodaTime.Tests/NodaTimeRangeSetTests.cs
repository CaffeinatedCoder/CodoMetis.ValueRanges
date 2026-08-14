using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

using DateSet    = RangeSet<LocalDateRange, LocalDate>;
using InstantRangeSet = RangeSet<InstantRange, Instant>;

[TestClass]
public class NodaTimeRangeSetTests
{
    private static LocalDate D(int year, int month, int day) => new(year, month, day);
    private static Instant I(int year, int month, int day) => Instant.FromUtc(year, month, day, 0, 0);

    [TestMethod]
    public void From_AdjacentDates_MergeViaDiscreteStep()
    {
        // [Jan] + [Feb] are adjacent for LocalDate (one day apart) and merge
        var set = DateSet.From([
            LocalDateRange.CreateFinite(D(2025, 2, 1), D(2025, 2, 28)),
            LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 31)),
            LocalDateRange.CreateFinite(D(2025, 6, 1), D(2025, 6, 30))
        ]);

        Assert.AreEqual(2, set.Count);
        Assert.AreEqual("{[2025-01-01,2025-02-28],[2025-06-01,2025-06-30]}", set.ToString());
    }

    [TestMethod]
    public void Contains_PointAndRange()
    {
        var set = DateSet.From([
            LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 31)),
            LocalDateRange.CreateFinite(D(2025, 6, 1), D(2025, 6, 30))
        ]);

        Assert.IsTrue(set.Contains(D(2025, 1, 15)));
        Assert.IsFalse(set.Contains(D(2025, 3, 15)));
        Assert.IsTrue(set.Contains(LocalDateRange.CreateFinite(D(2025, 6, 5), D(2025, 6, 10))));
    }

    [TestMethod]
    public void Union_BridgesGap()
    {
        var set = DateSet.From([
            LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 10)),
            LocalDateRange.CreateFinite(D(2025, 1, 20), D(2025, 1, 31))
        ]);

        var bridged = set.Union(LocalDateRange.CreateFinite(D(2025, 1, 11), D(2025, 1, 19)));
        Assert.AreEqual(1, bridged.Count);
    }

    [TestMethod]
    public void Complement_OfFiniteSet()
    {
        var set        = InstantRangeSet.From([InstantRange.CreateFinite(I(2025, 1, 1), I(2025, 2, 1))]);
        var complement = set.Complement();

        Assert.AreEqual(2, complement.Count);
        Assert.IsInstanceOfType<InstantRange.UnboundedStart>(complement[0]);
        Assert.IsInstanceOfType<InstantRange.UnboundedEnd>(complement[1]);
    }

    [TestMethod]
    public void MultirangeLiteral_RoundTrips()
    {
        var literal = "{[2025-01-01,2025-01-31],[2025-06-01,2025-06-30]}";
        var set     = DateSet.Parse(literal, null);

        Assert.AreEqual(2, set.Count);
        Assert.AreEqual(literal, set.ToString());

        Assert.AreEqual("{}",    DateSet.Empty.ToString());
        Assert.AreEqual("{(,)}", DateSet.Infinite.ToString());
    }

    [TestMethod]
    public void StructuralEquality_AfterNormalization()
    {
        var a = DateSet.From([LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 31))]);
        var b = DateSet.From([
            LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 15)),
            LocalDateRange.CreateFinite(D(2025, 1, 16), D(2025, 1, 31))
        ]);

        Assert.IsTrue(a == b);
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void RangeAgg_NormalizesInputs()
    {
        var set = new[]
        {
            LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 10)),
            LocalDateRange.CreateFinite(D(2025, 1, 5), D(2025, 1, 20)),
            LocalDateRange.CreateFinite(D(2025, 3, 1), D(2025, 3, 10))
        }.RangeAgg();

        Assert.AreEqual(2, set.Count);
        Assert.AreEqual("{[2025-01-01,2025-01-20],[2025-03-01,2025-03-10]}", set.ToString());
    }

    [TestMethod]
    public void RangeIntersectAgg_FoldsToCommonIntersection()
    {
        var common = new[]
        {
            InstantRange.CreateFinite(I(2025, 1, 1), I(2025, 6, 1)),
            InstantRange.CreateFinite(I(2025, 3, 1), I(2025, 9, 1))
        }.RangeIntersectAgg();

        Assert.AreEqual("[2025-03-01T00:00:00Z,2025-06-01T00:00:00Z)", common!.ToString());

        Assert.IsNull(Array.Empty<LocalDateTimeRange>().RangeIntersectAgg());
    }
}

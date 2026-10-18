namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// <c>Split</c> and the stepping <c>Values</c> with the examples <c>docs/ranges.md</c> shows.
/// <c>RangeSplitOracleTests</c> sweeps every range type, shape and step.
/// </summary>
[TestClass]
public class RangeSplitTests
{
    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static string Text<TRange>(IEnumerable<TRange> pieces) => string.Join(" ", pieces);

    [TestMethod]
    public void Split_Integers_LastPieceClipped()
    {
        Assert.AreEqual("[1,4] [5,8] [9,10]", Text(Int32Range.CreateFinite(1, 10).Split(4)));
        Assert.AreEqual("[1,10]", Text(Int32Range.CreateFinite(1, 10).Split(10)));
        Assert.AreEqual("[1,9] [10,10]", Text(Int32Range.CreateFinite(1, 10).Split(9)));
        CollectionAssert.AreEqual(new[] { 1, 4, 7, 10 }, Int32Range.CreateFinite(1, 10).Values(3).ToArray());
    }

    [TestMethod]
    public void Split_Continuous_PiecesAreHalfOpen_AndTheLastTakesTheEnd()
    {
        Assert.AreEqual("[0,4) [4,8) [8,10)", Text(DecimalRange.CreateFinite(0m, 10m).Split(4m)));
        Assert.AreEqual("[0,5) [5,10]", Text(DecimalRange.CreateFinite(0m, 10m, true, true).Split(5m)));
        Assert.AreEqual("(0,5) [5,10)", Text(DecimalRange.CreateFinite(0m, 10m, false, false).Split(5m)));
    }

    [TestMethod]
    public void Split_OpeningHours_IntoHalfHourSlots()
    {
        var hours = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 30));
        var slots = hours.Split(TimeSpan.FromMinutes(30)).ToList();

        Assert.AreEqual(17, slots.Count);
        Assert.AreEqual(TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(9, 30)), slots[0]);
        Assert.AreEqual(TimeRange.CreateFinite(new TimeOnly(17, 0), new TimeOnly(17, 30)), slots[^1]);
    }

    [TestMethod]
    public void Split_Dates_ByDays_AndValuesByDays()
    {
        var june = DateRange.Month(2026, 6);

        Assert.AreEqual("[2026-06-01,2026-06-07] [2026-06-08,2026-06-14] [2026-06-15,2026-06-21] [2026-06-22,2026-06-28] [2026-06-29,2026-06-30]",
                        Text(june.Split(7)));
        CollectionAssert.AreEqual(new[] { D(6, 1), D(6, 8), D(6, 15), D(6, 22), D(6, 29) }, june.Values(7).ToArray());
    }

    [TestMethod]
    public void Split_ByCalendarPeriod_BillingMonthsAndIsoWeeks()
    {
        var contract = DateRange.CreateFinite(D(1, 15), D(4, 14));
        Assert.AreEqual("[2026-01-15,2026-01-31] [2026-02-01,2026-02-28] [2026-03-01,2026-03-31] [2026-04-01,2026-04-14]",
                        Text(contract.Split(DateRange.Month)));

        var weeks = DateRange.Quarter(2026, 1).Split(DateRange.IsoWeek).ToList();
        Assert.AreEqual(14, weeks.Count);
        Assert.AreEqual(DateRange.CreateFinite(D(1, 1), D(1, 4)), weeks[0], "the quarter starts on a Thursday");
        Assert.AreEqual(DateRange.IsoWeek(2026, 2), weeks[1]);
        Assert.AreEqual(DateRange.CreateFinite(D(3, 30), D(3, 31)), weeks[^1]);
    }

    [TestMethod]
    public void Split_Rejections_AtTheCall()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => Int32Range.CreateUnboundedEnd(1).Split(2));
        Assert.ThrowsExactly<NotSupportedException>(() => DateRange.Infinite.Split(DateRange.Month));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Int32Range.CreateFinite(1, 10).Split(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.Month(2026, 6).Values(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(10, 0)).Split(TimeSpan.Zero));
        Assert.IsFalse(Int32Range.Empty.Split(3).Any());

        // A period that does not hold the day it is given would never finish.
        Assert.ThrowsExactly<ArgumentException>(() => DateRange.Month(2026, 6).Split(day => DateRange.Month(2026, 1)).ToList());
    }

    [TestMethod]
    public void Split_DecimalNearItsMaximum_AStepTooSmallToMoveThrows()
    {
        var top = DecimalRange.CreateFinite(decimal.MaxValue - 3m, decimal.MaxValue, true, true);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => top.Split(0.5m));
        Assert.AreEqual(3, top.Split(1m).Count(), "[max-3,max-2), [max-2,max-1), [max-1,max]");
    }

    /// <summary>
    /// From a negative start, <c>n × step</c> overflows a step before <c>start + n × step</c> does. The
    /// walk ended there, silently dropping the last values and widening the last piece (#219).
    /// </summary>
    [TestMethod]
    public void Split_AStepProductPastTheMaximum_StillReachesTheEnd()
    {
        var wide = Int64Range.CreateFinite(-9_000_000_000_000_000_000, 9_000_000_000_000_000_000);

        CollectionAssert.AreEqual(
            new[] { -9_000_000_000_000_000_000, -5_000_000_000_000_000_000, -1_000_000_000_000_000_000, 3_000_000_000_000_000_000, 7_000_000_000_000_000_000 },
            wide.Values(4_000_000_000_000_000_000).ToArray());
        Assert.AreEqual("[7000000000000000000,9000000000000000000]", wide.Split(4_000_000_000_000_000_000).Last().ToString());

        Assert.AreEqual(5, DecimalRange.CreateFinite(-7e28m, 7e28m).Split(3e28m).Count(), "-7e28, -4e28, -1e28, 2e28, 5e28");
        Assert.AreEqual(4, Float64Range.CreateFinite(-1.7e308, 1.7e308).Split(1e308).Count(), "-1.7e308, -0.7e308, 0.3e308, 1.3e308");
    }

    [TestMethod]
    public void Split_CanBeEnumeratedTwice()
    {
        var pieces = Int32Range.CreateFinite(1, 10).Split(4);

        Assert.AreEqual(Text(pieces), Text(pieces));
        Assert.AreEqual(Text(DateRange.Month(2026, 6).Split(DateRange.IsoWeek)), Text(DateRange.Month(2026, 6).Split(DateRange.IsoWeek)));
    }

    [TestMethod]
    public void Split_ReachesTheDomainEnd()
    {
        Assert.AreEqual("[2147483646,2147483647]", Text(Int32Range.CreateFinite(int.MaxValue - 1, int.MaxValue).Split(5)));
        Assert.AreEqual("[9999-12-01,9999-12-31]", Text(DateRange.CreateFinite(new DateOnly(9999, 12, 1), DateOnly.MaxValue).Split(DateRange.Month)));
    }
}

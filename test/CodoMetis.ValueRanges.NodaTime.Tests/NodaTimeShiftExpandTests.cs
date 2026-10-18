using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

/// <summary>
/// <c>Shift</c> and <c>Expand</c> on the NodaTime ranges, where the step is a <see cref="Period"/>, a
/// <see cref="Duration"/> or a number of months. <c>RangeArithmeticOracleTests</c> sweeps every shape;
/// these pin what only NodaTime's own arithmetic decides.
/// </summary>
[TestClass]
public class NodaTimeShiftExpandTests
{
    [TestMethod]
    public void LocalDateRange_ShiftByWeeks_RoundTrips()
    {
        var week = LocalDateRange.CreateFinite(new LocalDate(2026, 6, 1), new LocalDate(2026, 6, 7));

        Assert.AreEqual("[2026-06-08,2026-06-14]", week.Shift(Period.FromWeeks(1)).ToString());
        Assert.AreEqual(week, week.Shift(Period.FromWeeks(1)).Shift(Period.FromWeeks(-1)));
        Assert.AreEqual("[2026-05-31,2026-06-08]", week.Expand(Period.FromDays(1)).ToString());
    }

    /// <summary>
    /// A period of months moves the exclusive end, the day after the last, so a whole month lands on a
    /// whole month, as the <c>daterange</c> <c>[2026-02-01,2026-03-01)</c> moves in PostgreSQL and as
    /// <c>LocalDateTimeRange</c> moves. Moving the closed last day instead turned February into
    /// March 1 to 28 (#223). Every month of a leap and a non-leap year, both directions.
    /// </summary>
    [TestMethod]
    [DataRow(2024)]
    [DataRow(2026)]
    public void LocalDateRange_ShiftByMonths_MovesAWholeMonthToAWholeMonth(int year)
    {
        for (var month = 1; month <= 12; month++)
        {
            var current = LocalDateRange.Month(year, month);
            var next    = LocalDateRange.Month(new YearMonth(year, month).PlusMonths(1).OnDayOfMonth(1));
            var before  = LocalDateRange.Month(new YearMonth(year, month).PlusMonths(-1).OnDayOfMonth(1));

            Assert.AreEqual(next, current.Shift(Period.FromMonths(1)), $"{current} + P1M");
            Assert.AreEqual(before, current.Shift(Period.FromMonths(-1)), $"{current} - P1M");
            Assert.AreEqual(current, current.Shift(Period.FromMonths(1)).Shift(Period.FromMonths(-1)), $"{current} round trip");
        }

        for (var quarter = 1; quarter <= 4; quarter++)
        {
            var current = LocalDateRange.Quarter(year, quarter);
            var next    = quarter == 4 ? LocalDateRange.Quarter(year + 1, 1) : LocalDateRange.Quarter(year, quarter + 1);
            Assert.AreEqual(next, current.Shift(Period.FromMonths(3)), $"{current} + P3M");
        }

        Assert.AreEqual(LocalDateRange.Year(year + 1), LocalDateRange.Year(year).Shift(Period.FromYears(1)));
    }

    [TestMethod]
    public void LocalDateRange_ExpandByMonths_GrowsByWholeMonths()
    {
        Assert.AreEqual("[2026-01-01,2026-03-31]", LocalDateRange.Month(2026, 2).Expand(Period.FromMonths(1)).ToString());
        Assert.AreEqual("[2024-01-01,2024-03-31]", LocalDateRange.Month(2024, 2).Expand(Period.FromMonths(1)).ToString());
        Assert.AreEqual(LocalDateRange.Month(2026, 2), LocalDateRange.Quarter(2026, 1).Expand(Period.FromMonths(-1)));
    }

    [TestMethod]
    public void LocalDateRange_ShiftByMonths_AStartOnThe31stStillClips()
    {
        var range = LocalDateRange.CreateFinite(new LocalDate(2026, 1, 31), new LocalDate(2026, 2, 27));

        // The start clips to the shorter month; the end moves through its exclusive end: February 28 plus a month, less a day.
        Assert.AreEqual("[2026-02-28,2026-03-27]", range.Shift(Period.FromMonths(1)).ToString());
    }

    [TestMethod]
    public void YearMonthRange_ShiftByMonths_MovesAWholeQuarter()
    {
        for (var quarter = 1; quarter <= 4; quarter++)
            Assert.AreEqual(quarter == 4 ? YearMonthRange.Quarter(2027, 1) : YearMonthRange.Quarter(2026, quarter + 1),
                            YearMonthRange.Quarter(2026, quarter).Shift(3));
    }

    [TestMethod]
    public void LocalDateRange_PeriodWithATimePart_Throws()
    {
        var day = LocalDateRange.CreateFinite(new LocalDate(2026, 6, 1), new LocalDate(2026, 6, 1));

        Assert.ThrowsExactly<ArgumentException>(() => day.Shift(Period.FromHours(1)));
    }

    [TestMethod]
    public void InstantRange_ExpandByADuration()
    {
        var call = InstantRange.CreateFinite(Instant.FromUtc(2026, 6, 1, 9, 0), Instant.FromUtc(2026, 6, 1, 10, 0));

        Assert.AreEqual(InstantRange.CreateFinite(Instant.FromUtc(2026, 6, 1, 8, 45), Instant.FromUtc(2026, 6, 1, 10, 15)),
                        call.Expand(Duration.FromMinutes(15)));
    }

    [TestMethod]
    public void YearMonthRange_ShiftByMonths()
    {
        var quarter = YearMonthRange.CreateFinite(new YearMonth(2026, 1), new YearMonth(2026, 3));

        Assert.AreEqual(YearMonthRange.CreateFinite(new YearMonth(2027, 1), new YearMonth(2027, 3)), quarter.Shift(12));
        Assert.AreEqual(YearMonthRange.CreateFinite(new YearMonth(2025, 12), new YearMonth(2026, 4)), quarter.Expand(1));
        Assert.ThrowsExactly<OverflowException>(() => YearMonthRange.CreateUnboundedStart(new YearMonth(9999, 11), true).Shift(2));

        // 9999-12 is the last month of the ISO calendar, stored as infinity: it stays (#224).
        Assert.AreEqual(YearMonthRange.CreateUnboundedStart(new YearMonth(9999, 12), true),
                        YearMonthRange.CreateUnboundedStart(new YearMonth(9999, 12), true).Shift(1));
    }

    [TestMethod]
    public void LocalDateTimeRange_ShiftKeepsTheWallClock()
    {
        var slot = LocalDateTimeRange.CreateFinite(new LocalDateTime(2026, 3, 28, 9, 0), new LocalDateTime(2026, 3, 28, 10, 0));

        Assert.AreEqual(LocalDateTimeRange.CreateFinite(new LocalDateTime(2026, 3, 29, 9, 0), new LocalDateTime(2026, 3, 29, 10, 0)),
                        slot.Shift(Period.FromDays(1)));
    }
}

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

    [TestMethod]
    public void LocalDateRange_ShiftByMonths_ClipsToTheMonthEnd()
    {
        var january = LocalDateRange.CreateFinite(new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31));

        var february = january.Shift(Period.FromMonths(1));
        Assert.AreEqual("[2026-02-01,2026-02-28]", february.ToString());

        // Calendar arithmetic is not reversible: moving back gives January 1 to 28.
        Assert.AreEqual("[2026-01-01,2026-01-28]", february.Shift(Period.FromMonths(-1)).ToString());
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
        Assert.ThrowsExactly<OverflowException>(() => YearMonthRange.CreateUnboundedStart(new YearMonth(9999, 12), true).Shift(1));
    }

    [TestMethod]
    public void LocalDateTimeRange_ShiftKeepsTheWallClock()
    {
        var slot = LocalDateTimeRange.CreateFinite(new LocalDateTime(2026, 3, 28, 9, 0), new LocalDateTime(2026, 3, 28, 10, 0));

        Assert.AreEqual(LocalDateTimeRange.CreateFinite(new LocalDateTime(2026, 3, 29, 9, 0), new LocalDateTime(2026, 3, 29, 10, 0)),
                        slot.Shift(Period.FromDays(1)));
    }
}

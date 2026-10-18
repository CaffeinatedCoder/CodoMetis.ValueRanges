using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

/// <summary>
/// <c>Split</c> and the stepping <c>Values</c> on the NodaTime ranges: periods of months counted from
/// the start, the calendar split, and the steps a NodaTime type rejects.
/// </summary>
[TestClass]
public class NodaTimeSplitTests
{
    private static string Text<TRange>(IEnumerable<TRange> pieces) => string.Join(" ", pieces);

    [TestMethod]
    public void LocalDateRange_SplitByMonths_CountsFromTheStart()
    {
        var range = LocalDateRange.CreateFinite(new LocalDate(2026, 1, 31), new LocalDate(2026, 5, 15));

        Assert.AreEqual("[2026-01-31,2026-02-27] [2026-02-28,2026-03-30] [2026-03-31,2026-04-29] [2026-04-30,2026-05-15]",
                        Text(range.Split(Period.FromMonths(1))));
        Assert.AreEqual("2026-01-31 2026-02-28 2026-03-31 2026-04-30",
                        string.Join(" ", range.Values(Period.FromMonths(1)).Select(day => day.ToString("yyyy-MM-dd", null))));
    }

    [TestMethod]
    public void LocalDateRange_SplitByCalendarPeriod()
    {
        var contract = LocalDateRange.CreateFinite(new LocalDate(2026, 1, 15), new LocalDate(2026, 4, 14));

        Assert.AreEqual("[2026-01-15,2026-01-31] [2026-02-01,2026-02-28] [2026-03-01,2026-03-31] [2026-04-01,2026-04-14]",
                        Text(contract.Split(LocalDateRange.Month)));
    }

    [TestMethod]
    public void LocalDateRange_StepThatDoesNotMoveForward_Throws()
    {
        var range = LocalDateRange.Month(2026, 6);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => range.Split(Period.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => range.Split(Period.FromMonths(1) - Period.FromDays(1)));
        Assert.ThrowsExactly<ArgumentException>(() => range.Split(Period.FromHours(12)), "a date has no hours");
    }

    [TestMethod]
    public void InstantRange_AndLocalDateTimeRange_SplitIntoSlots()
    {
        var call = InstantRange.CreateFinite(Instant.FromUtc(2026, 6, 1, 9, 0), Instant.FromUtc(2026, 6, 1, 10, 0));
        Assert.AreEqual(4, call.Split(Duration.FromMinutes(15)).Count());

        var shift = LocalDateTimeRange.CreateFinite(new LocalDateTime(2026, 6, 1, 22, 0), new LocalDateTime(2026, 6, 2, 6, 0));
        Assert.AreEqual(2, shift.Split(Period.FromHours(4)).Count());
    }

    [TestMethod]
    public void YearMonthRange_SplitByQuarter_AndValuesByMonths()
    {
        var year = YearMonthRange.Year(2026);

        Assert.AreEqual("[2026-01,2026-03] [2026-04,2026-06] [2026-07,2026-09] [2026-10,2026-12]", Text(year.Split(YearMonthRange.Quarter)));
        Assert.AreEqual("[2026-01,2026-05] [2026-06,2026-10] [2026-11,2026-12]", Text(year.Split(5)));
        Assert.AreEqual(4, year.Values(3).Count());
    }
}

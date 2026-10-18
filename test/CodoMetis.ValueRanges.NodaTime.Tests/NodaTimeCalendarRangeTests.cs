using NodaTime;
using NodaTime.Calendars;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

/// <summary>
/// The calendar factories of <see cref="LocalDateRange"/> and <see cref="YearMonthRange"/>.
/// <c>CalendarRangeParityTests</c> holds them equal to <c>DateRange</c>'s over three decades.
/// </summary>
[TestClass]
public class NodaTimeCalendarRangeTests
{
    [TestMethod]
    public void LocalDateRange_AsDocumented()
    {
        Assert.AreEqual("[2026-10-01,2026-10-31]", LocalDateRange.Month(2026, 10).ToString());
        Assert.AreEqual("[2026-10-01,2026-12-31]", LocalDateRange.Quarter(2026, 4).ToString());
        Assert.AreEqual("[2026-01-01,2026-12-31]", LocalDateRange.Year(2026).ToString());
        Assert.AreEqual("[2025-12-29,2026-01-04]", LocalDateRange.IsoWeek(2026, 1).ToString());
        Assert.AreEqual(LocalDateRange.IsoWeek(2026, 53), LocalDateRange.IsoWeek(new LocalDate(2027, 1, 1)));
    }

    [TestMethod]
    public void LocalDateRange_ADateInAnotherCalendar_IsReadAsItsIsoDay()
    {
        // Julian 2026-02-20 is ISO 2026-03-05: the ISO month, quarter and week that hold that day.
        var julian = new LocalDate(2026, 2, 20, CalendarSystem.Julian);

        Assert.AreEqual(LocalDateRange.Month(2026, 3), LocalDateRange.Month(julian));
        Assert.AreEqual(LocalDateRange.Quarter(2026, 1), LocalDateRange.Quarter(julian));
        Assert.AreEqual(LocalDateRange.IsoWeek(new LocalDate(2026, 3, 5)), LocalDateRange.IsoWeek(julian));
    }

    [TestMethod]
    public void LocalDateRange_OutOfRange_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LocalDateRange.Month(2026, 13));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LocalDateRange.Quarter(2026, 5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LocalDateRange.IsoWeek(2027, 53));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LocalDateRange.IsoWeek(9999, 52));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LocalDateRange.IsoWeek(-9998, 1), "the week starts before the ISO calendar does");
    }

    [TestMethod]
    public void YearMonthRange_QuarterAndYear()
    {
        Assert.AreEqual("[2026-10,2026-12]", YearMonthRange.Quarter(2026, 4).ToString());
        Assert.AreEqual("[2026-01,2026-12]", YearMonthRange.Year(2026).ToString());
        Assert.AreEqual(YearMonthRange.Quarter(2026, 3), YearMonthRange.Quarter(new YearMonth(2026, 8)));
        Assert.AreEqual(YearMonthRange.Year(2026), YearMonthRange.Year(new YearMonth(2026, 8)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => YearMonthRange.Quarter(2026, 0));
        Assert.ThrowsExactly<ArgumentException>(() => YearMonthRange.Quarter(new YearMonth(2026, 8, CalendarSystem.Julian)));
    }
}

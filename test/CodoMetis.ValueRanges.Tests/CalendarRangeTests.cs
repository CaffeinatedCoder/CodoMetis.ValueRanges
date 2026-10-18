namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The calendar factories of <see cref="DateRange"/> with the examples <c>docs/ranges.md</c> shows,
/// and their argument checks. <c>CalendarRangeParityTests</c> sweeps every day of three decades and
/// both ends of the domain against .NET's ISO calendar and the NodaTime package.
/// </summary>
[TestClass]
public class CalendarRangeTests
{
    [TestMethod]
    public void Month_Quarter_Year_AsDocumented()
    {
        Assert.AreEqual("[2026-10-01,2026-10-31]", DateRange.Month(2026, 10).ToString());
        Assert.AreEqual("[2028-02-01,2028-02-29]", DateRange.Month(2028, 2).ToString());
        Assert.AreEqual("[2026-10-01,2026-12-31]", DateRange.Quarter(2026, 4).ToString());
        Assert.AreEqual("[2026-01-01,2026-12-31]", DateRange.Year(2026).ToString());

        var day = new DateOnly(2026, 8, 15);
        Assert.AreEqual(DateRange.Month(2026, 8), DateRange.Month(day));
        Assert.AreEqual(DateRange.Quarter(2026, 3), DateRange.Quarter(day));
        Assert.AreEqual(DateRange.Year(2026), DateRange.Year(day));
        Assert.AreEqual("[2026-08-10,2026-08-16]", DateRange.IsoWeek(day).ToString());
    }

    [TestMethod]
    public void IsoWeek_StartsOnMonday_AndWeekOneHoldsTheFirstThursday()
    {
        Assert.AreEqual("[2025-12-29,2026-01-04]", DateRange.IsoWeek(2026, 1).ToString());
        Assert.AreEqual("[2026-12-28,2027-01-03]", DateRange.IsoWeek(2026, 53).ToString(), "2026 has 53 ISO weeks");
        Assert.AreEqual(DateRange.IsoWeek(2026, 53), DateRange.IsoWeek(new DateOnly(2027, 1, 1)),
                        "January 1, 2027 belongs to week 53 of 2026");
        Assert.AreEqual(DateRange.IsoWeek(2026, 24), DateRange.IsoWeek(new DateOnly(2026, 6, 14)), "a Sunday ends its week");
        Assert.AreEqual(DateRange.IsoWeek(2026, 25), DateRange.IsoWeek(new DateOnly(2026, 6, 15)), "a Monday starts one");
    }

    [TestMethod]
    public void CalendarRanges_AreRangesLikeAnyOther()
    {
        var booked = DateRange.CreateFinite(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9));

        Assert.AreEqual(2, DateRange.Month(2026, 10).Except(booked).Count);
        Assert.IsTrue(DateRange.Month(2026, 10).IsAdjacentTo(DateRange.Month(2026, 11)));
        Assert.AreEqual(DateRange.Quarter(2026, 4), DateRange.Month(2026, 10).Merge(DateRange.Month(2026, 12)));
    }

    [TestMethod]
    public void OutOfRangeArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.Month(2026, 13));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.Month(0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.Quarter(2026, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.Quarter(2026, 5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.Year(10000));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.IsoWeek(2027, 53), "2027 has 52 ISO weeks");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.IsoWeek(2026, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.IsoWeek(9999, 52), "the week ends in 10000");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DateRange.IsoWeek(DateOnly.MaxValue));
    }

    [TestMethod]
    public void DomainEdges()
    {
        Assert.AreEqual("[0001-01-01,0001-01-07]", DateRange.IsoWeek(DateOnly.MinValue).ToString(), "0001-01-01 is a Monday");
        Assert.AreEqual("[9999-12-01,9999-12-31]", DateRange.Month(DateOnly.MaxValue).ToString());
        Assert.AreEqual("[9999-01-01,9999-12-31]", DateRange.Year(9999).ToString());
    }
}

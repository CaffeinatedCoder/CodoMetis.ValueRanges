using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// <c>DateRange.Month</c>, <c>Quarter</c>, <c>Year</c> and <c>IsoWeek</c> over a column run as SQL (#191):
/// <c>daterange(first, last, '[]')</c>, with memory's first and last periods as constants, so a range at an
/// extreme holds the infinity Npgsql binds for it, and a raise where memory throws. That the server's answers
/// are memory's is <c>CalendarFactoryParityTests</c>' (integration) to show.
/// </summary>
[TestClass]
public sealed class CalendarFactoryTranslationTests
{
    [TestMethod]
    public void MonthOfADate_TruncatesATimestamp_AndTakesTheExtremePeriodsAsConstants()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Where(b => DateRange.Month(b.Day).Contains(b.OtherDay)).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            WHERE daterange(CASE
                WHEN b."Day" <= DATE '0001-01-31' THEN DATE '-infinity'
                WHEN b."Day" >= DATE '9999-12-01' THEN DATE '9999-12-01'
                ELSE date_trunc('month', b."Day"::timestamp)::date
            END, CASE
                WHEN b."Day" >= DATE '9999-12-01' THEN DATE 'infinity'
                WHEN b."Day" <= DATE '0001-01-31' THEN DATE '0001-01-31'
                ELSE CAST(date_trunc('month', b."Day"::timestamp) + '1 month -1 day'::interval AS date)
            END, '[]') @> b."OtherDay"
            """));
    }

    [TestMethod]
    public void IsoWeekOfADate_RaisesForTheWeekPastTheLastDay()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => DateRange.IsoWeek(b.Day)).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            SELECT daterange(CASE
                WHEN b."Day" <= DATE '0001-01-07' THEN DATE '-infinity'
                WHEN b."Day" >= DATE '9999-12-27' THEN DATE '9999-12-27'
                ELSE date_trunc('week', b."Day"::timestamp)::date
            END, CASE
                WHEN b."Day" >= DATE '9999-12-27' THEN CAST('DateRange.IsoWeek: the week of ' || b."Day"::text || ' ends after 9999-12-31' AS date)
                WHEN b."Day" <= DATE '0001-01-07' THEN DATE '0001-01-07'
                ELSE CAST(date_trunc('week', b."Day"::timestamp) + '1 week -1 day'::interval AS date)
            END, '[]')
            """));
    }

    [TestMethod]
    public void MonthOfAYearAndMonth_GuardsTheYear_AndIsNullOverANullArgument()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => DateRange.Month(b.Id, b.Seats.LowerBound()!.Value)).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            SELECT CASE
                WHEN lower(b."Seats") IS NULL THEN NULL
                ELSE daterange(CASE
                    WHEN b."Id" = 1 AND lower(b."Seats") = 1 THEN DATE '-infinity'
                    ELSE make_date(CASE
                        WHEN b."Id" >= 1 AND b."Id" <= 9999 THEN b."Id"
                        ELSE CAST('DateRange.Month: the year ' || b."Id"::text || ' is outside 1 to 9999' AS integer)
                    END, lower(b."Seats"), 1)
                END, CASE
                    WHEN b."Id" = 9999 AND lower(b."Seats") = 12 THEN DATE 'infinity'
                    ELSE CAST(make_date(CASE
                        WHEN b."Id" >= 1 AND b."Id" <= 9999 THEN b."Id"
                        ELSE CAST('DateRange.Month: the year ' || b."Id"::text || ' is outside 1 to 9999' AS integer)
                    END, lower(b."Seats"), 1)::timestamp + '1 month -1 day'::interval AS date)
                END, '[]')
            END
            """));
    }

    [TestMethod]
    public void IsoWeekOfAWeekYearAndWeek_ChecksTheWeekAgainstTheYearsLast_NotTo_date()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => DateRange.IsoWeek(2026, b.Id)).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            WHEN b."Id" >= 1 AND b."Id" <= date_part('week', make_date(2026, 12, 28))::int THEN b."Id"
            ELSE CAST('DateRange.IsoWeek: the week-year ' || 2026::text || ' has no week ' || b."Id"::text AS integer)
            """));
        Assert.DoesNotContain("to_date", sql);
    }

    private static string Normalize(string sql) => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

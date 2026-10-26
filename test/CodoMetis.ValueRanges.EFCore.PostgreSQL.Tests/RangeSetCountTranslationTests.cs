using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// <c>RangeSet.Count</c> runs as SQL (#484): a subquery that counts the ranges <c>unnest</c> returns, built after
/// EF Core's alias pass (<c>MultirangeCount</c>), with a NULL multirange counted as NULL. That the server's
/// answers are memory's is <c>RangeSetCountParityTests</c>' (integration) to show.
/// </summary>
[TestClass]
public sealed class RangeSetCountTranslationTests
{
    [TestMethod]
    public void Count_InAFilter_CountsTheRangesUnnestReturns()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Where(b => b.BlockedDays.Count > 2).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            WHERE (
                SELECT count(*)::int
                FROM unnest(b."BlockedDays") AS vc0(r)) > 2
            """));
    }

    [TestMethod]
    public void Count_OfTwoColumns_GivesEachSubqueryItsOwnAlias()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => new { Days = b.BlockedDays.Count, Seats = b.SeatBlocks.Count }).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize("FROM unnest(b.\"BlockedDays\") AS vc0(r))"));
        StringAssert.Contains(Normalize(sql), Normalize("FROM unnest(b.\"SeatBlocks\") AS vc1(r))"));
    }

    [TestMethod]
    public void Count_OfAComputedMultirange_CountsTheResult()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.OrderBy(b => b.BlockedDays.Gaps().Count).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            ORDER BY (
                SELECT count(*)::int
                FROM unnest(datemultirange(range_merge(b."BlockedDays")) - b."BlockedDays") AS vc0(r))
            """));
    }

    [TestMethod]
    public void Count_OfANullableColumn_IsNullWhenTheColumnIs()
    {
        using var context = new TestDbContext();

        var sql = context.Bookings.Select(b => b.MaybeBlockedDays!.Count).ToQueryString();

        StringAssert.Contains(Normalize(sql), Normalize(
            """
            SELECT CASE
                WHEN b."MaybeBlockedDays" IS NULL THEN NULL
                ELSE (
                    SELECT count(*)::int
                    FROM unnest(b."MaybeBlockedDays") AS vc0(r))
            END
            """));
    }

    private static string Normalize(string sql) => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

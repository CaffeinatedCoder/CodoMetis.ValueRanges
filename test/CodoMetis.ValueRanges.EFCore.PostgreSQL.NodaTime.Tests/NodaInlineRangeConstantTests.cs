using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// A NodaTime range built inline in a query — evaluated by EF Core and inlined as a constant typed by
/// its variant — translates like any other constant. <c>YearMonthRange</c> has its own mapping, which
/// writes the month-aligned date form.
/// </summary>
[TestClass]
public sealed class NodaInlineRangeConstantTests
{
    private static string Where(Func<NodaTestDbContext, IQueryable<Reservation>> query)
    {
        using var context = new NodaTestDbContext();
        var sql = query(context).ToQueryString();
        return sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..].Trim();
    }

    [TestMethod]
    public void AnInstantRange()
        => Assert.AreEqual(
            "WHERE r.\"Window\" = '[2026-01-01T09:00:00Z,2026-01-01T17:00:00Z)'::tstzrange",
            Where(db => db.Reservations.Where(r => r.Window == InstantRange.CreateFinite(
                Instant.FromUtc(2026, 1, 1, 9, 0), Instant.FromUtc(2026, 1, 1, 17, 0)))));

    [TestMethod]
    public void AYearMonthRange_InItsDateForm()
        => Assert.AreEqual(
            "WHERE r.\"BillingPeriod\" = '[2026-01-01,2026-06-30]'::daterange",
            Where(db => db.Reservations.Where(r => r.BillingPeriod == YearMonthRange.CreateFinite(new YearMonth(2026, 1), new YearMonth(2026, 6)))));
}

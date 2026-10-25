using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// The calendar factories of <see cref="LocalDateRange"/> and <see cref="YearMonthRange"/> over a column stay in
/// memory, where <c>DateRange</c>'s run as SQL (#191): a column holding <c>-infinity</c> stands for
/// <see cref="LocalDate.MinIsoValue"/>, -9998-01-01, and neither its month nor any year before 4713 BC has a
/// PostgreSQL date, so the server would raise where memory answers. Inside a <c>Where</c> they fail
/// translation; built from values the query does not read, they are one constant and run as SQL.
/// </summary>
[TestClass]
public sealed class NodaCalendarFactoryClientSideTests
{
    private static readonly LocalDate June = new(2026, 6, 1);

    public static IEnumerable<object[]> Predicates =>
    [
        ["LocalDateRange.Month(column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => LocalDateRange.Month(r.Day).Contains(June)))],
        ["LocalDateRange.Quarter(column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => LocalDateRange.Quarter(r.Day).Contains(June)))],
        ["LocalDateRange.Year(column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => LocalDateRange.Year(r.Day).Contains(June)))],
        ["LocalDateRange.IsoWeek(column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => LocalDateRange.IsoWeek(r.Day).Contains(June)))],
        ["LocalDateRange.Month(column, column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => LocalDateRange.Month(r.CustomerId, r.Id).Contains(June)))],
        ["LocalDateRange.Year(int column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => LocalDateRange.Year(r.CustomerId).Contains(June)))],
        ["LocalDateRange.IsoWeek(column, column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => LocalDateRange.IsoWeek(r.CustomerId, r.Id).Contains(June)))],
        ["YearMonthRange.Quarter(column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => YearMonthRange.Quarter(r.BillingPeriod.LowerBound()!.Value).Contains(new YearMonth(2026, 6))))],
        ["YearMonthRange.Quarter(column, column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => YearMonthRange.Quarter(r.CustomerId, r.Id).Contains(new YearMonth(2026, 6))))],
        ["YearMonthRange.Year(column)", (Func<NodaTestDbContext, IQueryable<Reservation>>)(db => db.Reservations.Where(r => YearMonthRange.Year(r.BillingPeriod.LowerBound()!.Value).Contains(new YearMonth(2026, 6))))],
    ];

    [TestMethod]
    [DynamicData(nameof(Predicates))]
    public void CalendarFactory_OverAColumn_FailsTranslationInWhere(string operation, Func<NodaTestDbContext, IQueryable<Reservation>> query)
    {
        using var context = new NodaTestDbContext();

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => query(context).ToQueryString(), operation);
        StringAssert.Contains(error.Message, "could not be translated", operation);
    }

    [TestMethod]
    public void CalendarFactory_OverValuesTheQueryDoesNotRead_IsOneConstant()
    {
        using var context = new NodaTestDbContext();

        var sql = context.Reservations.Where(r => r.Period.Overlaps(LocalDateRange.Month(2026, 6))).Select(r => r.Id).ToQueryString();

        StringAssert.Contains(sql, "\"Period\" && '[2026-06-01,2026-06-30]'::daterange");
    }
}

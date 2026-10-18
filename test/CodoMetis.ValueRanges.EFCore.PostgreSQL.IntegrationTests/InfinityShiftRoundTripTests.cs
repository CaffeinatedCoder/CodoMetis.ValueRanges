using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A row whose range PostgreSQL holds as open to <c>infinity</c> or <c>-infinity</c>, loaded, moved
/// with <c>Shift</c> or <c>Expand</c> and saved, still holds the infinity (#224).
/// </summary>
/// <remarks>
/// <para>
/// The temporal extremes stand for the infinities: Npgsql reads <c>infinity</c> as
/// <c>DateTime.MaxValue</c>, <c>DateOnly.MaxValue</c>, <c>LocalDate.MaxIsoValue</c> and the rest, and
/// writes them back as <c>infinity</c>. <c>Shift</c> and <c>Expand</c> moved such a bound like any other
/// value, so <c>[2026-01-01,infinity)</c> shifted back a day was saved as a range ending on
/// 9999-12-30 at 23:59:59.999999. PostgreSQL's own <c>upper(r) - interval '1 day'</c> keeps
/// <c>infinity</c>, and so does the model now.
/// </para>
/// <para>
/// A <c>daterange</c> open to an exclusive <c>infinity)</c> is the one form that can't be shown this
/// way. It loads as <c>[x,9999-12-30]</c> (#117), and the plugin writes that back as
/// <c>[x,9999-12-31)</c>, finite, whether or not the range was moved. The last test therefore holds
/// what the rule can promise there: the moved row saves exactly as the unmoved one does.
/// </para>
/// </remarks>
[TestClass]
public sealed class InfinityShiftRoundTripTests
{
    private static async Task<(string? Lower, string? Upper)> RoundTrip<TRange>(
        int                                  id,
        string                               column,
        string                               literal,
        Expression<Func<Reservation, TRange>> property,
        Func<TRange, TRange>                 move
    )
        where TRange : class
    {
        await using (var seed = new IntegrationDbContext())
        {
            seed.Reservations.Add(new Reservation { Id = id });
            await seed.SaveChangesAsync();

            // The literal and the column are this test's own constants, not input.
#pragma warning disable EF1002
            await seed.Database.ExecuteSqlRawAsync($"UPDATE \"Reservations\" SET \"{column}\" = {literal} WHERE \"Id\" = {id}");
#pragma warning restore EF1002
        }

        await using (var context = new IntegrationDbContext())
        {
            var row    = await context.Reservations.SingleAsync(r => r.Id == id);
            var entry  = context.Entry(row).Property(property);
            entry.CurrentValue = move(entry.CurrentValue);
            entry.IsModified   = true;
            await context.SaveChangesAsync();
        }

        await using var read = new IntegrationDbContext();
#pragma warning disable EF1002
        var lower = await read.Database.SqlQueryRaw<string?>($"SELECT lower(\"{column}\")::text AS \"Value\" FROM \"Reservations\" WHERE \"Id\" = {id}").SingleAsync();
        var upper = await read.Database.SqlQueryRaw<string?>($"SELECT upper(\"{column}\")::text AS \"Value\" FROM \"Reservations\" WHERE \"Id\" = {id}").SingleAsync();
#pragma warning restore EF1002
        return (lower, upper);
    }

    [TestMethod]
    public async Task Timestamp_OpenToInfinity_ShiftedAndSaved_StaysInfinite()
    {
        ContainerLifecycle.RequireDatabase();

        var (lower, upper) = await RoundTrip<DateTimeRange>(
            2241, "LocalTime", "tsrange('2026-01-01 00:00', 'infinity')", r => r.LocalTime, range => range.Shift(TimeSpan.FromDays(-1)));

        Assert.AreEqual("2025-12-31 00:00:00", lower);
        Assert.AreEqual("infinity", upper, "the infinite end moved to 9999-12-30 23:59:59.999999 before #224");
    }

    [TestMethod]
    public async Task TimestampTz_FromMinusInfinity_ExpandedAndSaved_StaysInfinite()
    {
        ContainerLifecycle.RequireDatabase();

        var (lower, upper) = await RoundTrip<DateTimeOffsetRange>(
            2242, "InstantTime", "tstzrange('-infinity', '2026-01-01 00:00+00')", r => r.InstantTime, range => range.Expand(TimeSpan.FromHours(1)));

        Assert.AreEqual("-infinity", lower);
        Assert.IsTrue(upper!.StartsWith("2026-01-01 01:00:00", StringComparison.Ordinal), upper);
    }

    [TestMethod]
    public async Task Date_ClosedAtInfinity_ShiftedAndSaved_StaysInfinite()
    {
        ContainerLifecycle.RequireDatabase();

        var (lower, upper) = await RoundTrip<DateRange>(
            2243, "Period", "daterange('2026-01-01', 'infinity', '[]')", r => r.Period, range => range.Shift(-1));

        Assert.AreEqual("2025-12-31", lower);
        Assert.AreEqual("infinity", upper);
    }

    [TestMethod]
    public async Task Date_FromMinusInfinity_ShiftedAndSaved_StaysInfinite()
    {
        ContainerLifecycle.RequireDatabase();

        var (lower, upper) = await RoundTrip<DateRange>(
            2244, "Period", "daterange('-infinity', '2026-01-01', '[]')", r => r.Period, range => range.Shift(1));

        Assert.AreEqual("-infinity", lower);
        Assert.AreEqual("2026-01-03", upper, "[-infinity,2026-01-02] in PostgreSQL's half-open form");
    }

    [TestMethod]
    public async Task NodaTime_OpenToInfinity_ShiftedAndSaved_StaysInfinite()
    {
        ContainerLifecycle.RequireDatabase();

        var date = await RoundTrip<LocalDateRange>(
            2245, "NodaPeriod", "daterange('2026-01-01', 'infinity', '[]')", r => r.NodaPeriod, range => range.Shift(Period.FromMonths(-1)));
        Assert.AreEqual(("2025-12-01", "infinity"), date);

        var wallClock = await RoundTrip<LocalDateTimeRange>(
            2246, "NodaWallClock", "tsrange('2026-01-01 00:00', 'infinity')", r => r.NodaWallClock, range => range.Shift(Period.FromDays(-1)));
        Assert.AreEqual(("2025-12-31 00:00:00", "infinity"), wallClock);

        var instant = await RoundTrip<InstantRange>(
            2247, "NodaWindow", "tstzrange('2026-01-01 00:00+00', 'infinity')", r => r.NodaWindow, range => range.Expand(Duration.FromHours(1)));
        Assert.AreEqual("infinity", instant.Upper);

        var months = await RoundTrip<YearMonthRange>(
            2248, "BillingPeriod", "daterange('2026-01-01', 'infinity', '[]')", r => r.BillingPeriod, range => range.Shift(-1));
        Assert.AreEqual(("2025-12-01", "infinity"), months);
    }

    /// <summary>
    /// <c>[x,infinity)</c> as a <c>daterange</c> already comes back finite from a plain save (see the
    /// remarks), so what the rule promises there is that moving the range changes only its finite end:
    /// the moved row and the unmoved row hold the same upper bound.
    /// </summary>
    [TestMethod]
    public async Task Date_OpenToExclusiveInfinity_ShiftedAndSaved_SavesAsTheUnshiftedRowDoes()
    {
        ContainerLifecycle.RequireDatabase();

        const string literal = "daterange('2026-01-01', 'infinity')";
        var moved   = await RoundTrip<DateRange>(2249, "Period", literal, r => r.Period, range => range.Shift(-1));
        var unmoved = await RoundTrip<DateRange>(2250, "Period", literal, r => r.Period, range => range);

        Assert.AreEqual("2025-12-31", moved.Lower);
        Assert.AreEqual(unmoved.Upper, moved.Upper, "the end that stands for infinity) moved with the range before #224");
    }
}

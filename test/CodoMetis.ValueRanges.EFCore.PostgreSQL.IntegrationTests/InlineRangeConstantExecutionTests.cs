using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A range built inline in a query matches its own row, for every range type, against a live server.
/// EF Core inlines such a range as a constant typed by its sealed variant; until 9.0.0 the comparison
/// failed to translate ("does not have a type mapping assigned"). Each constant here is the whole
/// operand, typed by the column, which also satisfies the rule that a constant-operand query runs per
/// element type.
/// </summary>
[TestClass]
public sealed class InlineRangeConstantExecutionTests
{
    private const int Id = 9701;

    [ClassInitialize]
    public static async Task Seed(TestContext _)
    {
        if (ContainerLifecycle.ConnectionString is null) return;

        await using var write = new IntegrationDbContext();
        write.Reservations.Add(new Reservation
        {
            Id            = Id,
            Seats         = Int32Range.CreateFinite(1, 5),
            Tickets       = Int64Range.CreateFinite(10, 20),
            Price         = DecimalRange.CreateFinite(1.5m, 2.5m),
            Period        = DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            LocalTime     = DateTimeRange.CreateFinite(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 17, 0, 0)),
            InstantTime   = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 17, 0, 0, TimeSpan.Zero)),
            OpeningHours  = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            NodaPeriod    = LocalDateRange.CreateFinite(new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31)),
            NodaWallClock = LocalDateTimeRange.CreateFinite(new LocalDateTime(2026, 1, 1, 9, 0), new LocalDateTime(2026, 1, 1, 17, 0)),
            NodaWindow    = InstantRange.CreateFinite(Instant.FromUtc(2026, 1, 1, 9, 0), Instant.FromUtc(2026, 1, 1, 17, 0)),
            BillingPeriod = YearMonthRange.CreateFinite(new YearMonth(2026, 1), new YearMonth(2026, 6))
        });
        await write.SaveChangesAsync();
    }

    private static IEnumerable<(string Name, Expression<Func<Reservation, bool>> Filter)> InlineEqualities()
    {
        yield return ("Int32Range", r => r.Seats == Int32Range.CreateFinite(1, 5));
        yield return ("Int64Range", r => r.Tickets == Int64Range.CreateFinite(10, 20));
        yield return ("DecimalRange", r => r.Price == DecimalRange.CreateFinite(1.5m, 2.5m));
        yield return ("DateRange", r => r.Period == DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        yield return ("DateTimeRange", r => r.LocalTime == DateTimeRange.CreateFinite(new DateTime(2026, 1, 1, 9, 0, 0), new DateTime(2026, 1, 1, 17, 0, 0)));
        yield return ("DateTimeOffsetRange", r => r.InstantTime == DateTimeOffsetRange.CreateFinite(
                          new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 17, 0, 0, TimeSpan.Zero)));
        yield return ("TimeRange", r => r.OpeningHours == TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        yield return ("LocalDateRange", r => r.NodaPeriod == LocalDateRange.CreateFinite(new LocalDate(2026, 1, 1), new LocalDate(2026, 1, 31)));
        yield return ("LocalDateTimeRange", r => r.NodaWallClock == LocalDateTimeRange.CreateFinite(new LocalDateTime(2026, 1, 1, 9, 0), new LocalDateTime(2026, 1, 1, 17, 0)));
        yield return ("InstantRange", r => r.NodaWindow == InstantRange.CreateFinite(Instant.FromUtc(2026, 1, 1, 9, 0), Instant.FromUtc(2026, 1, 1, 17, 0)));
        yield return ("YearMonthRange", r => r.BillingPeriod == YearMonthRange.CreateFinite(new YearMonth(2026, 1), new YearMonth(2026, 6)));
    }

    [TestMethod]
    public async Task EveryRangeType_BuiltInline_MatchesItsRow()
    {
        ContainerLifecycle.RequireDatabase();

        var failures = new List<string>();
        foreach (var (name, filter) in InlineEqualities())
        {
            try
            {
                await using var ctx = new IntegrationDbContext();
                var count = await ctx.Reservations.Where(r => r.Id == Id).Where(filter).CountAsync();
                if (count != 1) failures.Add($"{name}: matched {count} rows.");
            }
            catch (Exception e)
            {
                failures.Add($"{name}: {e.GetType().Name}: {(e.InnerException ?? e).Message.Split('\n')[0]}");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }
}

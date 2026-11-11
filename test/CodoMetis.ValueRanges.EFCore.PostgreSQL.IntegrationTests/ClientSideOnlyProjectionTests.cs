using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The projection half of <c>docs/efcore.md#what-runs-where</c>: an operation the document lists as
/// client-side only falls back to client evaluation inside a <c>Select</c> and answers what it
/// answers in memory. Executed, because "fails translation" and "evaluates on the client" are both
/// decided only when EF compiles the query against a provider. The predicate half is
/// <c>ClientSideOnlyTranslationTests</c> in the translation project.
/// </summary>
[TestClass]
public class ClientSideOnlyProjectionTests
{
    private const int Id = 13001;

    [TestMethod]
    public async Task ClientSideOnlyOperations_InSelect_EvaluateOnTheClient()
    {
        ContainerLifecycle.RequireDatabase();

        var row = new Reservation
        {
            Id         = Id,
            Seats      = Int32Range.CreateFinite(3, 7),
            Price      = DecimalRange.CreateFinite(1.5m, 4m),
            Period     = DateRange.CreateFinite(new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 5)),
            NodaPeriod = LocalDateRange.CreateFinite(new LocalDate(2026, 6, 3), new LocalDate(2026, 6, 5)),
            BillingPeriod = YearMonthRange.CreateFinite(new YearMonth(2026, 6), new YearMonth(2026, 8)),
            Codes      = Int32Set.From([5, 1, 2, 3]),
            SeatBlocks = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 2), Int32Range.CreateFinite(4, 4)]),
            Tags       = StringSet.From(["b", "a"]),
        };

        await using (var write = new IntegrationDbContext())
        {
            write.Reservations.Add(row);
            await write.SaveChangesAsync();
        }

        await using var context = new IntegrationDbContext();
        var server = await context.Reservations
            .Where(r => r.Id == Id)
            .Select(r => new
            {
                // Length on Int32Range and Int64Range translates (#405); on the other range types it stays in memory.
                r.Price.Length,
                Values   = r.Seats.Values().ToList(),
                // Clamp translates on every range type (#406, #486); its one in-memory case, a value whose time zone
                // differs from the range's, is ClampDomainParityTests' (DateTimeRange_OfATimestamptzValue_…).
                Shifted  = r.Seats.Shift(10),
                Expanded = r.Seats.Expand(-1),
                Stepped  = r.Seats.Values(2).ToList(),
                Pieces   = r.Seats.Split(2).ToList(),
                // The calendar factories over a column translate on DateRange (#191); the NodaTime forms stay in memory.
                MonthOf   = LocalDateRange.Month(r.NodaPeriod.LowerBound()!.Value),
                WeekOf    = LocalDateRange.IsoWeek(r.NodaPeriod.LowerBound()!.Value),
                QuarterOf = YearMonthRange.Quarter(r.BillingPeriod.LowerBound()!.Value),
                RangeSet = r.Codes.ToRangeSet(),
                Set      = r.SeatBlocks.ToInt32Set(),
                First    = r.Tags[0],
            })
            .SingleAsync();

        Assert.AreEqual(row.Price.Length, server.Length, "Length");
        CollectionAssert.AreEqual(row.Seats.Values().ToList(), server.Values, "Values()");
        Assert.AreEqual(row.Seats.Shift(10), server.Shifted, "Shift");
        Assert.AreEqual(row.Seats.Expand(-1), server.Expanded, "Expand");
        CollectionAssert.AreEqual(row.Seats.Values(2).ToList(), server.Stepped, "Values(step)");
        CollectionAssert.AreEqual(row.Seats.Split(2).ToList(), server.Pieces, "Split(step)");
        Assert.AreEqual(LocalDateRange.Month(2026, 6), server.MonthOf, "LocalDateRange.Month(column)");
        Assert.AreEqual(LocalDateRange.IsoWeek(2026, 23), server.WeekOf, "LocalDateRange.IsoWeek(column)");
        Assert.AreEqual(YearMonthRange.Quarter(2026, 2), server.QuarterOf, "YearMonthRange.Quarter(column)");

        // Built from values the query does not read, a calendar range is one constant and runs as SQL.
        var inJune = await context.Reservations
            .Where(r => r.Id == Id && r.Period.IsContainedBy(DateRange.Month(2026, 6)))
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { Id }, inJune, "Period.IsContainedBy(DateRange.Month(2026, 6))");
        Assert.AreEqual(row.Codes.ToRangeSet(), server.RangeSet, "ToRangeSet()");
        Assert.AreEqual(row.SeatBlocks.ToInt32Set(), server.Set, "ToInt32Set()");
        Assert.AreEqual(row.Tags[0], server.First, "set[0]");
    }
}

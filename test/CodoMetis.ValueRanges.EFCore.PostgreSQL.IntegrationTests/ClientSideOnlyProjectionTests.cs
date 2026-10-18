using Microsoft.EntityFrameworkCore;

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
            Period     = DateRange.CreateFinite(new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 5)),
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
                r.Seats.Length,
                Values   = r.Seats.Values().ToList(),
                Clamp    = r.Seats.Clamp(50),
                MonthOf  = DateRange.Month(r.Period.LowerBound()!.Value),
                WeekOf   = DateRange.IsoWeek(r.Period.LowerBound()!.Value),
                RangeSet = r.Codes.ToRangeSet(),
                Set      = r.SeatBlocks.ToInt32Set(),
                First    = r.Tags[0],
            })
            .SingleAsync();

        Assert.AreEqual(row.Seats.Length, server.Length, "Length");
        CollectionAssert.AreEqual(row.Seats.Values().ToList(), server.Values, "Values()");
        Assert.AreEqual(row.Seats.Clamp(50), server.Clamp, "Clamp");
        Assert.AreEqual(DateRange.Month(2026, 6), server.MonthOf, "DateRange.Month(column)");
        Assert.AreEqual(DateRange.IsoWeek(2026, 23), server.WeekOf, "DateRange.IsoWeek(column)");

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

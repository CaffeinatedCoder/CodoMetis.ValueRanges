using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// The predicate half of <c>docs/efcore.md#what-runs-where</c>: every operation the document lists
/// as client-side only must fail translation inside a <c>Where</c> — loudly, as EF's
/// "could not be translated" — rather than be answered by something that only looks like it. The
/// projection half needs a server and lives in the integration project
/// (<c>ClientSideOnlyProjectionTests</c>).
/// </summary>
[TestClass]
public sealed class ClientSideOnlyTranslationTests
{
    public static IEnumerable<object[]> Predicates =>
    [
        ["Length", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Seats.Length > 3))],
        ["Values()", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Seats.Values().Contains(5)))],
        ["Clamp", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Seats.Clamp(5) == 5))],
        ["Shift", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Seats.Shift(10).Contains(15)))],
        ["Expand", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Period.Expand(1).Contains(new DateOnly(2026, 6, 1))))],
        ["DateRange.Month(column)", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => DateRange.Month(b.Period.LowerBound()!.Value).Contains(new DateOnly(2026, 6, 1))))],
        ["DateRange.IsoWeek(column)", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => DateRange.IsoWeek(b.Period.LowerBound()!.Value).Contains(new DateOnly(2026, 6, 1))))],
        ["ToRangeSet()", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Codes.ToRangeSet().Contains(5)))],
        ["ToInt32Set()", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.SeatBlocks.ToInt32Set().Contains(5)))],
        ["set[0]", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Tags[0] == "a"))],
    ];

    [TestMethod]
    [DynamicData(nameof(Predicates))]
    public void ClientSideOnlyOperation_InWhere_FailsTranslation(string operation, Func<TestDbContext, IQueryable<Booking>> query)
    {
        using var context = new TestDbContext();

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => query(context).ToQueryString(), operation);
        StringAssert.Contains(error.Message, "could not be translated", operation);
    }
}

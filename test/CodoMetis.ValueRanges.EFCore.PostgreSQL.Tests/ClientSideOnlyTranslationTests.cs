using CodoMetis.ValueRanges.Core;
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
        // Length on Int32Range and Int64Range translates (#405); on the other range types it stays in memory.
        ["Length", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Price.Length > 3m))],
        ["Values()", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Seats.Values().Contains(5)))],
        // Clamp on Int32Range, Int64Range and DecimalRange translates (#406); on the other range types it stays in memory.
        ["Clamp", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Period.Clamp(new DateOnly(2026, 6, 1)) == new DateOnly(2026, 6, 1)))],
        ["Shift", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Seats.Shift(10).Contains(15)))],
        ["Expand", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Period.Expand(1).Contains(new DateOnly(2026, 6, 1))))],
        ["Values(step)", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Seats.Values(2).Contains(5)))],
        ["Split(step)", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Seats.Split(2).Count() > 1))],
        ["Split(period)", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Period.Split(DateRange.Month).Count() > 1))],
        // The calendar factories over a column translate on DateRange (#191); the NodaTime forms stay in memory
        // (NodaCalendarFactoryClientSideTests).
        ["ToRangeSet()", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Codes.ToRangeSet().Contains(5)))],
        ["ToInt32Set()", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.SeatBlocks.ToInt32Set().Contains(5)))],
        ["set[0]", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => b.Tags[0] == "a"))],
        // EF Core reads any Compare(x, y) as the natural order, which for a range is not the lower-bound order (#385).
        ["RangeLowerBoundComparer.Compare", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare(b.Period, June) == 0))],
        ["RangeSet.LowerBoundComparer.Compare", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => RangeSet<DateRange, DateOnly>.LowerBoundComparer.Compare(b.Period, June) > 0))],
        ["IComparer<RangeSet>.Compare", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.Where(b => Comparer<RangeSet<DateRange, DateOnly>>.Default.Compare(b.BlockedDays, RangeSet<DateRange, DateOnly>.Empty) == 0))],
        ["OrderBy(RangeLowerBoundComparer.Compare)", (Func<TestDbContext, IQueryable<Booking>>)(db => db.Bookings.OrderBy(b => RangeLowerBoundComparer<Int32Range, int>.Instance.Compare(b.Seats, Int32Range.Infinite)))],
        // The operands decide, not the type Compare declares (#452). Each comparer is a captured local, so a
        // query parameter: EF Core leaves Compare on a constant comparer untranslated in any case.
        ["IComparer<IRange<T>>.Compare", (Func<TestDbContext, IQueryable<Booking>>)(db => { var comparer = OverInterface; return db.Bookings.Where(b => comparer.Compare(b.Period, June) == 0); })],
        ["OrderBy(IComparer<IRange<T>>.Compare)", (Func<TestDbContext, IQueryable<Booking>>)(db => { var comparer = OverInterface; return db.Bookings.OrderBy(b => comparer.Compare(b.Period, June)); })],
        ["IComparer.Compare", (Func<TestDbContext, IQueryable<Booking>>)(db => { var comparer = NonGeneric; return db.Bookings.Where(b => comparer.Compare(b.Period, June) == 0); })],
        ["OrderBy(IComparer.Compare)", (Func<TestDbContext, IQueryable<Booking>>)(db => { var comparer = NonGeneric; return db.Bookings.OrderBy(b => comparer.Compare(b.Period, June)); })],
        ["IComparer<StringSet>.Compare", (Func<TestDbContext, IQueryable<Booking>>)(db => { var comparer = OverValueSets; return db.Bookings.Where(b => comparer.Compare(b.Tags, OneTag) == 0); })],
        ["OrderBy(IComparer<StringSet>.Compare)", (Func<TestDbContext, IQueryable<Booking>>)(db => { var comparer = OverValueSets; return db.Bookings.OrderBy(b => comparer.Compare(b.Tags, OneTag)); })],
    ];

    private static readonly IComparer<IRange<DateOnly>> OverInterface =
        Comparer<IRange<DateOnly>>.Create((x, y) => RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare((DateRange)x, (DateRange)y));

    private static readonly System.Collections.IComparer NonGeneric = Comparer<DateRange>.Create(RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare);

    private static readonly IComparer<StringSet> OverValueSets = Comparer<StringSet>.Create((x, y) => x.Count.CompareTo(y.Count));

    private static readonly StringSet OneTag = StringSet.From(["z"]);

    private static readonly DateRange June = DateRange.Month(2026, 6);

    [TestMethod]
    [DynamicData(nameof(Predicates))]
    public void ClientSideOnlyOperation_InWhere_FailsTranslation(string operation, Func<TestDbContext, IQueryable<Booking>> query)
    {
        using var context = new TestDbContext();

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => query(context).ToQueryString(), operation);
        StringAssert.Contains(error.Message, "could not be translated", operation);
    }
}

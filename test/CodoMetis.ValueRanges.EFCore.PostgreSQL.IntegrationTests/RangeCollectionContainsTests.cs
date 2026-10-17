using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>wanted.Contains(r.Seats)</c> over a captured collection of ranges, for every range family,
/// executed and compared with the same expression in memory.
/// </summary>
/// <remarks>
/// <para>
/// Up to 9.0.0 the collection went out as one parameter, <c>r."Seats" = ANY (@wanted)</c>. Npgsql
/// received an untyped <c>NpgsqlRange&lt;T&gt;[]</c> and inferred it as a multirange, so PostgreSQL
/// failed with <c>42809 op ANY/ALL (array) requires array on right side</c>, and the custom
/// <c>timerange</c>, which has no inferred type, threw <c>InvalidCastException</c>. It is now sent one
/// parameter per range, <c>IN (@wanted1, @wanted2)</c>, each typed by the range's own mapping.
/// </para>
/// <para>
/// Two rows: the first holds a finite range in each column, the second the empty range. The wanted
/// collection holds the first row's range — built differently where the family allows it (other
/// bound inclusivity, another decimal scale, another offset) — and a range neither row holds. So
/// exactly one row is wanted, and the negation keeps exactly the other one. Ids are in the 15100
/// block.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeCollectionContainsTests
{
    private const int Wanted   = 15101;
    private const int Unwanted = 15102;

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private static readonly DateTimeOffset Start = new(2024, 6, 15, 10, 30, 15, 123, 456, TimeSpan.Zero);
    private static readonly DateTimeOffset End   = Start.AddHours(2);

    private static async Task<IntegrationDbContext> Context()
    {
        ContainerLifecycle.RequireDatabase();

        await SeedLock.WaitAsync();
        try
        {
            if (!_seeded)
            {
                await using var write = new IntegrationDbContext();
                write.Reservations.AddRange(
                    new Reservation
                    {
                        Id            = Wanted,
                        Seats         = Int32Range.CreateFinite(1, 5),
                        Tickets       = Int64Range.CreateFinite(9_000_000_000L, 9_000_000_005L),
                        Price         = DecimalRange.CreateFinite(1.5m, 2m),
                        Period        = DateRange.CreateFinite(new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 30)),
                        LocalTime     = DateTimeRange.CreateFinite(Start.DateTime, End.DateTime),
                        InstantTime   = DateTimeOffsetRange.CreateFinite(Start, End),
                        OpeningHours  = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 30, 15, 250)),
                        NodaPeriod    = LocalDateRange.CreateFinite(new LocalDate(2024, 6, 1), new LocalDate(2024, 6, 30)),
                        NodaWallClock = LocalDateTimeRange.CreateFinite(LocalDateTime.FromDateTime(Start.DateTime), LocalDateTime.FromDateTime(End.DateTime)),
                        NodaWindow    = InstantRange.CreateFinite(Instant.FromDateTimeOffset(Start), Instant.FromDateTimeOffset(End)),
                        BillingPeriod = YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 3))
                    },
                    // Every range column keeps its empty default, which no wanted collection holds.
                    new Reservation { Id = Unwanted });
                await write.SaveChangesAsync();
                _seeded = true;
            }
        }
        finally
        {
            SeedLock.Release();
        }

        return new IntegrationDbContext();
    }

    private static IQueryable<Reservation> Rows(IntegrationDbContext context)
        => context.Reservations.Where(r => r.Id == Wanted || r.Id == Unwanted);

    /// <summary>Holds the collection as a field, so EF Core parameterizes it as it does a captured local.</summary>
    private sealed class Captured<TRange>(TRange[] ranges)
    {
        public readonly TRange[]            Array      = ranges;
        public readonly List<TRange>        List       = [..ranges];
        public readonly IEnumerable<TRange> Enumerable = ranges.ToList();
    }

    private static readonly MethodInfo EnumerableContains =
        typeof(Enumerable).GetMethods().Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

    /// <summary>
    /// Runs <c>captured.Contains(column)</c> and its negation over a captured array, list and
    /// enumerable, and asserts each answers what the same expression answers in memory.
    /// </summary>
    private static async Task AssertAnswersAsInMemory<TRange>(Expression<Func<Reservation, TRange>> column, params TRange[] wanted)
    {
        await using var context = await Context();

        var read     = column.Compile();
        var loaded   = await Rows(context).AsNoTracking().ToListAsync();
        var expected = loaded.Where(r => wanted.Contains(read(r))).Select(r => r.Id).Order().ToList();
        CollectionAssert.AreEqual(new[] { Wanted }, expected, $"{column}: the data must want exactly the first row");

        var captured = Expression.Constant(new Captured<TRange>(wanted));
        var contains = EnumerableContains.MakeGenericMethod(typeof(TRange));

        foreach (var field in new[] { nameof(Captured<>.Array), nameof(Captured<>.List), nameof(Captured<>.Enumerable) })
        {
            var test = Expression.Call(contains, Expression.Field(captured, field), column.Body);

            var matched = await Rows(context).Where(Expression.Lambda<Func<Reservation, bool>>(test, column.Parameters))
                                             .Select(r => r.Id).OrderBy(id => id).ToListAsync();
            CollectionAssert.AreEqual(expected, matched, $"{column}, captured as {field}");

            var others = await Rows(context).Where(Expression.Lambda<Func<Reservation, bool>>(Expression.Not(test), column.Parameters))
                                            .Select(r => r.Id).OrderBy(id => id).ToListAsync();
            CollectionAssert.AreEqual(new[] { Unwanted }, others, $"!{column}, captured as {field}");
        }
    }

    [TestMethod]
    public async Task TheIssueQuery_AnswersAsInMemory()
    {
        await using var context = await Context();

        // As reported in #71: failed with 42809 op ANY/ALL (array) requires array on right side.
        var ranges = new List<Int32Range> { Int32Range.CreateFinite(1, 5) };

        var ids = await Rows(context).Where(r => ranges.Contains(r.Seats)).Select(r => r.Id).ToListAsync();

        CollectionAssert.AreEqual(new[] { Wanted }, ids);
    }

    [TestMethod]
    public async Task NumericFamilies_AnswerAsInMemory()
    {
        // [1,6) is [1,5] once canonical, in memory and as int4range.
        await AssertAnswersAsInMemory(r => r.Seats, Int32Range.Parse("[1,6)", null), Int32Range.CreateFinite(1, 4));
        await AssertAnswersAsInMemory(r => r.Tickets, Int64Range.CreateFinite(9_000_000_000L, 9_000_000_005L), Int64Range.CreateFinite(1, 2));
        // Another scale: 1.50 is 1.5 in memory and as numeric.
        await AssertAnswersAsInMemory(r => r.Price, DecimalRange.CreateFinite(1.50m, 2.0m), DecimalRange.CreateFinite(1.5m, 2.5m));
    }

    [TestMethod]
    public async Task TemporalFamilies_AnswerAsInMemory()
    {
        await AssertAnswersAsInMemory(r => r.Period, DateRange.CreateFinite(new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 30)), DateRange.CreateFinite(new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 29)));
        await AssertAnswersAsInMemory(r => r.LocalTime, DateTimeRange.CreateFinite(Start.DateTime, End.DateTime), DateTimeRange.CreateFinite(Start.DateTime, End.DateTime.AddSeconds(1)));
        // The same instants at another offset.
        await AssertAnswersAsInMemory(r => r.InstantTime, DateTimeOffsetRange.CreateFinite(Start.ToOffset(TimeSpan.FromHours(2)), End.ToOffset(TimeSpan.FromHours(-5))), DateTimeOffsetRange.CreateFinite(Start, End.AddSeconds(1)));
        // The custom timerange, which has no type Npgsql could infer for an array of it.
        await AssertAnswersAsInMemory(r => r.OpeningHours, TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 30, 15, 250)), TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 30)));
    }

    [TestMethod]
    public async Task NodaTimeFamilies_AnswerAsInMemory()
    {
        await AssertAnswersAsInMemory(r => r.NodaPeriod, LocalDateRange.CreateFinite(new LocalDate(2024, 6, 1), new LocalDate(2024, 6, 30)), LocalDateRange.CreateFinite(new LocalDate(2024, 6, 2), new LocalDate(2024, 6, 30)));
        await AssertAnswersAsInMemory(r => r.NodaWallClock, LocalDateTimeRange.CreateFinite(LocalDateTime.FromDateTime(Start.DateTime), LocalDateTime.FromDateTime(End.DateTime)), LocalDateTimeRange.CreateFinite(LocalDateTime.FromDateTime(Start.DateTime), LocalDateTime.FromDateTime(End.DateTime).PlusSeconds(1)));
        await AssertAnswersAsInMemory(r => r.NodaWindow, InstantRange.CreateFinite(Instant.FromDateTimeOffset(Start), Instant.FromDateTimeOffset(End)), InstantRange.CreateFinite(Instant.FromDateTimeOffset(Start), Instant.FromDateTimeOffset(End).PlusTicks(10)));
        await AssertAnswersAsInMemory(r => r.BillingPeriod, YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 3)), YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 4)));
    }

    [TestMethod]
    public async Task AnEmptyCollection_MatchesNothing()
    {
        await using var context = await Context();
        var wanted = new List<Int32Range>();

        Assert.AreEqual(0, await Rows(context).CountAsync(r => wanted.Contains(r.Seats)));
    }

    [TestMethod]
    public async Task AnyBesideContainsOverTheCollection_AnswersAsInMemory()
    {
        await using var context = await Context();
        // Overlapping on purpose: read as one multirange parameter they would merge into [1,8] (#76).
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(3, 8) };
        var loaded = await Rows(context).AsNoTracking().ToListAsync();

        var expected = loaded.Where(r => wanted.Contains(r.Seats) && wanted.Count(w => w.Overlaps(r.Seats)) == 2).Select(r => r.Id).ToList();
        CollectionAssert.AreEqual(new[] { Wanted }, expected, "the data must pick one row");
        CollectionAssert.AreEqual(
            expected,
            await Rows(context).Where(r => wanted.Contains(r.Seats) && wanted.Count(w => w.Overlaps(r.Seats)) == 2).Select(r => r.Id).ToListAsync());
    }

    [TestMethod]
    public async Task AnyAloneOverEFConstant_AnswersAsInMemory()
    {
        // The workaround docs/efcore.md gives for #76 until 10.0.0: EF.Constant writes each range as a
        // literal into VALUES, so overlapping ranges stay apart. Pins behaviour that held before 9.1.0.
        await using var context = await Context();
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(3, 8) };
        var loaded = await Rows(context).AsNoTracking().ToListAsync();

        var expected = loaded.Select(r => wanted.Count(w => w.Overlaps(r.Seats))).Order().ToList();
        CollectionAssert.AreEqual(new[] { 0, 2 }, expected, "the data must count both overlapping ranges");
        CollectionAssert.AreEqual(
            expected,
            await Rows(context).Select(r => EF.Constant(wanted).Count(w => w.Overlaps(r.Seats))).OrderBy(n => n).ToListAsync());
    }

    [TestMethod]
    public async Task EFParameter_IsRefusedWithWhatToWriteInstead()
    {
        await using var context = await Context();
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5) };

        var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Rows(context).CountAsync(r => EF.Parameter(wanted).Contains(r.Seats)));

        StringAssert.Contains(refusal.Message, "Remove 'EF.Parameter'");
    }

    [TestMethod]
    public async Task ACollectionOfRangeSets_StillAnswersAsInMemory()
    {
        // Unchanged by the fix: a collection of multiranges binds as one multirange array, and did.
        await using var context = await Context();
        var wanted = new List<RangeSet<Int32Range, int>> { RangeSet<Int32Range, int>.Empty };

        var ids = await Rows(context).Where(r => wanted.Contains(r.SeatBlocks)).Select(r => r.Id).OrderBy(id => id).ToListAsync();

        CollectionAssert.AreEqual(new[] { Wanted, Unwanted }, ids);
    }
}

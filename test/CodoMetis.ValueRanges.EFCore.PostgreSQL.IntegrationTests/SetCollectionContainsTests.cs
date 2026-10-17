using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.Conventions.Tests;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>wanted.Contains(r.Tags)</c> over a captured collection of value sets, for every family,
/// executed and compared with the same expression in memory.
/// </summary>
/// <remarks>
/// <para>
/// Up to 9.0.0 the collection went out as one array parameter, <c>r."Tags" = ANY (@wanted)</c>. A
/// value set is already an array, so that is an array of arrays, which PostgreSQL does not have:
/// Npgsql threw <c>InvalidCastException</c> (<c>Writing values of 'System.String[][]' is not
/// supported</c>) for every family, and for a list, an array and an <c>IEnumerable</c> alike. It is
/// now sent one parameter per set, <c>IN (@wanted1, @wanted2)</c>, which is equality against each,
/// and equality of canonical arrays is set equality.
/// </para>
/// <para>
/// Two rows, each family holding a different set in each. The wanted collection holds the first
/// row's set — built differently where the family allows it (another order, a duplicate, another
/// offset, another decimal scale) — and a set neither row holds. So exactly one row is wanted, and
/// the negation keeps exactly the other one. Ids are in the 14100 block.
/// </para>
/// </remarks>
[TestClass]
public sealed class SetCollectionContainsTests
{
    private const int Wanted   = 14101;
    private const int Unwanted = 14102;

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    private static readonly DateTimeOffset Moment = new(2024, 6, 15, 10, 30, 15, 123, 456, TimeSpan.Zero);

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
                        Id                 = Wanted,
                        Tags               = StringSet.From("a", "b"),
                        OptionalTags       = null,
                        Permissions        = StringSet<TestKey>.From([TestKey.Parse("users.read", null)]),
                        Uuids              = GuidSet.From(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                        SmallCodes         = Int16Set.From(1, 2),
                        Codes              = Int32Set.From(1, 2),
                        BigCodes           = Int64Set.From(9_000_000_000L),
                        Rates              = DecimalSet.From(1.5m),
                        BlackoutDates      = DateSet.From(new DateOnly(2024, 6, 15)),
                        Slots              = TimeSet.From(new TimeOnly(9, 30, 15, 250, 125)),
                        Marks              = DateTimeSet.From(Moment.DateTime),
                        Stamps             = DateTimeOffsetSet.From(Moment),
                        NodaHolidays       = LocalDateSet.From(new LocalDate(2024, 6, 15)),
                        NodaMarks          = LocalDateTimeSet.From(LocalDateTime.FromDateTime(Moment.DateTime)),
                        NodaOccurrences    = InstantSet.From(Instant.FromDateTimeOffset(Moment)),
                        NodaSlots          = LocalTimeSet.From(new LocalTime(9, 30, 15)),
                        BillingMonths      = YearMonthSet.From(new YearMonth(2024, 6)),
                        Audits             = DateTimeSet<AuditStamp>.From([new AuditStamp(Moment.DateTime)]),
                        WrappedMonths      = YearMonthSet<BillingMonth>.From([BillingMonth.Parse("2024-06", null)]),
                        WrappedTenants     = GuidSet<TenantId>.From([TenantId.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff", null)]),
                        WrappedTinyCodes   = Int16Set<TinyCode>.From([TinyCode.Parse("-7", null)]),
                        WrappedCodes       = Int32Set<SmallCode>.From([SmallCode.Parse("42", null)]),
                        WrappedBigCodes    = Int64Set<LargeCode>.From([LargeCode.Parse("9007199254740993", null)]),
                        WrappedRates       = DecimalSet<Money>.From([Money.Parse("12.5", null)]),
                        WrappedDays        = DateSet<BusinessDate>.From([BusinessDate.Parse("2024-06-15", null)]),
                        WrappedSlots       = TimeSet<ShiftTime>.From([ShiftTime.Parse("09:30:15.25", null)]),
                        WrappedStamps      = DateTimeOffsetSet<EventStamp>.From([EventStamp.Parse("2024-06-15T10:30:00.123456+02:00", null)]),
                        WrappedNodaDays    = LocalDateSet<CalendarDay>.From([CalendarDay.Parse("2024-06-15", null)]),
                        WrappedNodaMarks   = LocalDateTimeSet<WallClockStamp>.From([WallClockStamp.Parse("2024-06-15T10:30:15.123456", null)]),
                        WrappedNodaMoments = InstantSet<EventInstant>.From([EventInstant.Parse("2024-06-15T10:30:15.123456Z", null)]),
                        WrappedNodaSlots   = LocalTimeSet<OpeningTime>.From([OpeningTime.Parse("09:30:15.123456", null)])
                    },
                    new Reservation
                    {
                        Id           = Unwanted,
                        Tags         = StringSet.From("c"),
                        OptionalTags = StringSet.From("c")
                        // Every other family keeps its empty set, which no wanted collection holds.
                    });
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
    private sealed class Captured<TSet>(TSet[] sets)
    {
        public readonly TSet[]              Array      = sets;
        public readonly List<TSet>          List       = [..sets];
        public readonly IEnumerable<TSet>   Enumerable = sets.ToList();
    }

    private static readonly MethodInfo EnumerableContains =
        typeof(Enumerable).GetMethods().Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

    /// <summary>
    /// Runs <c>captured.Contains(column)</c> and its negation over a captured array, list and
    /// enumerable, and asserts each answers what the same expression answers in memory.
    /// </summary>
    private static async Task AssertAnswersAsInMemory<TSet>(Expression<Func<Reservation, TSet>> column, params TSet[] wanted)
    {
        await using var context = await Context();

        var read     = column.Compile();
        var loaded   = await Rows(context).AsNoTracking().ToListAsync();
        var expected = loaded.Where(r => wanted.Contains(read(r))).Select(r => r.Id).Order().ToList();
        CollectionAssert.AreEqual(new[] { Wanted }, expected, $"{column}: the data must want exactly the first row");

        var captured = Expression.Constant(new Captured<TSet>(wanted));
        var contains = EnumerableContains.MakeGenericMethod(typeof(TSet));

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

        // As reported in #46: threw InvalidCastException writing String[][].
        var wanted = new List<StringSet> { StringSet.From("a", "b"), StringSet.From("x") };

        var ids = await Rows(context).Where(r => wanted.Contains(r.Tags)).Select(r => r.Id).ToListAsync();

        CollectionAssert.AreEqual(new[] { Wanted }, ids);
    }

    [TestMethod]
    public async Task StringAndGuidFamilies_AnswerAsInMemory()
    {
        // Another order and a duplicate: still the first row's set once canonical.
        await AssertAnswersAsInMemory(r => r.Tags, StringSet.From("b", "a", "b"), StringSet.From("x"));
        await AssertAnswersAsInMemory(r => r.Uuids, GuidSet.From(Guid.Parse("11111111-1111-1111-1111-111111111111")), GuidSet.From(Guid.Empty));
    }

    [TestMethod]
    public async Task ANullableColumn_MatchesNullAsInMemory()
    {
        // The first row's OptionalTags is NULL, the second's {c}.
        await AssertAnswersAsInMemory(r => r.OptionalTags, null, StringSet.From("x"));
    }

    [TestMethod]
    public async Task IntegerAndDecimalFamilies_AnswerAsInMemory()
    {
        await AssertAnswersAsInMemory(r => r.SmallCodes, Int16Set.From(2, 1), Int16Set.From(3));
        await AssertAnswersAsInMemory(r => r.Codes, Int32Set.From(2, 1, 1), Int32Set.From(3));
        await AssertAnswersAsInMemory(r => r.BigCodes, Int64Set.From(9_000_000_000L), Int64Set.From(1));
        // Another scale: 1.50 is 1.5 in memory and as numeric.
        await AssertAnswersAsInMemory(r => r.Rates, DecimalSet.From(1.50m), DecimalSet.From(2m));
    }

    [TestMethod]
    public async Task TemporalFamilies_AnswerAsInMemory()
    {
        await AssertAnswersAsInMemory(r => r.BlackoutDates, DateSet.From(new DateOnly(2024, 6, 15)), DateSet.From(new DateOnly(2024, 6, 16)));
        await AssertAnswersAsInMemory(r => r.Slots, TimeSet.From(new TimeOnly(9, 30, 15, 250, 125)), TimeSet.From(new TimeOnly(9, 30)));
        await AssertAnswersAsInMemory(r => r.Marks, DateTimeSet.From(Moment.DateTime), DateTimeSet.From(Moment.DateTime.AddDays(1)));
        // The same instant at another offset.
        await AssertAnswersAsInMemory(r => r.Stamps, DateTimeOffsetSet.From(Moment.ToOffset(TimeSpan.FromHours(2))), DateTimeOffsetSet.From(Moment.AddDays(1)));
    }

    [TestMethod]
    public async Task NodaTimeFamilies_AnswerAsInMemory()
    {
        await AssertAnswersAsInMemory(r => r.NodaHolidays, LocalDateSet.From(new LocalDate(2024, 6, 15)), LocalDateSet.From(new LocalDate(2024, 6, 16)));
        await AssertAnswersAsInMemory(r => r.NodaMarks, LocalDateTimeSet.From(LocalDateTime.FromDateTime(Moment.DateTime)), LocalDateTimeSet.From(new LocalDateTime(2024, 1, 1, 0, 0)));
        await AssertAnswersAsInMemory(r => r.NodaOccurrences, InstantSet.From(Instant.FromDateTimeOffset(Moment)), InstantSet.From(Instant.FromUnixTimeSeconds(0)));
        await AssertAnswersAsInMemory(r => r.NodaSlots, LocalTimeSet.From(new LocalTime(9, 30, 15)), LocalTimeSet.From(new LocalTime(9, 30)));
        await AssertAnswersAsInMemory(r => r.BillingMonths, YearMonthSet.From(new YearMonth(2024, 6)), YearMonthSet.From(new YearMonth(2024, 7)));
    }

    [TestMethod]
    public async Task WrapperArities_AnswerAsInMemory()
    {
        await AssertAnswersAsInMemory(r => r.Permissions, StringSet<TestKey>.From([TestKey.Parse(" USERS.READ ", null)]), StringSet<TestKey>.From([TestKey.Parse("users.write", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedTenants, GuidSet<TenantId>.From([TenantId.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff", null)]), GuidSet<TenantId>.From([TenantId.Parse("11111111-1111-1111-1111-111111111111", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedTinyCodes, Int16Set<TinyCode>.From([TinyCode.Parse("-7", null)]), Int16Set<TinyCode>.From([TinyCode.Parse("7", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedCodes, Int32Set<SmallCode>.From([SmallCode.Parse("42", null)]), Int32Set<SmallCode>.From([SmallCode.Parse("43", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedBigCodes, Int64Set<LargeCode>.From([LargeCode.Parse("9007199254740993", null)]), Int64Set<LargeCode>.From([LargeCode.Parse("9007199254740992", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedRates, DecimalSet<Money>.From([Money.Parse("12.50", null)]), DecimalSet<Money>.From([Money.Parse("12.51", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedDays, DateSet<BusinessDate>.From([BusinessDate.Parse("2024-06-15", null)]), DateSet<BusinessDate>.From([BusinessDate.Parse("2024-06-16", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedSlots, TimeSet<ShiftTime>.From([ShiftTime.Parse("09:30:15.25", null)]), TimeSet<ShiftTime>.From([ShiftTime.Parse("09:30:15", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedStamps, DateTimeOffsetSet<EventStamp>.From([EventStamp.Parse("2024-06-15T08:30:00.123456+00:00", null)]), DateTimeOffsetSet<EventStamp>.From([EventStamp.Parse("2024-06-15T08:30:00+00:00", null)]));
        await AssertAnswersAsInMemory(r => r.Audits, DateTimeSet<AuditStamp>.From([new AuditStamp(Moment.DateTime)]), DateTimeSet<AuditStamp>.From([new AuditStamp(Moment.DateTime.AddDays(1))]));
        await AssertAnswersAsInMemory(r => r.WrappedNodaDays, LocalDateSet<CalendarDay>.From([CalendarDay.Parse("2024-06-15", null)]), LocalDateSet<CalendarDay>.From([CalendarDay.Parse("2024-06-16", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedNodaMarks, LocalDateTimeSet<WallClockStamp>.From([WallClockStamp.Parse("2024-06-15T10:30:15.123456", null)]), LocalDateTimeSet<WallClockStamp>.From([WallClockStamp.Parse("2024-06-15T10:30:15", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedNodaMoments, InstantSet<EventInstant>.From([EventInstant.Parse("2024-06-15T10:30:15.123456Z", null)]), InstantSet<EventInstant>.From([EventInstant.Parse("2024-06-15T10:30:15Z", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedNodaSlots, LocalTimeSet<OpeningTime>.From([OpeningTime.Parse("09:30:15.123456", null)]), LocalTimeSet<OpeningTime>.From([OpeningTime.Parse("09:30:15", null)]));
        await AssertAnswersAsInMemory(r => r.WrappedMonths, YearMonthSet<BillingMonth>.From([BillingMonth.Parse("2024-06", null)]), YearMonthSet<BillingMonth>.From([BillingMonth.Parse("2024-07", null)]));
    }

    [TestMethod]
    public async Task AnEmptyCollection_MatchesNothing()
    {
        await using var context = await Context();
        var wanted = new List<StringSet>();

        Assert.AreEqual(0, await Rows(context).CountAsync(r => wanted.Contains(r.Tags)));
    }

    [TestMethod]
    public async Task AnyAndCountOverTheCollection_AnswerAsInMemory()
    {
        await using var context = await Context();
        var wanted = new List<StringSet> { StringSet.From("b", "x"), StringSet.From("y") };

        var loaded  = await Rows(context).AsNoTracking().ToListAsync();

        // Read from VALUES (@wanted1), (@wanted2) rather than unnest of an array of arrays.
        var overlapping = loaded.Where(r => wanted.Any(w => w.Overlaps(r.Tags))).Select(r => r.Id).ToList();
        CollectionAssert.AreEqual(new[] { Wanted }, overlapping, "the data must make Any pick one row");
        CollectionAssert.AreEqual(
            overlapping,
            await Rows(context).Where(r => wanted.Any(w => w.Overlaps(r.Tags))).Select(r => r.Id).ToListAsync());

        var disjoint = loaded.Where(r => wanted.Count(w => w.Overlaps(r.Tags)) == 0).Select(r => r.Id).ToList();
        CollectionAssert.AreEqual(new[] { Unwanted }, disjoint, "the data must make Count pick one row");
        CollectionAssert.AreEqual(
            disjoint,
            await Rows(context).Where(r => wanted.Count(w => w.Overlaps(r.Tags)) == 0).Select(r => r.Id).ToListAsync());
    }

    [TestMethod]
    public async Task EFParameter_IsRefusedWithWhatToWriteInstead()
    {
        await using var context = await Context();
        var wanted = new List<StringSet> { StringSet.From("a", "b") };

        var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Rows(context).CountAsync(r => EF.Parameter(wanted).Contains(r.Tags)));

        StringAssert.Contains(refusal.Message, "Remove 'EF.Parameter'");
    }

    [TestMethod]
    public async Task ContainsOfAServerSideUnion_IsStillRefused()
    {
        await using var context = await Context();

        // In memory {a,b} ∪ {a,b} is {a,b}, which is wanted; on the server it is {a,b,a,b}, which
        // equals neither set. IN is equality against each, so it is refused like ==.
        var wanted = new List<StringSet> { StringSet.From("a", "b"), StringSet.From("x") };

        var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Rows(context).CountAsync(r => wanted.Contains(r.Tags.Union(r.Tags))));

        StringAssert.Contains(refusal.Message, "equality");
    }
}

using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Live-PostgreSQL coverage for the two v5 additions: <c>TimeRange</c> over the custom
/// <c>timerange</c> type (created by <c>HasPostgresRange</c>, wired through
/// <c>EnableUnmappedTypes</c>), and <c>YearMonthRange</c> over a month-aligned
/// <c>daterange</c>. Verifies round-trips and that the server's polymorphic range
/// operators agree with the in-memory implementations.
/// </summary>
[TestClass]
public class TimeAndYearMonthIntegrationTests
{
    private static YearMonth Ym(int year, int month) => new(year, month);

    private static async Task Seed(params Reservation[] rows)
    {
        await using var context = new IntegrationDbContext();
        context.Reservations.AddRange(rows);
        await context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // TimeRange — custom timerange type
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task TimeRange_AllShapes_RoundTripUnchanged()
    {
        ContainerLifecycle.RequireDatabase();

        var shapes = new (int Id, TimeRange Value)[]
        {
            (7001, TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0))),
            (7002, TimeRange.CreateFinite(new TimeOnly(9, 0, 0, 123, 456), new TimeOnly(17, 0), startInclusive: false, endInclusive: true)),
            (7003, TimeRange.Empty),
            (7004, TimeRange.Infinite),
            (7005, TimeRange.CreateUnboundedEnd(new TimeOnly(22, 0))),
            (7006, TimeRange.CreateUnboundedStart(new TimeOnly(6, 0)))
        };

        await Seed(shapes.Select(s => new Reservation { Id = s.Id, OpeningHours = s.Value }).ToArray());

        await using var context = new IntegrationDbContext();
        foreach (var (id, value) in shapes)
        {
            var loaded = await context.Reservations.SingleAsync(r => r.Id == id);
            Assert.AreEqual(value, loaded.OpeningHours, $"Round-trip failed for Id {id}: {value}");
        }
    }

    [TestMethod]
    public async Task TimeRangeSet_OvernightWindow_RoundTripsAsTimeMultirange()
    {
        ContainerLifecycle.RequireDatabase();

        // 22:00–06:00 crosses midnight: two ranges in one multirange.
        var nightShift = RangeSet<TimeRange, TimeOnly>.From(
        [
            TimeRange.CreateUnboundedStart(new TimeOnly(6, 0)),
            TimeRange.CreateUnboundedEnd(new TimeOnly(22, 0))
        ]);

        await Seed(new Reservation { Id = 7011, OpeningWindows = nightShift });

        await using var context = new IntegrationDbContext();
        var loaded = await context.Reservations.SingleAsync(r => r.Id == 7011);

        Assert.AreEqual(nightShift, loaded.OpeningWindows);
        Assert.AreEqual(2, loaded.OpeningWindows.Count);
    }

    [TestMethod]
    public async Task TimeRange_ServerOperators_AgreeWithInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var morning   = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(12, 0));
        var afternoon = TimeRange.CreateFinite(new TimeOnly(12, 0), new TimeOnly(17, 0));
        var evening   = TimeRange.CreateFinite(new TimeOnly(18, 0), new TimeOnly(23, 0));

        await Seed(
            new Reservation { Id = 7021, OpeningHours = morning },
            new Reservation { Id = 7022, OpeningHours = afternoon },
            new Reservation { Id = 7023, OpeningHours = evening });

        var lunch = new TimeOnly(11, 30);

        await using var context = new IntegrationDbContext();

        var containsLunch = await context.Reservations
            .Where(r => r.Id >= 7021 && r.Id <= 7023 && r.OpeningHours.Contains(lunch))
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 7021 }, containsLunch);

        // The polymorphic -|- operator works on the custom range type.
        var adjacentToMorning = await context.Reservations
            .Where(r => r.Id >= 7021 && r.Id <= 7023 && r.OpeningHours.IsAdjacentTo(morning))
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 7022 }, adjacentToMorning);
        Assert.IsTrue(afternoon.IsAdjacentTo(morning));

        // Server-side bound accessor on the custom type agrees with the model.
        var upper = await context.Reservations
            .Where(r => r.Id == 7021)
            .Select(r => r.OpeningHours.UpperBound())
            .SingleAsync();
        Assert.AreEqual(morning.UpperBound(), upper);
    }

    [TestMethod]
    public async Task TimeRange_ServerRangeAgg_AgreesWithInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        TimeRange[] shifts =
        [
            TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(12, 0)),
            TimeRange.CreateFinite(new TimeOnly(12, 0), new TimeOnly(17, 0)),
            TimeRange.CreateFinite(new TimeOnly(20, 0), new TimeOnly(22, 0))
        ];

        await Seed(shifts.Select((shift, index) =>
            new Reservation { Id = 7031 + index, GroupKey = 7031, OpeningHours = shift }).ToArray());

        await using var context = new IntegrationDbContext();

        var aggregated = await context.Reservations
            .Where(r => r.GroupKey == 7031)
            .GroupBy(r => r.GroupKey)
            .Select(g => g.Select(r => r.OpeningHours).RangeAgg())
            .SingleAsync();

        Assert.AreEqual(shifts.RangeAgg(), aggregated);
        Assert.AreEqual(2, aggregated.Count); // adjacent morning+afternoon merged
    }

    // -------------------------------------------------------------------------
    // YearMonthRange — month-aligned daterange
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task YearMonthRange_AllShapes_RoundTripUnchanged()
    {
        ContainerLifecycle.RequireDatabase();

        var shapes = new (int Id, YearMonthRange Value)[]
        {
            (7101, YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 12))),
            (7102, YearMonthRange.CreateFinite(Ym(2024, 2), Ym(2024, 2))), // single leap-February
            (7103, YearMonthRange.Empty),
            (7104, YearMonthRange.Infinite),
            (7105, YearMonthRange.CreateUnboundedEnd(Ym(2024, 7))),
            (7106, YearMonthRange.CreateUnboundedStart(Ym(2024, 6), endInclusive: true))
        };

        await Seed(shapes.Select(s => new Reservation { Id = s.Id, BillingPeriod = s.Value }).ToArray());

        await using var context = new IntegrationDbContext();
        foreach (var (id, value) in shapes)
        {
            var loaded = await context.Reservations.SingleAsync(r => r.Id == id);
            Assert.AreEqual(value, loaded.BillingPeriod, $"Round-trip failed for Id {id}: {value}");
        }
    }

    [TestMethod]
    public async Task YearMonthRange_IsStoredAsMonthAlignedDateRange()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(new Reservation
        {
            Id            = 7111,
            BillingPeriod = YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 3))
        });

        // Read the same column as a LocalDateRange via raw SQL to see the stored form:
        // the canonical daterange covering January through March 2024.
        await using var context = new IntegrationDbContext();
        var storedText = await context.Database
            .SqlQuery<string>($"SELECT \"BillingPeriod\"::text AS \"Value\" FROM \"Reservations\" WHERE \"Id\" = 7111")
            .SingleAsync();

        Assert.AreEqual("[2024-01-01,2024-04-01)", storedText);
    }

    [TestMethod]
    public async Task YearMonthRange_ServerOperators_AgreeWithInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var h1 = YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 6));
        var h2 = YearMonthRange.CreateFinite(Ym(2024, 7), Ym(2024, 12));
        var q3 = YearMonthRange.CreateFinite(Ym(2024, 7), Ym(2024, 9));

        await Seed(
            new Reservation { Id = 7121, BillingPeriod = h1 },
            new Reservation { Id = 7122, BillingPeriod = h2 });

        var march = Ym(2024, 3);

        await using var context = new IntegrationDbContext();

        var containsMarch = await context.Reservations
            .Where(r => r.Id >= 7121 && r.Id <= 7122 && r.BillingPeriod.Contains(march))
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 7121 }, containsMarch);

        // Month adjacency maps exactly onto daterange adjacency of the aligned forms.
        var adjacentToH1 = await context.Reservations
            .Where(r => r.Id >= 7121 && r.Id <= 7122 && r.BillingPeriod.IsAdjacentTo(h1))
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 7122 }, adjacentToH1);
        Assert.IsTrue(h2.IsAdjacentTo(h1));

        var containsQ3 = await context.Reservations
            .Where(r => r.Id >= 7121 && r.Id <= 7122 && r.BillingPeriod.Contains(q3))
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 7122 }, containsQ3);
    }

    [TestMethod]
    public async Task YearMonthRange_ServerBoundAccessors_AgreeWithInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var period = YearMonthRange.CreateFinite(Ym(2024, 3), Ym(2025, 2));
        await Seed(new Reservation { Id = 7131, BillingPeriod = period });

        await using var context = new IntegrationDbContext();

        var bounds = await context.Reservations
            .Where(r => r.Id == 7131)
            .Select(r => new { Lower = r.BillingPeriod.LowerBound(), Upper = r.BillingPeriod.UpperBound() })
            .SingleAsync();

        // upper() of the stored daterange is 2025-03-01; - 1 is 2025-02-28, whose month
        // is the model's inclusive upper bound.
        Assert.AreEqual(period.LowerBound(), bounds.Lower);
        Assert.AreEqual(period.UpperBound(), bounds.Upper);
        Assert.AreEqual(Ym(2025, 2), bounds.Upper);
    }

    [TestMethod]
    public async Task YearMonthRange_ServerRangeAgg_AgreesWithInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        YearMonthRange[] periods =
        [
            YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 3)),
            YearMonthRange.CreateFinite(Ym(2024, 4), Ym(2024, 6)),
            YearMonthRange.CreateFinite(Ym(2024, 10), Ym(2024, 12))
        ];

        await Seed(periods.Select((period, index) =>
            new Reservation { Id = 7141 + index, GroupKey = 7141, BillingPeriod = period }).ToArray());

        await using var context = new IntegrationDbContext();

        var aggregated = await context.Reservations
            .Where(r => r.GroupKey == 7141)
            .GroupBy(r => r.GroupKey)
            .Select(g => g.Select(r => r.BillingPeriod).RangeAgg())
            .SingleAsync();

        // Adjacent quarters merge on the server (their date forms are adjacent dateranges)
        // exactly as they do in memory (their months are one step apart).
        Assert.AreEqual(periods.RangeAgg(), aggregated);
        Assert.AreEqual(2, aggregated.Count);
    }

    [TestMethod]
    public async Task YearMonthRangeSet_RoundTripsAsDateMultirange()
    {
        ContainerLifecycle.RequireDatabase();

        var set = RangeSet<YearMonthRange, YearMonth>.From(
        [
            YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 3)),
            YearMonthRange.CreateFinite(Ym(2024, 7), Ym(2024, 9))
        ]);

        await Seed(new Reservation { Id = 7151, BillingPeriods = set });

        await using var context = new IntegrationDbContext();
        var loaded = await context.Reservations.SingleAsync(r => r.Id == 7151);

        Assert.AreEqual(set, loaded.BillingPeriods);
    }
}

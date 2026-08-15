using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

using DateSet    = RangeSet<LocalDateRange, LocalDate>;
using InstantRangeSet = RangeSet<InstantRange, Instant>;

/// <summary>
/// Live-PostgreSQL coverage for the NodaTime range types: value round-trips (including the
/// precision and infinity rules at the Npgsql boundary) and SQL-vs-in-memory parity for the
/// operations whose discrete canonicalization differs between server and model.
/// Id namespace: 5xxx for round-trips, 6xxx for executed queries.
/// </summary>
[TestClass]
public class NodaTimeIntegrationTests
{
    private static LocalDate D(int y, int m, int d) => new(y, m, d);
    private static Instant I(int y, int m, int d, int h = 0, int min = 0) => Instant.FromUtc(y, m, d, h, min);

    private static async Task<Reservation> RoundTrip(Reservation entity)
    {
        await using (var write = new IntegrationDbContext())
        {
            write.Reservations.Add(entity);
            await write.SaveChangesAsync();
        }

        await using var read = new IntegrationDbContext();
        return await read.Reservations.SingleAsync(r => r.Id == entity.Id);
    }

    // -------------------------------------------------------------------------
    // Round-trips
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task FiniteValues_AllThreeTypes_RoundTripUnchanged()
    {
        ContainerLifecycle.RequireDatabase();

        var original = new Reservation
        {
            Id            = 5001,
            NodaPeriod    = LocalDateRange.CreateFinite(D(2024, 1, 1), D(2024, 12, 31)),
            NodaWallClock = LocalDateTimeRange.CreateFinite(
                new LocalDateTime(2024, 6, 1, 8, 0), new LocalDateTime(2024, 6, 1, 17, 30)),
            NodaWindow    = InstantRange.CreateFinite(I(2024, 6, 1, 8, 0), I(2024, 6, 1, 17, 0))
        };

        var loaded = await RoundTrip(original);

        Assert.AreEqual(original.NodaPeriod, loaded.NodaPeriod);
        Assert.AreEqual(original.NodaWallClock, loaded.NodaWallClock);
        Assert.AreEqual(original.NodaWindow, loaded.NodaWindow);
    }

    [TestMethod]
    public async Task SpecialShapes_RoundTripUnchanged()
    {
        ContainerLifecycle.RequireDatabase();

        var original = new Reservation
        {
            Id            = 5002,
            NodaPeriod    = LocalDateRange.CreateUnboundedEnd(D(2024, 1, 1)),
            NodaWallClock = LocalDateTimeRange.Infinite,
            NodaWindow    = InstantRange.CreateUnboundedStart(I(2024, 6, 1), endInclusive: false)
        };

        var loaded = await RoundTrip(original);

        Assert.AreEqual(original.NodaPeriod, loaded.NodaPeriod);
        Assert.AreEqual(LocalDateTimeRange.Infinite, loaded.NodaWallClock);
        Assert.AreEqual(original.NodaWindow, loaded.NodaWindow);

        var empty = await RoundTrip(new Reservation { Id = 5003, NodaWindow = InstantRange.Empty });
        Assert.AreEqual(InstantRange.Empty, empty.NodaWindow);
    }

    [TestMethod]
    public async Task DiscreteCanonicalization_ServerAgreesWithModel()
    {
        ContainerLifecycle.RequireDatabase();

        // The model canonicalizes [1.1., 10.1.) to [1.1., 9.1.] at construction; PostgreSQL
        // canonicalizes daterange to half-open. Round-tripping must land on the same value.
        var halfOpen = LocalDateRange.CreateFinite(D(2024, 1, 1), D(2024, 1, 10), true, false);
        Assert.AreEqual("[2024-01-01,2024-01-09]", halfOpen.ToString());

        var loaded = await RoundTrip(new Reservation { Id = 5004, NodaPeriod = halfOpen });
        Assert.AreEqual(halfOpen, loaded.NodaPeriod);
    }

    [TestMethod]
    public async Task Multiranges_RoundTripNormalized()
    {
        ContainerLifecycle.RequireDatabase();

        var original = new Reservation
        {
            Id              = 5005,
            NodaBlockedDays = DateSet.From([
                LocalDateRange.CreateFinite(D(2024, 3, 1), D(2024, 3, 10)),
                LocalDateRange.CreateFinite(D(2024, 1, 1), D(2024, 1, 31))
            ]),
            NodaWindows     = InstantRangeSet.From([
                InstantRange.CreateFinite(I(2024, 6, 1), I(2024, 6, 15)),
                InstantRange.CreateFinite(I(2024, 7, 1), I(2024, 7, 15))
            ])
        };

        var loaded = await RoundTrip(original);

        Assert.AreEqual(original.NodaBlockedDays, loaded.NodaBlockedDays);
        Assert.AreEqual(original.NodaWindows, loaded.NodaWindows);
    }

    [TestMethod]
    public async Task InstantMinMax_MapToPostgresInfinity_AndBack()
    {
        ContainerLifecycle.RequireDatabase();

        // Npgsql maps Instant.MinValue/MaxValue to -infinity/infinity by default. These are
        // *finite* bounds that happen to be infinite — distinct from an unbounded side.
        var original = new Reservation
        {
            Id         = 5006,
            NodaWindow = InstantRange.CreateFinite(Instant.MinValue, Instant.MaxValue, true, true)
        };

        var loaded = await RoundTrip(original);

        var finite = (IFiniteRange<Instant>)loaded.NodaWindow;
        Assert.AreEqual(Instant.MinValue, finite.Start);
        Assert.AreEqual(Instant.MaxValue, finite.End);

        // The shape stays Finite: upper_inf/lower_inf are false on the server.
        await using var context = new IntegrationDbContext();
        var isUnbounded = await context.Reservations
            .Where(r => r.Id == 5006)
            .Select(r => r.NodaWindow.IsUnboundedEnd() || r.NodaWindow.IsUnboundedStart())
            .SingleAsync();
        Assert.IsFalse(isUnbounded);
    }

    [TestMethod]
    public async Task SubMicrosecondPrecision_IsReducedAtTheBoundary()
    {
        ContainerLifecycle.RequireDatabase();

        // NodaTime carries nanoseconds; PostgreSQL stores microseconds. Pin the boundary
        // behavior: the value read back has microsecond precision.
        var nanos = I(2024, 6, 1).PlusNanoseconds(123_456_789);
        var range = InstantRange.CreateFinite(nanos, I(2024, 6, 2));

        var loaded = await RoundTrip(new Reservation { Id = 5007, NodaWindow = range });

        var start = ((IFiniteRange<Instant>)loaded.NodaWindow).Start;

        // Whole microseconds survive; the sub-microsecond remainder does not.
        var expectedMicros = I(2024, 6, 1).PlusNanoseconds(123_456_000);
        var expectedRounded = I(2024, 6, 1).PlusNanoseconds(123_457_000);
        Assert.IsTrue(
            start == expectedMicros || start == expectedRounded,
            $"Expected microsecond-truncated or -rounded instant, got {start} ({start.ToUnixTimeTicks()} ticks)");
    }

    // -------------------------------------------------------------------------
    // SQL-vs-in-memory parity
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ContainmentAndOverlap_ReturnExpectedRows()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(
            new Reservation { Id = 6001, NodaPeriod = LocalDateRange.CreateFinite(D(2024, 1, 1), D(2024, 6, 30)) },
            new Reservation { Id = 6002, NodaPeriod = LocalDateRange.CreateFinite(D(2024, 7, 1), D(2024, 12, 31)) },
            new Reservation { Id = 6003, NodaPeriod = LocalDateRange.Empty });

        var day = D(2024, 3, 15);
        var mid = LocalDateRange.CreateFinite(D(2024, 6, 1), D(2024, 7, 31));

        await using var context = new IntegrationDbContext();

        var containsDay = await context.Reservations
            .Where(r => r.Id >= 6001 && r.Id <= 6003 && r.NodaPeriod.Contains(day))
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 6001 }, containsDay);

        var overlapsMid = await context.Reservations
            .Where(r => r.Id >= 6001 && r.Id <= 6003 && r.NodaPeriod.Overlaps(mid))
            .Select(r => r.Id)
            .OrderBy(id => id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 6001, 6002 }, overlapsMid);
    }

    [TestMethod]
    public async Task UpperBound_Discrete_ServerEqualsInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        // The critical discrete-canonicalization parity check: server-side upper() - 1
        // must equal the model's inclusive UpperBound().
        var period = LocalDateRange.CreateFinite(D(2024, 1, 1), D(2024, 12, 31));
        await Seed(new Reservation { Id = 6011, NodaPeriod = period });

        await using var context = new IntegrationDbContext();
        var serverUpper = await context.Reservations
            .Where(r => r.Id == 6011)
            .Select(r => r.NodaPeriod.UpperBound())
            .SingleAsync();

        Assert.AreEqual(period.UpperBound(), serverUpper);
        Assert.AreEqual(D(2024, 12, 31), serverUpper);
    }

    [TestMethod]
    public async Task RangeAgg_ServerEqualsInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var ranges = new[]
        {
            LocalDateRange.CreateFinite(D(2024, 1, 1), D(2024, 1, 10)),
            LocalDateRange.CreateFinite(D(2024, 1, 5), D(2024, 1, 20)),
            LocalDateRange.CreateFinite(D(2024, 3, 1), D(2024, 3, 10))
        };
        await Seed(ranges.Select((range, index) =>
            new Reservation { Id = 6021 + index, GroupKey = 6021, NodaPeriod = range }).ToArray());

        await using var context = new IntegrationDbContext();
        var serverAgg = await context.Reservations
            .Where(r => r.GroupKey == 6021)
            .GroupBy(r => r.GroupKey)
            .Select(g => g.Select(r => r.NodaPeriod).RangeAgg())
            .SingleAsync();

        Assert.AreEqual(ranges.RangeAgg(), serverAgg);
    }

    [TestMethod]
    public async Task MultirangeOperations_ServerEqualsInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var set = DateSet.From([
            LocalDateRange.CreateFinite(D(2024, 1, 1), D(2024, 1, 10)),
            LocalDateRange.CreateFinite(D(2024, 2, 1), D(2024, 2, 10))
        ]);
        var operand = LocalDateRange.CreateFinite(D(2024, 1, 5), D(2024, 2, 5));

        await Seed(new Reservation { Id = 6031, NodaBlockedDays = set });

        await using var context = new IntegrationDbContext();
        var serverUnion = await context.Reservations
            .Where(r => r.Id == 6031)
            .Select(r => r.NodaBlockedDays.Union(operand))
            .SingleAsync();

        Assert.AreEqual(set.Union(operand), serverUnion);
    }

    private static async Task Seed(params Reservation[] rows)
    {
        await using var context = new IntegrationDbContext();
        context.Reservations.AddRange(rows);
        await context.SaveChangesAsync();
    }
}

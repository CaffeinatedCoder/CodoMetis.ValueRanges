using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Persists every range and multirange type to a live PostgreSQL instance and reads it
/// back, verifying value-level round-trips including the timestamp normalization rules.
/// Each test uses its own Id so tests can run in parallel against the shared database.
/// </summary>
[TestClass]
public class RangeRoundTripTests
{
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

    [TestMethod]
    public async Task FiniteValues_AllSixTypes_RoundTripUnchanged()
    {
        ContainerLifecycle.RequireDatabase();

        var original = new Reservation
        {
            Id          = 1001,
            Seats       = Int32Range.CreateFinite(1, 10),
            Tickets     = Int64Range.CreateFinite(100L, 200L),
            Price       = DecimalRange.CreateFinite(9.99m, 19.99m),
            Period      = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
            LocalTime   = DateTimeRange.CreateFinite(new DateTime(2024, 6, 1, 8, 0, 0), new DateTime(2024, 6, 1, 17, 30, 0)),
            InstantTime = DateTimeOffsetRange.CreateFinite(
                new DateTimeOffset(2024, 6, 1, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 6, 1, 17, 0, 0, TimeSpan.Zero))
        };

        var loaded = await RoundTrip(original);

        Assert.AreEqual(original.Seats, loaded.Seats);
        Assert.AreEqual(original.Tickets, loaded.Tickets);
        Assert.AreEqual(original.Price, loaded.Price);
        Assert.AreEqual(original.Period, loaded.Period);
        Assert.AreEqual(original.LocalTime, loaded.LocalTime);
        Assert.AreEqual(original.InstantTime, loaded.InstantTime);
    }

    [TestMethod]
    public async Task SpecialShapes_RoundTripUnchanged()
    {
        ContainerLifecycle.RequireDatabase();

        var original = new Reservation
        {
            Id      = 1002,
            Seats   = Int32Range.Empty,
            Tickets = Int64Range.Infinite,
            Price   = DecimalRange.CreateUnboundedStart(100m, endInclusive: false),
            Period  = DateRange.CreateUnboundedEnd(new DateOnly(2024, 1, 1))
        };

        var loaded = await RoundTrip(original);

        Assert.AreEqual(Int32Range.Empty, loaded.Seats);
        Assert.AreEqual(Int64Range.Infinite, loaded.Tickets);
        Assert.AreEqual(original.Price, loaded.Price);
        Assert.AreEqual(original.Period, loaded.Period);
    }

    [TestMethod]
    public async Task Multiranges_RoundTripNormalized()
    {
        ContainerLifecycle.RequireDatabase();

        var blocked = RangeSet<DateRange, DateOnly>.From([
            DateRange.CreateFinite(new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 10)),
            DateRange.CreateFinite(new DateOnly(2024, 8, 1), new DateOnly(2024, 8, 15))
        ]);

        // Unsorted, overlapping input — normalization must survive the round-trip.
        var seatBlocks = RangeSet<Int32Range, int>.From([
            Int32Range.CreateFinite(20, 30),
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(4, 10)
        ]);

        var loaded = await RoundTrip(new Reservation { Id = 1003, BlockedDays = blocked, SeatBlocks = seatBlocks });

        Assert.AreEqual(blocked, loaded.BlockedDays);
        Assert.AreEqual(seatBlocks, loaded.SeatBlocks);
        Assert.IsTrue(loaded.SeatBlocks == seatBlocks); // the v4 operator agrees
        Assert.AreEqual(2, loaded.SeatBlocks.Count);    // {[1,10],[20,30]}
    }

    [TestMethod]
    public async Task UtcKindedTimestamp_IsReinterpretedAsWallClock()
    {
        ContainerLifecycle.RequireDatabase();

        // tsrange is `timestamp without time zone`: UTC-kinded input is written as its
        // wall-clock face, not converted. Ticks survive; the Kind comes back Unspecified.
        var start = new DateTime(2024, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2024, 6, 1, 17, 0, 0, DateTimeKind.Utc);

        var loaded = await RoundTrip(new Reservation
        {
            Id        = 1004,
            LocalTime = DateTimeRange.CreateFinite(start, end)
        });

        var finite = (IFiniteRange<DateTime>)loaded.LocalTime;
        Assert.AreEqual(start.Ticks, finite.Start.Ticks);
        Assert.AreEqual(DateTimeKind.Unspecified, finite.Start.Kind);
        Assert.AreEqual(loaded.LocalTime, DateTimeRange.CreateFinite(start, end)); // DateTime equality ignores Kind
    }

    [TestMethod]
    public async Task OffsetTimestamptz_PreservesInstantNotOffset()
    {
        ContainerLifecycle.RequireDatabase();

        // tstzrange stores instants: a +02:00 bound comes back at +00:00 but is the same
        // point in time, and DateTimeOffset equality is instant-based.
        var original = DateTimeOffsetRange.CreateFinite(
            new DateTimeOffset(2024, 6, 1, 10, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2024, 6, 1, 19, 0, 0, TimeSpan.FromHours(2)));

        var loaded = await RoundTrip(new Reservation { Id = 1005, InstantTime = original });

        Assert.AreEqual(original, loaded.InstantTime);
        var finite = (IFiniteRange<DateTimeOffset>)loaded.InstantTime;
        Assert.AreEqual(TimeSpan.Zero, finite.Start.Offset);
    }

    [TestMethod]
    public async Task MaxValueTimestampBound_BecomesInfinityButStaysFinite()
    {
        ContainerLifecycle.RequireDatabase();

        // Npgsql maps DateTime.MaxValue to the PostgreSQL `infinity` timestamp by default.
        // That is an explicit bound value, not an unbounded side: the shape stays Finite
        // and upper_inf remains false on the server.
        var range = DateTimeRange.CreateFinite(new DateTime(2024, 1, 1), DateTime.MaxValue, true, true);

        var loaded = await RoundTrip(new Reservation { Id = 1006, LocalTime = range });

        Assert.AreEqual(range, loaded.LocalTime);
        Assert.IsInstanceOfType<IFiniteRange<DateTime>>(loaded.LocalTime);

        await using var context = new IntegrationDbContext();
        var serverSaysUnbounded = await context.Reservations
            .Where(r => r.Id == 1006)
            .Select(r => r.LocalTime.IsUnboundedEnd())
            .SingleAsync();
        Assert.IsFalse(serverSaysUnbounded);
    }
}

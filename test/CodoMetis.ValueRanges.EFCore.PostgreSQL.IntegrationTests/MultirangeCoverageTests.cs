using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Round-trips the five multirange types that had never been mapped to a column, and executes the
/// server-side range constructors — the two coverage gaps this suite had for ranges.
/// </summary>
/// <remarks>
/// <para>
/// Six of the eleven <c>RangeSet&lt;,&gt;</c> instantiations were covered end to end and five —
/// over <see cref="Int64Range"/>, <see cref="DecimalRange"/>, <see cref="DateTimeRange"/>,
/// <see cref="DateTimeOffsetRange"/> and <c>LocalDateTimeRange</c> — appeared in no model at all.
/// Their store type, literal form and normalization on read were claims nothing checked.
/// </para>
/// <para>
/// The stored column text is asserted alongside CLR equality throughout, because a bridge that
/// coarsens a value consistently on both legs round-trips perfectly and stores the wrong thing.
/// </para>
/// </remarks>
[TestClass]
public class MultirangeCoverageTests
{
    private static async Task Seed(params Reservation[] rows)
    {
        await using var context = new IntegrationDbContext();
        context.Reservations.AddRange(rows);
        await context.SaveChangesAsync();
    }

    /// <summary>Reads one column back as PostgreSQL's own text, not through the type mapping.</summary>
    private static async Task<string> StoredText(int id, string column)
    {
        await using var context = new IntegrationDbContext();
        await using var command = context.Database.GetDbConnection().CreateCommand();

        command.CommandText = $"""SELECT "{column}"::text FROM "Reservations" WHERE "Id" = {id}""";
        await context.Database.OpenConnectionAsync();

        return (string)(await command.ExecuteScalarAsync())!;
    }

    [TestMethod]
    public async Task Int64AndDecimalMultiranges_RoundTripAndStoreTheExpectedLiteral()
    {
        ContainerLifecycle.RequireDatabase();

        // Beyond 2^53, where a value that detoured through a double would come back changed.
        var tickets = RangeSet<Int64Range, long>.From([
            Int64Range.CreateFinite(1L, 5L),
            Int64Range.CreateFinite(9_007_199_254_740_993L, 9_007_199_254_740_999L)
        ]);

        // Trailing-zero scale, which numrange preserves and a double would not.
        var prices = RangeSet<DecimalRange, decimal>.From([
            DecimalRange.CreateFinite(1.50m, 2.25m),
            DecimalRange.CreateFinite(10m, 20m)
        ]);

        await Seed(new Reservation { Id = 9101, TicketBlocks = tickets, PriceBands = prices });

        await using var context = new IntegrationDbContext();
        var stored = await context.Reservations.SingleAsync(r => r.Id == 9101);

        Assert.AreEqual(tickets, stored.TicketBlocks);
        Assert.AreEqual(prices, stored.PriceBands);

        // int8range is discrete, so PostgreSQL canonicalizes to half-open; numrange is not.
        Assert.AreEqual(
            "{[1,6),[9007199254740993,9007199254741000)}",
            await StoredText(9101, nameof(Reservation.TicketBlocks)));
        Assert.AreEqual(
            "{[1.50,2.25),[10,20)}",
            await StoredText(9101, nameof(Reservation.PriceBands)));
    }

    [TestMethod]
    public async Task TimestampMultiranges_RoundTripUnderBothKindRules()
    {
        ContainerLifecycle.RequireDatabase();

        // tsrange is wall-clock: Unspecified in, Unspecified out.
        var wallClock = RangeSet<DateTimeRange, DateTime>.From([
            DateTimeRange.CreateFinite(
                new DateTime(2024, 1, 1, 9, 30, 15, DateTimeKind.Unspecified),
                new DateTime(2024, 1, 1, 17, 0, 0, DateTimeKind.Unspecified)),
            DateTimeRange.CreateFinite(
                new DateTime(2024, 2, 1, 9, 0, 0, DateTimeKind.Unspecified),
                new DateTime(2024, 2, 1, 12, 0, 0, DateTimeKind.Unspecified))
        ]);

        // tstzrange stores an instant; the offset is normalized away, the instant is not.
        var instants = RangeSet<DateTimeOffsetRange, DateTimeOffset>.From([
            DateTimeOffsetRange.CreateFinite(
                new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2024, 6, 1, 18, 0, 0, TimeSpan.FromHours(2)))
        ]);

        await Seed(new Reservation { Id = 9102, WallClockWindows = wallClock, InstantWindows = instants });

        await using var context = new IntegrationDbContext();
        var stored = await context.Reservations.SingleAsync(r => r.Id == 9102);

        Assert.AreEqual(wallClock, stored.WallClockWindows);

        // The instant survives; the offset does not, so compare on the instant.
        Assert.AreEqual(1, stored.InstantWindows.Count);
        Assert.AreEqual(
            new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.FromHours(2)).UtcDateTime,
            stored.InstantWindows.LowerBound()!.Value.UtcDateTime);

        StringAssert.Contains(
            await StoredText(9102, nameof(Reservation.WallClockWindows)), "\"2024-01-01 09:30:15\"");
    }

    [TestMethod]
    public async Task LocalDateTimeMultirange_RoundTripsThroughTheNodaTimeSatellite()
    {
        ContainerLifecycle.RequireDatabase();

        var windows = RangeSet<LocalDateTimeRange, LocalDateTime>.From([
            LocalDateTimeRange.CreateFinite(
                new LocalDateTime(2024, 1, 1, 9, 30, 15), new LocalDateTime(2024, 1, 1, 17, 0, 0)),
            LocalDateTimeRange.CreateFinite(
                new LocalDateTime(2024, 3, 1, 8, 0, 0), new LocalDateTime(2024, 3, 1, 12, 0, 0))
        ]);

        await Seed(new Reservation { Id = 9103, NodaWallClocks = windows });

        await using var context = new IntegrationDbContext();
        var stored = await context.Reservations.SingleAsync(r => r.Id == 9103);

        Assert.AreEqual(windows, stored.NodaWallClocks);
        StringAssert.Contains(
            await StoredText(9103, nameof(Reservation.NodaWallClocks)), "\"2024-01-01 09:30:15\"");
    }

    /// <summary>
    /// The operators on the five newly mapped multiranges, executed rather than asserted as SQL
    /// text — the same parity check the six already-covered ones get.
    /// </summary>
    [TestMethod]
    public async Task NewlyMappedMultiranges_ServerOperatorsAgreeWithInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var tickets = RangeSet<Int64Range, long>.From([
            Int64Range.CreateFinite(1L, 5L), Int64Range.CreateFinite(20L, 30L)
        ]);
        var prices = RangeSet<DecimalRange, decimal>.From([DecimalRange.CreateFinite(1m, 5m)]);

        await Seed(new Reservation { Id = 9104, TicketBlocks = tickets, PriceBands = prices });

        var ticketProbe = Int64Range.CreateFinite(2L, 3L);
        var priceProbe  = DecimalRange.CreateFinite(2m, 3m);

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 9104)
            .Select(r => new
            {
                ContainsTicketRange = r.TicketBlocks.Contains(ticketProbe),
                ContainsTicketValue = r.TicketBlocks.Contains(25L),
                TicketsOverlap      = r.TicketBlocks.Overlaps(ticketProbe),
                TicketsLeftOf       = r.TicketBlocks.IsStrictlyLeftOf(Int64Range.CreateFinite(100L, 200L)),
                TicketLowerBound    = r.TicketBlocks.LowerBound(),
                TicketUpperBound    = r.TicketBlocks.UpperBound(),
                PricesContain       = r.PriceBands.Contains(priceProbe),
                PriceUpperBound     = r.PriceBands.UpperBound()
            })
            .SingleAsync();

        Assert.AreEqual(tickets.Contains(ticketProbe), server.ContainsTicketRange);
        Assert.AreEqual(tickets.Contains(25L), server.ContainsTicketValue);
        Assert.AreEqual(tickets.Overlaps(ticketProbe), server.TicketsOverlap);
        Assert.AreEqual(tickets.IsStrictlyLeftOf(Int64Range.CreateFinite(100L, 200L)), server.TicketsLeftOf);
        Assert.AreEqual(tickets.LowerBound(), server.TicketLowerBound);

        // int8range is discrete, so the upper bound is the compensated upper(x) - 1.
        Assert.AreEqual(tickets.UpperBound(), server.TicketUpperBound);
        Assert.AreEqual(prices.Contains(priceProbe), server.PricesContain);
        Assert.AreEqual(prices.UpperBound(), server.PriceUpperBound);
    }

    /// <summary>
    /// A constant element operand against every range and multirange type, executed. The range
    /// operators are polymorphic (<c>anyrange @&gt; anyelement</c>), which PostgreSQL resolves
    /// without applying implicit coercions, so a constant has to arrive already typed.
    /// </summary>
    /// <remarks>
    /// <c>Int64Range.Contains(25L)</c> emitted <c>"Tickets" @&gt; 25</c> before 7.0.0 and failed at
    /// execution with <c>operator does not exist: int8range @&gt; integer</c> — a bare numeric
    /// literal is an <c>integer</c> to PostgreSQL. Nothing caught it because the translation test
    /// asserted the prefix <c>@&gt; </c> and no test executed the query. Every other element type
    /// renders self-describing literal text, so <see cref="long"/> was the only one affected.
    /// </remarks>
    [TestMethod]
    public async Task ConstantElementOperands_ExecuteForEveryElementType()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(new Reservation
        {
            Id           = 9106,
            Seats        = Int32Range.CreateFinite(1, 10),
            Tickets      = Int64Range.CreateFinite(1L, 10L),
            Price        = DecimalRange.CreateFinite(1m, 10m),
            Period       = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
            OpeningHours = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            TicketBlocks = RangeSet<Int64Range, long>.From([Int64Range.CreateFinite(1L, 10L)]),
            SeatBlocks   = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 10)])
        });

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 9106)
            .Select(r => new
            {
                Seats   = r.Seats.Contains(5),
                Tickets = r.Tickets.Contains(5L),
                Price   = r.Price.Contains(5.5m),
                Period  = r.Period.Contains(new DateOnly(2024, 6, 15)),
                Hours   = r.OpeningHours.Contains(new TimeOnly(12, 0)),
                TicketBlocks = r.TicketBlocks.Contains(5L),
                SeatBlocks   = r.SeatBlocks.Contains(5)
            })
            .SingleAsync();

        Assert.IsTrue(server.Seats);
        Assert.IsTrue(server.Tickets, "int8range @> a bare integer literal does not resolve");
        Assert.IsTrue(server.Price);
        Assert.IsTrue(server.Period);
        Assert.IsTrue(server.Hours);
        Assert.IsTrue(server.TicketBlocks, "int8multirange @> a bare integer literal does not resolve");
        Assert.IsTrue(server.SeatBlocks);
    }

    /// <summary>
    /// The range constructors evaluated on the server, which nothing executed before: every use of
    /// <c>CreateFinite</c> in this suite had constant arguments and was folded client-side.
    /// </summary>
    /// <remarks>
    /// Two operands in the emitted call carry no type of their own — the bounds string
    /// (<c>'[]'</c>) and, for the half-open factories, a bare <c>NULL</c> — so their resolution is
    /// PostgreSQL's function-overload resolution rather than anything this package controls. That
    /// makes the generated SQL a claim about the server until it is actually run, and it is the
    /// one operand position in the range translations where an element appears untyped.
    /// </remarks>
    [TestMethod]
    public async Task ServerSideRangeConstructors_AgreeWithTheInMemoryFactories()
    {
        ContainerLifecycle.RequireDatabase();

        var day      = new DateOnly(2024, 3, 1);
        var otherDay = new DateOnly(2024, 6, 30);
        var amount   = 1.50m;
        var other    = 9.75m;
        var at       = new TimeOnly(9, 0);
        var until    = new TimeOnly(17, 30);

        await Seed(new Reservation
        {
            Id = 9105, Day = day, OtherDay = otherDay,
            Amount = amount, OtherAmount = other, At = at, Until = until
        });

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 9105)
            .Select(r => new
            {
                // Discrete: the server canonicalizes to half-open and the model to closed.
                Dates      = DateRange.CreateFinite(r.Day, r.OtherDay),
                DatesOpen  = DateRange.CreateFinite(r.Day, r.OtherDay, false, false),
                DatesUpTo  = DateRange.CreateUnboundedStart(r.OtherDay),
                DatesFrom  = DateRange.CreateUnboundedEnd(r.Day),
                // Inverted bounds must give the empty range, not a PostgreSQL error.
                Inverted   = DateRange.CreateFinite(r.OtherDay, r.Day),
                // Continuous, and a custom range type whose constructor function
                // CREATE TYPE ... AS RANGE generated.
                Amounts    = DecimalRange.CreateFinite(r.Amount, r.OtherAmount),
                AmountsUpTo = DecimalRange.CreateUnboundedStart(r.OtherAmount),
                Hours      = TimeRange.CreateFinite(r.At, r.Until),
                HoursFrom  = TimeRange.CreateUnboundedEnd(r.At)
            })
            .SingleAsync();

        Assert.AreEqual(DateRange.CreateFinite(day, otherDay), server.Dates);
        Assert.AreEqual(DateRange.CreateFinite(day, otherDay, false, false), server.DatesOpen);
        Assert.AreEqual(DateRange.CreateUnboundedStart(otherDay), server.DatesUpTo);
        Assert.AreEqual(DateRange.CreateUnboundedEnd(day), server.DatesFrom);
        Assert.AreEqual(DateRange.Empty, server.Inverted);

        Assert.AreEqual(DecimalRange.CreateFinite(amount, other), server.Amounts);
        Assert.AreEqual(DecimalRange.CreateUnboundedStart(other), server.AmountsUpTo);
        Assert.AreEqual(TimeRange.CreateFinite(at, until), server.Hours);
        Assert.AreEqual(TimeRange.CreateUnboundedEnd(at), server.HoursFrom);
    }
}

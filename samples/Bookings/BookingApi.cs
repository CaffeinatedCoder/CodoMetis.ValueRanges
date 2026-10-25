using System.Diagnostics;
using CodoMetis.ValueRanges;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bookings;

public record BookingRequest(int RoomId, string Guest, DateRange Stay, DateTimeOffsetRange? Arrival);

public record BookingResult(int Id, int RoomId, string Guest, DateRange Stay, DateTimeOffsetRange? Arrival);

public record RoomResult(int Id, string Name, StringSet Features);

public record RoomSearch(DateRange Stay, StringSet Features, List<RoomResult> Rooms);

public record Availability(int RoomId, DateRange Window, RangeSet<DateRange, DateOnly> Booked, RangeSet<DateRange, DateOnly> Free);

public static class BookingApi
{
    public static void MapBookingApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/rooms", FindRooms);
        app.MapPost("/bookings", Book);
        app.MapGet("/bookings/{id:int}", GetBooking);
        app.MapGet("/rooms/{id:int}/availability", GetAvailability);
    }

    // GET /rooms?stay=[2026-07-01,2026-07-05)&features={sea-view,balcony}
    static async Task<IResult> FindRooms(DateRange stay, StringSet? features, BookingContext db)
    {
        if (CheckStay(stay) is { } problem)
            return Invalid("stay", problem);

        var required = features ?? StringSet.Empty;
        var rooms = await db.Rooms
            .Where(room => room.Features.IsSupersetOf(required))
            .Where(room => room.Bookable.Contains(stay))
            .Where(room => !db.Bookings.Any(booking => booking.RoomId == room.Id && booking.Period.Overlaps(stay)))
            .OrderBy(room => room.Id)
            .Select(room => new RoomResult(room.Id, room.Name, room.Features))
            .ToListAsync();

        return TypedResults.Ok(new RoomSearch(stay, required, rooms));
    }

    // POST /bookings with a JSON body
    static async Task<IResult> Book(BookingRequest request, BookingContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Guest))
            return Invalid("guest", "Name the guest.");
        if (CheckStay(request.Stay) is { } problem)
            return Invalid("stay", problem);

        var room = await db.Rooms.FindAsync(request.RoomId);
        if (room is null)
            return TypedResults.NotFound();
        if (!room.Bookable.Contains(request.Stay))
            return Invalid("stay", $"{room.Name} takes bookings for {room.Bookable} only.");

        var booking = new Booking
        {
            RoomId = request.RoomId,
            Guest = request.Guest,
            Period = request.Stay,
            Arrival = request.Arrival,
        };
        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            return TypedResults.Conflict(new { error = $"{room.Name} is already booked for some of those nights." });
        }

        return TypedResults.Created($"/bookings/{booking.Id}", ToResult(booking));
    }

    // GET /bookings/1
    static async Task<IResult> GetBooking(int id, BookingContext db)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(booking => booking.Id == id);
        return booking is null ? TypedResults.NotFound() : TypedResults.Ok(ToResult(booking));
    }

    // GET /rooms/1/availability?window=[2026-07-01,2026-07-31]
    static async Task<IResult> GetAvailability(int id, DateRange window, BookingContext db)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null)
            return TypedResults.NotFound();

        var booked = await db.Bookings
            .Where(booking => booking.RoomId == id && booking.Period.Overlaps(window))
            .GroupBy(booking => booking.RoomId)
            .Select(group => group.Select(booking => booking.Period).RangeAgg())
            .SingleOrDefaultAsync()
            ?? RangeSet<DateRange, DateOnly>.Empty;

        var open = RangeSet<DateRange, DateOnly>.From(room.Bookable.Intersect(window));
        var free = open.Except(booked);

        return TypedResults.Ok(new Availability(id, window, booked, free));
    }

    static string? CheckStay(DateRange stay) => stay switch
    {
        DateRange.Finite { Length: > 30 } => "A stay is at most 30 nights.",
        DateRange.Finite => null,
        DateRange.EmptyRange => "The stay has no nights.",
        DateRange.UnboundedStart or DateRange.UnboundedEnd or DateRange.Infinity => "The stay needs a first and a last night.",
        _ => throw new UnreachableException(),
    };

    static IResult Invalid(string field, string problem) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [problem] });

    static BookingResult ToResult(Booking booking) =>
        new(booking.Id, booking.RoomId, booking.Guest, booking.Period, booking.Arrival);
}

# Walkthrough: a booking service

This page follows one hotel booking from the HTTP request to the database and back. On the way you
bind a stay from a query string and from a JSON body, validate it, store it under a constraint that
rejects double bookings, filter rooms by their features, and work out which nights are still free.

The code is a small ASP.NET Core minimal API in [`samples/Bookings`](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/tree/main/samples/Bookings). CI builds
it against the packages on every pull request, runs it against PostgreSQL, and checks every response,
SQL statement and stored value on this page. The code blocks are quoted from the sample's source, and
a test fails when the two differ.

At each step, the page says what happens to the values there, or that nothing does. Four things can
happen at a boundary:

- **Canonical form.** A range or a set takes one fixed spelling when it is created.
- **Microseconds.** PostgreSQL keeps six fraction digits of a timestamp, and .NET has seven.
- **`infinity`.** PostgreSQL has `infinity` and `-infinity` as timestamp and date values.
- **The literal.** The text form of a range or a set, such as `[2026-07-01,2026-07-04]`.

## Run it yourself

You need Docker, and the .NET SDK for the framework `Bookings.csproj` targets:

```sh
docker run -d --name bookings-db -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16-alpine
until docker exec bookings-db pg_isready -h 127.0.0.1 -U postgres; do sleep 1; done
cd samples/Bookings
ConnectionStrings__Bookings="Host=localhost;Database=bookings;Username=postgres;Password=postgres" dotnet run
```

The app applies its migration, adds three rooms, and listens on `http://localhost:5000`. The
requests below use these rooms:

| Id | Name | Features | Takes bookings |
|---|---|---|---|
| 1 | Harbour | `balcony`, `sea-view` | from 2026-01-01, no end date |
| 2 | Garden | `garden`, `step-free` | 2026-05-01 to 2026-09-30 |
| 3 | Lighthouse | `balcony`, `bathtub`, `sea-view` | from 2026-01-01, no end date |

Good to know:

- **Start again from an empty database.** The bookings stay in the container between runs, so
  booking the same nights a second time returns 409. Remove the container with
  `docker rm -f bookings-db` and run the commands again.
- **The project references the newest release on NuGet.** So it restores and builds wherever you copy
  it. CI moves it to the version in the repository, and to that version's framework, before it runs
  the requests on this page. To do the same, clone the repository and run
  `./test/consumer-smoke-test.sh`.

## Model rooms and bookings

<!-- sample: samples/Bookings/BookingContext.cs -->
```csharp
public class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    // text[]: stored deduplicated and sorted, whatever order you add them in.
    public StringSet Features { get; set; } = StringSet.Empty;

    // daterange: the nights the room takes bookings. An open end means "until further notice".
    public DateRange Bookable { get; set; } = DateRange.Empty;
}

public class Booking
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public string Guest { get; set; } = "";

    // daterange: the nights of the stay. The exclusion constraint in the migration keeps two
    // bookings of one room from sharing a night.
    public DateRange Period { get; set; } = DateRange.Empty;

    // tstzrange: when the guest expects to arrive, if they said.
    public DateTimeOffsetRange? Arrival { get; set; }
}
```

The properties are ordinary properties, and you configure nothing for them. A stay is the set of
nights a guest sleeps in the room, so a `DateRange` fits it exactly. A guest who arrives on 1 July
and leaves on 5 July sleeps four nights, `[2026-07-01,2026-07-04]`.

`UseValueRanges()` turns on the mapping, in `Program.cs`:

<!-- sample: samples/Bookings/Program.cs -->
```csharp
builder.Services.AddDbContext<BookingContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Bookings"),
    npgsql => npgsql.UseValueRanges()));
```

## Reject double bookings in the database

Two requests can check the same room at the same moment, both find it free, and both insert. Only a
database constraint closes that gap. The sample adds an exclusion constraint in its migration, with
explicit SQL, because EF Core has no API for one:

<!-- sample: samples/Bookings/Migrations/20261025111834_InitialCreate.cs -->
```csharp
// Added by hand: EF Core has no API for an exclusion constraint. Two bookings of the same
// room whose periods share a night now fail with 23P01 (exclusion_violation).
migrationBuilder.Sql("""
    ALTER TABLE "Bookings" ADD CONSTRAINT "ex_bookings_room_period"
      EXCLUDE USING gist ("RoomId" WITH =, "Period" WITH &&);
    """);
```

The constraint compares `RoomId` with `=`, and a GiST index supports `=` on an integer only through
the `btree_gist` extension. The model declares the extension, so the same migration creates it:

<!-- sample: samples/Bookings/BookingContext.cs -->
```csharp
// The exclusion constraint compares RoomId with =, which GiST supports through btree_gist.
model.HasPostgresExtension("btree_gist");
```

The rest of the migration is what `dotnet ef migrations add InitialCreate` generated.
[Reject overlapping rows with an exclusion constraint](efcore.md#reject-overlapping-rows-with-an-exclusion-constraint)
covers the other ways to declare one.

## Search for a free room

The first request asks for a room with a sea view and a balcony, for the nights of 1 to 4 July:

```sh
curl -G http://localhost:5000/rooms --data-urlencode 'stay=[2026-07-01,2026-07-05)' --data-urlencode 'features={sea-view,balcony}'
```

A URL can't carry the brackets and braces as they are, so `--data-urlencode` escapes them. The
handler takes the stay and the features as typed parameters:

<!-- sample: samples/Bookings/BookingApi.cs -->
```csharp
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
```

The response:

```json
{"stay":"[2026-07-01,2026-07-04]","features":["balcony","sea-view"],"rooms":[{"id":1,"name":"Harbour","features":["balcony","sea-view"]},{"id":3,"name":"Lighthouse","features":["balcony","bathtub","sea-view"]}]}
```

The whole query runs in PostgreSQL, as one statement. This is the SQL EF Core logs:

```sql
SELECT r."Id", r."Name", r."Features"
FROM "Rooms" AS r
WHERE r."Features" @> @required AND r."Bookable" @> @stay AND NOT EXISTS (
    SELECT 1
    FROM "Bookings" AS b
    WHERE b."RoomId" = r."Id" AND b."Period" && @stay)
ORDER BY r."Id"
```

What happens at this boundary:

- **The stay takes its canonical form.** ASP.NET Core binds `stay` through `DateRange.TryParse`, and
  you register nothing for it. You sent `[2026-07-01,2026-07-05)`, and the response holds
  `[2026-07-01,2026-07-04]`. They are the same four nights. A date range always takes the closed
  form `[first, last]`.
- **The features come back sorted.** You sent `{sea-view,balcony}`, and the response holds
  `["balcony","sea-view"]`. A `StringSet` is always deduplicated and sorted, ordinally.
- **A range is a JSON string, and a set is a JSON array.** The string holds the range's literal. The
  types carry their own JSON converters, so the response needs no configuration either.
- **Nothing is truncated, and nothing becomes `infinity`.** Dates have no fraction digits, and no
  bound here is `DateOnly.MinValue` or `DateOnly.MaxValue`.

## Validate the stay

The handler checks the stay before it queries anything. A range comes in five kinds, called shapes,
and a switch over them says what is wrong:

<!-- sample: samples/Bookings/BookingApi.cs -->
```csharp
static string? CheckStay(DateRange stay) => stay switch
{
    DateRange.Finite { Length: > 30 } => "A stay is at most 30 nights.",
    DateRange.Finite => null,
    DateRange.EmptyRange => "The stay has no nights.",
    DateRange.UnboundedStart or DateRange.UnboundedEnd or DateRange.Infinity => "The stay needs a first and a last night.",
    _ => throw new UnreachableException(),
};
```

A stay with no last night, `[2026-08-01,)`, is a valid range, so it binds. The check rejects it:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"stay":["The stay needs a first and a last night."]}}
```

Text that isn't a range at all fails before your handler runs. A reversed stay such as
`[2026-07-10,2026-07-01)` doesn't parse. In the query string, `TryParse` returns `false`, and in a
JSON body, the converter throws. Either way the response is a 400 with an empty body.

Good to know:

- **The discard arm is there for the compiler.** The five shapes are the only ones that can exist,
  but C# can't prove it. [Pattern matching](ranges.md#pattern-matching) explains why the discard
  should throw.

## Book a room

The second request books the Harbour room for the same nights. It also says when the guest expects
to arrive, as a range of times with an offset:

```sh
curl http://localhost:5000/bookings -H 'Content-Type: application/json' -d '{"roomId":1,"guest":"Ada","stay":"[2026-07-01,2026-07-05)","arrival":"[2026-07-01T15:00:00.1234567+02:00,2026-07-01T18:00:00+02:00)"}'
```

The body binds to a record, through the converters on the range types:

<!-- sample: samples/Bookings/BookingApi.cs -->
```csharp
public record BookingRequest(int RoomId, string Guest, DateRange Stay, DateTimeOffsetRange? Arrival);
```

The handler checks that the request names a guest, checks the stay as above, and checks it against
the nights the room takes bookings. Then it saves the booking, and turns the constraint's error into
a 409:

<!-- sample: samples/Bookings/BookingApi.cs -->
```csharp
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
```

The response, 201 Created:

```json
{"id":1,"roomId":1,"guest":"Ada","stay":"[2026-07-01,2026-07-04]","arrival":"[2026-07-01T15:00:00.1234567+02:00,2026-07-01T18:00:00.0000000+02:00)"}
```

The arrival is what you sent: the same offset, and all seven fraction digits. This response is
built from the object in memory, which still holds the values from the request.

## Read the booking back

`curl http://localhost:5000/bookings/1` loads the row from PostgreSQL:

```json
{"id":1,"roomId":1,"guest":"Ada","stay":"[2026-07-01,2026-07-04]","arrival":"[2026-07-01T13:00:00.1234560+00:00,2026-07-01T16:00:00.0000000+00:00)"}
```

This is what PostgreSQL stores:

```sh
docker exec bookings-db psql -U postgres -d bookings -At \
  -c 'SELECT "Id", "Period", "Arrival" FROM "Bookings" ORDER BY "Id"' \
  -c 'SELECT "Id", "Features", "Bookable" FROM "Rooms" ORDER BY "Id"'
```

```text
1|[2026-07-01,2026-07-05)|["2026-07-01 13:00:00.123456+00","2026-07-01 16:00:00+00")
1|{balcony,sea-view}|[2026-01-01,)
2|{garden,step-free}|[2026-05-01,2026-10-01)
3|{balcony,bathtub,sea-view}|[2026-01-01,)
```

The first line is the booking, and the other three are the rooms.

What happens at this boundary:

- **The arrival is stored in UTC.** A `tstzrange` keeps the instant, not the offset. The arrival
  comes back as `13:00` at `+00:00`, the same instant as `15:00` at `+02:00`.
- **The seventh fraction digit is dropped.** PostgreSQL keeps microseconds, so `.1234567` is
  stored as `.123456`, and you read back `.1234560`. Read the booking back when you need what was
  stored, because the 201 response shows what you sent.
- **PostgreSQL spells the stay differently, and holds the same nights.** PostgreSQL stores a
  `daterange` half-open, `[2026-07-01,2026-07-05)`. You read it back as `[2026-07-01,2026-07-04]`.
- **An open end stays open.** The Harbour room takes bookings from 2026-01-01 with no end date, and
  PostgreSQL stores `[2026-01-01,)`, with no upper bound. It doesn't store `infinity`. Only a bound
  at the very end of its type, such as `DateOnly.MaxValue`, becomes `infinity`, and
  [Store timestamp ranges](efcore.md#store-timestamp-ranges) explains that case.
- **The features are stored sorted.** The seed code adds the Harbour room's features in another
  order, and the `text[]` column holds `{balcony,sea-view}`:

<!-- sample: samples/Bookings/BookingContext.cs -->
```csharp
Name = "Harbour",
Features = ["sea-view", "balcony"],
```

## Try to book the same nights twice

A second guest asks for the Harbour room for the nights of 4 and 5 July:

```sh
curl http://localhost:5000/bookings -H 'Content-Type: application/json' -d '{"roomId":1,"guest":"Grace","stay":"[2026-07-04,2026-07-06)"}'
```

The night of 4 July is already booked. PostgreSQL rejects the insert with `23P01`, and the handler
returns 409 Conflict:

```json
{"error":"Harbour is already booked for some of those nights."}
```

The same guest then asks for the nights of 5 to 7 July, `[2026-07-05,2026-07-08)`. That stay starts
the night after Ada's ends, so the two are adjacent and don't overlap. PostgreSQL accepts it:

```json
{"id":3,"roomId":1,"guest":"Grace","stay":"[2026-07-05,2026-07-07]","arrival":null}
```

The first search, sent again, now finds only the Lighthouse room:

```json
{"stay":"[2026-07-01,2026-07-04]","features":["balcony","sea-view"],"rooms":[{"id":3,"name":"Lighthouse","features":["balcony","bathtub","sea-view"]}]}
```

Good to know:

- **The rejected insert used up id 2.** PostgreSQL takes the next identity value before it checks
  the constraint, and doesn't give it back.

## Find the free nights

The last endpoint answers "which nights in July are still free in the Harbour room?":

```sh
curl -G http://localhost:5000/rooms/1/availability --data-urlencode 'window=[2026-07-01,2026-07-31]'
```

<!-- sample: samples/Bookings/BookingApi.cs -->
```csharp
var booked = await db.Bookings
    .Where(booking => booking.RoomId == id && booking.Period.Overlaps(window))
    .GroupBy(booking => booking.RoomId)
    .Select(group => group.Select(booking => booking.Period).RangeAgg())
    .SingleOrDefaultAsync()
    ?? RangeSet<DateRange, DateOnly>.Empty;

var open = RangeSet<DateRange, DateOnly>.From(room.Bookable.Intersect(window));
var free = open.Except(booked);
```

The response:

```json
{"roomId":1,"window":"[2026-07-01,2026-07-31]","booked":"{[2026-07-01,2026-07-07]}","free":"{[2026-07-08,2026-07-31]}"}
```

`RangeAgg` runs in PostgreSQL as `range_agg`, and you get the room's bookings back as one
`RangeSet`:

```sql
SELECT range_agg(b."Period")
FROM "Bookings" AS b
WHERE b."RoomId" = @id AND b."Period" && @window
GROUP BY b."RoomId"
LIMIT 2
```

`Except` then runs in memory. It removes the booked nights from the nights the room is open in the
window, and leaves the free ones.

What happens at this boundary:

- **Adjacent bookings merge.** Ada's stay and Grace's stay touch, so the `RangeSet` holds them as
  one range, `[2026-07-01,2026-07-07]`. A `RangeSet` never holds two ranges that overlap or touch,
  and PostgreSQL's multiranges follow the same rule.
- **A `RangeSet` is a JSON string too.** It holds the multirange literal, `{[2026-07-08,2026-07-31]}`.
- **No bookings is an empty set, not `null`.** With no rows there is no group, so the query returns
  `null`, and the handler uses `RangeSet<DateRange, DateOnly>.Empty`. The Lighthouse room's July
  is `"booked":"{}"` and `"free":"{[2026-07-01,2026-07-31]}"`.

An open window gives an open answer. Ask from 28 June with no end date, `[2026-06-28,)`:

```json
{"roomId":1,"window":"[2026-06-28,)","booked":"{[2026-07-01,2026-07-07]}","free":"{[2026-06-28,2026-06-30],[2026-07-08,)}"}
```

The room is free from 8 July with no end date, because it takes bookings with no end date.

## Where to go next

| If you want | Read |
|---|---|
| Every operation on ranges and range sets | [Ranges and range sets](ranges.md) |
| Value sets and their canonical form | [Value sets](value-sets.md) |
| Every translation, and what runs in memory | [Entity Framework Core](efcore.md) |
| The literals, binding and JSON in detail | [Literals, parsing, and JSON](serialization.md) |

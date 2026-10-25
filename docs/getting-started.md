# Getting started

This page takes you from an empty project to a first query that runs in PostgreSQL. Part 1 needs
only the core package and runs entirely in memory. Part 2 adds PostgreSQL. Together they take about
five minutes.

If you'd like to see the reasoning first, [Why this exists](why.md) covers the design. This page
assumes you have decided to try the library.

## Part 1 — Try it in memory, no database (2 minutes)

```sh
dotnet new console -o RangeDemo && cd RangeDemo
dotnet add package CodoMetis.ValueRanges
```

The core question in a booking system is whether two stays collide. You answer it with one call:

```csharp
using CodoMetis.ValueRanges;

var june = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 8));
var july = DateRange.CreateFinite(new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 5));

Console.WriteLine(june.Overlaps(july));                  // False
Console.WriteLine(june.Contains(new DateOnly(2025, 6, 3)));  // True
Console.WriteLine(june);                                 // [2025-06-01,2025-06-08]
```

The next three steps show what the types are for.

### Combine stays that leave a gap

A gap is a value, not an error. Call `Union` on two stays that don't overlap, and you get a
`RangeSet` back. A `RangeSet` is a multirange in memory, and it supports the same operations as a
single range:

```csharp
var blocked = june.Union(july);        // {[2025-06-01,2025-06-08],[2025-07-01,2025-07-05]}

blocked.Count;                              // 2
blocked.Contains(new DateOnly(2025, 6, 20));    // False — falls in the gap
blocked.Complement();                       // everything else, as a RangeSet
```

To find the free days between the stays, ask for the gaps. `Gap` gives you the range between two
stays, and `Gaps` gives you every free stretch inside a set or inside a window:

```csharp
june.Gap(july);                             // [2025-06-09,2025-06-30]
blocked.Gaps();                             // {[2025-06-09,2025-06-30]}

var summer = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 8, 31));
blocked.Gaps(summer);                       // {[2025-06-09,2025-06-30],[2025-07-06,2025-08-31]}
```

### Leave the end date open

"No end date" is a shape, not a null. A range comes in five kinds, called shapes: `Finite`,
`UnboundedStart`, `UnboundedEnd`, `EmptyRange` and `Infinity`. An open-ended stay is an
`UnboundedEnd`. It has no `End` property to read, so you have no nullable to guard and no sentinel
value to misread:

```csharp
DateRange openEnded = DateRange.CreateUnboundedEnd(new DateOnly(2025, 6, 1));   // [2025-06-01,)

// The five shapes are the only ones that can exist, so these arms are complete in fact.
// C# still cannot prove that, so a switch expression needs a discard — make it throw.
var description = openEnded switch
{
    DateRange.Finite f          => $"{f.Start} to {f.End}",
    DateRange.UnboundedEnd u    => $"from {u.Start}, open-ended",
    DateRange.UnboundedStart u  => $"until {u.End}",
    DateRange.EmptyRange        => "no dates",
    DateRange.Infinity          => "always",
    _                           => throw new UnreachableException(),   // System.Diagnostics
};
```

Don't skip the discard arm. Without it, the switch warns `CS8509`, and that warning is a build
**error** in any project that treats warnings as errors. [Pattern matching](ranges.md#pattern-matching)
explains why C# can't see that the five arms are complete, and why the discard should throw instead
of returning a value.

### Work with sets of values

Value sets bring the same idea to plain values such as strings. Every way you create a set gives you
the same canonical form: deduplicated and sorted. The set is also immutable:

```csharp
var tags = StringSet.From("beta", "alpha", "beta");   // {alpha,beta}
StringSet required = ["alpha", "gamma"];              // collection expressions work

tags.IsSubsetOf(required);   // False
tags.Overlaps(required);     // True — they share "alpha"
```

That is the whole library, running in your process. No database has been involved so far, and you
don't need one. If the in-memory operations are all you need, you are done.
[Ranges and range sets](ranges.md) and [Value sets](value-sets.md) are the complete references.

## Part 2 — Store and query it in PostgreSQL (3 minutes)

The EF Core package maps these types to PostgreSQL's native `daterange`, `datemultirange` and
`text[]` columns. Your method calls then run in the database, as the matching PostgreSQL operators.

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design
```

The EF Core package brings `Npgsql.EntityFrameworkCore.PostgreSQL` and the core package with it.

### Add range and set properties to an entity

Range and set properties are ordinary properties. You don't configure value converters, comparers
or column types:

```csharp
using CodoMetis.ValueRanges;

public class Booking
{
    public int Id { get; set; }
    public int RoomId { get; set; }

    public DateRange Period { get; set; } = DateRange.Empty;

    public RangeSet<DateRange, DateOnly> BlockedDays { get; set; }
        = RangeSet<DateRange, DateOnly>.Empty;

    public StringSet Tags { get; set; } = StringSet.Empty;
}
```

### Turn on the mapping in your DbContext

One line, `UseValueRanges()`, turns on the whole mapping:

```csharp
using Microsoft.EntityFrameworkCore;

public class BookingContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseNpgsql(
            "Host=localhost;Database=bookings;Username=postgres;Password=postgres",
            npgsql => npgsql.UseValueRanges());
}
```

### Create the database

```sh
dotnet ef migrations add InitialCreate
dotnet ef database update
```

The mapping works by convention. The properties above get these column types with no further
configuration:

| Property | Column type |
|---|---|
| `Period` | `daterange` |
| `BlockedDays` | `datemultirange` |
| `Tags` | `text[]` |

[Entity Framework Core](efcore.md) has the full table for every type.

Good to know before you model with these types:

- **`TimeRange` needs a custom PostgreSQL type.** Add `modelBuilder.HasPostgresRange("timerange", "time")`
  to your model, and call `EnableUnmappedTypes` on the data source. See
  [TimeRange and the custom timerange type](efcore.md#timerange-and-the-custom-timerange-type).
- **The NodaTime types need `UseValueRangesNodaTime()`.** Call it instead of `UseValueRanges()`.

### Query with the same methods

Here are the calls from part 1 again, now running as SQL:

```csharp
var day = new DateOnly(2025, 6, 3);
var request = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 8));

// WHERE b."Period" @> @day
var onThatDay = context.Bookings.Where(b => b.Period.Contains(day)).ToList();

// WHERE b."Period" && @request
var colliding = context.Bookings.Where(b => b.Period.Overlaps(request)).ToList();

// WHERE b."Tags" @> ARRAY[@tag]::text[]        — a plain GIN index serves this
var tagged = context.Bookings.Where(b => b.Tags.Contains("vip")).ToList();

// ORDER BY lower(b."Period"); a booking with no start, or an empty period, sorts last
var chronological = context.Bookings.OrderBy(b => b.Period.LowerBound()).ToList();
```

Call `ToQueryString()` on any of these queries to print the SQL and check it yourself. The
translation tests assert on exactly that output.

### Prevent double bookings

Plan for this early, because your application code can't do it. Checking for an overlap and then
inserting is a race between a read and a write. Under concurrency, two requests can both find the
slot free. Only a database constraint is atomic.

In PostgreSQL, that constraint is an exclusion constraint, or on PostgreSQL 18 a temporal key.
[Indexing a range column, and preventing overlaps](efcore.md#indexing-a-range-column-and-preventing-overlaps)
shows how to declare either on a column this package maps.

## What runs in memory only

Most operations run in the database. A few run in memory only, such as `Intersect`, `Except` and
`Add` on *value sets*. [What runs where](efcore.md#what-runs-where) lists them all and says why
for each.

Some queries over a `Union` of sets that the database computes throw instead of running as SQL,
because the database would return a wrong result. An equality check on such a union is one.
[Combine sets with `Union`](efcore.md#combine-sets-with-union) lists the rest.

When an in-memory-only operation appears in a `Where`, EF Core **throws**. It does not quietly
fetch the whole table. This is intended, because a loud failure is the right outcome. Call
`AsEnumerable()` when you want the in-memory answer on purpose.
[What runs where](efcore.md#what-runs-where) has the complete list, and it is short.

## Where to go next

| If you want | Read |
|---|---|
| The complete in-memory algebra | [Ranges and range sets](ranges.md) |
| Canonical scalar sets and validated wrapper elements | [Value sets](value-sets.md) |
| Every translation, and what runs where | [Entity Framework Core](efcore.md) |
| PostgreSQL literals, parsing, System.Text.Json | [Literals, parsing, and JSON](serialization.md) |
| Why the model is shaped this way | [Why this exists](why.md) |
| Upgrading from an earlier major | [Migration guide](migration.md) |

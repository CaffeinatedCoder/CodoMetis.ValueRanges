# Getting started

From an empty project to a first translated query. Part 1 needs nothing but the core package and
runs entirely in memory; part 2 adds PostgreSQL. Together they take about five minutes.

If you would rather see the reasoning first, [Why this exists](why.md) covers the design; this page
assumes you have decided to try it.

## Part 1 — in memory, no database (2 minutes)

```sh
dotnet new console -o RangeDemo && cd RangeDemo
dotnet add package CodoMetis.ValueRanges
```

A booking system's core question is whether two stays collide. That is one call:

```csharp
using CodoMetis.ValueRanges;

var june = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 8));
var july = DateRange.CreateFinite(new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 5));

Console.WriteLine(june.Overlaps(july));                  // False
Console.WriteLine(june.Contains(new DateOnly(2025, 6, 3)));  // True
Console.WriteLine(june);                                 // [2025-06-01,2025-06-08]
```

Three things are worth noticing, because they are what the type is for.

**A gap is a value, not an error.** Union two disjoint stays and you get a `RangeSet` — a multirange
in memory, with the same algebra as a single range:

```csharp
var blocked = june.Union(july);        // {[2025-06-01,2025-06-08],[2025-07-01,2025-07-05]}

blocked.Count;                              // 2
blocked.Contains(new DateOnly(2025, 6, 20));    // False — falls in the gap
blocked.Complement();                       // everything else, as a RangeSet
```

**"No end date" is a shape, not a null.** An open-ended stay has no `End` property to read, so there
is no nullable to guard and no sentinel to misread:

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

That discard is not a formality worth skipping: without it the switch warns `CS8509`, which is a
build **error** in any project that treats warnings as errors. [Pattern
matching](ranges.md#pattern-matching) explains why C# cannot close the gap itself, and why the arm
should throw rather than return a value.

**Sets are the same idea for scalars.** Canonical on every path — deduplicated, sorted, immutable:

```csharp
var tags = StringSet.From("beta", "alpha", "beta");   // {alpha,beta}
StringSet required = ["alpha", "gamma"];              // collection expressions work

tags.IsSubsetOf(required);   // False
tags.Overlaps(required);     // True — they share "alpha"
```

That is the whole library, in process. No database has been involved so far, and none is required —
if you only need the algebra, you are done. [Ranges and range sets](ranges.md) and
[Value sets](value-sets.md) are the complete references.

## Part 2 — persist and query it in PostgreSQL (3 minutes)

The companion package maps these types to PostgreSQL's native `daterange`, `datemultirange` and
`text[]` columns and translates the same method calls into the same operators, server-side.

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design
```

The EF package brings `Npgsql.EntityFrameworkCore.PostgreSQL` and the core package with it.

### The entity

Range and set properties are ordinary properties. There are no value converters, comparers or column
types to configure:

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

### The context

One line enables the whole mapping:

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

### The migration

```sh
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Mapping is by convention, so the properties above land in these columns with no further
configuration:

| Property | Column type |
|---|---|
| `Period` | `daterange` |
| `BlockedDays` | `datemultirange` |
| `Tags` | `text[]` |

[Entity Framework Core](efcore.md) has the full table for every type. Two need one extra line, and
it is worth knowing which before you model with them: `TimeRange` needs a custom PostgreSQL type
(`modelBuilder.HasPostgresRange("timerange", "time")` plus `EnableUnmappedTypes` on the data source),
and the NodaTime types need `UseValueRangesNodaTime()` instead of `UseValueRanges()`.

### The query

The same calls from part 1, now as SQL:

```csharp
var day = new DateOnly(2025, 6, 3);
var request = DateRange.CreateFinite(new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 8));

// WHERE b."Period" @> @day
var onThatDay = context.Bookings.Where(b => b.Period.Contains(day)).ToList();

// WHERE b."Period" && @request
var colliding = context.Bookings.Where(b => b.Period.Overlaps(request)).ToList();

// WHERE b."Tags" @> ARRAY[@tag]::text[]        — a plain GIN index serves this
var tagged = context.Bookings.Where(b => b.Tags.Contains("vip")).ToList();

// ORDER BY lower(b."Period")
var chronological = context.Bookings.OrderBy(b => b.Period.LowerBound()).ToList();
```

`ToQueryString()` on any of those prints the SQL if you want to confirm it yourself — that is
exactly what the translation test suite asserts on.

### Stopping double-bookings

Worth flagging early, because application code cannot do it: checking for an overlap and then
inserting is a read-then-write race, so under concurrency two requests can both find the slot free.
Only a database constraint is atomic. PostgreSQL's answer is an exclusion constraint, and
[Indexing a range column, and preventing overlaps](efcore.md#indexing-a-range-column-and-preventing-overlaps)
shows how to declare one over a column mapped by this package.

## The one thing to know about boundaries

Most of the algebra translates. A few operations are client-side only — `Intersect`, `Except` and
`Add` on *value sets*, because PostgreSQL's array type has no intersection, difference or sorted
insert — and one composition is refused outright rather than translated, because it would answer
wrongly (equality over a server-computed set `Union`).

When a client-side operation appears in a `Where`, EF **throws** rather than silently fetching the
table. That is intended: a loud failure is the correct outcome, and `AsEnumerable()` is how you ask
for the in-memory answer deliberately. [What runs where](efcore.md#what-runs-where) is the exhaustive
list, and it is short.

## Where to go next

| If you want | Read |
|---|---|
| The complete in-memory algebra | [Ranges and range sets](ranges.md) |
| Canonical scalar sets and validated wrapper elements | [Value sets](value-sets.md) |
| Every translation, and what runs where | [Entity Framework Core](efcore.md) |
| PostgreSQL literals, parsing, System.Text.Json | [Literals, parsing, and JSON](serialization.md) |
| Why the model is shaped this way | [Why this exists](why.md) |
| Upgrading from an earlier major | [Migration guide](migration.md) |

# Why this exists

This page explains why the library is shaped the way it is, what else exists, and why some types are
deliberately left out. You don't need any of it to use the package. The [README](../README.md)
installs it, and [Getting started](getting-started.md) walks you through a first query. Read on if
you are deciding whether the model fits your project, or wondering why a boundary sits where it does.

## Why not use what .NET already has?

Every piece of this problem already has a solution in .NET. What's missing is a single type that
holds all of them at once.

| What you would reach for today | Where it stops |
|---|---|
| Two properties (`From`, `To`) | No algebra, no empty or unbounded case; "no end date" becomes a nullable that every reader reinterprets |
| [`NpgsqlRange<T>`](https://www.npgsql.org/doc/api/NpgsqlTypes.NpgsqlRange-1.html) | Declared in `NpgsqlTypes`, in `Npgsql.dll` — a domain model that uses it references the database driver. The struct carries no algebra of its own ([and reconciles unboundedness at runtime](#unboundedness-is-a-shape-not-a-bound-value)) |
| NodaTime `Interval` / `DateInterval` with the [Npgsql NodaTime plugin](https://www.npgsql.org/efcore/mapping/nodatime.html) | Two date/time shapes only, and `DateInterval` is always closed and always bounded — no half-open, no unbounded, no empty |
| [FRange](https://www.nuget.org/packages/FRange/), [Open.Range](https://www.nuget.org/packages/Open.Range) | In-memory only — no PostgreSQL mapping, no SQL translation, no range literals. Neither has a discrete domain, so `[1,10)` and `[1,9]` stay different values and integer or date adjacency cannot be decided |
| `T[]` / `List<T>` with EF Core primitive collections | Mutable references, so the domain cannot defend an invariant it has already returned; and a list, not a set — order and multiplicity are part of the value |

This package joins the two halves. You get immutable domain types that carry the complete algebra
in memory, with no database dependency. The EF Core package runs the same operations in PostgreSQL,
using PostgreSQL's own operators. That agreement is [checked against a live
server](../README.md#verified-against-postgresql), not assumed.

*Landscape surveyed August 2026. If something here is out of date, or a comparable library was
missed, please [open an issue](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues).
The claim is meant to be falsifiable.*

### Why not `NpgsqlRange<T>`?

**The SQL translation is not what's missing.** Npgsql already translates the full set of range
operators, and this package does not improve on that. What's missing is a domain type to hang them
on.

`NpgsqlRange<T>`'s operations are EF Core extension methods. Each one is documented as *"only
intended for use via SQL translation as part of an EF Core LINQ query"*. Call one from a unit test
or a domain service and it throws. So the algebra exists only inside a query, on a type that exists
only inside the driver. A domain model that wants both gets neither.

### Why not an in-memory range library?

The in-memory libraries make the opposite trade. They have the algebra but no database side. Both
also leave unboundedness as something you check at runtime.

[FRange](https://www.nuget.org/packages/FRange/) comes closest. It has unbounded bounds and
multiranges. But in its C# API, reading `LowerBoundValue` on an unbounded range throws
(`failwith "No bound"`). You are expected to remember to call `HasLowerBound` first. That is the
question this library [doesn't let you ask](#unboundedness-is-a-shape-not-a-bound-value):
`UnboundedStart` has no `Start` property to throw from.

### Why not an array or a list?

**On the set side, there is no equivalent at all.** Today, storing a PostgreSQL array means
exposing a `T[]` or a `List<T>`. Both are *mutable* references:

- **Any code can rewrite an element after load.** Your domain model cannot keep a rule on a value
  it has already handed out.
- **Order and duplicates are part of the value.** Keeping it deduplicated and sorted is left to
  every writer to remember.
- **Nothing says "set".** A set of tags, permissions or IDs has no type that says so.

The value sets here are immutable end to end. Every way you create one deduplicates and sorts it:
`From`, parsing, JSON, and loading it from the database. No member can change it afterwards.

## Unboundedness is a shape, not a bound value

Each range has one of five shapes (`Finite`, `UnboundedStart`, `UnboundedEnd`, …), and the shape
is part of its type. That keeps two different facts apart: *"there is no upper bound"* and *"the
upper bound is the largest value the type can hold"*. A representation built from bounds plus flags
stores both facts in the same object.

Picture a range built from two nullable bounds plus an `IsUpperInfinite` bit. Both facts share the
same fields, so something has to reconcile them at runtime. `NpgsqlRange<T>` does it by discarding
one. Pass an upper bound together with `upperBoundInfinite: true`, and the constructor keeps the
flag and silently drops the value.

That is a sound rule, but a constructor enforces it, not the type. It also leaves `LowerBound` and
`UpperBound` typed `T?` on *every* instance. Even code that has already checked that the range is
bounded still has a nullable to deal with.

In this library you can't ask the question in the first place:

- **`UnboundedEnd` has no `End` property** to put a sentinel value in.
- **`Finite` has no flag** that could disown its `End`, and its `Start` and `End` are not nullable.

The type carries the distinction, not a constructor rule you have to know about:

```csharp
DateTimeRange.CreateUnboundedEnd(start)                  // UnboundedEnd — genuinely open-ended
DateTimeRange.CreateFinite(start, DateTime.MaxValue)     // Finite — ends at a specific instant
```

The two are not interchangeable, and the compiler won't let you confuse them.

This matters at the database boundary too. Npgsql maps `DateTime.MaxValue` to PostgreSQL
`infinity`. That is a *finite bound that happens to be infinite*, and it stays distinct from an
unbounded side. See [Entity Framework Core](efcore.md) for how it round-trips.

## Why these element types

The list of element types is chosen on purpose. Interval algebra needs two things from a type:

- **A total order that the type's own comparisons agree with.**
- **A defined step between neighbouring values, for a discrete type**, so adjacency is exact.

The first six element types meet that bar, and they are the six PostgreSQL ships as built-in
range types. `int`, `long` and `DateOnly` are discrete. `decimal`, `DateTime` and
`DateTimeOffset` are continuous, so adjacency for them depends on whether the bounds include their
end point. `TimeOnly` (and, in the NodaTime package, `YearMonth`) meet the same bar and were added in
v5. `double` and `float` meet it in 10.0, once `NaN` and the infinities are left out.

### Why doubles are finite only

`Float64Range` and `Float64Set` hold every `double` except `NaN`, `+∞` and `-∞`. Creating, parsing
or loading one with those values throws. `Float32Range` and `Float32Set` do the same for `float`.
Here is why, measured on PostgreSQL 16, 17 and 18:

| Literal | PostgreSQL | .NET, comparing with `CompareTo` |
|---|---|---|
| `[5,NaN)` | a range that contains `1e308` and `Infinity` | inverted, so empty |
| `[NaN,5)` | an error: lower bound above upper | a valid range |
| `[0,)` and the value `NaN` | contains it | doesn't contain it |
| `[1,Infinity)` and `[1,)` | two different ranges | two different ranges |

**`NaN` is the one value the two sides order differently.** .NET sorts it below every number, and
PostgreSQL sorts it above `Infinity`. No comparison involving `NaN` could give the same answer in
memory and in the database, so the library throws instead of answering.

**The infinities only repeat what an unbounded side already says.** `[1,Infinity)` is a different
value from `[1,)` on the server, but it holds the same numbers. Use `CreateUnboundedEnd`,
`CreateUnboundedStart` or `Infinite` for an open side.

Without those three values, `CompareTo`, the `<` and `>` operators and PostgreSQL agree on every
pair, including `-0` and `0`, which are equal on both sides.

[Intervals.NET](https://www.nuget.org/packages/Intervals.NET) accepts `double` ranges too. It
compares through `Comparer<T>.Default`, which is .NET's order with `NaN` first, and makes no promise
about PostgreSQL.

### Why there is no `Half` range

PostgreSQL has no two-byte float, so there is no column type for a `Half` range or set to mirror. A
`real` column holds values that a `Half` can't, which would round or fail when loaded. It's the same
reason there is an `Int16Set` (`smallint[]`) but no `Int16Range`: PostgreSQL has no `int2range`.

### Why not `Guid`?

`Guid` is left out for a different reason. Version 7 GUIDs are ordered, so the algebra would be
well-defined. But "every GUID between these two" is not a question that means anything in a real
domain.

### Why not `ZonedDateTime` or `OffsetDateTime`?

The NodaTime package leaves these out for a reason close to `NaN`'s. NodaTime deliberately gives
them no default ordering, because ordering by instant and ordering by local time disagree. The
`IComparable<T>` constraint therefore rejects them at compile time. The [NodaTime package's
README](../src/CodoMetis.ValueRanges.NodaTime/README.md#why-these-element-types) has the full reasoning, and the conversions to and from NodaTime's `Interval` and `DateInterval`.

## Where is the model stricter than the database?

**A `DecimalRange` bound can't be `NaN`.** PostgreSQL's `numeric` has a `NaN` value, which it sorts
above all other values by definition. So a `numrange` bound can be `NaN`. .NET's `decimal` has no
such value, so a `DecimalRange` can't hold one. The case that `numrange` has to define away never
comes up.

**A float range bound or set element can't be `NaN` or an infinity.** PostgreSQL's `double
precision` and `real` hold both, so a `float8range` column can store `[5,NaN)` and a `double
precision[]` column can store `{1.5,NaN}`, for example when another tool writes them. Loading such a
row throws (`ArgumentException`) instead of returning a value that would sort differently in memory.
A `NaN` or infinite query parameter throws in the same way, before the query runs.
A query that runs in the database over such a row still answers in PostgreSQL's order: `[0,NaN)`
matches `Contains(1e308)` there. A CHECK constraint keeps these values out of the column ([Keep NaN
and infinity out of a float column](efcore.md#keep-nan-and-infinity-out-of-a-float-column)).

**The NodaTime types avoid the two timestamp caveats.** `DateTimeRange` and `DateTimeOffsetRange`
carry two [timestamp caveats](efcore.md) at the database boundary. The NodaTime types don't, for a
related reason:

- **`LocalDateTime` is wall-clock time by construction.** There is no `Kind` to reinterpret.
- **`Instant` is an instant by construction.** There is no offset to normalize away.

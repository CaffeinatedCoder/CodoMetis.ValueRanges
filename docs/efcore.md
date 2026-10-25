# Entity Framework Core (PostgreSQL)

With the EF Core package you store ranges, range sets and value sets in native PostgreSQL columns,
and your LINQ queries over them run as SQL. A query gives the same answer in the database as the
same code gives in memory.

```bash
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL
```

## Map ranges to PostgreSQL columns

Turn the package on with one line:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());
```

You don't configure value converters, comparers or column types. Every range type maps to its
PostgreSQL range column, and every `RangeSet<TRange, T>` maps to the matching multirange column,
by convention:

| Property type                    | Column type      |
|----------------------------------|------------------|
| `Int32Range`                     | `int4range`      |
| `RangeSet<Int32Range, int>`      | `int4multirange` |
| `DateRange`                      | `daterange`      |
| `RangeSet<DateRange, DateOnly>`  | `datemultirange` |
| `TimeRange`                      | `timerange` ([custom type](#custom-range-types)) |
| `Float64Range`                   | `float8range` ([custom type](#custom-range-types)) |
| `Float32Range`                   | `float4range` ([custom type](#custom-range-types)) |
| … and so on for all types        |                  |

Good to know:

- **The package bridges to Npgsql through `NpgsqlRange<T>`.** The conversion happens at the
  provider boundary, and your properties keep the range types.
- **Scaffolding gives you `NpgsqlRange<T>`.** `dotnet ef dbcontext scaffold` maps range columns to
  `NpgsqlRange<T>`, not to these types, because the package provides no design-time services.
  Change the property types by hand after scaffolding.
- **A property's type picks its mapping, before any column type.** `HasColumnType("int4range")` on
  an `NpgsqlRange<int>` property keeps Npgsql's own mapping. On an `Int32Range` property, the column
  type of another range, such as `int8range`, fails when EF Core builds the model
  (`InvalidOperationException`).
- **A range or multirange column type on its own maps to these types.** Ask EF Core's
  `IRelationalTypeMappingSource` for `FindMapping("int4range")` with no property type, and you get
  the `Int32Range` mapping, or `RangeSet<Int32Range, int>` for `int4multirange`. With
  `UseValueRangesNodaTime`, which turns on Npgsql's NodaTime plugin, that plugin takes `daterange`,
  `tsrange` and `tstzrange` first (`DateInterval`, `NpgsqlRange<LocalDateTime>`, `Interval`). The
  multiranges still map to the core types.
- **An array column type on its own stays Npgsql's.** `text[]` is also the column type of every
  `string[]` and `List<string>` property, so a value set maps only from a property typed as one.

## Query ranges with LINQ

The full range algebra runs as SQL:

```csharp
var day = new DateOnly(2024, 6, 15);

// b."Period" @> @day
bookings.Where(b => b.Period.Contains(day));

// b."Period" && b."Blocked", b."Period" << @other, b."Period" -|- @other, ...
bookings.Where(b => b.Period.Overlaps(other));

// b."Period" * @other                                   (intersection)
bookings.Select(b => b.Period.Intersect(other));

// datemultirange(b."Period") + datemultirange(@other)   (union -> multirange)
bookings.Select(b => b.Period.Union(other));

// b."BlockedDays" @> @day, multirange + - * operators, complement, ...
bookings.Where(b => b.BlockedDays.Contains(day));
bookings.Select(b => b.BlockedDays | b.Period);

// CASE WHEN b."From" <= b."To" THEN daterange(b."From", b."To", '[]') ELSE 'empty' END
bookings.Where(b => DateRange.CreateFinite(b.From, b.To).Contains(day));
```

Each method becomes one PostgreSQL operator or function call:

- **`Contains`, `Overlaps` and `IsContainedBy`** become `@>`, `&&` and `<@`.
- **`IsStrictlyLeftOf`/`RightOf`** become `<<` and `>>`.
- **`DoesNotExtendLeftOf`/`RightOf`** become `&<` and `&>`.
- **`IsAdjacentTo`** becomes `-|-`.
- **`CompareTo` against 0** becomes `<`, `<=`, `>`, `>=` or `=`, for a range and a multirange, in the
  order `OrderBy` sorts by ([Sort ranges](ranges.md#sort-ranges)).
- **`Intersect`** becomes `*`.
- **`Union` and `Except`** turn both ranges into multiranges first, then use `+` and `-`. That
  matches their `RangeSet` return type. A union of two ranges that don't touch is a real
  two-element multirange, never an error.
- **`CreateFinite`, `CreateUnboundedStart` and `CreateUnboundedEnd`** become range constructor
  calls inside a guard. As in memory, a start after the end gives you an empty range. Over a NULL
  element, such as a column of a missing child in an outer join, the range is NULL, as EF Core's own
  `AddDays` is.

Since v4 the eight predicates above (`Contains` through `IsAdjacentTo`) also work on a `RangeSet`,
with either a range or a multirange on the other side. Since 10.0.0 they also work on a range with a
multirange on the other side, as the operator with the range on the left
(`b."Period" &< b."Blocked"`), and `RangeSet.IsContainedBy` runs as `<@`.

On a nullable column, a predicate over a NULL value is `false`, and its negation `true`. That is what
EF Core does for a `string` or `string[]` column, and what the methods return in memory for a `null`
range. So `!b.Period.Overlaps(other)` keeps the rows whose `Period` is NULL, as
`!b.Name.StartsWith("a")` keeps the rows whose `Name` is NULL. The SQL adds
`b."Period" IS NULL OR NOT (…)` for a nullable column and nothing for a required one. Value set
predicates follow the same rule in a query, though in memory they throw for a `null` set. So does
`Contains(day)` when `day` is NULL, as it is for a column of a missing child in an outer join or a
nullable column read with `.Value`.
`CompareTo` keeps its own rule for a NULL column ([Sort ranges](ranges.md#sort-ranges)).

### Read bounds, merge ranges and aggregate

Since v4 you can also read bounds, merge ranges, aggregate per group and compare multiranges:

```csharp
// ORDER BY lower(b."Period") — bound accessors: lower / upper / lower_inc / upper_inc
// A row with no lower bound, or an empty one, sorts last (see "Good to know" below)
bookings.OrderBy(b => b.Period.LowerBound());

// range_merge(b."Period", @other) and range_merge(b."BlockedDays")
bookings.Select(b => b.Period.Merge(other));
bookings.Select(b => b.BlockedDays.Merge());

// range_merge((datemultirange(range_merge(b."Period", @other)) - datemultirange(b."Period"))
//             - datemultirange(@other))                          (the gap between them)
bookings.Where(b => !b.Period.Gap(other).IsEmpty());

// datemultirange(range_merge(b."BlockedDays")) - b."BlockedDays"   and
// datemultirange(@month) - b."BlockedDays"                         (the free slots)
bookings.Select(b => b.BlockedDays.Gaps());
bookings.Select(b => b.BlockedDays.Gaps(month));

// range_agg(b."Period") / range_intersect_agg(b."Period") per group
bookings.GroupBy(b => b.CustomerId)
        .Select(g => g.Select(b => b.Period).RangeAgg());

// range_agg(b."BlockedDays") / range_intersect_agg(b."BlockedDays") over a multirange column
bookings.GroupBy(b => b.CustomerId)
        .Select(g => g.Select(b => b.BlockedDays).RangeIntersectAgg());

// isempty / lower_inf / upper_inf on multirange columns
bookings.Where(b => !b.BlockedDays.IsEmpty());

// Value equality on multirange columns — b."BlockedDays" = @set
bookings.Where(b => b.BlockedDays == someSet);
```

Merging ranges and finding gaps run as functions over multiranges:

- **`Merge`** becomes `range_merge`.
- **`Gap`** becomes `range_merge` of the hull minus both ranges, computed on multiranges. PostgreSQL
  has no gap function, and subtracting one range from another fails when two ranges would remain.
- **`Gaps()` and `Gaps(window)`** on a `RangeSet` become the hull minus the set, and the window
  minus the set.

The state checks on a range become PostgreSQL functions:

- **`IsEmpty()`** becomes `isempty`.
- **`IsUnboundedStart()`** becomes `lower_inf`.
- **`IsUnboundedEnd()`** becomes `upper_inf`.
- **`IsInfinity()`** becomes `lower_inf AND upper_inf`.
- **`IsFinite()`** becomes `NOT lower_inf AND NOT upper_inf AND NOT isempty`.

Good to know:

- **On a `RangeSet`, `IsInfinity()` is an equality check.** The other state checks use the
  multirange forms of the same functions. `IsInfinity()` instead becomes an equality with the
  infinite multirange: `x = '{(,)}'::datemultirange`. `lower_inf AND upper_inf` is right for a
  range but wrong for a multirange, because a multirange can be unbounded at both ends and still
  have a gap. PostgreSQL normalizes multiranges the same way this library does, so the equality is
  exact ([verified against live PostgreSQL](../README.md#verified-against-postgresql)).
- **`LowerBound()` and `UpperBound()` return `T?`.** PostgreSQL's `lower` and `upper` return `NULL`
  for an unbounded or empty range, and the in-memory methods match that. So `== null`, `HasValue`
  and `??` on a bound select the same rows in a query as in memory, on a range or a `RangeSet`
  column.
- **Ordering by a bound puts the rows without it where PostgreSQL puts NULLs.**
  `OrderBy(b => b.Period.LowerBound())` sorts the empty rows and the rows with no lower bound last,
  and `OrderByDescending(b => b.Period.UpperBound())` sorts those with no upper bound first. That
  holds on a required column too, because the bound is NULL there. In memory, `null` sorts first
  ascending and last descending. To sort as memory does, put a `!= null` key in front:
  `OrderBy(b => b.Period.LowerBound() != null).ThenBy(b => b.Period.LowerBound())`, or
  `OrderByDescending` and `ThenByDescending` for the other direction. To sort open starts first, as a
  range's own order does, order by the range: `OrderBy(b => b.Period)`
  ([Sort ranges](ranges.md#sort-ranges)).
- **Discrete upper bounds are adjusted for you.** For `int4range`, `int8range` and `daterange`,
  PostgreSQL normalizes to the half-open form `[lower, upper)`. This library normalizes to the
  closed form `[lower, upper]`. So `UpperBound()` becomes `upper(x) - 1`, and
  `UpperBoundInclusive()` becomes `NOT upper_inf(x) AND NOT isempty(x)`. The database always
  returns the same results as memory
  ([verified against live PostgreSQL](../README.md#verified-against-postgresql)).
- **Aggregates over zero rows differ.** In SQL the aggregates return `NULL` for zero input rows,
  which is standard PostgreSQL aggregate behaviour. The in-memory `RangeAgg()` returns the empty set.
  `RangeIntersectAgg()` returns `null` in both.
- **The factory flags must be constants.** To run as SQL, the bound-inclusiveness flags of the
  factory methods must be compile-time constants, because they pick the bounds literal, such as
  `'[]'`. In practice they always are, because the flags take their default at the call site.

### Match a column against a list of ranges

```csharp
var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(10, 20) };

// b."Seats" IN (@wanted1, @wanted2)   — one parameter per range
bookings.Where(b => wanted.Contains(b.Seats));
```

Each range in your list goes to PostgreSQL as its own parameter, and `Contains` runs as `IN`. You
get exactly the rows whose range equals one in your list, the same rows as in memory. This works
for a list, an array or any other collection you capture, and for every range type, `TimeRange`
and the NodaTime ranges included.
`Any` and `Count` over the list read the ranges from `VALUES`, so overlapping, adjacent, duplicate
and empty ranges stay what they are. An empty list matches nothing: `Contains` is `false`, `Any`
with a condition is `false` and `Count` with a condition is `0`.

Good to know:

- **`EF.Parameter(wanted)` throws.** It asks for the whole list as one parameter, and Npgsql would
  send that as a multirange rather than as an array of ranges. Remove `EF.Parameter`, and the list
  goes out one parameter per range.
- **Before 9.1.0 a captured list failed.** PostgreSQL rejected the parameter
  (`42809 op ANY/ALL (array) requires array on right side`), and for `TimeRange` Npgsql threw
  (`InvalidCastException`). A constant list and an inline `new[] { … }` already worked.
- **Before 10.0.0, `Any` or `Count` alone over a captured list returned a different answer from
  memory.** Without a `Contains` in the same query, the list went out as one parameter that
  PostgreSQL read as a multirange, which merges overlapping and adjacent ranges and drops empty
  ones. `wanted.Count(w => w.Overlaps(b.Seats))` over `[1,5]` and `[3,8]` was 1 where memory says
  2. If you wrapped the list in `EF.Constant` to avoid that, you can remove the wrapper; it still
  works.
- **A list of `RangeSet` values needs nothing.** It goes out as one array of multiranges, which is
  what it is.

## Store timestamp ranges

Here is what happens to the bounds of `DateTimeRange` and `DateTimeOffsetRange` on the way to the
column:

- **`DateTimeRange` keeps the wall-clock time.** Its bounds are written as `timestamp` with
  `DateTimeKind.Unspecified`. A UTC `DateTime` is read as wall-clock time, not converted.
- **`DateTimeOffsetRange` keeps the instant, not the offset.** Its bounds are converted to UTC for
  `timestamptz`. Values you read back carry the offset `+00:00`. They still compare equal to what
  you wrote, because `DateTimeOffset` equality compares instants.
- **A single value you pass is converted the same way.** `range.Contains(dto)` takes a
  `DateTimeOffset` with any offset, and `range.Contains(dt)` takes a `DateTime` of any `Kind`.
- **`DateTime.MaxValue` becomes `infinity`.** By default Npgsql maps `DateTime.MinValue` and
  `MaxValue` to PostgreSQL's `-infinity` and `infinity`. So a *finite* bound of `DateTime.MaxValue`
  becomes an explicit `infinity` bound in the database. That is still different from an
  *unbounded* end (`upper_inf` stays `false`), so the shape checks behave consistently.
- **A date at the extreme is adjacent to nothing in the database.** Npgsql also writes
  `DateOnly.MinValue` and `MaxValue`, and `LocalDate.MinIsoValue` and `MaxIsoValue`, as
  `-infinity` and `infinity`, and no date is next to those. So `IsAdjacentTo` returns `false` in
  SQL for `[0001-01-01,0001-01-01]` and `[0001-01-02,)`, and `true` in memory, where 0001-01-01 is
  a day like any other. One day inside the extremes the two sides agree. If you set
  `Npgsql.DisableDateTimeInfinityConversions`, the extremes stay dates and the two sides agree
  there too.

## Indexing a range column, and preventing overlaps

A GiST index over a range column is ordinary EF configuration, and the package isn't involved:

```csharp
modelBuilder.Entity<Booking>()
    .HasIndex(b => b.Period)
    .HasMethod("gist");
```

The index serves `&&`, `@>`, `<@` and the positional operators. It does *not* stop two overlapping
rows from being written, and your application code can't close that gap on its own. Checking for an
overlap and then inserting is a read-then-write race, so under concurrency two requests can both
find the slot free. Only a database constraint is atomic.

### Reject overlapping rows with an exclusion constraint

> **On EF Core 11, `EFCore.ComplexIndexes.PostgreSQL` is succeeded by `CodoMetis.EFCore.SchemaKit.PostgreSQL`.**
> ComplexIndexes 5.x stays on EF Core 10, so you can't install it beside the 11 packages. SchemaKit
> isn't on NuGet yet ([CaffeinatedCoder/EFCore.ComplexIndexes#38](https://github.com/CaffeinatedCoder/EFCore.ComplexIndexes/issues/38)).
> Until it is, use the 10.x packages with ComplexIndexes 5.x, or add the constraint with explicit SQL
> in a migration. Temporal keys don't wait for it: on EF Core 11, use
> [Npgsql's temporal keys](#reject-overlapping-rows-with-npgsqls-temporal-keys-postgresql-18).

PostgreSQL's tool for this is an exclusion constraint.
[`EFCore.ComplexIndexes.PostgreSQL`](https://www.nuget.org/packages/EFCore.ComplexIndexes.PostgreSQL)
declares one from your model. It accepts a range column mapped by this package exactly as it accepts
an `NpgsqlRange<T>`. That works because the constraint is declared over the *column*, and these
types map as ordinary scalars:

```csharp
modelBuilder.Entity<Booking>().HasExclusionConstraint(ex => ex
    .WithEquality(b => b.RoomId)     // same room …
    .WithOverlaps(b => b.Period)     // … must not overlap in time
    .HasName("ex_booking_room_period"));
```

The migration creates this:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;   -- injected automatically for the scalar `=` element
ALTER TABLE "Bookings" ADD CONSTRAINT "ex_booking_room_period"
  EXCLUDE USING gist ("RoomId" WITH =, "Period" WITH &&);
```

Now a second booking for the same room whose `Period` overlaps an existing one fails. PostgreSQL
raises `23P01` (`exclusion_violation`), and you get a `DbUpdateException` wrapping a
`PostgresException` that names the constraint.

- **Different rooms aren't affected.**
- **Adjacent periods are accepted.** `daterange` is discrete and canonical, so
  `[2024-01-01, 2024-01-10]` and `[2024-01-11, 2024-01-20]` are *adjacent, not overlapping*, and
  both are saved. That is the same distinction `IsAdjacentTo` and `Overlaps` draw in memory.

> **`EnsureCreated()` creates exclusion constraints only with `UseNpgsqlComplexIndexes()`.** A design-time service
> emits them, and `dotnet ef migrations add` loads it automatically. `EnsureCreated()` builds the
> schema through the runtime differ, which doesn't know about them. The table is created, the
> constraint silently isn't, and overlapping rows are accepted. This was verified: a table from
> `EnsureCreated()` carries zero exclusion constraints. Use migrations, add the constraint with
> explicit SQL, or call `UseNpgsqlComplexIndexes()` on the options. That registers the package's
> differ for `EnsureCreated()` too, so the constraints are created.

Everything in this section was run against PostgreSQL 17 with a `DateRange` property, including
the rejected overlap and the accepted adjacent pair.

### Reject overlapping rows with Npgsql's temporal keys (PostgreSQL 18)

On EF Core 11, Npgsql declares temporal keys itself, and every range and multirange column this
package maps works as the period. Put the range column last in the key and mark the key
`WithoutOverlaps()`:

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    => optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql
        .UseValueRanges()
        .SetPostgresVersion(18, 0));

protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.Entity<Booking>()
        .HasKey(b => new { b.RoomId, b.Period })
        .WithoutOverlaps();
```

The table is created with this key:

```sql
CONSTRAINT "PK_Bookings" PRIMARY KEY ("RoomId", "Period" WITHOUT OVERLAPS)
```

A second booking for the same room whose `Period` overlaps an existing one fails with a
`DbUpdateException` wrapping a `PostgresException`. Its `SqlState` is `23P01` and its
`ConstraintName` is the key's name. Adjacent bookings, and bookings for another room, are saved.

For a temporal foreign key, give the referenced entity such a key and mark the relationship
`WithPeriod()`:

```csharp
modelBuilder.Entity<Availability>()
    .HasKey(a => new { a.RoomId, a.Period })
    .WithoutOverlaps();

modelBuilder.Entity<Booking>()
    .HasOne(b => b.Availability)
    .WithMany()
    .HasForeignKey(b => new { b.RoomId, b.Period })
    .WithPeriod();
```

That gives `FOREIGN KEY ("RoomId", PERIOD "Period") REFERENCES "Availabilities" ("RoomId", PERIOD "Period")`.
A booking is saved when the room's availabilities cover its period together, even across two adjacent
rows. A booking that runs past them fails with `SqlState` `23503` (`foreign_key_violation`).

Good to know:

- **Call `SetPostgresVersion(18, 0)`.** Npgsql checks the PostgreSQL version it targets, not the
  server it connects to. Without the call, building the model throws (`InvalidOperationException`)
  before any SQL is sent. On PostgreSQL 16 and 17, use the
  [exclusion constraint](#reject-overlapping-rows-with-an-exclusion-constraint) above.
- **Every range and multirange type works as the period.** That includes `TimeRange` over the custom
  `timerange`, `YearMonthRange`, and the `RangeSet<TRange, T>` multiranges.
- **`btree_gist` is created for you.** Npgsql adds `CREATE EXTENSION IF NOT EXISTS btree_gist` to the
  schema, because the key indexes the scalar column with GiST.
- **Migrations and `EnsureCreated()` both create the key.** It is part of EF Core's own model, so no
  extra registration is needed.

Everything in this section was run against PostgreSQL 18 with Npgsql 11.0.0-rc.2, over a `DateRange`,
a `RangeSet<DateRange, DateOnly>`, a `TimeRange` and a `YearMonthRange` column for the key, and a
`DateRange` column for the foreign key.

### Reject overlapping rows with a temporal key (PostgreSQL 18)

On the 10.x line (EF Core 10), Npgsql has no temporal keys of its own, and this section declares them
through `EFCore.ComplexIndexes.PostgreSQL` instead. On EF Core 11, use
[Npgsql's temporal keys](#reject-overlapping-rows-with-npgsqls-temporal-keys-postgresql-18).
ComplexIndexes 5.x can't be installed beside the 11 packages, and its EF Core 11 successor,
`CodoMetis.EFCore.SchemaKit.PostgreSQL`, isn't on NuGet yet.

PostgreSQL 18 has this rule built in, as a temporal key: a `UNIQUE` constraint whose last column is a
range column marked `WITHOUT OVERLAPS`. `EFCore.ComplexIndexes.PostgreSQL` declares one with
`HasTemporalConstraint`, over any range or multirange column this package maps:

```csharp
modelBuilder.Entity<Booking>()
    .HasTemporalConstraint(b => b.RoomId, b => b.Period, "uq_booking_room_period");
```

The constraint it creates reads back from PostgreSQL as:

```sql
UNIQUE ("RoomId", "Period" WITHOUT OVERLAPS)
```

The rule is the exclusion constraint's. A second booking for the same room whose `Period` overlaps
an existing one fails with a `DbUpdateException` wrapping a `PostgresException`. Its `SqlState` is
`23P01` and its `ConstraintName` is the constraint's name. Bookings for another room, and adjacent
bookings, are saved.

A temporal foreign key goes one step further. It requires each row's period to lie inside the
periods the referenced rows hold for the same key:

```csharp
modelBuilder.Entity<Availability>()
    .HasTemporalConstraint(a => a.RoomId, a => a.Period, "uq_availability_room_period");

modelBuilder.Entity<Booking>()
    .HasTemporalForeignKey<Booking, Availability>(
        b => b.RoomId, b => b.Period, a => a.RoomId, a => a.Period, "fk_booking_room_period");
```

That gives `FOREIGN KEY ("RoomId", PERIOD "Period") REFERENCES "Availabilities"("RoomId", PERIOD "Period")`.
A booking passes when the room's availabilities cover its period together, even across two adjacent
rows. A booking that runs past them, or names a room with no availability, fails with `SqlState`
`23503` (`foreign_key_violation`). PostgreSQL requires the referenced columns to carry a temporal key,
which is why `Availability` declares one.

Good to know:

- **It needs PostgreSQL 18.** Older servers reject `WITHOUT OVERLAPS` as a syntax error. On 16 and
  17, use the [exclusion constraint](#reject-overlapping-rows-with-an-exclusion-constraint) above.
- **`btree_gist` is created for you.** The constraint indexes the scalar key with GiST, which needs
  the extension, and ComplexIndexes adds `CREATE EXTENSION IF NOT EXISTS btree_gist` to the schema.
- **`EnsureCreated()` needs `UseNpgsqlComplexIndexes()`**, exactly as for the exclusion constraint.
- **The API is ComplexIndexes'.** This package adds nothing for temporal keys. They work because
  every range and multirange type here maps to a column whose store type ends in `range`, which is
  what `HasTemporalConstraint` accepts.

Everything in this section was run against PostgreSQL 18 with `EFCore.ComplexIndexes.PostgreSQL`
5.4.0, over a `DateRange`, a `RangeSet<DateRange, DateOnly>` and a `TimeRange` column for the
temporal key, and a `DateRange` column for the foreign key.

## Store a value set in an array column

The same package maps every [value set type](value-sets.md) to its native PostgreSQL array column,
by convention, with nothing to configure. Versions with your own element type, such as
`StringSet<AccessRight>`, are recognized automatically from the open generic type. There is no
registration per element type to forget.

| Property type | Column type |
|---|---|
| `StringSet`, `StringSet<TElement>` | `text[]` |
| `GuidSet`, `GuidSet<TElement>` | `uuid[]` |
| `Int32Set`, `Int32Set<TElement>` | `integer[]` |
| `DateSet` | `date[]` |
| `YearMonthSet` (NodaTime) | `date[]` (month-aligned) |
| … and so on for all types | |

The set operations run as PostgreSQL array operators:

```csharp
// b."Tags" @> ARRAY[@tag]::text[]   — containment, not = ANY: a GIN index always serves it
bookings.Where(b => b.Tags.Contains(tag));

// b."Tags" && @wanted               — order- and duplicate-insensitive, like all of these
bookings.Where(b => b.Tags.Overlaps(wanted));

// b."Tags" <@ @allowed  /  b."Tags" @> @required
bookings.Where(b => b.Tags.IsSubsetOf(allowed));
bookings.Where(b => b.Tags.IsSupersetOf(required));

// b."Tags" <@ @allowed AND NOT (b."Tags" @> @allowed)   — the negated converse, not <>,
// so proper containment stays duplicate-insensitive like everything else here
bookings.Where(b => b.Tags.IsProperSubsetOf(allowed));

// cardinality(b."Tags") > 2  /  cardinality(b."Tags") = 0
bookings.Where(b => b.Tags.Count > 2);
bookings.Where(b => b.Tags.IsEmpty);

// array_remove(b."Tags", @tag)  — preserves canonical form, so it composes freely
bookings.Where(b => b.Tags.Remove(tag).Count > 1);

// array_cat(b."Tags", @more) @> ARRAY[@tag]::text[]
bookings.Where(b => b.Tags.Union(more).Contains(tag));
```

`Intersect`, `Except` and `Add` run in memory only. EF Core fails to translate them, by design.

Good to know:

- **`Remove` keeps the set canonical.** `array_remove` leaves the array sorted and deduplicated,
  so you can combine it with anything. `Union` is different, as the next section shows.
- **Your own element types travel as their underlying type.** An `AccessRight` parameter is bound
  as `text`, and loading a row runs the element's validation again.
- **An empty set and `NULL` stay different.** They are `{}` and `NULL` in the column. Whether the
  column allows `NULL` is up to the property.
- **A NULL set or element gives NULL.** In a query, `Union` gives NULL when either set is NULL, and
  `Remove` gives NULL when the set or the element is. A `Select` loads `null` for those rows, and
  `Where(… == null)` selects them. In memory, both throw for a `null`.
- **Plain arrays keep their own mapping.** `T[]` and `List<T>` properties keep their native Npgsql
  mapping, and both can sit next to value sets in one model. Scaffolding a database gives you plain
  arrays, because choosing a set type is a decision you make in your model.
- **The NodaTime package adds five set types.** The same `UseValueRangesNodaTime()` call registers
  them. `YearMonthSet` stores first-of-month dates, and reading checks that each date is the first
  of its month, exactly like `YearMonthRange`.

### Match a column against a list of sets

```csharp
var wanted = new List<StringSet> { StringSet.From("a", "b"), StringSet.From("c") };

// b."Tags" IN (@wanted1, @wanted2)   — one parameter per set
bookings.Where(b => wanted.Contains(b.Tags));

// EXISTS (SELECT 1 FROM (VALUES (@wanted1), (@wanted2)) AS w("Value") WHERE w."Value" && b."Tags")
bookings.Where(b => wanted.Any(w => w.Overlaps(b.Tags)));
```

Each set in your list goes to PostgreSQL as its own parameter, and `Contains` runs as `IN`. `IN`
compares the column with each set in turn. Every set is stored deduplicated and sorted, so you get
exactly the rows whose set equals one in your list, the same rows as in memory. This works for a
list, an array or any other collection you capture, and for every set type. `Any` and `Count` over
the list read the sets from `VALUES`.

Good to know:

- **`EF.Parameter(wanted)` throws.** It asks for the whole list as one parameter. A set is already
  a PostgreSQL array, so that parameter would be an array of arrays, which PostgreSQL doesn't have.
  Remove `EF.Parameter`, and the list goes out one parameter per set.
- **Your parameterized-collection setting is kept.** A context set to
  `UseParameterizedCollectionMode(ParameterTranslationMode.Constant)`, and a list wrapped in
  `EF.Constant`, still put the sets into the SQL as literals.
- **An empty list matches nothing.** `Contains` is `false`, `Any` with a condition is `false` and
  `Count` with a condition is `0`, as in memory. Before 10.0.0, `Any` and `Count` over an empty
  list threw (`InvalidOperationException`, "Empty collections are not supported as inline query
  roots").
- **Before 9.1.0 a captured list failed.** Npgsql sent it as one array parameter and threw
  (`InvalidCastException`) because it couldn't write the array of arrays. A constant list and an
  inline `new[] { … }` already worked.
- **A `Union` in the database can't be matched against a list.** `IN` is equality, so
  `wanted.Contains(b.Tags.Union(more))` throws, as the next section explains.

### Combine sets with `Union`

`Union` runs as `array_cat`. It is the one operation running as SQL whose result is **not**
canonical, because `array_cat` only concatenates the two arrays. You can't tell in exactly two
places: the operators above all ignore duplicates, and reading a set back puts it in canonical form
again. Anything that depends on order or on duplicates throws instead of running.

Array equality depends on both, and the order is the part that surprises people. In the database:

- **`{a,c} ∪ {a,b} = {a,b,c}` is false** because of the repeated element.
- **`{a,c} ∪ {b} = {a,b,c}` is false** because of the order alone, where nothing repeats.

Both are true in memory. To compare a union, use `IsSubsetOf`/`IsSupersetOf` instead. For
canonical sets they mean the same thing, and they ignore both order and duplicates. Or load the
rows first with `AsEnumerable()` and compare in memory:

```csharp
bookings.Select(b => b.Tags.Union(more));                    // fine — materialized canonical
bookings.Select(b => b.Tags.Union(more).Count);              // fine — client-evaluated, correct
bookings.Where(b => b.Tags.Union(more).IsSupersetOf(need));  // fine — @> ignores order and duplicates
bookings.Where(b => b.Tags.Union(more).Count > 2);           // throws
bookings.Select(b => b.Tags.Union(more) == expected);        // throws
bookings.AsEnumerable().Where(b => b.Tags.Union(more) == expected);  // fine — in memory
```

`Count` in a **projection** still works. Its translator declines a union, so EF runs `Count` in
memory on the loaded set. A comparison doesn't get that fallback, because EF translates `==`
itself. So a comparison throws in a projection too.

Since 9.0.0 the check runs on the **translated SQL**, right before it is sent, so it doesn't matter
how you write the query. It follows every value derived from a union: `array_cat` itself,
`array_remove` over one, a conditional or `??` that may return one, and a subquery or `Concat`
column that projects one. Such a value may only feed:

- **`@>`, `<@` and `&&`**, that is `Contains`, `Overlaps`, and the subset and superset methods.
- **`Remove` and `IsEmpty`.**
- **A null test.**
- **The outermost projection**, which is loaded and so put in canonical form.

Anything else throws, including:

- **Equality, `Count`, `GroupBy`, `Distinct` and `OrderBy`.**
- **`Union`, `Intersect` and `Except` of queries.**
- **`Contains` over a sequence of sets.**
- **An `ExecuteUpdate` value.** It would store the concatenation, and from then on equality and
  `Count` on that row would be wrong for good.

Good to know:

- **Before 9.0.0 the check missed most cases.** A LINQ-level pattern caught only `==`/`Equals` in
  predicates and `Count` over a bare union. A projection, a conditional, a coalesce, a grouping
  key, `Distinct` and `ExecuteUpdate` all ran and returned wrong answers.
- **Keep the SQL check in place.** The check runs from a decorator around the provider's
  `IRelationalParameterBasedSqlProcessorFactory`, and it **fails closed**. A `Union` running as SQL
  carries the placeholder name `valueranges_union_without_sql_guard` until the check renames it to
  `array_cat`. Suppose something replaces that service after `UseValueRanges()`, such as a
  `ReplaceService` call or another extension that replaces it rather than decorating it. Then every
  query with a `Union` running as SQL fails with PostgreSQL's
  `function valueranges_union_without_sql_guard(…) does not exist`, instead of running as if the
  check weren't there. Decorate the registered factory rather than replacing it, and both keep
  working.
- **There is no package-specific switch for the in-memory form, on purpose.** `AsEnumerable()` is
  the framework's own boundary, and everyone reads it the same way. Its cost, fetching every
  matching row, is visible where you pay it. A marker buried inside the expression would look local
  and cheap, and still force the same full load.
- **Sorting in SQL wouldn't fix it.** PostgreSQL has no `array_distinct`. And sorting inside the
  query orders `text` by the database collation rather than ordinal, so it would disagree with the
  order your sets have in memory, one layer down.

### Index a value set column

A GIN index is ordinary EF configuration, and the package isn't involved:

```csharp
modelBuilder.Entity<Booking>()
    .HasIndex(b => b.Tags)
    .HasMethod("GIN");
```

`Contains` runs as containment (`@>`) on purpose, rather than as `= ANY(...)`, because only
containment can use a GIN index. That gives you one code path, and it can always use the index.

### Rows written by other tools

Set equality (`==`) runs as SQL `=`, which compares arrays in order. It means set equality exactly
because every writer stores canonical form. Rows that other tools wrote in a non-canonical order
still match every *operator* above correctly: `@>`, `<@`, `&&` and the proper-containment pairs
ignore both order and duplicates. Those rows are put in canonical form when you load them.

Two members depend on every writer storing canonical form:

- **`==`**, because SQL `=` compares arrays as sequences.
- **`Count`**, because it runs as `cardinality`, which ignores order but *not* duplicates. A row
  stored as `{b,a,b}` loads as the two-element set `{a,b}`. So `set.Count` is 2 in memory, while
  `WHERE "Tags".cardinality = 2` doesn't match the row and `= 3` does.

`IsEmpty` isn't affected. An array is empty exactly when it has no elements, however many times
any of them repeats.

A date range that another tool wrote with an exclusive infinite bound loads one day short. For
example, `daterange('2026-01-01', 'infinity')` loads as `[2026-01-01,9999-12-30]`, and
`(-infinity,2026-01-01]` as starting on 0001-01-02. `DateOnly.MaxValue` and `MinValue` stand for
`infinity` and `-infinity`, so the range can hold the extreme only when it includes the infinity.
The literal, JSON and a JSON column read the same text the same way.

`Shift` and `Expand` keep a bound that stands for an infinity where it is, so a row read from
`tsrange('2026-01-01', 'infinity')`, moved and saved, still ends at `infinity`
([Move or widen a range](ranges.md#move-or-widen-a-range)). A `daterange` with an exclusive
`infinity)` is the exception on the way back: it loads as `[x,9999-12-30]`, and that saves as
`[x,9999-12-31)`, a finite date, whether or not you moved it.

If other tools write open-ended dates into your tables, have them write an absent bound
(`daterange('2026-01-01', NULL)`, which loads as `[2026-01-01,)`) or an inclusive one
(`'[2026-01-01,infinity]'`, which loads as `[2026-01-01,9999-12-31]`). The same holds for
`LocalDateRange` ([Domain width](architecture.md#domain-width-and-the-boundary)).

A `YearMonthRange` column can't load the exclusive form at all. `[2026-01-01,infinity)` holds every
day up to 9999-12-30, which ends part-way through a month, so the query throws
`InvalidOperationException`, as it does for any range that covers part of a month. `(-infinity,x)`
fails the same way at its start. The absent and the inclusive forms work: `[2026-01-01,infinity]`
loads as `[2026-01,9999-12]`, and `daterange('2026-01-01', NULL)` as `[2026-01,)`.

The difference comes from reading rows normalized while leaving them as written. The alternatives
are rewriting other tools' rows on read, or not translating `Count` at all, and neither is worth it
for the common case. If you query `Count` on a table that other tools also write, put the data in
canonical form when it comes in.

## Put a range or set inside a JSON column

A range, multirange or value set can sit inside a complex or owned type mapped to a `jsonb` column:

```csharp
modelBuilder.Entity<Contract>().ComplexProperty(c => c.Terms, terms => terms.ToJson());
```

**The document holds the column's text, not the API's JSON.** Each property is written as the text
of its PostgreSQL store type. A query that reaches into the document casts it to that type, so the
operators run exactly as on a plain column:

```json
{ "Period": "[2026-01-01,2026-06-30]", "Blocks": "{[2026-02-01,2026-02-28]}", "Tags": "{a,b}" }
```

```sql
WHERE CAST(c."Terms" ->> 'Period' AS daterange) @> @day
```

That text belongs to the database, and it differs from what System.Text.Json writes for the same
value in an API payload:

| Value | API JSON | Inside the document |
|---|---|---|
| a value set | `["a","b"]` | `"{a,b}"` |
| a `YearMonth` range or set (wrapper arity too) | `"[2026-01,2026-06]"`, `["2026-01"]` | month-aligned dates: `"[2026-01-01,2026-06-30]"`, `"{2026-01-01}"` — the cast reads dates |
| a temporal value | every tick (BCL) or nanosecond (NodaTime) | the store's microsecond, truncated toward 2000-01-01 as Npgsql truncates a parameter |
| `MinValue` / `MaxValue` of `DateTime`, `DateTimeOffset`, `DateOnly`, and `Instant`, `LocalDate`, `LocalDateTime` extremes | the extreme value | `-infinity` / `infinity`, as Npgsql sends them |

This text is also a **stored format**. Changing it later would mean migrating stored documents, not
recompiling. The temporal rule keeps the three copies of a value in agreement:

- **A document loads to the same value a native column would.**
- **A query that casts out of the document compares the value a parameter carries.**

If the document held every digit and let the server round them, `Doc.Period == period` would miss
the row it was saved from. `TemporalStoreParityTests` checks the rule against a live server.

**What works inside the document** (measured against PostgreSQL 17):

| Operation | |
|---|---|
| `SaveChanges`: insert, tracked change (to and from `null` too), nested complex types, replacing the whole document | ✓ — a complex type rewrites the whole document; an owned type writes a lone changed property with `jsonb_set`, and rewrites the document when several change |
| `null` and empty, kept apart | ✓ |
| the range and set operators, bounds, `Count`, projections | ✓ |
| `ExecuteUpdate` of the whole JSON property, a JSON-to-JSON copy, a range or multirange | ✓ — a range is then stored in PostgreSQL's own text form, which reads back |
| `ExecuteUpdate` of a value set | **refused** — EF Core would write it with `to_jsonb`, a JSON array the document cannot hold, and the row would no longer load. Recognized in the translated `UPDATE`, so it holds however the setter reaches the property (a join, `EF.Property`, a nested complex type) |
| `ExecuteUpdate` of a whole complex property that contains a value set | ✓ — EF serializes the complex value through the plugin's writer |
| `ExecuteUpdate` setting a property to `null` | fails for every type — a range or multirange in Npgsql (`42804`, the untyped `NULL` in `to_jsonb(NULL)`); a value set is refused before it reaches Npgsql, for the same reason. Set it to `null` with `SaveChanges`, or replace the whole JSON property |
| `ExecuteUpdate` on an owned JSON type | refused by EF Core |

Good to know:

- **A filter inside the document doesn't use the column's index.** It casts out of the document
  first. If such a query is hot, keep the value in a plain column of its own as well.
- **Before 9.0 these types couldn't be saved in a `ToJson()` type.** `SaveChanges` threw a
  `NullReferenceException` from inside EF, because the package's type mappings had no JSON reader
  or writer.

## Custom range types

Three range types map to a PostgreSQL range type the database doesn't ship. You create it once, from
your model, and the rest works as for the built-in ones:

| .NET type | Column types | Create it with | Good to know |
|---|---|---|---|
| `TimeRange` | `timerange`, `timemultirange` | `HasPostgresRange("timerange", "time")` | No `24:00:00`, see [below](#timerange-and-the-custom-timerange-type) |
| `Float64Range` | `float8range`, `float8multirange` | `HasPostgresRange("float8range", "double precision")` | Finite values only, and no `subtype_diff`, see [below](#float64range-and-the-custom-float8range-type) |
| `Float32Range` | `float4range`, `float4multirange` | `HasPostgresRange("float4range", "real")` | Finite values only, see [below](#float64range-and-the-custom-float8range-type) |

All three also need `EnableUnmappedTypes()` on the Npgsql data source, so Npgsql can send a type it
doesn't know by name. The value sets need nothing extra: `Float64Set` is a native `double
precision[]` column, and `Float32Set` a native `real[]` column.

## TimeRange and the custom timerange type

`timerange` isn't built into PostgreSQL. To use `TimeRange` columns you need two one-line additions
beyond `UseValueRanges()`:

```csharp
// 1. The database needs the type — this generates
//    CREATE TYPE timerange AS RANGE (SUBTYPE = time) in your migrations
//    (PostgreSQL auto-creates timemultirange alongside it):
modelBuilder.HasPostgresRange("timerange", "time");

// 2. Npgsql needs permission to resolve the unmapped type on the wire:
options.UseNpgsql(connectionString, npgsql => npgsql
    .UseValueRanges()
    .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));
// (call EnableUnmappedTypes() on your own NpgsqlDataSourceBuilder instead
//  if you pass a pre-built NpgsqlDataSource to UseNpgsql)
```

Everything else works on its own. All of PostgreSQL's range and multirange operators, functions
and aggregates are polymorphic (`anyrange`/`anymultirange`). So every LINQ query runs as SQL on the
custom type exactly as on the built-in ones
([verified against live PostgreSQL](../README.md#verified-against-postgresql)).

Good to know:

- **`TimeOnly` has no `24:00:00`.** PostgreSQL's `time` accepts the special value `24:00:00`, which
  `TimeOnly` can't represent. To say "until the end of the day", use an unbounded end or an
  inclusive `TimeOnly.MaxValue` bound.
- **A stored `24:00:00` bound fails to load.** If another tool wrote one, such as `[22:00,24:00)`,
  loading the row throws `ArgumentOutOfRangeException` from Npgsql's `time` read, and so does a
  query that selects the bound. Rewrite such rows to an unbounded end:
  `UPDATE … SET hours = timerange(lower(hours), NULL)`.

## Float64Range and the custom float8range type

Create the type without a `subtype_diff`, and turn on unmapped types as for `timerange`:

```csharp
// CREATE TYPE float8range AS RANGE (SUBTYPE = double precision) in your migrations;
// PostgreSQL creates float8multirange alongside it.
modelBuilder.HasPostgresRange("float8range", "double precision");

options.UseNpgsql(connectionString, npgsql => npgsql
    .UseValueRanges()
    .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));
```

Every LINQ query on a `Float64Range` or `RangeSet<Float64Range, double>` column runs as SQL, as on
the built-in types. A constant you write in the query, such as `r.Score.Contains(0.5)`, is sent as
`'0.5'::double precision`, the exact `double` you wrote.

Good to know:

- **Leave out `subtype_diff`.** The usual `subtype_diff = float8mi` overflows when PostgreSQL builds
  a GiST index over very large values, and the insert or `CREATE INDEX` fails (`22003`). Without it,
  the index builds and serves the range operators.
- **Only finite values load.** A row that another tool wrote with `NaN` or an infinity in a bound
  throws when you load it (`ArgumentException`). A `NaN` or infinite parameter throws before the query
  runs. A query over such a row still runs, though, in PostgreSQL's order: see [Keep NaN and infinity
  out of a float column](#keep-nan-and-infinity-out-of-a-float-column).
- **`Float32Range` works the same way over `float4range`.** Create it with
  `HasPostgresRange("float4range", "real")`. There is no `subtype_diff` to leave out: `float4mi`
  returns `real`, and PostgreSQL accepts only a function that returns `double precision`.
- **The text is PostgreSQL's.** `ToString()`, a SQL literal and JSON all print a `double` the way
  PostgreSQL does, so `1e23` is `9.999999999999999e+22` and `1e15` is `1e+15`. A `float` prints as
`real` does: `1e6f` is `1e+06`, and `0.1f` is `0.1`, never the `0.10000000149011612` of the same value
as a `double`.

### Keep NaN and infinity out of a float column

The finite-only rule covers every value your code creates, binds or loads. It can't cover a row that
another tool writes into the same column. A query that runs in the database over such a row answers
in PostgreSQL's order, where `NaN` sorts above every number, and only loading the entity throws:

| Another tool stored | Your query | Result |
|---|---|---|
| `Score = '[0,NaN)'` | `Where(r => r.Score.Contains(1e308))` | the row matches |
| `Ratio = 'NaN'` in a `double` column | `Where(r => r.Score.Contains(r.Ratio))` on `[0,)` | the row matches |
| `Weights = '{1,NaN}'` | `Select(r => r.Weights.Count)` | 2 |

To keep these values out of the database, give each float column a CHECK constraint:

```csharp
modelBuilder.Entity<Reading>().ToTable(table =>
{
    // A range: both bounds. lower and upper return NULL for an unbounded side and the empty range,
    // and a CHECK that is NULL passes.
    table.HasCheckConstraint("CK_Reading_Score_Finite",
        """lower("Score") NOT IN ('NaN', 'Infinity', '-Infinity') AND upper("Score") NOT IN ('NaN', 'Infinity', '-Infinity')""");

    // A multirange: its text. Its lower and upper are only the outer bounds, so {(,-Infinity],[1,2)}
    // would pass the range check above. A finite float never prints these letters.
    table.HasCheckConstraint("CK_Reading_Bands_Finite",
        """"Bands"::text !~* '(nan|inf)'""");

    // An array: no element in common with the three values.
    table.HasCheckConstraint("CK_Reading_Weights_Finite",
        """NOT ("Weights" && '{NaN,Infinity,-Infinity}')""");
});
```

The same three expressions work for `float4range`, `float4multirange` and `real[]`. For a range,
multirange or array column, they reject `NaN`, `Infinity` and `-Infinity` and accept every finite
value, an unbounded side, the empty range and `NULL`. On a column that already holds such rows,
adding the constraint fails until you fix them.

## Store a YearMonthRange

The NodaTime package stores `YearMonthRange` as a **month-aligned `daterange`**. For example,
`[2024-01, 2024-03]` becomes `[2024-01-01, 2024-04-01)`. You need no custom database type, and
every operator, bound accessor and aggregate runs as SQL and agrees with the in-memory results.
The `upper()` adjustment lands on the last day of the end month, and that month is the inclusive
upper bound in memory.

Reading checks month alignment. A `daterange` that covers part of a month fails loudly, instead of
silently shifting the boundaries.

There is one restriction. Months are coarser than the `date` type underneath, so the
`CreateFinite`/`CreateUnbounded*` factories can't build a range *in SQL from column values*.
Constant and parameter ranges work as usual. A factory call that depends on a column fails
translation with a clear error. In practice that only matters when a query builds the range *from
a column*:

```csharp
// Factories over constants and locals never reach the translator — EF evaluates
// them client-side and the result renders as a month-aligned daterange literal:
// r."BillingPeriod" && '[2024-01-01,2024-06-30]'::daterange
var from = new YearMonth(2024, 1); var to = new YearMonth(2024, 6);
reservations.Where(r => r.BillingPeriod.Overlaps(YearMonthRange.CreateFinite(from, to)));

// Building the range from a column would need month arithmetic in SQL — a closed
// upper bound must expand to first-of-next-month, which the element-wise bound
// conversion cannot express. Fails with the standard EF translation error:
reservations.Where(r => YearMonthRange.CreateUnboundedEnd(r.Day.ToYearMonth())
                                      .Contains(month));  // ⛔ InvalidOperationException
```

To build a range from a column, use a `LocalDateRange` built from the date column instead. Building
a `daterange` in SQL is fully supported there.

## What runs where

Most of the API runs as SQL and gives the same answers in the database as in memory. That is the
point of the library, and the [live-PostgreSQL suite](../README.md#verified-against-postgresql)
checks it. The rest runs in memory. For some of it, composed SQL or a subquery could give the same
answers, and the table says what it would take; `Gap` and `Gaps` show that a missing operator alone
doesn't stop a translation. For the rest, SQL would answer differently from memory in cases the
library can't rule out. These two tables are the whole picture.

**Runs as SQL.** You can use these in `Where`, `OrderBy` and `Select`, on columns and on
parameters. A value set `Union` composes only under some operators; see
[Combine sets with `Union`](#combine-sets-with-union).

| Surface | Operations | PostgreSQL |
|---|---|---|
| Ranges | `Contains`, `IsContainedBy`, `Overlaps`, `IsAdjacentTo` | `@>`, `<@`, `&&`, `-\|-` |
| | `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf` | `<<`, `>>`, `&<`, `&>` |
| | `CompareTo` against 0, `OrderBy` | `<`, `<=`, `>`, `>=`, `=`, `ORDER BY` |
| | `Intersect`, `Union`, `Except`, `Merge` | `*`, `+`, `-`, `range_merge` |
| | `Gap` | `range_merge` over the multirange difference of the hull and both ranges |
| | `IsEmpty`, `IsUnboundedStart`/`End`, `IsInfinity`, `IsFinite` | `isempty`, `lower_inf`, `upper_inf`, and combinations |
| | `LowerBound`/`UpperBound`, `LowerBoundInclusive`/`UpperBoundInclusive` | `lower`, `upper`, `lower_inc`, `upper_inc` |
| | `Length` on `Int32Range` and `Int64Range` | `upper(x) - lower(x)` after a widening cast, and 0 for the empty range |
| | `Clamp(value)` on `Int32Range`, `Int64Range` and `DecimalRange` | `LEAST(GREATEST(value, lower(x)), upper(x))`, one step back from a discrete type's exclusive upper bound, and NULL for the empty range |
| | `CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` | range constructor functions |
| | `DateRange.Month`, `Quarter`, `Year` and `IsoWeek` with an argument read from a column | `daterange(first, last, '[]')`, the days from `date_trunc` or `make_date`, and an error where memory throws |
| | `RangeAgg`, `RangeIntersectAgg`, over ranges or sets | `range_agg`, `range_intersect_agg` |
| `RangeSet` | the same operations over multirange columns, plus `Complement` and `IsContainedBy` | the multirange forms, and `'{(,)}' - x` |
| | `Gaps()`, `Gaps(window)` | `multirange(range_merge(x)) - x`, `multirange(window) - x` |
| Value sets | `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, and the proper variants | `@>`, `&&`, `<@` |
| | `Count`, `IsEmpty` | `cardinality` |
| | `Union`, `Remove` | `array_cat`, `array_remove` |
| Both families | `==`/`!=` and `Equals` on a column | `=`, `<>` |

**Runs in memory only.** These compute the right answer in memory. In a predicate they fail
translation, and in the final projection EF runs them in memory instead:

| Operation | Why it runs in memory |
|---|---|
| `Length` on the other range types | Not translated. A temporal range open to `infinity` differs: PostgreSQL fails (`cannot subtract infinite dates`) where memory returns a finite length. On `Int32Range` and `Int64Range` it runs as SQL. |
| `Values()` and `Values(step)` on a discrete range, `Split` on any range | Not translated. Each returns a sequence, which in SQL would be `generate_series` wrapped in `ARRAY(SELECT …)`. A long range produces one element per step. |
| `ToRangeSet()` / `ToInt32Set()` and the other bridge conversions | Not translated. An array becomes a multirange through `range_agg` over one-element ranges, which needs care at the element type's extremes: `[2147483647,2147483647]` can't be built as an `int4range`. A multirange becomes an array only by enumerating it, as `Values` does. |
| `Shift`, `Expand` on any range | Not translated. Rebuilding the range from `lower(x) + d` differs from memory unless each case is handled: the empty range becomes `(,)`, `time` wraps at midnight where `TimeRange` throws, and a bound moved past the last date .NET holds throws in memory where PostgreSQL returns a later date. A bound at `infinity` stays there in both. |
| `LocalDateRange.Month`, `Quarter`, `Year` and `IsoWeek`, and `YearMonthRange.Quarter` and `Year`, with an argument read from a column | Not translated. PostgreSQL has no dates before 4714-11-24 BC, so the month and the year that hold its first day have no start, and the database would fail where memory returns a range. That holds with Npgsql's infinity conversions on or off. Built from values your code passes in, the range is computed in memory and sent as one constant, so comparing a column against a month runs as SQL. On `DateRange` these run as SQL. |
| `Clamp(value)` on the other range types | Not translated. The same `LEAST(GREATEST(…))` form would apply, but it has only been checked against memory on `Int32Range`, `Int64Range` and `DecimalRange`, where it runs as SQL. |
| `Intersect`, `Except` on value sets | Not translated. Filtering the left array with `unnest … WITH ORDINALITY` keeps its order, but the result is canonical only when the stored arrays are, as with `Union`. |
| `Add` on value sets | Inserting in canonical order means sorting in SQL, where `text` follows the database collation rather than ordinal order. |
| The value set indexer, `set[0]` | Subscripting reads the stored array, which is canonical only for rows this library wrote ([Rows written by other tools](#rows-written-by-other-tools)). PostgreSQL arrays also start at 1, and an index past the end is `NULL` where memory throws. |
| `Values` on a value set | It is the loaded array itself. In a query, use the set's own members, such as `Contains` and `Count`. |
| `Count` and the indexer on a `RangeSet` | Not translated. PostgreSQL has no function for either, and each would be a subquery over `unnest(x)`. |
| `Compare` on `RangeLowerBoundComparer`, `RangeSet.LowerBoundComparer`, any other `IComparer` of ranges or sets, and any other `Compare(x, y)` over them | EF Core reads every `Compare(x, y)` as the natural order of `x` and `y`, whatever the comparer. In SQL the lower-bound comparer would compare in the range order. What counts is what you compare, not the type the comparer is declared over: an `IComparer<IRange<T>>`, a non-generic `IComparer` and a comparer of value sets run in memory too. So do a `Compare` method on a class that isn't a comparer and a static `Compare`. The query runs the method you called, so a class whose public `Compare` differs from its `IComparer<T>.Compare` gets the public one, as in memory. For the range order in SQL, call `x.CompareTo(y)`. |
| `From` and `Parse` on any range or set | They build a value in .NET. Given values your code passes in, EF Core runs them before the query and sends the result as a parameter. |
| `ToInstantRange`, `ToInterval`, `ToLocalDateRange`, `ToDateInterval` and `ToYearMonthRange` (NodaTime package) | They convert in .NET: between NodaTime's `Interval` or `DateInterval` and a range type, or between `LocalDateRange` and `YearMonthRange`. |

Good to know:

- **`Length` on `Int32Range` and `Int64Range` counts in the database as it does in memory.** `[1,10]`
  measures 10, the empty range 0, and a range unbounded on either side `null`. An `Int64Range` whose
  count is larger than `long.MaxValue` measures `null` too, so you can filter and sort by `Length`.
- **`Clamp` on `Int32Range`, `Int64Range` and `DecimalRange` clamps in the database as it does in
  memory.** A value inside the range comes back as it is, and one outside comes back as the bound it
  passes, even an exclusive one. The empty range gives `null`, and so does a `null` range or value.
- **`DateRange.Month`, `Quarter`, `Year` and `IsoWeek` over a column build the same range in the
  database as in memory.** `DateRange.Month(b.Day)` gives the month that holds each row's day, so
  you can filter, join and sort by it. The first period starts at `-infinity` and the last ends at
  `infinity`, as Npgsql writes `DateOnly.MinValue` and `MaxValue` (the days themselves with
  `Npgsql.DisableDateTimeInfinityConversions`), so they still equal a stored range.
- **An argument out of range fails in the database too.** A month outside 1 to 12, a year outside 1
  to 9999, a week its week-year doesn't have, and the ISO week that runs past 9999-12-31 throw
  `ArgumentOutOfRangeException` in memory. In a query, PostgreSQL raises an error
  (`PostgresException`). For a week, or a year other than 0, the message names the argument. For a
  month, a quarter or the year 0, it shows the date PostgreSQL was asked to build: `Quarter(2026, 5)`
  reports `2026-13-01`.
- **An in-memory operation inside a `Where` fails loudly.** EF throws rather than silently fetching
  the whole table, and that is the intended behaviour.
- **In the final `Select` it runs in memory.** EF Core evaluates the outermost projection on the
  rows it is already returning. A `Select` that a later `Where` or `OrderBy` reads from has to
  translate, so it fails there too. Each loaded row pays for the computation.
- **`ToString`, `GetHashCode` and `GetEnumerator` run in memory too.** They are for loaded values:
  the literal, hashing and `foreach`. The literal is formatted in .NET, so `ToString` gives the
  library's literal rather than PostgreSQL's text output.
- **`Count` over a `Union` computed in the database throws.** `array_cat` concatenates without
  deduplicating, so the query throws rather than returning a wrong count. In a projection, EF
  counts the loaded set in memory instead. See [Combine sets with `Union`](#combine-sets-with-union).

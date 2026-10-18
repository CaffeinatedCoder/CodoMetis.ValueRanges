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
| `TimeRange`                      | `timerange` ([custom type](#timerange-and-the-custom-timerange-type)) |
| … and so on for all types        |                  |

Good to know:

- **The package bridges to Npgsql through `NpgsqlRange<T>`.** The conversion happens at the
  provider boundary, and your properties keep the range types.
- **Scaffolding gives you `NpgsqlRange<T>`.** `dotnet ef dbcontext scaffold` maps range columns to
  `NpgsqlRange<T>`, not to these types, because the package provides no design-time services.
  Change the property types by hand after scaffolding.

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
- **`Intersect`** becomes `*`.
- **`Union` and `Except`** turn both ranges into multiranges first, then use `+` and `-`. That
  matches their `RangeSet` return type. A union of two ranges that don't touch is a real
  two-element multirange, never an error.
- **`CreateFinite`, `CreateUnboundedStart` and `CreateUnboundedEnd`** become range constructor
  calls inside a guard. As in memory, a start after the end gives you an empty range.

Since v4 the eight predicates above (`Contains` through `IsAdjacentTo`) also work on a `RangeSet`,
with either a range or a multirange on the other side.

### Read bounds, merge ranges and aggregate

Since v4 you can also read bounds, merge ranges, aggregate per group and compare multiranges:

```csharp
// ORDER BY lower(b."Period") — bound accessors: lower / upper / lower_inc / upper_inc
bookings.OrderBy(b => b.Period.LowerBound());

// range_merge(b."Period", @other) and range_merge(b."BlockedDays")
bookings.Select(b => b.Period.Merge(other));
bookings.Select(b => b.BlockedDays.Merge());

// range_agg(b."Period") / range_intersect_agg(b."Period") per group
bookings.GroupBy(b => b.CustomerId)
        .Select(g => g.Select(b => b.Period).RangeAgg());

// isempty / lower_inf / upper_inf on multirange columns
bookings.Where(b => !b.BlockedDays.IsEmpty());

// Value equality on multirange columns — b."BlockedDays" = @set
bookings.Where(b => b.BlockedDays == someSet);
```

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
  for an unbounded or empty range, and the in-memory methods match that.
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

> **Exclusion constraints come from migrations, not `EnsureCreated()`.** A design-time service
> emits them, and `dotnet ef migrations add` loads it automatically. `EnsureCreated()` builds the
> schema through the runtime differ, which doesn't know about them. The table is created, the
> constraint silently isn't, and overlapping rows are accepted. This was verified: a table from
> `EnsureCreated()` carries zero exclusion constraints. Use migrations, or add the constraint with
> explicit SQL.

Everything in this section was run against PostgreSQL 17 with a `DateRange` property, including
the rejected overlap and the accepted adjacent pair.

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
| `ExecuteUpdate` setting a property to `null` | fails for every type — a range or multirange in Npgsql (`42804`); a value set is refused before it reaches Npgsql, because a `NULL` written there would turn the whole document into `NULL`. Set it to `null` with `SaveChanges`, or replace the whole JSON property |
| `ExecuteUpdate` on an owned JSON type | refused by EF Core |

Good to know:

- **A filter inside the document doesn't use the column's index.** It casts out of the document
  first. If such a query is hot, keep the value in a plain column of its own as well.
- **Before 9.0 these types couldn't be saved in a `ToJson()` type.** `SaveChanges` threw a
  `NullReferenceException` from inside EF, because the package's type mappings had no JSON reader
  or writer.

## TimeRange and the custom timerange type

`timerange` isn't built into PostgreSQL. To use `TimeRange` columns you need two one-line additions
beyond `UseValueRanges()`:

```csharp
// 1. The database needs the type — this generates
//    CREATE TYPE timerange AS RANGE (SUBTYPE = time) in your migrations
//    (PostgreSQL 14+ auto-creates timemultirange alongside it):
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
checks it. A few operations run in memory only, always because PostgreSQL has no operator for them,
not because nobody wrote the translation. These two tables are the whole picture.

**Runs as SQL.** You can use these in `Where`, `OrderBy` and `Select`, on columns and on
parameters:

| Surface | Operations | PostgreSQL |
|---|---|---|
| Ranges | `Contains`, `IsContainedBy`, `Overlaps`, `IsAdjacentTo` | `@>`, `<@`, `&&`, `-\|-` |
| | `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf` | `<<`, `>>`, `&<`, `&>` |
| | `Intersect`, `Union`, `Except`, `Merge` | `*`, `+`, `-`, `range_merge` |
| | `IsEmpty`, `IsUnboundedStart`/`End`, `IsInfinity`, `IsFinite` | `isempty`, `lower_inf`, `upper_inf`, and combinations |
| | `LowerBound`/`UpperBound`, `LowerBoundInclusive`/`UpperBoundInclusive` | `lower`, `upper`, `lower_inc`, `upper_inc` |
| | `CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` | range constructor functions |
| | `RangeAgg`, `RangeIntersectAgg` | `range_agg`, `range_intersect_agg` |
| `RangeSet` | the same operations over multirange columns, plus `Complement` | the multirange forms, and `'{(,)}' - x` |
| Value sets | `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, and the proper variants | `@>`, `&&`, `<@` |
| | `Count`, `IsEmpty` | `cardinality` |
| | `Union`, `Remove` | `array_cat`, `array_remove` |
| Both families | `==`/`!=` on a column | `=`, `<>` |

**Runs in memory only.** These compute the right answer in memory. In a predicate they fail
translation, and in a projection EF runs them in memory instead:

| Operation | Why it does not translate |
|---|---|
| `Length` on any range | The finite case would be `upper(x) - lower(x)`, but the empty range measures 0 where PostgreSQL's subtraction yields `NULL`, and `int4range` overflows `int4` before a cast can widen it. |
| `Values()` on a discrete range | Enumeration is `generate_series`, whose result is a set of rows rather than a value — it cannot appear where a scalar is expected. |
| `ToRangeSet()` / `ToInt32Set()` and the other bridge conversions | PostgreSQL converts between arrays and multiranges only through `unnest` and a custom aggregate. |
| `Clamp(value)` on any range | Expressible as `GREATEST`/`LEAST` over `lower`/`upper`, but the empty and unbounded cases have no bound to clamp to and would need a `CASE` per shape. |
| `Intersect`, `Except`, `Add` on value sets | PostgreSQL's array type has no intersection, difference, or sorted insert. |
| The value set indexer, `set[0]` | Array subscripting exists, but the canonical order is the CLR comparer's, not the server's. |

Good to know:

- **An in-memory operation inside a `Where` fails loudly.** EF throws rather than silently fetching
  the whole table, and that is the intended behaviour.
- **Inside a `Select` it is safe.** It runs on the rows that are already being returned.
- **`Count` over a `Union` computed in the database throws.** `array_cat` concatenates without
  deduplicating, so the query throws rather than returning a wrong count. In a projection, EF
  counts the loaded set in memory instead. See [Combine sets with `Union`](#combine-sets-with-union).

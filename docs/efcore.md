# Entity Framework Core (PostgreSQL)


The companion package **CodoMetis.ValueRanges.EFCore.PostgreSQL** maps every range type to its PostgreSQL range column and `RangeSet<TRange, T>` to the corresponding multirange column, bridging through `NpgsqlRange<T>` at the provider boundary — giving you identical semantics whether executing against an in-memory collection or a live PostgreSQL database.

```bash
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL
```

Enable it with one line — no value converters, comparers, or column types to configure:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());
```

Properties of the range types and of `RangeSet<TRange, T>` are then mapped by convention:

| Property type                    | Column type      |
|----------------------------------|------------------|
| `Int32Range`                     | `int4range`      |
| `RangeSet<Int32Range, int>`      | `int4multirange` |
| `DateRange`                      | `daterange`      |
| `RangeSet<DateRange, DateOnly>`  | `datemultirange` |
| `TimeRange`                      | `timerange` ([custom type](#timerange-and-the-custom-timerange-type)) |
| … and so on for all types        |                  |

The full range algebra translates from LINQ to SQL:

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

`Contains`, `Overlaps`, `IsContainedBy`, `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf` and `IsAdjacentTo` map to `@>`, `&&`, `<@`, `<<`, `>>`, `&<`, `&>` and `-|-` — on ranges and, since v4, on `RangeSet` with range or multirange operands. `Intersect` maps to `*`; `Union` and `Except` lift both operands to multiranges (`+`/`-`), matching their `RangeSet` return type — a disjoint union is a real two-element multirange, never an error. The `CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` factories translate to guarded range constructor calls with the model's inverted-bounds-yield-empty semantics.

New in v4:

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

Notes:

- Range state checks translate directly: `IsEmpty()` → `isempty`, `IsUnboundedStart()` → `lower_inf`, `IsUnboundedEnd()` → `upper_inf`, `IsInfinity()` → `lower_inf AND upper_inf`, `IsFinite()` → `NOT lower_inf AND NOT upper_inf AND NOT isempty`.
- The same state checks exist on `RangeSet` and translate to the multirange forms of those functions — **except `IsInfinity()`**, which translates to equality against the infinite multirange (`x = '{(,)}'::datemultirange`). `lower_inf AND upper_inf` is the right translation for a range and the wrong one for a multirange, which can satisfy both and still have a gap. PostgreSQL canonicalizes multiranges the way the model does, so the equality is exact ([verified against live PostgreSQL](../README.md#verified-against-postgresql)).
- `LowerBound()`/`UpperBound()` return `T?` because PostgreSQL's `lower`/`upper` return `NULL` for an unbounded or empty operand — the in-memory implementation matches.
- For the discrete types (`int4range`, `int8range`, `daterange`), PostgreSQL canonicalizes to half-open `[lower, upper)` while the model canonicalizes to closed `[lower, upper]`. `UpperBound()` therefore translates to `upper(x) - 1` and `UpperBoundInclusive()` to `NOT upper_inf(x) AND NOT isempty(x)`, so server results always equal the in-memory results ([verified against live PostgreSQL](../README.md#verified-against-postgresql)).
- The aggregates return `NULL` in SQL for zero input rows (standard PostgreSQL aggregate behavior), while the in-memory `RangeAgg()` returns the empty set. `RangeIntersectAgg()` returns `null` in both worlds.
- The factory-method bound-inclusiveness flags must be compile-time constants to translate (they pick the bounds literal, e.g. `'[]'`); in practice they always are, because the flags default at the call site.

Timestamp semantics:

- `DateTimeRange` bounds are written as `timestamp` with `DateTimeKind.Unspecified` — a UTC-kinded `DateTime` is reinterpreted as wall-clock time, not converted. `DateTimeOffsetRange` bounds are normalized to UTC for `timestamptz`: the instant is preserved, but the original offset is not round-tripped (values read back carry offset `+00:00` and compare equal to what was written, since `DateTimeOffset` equality is instant-based).
- Npgsql by default maps `DateTime.MinValue`/`MaxValue` to PostgreSQL `-infinity`/`infinity`. A *finite* bound of `DateTime.MaxValue` therefore becomes an explicit `infinity` bound in the database — which is distinct from an *unbounded* side (`upper_inf` stays `false`), so shape checks behave consistently.
- Reverse engineering (`dotnet ef dbcontext scaffold`) maps range columns to `NpgsqlRange<T>`, not to these types — the plugin provides no design-time services. Apply the range types manually after scaffolding.

## Indexing a range column, and preventing overlaps

A GiST index over a range column is ordinary EF configuration — no package involvement:

```csharp
modelBuilder.Entity<Booking>()
    .HasIndex(b => b.Period)
    .HasMethod("gist");
```

That serves `&&`, `@>`, `<@` and the positional operators. What it does *not* do is stop two overlapping rows from being written, and that is a gap application code cannot close on its own: checking for an overlap and then inserting is a read-then-write race, so under concurrency two requests can both find the slot free. Only a database constraint is atomic.

PostgreSQL's answer is an exclusion constraint, and [`EFCore.ComplexIndexes.PostgreSQL`](https://www.nuget.org/packages/EFCore.ComplexIndexes.PostgreSQL) declares one from the model. It accepts a range column mapped by this package exactly as it accepts an `NpgsqlRange<T>` — the constraint is expressed over the *column*, and these types map as ordinary scalars:

```csharp
modelBuilder.Entity<Booking>().HasExclusionConstraint(ex => ex
    .WithEquality(b => b.RoomId)     // same room …
    .WithOverlaps(b => b.Period)     // … must not overlap in time
    .HasName("ex_booking_room_period"));
```

which migrates to:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;   -- injected automatically for the scalar `=` element
ALTER TABLE "Bookings" ADD CONSTRAINT "ex_booking_room_period"
  EXCLUDE USING gist ("RoomId" WITH =, "Period" WITH &&);
```

A second booking for the same room whose `Period` overlaps an existing one now fails with `23P01` (`exclusion_violation`), surfacing as a `DbUpdateException` wrapping a `PostgresException` that names the constraint. Different rooms are unaffected, and — because `daterange` is discrete and canonicalized — `[2024-01-01, 2024-01-10]` and `[2024-01-11, 2024-01-20]` are *adjacent, not overlapping*, so both are accepted. That is the same distinction `IsAdjacentTo` and `Overlaps` draw in memory.

> **Exclusion constraints come from migrations, not `EnsureCreated()`.** They are emitted by a design-time service that `dotnet ef migrations add` loads automatically. `EnsureCreated()` builds its schema through the runtime differ, which does not know about them — the table is created, the constraint silently is not, and overlapping rows are accepted. Verified: an `EnsureCreated()` table carries zero exclusion constraints. Use migrations, or add the constraint with explicit SQL.

Everything above was executed against PostgreSQL 17 with a `DateRange` property, including the rejected overlap and the accepted adjacent pair.

## Value set columns

The same package maps every [value set type](value-sets.md) to its native PostgreSQL array column — by convention, with nothing to configure. Wrapper instantiations (`StringSet<AccessRight>`) are recognized automatically from the open generic; there is no per-element registration to forget:

| Property type | Column type |
|---|---|
| `StringSet`, `StringSet<TElement>` | `text[]` |
| `GuidSet`, `GuidSet<TElement>` | `uuid[]` |
| `Int32Set`, `Int32Set<TElement>` | `integer[]` |
| `DateSet` | `date[]` |
| `YearMonthSet` (NodaTime) | `date[]` (month-aligned) |
| … and so on for all types | |

The set algebra translates to PostgreSQL's array operators:

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

`Intersect`, `Except` and `Add` are client-side only and fail query translation by design.

`Union` is the one translated operation whose result is **not** canonical — `array_cat`
concatenates. That is invisible to the operators above (all duplicate-insensitive) and to
materialization (reads re-canonicalize), but anything sensitive to order or multiplicity is
**refused** rather than translated: `Count`, and since 7.0.1 `==`/`!=`/`Equals`. Array equality
is sensitive to both, and the ordering half is the one that surprises — on the server
`{a,c} ∪ {a,b} = {a,b,c}` is false for the repeated element, and `{a,c} ∪ {b} = {a,b,c}` is false
for the ordering alone, where nothing repeats. Both are true in memory. Compare with
`IsSubsetOf`/`IsSupersetOf` instead, which mean the same thing for canonical sets and ignore both,
or materialize the union first.

This is deliberately not "fixed" by canonicalizing in SQL: PostgreSQL has no array_distinct, and
sorting inside the query orders `text` by the database collation rather than ordinally — which is
the same disagreement with the client's canonical order, one layer down.

`Remove` has no such caveat: `array_remove` leaves the array sorted and deduplicated. Wrapper elements bind as their backing primitive (`AccessRight` parameters travel as `text`), and materialization re-runs the element's validation.

**Indexing** is ordinary EF configuration — no package involvement:

```csharp
modelBuilder.Entity<Booking>()
    .HasIndex(b => b.Tags)
    .HasMethod("GIN");
```

`Contains` deliberately translates as containment (`@>`) rather than `= ANY(...)`, because only containment is GIN-servable — one code path, always indexable.

**Set equality** (`==`) translates to SQL `=`, which is order-sensitive on arrays: it means set equality exactly because every writer stores canonical form. Rows written by other tools in non-canonical order are still matched correctly by every *operator* above — `@>`, `<@`, `&&` and the proper-containment pairs ignore both order and duplicates — and normalize when materialized.

Two members carry the canonical-writers precondition, not one:

- **`==`**, because SQL `=` compares arrays as sequences.
- **`Count`**, because it translates to `cardinality`, which ignores order but *not* duplicates. A row stored as `{b,a,b}` materializes as the two-element set `{a,b}`, so `set.Count` is 2 in memory while `WHERE "Tags".cardinality = 2` does not match it and `= 3` does. `IsEmpty` is unaffected: an array is empty exactly when it has no elements, whatever its multiplicities.

That divergence is inherent to reading normalized while leaving the row as written — the alternatives are rewriting foreign rows on read, or refusing to translate `Count` at all, and neither is worth the common case. If you query `Count` against a table other tools also write, canonicalize on ingest.

The empty set and a NULL column stay distinct (`{}` vs `NULL`); nullability is the property's own concern.

Two boundary notes: plain `T[]`/`List<T>` properties keep their native Npgsql mapping — both can coexist in one model — and database scaffolding produces plain arrays, since opting into a set type is a model decision. The NodaTime satellite registers its five set types via the same `UseValueRangesNodaTime()` call; `YearMonthSet` persists first-of-month dates and reads validate alignment, exactly like `YearMonthRange`.

## TimeRange and the custom timerange type

`timerange` is not built into PostgreSQL, so using `TimeRange` columns takes two one-line opt-ins beyond `UseValueRanges()`:

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

Everything else is automatic: all range and multirange operators, functions and aggregates in PostgreSQL are polymorphic (`anyrange`/`anymultirange`), so the full LINQ translation works on the custom type exactly as on the built-ins — [verified against live PostgreSQL](../README.md#verified-against-postgresql). One caveat: PostgreSQL's `time` admits the special value `24:00:00`, which `TimeOnly` cannot represent; express "until end of day" as an unbounded end or an inclusive `TimeOnly.MaxValue` bound.

## YearMonthRange storage

The NodaTime satellite stores `YearMonthRange` as a **month-aligned `daterange`** — `[2024-01, 2024-03]` becomes `[2024-01-01, 2024-04-01)` — so no custom database type is involved and every operator, bound accessor and aggregate translates and agrees with the in-memory results (`upper()` compensation lands on the last day of the end month, whose month is the model's inclusive upper bound). Reads validate month alignment: a `daterange` covering a partial month fails loudly instead of silently shifting boundaries. The one restriction: because months are coarser than the `date` subtype, the `CreateFinite`/`CreateUnbounded*` factories cannot be built *in SQL from column values* — constant and parameter ranges work as usual, and a column-dependent factory call fails translation with a clear error.

In practice the restriction only bites when a query constructs the range *from a column*:

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

For column-driven construction, fall back to a `LocalDateRange` built from the date column — `daterange` construction in SQL is fully supported there.

## What runs where

Most of the surface translates to SQL and gives identical answers in memory and on the server — that is the point of the library, and the [live-PostgreSQL suite](../README.md#verified-against-postgresql) holds it to that. A minority evaluates client-side, always because PostgreSQL has no operator for it rather than because the translation was not written. This table is the whole picture.

**Translated to SQL** — usable in `Where`, `OrderBy`, `Select`, on columns and on parameters:

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

**Client-side only** — these compute the right answer in memory, fail translation in a predicate, and fall back to client evaluation in a projection:

| Operation | Why it does not translate |
|---|---|
| `Length` on any range | The finite case would be `upper(x) - lower(x)`, but the empty range measures 0 where PostgreSQL's subtraction yields `NULL`, and `int4range` overflows `int4` before a cast can widen it. |
| `Values()` on a discrete range | Enumeration is `generate_series`, whose result is a set of rows rather than a value — it cannot appear where a scalar is expected. |
| `ToRangeSet()` / `ToInt32Set()` and the other bridge conversions | PostgreSQL converts between arrays and multiranges only through `unnest` and a custom aggregate. |
| `Clamp(value)` on any range | Expressible as `GREATEST`/`LEAST` over `lower`/`upper`, but the empty and unbounded cases have no bound to clamp to and would need a `CASE` per shape. |
| `Intersect`, `Except`, `Add` on value sets | PostgreSQL's array type has no intersection, difference, or sorted insert. |
| The value set indexer, `set[0]` | Array subscripting exists, but the canonical order is the CLR comparer's, not the server's. |

Two consequences worth knowing. A client-side operation inside a `Where` **fails translation loudly** rather than silently fetching the table — EF throws, and that is the intended behaviour. Inside a `Select` it evaluates on the rows already being returned, which is safe. And `Count` over a *server-computed* `Union` is refused outright rather than answered, because `array_cat` concatenates without deduplicating — see the note under [value set columns](#value-set-columns).


# CodoMetis.ValueRanges.EFCore.PostgreSQL

Entity Framework Core (Npgsql) plugin for
[CodoMetis.ValueRanges](https://www.nuget.org/packages/CodoMetis.ValueRanges): maps the range types
to PostgreSQL range columns, `RangeSet<TRange, T>` to multirange columns and the value set types to
native array columns — then translates the full algebra from LINQ to SQL, so the same code gives
identical results in memory and against a live database.

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL
```

Enable it with one line — no value converters, comparers or column types to configure:

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());
```

## Mapped by convention

| Property type                   | Column type                              |
|---------------------------------|------------------------------------------|
| `Int32Range`                    | `int4range`                              |
| `RangeSet<Int32Range, int>`     | `int4multirange`                         |
| `DateRange`                     | `daterange`                              |
| `RangeSet<DateRange, DateOnly>` | `datemultirange`                         |
| `TimeRange`                     | `timerange` (custom type — see below)    |
| `StringSet`                     | `text[]`                                 |
| `GuidSet`                       | `uuid[]`                                 |
| … and so on for every type      |                                          |

Wrapper set instantiations like `StringSet<AccessRight>` are recognized automatically from the open
generic — there is no per-element registration to forget. Plain `string[]`/`List<Guid>` properties
keep their native Npgsql array mapping and coexist in the same model.

## Range algebra

```csharp
var day = new DateOnly(2024, 6, 15);

bookings.Where(b => b.Period.Contains(day));        // b."Period" @> @day
bookings.Where(b => b.Period.Overlaps(other));      // b."Period" && @other
bookings.Select(b => b.Period.Intersect(other));    // b."Period" * @other
bookings.OrderBy(b => b.Period.LowerBound());       // ORDER BY lower(b."Period")
bookings.Select(b => b.Period.Merge(other));        // range_merge(b."Period", @other)
bookings.Where(b => b.BlockedDays == someSet);      // b."BlockedDays" = @set

bookings.GroupBy(b => b.CustomerId)                 // range_agg(b."Period")
        .Select(g => g.Select(b => b.Period).RangeAgg());
```

`Contains`, `Overlaps`, `IsContainedBy`, `IsStrictlyLeftOf`/`RightOf`,
`DoesNotExtendLeftOf`/`RightOf` and `IsAdjacentTo` map to `@>`, `&&`, `<@`, `<<`, `>>`, `&<`, `&>`
and `-|-` — on ranges and on `RangeSet` with range or multirange operands. `Intersect` maps to `*`;
`Union` and `Except` lift both operands to multiranges (`+`/`-`), matching their `RangeSet` return
type, so a disjoint union is a real two-element multirange rather than an error. The
`CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` factories translate to guarded range
constructor calls carrying the model's inverted-bounds-yield-empty semantics.

State checks translate directly: `IsEmpty()` → `isempty`, `IsUnboundedStart()` → `lower_inf`,
`IsUnboundedEnd()` → `upper_inf`, `IsInfinity()` → `lower_inf AND upper_inf`, `IsFinite()` → the
negation of both plus `NOT isempty`.

## Value set algebra

```csharp
users.Where(u => u.Roles.Contains("admin"));        // u."Roles" @> ARRAY['admin']::text[]
users.Where(u => u.Roles.Overlaps(required));       // u."Roles" && @required
users.Where(u => u.Roles.IsSubsetOf(granted));      // u."Roles" <@ @granted
users.Where(u => u.Roles.Count > 2);                // cardinality(u."Roles") > 2
users.Where(u => u.Roles.Remove("admin").IsEmpty);  // cardinality(array_remove(…)) = 0
```

`Contains` always translates as `@>` rather than `= ANY`, so a plain GIN index serves it.
`Intersect`, `Except` and `Add` are client-side only — PostgreSQL's array type has no intersection,
difference or sorted insert — and fail query translation by design.

## Notes

- **Discrete canonicalization is compensated.** PostgreSQL canonicalizes `int4range`, `int8range`
  and `daterange` to half-open `[lower, upper)` while the model canonicalizes to closed
  `[lower, upper]`, so `UpperBound()` translates to `upper(x) - 1` and `UpperBoundInclusive()` to
  `NOT upper_inf(x) AND NOT isempty(x)`. Server results always equal in-memory results, verified
  against live PostgreSQL.
- **`LowerBound()`/`UpperBound()` return `T?`** because PostgreSQL's `lower`/`upper` return `NULL`
  for an unbounded or empty operand. The in-memory implementation matches.
- **Aggregates return `NULL` in SQL for zero input rows** (standard PostgreSQL behaviour), while
  the in-memory `RangeAgg()` returns the empty set. `RangeIntersectAgg()` returns `null` in both.
- **Timestamps.** `DateTimeRange` bounds are written as `timestamp` with `DateTimeKind.Unspecified`
  — a UTC-kinded `DateTime` is *reinterpreted* as wall-clock time, not converted.
  `DateTimeOffsetRange` bounds are normalized to UTC for `timestamptz`: the instant is preserved,
  the original offset is not round-tripped.
- **`DateTime.MinValue`/`MaxValue`** map to PostgreSQL `-infinity`/`infinity` by Npgsql's default
  rule — a *finite* bound that happens to be infinite, still distinct from an *unbounded* side
  (`upper_inf` stays `false`).
- **`TimeRange` needs two opt-ins**, because PostgreSQL has no built-in `timerange`:
  `HasPostgresRange` to create the type and `EnableUnmappedTypes` on the data source.
- **`Union` is the one set operation whose SQL result is not canonical** — it translates to
  `array_cat`, which concatenates without deduplicating. Harmless inside the duplicate-insensitive
  operators and on materialization (reads re-canonicalize), but `Count` over a union is refused
  rather than counting duplicates.
- **Sets are mapped as scalars** with plugin-owned mappings and translators, never through EF's
  primitive-collection machinery.
- **Reverse engineering** (`dotnet ef dbcontext scaffold`) maps range columns to `NpgsqlRange<T>`
  and array columns to plain arrays, not to these types — the plugin provides no design-time
  services. Apply the types manually after scaffolding; opting into them is a model decision.

For NodaTime types, add
[CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime)
and call `npgsql.UseValueRangesNodaTime()` instead.

## Documentation

The [full README](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/README.md#entity-framework-core-postgresql)
documents every translation with its generated SQL. See also the
[changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.EFCore.PostgreSQL/CHANGELOG.md).

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

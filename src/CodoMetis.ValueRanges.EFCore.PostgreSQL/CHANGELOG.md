# Changelog — CodoMetis.ValueRanges.EFCore.PostgreSQL

The changes that affect the EF Core (Npgsql) package. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [10.0.0] — 2026-10-17

### Added

- **`Float64Range` maps to the custom `float8range`** (`float8multirange` for `RangeSet<Float64Range, double>`), created with `HasPostgresRange("float8range", "double precision")` and no `subtype_diff`, plus `EnableUnmappedTypes()`. `Float64Set` and `Float64Set<T>` map to `double precision[]`. A stored `NaN` or infinity throws on load, and a `NaN` parameter throws before the query runs ([Custom range types](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#custom-range-types)).
- **`Float32Range` maps to the custom `float4range`** (`float4multirange`), created with `HasPostgresRange("float4range", "real")` and `EnableUnmappedTypes()`. `Float32Set` and `Float32Set<T>` map to `real[]` ([Custom range types](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#custom-range-types)).

### Fixed

- EF Core: `Any` or `Count` with a predicate over an empty captured list of value sets, or of ranges beside a `Contains`, returns `false` or `0`, as in memory. It threw `InvalidOperationException` ("Empty collections are not supported as inline query roots") ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).

## [9.1.0] — 2026-10-17

### Added

- `wanted.Contains(b.Tags)` over a captured list, array or other collection of value sets runs as `IN (@p1, @p2, …)`, one parameter per set, and returns the same rows as in memory. It threw `InvalidCastException` in Npgsql. `EF.Parameter(wanted)` throws and says to remove it ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- `wanted.Contains(b.Seats)` over a captured list, array or other collection of ranges runs as `IN (@p1, @p2, …)`, one parameter per range, and returns the same rows as in memory. It failed with `42809`, and with `InvalidCastException` for `TimeRange`. `EF.Parameter(wanted)` there throws and says to remove it ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-ranges)).

### Fixed

- `ExecuteUpdate` setting a value set inside a `ToJson()` column to `null` is still refused, now for its own reason: a `NULL` there would turn the whole document into `NULL`. The message blamed the JSON array a value would write. Set it with `SaveChanges`, or replace the whole JSON property ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#put-a-range-or-set-inside-a-json-column)).

## [9.0.0] — 2026-10-11

Nothing was removed or renamed. Some queries that ran now throw, and temporal values reach the database differently: [Migration from v8.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x).

### Breaking

- A query that builds on a server-computed value set `Union` throws `InvalidOperationException`, where it answered differently from memory: equality in a projection, `?:` or `??` around a union, `GroupBy`, `Distinct`, `OrderBy`, set operations, `Count` through `??`, and `ExecuteUpdate` to a union. Use `IsSubsetOf` and `IsSupersetOf`, or materialize first ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- If you replace `IRelationalParameterBasedSqlProcessorFactory` after `UseValueRanges()`, every query with a server-side `Union` fails with `valueranges_union_without_sql_guard`. Wrap the registered factory instead ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- Temporal SQL constants carry the value Npgsql binds, truncated to the microsecond toward 2000-01-01, with the extremes as `-infinity` and `infinity`. A constant now matches the row a parameter matched, and a temporal set whose elements are less than a microsecond apart is stored with one of them ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- The numeric wrapper sets read element text with their family's grammar, so an element that formats in the current culture (`1,5` under `de-DE`) is rejected instead of bound as 15.

### Changed

- A union may still feed `@>`, `<@`, `&&`, `Remove`, `IsEmpty` or the outermost projection, and `Count` in a projection still runs in memory. The check reads the final SQL, so it follows a union through `array_remove`, `CASE`, `COALESCE` and subqueries ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).

### Fixed

- A range, multirange or value set inside a `ToJson()` column saves, where `SaveChanges` threw `NullReferenceException`, and its operators translate (`CAST(doc ->> 'Period' AS daterange) @> @day`). The document holds the store's text, a persisted format: `"{a,b}"` for a value set. Temporal values in it follow the constant rule above ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- A compiled model (`dotnet ef dbcontext optimize`) saves and queries these JSON columns, through the public `LiteralJsonValueReaderWriter<TValue>`. `--nativeaot` generation fails at design time and isn't supported yet.
- `ExecuteUpdate` of a value set inside a `ToJson()` column throws, however the setter reaches it, because EF Core would store a JSON array the row could no longer load from. Ranges, multiranges, a whole complex property holding a set, and the whole JSON property can be set ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- A range built in a query from values that don't depend on it, such as `r.Period == DateRange.CreateFinite(…)`, translates, where it threw "does not have a type mapping assigned".
- A temporal range's element operand gets the range's own mapping. `DateTimeOffsetRange.Contains(dto)` threw for any non-UTC offset, and `DateTimeRange.Contains` for a UTC `DateTime`.
- A `DateTimeSet<T>` element keeps its wall clock on the way to the column, even one the machine's time zone skips.
- From the core package: the JSON converters, `TypeConverter`s and the culture-free literal of 9.0.0 apply to every mapped type.

## [8.0.0] — 2026-08-17

No signature changed, and one query that ran now throws: [Migration from v7.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v7x).

### Breaking

- `==`, `!=` and `Equals` over a server-computed value set `Union` fail translation in a filter, where they returned wrong rows: `array_cat` keeps duplicates and the operands' order. Use `Contains`, `Overlaps`, `IsSubsetOf` or `IsSupersetOf`, or call `AsEnumerable()` first ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v7x)).

### Changed

- The wrapper-set bridge rejects an element type it doesn't know, instead of writing its `ToString()`.
- `RangeSet.Except` with an infinite range now gives the same empty set in memory that the SQL translation always gave.

### Fixed

- Docs: this package's README linked to anchors the root README doesn't have. They now point to `docs/efcore.md`.

## [7.0.0] — 2026-08-17

Nothing was removed or renamed: [Migration from v6.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x).

### Added

- **Mappings for the new wrapper sets**: `Int16Set<T>` to `smallint[]`, `DecimalSet<T>` to `numeric[]`, `DateSet<T>` to `date[]`, `TimeSet<T>` to `time[]`, `DateTimeSet<T>` to `timestamp[]` and `DateTimeOffsetSet<T>` to `timestamptz[]`, found automatically ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- **`SetTypeRegistry.RegisterFamily`**, for a package that adds wrapper set families.

### Changed

- The temporal wrapper sets bind elements in round-trip form and parse them strictly, so an element that ignores the format specifier is rejected instead of stored truncated to the second. `StringSet<T>`, `GuidSet<T>`, `Int32Set<T>` and `Int64Set<T>` are unchanged.
- `DateTimeSet<T>` and `DateTimeOffsetSet<T>` normalize as their closed sets do: `Unspecified` for `timestamp`, UTC for `timestamptz`.
- The core package's fixes to `Contains` with an empty range and to `IsStrictlyLeftOf` and `IsStrictlyRightOf` bring memory in line with the SQL, which was right. No translation changed.
- Docs: `Count` on a value set, like `==`, assumes every row is canonical. A row another tool stored as `{b,a,b}` counts 3 in SQL and loads as `{a,b}`. No behaviour changed ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).

### Fixed

- `Int64Range.Contains(25)` and `RangeSet<Int64Range, long>.Contains(25)` with a constant run, where PostgreSQL rejected them (`42883: operator does not exist: int8range @> integer`). The constant now carries `::bigint`, and no other SQL changed.

## [6.3.0] — 2026-08-16

### Added

- **`RangeSet.IsFinite()` translates** to `NOT lower_inf(x) AND NOT upper_inf(x) AND NOT isempty(x)`.
- **`RangeSet.IsInfinity()` translates** to equality with the infinite multirange (`x = '{(,)}'::datemultirange`), not to `lower_inf(x) AND upper_inf(x)`, which a multirange with a gap also satisfies ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).

## [6.2.1] — 2026-08-16

No change in this package. `IsAdjacentTo` always translated to `-|-` correctly; the core package's 6.2.1 fix makes memory agree.

## [6.2.0] — 2026-08-16

### Fixed

- `Count` over `Union(…).Remove(x)` fails translation in a filter and runs in memory in a projection, where it counted shared elements twice.

## [6.1.0] — 2026-08-15

No change in this package.

## [6.0.0] — 2026-08-14

No breaking changes.

### Added

- **Value set columns**: the core value sets map to native arrays (`StringSet` → `text[]`, `GuidSet` → `uuid[]`, …), and wrapper sets such as `StringSet<YourKey>` are found automatically ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- **Set operations in SQL**: `Contains` → `@> ARRAY[value]`, so a GIN index serves it, `Overlaps` → `&&`, `IsSubsetOf` → `<@`, `IsSupersetOf` → `@>`, `Union` → `array_cat`, `Remove` → `array_remove`, `Count` and `IsEmpty` → `cardinality`. `Count` over a `Union` fails translation.

## [5.0.0] — 2026-08-13

No breaking changes.

### Added

- **`TimeRange` mapping** to the custom `timerange` type, which needs `HasPostgresRange` and `EnableUnmappedTypes` ([TimeRange](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#timerange-and-the-custom-timerange-type)).

## [4.1.0] — 2026-08-12

### Changed

- Range types are registered through `RangeTypeRegistry.Register`, so other packages can add theirs. Your code doesn't change.

## [4.0.0] — 2026-08-10

### Added

- **Bound accessors in SQL**: `LowerBound()`, `UpperBound()`, `LowerBoundInclusive()` and `UpperBoundInclusive()` → `lower`, `upper`, `lower_inc` and `upper_inc`, so `OrderBy(b => b.Period.LowerBound())` is `ORDER BY lower("Period")`.
- **`Merge`** → `range_merge`, and **`RangeAgg` and `RangeIntersectAgg`** → `range_agg` and `range_intersect_agg` inside a `GroupBy`.
- **`RangeSet` in SQL**: every operator and state check translates to its multirange form, `==` to `=`.

## [3.1.0] — 2026-06-17

No change in this package.

## [3.0.0] — 2026-06-11

The first release of this package.

### Added

- **Range columns**: the range types map to `int4range`, `int8range`, `numrange`, `daterange`, `tsrange` and `tstzrange`, and `RangeSet<TRange, T>` to the matching multirange, with no value converters to write. Turn it on with `npgsql.UseValueRanges()`.
- **Range operations in SQL**: `Contains`, `IsContainedBy`, `Overlaps`, `IsStrictlyLeftOf`, `IsStrictlyRightOf`, `DoesNotExtendLeftOf`, `DoesNotExtendRightOf` and `IsAdjacentTo` (`@>`, `<@`, `&&`, `<<`, `>>`, `&<`, `&>`, `-|-`), `Intersect`, `Union` and `Except`, and the factories as range constructors.

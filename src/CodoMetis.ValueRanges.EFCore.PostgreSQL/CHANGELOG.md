# Changelog — CodoMetis.ValueRanges.EFCore.PostgreSQL

The changes that affect the EF Core (Npgsql) package. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [11.0.0-preview.1] — 2026-10-19

The first preview of the 11 line: the same public API as 10.0.0, on .NET 11, EF Core 11 and Npgsql 11. The 10.x line stays on .NET 10.

### Breaking

- Targets `net11.0` and references EF Core 11 and `Npgsql.EntityFrameworkCore.PostgreSQL` 11 (release candidates while .NET 11 is in preview). PostgreSQL 16 or later, as in 10.x.
- EF Core: `EFCore.ComplexIndexes.PostgreSQL` 5.x stays on EF Core 10 and can't be installed beside the 11 packages. Its EF Core 11 successor, `CodoMetis.EFCore.SchemaKit.PostgreSQL`, isn't on NuGet yet ([CaffeinatedCoder/EFCore.ComplexIndexes#38](https://github.com/CaffeinatedCoder/EFCore.ComplexIndexes/issues/38)). For exclusion constraints until then, stay on 10.x or add the constraint with explicit SQL. Temporal keys are Npgsql's own on EF Core 11 ([Exclusion constraints](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#reject-overlapping-rows-with-an-exclusion-constraint)).
- EF Core: `LowerBound()` and `UpperBound()` in a query are `NULL` for an empty range or set and on an unbounded side, as in memory. So `== null` selects those rows, where it selected none on a required column, and `?? fallback` returns the fallback, where the row was missing ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-bound-comparisons-with-null-in-queries)).
- EF Core: a range, multirange or value set predicate over a NULL column is `false`, so its negation keeps the row, as EF Core does for `string` and `string[]`. `!b.Period.Overlaps(p)` dropped the NULL rows, and a projected predicate threw ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-predicates-over-a-null-range-or-a-null-column)).
- EF Core: `Contains(T)` on a range or multirange is `false` in a query when the element is NULL: a column of a missing child in an outer join, or a nullable column read with `.Value`. Its negation dropped the row, and a projection threw ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-predicates-over-a-null-range-or-a-null-column)).
- EF Core: `CreateFinite`, `CreateUnboundedStart` and `CreateUnboundedEnd` over a NULL element are NULL in a query, as EF Core's own `AddDays` is. They built `empty` or `(,)`, so `Contains(v)` selected a row without the element ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-predicates-over-a-null-range-or-a-null-column)).
- EF Core: a value set `Union` with a NULL set, or `Remove` with a NULL set or element, is NULL in a query, as `Where(… == null)` already said. A `Select` loaded the other set instead, for a nullable column, a missing outer-join child or a `null` parameter ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-value-set-union-and-remove-over-null-in-queries)).
- EF Core: any `Compare(x, y)` over ranges or sets, `RangeLowerBoundComparer.Compare` and every other `IComparer` included, runs in memory only in a query, as the method called. A filter or an ordering fails translation. In SQL it compared in the range order, so `Compare(r.Period, p) == 0` missed rows with the same lower bound ([#464](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/464), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#move-rangelowerboundcomparer-out-of-queries)).

### Added

- EF Core: Npgsql 11's own temporal keys accept the range and multirange columns this package maps. `HasKey(…).WithoutOverlaps()` and `WithPeriod()` foreign keys work over a `DateRange`, a `TimeRange` or a `RangeSet`, on PostgreSQL 18 with `SetPostgresVersion(18, 0)` ([Temporal keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#reject-overlapping-rows-with-npgsqls-temporal-keys-postgresql-18)).
- EF Core: `Length` on `Int32Range` and `Int64Range` runs as SQL, so a query can filter and sort by it. It counts the integers as memory does: 0 for the empty range, and `null` for an unbounded side or, on `Int64Range`, a count larger than `long.MaxValue` ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- EF Core: `Clamp` on `Int32Range`, `Int64Range` and `DecimalRange` runs as SQL, so a query can filter and sort by the clamped value. It answers as memory does: the value inside the range, otherwise the bound it passes, and `null` for the empty range or a `null` range or value ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- EF Core: `Clamp` runs as SQL on every range type, no longer only on `Int32Range`, `Int64Range` and `DecimalRange`, and answers as memory does, at the extremes too. A `DateTime` stored as `timestamp with time zone` against a `DateTimeRange` runs in memory, because PostgreSQL would compare them through the session's time zone ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- EF Core: `DateRange.Month`, `Quarter`, `Year` and `IsoWeek` run as SQL when they read a column, so `DateRange.Month(b.Day)` works in a `Where` and an `OrderBy`. They build the range memory builds, the extremes included, and an argument memory rejects fails in PostgreSQL too. The NodaTime forms stay in memory ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- EF Core: `Union`, `Add`, `Intersect` and `Except` on value sets run as SQL for every set type except `StringSet` and `StringSet<TElement>`. They give the deduplicated, sorted set memory gives, so a query can compare, count, group by and store the result ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).

### Changed

- EF Core: an `ExecuteUpdate` that sets a nullable value set inside a JSON column to a value that may be null is refused for the JSON array alone; Npgsql 11 writes the null outcome as JSON null, so the message no longer names a whole-document NULL ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#put-a-range-or-set-inside-a-json-column)).
- EF Core: an empty captured list of ranges or sets reads no rows through EF Core 11's own `SELECT … WHERE FALSE`; the SQL differs from 10.x, the answers do not ([Captured lists](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- Docs: each package's Project website link on NuGet opens the [documentation site](https://caffeinatedcoder.github.io/CodoMetis.ValueRanges/), and its README links the site and the [walkthrough](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/walkthrough.md).
- Docs: a value set over your own element type, other than `StringSet<TElement>`, needs a `CompareTo` that orders values as the underlying type does, because the database sorts the underlying values ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#how-your-element-types-sort-and-serialize)).

### Fixed

- Docs: [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where), the READMEs and the XML docs give each in-memory operation's real reason. `Shift` and `Expand` keep a bound at `infinity`, so that reason was stale. `Length`, `Values`, `Split`, the bridge conversions and `Clamp` would take composed SQL, not a missing operator. In-memory operations run in the final `Select` only.
- Docs: the [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where) tables list every public member a query can call. `Equals` runs as `=`. `Values` on a value set, `Count` and the indexer on a `RangeSet`, `From`, `Parse`, the NodaTime conversions, `ToString`, `GetHashCode` and `GetEnumerator` run in memory.
- Docs: an ordering by `LowerBound()` or `UpperBound()` puts the empty rows and those without that bound where PostgreSQL puts NULLs, last ascending and first descending, also on a required column. A `!= null` key in front sorts them as memory does ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#read-bounds-merge-ranges-and-aggregate)).
- EF Core: an empty captured list of ranges or sets keeps EF Core's ordering column (`_ord`, behind `FirstOrDefault` over the list) an integer, as it is when the list has elements. It was cast to the range or set type. No answer changed, but comparing that column with an integer would have failed for the empty list ([Captured lists](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).

## [10.3.0] — 2026-10-25

Some calls answer differently: [Changed answers in 10.3.0](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1030).

### Changed answers

- EF Core: a value set `Union` with a NULL set, or `Remove` with a NULL set or element, is NULL in a query, as `Where(… == null)` already said. A `Select` loaded the other set instead, for a nullable column, a missing outer-join child or a `null` parameter ([#395](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/395), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1030)).
- EF Core: any `Compare(x, y)` over ranges or sets, `RangeLowerBoundComparer.Compare` and every other `IComparer` included, runs in memory only in a query, as the method called. A filter or an ordering fails translation. In SQL it compared in the range order, so `Compare(r.Period, p) == 0` missed rows with the same lower bound ([#396](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/396), [#452](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/452), [#464](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/464), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1030)).

### Added

- EF Core: `Length` on `Int32Range` and `Int64Range` runs as SQL, so a query can filter and sort by it. It counts the integers as memory does: 0 for the empty range, and `null` for an unbounded side or, on `Int64Range`, a count larger than `long.MaxValue` ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- EF Core: `Clamp` on `Int32Range`, `Int64Range` and `DecimalRange` runs as SQL, so a query can filter and sort by the clamped value. It answers as memory does: the value inside the range, otherwise the bound it passes, and `null` for the empty range or a `null` range or value ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- EF Core: `DateRange.Month`, `Quarter`, `Year` and `IsoWeek` run as SQL when they read a column, so `DateRange.Month(b.Day)` works in a `Where` and an `OrderBy`. They build the range memory builds, the extremes included, and an argument memory rejects fails in PostgreSQL too. The NodaTime forms stay in memory ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).

### Changed

- Docs: each package's Project website link on NuGet opens the [documentation site](https://caffeinatedcoder.github.io/CodoMetis.ValueRanges/), and its README links the site and the [walkthrough](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/walkthrough.md).

## [10.2.0] — 2026-10-25

### Changed answers

- EF Core: `LowerBound()` and `UpperBound()` in a query are `NULL` for an empty range or set and on an unbounded side, as in memory. So `== null` selects those rows, where it selected none on a required column, and `?? fallback` returns the fallback, where the row was missing ([#354](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/354), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).
- EF Core: a range, multirange or value set predicate over a NULL column is `false`, so its negation keeps the row, as EF Core does for `string` and `string[]`. `!b.Period.Overlaps(p)` dropped the NULL rows, and a projected predicate threw ([#357](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/357), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).
- EF Core: `Contains(T)` on a range or multirange is `false` in a query when the element is NULL: a column of a missing child in an outer join, or a nullable column read with `.Value`. Its negation dropped the row, and a projection threw ([#387](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/387), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).
- EF Core: `CreateFinite`, `CreateUnboundedStart` and `CreateUnboundedEnd` over a NULL element are NULL in a query, as EF Core's own `AddDays` is. They built `empty` or `(,)`, so `Contains(v)` selected a row without the element ([#388](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/388), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).

### Fixed

- Docs: [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where), the READMEs and the XML docs give each in-memory operation's real reason. `Shift` and `Expand` keep a bound at `infinity`, so that reason was stale. `Length`, `Values`, `Split`, the bridge conversions, `Clamp`, `Intersect` and `Except` would take composed SQL, not a missing operator. In-memory operations run in the final `Select` only.
- Docs: the [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where) tables list every public member a query can call. `Equals` runs as `=`. `Values` on a value set, `Count` and the indexer on a `RangeSet`, `From`, `Parse`, the NodaTime conversions, `ToString`, `GetHashCode` and `GetEnumerator` run in memory.
- Docs: two queries in 10.2.0 still return something other than the same LINQ over the loaded rows, without an error: a value set `Union` or `Remove` over NULL in a projection, and `RangeLowerBoundComparer` in a query. Each has a workaround, and its fix comes in a later 10.x minor ([#397](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/397), [EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/release/10.x/docs/efcore.md#known-differences-in-1020)).
- Docs: an ordering by `LowerBound()` or `UpperBound()` puts the empty rows and those without that bound where PostgreSQL puts NULLs, last ascending and first descending, also on a required column. A `!= null` key in front sorts them as memory does ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#read-bounds-merge-ranges-and-aggregate)).
- EF Core: on EF Core 10.0.2 and later, an empty captured list of ranges or sets keeps EF Core's ordering column (`_ord`, behind `FirstOrDefault`) an integer, as with elements. It was cast to the range or set type. No answer changed, but comparing it with an integer would have failed for the empty list ([Captured lists](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).

## [10.1.0] — 2026-10-19

### Fixed

- EF Core: `Any` or `Count` over an empty captured list of ranges or value sets returns `false` or `0` on EF Core 10.0.2 and later, where it failed with `42883: operator does not exist: text && int4range`. The stopgap `AppContext.SetSwitch("Microsoft.EntityFrameworkCore.Issue37216", true)` also brings back EF Core's own failure on empty lists of other types ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- EF Core: `RangeSet.Contains` translates on the EF Core 10 patch that carries [dotnet/efcore#37081](https://github.com/dotnet/efcore/issues/37081), where every overload threw `ArgumentException` inside EF Core before translation. That patch, planned as 10.0.15, was not published when 10.1.0 shipped. Its stopgap `AppContext.SetSwitch("Microsoft.EntityFrameworkCore.Issue37081", true)` also gives up EF Core's own fix for `IReadOnlySet` parameters, which inline their values again ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#query-ranges-with-linq)).
- EF Core: `ExecuteUpdate` of a value set inside a JSON column is refused naming the property (`'Doc.Tags'`) on Npgsql EF 10.0.3 with `SetPostgresVersion(16, 0)` or higher, where it was refused naming the column (`'Doc'`), and the refusal no longer says that a value that may be `null` writes `NULL` as the whole document ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#put-a-range-or-set-inside-a-json-column)).
- EF Core: `a == b ? null : a` over a value set `Union` computed on the server, which EF Core writes as `NULLIF(a, b)`, is refused as value set equality, where it was refused naming no operation ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#combine-sets-with-union)).

## [10.0.0] — 2026-10-19

Nothing was removed or renamed. Some calls answer differently, and text PostgreSQL refuses is refused here too: [Migration from v9.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v9x).

### Breaking

- EF Core: the packages support PostgreSQL 16 and later. 9.x said 14 or later, but nothing older than 16 was tested ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#run-postgresql-16-or-later)).
- EF Core: `Any` and `Count` over a captured list of ranges return the in-memory answer, where a list with overlapping, adjacent or empty ranges answered differently. `EF.Parameter(list)` there throws ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-any-and-count-over-a-captured-list-of-ranges)).

### Added

- **EF Core: `Float64Range` and `Float32Range` map to custom `float8range` and `float4range` types** through `HasPostgresRange` and `EnableUnmappedTypes()`, and their sets to `double precision[]` and `real[]` ([Custom range types](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#custom-range-types)).
- **EF Core: `CompareTo` against 0** on a range or multirange runs as `<`, `<=`, `>`, `>=` or `=` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#query-ranges-with-linq)).
- **EF Core: a range with a multirange operand** runs as the operator with the range on the left, and `RangeSet.IsContainedBy` as `<@` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#query-ranges-with-linq)).
- **EF Core: `RangeAgg` and `RangeIntersectAgg` over a multirange column** inside a `GroupBy` projection ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#read-bounds-merge-ranges-and-aggregate)).
- **Docs: PostgreSQL 18 temporal keys over a range column** (`WITHOUT OVERLAPS`, `PERIOD`), declared through `EFCore.ComplexIndexes.PostgreSQL` ([Temporal keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#reject-overlapping-rows-with-a-temporal-key-postgresql-18)).
- **`Gap`**, **`Gaps()`** and **`Gaps(window)`** run as SQL: `range_merge` over a multirange difference for `Gap`, and a multirange difference for `Gaps` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#read-bounds-merge-ranges-and-aggregate)).

### Fixed

- EF Core: `Any` or `Count` with a predicate over an empty captured list of value sets, or of ranges beside a `Contains`, returns `false` or `0`, where it threw `InvalidOperationException` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- Docs: a database query over a `NaN` or infinite float another tool stored answers in PostgreSQL's order, and a CHECK constraint keeps such values out ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#keep-nan-and-infinity-out-of-a-float-column)).

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

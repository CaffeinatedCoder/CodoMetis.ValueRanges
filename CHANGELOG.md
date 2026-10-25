# Changelog

All notable changes to CodoMetis.ValueRanges and its packages.

The four packages share one version number (the `Version` property in `Directory.Build.props`) and
are released together, so a version appears here even when a package saw no change in it. Each
package also has its own `CHANGELOG.md` with the entries that affect it. Versions follow
[Semantic Versioning](https://semver.org/). Entries are newest first.

A version's section is the GitHub release's notes. Each line is one change: what happens now, what
to do about it, and a link to the guide with the detail. [CONTRIBUTING.md](CONTRIBUTING.md#changelog)
has the rules.

## [11.0.0-preview.1] — 2026-10-19

The first preview of the 11 line: the same public API as 10.0.0, on .NET 11. Nothing was removed or renamed. The 10.x line stays on .NET 10 ([Branches and releases](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CONTRIBUTING.md#branches-and-releases)).

### Breaking

- All four packages target `net11.0` and need the .NET 11 runtime, one target framework per line; the EF Core packages reference EF Core 11 and `Npgsql.EntityFrameworkCore.PostgreSQL` 11 (release candidates while .NET 11 is in preview). PostgreSQL 16 or later, as in 10.x.
- Ranges: `RangeAgg()` and `RangeIntersectAgg()` skip a `null` range or set, as `range_agg` and `range_intersect_agg` skip a NULL row. With a `null` after another range, `RangeIntersectAgg()` returned `empty`, or threw after `(,)`, and `RangeAgg()` threw unless an input was `(,)` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-rangeagg-and-rangeintersectagg-over-null-ranges)).
- Ranges: `RangeSet.From`, a collection expression and `RangeSet.Create` throw `ArgumentException` for a `null` range, where they built a set holding `null`, whose `IsEmpty()` was `false`. `Union` and `Except` called on a `null` range throw too ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#keep-null-out-of-a-range-set)).
- Ranges: a predicate called on a `null` range, or given a `null` range or set, returns `false`, as a query does over a NULL column. `Contains(Empty)`, `IsContainedBy((,))` and `(,).Contains(null)` returned `true`, and `Contains(T)` and a `null` set operand threw ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-predicates-over-a-null-range-or-a-null-column)).
- EF Core: `EFCore.ComplexIndexes.PostgreSQL` 5.x stays on EF Core 10 and can't be installed beside the 11 packages. Its EF Core 11 successor, `CodoMetis.EFCore.SchemaKit.PostgreSQL`, isn't on NuGet yet ([CaffeinatedCoder/EFCore.ComplexIndexes#38](https://github.com/CaffeinatedCoder/EFCore.ComplexIndexes/issues/38)). For exclusion constraints until then, stay on 10.x or add the constraint with explicit SQL. Temporal keys are Npgsql's own on EF Core 11 ([Exclusion constraints](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#reject-overlapping-rows-with-an-exclusion-constraint)).
- EF Core: `LowerBound()` and `UpperBound()` in a query are `NULL` for an empty range or set and on an unbounded side, as in memory. So `== null` selects those rows, where it selected none on a required column, and `?? fallback` returns the fallback, where the row was missing ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-bound-comparisons-with-null-in-queries)).
- EF Core: a range, multirange or value set predicate over a NULL column is `false`, so its negation keeps the row, as EF Core does for `string` and `string[]`. `!b.Period.Overlaps(p)` dropped the NULL rows, and a projected predicate threw ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-predicates-over-a-null-range-or-a-null-column)).
- EF Core: `Contains(T)` on a range or multirange is `false` in a query when the element is NULL: a column of a missing child in an outer join, or a nullable column read with `.Value`. Its negation dropped the row, and a projection threw ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-predicates-over-a-null-range-or-a-null-column)).
- EF Core: `CreateFinite`, `CreateUnboundedStart` and `CreateUnboundedEnd` over a NULL element are NULL in a query, as EF Core's own `AddDays` is. They built `empty` or `(,)`, so `Contains(v)` selected a row without the element ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-predicates-over-a-null-range-or-a-null-column)).
- EF Core: a value set `Union` with a NULL set, or `Remove` with a NULL set or element, is NULL in a query, as `Where(… == null)` already said. A `Select` loaded the other set instead, for a nullable column, a missing outer-join child or a `null` parameter ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-value-set-union-and-remove-over-null-in-queries)).
- EF Core: any `Compare(x, y)` over ranges or sets, `RangeLowerBoundComparer.Compare` and every other `IComparer` included, runs in memory only in a query, as the method called. A filter or an ordering fails translation. In SQL it compared in the range order, so `Compare(r.Period, p) == 0` missed rows with the same lower bound ([#464](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/464), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#move-rangelowerboundcomparer-out-of-queries)).

### Added

- EF Core: Npgsql 11's own temporal keys accept the range and multirange columns this package maps. `HasKey(…).WithoutOverlaps()` and `WithPeriod()` foreign keys work over a `DateRange`, a `TimeRange` or a `RangeSet`, on PostgreSQL 18 with `SetPostgresVersion(18, 0)` ([Temporal keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#reject-overlapping-rows-with-npgsqls-temporal-keys-postgresql-18)).
- EF Core (NodaTime): Npgsql 11's own temporal keys accept `YearMonthRange` and the NodaTime range and multirange columns. `HasKey(…).WithoutOverlaps()` and `WithPeriod()` foreign keys work over them on PostgreSQL 18 with `SetPostgresVersion(18, 0)` ([Temporal keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#reject-overlapping-rows-with-npgsqls-temporal-keys-postgresql-18)).
- EF Core: `Length` on `Int32Range` and `Int64Range` runs as SQL, so a query can filter and sort by it. It counts the integers as memory does: 0 for the empty range, and `null` for an unbounded side or, on `Int64Range`, a count larger than `long.MaxValue` ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- EF Core: `Clamp` on `Int32Range`, `Int64Range` and `DecimalRange` runs as SQL, so a query can filter and sort by the clamped value. It answers as memory does: the value inside the range, otherwise the bound it passes, and `null` for the empty range or a `null` range or value ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- EF Core: `DateRange.Month`, `Quarter`, `Year` and `IsoWeek` run as SQL when they read a column, so `DateRange.Month(b.Day)` works in a `Where` and an `OrderBy`. They build the range memory builds, the extremes included, and an argument memory rejects fails in PostgreSQL too. The NodaTime forms stay in memory ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).
- Docs: the guides, the package READMEs, the changelog and an API reference are published as a [documentation site](https://caffeinatedcoder.github.io/CodoMetis.ValueRanges/) with one set of pages per line. It opens on the stable 10.x line, and the 11.x previews are under `/11.x/`.
- Docs: a [walkthrough](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/walkthrough.md) follows one booking through a minimal API and PostgreSQL, from the request to the stored row and back, and says what changes at each step. CI runs its sample against the packages, so the responses and SQL on the page stay true.

### Changed

- EF Core: an `ExecuteUpdate` that sets a nullable value set inside a JSON column to a value that may be null is refused for the JSON array alone; Npgsql 11 writes the null outcome as JSON null, so the message no longer names a whole-document NULL ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#put-a-range-or-set-inside-a-json-column)).
- EF Core: an empty captured list of ranges or sets reads no rows through EF Core 11's own `SELECT … WHERE FALSE`; the SQL differs from 10.x, the answers do not ([Captured lists](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- Docs: each package's Project website link on NuGet opens the [documentation site](https://caffeinatedcoder.github.io/CodoMetis.ValueRanges/), and its README links the site and the [walkthrough](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/walkthrough.md).

### Fixed

- Docs: [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where), the READMEs and the XML docs give each in-memory operation's real reason. `Shift` and `Expand` keep a bound at `infinity`, so that reason was stale. `Length`, `Values`, `Split`, the bridge conversions, `Clamp`, `Intersect` and `Except` would take composed SQL, not a missing operator. In-memory operations run in the final `Select` only.
- Docs: the [What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where) tables list every public member a query can call. `Equals` runs as `=`. `Values` on a value set, `Count` and the indexer on a `RangeSet`, `From`, `Parse`, the NodaTime conversions, `ToString`, `GetHashCode` and `GetEnumerator` run in memory.
- Value sets: every set type implements `IFormattable` itself, so `ToString()` and formatting no longer run the interface's default, whose shared-generic stub traps under Native AOT with ILCompiler 11 RC2 ([Native AOT](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).
- Docs: an ordering by `LowerBound()` or `UpperBound()` puts the empty rows and those without that bound where PostgreSQL puts NULLs, last ascending and first descending, also on a required column. A `!= null` key in front sorts them as memory does ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#read-bounds-merge-ranges-and-aggregate)).
- EF Core: an empty captured list of ranges or sets keeps EF Core's ordering column (`_ord`, behind `FirstOrDefault` over the list) an integer, as it is when the list has elements. It was cast to the range or set type. No answer changed, but comparing that column with an integer would have failed for the empty list ([Captured lists](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).

## [10.2.0] — 2026-10-25

Some calls answer differently: [Changed answers in 10.2.0](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020).

### Changed answers

- Ranges: `RangeAgg()` and `RangeIntersectAgg()` skip a `null` range or set, as `range_agg` and `range_intersect_agg` skip a NULL row. With a `null` after another range, `RangeIntersectAgg()` returned `empty`, or threw after `(,)`, and `RangeAgg()` threw unless an input was `(,)` ([#363](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/363), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).
- Ranges: `RangeSet.From`, a collection expression and `RangeSet.Create` throw `ArgumentException` for a `null` range, where they built a set holding `null`, whose `IsEmpty()` was `false`. `Union` and `Except` called on a `null` range throw too ([#357](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/357), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).
- Ranges: a predicate called on a `null` range, or given a `null` range or set, returns `false`, as a query does over a NULL column. `Contains(Empty)`, `IsContainedBy((,))` and `(,).Contains(null)` returned `true`, and `Contains(T)` and a `null` set operand threw ([#357](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/357), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1020)).
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

One call answers differently, on the .NET 11 runtime only: [Changed answers in 10.1.0](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1010).

### Changed answers

- Literals: a time, `DateTime` or `DateTimeOffset` element with an hour of 24 throws `FormatException` on the .NET 11 runtime too, where it returned midnight: `{24:00:00}` read as `{00:00:00}` and `T24:00:00` as the next day. On .NET 10 it always threw ([#300](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/300), [Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#changed-answers-in-1010)).

### Fixed

- Value sets: `ToString` on every value set, in the core and NodaTime packages, runs in a `net11.0` Native AOT consumer, where the process ended with `SIGTRAP` on the first call ([Native AOT](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).
- EF Core: `Any` or `Count` over an empty captured list of ranges or value sets returns `false` or `0` on EF Core 10.0.2 and later, where it failed with `42883: operator does not exist: text && int4range`. The stopgap `AppContext.SetSwitch("Microsoft.EntityFrameworkCore.Issue37216", true)` also brings back EF Core's own failure on empty lists of other types ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- EF Core: `RangeSet.Contains` translates on the EF Core 10 patch that carries [dotnet/efcore#37081](https://github.com/dotnet/efcore/issues/37081), where every overload threw `ArgumentException` inside EF Core before translation. That patch, planned as 10.0.15, was not published when 10.1.0 shipped. Its stopgap `AppContext.SetSwitch("Microsoft.EntityFrameworkCore.Issue37081", true)` also gives up EF Core's own fix for `IReadOnlySet` parameters, which inline their values again ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#query-ranges-with-linq)).
- EF Core: `ExecuteUpdate` of a value set inside a JSON column is refused naming the property (`'Doc.Tags'`) on Npgsql EF 10.0.3 with `SetPostgresVersion(16, 0)` or higher, where it was refused naming the column (`'Doc'`), and the refusal no longer says that a value that may be `null` writes `NULL` as the whole document ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#put-a-range-or-set-inside-a-json-column)).
- EF Core: `a == b ? null : a` over a value set `Union` computed on the server, which EF Core writes as `NULLIF(a, b)`, is refused as value set equality, where it was refused naming no operation ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#combine-sets-with-union)).

## [10.0.0] — 2026-10-19

Nothing was removed or renamed. Some calls answer differently, and text PostgreSQL refuses is refused here too: [Migration from v9.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v9x).

### Breaking

- Ranges: `IsUnboundedStart()` and `IsUnboundedEnd()` return `true` for the infinite range `(,)`, as PostgreSQL and `RangeSet` do, where they returned `false` in memory. Match `IUnboundedStartRange<T>` or `IUnboundedEndRange<T>` for the half-unbounded shape alone ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-isunboundedstart-and-isunboundedend-on-the-infinite-range)).
- Ranges: `RangeSet.Overlaps` on a continuous domain returns `true` when the operand ends where a later element starts and shares values with an earlier one, as `&&` does. `{[1,4),[5,8)}.Overlaps([3,5))` returned `false` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-rangesetoverlaps-on-a-continuous-element-type)).
- Ranges: a record derived from a range type throws `InvalidOperationException` when it is constructed, where it compiled and `Contains` and `Overlaps` answered `false` for it ([Pattern matching](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#why-the-discard-arm-must-throw)).
- Value sets: every core wrapper set checks that an element's text form reads back as the same element, and throws if it doesn't, where the element was stored as a different value ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#keep-the-text-form-of-the-underlying-type)).
- Literals: integer or decimal text with a NUL throws `FormatException`, in a range, a set and JSON, where a trailing NUL was ignored and `31\0` read as 31 ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#keep-nul-out-of-string-elements)).
- Literals: `StringSet` and `StringSet<T>` refuse an element holding a NUL: `From` and `Add` throw `ArgumentException` and parsing fails, where the set was built and failed when saved ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- Literals: `GuidSet` and `GuidSet<T>` read PostgreSQL's `uuid_in` grammar. `{6f96-19ff-8b86-d011-b42d-00c0-4fc9-64ff}` parses, where it threw; `{(6f9619ff-…)}` and whitespace inside a quoted element throw `FormatException`, where they parsed ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- Literals: a `DateTimeOffset` offset other than `Z`, `±HH`, `±HH:mm` or `±HH:mm:ss` throws `FormatException`, where `+0530` and `+5:30` were read ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#write-temporal-text-as-strict-iso-8601)).
- Literals: decimal text with more precision than `decimal` holds, such as a 29th significant digit or `1e-30`, throws `FormatException`, where it was rounded ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).
- Literals: a range or multirange literal whose lower bound is above its upper bound, such as `[5,1)`, throws `FormatException`, where it read as `empty`. `CreateFinite` still returns `Empty` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).
- JSON: the integer, decimal and Guid wrapper sets read only the JSON their plain sets read: a quoted number needs `AllowReadingFromString`, and a Guid is hyphenated ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#how-your-element-types-sort-and-serialize)).
- ASP.NET Core: a minimal-API parameter typed as one shape, such as `DateRange.Finite`, answers 400 for a literal of another shape, where it bound `null` and ran the handler ([Binding](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#bind-a-range-from-configuration)).
- EF Core: the packages support PostgreSQL 16 and later. 9.x said 14 or later, but nothing older than 16 was tested ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#run-postgresql-16-or-later)).
- EF Core: `Any` and `Count` over a captured list of ranges return the in-memory answer, where a list with overlapping, adjacent or empty ranges answered differently. `EF.Parameter(list)` there throws ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#check-any-and-count-over-a-captured-list-of-ranges)).

### Added

- **`Float64Range`, `Float64Set` and `Float64Set<T>`** over finite `double` values. `NaN` and the infinities throw, and the text is what PostgreSQL's `float8out` prints ([Why doubles are finite only](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md#why-doubles-are-finite-only)).
- **`Float32Range`, `Float32Set` and `Float32Set<T>`** over finite `float` values, with the text `float4out` prints. There is no `Half` family, as PostgreSQL has no two-byte float ([Why doubles are finite only](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md#why-doubles-are-finite-only)).
- **`IComparable` on every range type and on `RangeSet`**, in PostgreSQL's range and multirange order: empty first, then by lower bound, then by upper bound ([Sort ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#sort-ranges)).
- **`Parse` and `TryParse` on every shape** (`Int32Range.Finite.TryParse`, …), which accept only a literal of that shape ([Binding](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#bind-a-range-from-configuration)).
- **A range with a `RangeSet` operand** for `Contains`, `Overlaps`, `IsAdjacentTo` and the other range operators, and **`RangeSet.IsContainedBy`** ([Compare a range with a set](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#compare-a-range-with-a-set)).
- **`RangeAgg` and `RangeIntersectAgg` over a sequence of `RangeSet`**, as PostgreSQL's `range_agg` and `range_intersect_agg` over multiranges ([Aggregates](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#aggregate-a-sequence-of-ranges)).
- **`Gap`** between two ranges, and **`Gaps()`** and **`Gaps(window)`** on a `RangeSet`, in memory and in EF Core queries ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#find-the-gap-between-two-ranges)).
- **`Shift`** and **`Expand`** on every range type, which move a range by a step or grow it on both sides, in memory only ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#move-or-widen-a-range)).
- **`Split`** on every range type, by a step or at calendar period boundaries, and **`Values(step)`** on the discrete types, in memory only ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#split-a-range-into-pieces)).
- **`DateRange.Month`**, **`Quarter`**, **`Year`** and **`IsoWeek`**, the same on `LocalDateRange`, and `Quarter` and `Year` on `YearMonthRange`. Weeks are ISO 8601 only, whatever the culture ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#create-a-month-a-quarter-a-year-or-a-week)).
- **EF Core: `Float64Range` and `Float32Range` map to custom `float8range` and `float4range` types** through `HasPostgresRange` and `EnableUnmappedTypes()`, and their sets to `double precision[]` and `real[]` ([Custom range types](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#custom-range-types)).
- **EF Core: `CompareTo` against 0** on a range or multirange runs as `<`, `<=`, `>`, `>=` or `=` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#query-ranges-with-linq)).
- **EF Core: a range with a multirange operand** runs as the operator with the range on the left, and `RangeSet.IsContainedBy` as `<@` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#query-ranges-with-linq)).
- **EF Core: `RangeAgg` and `RangeIntersectAgg` over a multirange column** inside a `GroupBy` projection ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#read-bounds-merge-ranges-and-aggregate)).
- **Docs: PostgreSQL 18 temporal keys over a range column** (`WITHOUT OVERLAPS`, `PERIOD`), declared through `EFCore.ComplexIndexes.PostgreSQL` ([Temporal keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#reject-overlapping-rows-with-a-temporal-key-postgresql-18)).

### Fixed

- Literals: integer text reads PostgreSQL 16's `0x`, `0o` and `0b` prefixes and underscores between digits, where `[0x1F,)` and `{1_000}` threw `FormatException` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#parse-a-literal)).
- Literals: value sets read a dimension decoration such as `[0:1]={1,2}`, and `{{}}` as `{}` as PostgreSQL 17 does, where both threw `FormatException` ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- Literals: `DateTime` and `DateTimeOffset` text reads an offset up to ±15:59:59, as PostgreSQL does, where `+15:00` threw `FormatException` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#dates-and-times-are-strict-iso-8601)).
- EF Core: `Any` or `Count` with a predicate over an empty captured list of value sets, or of ranges beside a `Contains`, returns `false` or `0`, where it threw `InvalidOperationException` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- NodaTime: `LocalDateTimeRange` and `LocalDateTimeSet` read `Z` or an offset and keep the wall clock, where they threw. `InstantRange` and `InstantSet` refuse an offset beyond ±15:59:59 ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#dates-and-times-are-strict-iso-8601)).
- Docs: an exclusive `infinity` date bound excludes the date extreme, so `[2026-01-01,infinity)` is `[2026-01-01,9999-12-30]` ([Domain width](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/architecture.md#domain-width-and-the-boundary)).
- Docs: a `YearMonthRange` column holding an exclusive `infinity)` or `(-infinity` throws when it loads. Ask other tools to write an absent or inclusive bound ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#rows-written-by-other-tools)).
- Docs: a temporal literal with more than six fraction digits can be 1 µs from what PostgreSQL reads and the EF Core package stores ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#dates-and-times-are-strict-iso-8601)).
- Docs: a database query over a `NaN` or infinite float another tool stored answers in PostgreSQL's order, and a CHECK constraint keeps such values out ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#keep-nan-and-infinity-out-of-a-float-column)).

## [9.1.0] — 2026-10-17

### Added

- EF Core: `wanted.Contains(b.Tags)` over a captured list, array or other collection of value sets runs as `IN (@p1, @p2, …)`, one parameter per set, and returns the same rows as in memory. It threw `InvalidCastException` in Npgsql. `EF.Parameter(wanted)` throws and says to remove it ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-sets)).
- EF Core: `wanted.Contains(b.Seats)` over a captured list, array or other collection of ranges runs as `IN (@p1, @p2, …)`, one parameter per range, and returns the same rows as in memory. It failed with `42809`, and with `InvalidCastException` for `TimeRange`. `EF.Parameter(wanted)` there throws and says to remove it ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#match-a-column-against-a-list-of-ranges)).

### Fixed

- Literals: whitespace at either end of a quoted or escaped range bound is dropped, as in PostgreSQL, so `[" 2026-01-01",2026-02-01)` parses, where it threw `FormatException` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: a date or time value set drops whitespace at either end of a quoted or escaped element, as PostgreSQL does, so `DateSet.Parse("{\" 2026-01-01\"}", null)` is `{2026-01-01}`, where it threw `FormatException`. A string set keeps the whitespace, as before ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#parse-and-print-literals)).
- EF Core: `ExecuteUpdate` setting a value set inside a `ToJson()` column to `null` is still refused, now for its own reason: a `NULL` there would turn the whole document into `NULL`. The message blamed the JSON array a value would write. Set it with `SaveChanges`, or replace the whole JSON property ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#put-a-range-or-set-inside-a-json-column)).

## [9.0.0] — 2026-10-11

Nothing was removed or renamed. What existing calls write, accept and translate changed, and the compiler can't show you where: [Migration from v8.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x).

### Breaking

- JSON: every range, variant, `RangeSet` and value set writes its literal with nothing registered (`"[2026-01-01,2026-06-30]"`, `["a","b"]`), where it wrote a property dump such as `{"Length":181}` that nothing could read back. `AddRangeConverters()` is now redundant, and a converter on the options or on a property still wins ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- JSON: a converter you register on the options for a range type also handles values serialized through `object` or declared as a variant, which wrote the literal before ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- JSON: a property declared as `IRange<T>`, a shape interface or `IValueSet<T>` writes its runtime type's literal, where it wrote `{}` or a property dump. Reading one throws `NotSupportedException`, so declare the concrete type ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: `Parse` and `TryParse` ignore the format provider, and formatting without a format string, or with `""`, writes the invariant literal whatever the provider. `th-TH` read `2026-01-01` as the year 1483. To read culture-formatted text, parse the bounds yourself and use the factories ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: interpolating an `Int32Range`, `Int64Range` or `DecimalRange` is invariant under every culture. Under `de-DE`, `$"{range}"` wrote `[1,5,2)`, which parsed back as `[1,52)` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: temporal bounds and elements are strict ISO 8601 in `Parse`, `TryParse`, JSON, the `TypeConverter` and configuration binding. Dotted, slashed and named-month dates, a date alone for a date-time, and a `DateTimeOffset` without an offset throw `FormatException` (`[01.02.2027,05.03.2027)` read as January 2 to May 2). `HH:mm`, a space before the time and `infinity` still parse ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: a wrapper set parses element text as its underlying type first and hands your element the round-trip form, so a lenient element `Parse` no longer sees other text. An element that ignores the format specifier fails on read, where it was truncated ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- Literals: a closed temporal set reads its JSON elements with the same ISO grammar, unless you register a converter for the element type. System.Text.Json's own converter read an offset-less `DateTimeOffset` in the machine's offset ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: a `DateTime` bound or element with an offset keeps its wall clock as `Local` (`2026-01-01T09:30+05:30` is 09:30 on every machine), where it was converted into the parsing machine's zone. If you mean instants, use `DateTimeOffsetRange`, or convert with `ToUniversalTime()` first ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- Literals: decimal bounds and elements use PostgreSQL's numeric grammar, so `"1,000"`, `"(5)"` and currency symbols throw `FormatException`. This covers `DecimalRange`, `DecimalSet`, the numeric wrapper sets and their EF Core bridge (`DecimalSet<Price>.Parse("{\"1,5\"}")` was `{15}`) ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- Literals: the grammar is PostgreSQL's. A quoted-empty or blank range bound throws, where it meant unbounded (`["",5)` was `(,4]`). A backslash escapes outside quotes too (`{a\,b}` is the one element `a,b`). `{{a,b}}`, `{a}b}` and `{a"b}` throw, and only ASCII whitespace is skipped ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- NodaTime: a bare `LocalDate`, `LocalDateTime` or `YearMonth` in a non-ISO calendar throws `ArgumentException` on write, and so does such an element of `LocalDateSet<T>`, `LocalDateTimeSet<T>` or `YearMonthSet<T>`, in memory and as an EF Core operand. Both were stored thirteen days off. Convert with `WithCalendar(CalendarSystem.Iso)` first ([NodaTime](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.NodaTime/README.md)).
- NodaTime: `AddNodaTimeRangeConverters()` no longer outranks `ConfigureForNodaTime` when registered first. Any other converter for the type wins, in either order ([JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#element-converters)).
- EF Core: a query that builds on a server-computed value set `Union` throws `InvalidOperationException`, where it answered differently from memory: equality in a projection, `?:` or `??` around a union, `GroupBy`, `Distinct`, `OrderBy`, set operations, `Count` through `??`, and `ExecuteUpdate` to a union. Use `IsSubsetOf` and `IsSupersetOf`, or materialize first ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- EF Core: if you replace `IRelationalParameterBasedSqlProcessorFactory` after `UseValueRanges()`, every query with a server-side `Union` fails with `valueranges_union_without_sql_guard`. Wrap the registered factory instead ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).
- EF Core: temporal SQL constants carry the value Npgsql binds, truncated to the microsecond toward 2000-01-01, with the extremes as `-infinity` and `infinity`. A constant now matches the row a parameter matched, and a temporal set whose elements are less than a microsecond apart is stored with one of them ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v8x)).

### Added

- **A `TypeConverter` on every type**, so configuration binding reads `"Period": "[2026-01-01,2026-06-30]"`, where it bound `null` with no error. It is always invariant, an empty string is `null`, and a variant rejects a literal of another shape ([TypeConverter](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **Native AOT and trimming**: the core and NodaTime packages build with `IsAotCompatible`, and `RangeSet` and the wrapper sets no longer fail with "missing native code". A source-generated context lists a value set's element type as well as the set ([Native AOT](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).
- **Ranges and multiranges as dictionary keys**, written as the literal (`{"[2026-01-01,2026-06-30]":181}`), where they threw `NotSupportedException`. `DictionaryKeyPolicy` is not applied, and two spellings of one key collide; set `AllowDuplicateProperties = false` to reject that ([Dictionary keys](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **`Deconstruct` on every bounded variant**: `var (start, end) = finite` on a discrete type, and `(start, end, startInclusive, endInclusive)` on a continuous one ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **NodaTime: `infinity` and `-infinity`** parse as the extremes of `Instant`, `LocalDate` and `LocalDateTime`, the ones Npgsql maps them to ([NodaTime](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges.NodaTime/README.md)).
- **Docs: an OpenAPI schema transformer** to copy into your API project. It describes ranges and multiranges as `type: string` with a pattern and value sets as arrays, where ASP.NET Core wrote `{}` ([OpenAPI](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#openapi)).

### Changed

- The range interfaces (`IRange<T>`, the shape interfaces and `IRangeFactory<TRange, T>`) are not an extension point. Implementing them outside the library is unsupported, and they may gain members in a minor version ([Architecture](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/architecture.md)).

### Fixed

- EF Core: a range, multirange or value set inside a `ToJson()` column saves, where `SaveChanges` threw `NullReferenceException`, and its operators translate (`CAST(doc ->> 'Period' AS daterange) @> @day`). The document holds the store's text, a persisted format: `"{a,b}"` for a value set, month-aligned dates for a `YearMonth` type. A compiled model works; `--nativeaot` doesn't yet ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- EF Core: `ExecuteUpdate` of a value set inside a `ToJson()` column throws, however the setter reaches it, because EF Core would store a JSON array the row could no longer load from. Setting the whole complex property still works ([JSON columns](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).
- EF Core: a range built in a query from values that don't depend on it, such as `r.Period == DateRange.CreateFinite(…)`, translates, where it threw "does not have a type mapping assigned".
- EF Core: a temporal range's element operand gets the range's own mapping. `DateTimeOffsetRange.Contains(dto)` threw for any non-UTC offset, and `DateTimeRange.Contains` for a UTC `DateTime`.
- EF Core: a `LocalDateRange` or `LocalDateTimeRange` element operand in another calendar is converted to ISO, where it missed its row or threw inside Npgsql.
- EF Core: a `DateTimeSet<T>` element keeps its wall clock on the way to the column, even one the machine's time zone skips.
- NodaTime: `HH:mm` parses for a `LocalTime`, the time of a `LocalDateTime`, and an `Instant` with its offset or `Z`, as the BCL types parse it.
- `DecimalSet<T>` reads exponent notation (`{1e3}`, a JSON `1.5e1`), as `DecimalSet` does.

## [8.0.0] — 2026-08-17

No signature changed. Some existing calls answer differently, and one query that ran now throws: [Migration from v7.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v7x).

### Breaking

- EF Core: `==`, `!=` and `Equals` over a server-computed value set `Union` fail translation in a filter, where they returned wrong rows: `array_cat` keeps duplicates and the operands' order. Use `Contains`, `Overlaps`, `IsSubsetOf` or `IsSupersetOf`, or call `AsEnumerable()` first ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v7x)).
- `RangeSet.Except(TRange)` with an infinite range returns the empty set, where it returned the whole domain, and `Complement()` of the infinite set does too.
- `YearMonthRange.NextValueAfter` and `PreviousValueBefore` reject a non-ISO calendar, where they stepped in it and returned a value the type's constructors reject.
- `RangeSet.Contains(T)` on the infinite set returns `true`, where it threw `InvalidOperationException`. `LocalDateRange`'s step functions return `null` at a Gregorian `9999-12-31`, where they threw. Only code that caught these exceptions is affected.

### Added

- **Docs: a getting-started guide**, and the migration guide covers v7 and v8 ([Getting started](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/getting-started.md)).

### Changed

- An `IRange<T>` that is none of the five shapes makes `IRangeFactory.ToString` throw, where it was formatted as `"empty"` and stored as the empty range.
- A shape dispatch with no case for a pair of shapes throws `UnreachableException` naming the pair, instead of returning a value. No reachable call changed.
- EF Core: the wrapper-set bridge rejects an element type it doesn't know, instead of writing its `ToString()`.

### Fixed

- Docs: a switch over the five range shapes needs a `_` arm, which should throw. The docs said the compiler checks the cases, and the example failed to build with CS8509 ([Pattern matching](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- Docs: the value set table lists a wrapper set for every family, and the EF Core README's links reach `docs/efcore.md`.

## [7.0.0] — 2026-08-17

Nothing was removed or renamed. Three range fixes change what existing calls answer in memory: [Migration from v6.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x).

### Breaking

- `Contains` and `IsContainedBy` treat the empty range as contained by every range, as PostgreSQL's `@>` does, so `[1,5].Contains(Int32Range.Empty)` is `true`. For the old meaning, write `outer.Contains(inner) && !inner.IsEmpty()` ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x)).
- `IsStrictlyLeftOf` and `IsStrictlyRightOf` give PostgreSQL's `<<` and `>>` answer for a range unbounded at its start: `(,5]` is left of `[10,20]`, where it returned `false`. A one-element `RangeSet` changes with it.
- `Except` subtracts when the two ranges are unbounded in opposite directions: `(,5].Except([1,))` is `{(,0]}`, where it returned `(,5]` unchanged. `RangeSet.Except` changes with it.
- JSON: `Int16Set<T>`, `Int32Set<T>`, `Int64Set<T>` and `DecimalSet<T>` honour `JsonNumberHandling.WriteAsString` and write strings, as their primitive sets do. Reads are unchanged ([Migration](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v6x)).
- `DecimalRange.Length` returns `null` for a span wider than `decimal`, where it threw `OverflowException`.

### Added

- **A wrapper set for every family**: `Int16Set<T>`, `DecimalSet<T>`, `DateSet<T>`, `TimeSet<T>`, `DateTimeSet<T>` and `DateTimeOffsetSet<T>`, and in the NodaTime package `LocalDateSet<T>`, `LocalDateTimeSet<T>`, `InstantSet<T>`, `LocalTimeSet<T>` and `YearMonthSet<T>`, each mapped by EF Core to its array column. `DecimalSet<T>` writes JSON numbers with their scale ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **`SetTypeRegistry.RegisterFamily`**, for a package that adds wrapper set families.

### Changed

- The temporal wrapper sets ask their elements for a round-trip format (`yyyy-MM-dd` for `DateSet<T>`, `O` for the others, the ISO pattern in NodaTime), so a hand-written element must pass its `format` argument on. One that ignores it is rejected when stored ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- JSON: a temporal wrapper set writes the same value as its primitive set but not the same bytes: seven fraction digits, and `+` escaped. Give the element its own `[JsonConverter]` if the response shape matters ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- EF Core: `DateTimeSet<T>` and `DateTimeOffsetSet<T>` normalize as their closed sets do: `Unspecified` for `timestamp`, UTC for `timestamptz`.
- EF Core docs: `Count` on a value set, like `==`, assumes every row is canonical. A row another tool stored as `{b,a,b}` counts 3 in SQL and loads as `{a,b}`. No behaviour changed ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).

### Fixed

- EF Core: `Int64Range.Contains(25)` with a constant runs, where PostgreSQL rejected it (`42883: operator does not exist: int8range @> integer`). The constant now carries `::bigint`, and no other SQL changed.

## [6.3.0] — 2026-08-16

### Added

- **`RangeSet.IsInfinity()` and `IsFinite()`**. `IsInfinity()` is true only for the set that covers the whole domain, which `{(,5],[10,)}` is not, though it is unbounded at both ends. Both translate ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **Collection expressions for `RangeSet<TRange, T>`** (`RangeSet<Int32Range, int> set = [a, b];`), normalized as `From` normalizes. Also `From(params ReadOnlySpan<TRange>)` and the builder `RangeSet.Create<TRange, T>` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`ISpanParsable<T>`** on every range type, `RangeSet` and every value set: `Parse` and `TryParse` over a `ReadOnlySpan<char>` ([Literals](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)).
- **`Length`** on every range: a count for a discrete type, both ends included, and a span for a continuous one. It is `0` for the empty range and `null` when unbounded, and runs in memory only ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`Values()`** on the discrete ranges (`Int32Range`, `Int64Range`, `DateRange`, `LocalDateRange`, `YearMonthRange`) lists the values in order. An unbounded range throws at the call ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **`ToRangeSet()`** on `Int32Set`, `Int64Set`, `DateSet`, `LocalDateSet` and `YearMonthSet`, and `ToInt32Set()`, `ToDateSet()` and the others back: `{1,2,3,7}` becomes `{[1,3],[7,7]}`. In memory only ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **`Clamp(value)`** on every range: the contained value nearest the argument, or `null` for the empty range ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- **An indexer** on every value set (`set[0]`).
- **`IRangeFactory<TRange, T>.IsDiscrete`**, for generic code that needs to know whether the element type has a step.
- **EF Core: `RangeSet.IsFinite()` and `IsInfinity()` translate**, to `NOT lower_inf(x) AND NOT upper_inf(x) AND NOT isempty(x)` and to equality with `'{(,)}'` ([What runs where](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#what-runs-where)).

## [6.2.1] — 2026-08-16

### Breaking

- `IsAdjacentTo` returns `true` for an unbounded range next to its neighbour, as PostgreSQL's `-|-` does: `(,0].IsAdjacentTo([1,3])` returned `false`. `RangeSet.From` and `Union` now merge such neighbours, so `From([(,0], [1,)])` is `{(,)}` ([Ranges](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)).
- A literal that fails to parse, through `Parse` or JSON, quotes at most 64 characters of the input and its length, and no longer chains the element parser's exception. If you match the message or read `InnerException`, update that. Exception types are unchanged.

## [6.2.0] — 2026-08-16

### Breaking

- JSON: a `null` range reads back as `null`, where it threw `JsonException`. If you relied on that to reject a null, validate it yourself. `null` and `"empty"` stay different.

### Fixed

- EF Core: `Count` over `Union(…).Remove(x)` fails translation in a filter and runs in memory in a projection, where it counted shared elements twice.

## [6.1.0] — 2026-08-15

### Breaking

- JSON: a wrapper set whose element has no `[JsonConverter]` writes the underlying values (`["users.read"]`, `[1,2]`), where it wrote objects such as `[{"Value":"users.read"}]` or `[{}]`. Reads run the element's `Parse`, and a registered converter still wins ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).

### Added

- **`IValueSetFactory<TSet, T>.ElementJsonConverter`**, the element converter a set uses when none is registered.
- **`RangeVariantJsonConverter<TVariant, TRange, T>`**.
- **NodaTime: `AddNodaTimeRangeConverters()`**, for bare NodaTime values next to a set. It works before or after `ConfigureForNodaTime`.

### Fixed

- JSON: the NodaTime sets write ISO 8601 elements, where they wrote objects and read back `default`.
- JSON: a `null` range property is written as `null`, where it threw `NullReferenceException`. Reading `null` still throws; write `"empty"` for the empty range.
- JSON: a range serialized through `object` works, where it threw `ArgumentException`.

## [6.0.0] — 2026-08-14

Value sets, a second type family. No breaking changes.

### Added

- **Value sets**: `StringSet`, `GuidSet`, `Int16Set`, `Int32Set`, `Int64Set`, `DecimalSet`, `DateSet`, `TimeSet`, `DateTimeSet` and `DateTimeOffsetSet`, deduplicated and sorted, with no nulls. The wrapper sets `StringSet<T>`, `GuidSet<T>`, `Int32Set<T>` and `Int64Set<T>` hold your own element types and need only BCL interfaces on them ([Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)).
- **NodaTime value sets**: `LocalDateSet`, `LocalDateTimeSet`, `InstantSet`, `LocalTimeSet` and `YearMonthSet`.
- **Set operations**: `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, `IsProperSubsetOf`, `IsProperSupersetOf`, `Union`, `Remove`, `Count` and `IsEmpty`, and `Intersect`, `Except` and `Add` in memory only. Also array literals, JSON and collection expressions.
- **EF Core: value set columns** as native arrays (`StringSet` → `text[]`, …), with wrapper sets found automatically. The operations translate to `@>`, `&&`, `<@`, `cardinality`, `array_cat` and `array_remove`; `Contains` is `@>`, so a GIN index serves it. `Count` over a `Union` fails translation ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).

## [5.0.0] — 2026-08-13

New range types beyond PostgreSQL's built-ins. No breaking changes.

### Added

- **`TimeRange`** over `TimeOnly`, for the custom `timerange` type. EF Core needs `HasPostgresRange` and `EnableUnmappedTypes` for it ([TimeRange](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md#timerange-and-the-custom-timerange-type)).
- **NodaTime: `YearMonthRange`** over `YearMonth`, stored as a month-aligned `daterange`, with conversions to and from `LocalDateRange` and `DateInterval`. A non-ISO calendar is rejected.

## [4.1.0] — 2026-08-12

### Added

- **`CodoMetis.ValueRanges.NodaTime`**: `LocalDateRange` (`daterange`), `LocalDateTimeRange` (`tsrange`) and `InstantRange` (`tstzrange`), with conversions to and from NodaTime's `Interval` and `DateInterval`.
- **`CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime`**: maps them through `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`, turned on with `npgsql.UseValueRangesNodaTime()`.

### Changed

- EF Core: range types are registered through `RangeTypeRegistry.Register`, so other packages can add theirs. Your code doesn't change.

## [4.0.0] — 2026-08-10

Every PostgreSQL range and multirange operator now runs in memory and in SQL: [Migration from v3.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v3x).

### Breaking

- `==` and `!=` on `RangeSet` compare values, as the range records and SQL `=` do. Use `ReferenceEquals` where you meant identity.
- `DoesNotExtendRightOf` and `DoesNotExtendLeftOf` match PostgreSQL for infinite bounds: `+∞ ≤ +∞`, so an unbounded range no longer always returns `false`.

### Added

- **Bound accessors**: `LowerBound()` and `UpperBound()` return `T?`, `null` where PostgreSQL returns `NULL`, and `LowerBoundInclusive()` and `UpperBoundInclusive()`, on ranges and `RangeSet`. EF Core translates them to `lower`, `upper`, `lower_inc` and `upper_inc`.
- **`Merge`**: the smallest range covering both, gap included (`range_merge`).
- **Aggregates**: `RangeAgg()` and `RangeIntersectAgg()`, translated to `range_agg` and `range_intersect_agg` inside a `GroupBy`.
- **`RangeSet` operators**: `Contains`, `Overlaps`, `IsAdjacentTo`, `IsStrictlyLeftOf`, `IsStrictlyRightOf`, `DoesNotExtendLeftOf`, `DoesNotExtendRightOf`, `IsEmpty()`, `IsUnboundedStart()` and `IsUnboundedEnd()`, each translated to its multirange operator.

### Fixed

- `RangeSet.Infinite.Contains(range)` and `Overlaps(range)` work for a range with a finite bound, where they threw `InvalidOperationException`.

## [3.1.0] — 2026-06-17

### Added

- **`RangeSet<TRange, T>.LowerBoundComparer`**, the set's lower-bound ordering as an `IComparer<TRange>`, also as `RangeLowerBoundComparer<TRange, T>.Instance`.

### Changed

- `RangeSet` is faster: `Contains` and `Overlaps` are O(log n), and `Union`, `Intersect` and `Except` merge two sorted lists. No result changed.

### Fixed

- A quoted range bound unescapes `\"` and `\\`, so an element whose text holds a quote or a backslash round-trips.

## [3.0.0] — 2026-06-11

[Migration from v2.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v2x).

### Breaking

- `IsEmpty`, `IsFinite`, `IsInfinity`, `IsUnboundedStart` and `IsUnboundedEnd` are methods, so add parentheses at every call. As methods they translate to SQL.

### Added

- **`CodoMetis.ValueRanges.EFCore.PostgreSQL`**: maps the range types to PostgreSQL range and multirange columns and translates the range operations to SQL, turned on with `npgsql.UseValueRanges()` ([EF Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)).

## [2.0.0] — 2026-06-11

[Migration from v1.x](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md#migration-from-v1x).

### Breaking

- `ToString()` returns the PostgreSQL literal (`[1,10]`), where it returned the record's `Finite { Start = 1, … }`. Update code that logs, displays or compares that text.

### Added

- **`Parse` and `TryParse`** for PostgreSQL range literals.

## [1.0.0] — 2026-06-10

The first release.

### Added

- **The six PostgreSQL range types** as unions of five shapes (`Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange`, `Infinity`), with the range operations and `RangeSet<TRange, T>`, a multirange that keeps itself normalized.

[9.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v9.0.0
[8.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v8.0.0
[7.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v7.0.0
[6.3.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.3.0
[6.2.1]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.2.1
[6.2.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.2.0
[6.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.1.0
[6.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.0.0
[5.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v5.0.0
[4.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v4.1.0
[4.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v4.0.0
[3.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v3.1.0
[3.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v3.0.0
[2.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v2.0.0
[1.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v1.0.0

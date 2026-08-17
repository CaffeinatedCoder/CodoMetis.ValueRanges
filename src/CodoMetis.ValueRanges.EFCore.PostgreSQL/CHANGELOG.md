# Changelog — CodoMetis.ValueRanges.EFCore.PostgreSQL

Entries affecting the EF Core (Npgsql) plugin. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [6.4.0] — 2026-08-17

### Added

- **Mappings for the six new core wrapper arities** — `Int16Set<T>` to `smallint[]`,
  `DecimalSet<T>` to `numeric[]`, `DateSet<T>` to `date[]`, `TimeSet<T>` to `time[]`,
  `DateTimeSet<T>` to `timestamp[]` and `DateTimeOffsetSet<T>` to `timestamptz[]`. As with the
  existing arities they are matched by open generic definition and built on demand, so there is
  no per-element registration to get wrong.
- **`SetTypeRegistry.RegisterFamily`**, so a satellite can contribute wrapper families. Only
  closed definitions could be registered before, which is no use to a family whose element type
  comes from the consumer.

### Changed

- **The wrapper element bridge takes an explicit format and conversion delegates** instead of
  going through the element's default text form and `IParsable<TPrimitive>`. Two reasons, both
  load-bearing for the new arities: the default text form of `TimeOnly`, `DateTime` and
  `DateTimeOffset` drops sub-seconds, and `DateTime.Parse` reached through `IParsable` has no way
  to ask for `DateTimeStyles.RoundtripKind`, so a UTC element became `DateTimeKind.Local` on the
  way to the parameter. NodaTime's value types do not implement `IParsable` at all, which the
  satellite's arities need.

  The temporal families parse strictly (`ParseExact`), so an element that ignores the format
  specifier raises the existing contract error naming the type rather than binding a value
  truncated to the second. Behaviour for `StringSet<T>`, `GuidSet<T>`, `Int32Set<T>` and
  `Int64Set<T>` is unchanged.

## [6.3.0] — 2026-08-16

### Added

- **`RangeSet.IsFinite()` translates** to `NOT lower_inf(x) AND NOT upper_inf(x) AND NOT isempty(x)`
  on a multirange column, matching the single-range predicate of the same name.
- **`RangeSet.IsInfinity()` translates** to equality against the infinite multirange literal —
  `x = '{(,)}'::datemultirange` — and deliberately *not* to `lower_inf(x) AND upper_inf(x)`, which
  is the correct translation for a single range and the wrong one for a multirange: a multirange
  can satisfy both and still have a gap. PostgreSQL canonicalizes multiranges the way the model
  does, so equality is exact. Verified against live PostgreSQL, where `{(,5],[10,)}` answers
  `lower_inf` and `upper_inf` true and the equality false.

## [6.2.1] — 2026-08-16

No source change in this package. The translation of `IsAdjacentTo` to `-|-` was always correct —
PostgreSQL's operator is symmetric — but the in-memory predicate it mirrors was not, so client-side
and server-side answers disagreed for unbounded operands. Fixed in `CodoMetis.ValueRanges` 6.2.1;
the live-PostgreSQL suite here now pins the agreement for every affected shape pair.

## [6.2.0] — 2026-08-16

### Fixed

- **`Count` over a union wrapped in `Remove` counted shared elements twice.** The refusal that
  keeps `Count` off a server-computed `array_cat` matched only the outermost call, and
  `array_remove` preserves canonical form rather than establishing it — so
  `Tags.Union(Other).Remove(x).Count` reached `cardinality` over the concatenation. Against live
  PostgreSQL, `{a,c}` unioned with `{a,b}` counted **4** where the in-memory expression is 3.
  Canonicality-preserving wrappers are now looked through.

## [6.1.0] — 2026-08-15

Version-alignment release — no changes to this package. 6.1.0 was a System.Text.Json audit
affecting the core and NodaTime packages.

## [6.0.0] — 2026-08-14

### Added

- **Value set columns.** The ten core set types map by convention to native PostgreSQL array
  columns (`StringSet` → `text[]`, `GuidSet` → `uuid[]`, …), with wrapper instantiations like
  `StringSet<YourKey>` recognized automatically — nothing to configure or register.
- **Set algebra translation** to the array operators: `Contains` → `@> ARRAY[value]` (so a plain
  GIN index serves it), `Overlaps` → `&&`, `IsSubsetOf` → `<@`, `IsSupersetOf` → `@>`, `Union` →
  `array_cat`, `Remove` → `array_remove`, `Count`/`IsEmpty` → `cardinality`.

Sets are mapped as scalars with plugin-owned mappings and translators, never through EF's
primitive-collection machinery. `Count` over a `Union` is deliberately refused: `array_cat` does
not canonicalize, so only order- and multiplicity-insensitive operators may compose on it.

No breaking changes.

## [5.0.0] — 2026-08-13

### Added

- **`TimeRange` mapping** to the custom PostgreSQL `timerange` type. Unlike the built-ins this one
  needs two one-line opt-ins on the database side: `HasPostgresRange` to create the type and
  `EnableUnmappedTypes` on the data source.

No breaking changes.

## [4.1.0] — 2026-08-12

### Changed

- The internal type registry became extensible so satellite packages can register their own range
  types. New range types are wired exclusively through `RangeTypeRegistry.Register`. No source
  changes required in consuming code.

## [4.0.0] — 2026-08-10

### Added

- **Bound accessor translation** — `LowerBound()`/`UpperBound()` → `lower`/`upper`,
  `LowerBoundInclusive()`/`UpperBoundInclusive()` → `lower_inc`/`upper_inc`, so ordering by range
  start works straight from LINQ: `query.OrderBy(b => b.Period.LowerBound())` → `ORDER BY
  lower("Period")`.
- **`Merge`** → `range_merge`, and the **`RangeAgg`/`RangeIntersectAgg` aggregates** →
  `range_agg`/`range_intersect_agg`, translated inside `GroupBy` projections.
- **Multirange operator parity** — every `RangeSet` operator and state check translates to its
  multirange operator or function, including `==` as SQL `=`.
- **Live-PostgreSQL integration suite** via Testcontainers, asserting the generated SQL agrees with
  the in-memory results.

## [3.1.0] — 2026-06-17

Version-alignment release — no changes to this package.

## [3.0.0] — 2026-06-11

Initial release of this package.

### Added

- EF Core (Npgsql) plugin mapping the range types to PostgreSQL range columns (`int4range`,
  `int8range`, `numrange`, `daterange`, `tsrange`, `tstzrange`) and `RangeSet<TRange, T>` to the
  corresponding multirange columns — no manual value converters required.
- LINQ-to-SQL translation of the range algebra: `Contains`, `Overlaps`, `IsContainedBy`,
  `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf`, `IsAdjacentTo` (`@>`, `<@`, `&&`,
  `<<`, `>>`, `&<`, `&>`, `-|-`), the `Intersect`/`Union`/`Except` operators, and the
  `CreateFinite`/`CreateUnboundedStart`/`CreateUnboundedEnd` factories as range constructor calls.
- Enabled with one line: `options.UseNpgsql(..., npgsql => npgsql.UseValueRanges())`.

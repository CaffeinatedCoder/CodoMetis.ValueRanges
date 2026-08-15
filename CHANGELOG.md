# Changelog

All notable changes to CodoMetis.ValueRanges and its satellite packages.

The four published packages share one version number (the `Version` property in
`Directory.Build.props`) and are released together, so a version appears here even when a given
package saw no source change in it. Each package additionally carries its own `CHANGELOG.md`,
filtered to the entries that affect it.

Versions follow [Semantic Versioning](https://semver.org/). Entries are newest-first.

## [6.1.0] — 2026-08-15

A System.Text.Json audit and the three defects it found. All three shared one shape: the
serializer fell back to reflection where the library expected a converter, and the result was
silence rather than an exception. Every fix replaces a crash or a wrong answer.

### Fixed

- **Value set elements without a converter serialized as objects.** A validated wrapper —
  `StringSet<PermissionKey>`, `GuidSet<TenantId>`, … — whose element type carried no
  `[JsonConverter]` was handed to the reflection path, which wrote `[{"Value":"users.read"}]`, or
  `[{}]` for the generator-typical record struct over a private field. The `[{}]` form destroyed
  data on read, and both disagreed with the `{users.read}` stored in PostgreSQL. Elements now go
  through the family's own text form, and reads re-run the element's `IParsable` validation.
  **Payload change:** string- and Guid-backed sets now write `["users.read"]`, integer-backed ones
  write `[1,2]` — indistinguishable from the primitive each wraps. If you persisted or published
  the old object form, that payload shape changes. A registered converter still wins.
- **The NodaTime sets had the identical failure** — `[{"Calendar":{…},"Year":2024,…}]` on write,
  `default` on read.
- **Nullable range properties threw.** `HandleNull` routed nulls into the write path, which
  dereferenced them: serializing an object with a null `Int32Range?` threw
  `NullReferenceException`. It now writes `null`. Reads still reject a null token — use `"empty"`.
- **Ranges reached through `object` threw.** `Serialize<object>(range)`, an `object`-typed property
  and heterogeneous collections all present the union's sealed variant, for which the converter
  could not be constructed, and a reflection `ArgumentException` escaped.

### Added

- `IValueSetFactory<TSet, T>.ElementJsonConverter` — a defaulted virtual static hook consulted only
  when the element type has no registered converter. The interface stays closed to external
  implementation.
- `RangeVariantJsonConverter<TVariant, TRange, T>`.
- `AddNodaTimeRangeConverters()` in the NodaTime satellite, for bare NodaTime values sitting next
  to a set, which the element hook does not reach. Composes with `ConfigureForNodaTime` in either
  registration order.

## [6.0.0] — 2026-08-14

**Value sets — a second type family.** The model was never "ranges" narrowly; it is immutable,
canonical-at-construction value domains with PostgreSQL-native storage shapes. `RangeSet` has
embodied that for multiranges since v2; v6 applies it one level down, to canonical sets of scalar
values stored as native PostgreSQL arrays.

### Added

- **Ten closed set types** in the core package — `StringSet`, `GuidSet`, `Int16Set`, `Int32Set`,
  `Int64Set`, `DecimalSet`, `DateSet`, `TimeSet`, `DateTimeSet`, `DateTimeOffsetSet` — plus the
  **validated-wrapper arities** `StringSet<T>`, `GuidSet<T>`, `Int32Set<T>`, `Int64Set<T>` for
  generator-produced domain values (Vogen, Metalama, StronglyTypedId, hand-written), constrained
  only on BCL interfaces so domain types never reference this package.
- **Five NodaTime set types** in the satellite: `LocalDateSet`, `LocalDateTimeSet`, `InstantSet`,
  `LocalTimeSet`, and the month-granularity `YearMonthSet`.
- **Membership algebra** — `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`,
  `IsProperSubsetOf`, `IsProperSupersetOf`, `Union`, `Remove`, `Count`, `IsEmpty`, plus
  client-side-only `Intersect`, `Except` and `Add` — with PostgreSQL array literals, JSON support
  and collection expressions.
- **EF Core mapping by convention** to native array columns, wrapper instantiations recognized
  automatically, translating to `@>`, `&&`, `<@`, `cardinality`, `array_cat` and `array_remove`.
  Containment always translates as `@>`, so a plain GIN index serves it.

No breaking changes; the major marks the package growing a second type family.

## [5.0.0] — 2026-08-13

### Added

- **`TimeRange`** (core) — a time-of-day range over `TimeOnly`, matching the most common custom
  range type in PostgreSQL practice (`CREATE TYPE timerange AS RANGE (subtype = time)`). Its EF
  mapping needs `HasPostgresRange` plus `EnableUnmappedTypes` on the database side.
- **`YearMonthRange`** (NodaTime satellite) — a month-granularity range over `YearMonth` for
  billing and reporting periods, stored by the EF satellite as a month-aligned `daterange`, so no
  custom database type is required. Conversions to and from `LocalDateRange` and `DateInterval`
  included.

Both carry the complete algebra, multiranges, literals, JSON support and aggregate overloads of
the existing eight. No breaking changes; the major marks the model growing beyond the PostgreSQL
built-ins.

## [4.1.0] — 2026-08-12

### Added

- **`CodoMetis.ValueRanges.NodaTime`** — `LocalDateRange` (`daterange`), `LocalDateTimeRange`
  (`tsrange`) and `InstantRange` (`tstzrange`), with conversions to and from NodaTime's own
  `Interval` and `DateInterval`.
- **`CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime`** — maps them via
  `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`, enabled with `npgsql.UseValueRangesNodaTime()`.

### Changed

- The EF plugin's internal type registry became extensible so satellites can register their own
  range types. No source changes required in consuming code.

## [4.0.0] — 2026-08-10

**PostgreSQL feature-matrix completion** — every remaining range and multirange operator and
function gained an in-memory implementation and a LINQ-to-SQL translation.

### Added

- **Bound accessors** — `LowerBound()`/`UpperBound()` returning `T?` with PostgreSQL's `NULL`
  semantics, and `LowerBoundInclusive()`/`UpperBoundInclusive()` mirroring `lower_inc`/`upper_inc`,
  on ranges and on `RangeSet`.
- **`Merge`** — the smallest single range spanning both operands including any gap
  (`range_merge`).
- **Aggregates** — `RangeAgg()` and `RangeIntersectAgg()` (`range_agg`,
  `range_intersect_agg`), translated inside `GroupBy` projections.
- **Multirange operator parity** — `Contains`, `Overlaps`, `IsAdjacentTo`,
  `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf` on `RangeSet`, plus the state
  checks `IsEmpty()`, `IsUnboundedStart()`, `IsUnboundedEnd()`.
- **Live-PostgreSQL integration suite** via Testcontainers, asserting the translated SQL agrees
  with the in-memory results.

### Changed

- **Breaking — `==`/`!=` on `RangeSet` is now structural equality**, consistent with the range
  records and with the SQL `=` the provider generates. Call sites that relied on reference
  identity must switch to `ReferenceEquals`.
- **Breaking — `DoesNotExtendRightOf`/`LeftOf` now match PostgreSQL for infinite bounds.** An
  infinite bound compares equal to another infinite bound (`+∞ ≤ +∞`), so an unbounded receiver no
  longer always returns `false`. Results against finite-bounded or empty operands are unchanged.

### Fixed

- `RangeSet.Infinite.Contains(range)` and `.Overlaps(range)` threw `InvalidOperationException` for
  operands with a finite bound.

## [3.1.0] — 2026-06-17

### Changed

- **Performance** — `RangeSet<TRange, T>` now exploits its sorted, disjoint, non-adjacent
  invariant: `Contains`/`Overlaps` go from O(n) to O(log n), `Union`/`Intersect`/`Except` become
  merge-joins over two pre-sorted streams, `Except` from `Infinite` becomes a single-pass
  complement walk, and single-element `From` takes a zero-allocation fast path. No public API or
  result changed.

### Added

- `RangeSet<TRange, T>.LowerBoundComparer`, exposing the set's internal lower-bound ordering as a
  public `IComparer<TRange>` singleton (also `RangeLowerBoundComparer<TRange, T>.Instance`).

### Fixed

- Quoted range bounds now unescape `\"` → `"` and `\\` → `\` on parse, so element types whose
  stringification contains quotes or backslashes round-trip correctly.

## [3.0.0] — 2026-06-11

### Added

- **`CodoMetis.ValueRanges.EFCore.PostgreSQL`** — the EF Core (Npgsql) plugin, mapping the range
  types to PostgreSQL range columns and translating the algebra from LINQ to SQL.

### Changed

- **Breaking — the state checks are extension methods, not extension properties.** `IsEmpty`,
  `IsFinite`, `IsInfinity`, `IsUnboundedStart` and `IsUnboundedEnd` need parentheses at every call
  site. Extension properties cannot appear in LINQ expression trees, which blocked SQL
  translation; as methods they translate.

## [2.0.0] — 2026-06-11

### Added

- `Parse`/`TryParse` and `ToString` support for PostgreSQL range literals.

### Changed

- **Breaking — `ToString()` returns a PostgreSQL range literal** (`[1,10]`) rather than the default
  C# record representation (`Finite { Start = 1, End = 10, … }`). Code depending on the old format
  for logging, display, serialization or string comparison must be updated.

## [1.0.0] — 2026-06-10

Initial release: the six PostgreSQL range domains as discriminated unions of five sealed variants
(`Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange`, `Infinity`), the interval algebra, and
`RangeSet<TRange, T>` as an always-normalized multirange.

[6.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.1.0
[6.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v6.0.0
[5.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v5.0.0
[4.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v4.1.0
[4.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v4.0.0
[3.1.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v3.1.0
[3.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v3.0.0
[2.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v2.0.0
[1.0.0]: https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases/tag/v1.0.0

# Changelog

All notable changes to CodoMetis.ValueRanges and its satellite packages.

The four published packages share one version number (the `Version` property in
`Directory.Build.props`) and are released together, so a version appears here even when a given
package saw no source change in it. Each package additionally carries its own `CHANGELOG.md`,
filtered to the entries that affect it.

Versions follow [Semantic Versioning](https://semver.org/). Entries are newest-first.

## [6.4.0] — 2026-08-17

The validated-wrapper arities now exist for every value set family instead of four of them. No
breaking changes.

### Added

- **Eleven new wrapper arities**, completing the set: `Int16Set<T>`, `DecimalSet<T>`, `DateSet<T>`,
  `TimeSet<T>`, `DateTimeSet<T>` and `DateTimeOffsetSet<T>` in the core package, and
  `LocalDateSet<T>`, `LocalDateTimeSet<T>`, `InstantSet<T>`, `LocalTimeSet<T>` and
  `YearMonthSet<T>` in the NodaTime satellite. Every value set family now has one, so a
  domain type backed by any supported primitive can be stored as a native PostgreSQL array
  without the domain type referencing this package.

  As before, `TElement` is constrained only on BCL interfaces — `struct`, `IEquatable<T>`,
  `IComparable<T>`, `IFormattable`, `IParsable<T>` — which is what Vogen, Metalama,
  StronglyTypedId and hand-written wrappers already emit.

- **`SetTypeRegistry.RegisterFamily`**, the seam the NodaTime satellite registers its arities
  through. A wrapper family cannot be registered as a closed definition, because its element type
  is whatever the consumer supplies.

### Changed

- **The temporal arities ask their elements for a round-trip format** rather than accepting the
  element's default text form. This is the one place the wrapper contract is stricter than for the
  existing four arities, and it is not cosmetic: `TimeOnly` renders as `09:30` with a null format,
  `DateTime` as `06/15/2024 10:30:00`, so an arity built the way `Int32Set<T>` is built would have
  stored every timestamp truncated to the second, and every `DateTimeKind` lost — silently, on the
  way to the column.

  Concretely, the contract for these six families is that the element's `ToString("O", …)` (or
  `"yyyy-MM-dd"` for `DateSet<T>`, and the ISO pattern for the NodaTime arities) is exactly the
  backing primitive's. A wrapper that forwards its `format` argument — the generated shape —
  satisfies it with no extra work. One that swallows the argument is rejected at the persistence
  boundary with an error naming the type and the contract, rather than storing a truncated value.

- **`DateTimeSet<T>` and `DateTimeOffsetSet<T>` normalize at the provider boundary** exactly as
  their closed siblings do: wall-clock `DateTimeKind.Unspecified` for `timestamp`, UTC for
  `timestamptz`.

### Fixed

- **⚠️ The numeric wrapper arities ignored `JsonSerializerOptions.NumberHandling`.** `Int16Set<T>`,
  `Int32Set<T>`, `Int64Set<T>` and `DecimalSet<T>` write their elements' JSON tokens themselves, so
  System.Text.Json never gets to apply the setting on their behalf — and they did not consult it.
  Under `JsonNumberHandling.WriteAsString` an arity emitted a bare number where its primitive
  sibling emitted a string:

  ```
  Int64Set              ["9007199254740993"]
  Int64Set<OrderId>     [9007199254740993]     ← before
  ```

  `WriteAsString` is switched on almost exclusively because the consumer is JavaScript, where a
  bare number above 2^53 is rounded on arrival — 9007199254740993 arrives as 9007199254740992. So
  swapping a closed set for its arity silently reintroduced, at the client only, the corruption the
  setting was turned on to prevent. **Payloads change for anyone serializing a numeric wrapper arity
  under `WriteAsString`**, from a number to the string their primitive sibling was already writing.
  Reads are unaffected — the numeric converters have always accepted a JSON string unconditionally.

### Known difference

- **A temporal arity's JSON is not byte-identical to its closed sibling's**, though the token type
  and the value are the same and each payload deserializes into the other's type. The round-trip
  format always writes seven fraction digits where System.Text.Json trims them, and the default
  encoder escapes `+`:

  ```
  DateTimeOffsetSet     ["2024-06-15T10:30:00+02:00"]
  DateTimeOffsetSet<T>  ["2024-06-15T10:30:00.0000000+02:00"]
  ```

  The string, Guid, integer and decimal arities remain byte-identical to their siblings. Give the
  element type its own `[JsonConverter]` if an existing response shape matters.

## [6.3.0] — 2026-08-16

Three additions that close gaps in the existing surface rather than extending the model. No
breaking changes.

### Added

- **`RangeSet.IsInfinity()` and `RangeSet.IsFinite()`** — the two shape predicates a range had and
  its multirange counterpart did not. `IsFinite()` is true for a non-empty set bounded at both
  ends; `IsInfinity()` is true only for the set covering the whole domain.

  `IsInfinity()` is deliberately **not** `IsUnboundedStart() && IsUnboundedEnd()`. That equivalence
  holds for a single range, because a range is contiguous, and fails for a set:
  `{(,5],[10,)}` is unbounded at both ends and does not contain 7. The EF translation reflects
  the same distinction — the range predicate maps to `lower_inf(x) AND upper_inf(x)`, the set
  predicate to equality against the infinite multirange literal, which is exact because
  PostgreSQL canonicalizes multiranges the way the model does. `IsFinite()` maps to
  `NOT lower_inf AND NOT upper_inf AND NOT isempty` for both.

- **Collection expressions for `RangeSet<TRange, T>`** — `RangeSet<Int32Range, int> set = [a, b];`,
  matching what the nineteen value set types and arities already supported. Elements are normalized exactly as
  `From` normalizes them: empties dropped, sorted by lower bound, overlapping and adjacent
  neighbours merged, any infinity collapsing the set. A `From(params ReadOnlySpan<TRange>)`
  overload comes with it, alongside the existing `From(IEnumerable<TRange>)`.

  The builder is exposed as a non-generic `RangeSet.Create<TRange, T>`, because a
  `[CollectionBuilder]` target cannot be generic. Prefer the collection expression: C# does not
  infer type arguments from constraints, so a direct call has to name both `TRange` and `T`, which
  is longer than `RangeSet<TRange, T>.From` already was.

- **`ISpanParsable<T>` on every parsable type** — the eleven range types, `RangeSet<TRange, T>`, and
  all nineteen value set types and arities, with public `Parse`/`TryParse` overloads over
  `ReadOnlySpan<char>` beside the existing `string` ones. The literal grammars were already parsed
  over spans internally, so this exposes the parser that was always there and lets a caller parse
  a slice of a larger buffer without allocating a substring first. `IParsable<T>` remains
  satisfied — `ISpanParsable<T>` extends it.

  One consequence worth knowing if you write generic code over these types: where a type parameter
  is constrained to `IRangeFactory`/`IValueSetFactory`, both `Parse` overloads are now visible, and
  a `string` argument binds to the span overload through the implicit conversion. Every type's two
  overloads are the same call, so results do not change.

- **`Length`** on every range type — the measure of what it covers. A discrete domain counts its
  values inclusive of both ends (`[2024-01-01, 2024-01-31]` measures 31 days, `[1, 10]` measures
  10 integers), a continuous one measures the span between the bounds. The empty range measures
  zero and every unbounded shape measures `null`: the two are different answers and stay
  distinguishable. The type follows the domain — `long?` for `Int32Range`/`Int64Range`, `int?`
  days for `DateRange`, `TimeSpan?` for the timestamp ranges, `decimal?` for `DecimalRange`, and
  `Duration?`/`Period?` for the NodaTime ranges, which distinguish elapsed time from a calendar
  quantity. Client-side only; it does not translate to SQL.

- **`Values()`** on the discrete range types — enumerates the contained values ascending,
  inclusive of both bounds. Declared only by `Int32Range`, `Int64Range`, `DateRange`,
  `LocalDateRange` and `YearMonthRange`, so asking a continuous range for its values is a compile
  error rather than a runtime failure. An unbounded range throws immediately rather than at the
  first iteration, so the failure points at the call rather than at the `foreach`.

- **A bridge between the value sets and the range sets** over the same discrete domain:
  `Int32Set`/`Int64Set`/`DateSet` (plus `LocalDateSet`/`YearMonthSet` in the NodaTime satellite)
  gain `ToRangeSet()`, which collapses runs of consecutive values — `{1,2,3,7}` becomes
  `{[1,3],[7,7]}` — and the matching `ToInt32Set()`/`ToDateSet()`/… expand back. The two shapes
  describe the same membership; which to store is a question of density, and a thousand
  consecutive dates are better served by one `daterange` than by a thousand-element array. Both
  directions are client-side: PostgreSQL converts between arrays and multiranges only through
  `unnest` and a custom aggregate.

- **`Clamp(value)`** on every range — the contained value nearest the argument, or `null` for the
  empty range. An unbounded side never constrains.

- **An indexer on the value set types**, `set[0]`, matching what `RangeSet` already offered.

- **`IRangeFactory<TRange, T>.IsDiscrete`** — a defaulted virtual static reporting whether the
  domain has a step, for generic code that cannot see which concrete type it holds. It cannot be
  derived from `NextValueAfter`, which returns `null` both for a continuous domain and for the
  last value of a discrete one; a convention test now holds the two to agreement.

## [6.2.1] — 2026-08-16

### Changed

- **Parse and JSON rejections no longer echo the whole input.** A malformed range, multirange or
  array literal used to come back in the `FormatException` — and a bad bound or element chained the
  BCL's exception, whose message embeds the text again — so a megabyte of hostile input became a
  megabyte of exception message, copied into every log sink and, in development, returned to the
  client. Messages now carry a 64-character excerpt plus the input's length, and element failures
  report the inner parser's reason as an excerpt instead of chaining it. **If you matched on the
  full message text or read `InnerException` on these failures, that shape has changed.** Exception
  types are unchanged: `FormatException`, `OverflowException`, `JsonException`; `TryParse` still
  never throws.
- **Tests:** a parser resilience suite (`ParserResilienceTests`) now backs the "denial of service
  through parsing" line in SECURITY.md — megabyte-scale malformed literals and 200,000-element sets
  are accepted or rejected in bounded time, `TryParse` never throws, and no rejection echoes the
  payload.

### Fixed

- **⚠️ `IsAdjacentTo` answered `false` whenever the receiver was unbounded, and normalization
  inherited it.** The predicate switched on the receiver's shape and handled only
  `IFiniteRange<T>`; every other shape fell through to `false`. Its *inner* switch did handle
  unbounded operands, so the relation was asymmetric — `[1,3].IsAdjacentTo((,0])` was `true` while
  `(,0].IsAdjacentTo([1,3])` was `false`. PostgreSQL's `-|-` is symmetric and answers `true` for
  both; the XML doc asserted the broken behaviour as if it were intended, which is why reading the
  code confirmed it.

  The consequence was not confined to the predicate. `RangeSet.From` and `RangeSet.Union` merge
  neighbours with `current.IsAdjacentTo(next)` after sorting by lower bound, so an unbounded-start
  element is *always* the receiver and always took the broken direction. Sets were built violating
  the invariant they document:

  ```
  RangeSet.From([(,0], [1,)])          was {(,0],[1,)}   now {(,)}
  blocks.Union(blocks.Complement())    was {(,0],[1,)}   now the Infinite set
  ```

  Two sets that should be equal compared unequal depending on how they were built, and a set
  covering the whole domain did not equal `RangeSet.Infinite`. **Results change for any range or
  set involving an unbounded element adjacent to its neighbour** — always from a wrong answer to
  the one PostgreSQL gives. Model-versus-server agreement is now pinned by the live suite for
  every affected shape pair. Applies to the NodaTime range types, which share the predicate.

## [6.2.0] — 2026-08-16

### Changed

- **⚠️ A null range now reads back as `null` instead of throwing.** Writing a null `Int32Range?`
  property emitted `{"Seats":null}`, and reading that same document threw `JsonException` — the
  package could not read a payload it had just written, so an ASP.NET Core API could return a
  body it was unable to accept. `null` is now left to System.Text.Json in both directions, as for
  any other reference type, matching what `RangeSet` and the value sets already did.
  `null` and the empty range stay distinct: absent is `null`, empty is the literal `"empty"`.
  **If you relied on the exception to reject a null where a non-nullable range was expected, that
  validation is gone** — the property now receives `null`, exactly as any other reference-typed
  property would. Malformed literals are still rejected.

### Fixed

- **`Count` over a union wrapped in `Remove` counted shared elements twice.** `Union` translates
  to `array_cat`, which concatenates rather than deduplicating, so `Count` over a server-computed
  union has always been refused rather than answered. The refusal matched only the outermost call,
  and `array_remove` *preserves* canonical form rather than establishing it — so
  `Tags.Union(Other).Remove(x).Count` slipped through to `cardinality` over the concatenation.
  Against live PostgreSQL, `{a,c}` unioned with `{a,b}` counted **4** where the in-memory
  expression is `{a,b,c}` — **3**. A `Where` on that count filtered on a number that was quietly
  too large; a `Select` returned it. Canonicality-preserving wrappers are now looked through, so
  the expression is refused in a predicate and falls back to client evaluation in a projection,
  where it answers 3.

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

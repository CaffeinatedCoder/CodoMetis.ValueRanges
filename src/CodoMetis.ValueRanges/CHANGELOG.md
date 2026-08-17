# Changelog — CodoMetis.ValueRanges

Entries affecting the core package. The [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md)
covers all four packages, which share one version number and release together.

## [6.4.0] — 2026-08-17

### Fixed

- **⚠️ `IsStrictlyLeftOf` answered `false` for every range unbounded at its *start***, and
  `IsStrictlyRightOf` for every such operand. The `<<` relation compares the receiver's **upper**
  bound with the operand's **lower** bound, so being unbounded at the *other* end is irrelevant:
  `(-∞, 5]` ends at 5 and is strictly left of `[10, 20]`. The implementation switched on the
  receiver's shape and handled only `IFiniteRange<T>` there, while its inner switch handled
  unbounded *operands* — so `((-∞,5]).IsStrictlyLeftOf([10,20])` returned `false` where
  `'(,5]'::int4range << '[10,20]'` returns `true`.

  The EF translation emits `<<` and was always correct, which is what made this dangerous: the
  same predicate over the same two values answered `true` server-side and `false` in memory.
  `RangeSet.IsStrictlyLeftOf`/`RightOf` delegate to their outermost element and inherited it, so a
  one-element set `{(,5]}` was affected too.

  Now decided by reading the receiver's upper bound and the operand's lower bound rather than by
  switching on the receiver's shape, so the two directions cannot drift apart again — the same
  correction `IsAdjacentTo` received in 6.2.1, which was the only other receiver-shaped predicate.
  A 5×5 shape sweep in both directions and a live-PostgreSQL parity test now cover it.
  **Behaviour changes for in-memory comparisons involving an unbounded-start range**, from a wrong
  `false` to the answer the database already gave.

## [6.3.0] — 2026-08-16

### Added

- **`RangeSet.IsInfinity()` and `RangeSet.IsFinite()`**, completing the shape predicates the single
  ranges already had. `IsInfinity()` is true only for the set covering the whole domain — not the
  same thing as unbounded at both ends, which `{(,5],[10,)}` satisfies while missing 7.
  `IsFinite()` is true for a non-empty set bounded at both ends.
- **Collection expressions for `RangeSet<TRange, T>`** — `RangeSet<Int32Range, int> set = [a, b];`,
  as the value set types already supported. Normalization is unchanged: empties dropped, sorted,
  overlapping and adjacent neighbours merged. Adds `From(params ReadOnlySpan<TRange>)` and the
  non-generic builder `RangeSet.Create<TRange, T>`.
- **`ISpanParsable<T>`** on the seven range types, `RangeSet<TRange, T>`, and the fourteen set types
  and arities, with `Parse`/`TryParse` overloads over `ReadOnlySpan<char>`. The literal parsers
  were already span-based internally; this exposes them, so a slice of a larger buffer no longer
  needs a substring allocation first. `IParsable<T>` is still satisfied.

- **`Length`** on every range type: a count for the discrete domains (inclusive of both bounds),
  a span for the continuous ones. Zero for the empty range, `null` for every unbounded shape.
- **`Values()`** on `Int32Range`, `Int64Range` and `DateRange` — the contained values, ascending.
  The continuous types do not declare it, so misuse is a compile error; an unbounded range throws
  eagerly.
- **`ToRangeSet()` / `ToInt32Set()` / `ToInt64Set()` / `ToDateSet()`** — conversions between a
  value set and a range set over the same discrete domain, collapsing runs of consecutive values
  into ranges and expanding them back. Client-side only.
- **`Clamp(value)`** on every range, and an **indexer** on every value set type.
- **`IRangeFactory<TRange, T>.IsDiscrete`**, a defaulted virtual static reporting whether the
  domain has a step.

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

- **⚠️ `IsAdjacentTo` answered `false` whenever the receiver was unbounded.** The predicate
  switched on the receiver and handled only `IFiniteRange<T>`, while its inner switch handled
  unbounded *operands* — so the relation was asymmetric: `[1,3].IsAdjacentTo((,0])` was `true`,
  `(,0].IsAdjacentTo([1,3])` was `false`. PostgreSQL's `-|-` answers `true` for both.
  `RangeSet.From` and `RangeSet.Union` merge neighbours after sorting by lower bound, which puts an
  unbounded-start element in the receiver position every time, so they built sets that violated the
  pairwise-non-adjacent invariant: `From([(,0], [1,)])` returned `{(,0],[1,)}` rather than `{(,)}`,
  and a set unioned with its complement did not equal `RangeSet.Infinite`. **Results change for any
  range or set with an unbounded element adjacent to its neighbour**, in every case from a wrong
  answer to PostgreSQL's.

## [6.2.0] — 2026-08-16

### Changed

- **⚠️ A null range now reads back as `null` instead of throwing.** Writing a null `Int32Range?`
  property emitted `{"Seats":null}`, and reading that same document threw `JsonException` — the
  package could not read a payload it had just written, so an ASP.NET Core API could return a body
  it was unable to accept. `null` is now left to System.Text.Json in both directions, as for any
  other reference type, matching what `RangeSet` and the value sets already did. `null` and the
  empty range stay distinct: absent is `null`, empty is the literal `"empty"`. **If you relied on
  the exception to reject a null where a non-nullable range was expected, that validation is
  gone.** Malformed literals are still rejected.

## [6.1.0] — 2026-08-15

### Fixed

- **Value set elements without a converter serialized as objects.** A validated wrapper —
  `StringSet<PermissionKey>`, `GuidSet<TenantId>`, … — whose element type carried no
  `[JsonConverter]` was handed to System.Text.Json's reflection path, which wrote
  `[{"Value":"users.read"}]`, or `[{}]` for the generator-typical record struct over a private
  field. The `[{}]` form destroyed data on read, and both disagreed with the `{users.read}` stored
  in PostgreSQL. Elements now go through the family's own text form, and reads re-run the element's
  `IParsable` validation. **Payload change:** string- and Guid-backed sets now write
  `["users.read"]`, integer-backed ones write `[1,2]` — indistinguishable from the primitive each
  wraps. A registered converter still wins.
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

## [6.0.0] — 2026-08-14

### Added

- **Value sets — a second type family:** immutable, canonical (deduplicated, sorted, null-free)
  sets of scalar values whose PostgreSQL storage shape is a native array. Ten closed types —
  `StringSet`, `GuidSet`, `Int16Set`, `Int32Set`, `Int64Set`, `DecimalSet`, `DateSet`, `TimeSet`,
  `DateTimeSet`, `DateTimeOffsetSet` — plus the validated-wrapper arities `StringSet<T>`,
  `GuidSet<T>`, `Int32Set<T>`, `Int64Set<T>` for generator-produced domain values, constrained only
  on BCL interfaces so domain types never reference this package.
- **Membership algebra** — `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`,
  `IsProperSubsetOf`, `IsProperSupersetOf`, `Union`, `Remove`, `Count`, `IsEmpty`, plus
  client-side-only `Intersect`, `Except` and `Add` (PostgreSQL's array type has no intersection,
  difference or sorted insert) — with array literals, JSON support and collection expressions.

No breaking changes; the major marks the package growing a second type family.

## [5.0.0] — 2026-08-13

### Added

- **`TimeRange`** — a time-of-day range over `TimeOnly`, matching the most common custom range type
  in PostgreSQL practice (`CREATE TYPE timerange AS RANGE (subtype = time)`). Continuous and
  half-open by default, so `[09:00, 12:00)` and `[12:00, 17:00)` compose the way shifts do; a
  window crossing midnight is two ranges, which `RangeSet` represents naturally.

No breaking changes.

## [4.1.0] — 2026-08-12

Version-alignment release — no changes to this package. The NodaTime satellites were introduced in
4.1.0; see the [root changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/CHANGELOG.md).

## [4.0.0] — 2026-08-10

### Added

- **Bound accessors** — `LowerBound()`/`UpperBound()` returning `T?` with PostgreSQL's `NULL`
  semantics, and `LowerBoundInclusive()`/`UpperBoundInclusive()` mirroring `lower_inc`/`upper_inc`,
  on ranges and on `RangeSet`.
- **`Merge`** — the smallest single range spanning both operands including any gap.
- **Aggregates** — `RangeAgg()` and `RangeIntersectAgg()` over sequences of ranges.
- **Multirange operator parity** — `Contains`, `Overlaps`, `IsAdjacentTo`,
  `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf` on `RangeSet`, plus `IsEmpty()`,
  `IsUnboundedStart()`, `IsUnboundedEnd()`.

### Changed

- **Breaking — `==`/`!=` on `RangeSet` is now structural equality**, consistent with the range
  records and with the SQL `=` the EF provider generates. Call sites relying on reference identity
  must switch to `ReferenceEquals`.
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
  C# record representation. Code depending on the old format must be updated.

## [1.0.0] — 2026-06-10

Initial release: the six PostgreSQL range domains as discriminated unions of five sealed variants
(`Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange`, `Infinity`), the interval algebra, and
`RangeSet<TRange, T>` as an always-normalized multirange.

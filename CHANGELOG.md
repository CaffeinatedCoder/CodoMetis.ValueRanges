# Changelog

All notable changes to CodoMetis.ValueRanges and its satellite packages.

The four published packages share one version number (the `Version` property in
`Directory.Build.props`) and are released together, so a version appears here even when a given
package saw no source change in it. Each package additionally carries its own `CHANGELOG.md`,
filtered to the entries that affect it.

Versions follow [Semantic Versioning](https://semver.org/). Entries are newest-first.

## [7.0.1] — 2026-08-17

A fourth instance of the trap 7.0.0 documented, and the structural change that makes a fifth fail
loudly instead of silently.

7.0.0 recorded that three bugs had come from one shape: dispatch on the *receiver's* shape, an inner
switch over the operand's, and a discard arm that answers the pairs nobody wrote. Auditing the
engines for that shape turned up one more — `RangeSet.Except(TRange)` with an infinity operand — and
made the case that documenting the rule was not going to be enough on its own. The engines now
decide on the shape *pair*, and a pair with no arm throws instead of returning something plausible.

### Fixed

- **`IRangeFactory.ToString` formatted an unrecognised range as `"empty"`.** All five shapes are
  named above the fallback, so it is reachable only through an `IRange<T>` implementation that is
  none of them — which the sealed-variant rule forbids and the type system permits, the interface
  being public. `"empty"` was the worst available answer: that text is what `Parse` round-trips,
  what the EF literal sends to PostgreSQL and what the shape matrix compares against the server, so
  such a range would have been stored, queried and asserted as the empty range with nothing raised.
  It now throws. Found by triaging every discard arm outside `Internals/` — 60 of them, recorded in
  `docs/discard-triage.md`, of which this was the only defect.
- **`BridgedElementTypeMapping` produced a `string` element's literal by accident.** `string` is not
  `IFormattable`, so it missed every named arm and reached the fallback, where `ToString()` returned
  it unchanged — the right answer for the wrong reason, and the arm that would have silently handed
  PostgreSQL whatever `ToString` produced for a genuinely unknown element type. The string case is
  named and the fallback refuses.
- **`RangeSet.Contains(T)` threw on the infinite set.**
  `RangeSet<Int32Range, int>.Infinite.Contains(value)` raised
  `InvalidOperationException: Range shape 'Infinity' has no lower bound` for every value, where the
  answer is `true` — the infinite set contains everything, and `Int32Range.Infinite.Contains(value)`
  has always said so. The set locates the candidate element by binary-searching the sorted lower
  bounds, and its single element has no lower bound to search on; every other query on the set
  short-circuits the infinite case first, and this one did not. Loud rather than silent, unlike the
  rest of this release, and found by the new oracle below on its first run.
- **`RangeSet.Except(TRange)` returned the whole domain when subtracting an infinity range.**
  `RangeSet<Int32Range, int>.Infinite.Except(Int32Range.Infinite)` answered `{(,)}` where `X \ (-∞,
  +∞)` is the empty set for every `X`, and `Complement()` on the infinite set was wrong through the
  same path. The set-minus-set overload has always guarded its infinite operand and the single-range
  overload answers it through its `Contains` guard, so — as with the empty-range containment bug in
  7.0.0 — the three overloads were answering the same question two different ways. The engine's
  discard arm supplied `∞` for the one pair it was never given: `(Infinity, Infinity)`.

### Changed

- **The `Intersect`, `Merge` and `Except` engines dispatch on the shape pair.** Each had three
  entry points typed by the receiver's shape, and each of those switched over the operand's shape
  with a discard that rebuilt the receiver or returned `Empty` — the structure all four bugs shared.
  They are now one entry point per engine taking `IRange<T>` on both sides, switching over
  `(left, right)` with one arm per accepted pair, so a pair nobody handled is a missing *line*
  rather than something a fallback absorbs. No public signature changed and no behaviour changed
  beyond the fix above; the 3,300-comparison shape matrix agrees with PostgreSQL as before.
- **A shape dispatch with no arm for its operands throws `UnreachableException` naming the pair.**
  C# cannot prove a switch over interface patterns exhaustive, so the discard arm cannot be removed
  — but it can stop producing values. Every one of the four bugs was a fallback returning something
  well-formed: a wrong boolean, or a range of the right shape carrying the wrong values, which looks
  correct in a debugger and disagrees only with the database. These paths are unreachable behind the
  callers' existing guards; if a future change breaks one, the first test to reach it now says which
  pair is missing.

### Added

- **`EngineDispatchConventionTests`**, which parses the shipping sources and enforces both halves of
  the rule: a switch that dispatches on range shape must throw from its discard arm, and an engine's
  entry points must take `IRange<T>` on both sides rather than one operand's shape. Both are
  discovered by globbing `src/`, and both were verified by seeding the defect they claim to catch.
- **`SmallModelOracleTests`** — a second oracle beside the PostgreSQL shape matrix, asking set
  theory instead of the database, and needing no Docker. Every representable range over a tiny
  universe is enumerated from its specification — around 110 per domain, all five shapes and all
  four inclusiveness combinations at every bound — the expected value set is derived arithmetically
  from that specification, and every one of the ~12,100 ordered pairs is checked for all eight
  binary predicates, the four value-producing operations, and the same questions asked again at the
  `RangeSet` arities. About 460,000 assertions per run over the discrete and continuous domains, in
  under 200 ms.

  It exists because hand-picked representatives are how the first version of the 7.0.0 `Except`
  sweep reported zero disagreements on the exact defect it was written to catch. All five bugs of
  this family were seeded back in to prove it catches them: the adjacency asymmetry (6.2.1),
  `IsStrictlyLeftOf` on an unbounded start and `Except` between opposing unbounded operands
  (7.0.0), and both fixes above. The model has one axiom — that `Contains(T)` is correct, since
  results are read back through it — and that is pinned by its own test rather than assumed.
- **An equality sweep in `SmallModelOracleTests`**, closing the last surface the oracles reached at
  the multirange and value-set arities but not at the range's own. Two ranges are equal exactly when
  they hold the same values, and equal ranges hash alike — the law `DiscreteCanonical` exists to
  uphold, whose summary says the bounds are closed "so that structural record equality coincides
  with set equality", and which nothing checked. Over the integers `(1,5)`, `[2,5)`, `(1,4]` and
  `[2,4]` are four spellings of one range and equality has to see through all of them; the
  enumeration holds every spelling at every bound. `Equals(object)`, the `IEquatable<T>` path and
  the `==`/`!=` operators are checked separately because they can diverge, along with reflexivity
  and comparison against `null` and against another type.

  Seeded both halves: a `Finite.Equals` that forgets its upper bound, and a `GetHashCode` on
  reference identity, which reports `[1,1]` and `[1,2)` — the same discrete range spelled twice —
  as hashing apart. The first attempt at the equality seed would not compile, since defining
  `Equals` on a record without `GetHashCode` is CS8851, so the compiler already guards that half.
- **The bound accessors and `Clamp` joined the sweep**, having been argued total during the discard
  triage rather than checked. Grounded in three independent links: nullness from the
  specification's shape, inclusivity cross-checked against `Contains`, and `Clamp` against the
  bounds those establish — predicting a bound's value directly does not work, because a discrete
  range canonicalizes and an exclusive continuous bound is a value the range does not contain.
- **`ValueSetNullContractTests`**, pinning by discovery that canonical form's exclusion of nulls is
  enforced by *refusing*, never by dropping. Silently discarding a null is the value-set shape of the
  fallback that produced five range bugs — an input nobody wrote a case for, answered plausibly:
  three supplied elements would yield a set of two, indistinguishable from a duplicate. Every entry
  point is checked — both `From` overloads, `Add`, `Remove`, `Contains`, an unquoted `NULL` in the
  PostgreSQL array literal, and a JSON `null` element — and all of them already refused, so nothing
  changed in the library.

  What is new is that the rule is enforced over *discovered* families rather than by hand. Exactly
  one qualifies today: every wrapper element is a `readonly record struct` and every NodaTime
  element is a struct, so `string` in `StringSet` is the only nullable element type among the 30.
  A family added later over a reference type is covered without anyone remembering to come back.
  Nulls stay out of `SetProbes`, which feeds sweeps that build valid sets — they need their own test,
  not a probe entry.
- **`Reflect.InvokeGeneric`**, so the three discovery-driven suites report a failed assertion
  directly instead of behind a `TargetInvocationException` frame that says nothing.
- **`ShapeMatrixCoverageTests`**, which fails when a binary range operation has no row in
  `ShapeMatrixParityTests`. The matrix found three of the five bugs in the receiver-shaped-dispatch
  family, and its weakness is that it is a list: an operation added to `RangeExtensions` without a
  row there is simply not swept, and until now nothing said so — the suite stayed green while the
  one check that would catch the next instance quietly stopped covering it.

  Operations are discovered by reflection (C# 14 extension members lower to static methods, so the
  twelve show up as `(receiver, IRange<T>)` pairs) and coverage is parsed out of the matrix's own
  source. The two forms are checked separately by return type: a predicate must appear in the table
  of `(name, operator)` pairs, a value-producing operation must be invoked against its SQL
  counterpart. That split is load-bearing — the matrix's dispatch switch calls all eight predicates,
  so an invocation alone would mark one covered after its row was deleted. All three ways coverage
  can be lost were seeded: a new operation with no row, a deleted predicate row, and a dropped
  value-operation sweep.
- **`SmallModelMultirangeOracleTests`**, reaching the multi-element algorithms that no oracle
  touched: the greedy merge, the sorted merge behind `Union`, the two-pointer merge-join behind
  `Except` and the single-pass gap walk behind `Complement` — the most intricate code here, each
  carrying a hand-written correctness argument in its doc comment, and covered by worked examples
  only. Over eight grid points every subset is a representable multirange, so all 256 subsets and
  all 65,536 ordered pairs is exhaustive over the whole multirange value space.

  It differs from the single-range sweep in two ways that matter. It compares results **element by
  element** against the canonical run decomposition rather than only probing membership, so an
  unmerged neighbour or a stray empty fails — the `RangeSet` invariant was load-bearing and
  unasserted, because `Contains(value)` cannot see it while `Count`, `Equals`, `GetHashCode`,
  `ToString` and the EF multirange literal all can. And it feeds `From` a deliberately
  non-canonical decomposition, since building from maximal runs asks normalization to do nothing;
  a seeded defect disabling adjacency merging passed every other check in the file until that path
  existed. Found no defects; three seeded ones — that adjacency arm, an inclusiveness flip in the
  complement walk, and a merge-join pointer advancing too far — are each caught.

  Discrete domains only, and not incidentally: a run of consecutive grid points is the canonical
  decomposition exactly when consecutive points are contiguous, which is false of the reals.
- **`SmallModelSetOracleTests`** — the same treatment for the value set families, where it matters
  more: `Intersect`, `Except` and `Add` are deliberately client-side only, because a PostgreSQL
  array has no intersection, no difference and no sorted insert, so unlike the ranges there is no
  second implementation anywhere to cross-check against. For all 30 set types — core and NodaTime,
  plain and wrapper arity — it builds *every* subset of the probe universe (2^n, so exhaustive over
  the whole value space rather than a sample) and checks `Values`, `Count`, `IsEmpty`, `Contains`,
  `Add`, `Remove`, all five relational predicates, `Union`, `Intersect`, `Except`, equality and
  hashing over every ordered pair, plus five construction paths per subset — reversed input,
  duplicated input, `IEnumerable`, repeated `Add`, and both round trips.

  It found no defects, which is the honest result; seven were seeded in to show it would have.
  Its model reads the canonical *order* from `CanonicalComparer`, so it verifies that every path
  agrees with the declared order but not that the order is the specified one — a limit recorded on
  the class and closed by the test below.
- **`ValueSetContractTests.StringBackedFamilies_SortOrdinal`**, pinning the one ordering claim no
  self-consistency check can make: `StringSet` and its wrapper arity must order `Zebra` before
  `apple`. Ordinal puts `Z` (90) first and every culture puts `apple` first, so swapping
  `StringComparer.Ordinal` for `StringComparer.InvariantCulture` used to leave the whole suite
  green while changing what the client considers sorted — and PostgreSQL's ordering of a `text[]`
  is not the current culture's.
- **A shared `SetProbes`** holding the value set probe table and type discovery, so the contract
  tests and the new oracle cannot drift apart on which families exist or what to feed them. Two
  tables would let a family lose its probes in one suite and go unexercised there while the other
  stayed green — the failure mode a discovery-driven suite has to defend against hardest.

## [7.0.0] — 2026-08-17

Two workstreams land together: the validated-wrapper arities now exist for every value set family
instead of four of them, and an audit of the range and multirange types corrected five defects.

**Nothing was removed or resignatured** — package validation passes against the 6.3.0 baseline — and
the value set surface only grew. What makes this a major is the range half: three of its five
corrections change what existing calls *answer*, silently, and a silent change of answer is the kind
a caller cannot discover from a compile error.

Those three shared one shape: the EF translation was correct and the in-memory implementation was
not, so the same expression gave one answer when it ran in PostgreSQL and another when it ran in
memory. Nothing threw. If you evaluate range operations only server-side, or only in memory, you saw
consistent (in these cases consistently wrong) results either way; the disagreement was visible only
to code that did both. The remaining two were loud rather than silent — a query PostgreSQL refused
to run, and a property that threw.

Two of the three are the same mistake, and it is the third time this repository has made it: an
operation that dispatches on the *receiver's* shape and handles the *operand's* shapes in an inner
switch, where the missing arm falls through to a default that answers `false` or returns the
receiver unchanged. `IsAdjacentTo` had it in 6.2.1; `IsStrictlyLeftOf` and `Except` have it here.

The audit's durable output is `ShapeMatrixParityTests`, which asks PostgreSQL for all eight binary
predicates *and* the four value-producing operations over every ordered pair of range shapes, and
requires the model to match — some 3,300 comparisons, no exclusions.

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


- **`DecimalRange.Length` threw `OverflowException` for a range wider than `decimal` itself.**
  `DecimalRange.CreateFinite(decimal.MinValue, decimal.MaxValue).Length` raised instead of
  answering; the span is twice `decimal.MaxValue` and there is no wider type to compute it in. It
  now returns `null`, which is the answer `Int64Range.Length` already gave for a count above
  `long.MaxValue` and documented as "too large to represent". Only a range straddling zero can
  reach it, and the refusal is exact — a span of exactly `decimal.MaxValue` still measures.

  This was the only measure in the family that could fail, and a property that throws breaks
  debugger evaluation and LINQ projections as much as it breaks the caller.

- **⚠️ `Except` subtracted nothing when the two operands were unbounded in opposite directions.**
  `((-∞,5]).Except([1,+∞))` returned `{(-∞,5]}` — the receiver, unchanged — where the answer is
  `{(-∞,0]}`, and symmetrically `([1,+∞)).Except((-∞,5])` returned `{[1,+∞)}` instead of `{[6,+∞)}`.
  `RangeSet.Except` reaches the same engine through its merge-join and had it too.

  This is the most damaging of the five, because the result is a **well-formed range of the right
  shape carrying the wrong values** — nothing to notice at a glance, and a subtraction that quietly
  keeps what it was asked to remove. Every element type and both discrete and continuous domains
  were affected.

  `ExceptEngine` dispatched on the receiver's shape; each unbounded receiver's inner switch had an
  arm for a finite operand and one for an operand unbounded the *same* way, but none for the
  opposing one, so the `_` fallback rebuilt the receiver. That fallback is reachable *only* for the
  opposing-unbounded pair — an empty operand is filtered by the `Overlaps` guard and an infinite one
  by the `Contains` guard — so it was wrong on every call that reached it.

- **⚠️ `Contains` and `IsContainedBy` now agree that the empty range is contained by everything.**
  `[1,5].Contains(Int32Range.Empty)` returned `false` and now returns `true`, as does
  `Int32Range.Empty.IsContainedBy(anything)` and `Int32Range.Empty.Contains(Int32Range.Empty)`.

  ∅ ⊆ S for every S: "every value of the inner range is also in the outer" is vacuously satisfied
  when the inner range has no values. PostgreSQL's `@>` answers the same, so the previous behaviour
  put the two sides of the wire in disagreement — `r.Period.Contains(DateRange.Empty)` matched no
  row in memory and every row in SQL.

  It also disagreed with this library. `RangeSet.Contains(RangeSet)` has always answered `true` for
  an empty operand by iterating zero elements, and `RangeSet.From` drops empty elements — so
  `RangeSet.Empty` and `Int32Range.Empty` are each other's normalized form, and the two overloads
  were answering the same question two ways.

  **Migration.** Only comparisons with an explicitly empty operand change. Code that relied on
  `Contains` to mean "contains and is non-empty" should say so: `outer.Contains(inner) &&
  !inner.IsEmpty()`. `Overlaps` is unchanged and still `false` for an empty operand — overlap needs
  a shared value — so a guard that wanted "shares something" was always better written with it.

- **⚠️ `Int64Range.Contains(value)` produced SQL PostgreSQL refused to run**, whenever the value was
  a constant rather than a captured variable. The range operators are polymorphic
  (`anyrange @> anyelement`), and PostgreSQL resolves polymorphic operators without applying
  implicit coercions — so a bare `25`, which it types as `integer`, does not match `int8range`:

  ```
  WHERE t."Tickets" @> 25          →  42883: operator does not exist: int8range @> integer
  WHERE t."Tickets" @> 25::bigint  →  runs
  ```

  Constant element operands now carry an explicit cast when their store type is not the one
  PostgreSQL infers from a bare numeric literal. `Int64Range` and
  `RangeSet<Int64Range, long>` were the only types affected: every other element type renders
  self-describing literal text (`DATE '2024-06-15'`, `TIMESTAMP '…'`), and `integer`/`numeric`
  literals already arrive as the type their subtype wants — so no other emitted SQL changes.

  The translation test for this asserted `@> ` and stopped there, and no test executed the query,
  which is exactly the pair of gaps that let it ship.

- **⚠️ `IsStrictlyLeftOf` answered `false` for every range unbounded at its *start***, and
  `IsStrictlyRightOf` for every such operand. `<<` compares the receiver's **upper** bound with the
  operand's **lower** bound, so `(-∞, 5]` — which has a perfectly finite upper bound — is strictly
  left of `[10, 20]`. The implementation switched on the receiver's shape and handled only
  `IFiniteRange<T>` there, while its inner switch handled unbounded *operands*:

  ```
  ((-∞,5]).IsStrictlyLeftOf([10,20])     false     ← before
  '(,5]'::int4range << '[10,20]'         true      ← PostgreSQL, and now the model
  ```

  The disagreement was **between the two sides of the wire**: the EF translation emits `<<` and
  was always right, so the same predicate answered `true` when it ran in the database and `false`
  when it ran in memory — over the same two values. `RangeSet.IsStrictlyLeftOf`/`RightOf` inherited
  it through their outermost element, so a one-element multirange `{(,5]}` was affected too.

  This is the same receiver-vs-operand asymmetry as the `IsAdjacentTo` bug fixed in 6.2.1, in the
  one other predicate whose answer depends on the receiver's shape. It is now decided by reading
  the two bounds rather than by switching on the receiver, so the two directions cannot drift
  again. **Behaviour changes for anyone comparing an unbounded-start range with `IsStrictlyLeftOf`
  or `IsStrictlyRightOf` in memory** — from a wrong `false` to the answer PostgreSQL already gave.

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

- **`Count` on a value set carries the canonical-writers precondition, as `==` does.** The
  documentation claimed only `==` did. `Count` translates to `cardinality`, which ignores order but
  not duplicates, so a row another tool stored as `{b,a,b}` reads back as the two-element set
  `{a,b}` while the server counts three. No behaviour changed — `docs/efcore.md` is corrected and
  the live suite now pins it. `IsEmpty` is unaffected.

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

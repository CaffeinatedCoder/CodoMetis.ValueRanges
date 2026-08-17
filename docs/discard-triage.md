# Discard-arm triage

A `_ =>` arm that produces a value is how five bugs shipped: it answers the cases nobody wrote an
arm for, plausibly enough that nothing throws and a debugger looks right. `EngineDispatchConventionTests`
bans them mechanically inside `src/**/Internals/`, where all five lived. It cannot be extended
repo-wide, because plenty of discards elsewhere are correct and load-bearing — `Contains`'s
`_ => false` for an empty *receiver* is the specification, not a gap.

So the rest were triaged by hand, once, in 8.0.0. This is the record: what was found, and — more
usefully — which arms are covered by something mechanical and which rest on an argument.

**60 arms outside `Internals/`. One defect. One cosmetic fix. Six promoted from argued to swept.**

## Already refusing (11)

`RangeProviderConversion`, `BridgedElementTypeMapping.Convert`, `ValueRangeTypeMapping`,
`ValueRangeSetTypeMapping`, `YearMonthRangeTypeDefinition` (×3), `NodaTimeInteropExtensions` (×2),
`RangeSetHelpers.FiniteLowerBound`, `RangeSet.BoundsOf`. All throw on an unrecognised variant.
Nothing to do.

## Total by construction (10)

Not shape dispatch at all, so the discard is the last case of a closed set:

- **`CreateFinite` in the six continuous types** — `start.CompareTo(end) switch { > 0, 0, _ }`. The
  discard is `< 0`, the only remaining sign.
- **`RangeSet.CollapseSingletons`, `NormalizeCollected`, `Merge()`** — switches on `Length`/`Count`
  where the discard is "two or more". `Merge()` is correct only because elements are sorted, which
  is the set invariant.
- **`RangeSet.NormalizeSingle`** — discard is "one of the three bounded shapes", all handled alike.

## Correct and now swept (6)

These were right, and the argument was sound, but nothing checked them: they take an element or
nothing at all, so no shape-pair matrix reaches them. `SmallModelOracleTests` now sweeps them —
`LowerBound`, `UpperBound`, `LowerBoundInclusive`, `UpperBoundInclusive`, `Clamp`, and
`Contains(T)` as the model's axiom.

Predicting a bound's *value* does not work: a discrete range canonicalizes, so `(1,5)` reports its
lower bound as `2`, and an exclusive continuous bound is a value the range does not contain. The
sweep grounds them in three independent links instead — nullness from the specification's shape,
inclusivity cross-checked against `Contains`, and `Clamp` against the bounds those establish.

Watch for one trap when extending it: a finite specification with no values between its bounds
(`[1,1)`, `(1,1)`, and on a discrete domain `(1,2)`) *is* the empty range, because `CreateFinite`
collapses it. Predict from the collapsed shape, not the requested one.

## Correct because exhaustively swept (17)

Every discard in `Contains(IRange)`, `Overlaps`, `IsStrictlyLeftOf`, `DoesNotExtendRightOf`,
`DoesNotExtendLeftOf` and `IsAdjacentTo` — inner and outer. These are the arms that hid the five
bugs, and they are the ones now under the most pressure: `SmallModelOracleTests` checks every one
against set theory over every shape and bound configuration in two domains, and
`ShapeMatrixParityTests` asks PostgreSQL the same questions. Left as values rather than throws
because an empty or infinity operand genuinely reaches them and genuinely has an answer.

## Correct EF idiom (4)

`ValueRangesAggregateMethodCallTranslator`, `ValueRangesQueryExpressionInterceptor`,
`ValueSetsMemberTranslator` (×2). Returning `null` from a translator means "not mine, try the next
one" — the discard is how EF composition works, not a gap. `DerivesFromUnion`'s `_ => false`
terminates a recursion.

## Fixed (2)

- **`IRangeFactory.ToString` answered `"empty"`.** All five shapes are named above it, so the
  discard is only reachable through an `IRange<T>` implementation that is none of them — which the
  sealed-variant rule forbids but the type system permits, since the interface is public. `"empty"`
  is the worst available answer: that text is what `Parse` round-trips, what the EF literal sends
  to PostgreSQL, and what the shape matrix compares against the server, so an unrecognised range
  would have been stored, queried and asserted as the empty range with nothing raised anywhere. Now
  throws; `UnknownRangeVariantTests` implements a rogue variant and pins it.
- **`BridgedElementTypeMapping.Of` fell through to `ToString()` for `string`.** Right answer,
  reached by accident — `string` is not `IFormattable`, so it missed every named arm. Named
  explicitly, and the discard now throws for a genuinely unknown element type rather than handing
  PostgreSQL whatever `ToString` produced.

## The rule this leaves

A discard arm is acceptable when it is the last case of a closed set (a comparison sign, a count),
when it is a documented specification (`null` for an absent bound, `false` for an empty operand), or
when it is an EF "not mine". It is not acceptable as the answer to *a shape nobody thought about* —
there it must throw, and in `Internals/` the convention test enforces that.

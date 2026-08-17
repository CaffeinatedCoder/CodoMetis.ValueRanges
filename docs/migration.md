# Migration guide

Source changes required when moving between majors. Each release is also described in full in
the [changelog](../CHANGELOG.md); this page collects only the parts that need an edit at the
call site.

## Migration from v1.x

### `ToString()` now returns a PostgreSQL range literal

In v1.x, calling `.ToString()` on any range variant returned the default C# record representation:

```
Finite { Start = 1, End = 10, StartInclusive = True, EndInclusive = True }
```

From v2.0.0, `ToString()` returns the PostgreSQL range literal:

```
[1,10]
```

If your code depended on the old format for logging, display, serialization, or string comparison, update it to use the new literal format or, if you need the structural representation, reconstruct it from the variant's properties via pattern matching.

## Migration from v2.x

### State-check methods now require parentheses

`IsEmpty`, `IsFinite`, `IsInfinity`, `IsUnboundedStart`, and `IsUnboundedEnd` were extension properties in v2.x. In v3.0.0 they are extension methods — add parentheses at every call site:

```csharp
// v2.x
if (range.IsEmpty) { … }

// v3.0.0
if (range.IsEmpty()) { … }
```

The change is mechanical and the compiler will flag every affected site. The motivation is EF Core compatibility: extension properties cannot appear in LINQ expression trees, preventing SQL translation. As extension methods they are fully translated by the EF Core companion package — see [Entity Framework Core](efcore.md).

## Migration from v3.x

### `RangeSet` `==` is now structural

`RangeSet<TRange, T>` defines `operator ==`/`!=` as value equality, delegating to `Equals` — consistent with the range types themselves (records) and with the SQL `=` the EF Core provider generates. Code that compared sets with `==` previously got reference equality; recompiling against v4 silently changes those call sites to value comparison. If you relied on reference identity, switch to `ReferenceEquals(a, b)`.

### `DoesNotExtendRightOf`/`LeftOf` now match PostgreSQL for infinite bounds

An unbounded receiver previously always returned `false`. In v4, an infinite bound compares equal to another infinite bound — `[5, +∞).DoesNotExtendRightOf([100, +∞))` is now `true` (`+∞ ≤ +∞`), matching the `&<`/`&>` operators exactly. Results against finite-bounded or empty operands are unchanged.

Everything else in v4 is additive — no other source changes are required.

## Migration from v4.x and v5.x

**No source changes.** Both majors are additive, and each was bumped to mark the model growing
rather than to signal a break: v5 added `TimeRange` and `YearMonthRange`, v6 added the value set
family. Recompiling against either requires no edit.

## Migration from v6.x

v7.0.0 changed what several existing calls *answer*. Nothing was removed or resignatured, so the
compiler will not flag any of these — each one needs a decision about whether your code depended on
the old result.

### The empty range is now contained by everything

`[1,5].Contains(Int32Range.Empty)` returned `false` and now returns `true`, as does
`Int32Range.Empty.IsContainedBy(anything)` and `Int32Range.Empty.Contains(Int32Range.Empty)`. ∅ ⊆ S
for every S, which is also what PostgreSQL's `@>` answers — the previous behaviour put memory and
SQL in disagreement.

Only comparisons with an explicitly empty operand change. Code that relied on `Contains` to mean
"contains and is non-empty" should now say so:

```csharp
outer.Contains(inner) && !inner.IsEmpty()
```

`Overlaps` is unchanged and still `false` for an empty operand, so a guard that meant "shares a
value" was always better written with it.

### `IsStrictlyLeftOf`/`IsStrictlyRightOf` now answer correctly for unbounded-start ranges

`((-∞,5]).IsStrictlyLeftOf([10,20])` returned `false` and now returns `true`. `<<` compares the
receiver's *upper* bound with the operand's *lower* bound, and `(-∞, 5]` has a perfectly finite
upper bound. The EF translation always emitted `<<` and was always right, so the same predicate
answered `true` in the database and `false` in memory. One-element `RangeSet`s inherited it.

No edit is required unless you compensated for the wrong answer.

### `Except` no longer keeps what it was asked to remove

`((-∞,5]).Except([1,+∞))` returned `{(-∞,5]}` — the receiver, unchanged — where the answer is
`{(-∞,0]}`; symmetrically for the mirrored pair. It only affected operands unbounded in *opposite*
directions, and `RangeSet.Except` reached the same engine. Every element type was affected.

This is the one to look hardest at, because the old result was a well-formed range of the right
shape carrying the wrong values — a subtraction that quietly kept part of what it removed.

### `DecimalRange.Length` returns `null` instead of throwing

A range wider than `decimal` itself (only one straddling zero can be) raised `OverflowException`
and now returns `null`, matching what `Int64Range.Length` already did for an uncountable span. Only
code catching that exception is affected.

### Numeric wrapper arities honour `JsonNumberHandling.WriteAsString`

`Int16Set<T>`, `Int32Set<T>`, `Int64Set<T>` and `DecimalSet<T>` wrote a bare JSON number under
`WriteAsString` where their primitive siblings wrote a string:

```
Int64Set              ["9007199254740993"]
Int64Set<OrderId>     [9007199254740993]     ← v6
Int64Set<OrderId>     ["9007199254740993"]   ← v7
```

**Payloads change for anyone serializing a numeric wrapper arity under that setting.** Reads are
unaffected — the numeric converters have always accepted a JSON string. If a consumer pinned the old
shape, give the element type its own `[JsonConverter]`.

### Temporal wrapper elements must forward the format specifier

The six temporal arities added in v7 (`DateSet<T>`, `TimeSet<T>`, `DateTimeSet<T>`,
`DateTimeOffsetSet<T>`, and the NodaTime ones) require the element's `ToString(format, provider)` to
forward its `format` argument, because the default text form is lossy — `TimeOnly` renders as
`09:30`, `DateTime` as `06/15/2024 10:30:00`. A wrapper that swallows the argument is rejected at the
persistence boundary with an error naming the type and the contract, rather than storing a truncated
value. Generator-produced wrappers already forward it; hand-written ones may not. See [the text-form
contract](value-sets.md#the-text-form-contract).

### One thing that starts working

`Int64Range.Contains(25)` with a *constant* operand emitted SQL PostgreSQL refused to run
(`42883: operator does not exist: int8range @> integer`). Constant element operands now carry an
explicit cast. No edit needed — queries that threw now execute.

## Migration from v7.x

v8.0.0 is a major for the same reason: several corrections change what existing calls answer, and one
changes a query that ran into one that throws. No signature changed anywhere.

### Equality over a server-computed value set `Union` now fails translation

This is the only change here that turns working code into an exception, and it is first because it is
the most likely to reach you:

```csharp
// v7: ran, and returned wrong rows.  v8: throws at translation.
db.Rows.Where(r => r.Tags.Union(other) == expected);
```

`Union` translates to `array_cat`, which concatenates rather than canonicalizes — the result carries
duplicates and keeps each operand's ordering, while array equality is sensitive to both. Against a
live server `{a,c} ∪ {a,b} = {a,b,c}` was `false` for the repeated `a`, and `{a,c} ∪ {b} = {a,b,c}`
was `false` too, where nothing repeats and only the order differs. In memory both are `true`.

`==`, `!=` and `Equals` over a union now throw with a message naming the alternatives. **If a query
relied on this comparison it was already returning wrong rows.** Your options:

- Use the order- and multiplicity-insensitive operators, which still compose on a union:
  `Contains`, `Overlaps`, `IsSubsetOf`, `IsSupersetOf`, `IsEmpty`, and the proper subset/superset
  pair.
- Materialize first with `AsEnumerable()` to get the in-memory answer deliberately.
- Keep it in a **projection**, where it still works — EF falls back to client evaluation there and
  computes against the materialized set.

The refusal covers only the contexts EF must translate in full: `Where`, `Any`, `All`, the predicate
overloads, and the ordering and grouping keys. It matches how `Count` over a union has been refused
since 6.2.0.

### `RangeSet.Except` with an infinity operand, and `Complement()`

`RangeSet<Int32Range, int>.Infinite.Except(Int32Range.Infinite)` answered `{(,)}` — the whole domain
— where `X \ (-∞, +∞)` is the empty set for every `X`. `Complement()` on the infinite set was wrong
through the same path. Both now return the empty set.

### `YearMonthRange` step functions reject a non-ISO calendar

`NextValueAfter`/`PreviousValueBefore` stepped in whatever calendar they were handed and could return
a value the type's own constructors refuse. They now reject a non-ISO argument outright. Pass ISO
values, or convert first — `LocalDateRange` normalizes to ISO instead of rejecting.

### Two calls that threw now answer

Both were loud failures, so only code catching them is affected:

- `RangeSet<Int32Range, int>.Infinite.Contains(value)` raised `InvalidOperationException` for every
  value and now answers `true` — the infinite set contains everything.
- The NodaTime step functions threw at a Gregorian-spelled domain maximum and now answer `null`.

### If you implement `IRange<T>` yourself

Don't — the sealed-variant rule forbids it and always has. But note that `IRangeFactory.ToString`
used to format an unrecognised range as `"empty"`, which is the literal PostgreSQL round-trips, so
such a value would have been stored and queried as the empty range with nothing raised. It now
throws.


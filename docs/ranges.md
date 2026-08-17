# Ranges and range sets

The complete in-memory algebra: constructing ranges, matching on their shape, querying and
combining them, and the `RangeSet<TRange, T>` multirange counterpart. Everything here runs in
process with no database involved — [Entity Framework Core](efcore.md) covers what the same
calls translate to in SQL, and [What runs where](efcore.md#what-runs-where) is the exhaustive
list of which ones do.

## Creating Ranges

Every type exposes four static factory methods:

```csharp
// Bounded on both sides
Int32Range closed = Int32Range.CreateFinite(1, 10);                       // [1, 10]
Int32Range half   = Int32Range.CreateFinite(1, 10, endInclusive: false);  // [1, 10)

// Unbounded on the left — end exclusive by default
DateRange upToToday = DateRange.CreateUnboundedStart(DateOnly.FromDateTime(DateTime.Today)); // (-∞, today)
// Inclusive variant:
DateRange throughToday = DateRange.CreateUnboundedStart(DateOnly.FromDateTime(DateTime.Today), endInclusive: true);

// Unbounded on the right — start inclusive by default
Int32Range fromFive = Int32Range.CreateUnboundedEnd(5);  // [5, +∞)

// Unbounded on both ends
Int32Range everything = Int32Range.Infinite;  // (-∞, +∞)

// Explicitly empty
Int32Range empty = Int32Range.Empty;
```

`CreateFinite()` automatically returns an `Empty` when the arguments form a degenerate or inverted interval (e.g. `start > end`, or equal bounds that are both exclusive).

**Default boundary inclusiveness:**

| Range type                                                 | `CreateFinite()` default   |
|------------------------------------------------------------|----------------------------|
| `Int32Range`, `Int64Range`, `DateRange`                    | `[start, end]` — closed    |
| `DecimalRange`, `DateTimeRange`, `DateTimeOffsetRange`     | `[start, end)` — half-open |

Discrete types default to fully closed intervals; continuous types default to the half-open convention that is conventional for monetary amounts and timestamps.

## Pattern Matching

The nested sealed records are first-class citizens and ideal for exhaustive pattern matching:

```csharp
string Describe(Int32Range range) => range switch
{
    Int32Range.EmptyRange       => "empty",
    Int32Range.Finite f         => $"[{f.Start}, {f.End}]",
    Int32Range.UnboundedStart s => $"(-∞, {s.End}]",
    Int32Range.UnboundedEnd e   => $"[{e.Start}, +∞)",
    Int32Range.Infinity         => "(-∞, +∞)",
    _                           => throw new UnreachableException(),   // System.Diagnostics
};
```

The five variants are the only ranges that can exist: the private constructor on the abstract base
record prevents any subtype being declared outside the assembly. So the five arms are complete in
fact, and the discard is genuinely unreachable.

C# does not prove that, though — its exhaustiveness analysis does not reason about a closed class
hierarchy, so a switch *expression* over the variants warns `CS8509` without a discard arm, and
adding a `null` arm does not help (the complaint just moves to `not null`). Keep the discard and make
it **throw** rather than return a value: that is the same rule the library applies to its own shape
dispatches, and for the same reason — a fallback that produces a plausible answer is how four
range-algebra bugs stayed hidden ([architecture](architecture.md)). A switch *statement* needs no
discard, and `if`/`is` chains are unaffected.

## Query Operations

All query methods are extension methods on `IRange<T>` and work across any combination of range shapes.

### Containment

```csharp
var sprint = DateRange.CreateFinite(new DateOnly(2025, 1, 6), new DateOnly(2025, 1, 17));

sprint.Contains(new DateOnly(2025, 1, 10));  // true  — point containment
sprint.Contains(new DateOnly(2025, 1, 20));  // false

var inner = DateRange.CreateFinite(new DateOnly(2025, 1, 8), new DateOnly(2025, 1, 14));
sprint.Contains(inner);       // true  — range containment
inner.IsContainedBy(sprint);  // true  — symmetric alias

sprint.Contains(DateRange.Empty);        // true  — ∅ ⊆ S, for every S
DateRange.Empty.Contains(DateRange.Empty);  // true  — including itself
DateRange.Empty.Contains(sprint);        // false — ∅ contains nothing
```

The empty range is contained by every range, which is the vacuous reading of "every value of the
inner range is also in the outer" and matches PostgreSQL's `@>`. Containment and *overlap* part
ways here: `sprint.Overlaps(DateRange.Empty)` is `false`, because overlap needs a shared value and
the empty range has none.

### Overlap

```csharp
var a = Int32Range.CreateFinite(1, 5);
var b = Int32Range.CreateFinite(5, 10);
var c = Int32Range.CreateFinite(6, 10);

a.Overlaps(b);  // true  — they share the point 5
a.Overlaps(c);  // false
```

### Adjacency

Two ranges are adjacent when they are contiguous with no gap and no overlap — their union would form a single range.

```csharp
// Discrete: consecutive integer values are adjacent
var a = Int32Range.CreateFinite(1, 5);
var b = Int32Range.CreateFinite(6, 10);
a.IsAdjacentTo(b);  // true — NextValueAfter(5) == 6

// Continuous: touching bounds with complementary inclusiveness
var x = DecimalRange.CreateFinite(1m, 5m, endInclusive: true);      // [1, 5]
var y = DecimalRange.CreateFinite(5m, 10m, startInclusive: false);  // (5, 10)
x.IsAdjacentTo(y);  // true — one side claims 5, the other does not
```

An unbounded range is adjacent on its bounded edge, and the relation is symmetric — the receiver's shape does not change the answer, matching PostgreSQL's `-|-`:

```csharp
var upTo    = Int32Range.CreateUnboundedStart(0, true);  // (-∞, 0]
var from    = Int32Range.CreateUnboundedEnd(1);          // [1, +∞)
var between = Int32Range.CreateFinite(1, 3);             // [1, 3]

upTo.IsAdjacentTo(between);  // true      between.IsAdjacentTo(upTo);  // true
upTo.IsAdjacentTo(from);     // true — the two halves close the domain with no overlap

// The empty and infinite ranges are adjacent to nothing, and two ranges open at the
// same end always overlap:
Int32Range.Infinite.IsAdjacentTo(between);                        // false
upTo.IsAdjacentTo(Int32Range.CreateUnboundedStart(9, true));      // false
```

> **Changed in 6.2.1.** Before 6.2.1 `IsAdjacentTo` answered `false` whenever the *receiver* was unbounded, so the relation was asymmetric and disagreed with PostgreSQL. Because `RangeSet` normalization merges neighbours after sorting by lower bound — which always puts an unbounded-start element in the receiver position — `RangeSet.From([(,0], [1,)])` returned `{(,0],[1,)}` instead of `{(,)}`. See the [changelog](../CHANGELOG.md).

### Measuring a range

`Length` reports what a range covers. The convention follows the domain: a discrete one counts its values inclusive of both bounds, a continuous one measures the span between them.

```csharp
DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)).Length;  // 31 (days)
Int32Range.CreateFinite(1, 10).Length;                                              // 10 (integers)
DecimalRange.CreateFinite(1m, 5m).Length;                                           // 4  (span)

DateTimeRange.CreateFinite(nineAm, fiveThirtyPm).Length;  // TimeSpan of 8.5 hours
```

Empty and unbounded are different answers and stay distinguishable — the empty range contains nothing, an unbounded one contains too much to measure:

```csharp
Int32Range.Empty.Length;     // 0
Int32Range.Infinite.Length;  // null
```

The type follows the domain: `long?` for the integer ranges, `int?` days for `DateRange`, `TimeSpan?` for the timestamp ranges, `decimal?` for `DecimalRange`, and `Duration?`/`Period?` for the NodaTime ranges — an instant range measures exact elapsed time, a wall-clock range a calendar quantity. `Length` is client-side and does not translate to SQL.

### Enumerating a discrete range

The discrete range types enumerate what they contain. The continuous ones do not declare `Values()` at all, so the mistake is caught at compile time rather than at runtime:

```csharp
foreach (var day in DateRange.CreateFinite(monday, friday).Values())
    Schedule(day);

Int32Range.CreateFinite(1, 5).Values();   // 1, 2, 3, 4, 5
DecimalRange.CreateFinite(1m, 5m).Values();  // does not compile — no step to walk
```

An unbounded range throws `NotSupportedException` at the call rather than at the first iteration, so the failure points at the line that was wrong.

### Clamping a value into a range

```csharp
var year = DateRange.CreateFinite(jan1, dec31);

year.Clamp(new DateOnly(2020, 5, 5));  // jan1  — pulled up to the lower bound
year.Clamp(new DateOnly(2024, 6, 15)); // unchanged — already inside
DateRange.Empty.Clamp(anyDate);        // null — nothing to snap to
```

An unbounded side never constrains: clamping into `(-∞, 10]` only ever pulls a value down.

### Directional Comparisons

```csharp
Int32Range.CreateFinite(1, 3).IsStrictlyLeftOf(Int32Range.CreateFinite(5, 9));  // true
Int32Range.CreateFinite(1, 5).IsStrictlyLeftOf(Int32Range.CreateFinite(5, 9));  // false — they share 5

Int32Range.CreateFinite(7, 9).IsStrictlyRightOf(Int32Range.CreateFinite(1, 5)); // true
```

**PostgreSQL `&<` / `&>` equivalents:**

```csharp
// Does not extend to the right of other  (&<)
Int32Range.CreateFinite(1, 5).DoesNotExtendRightOf(Int32Range.CreateFinite(1, 10));  // true

// Does not extend to the left of other  (&>)
Int32Range.CreateFinite(3, 10).DoesNotExtendLeftOf(Int32Range.CreateFinite(1, 10));  // true
```

### Bound Accessors

The PostgreSQL `lower` / `upper` / `lower_inc` / `upper_inc` functions, on any range shape. The variants expose `Start`/`End` only where they exist structurally; the accessors provide the dynamic view: `T?` with `null` for a missing bound — exactly PostgreSQL's `NULL` semantics.

```csharp
Int32Range.CreateFinite(1, 10).LowerBound();          // 1
Int32Range.CreateFinite(1, 10).UpperBoundInclusive(); // true

Int32Range.CreateUnboundedStart(5, true).LowerBound(); // null — no lower bound
Int32Range.Empty.UpperBound();                         // null
Int32Range.Infinite.LowerBoundInclusive();             // false

// On RangeSet: the first element's lower bound, the last element's upper bound.
var set = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9)]);
set.LowerBound();  // 1
set.UpperBound();  // 9
```

## Set Operations

Set operations are extension methods on the concrete range types (any type that implements `IRangeFactory<TRange, T>`).

### Intersection

Returns the largest range contained by both operands. The intersection of two ranges is always expressible as a single range, so `Intersect` returns the range type directly — `Empty` genuinely means an empty intersection.

```csharp
var a = Int32Range.CreateFinite(1, 10);
var b = Int32Range.CreateFinite(5, 15);

Int32Range intersection = a.Intersect(b);       // [5, 10]
a.Intersect(Int32Range.CreateFinite(11, 20));   // Empty — no overlap
```

All shape combinations are handled: `Finite ∩ UnboundedStart`, `UnboundedEnd ∩ UnboundedStart`, and so on, each producing the correctly shaped result type.

### Union

Returns a `RangeSet<TRange, T>` containing every value of both operands. When the ranges overlap or are adjacent, the set holds the single merged range; when they are disjoint, the set holds both — the union of two separated ranges genuinely *is* two ranges, and the result type says so.

```csharp
var a = Int32Range.CreateFinite(1, 5);
var b = Int32Range.CreateFinite(5, 10);
var c = Int32Range.CreateFinite(7, 10);

var ab = a.Union(b);  // { [1, 10] }        — overlapping, one element
var ac = a.Union(c);  // { [1, 5], [7, 10] } — disjoint, two elements

ab.Count;  // 1
ac.Count;  // 2
ac[1];     // [7, 10]
```

Merging an `UnboundedEnd` with an overlapping `Finite` yields an `UnboundedEnd`; an `UnboundedStart` overlapping an `UnboundedEnd` covers the entire domain and yields `{ Infinity }`.

### Except (Set Difference)

Removes the overlap of `other` from the receiver, returning a `RangeSet<TRange, T>` whose cardinality reflects the structural outcome directly.

```csharp
var range  = Int32Range.CreateFinite(1, 10);
var remove = Int32Range.CreateFinite(4, 6);

// [4, 6] is interior to [1, 10] — the result is split in two
var result = range.Except(remove);
// result[0] = [1, 4) ≡ [1, 3]
// result[1] = (6, 10] ≡ [7, 10]
```

| Result       | Meaning                                                            |
|--------------|--------------------------------------------------------------------|
| `0` elements | The receiver is fully contained by `other`; nothing remains        |
| `1` element  | One-sided trim or no overlap; the remaining range                  |
| `2` elements | `other` was strictly interior to the receiver; it is split in two  |

Boundary inclusiveness is inverted at the cut point so that no value is lost or double-counted across the resulting pieces.

### Merge (Convex Hull)

Returns the smallest single range containing both operands — PostgreSQL's `range_merge`. Unlike `Union`, the result also covers any gap between disjoint operands.

```csharp
var a = Int32Range.CreateFinite(1, 3);
var b = Int32Range.CreateFinite(10, 12);

a.Union(b);  // { [1, 3], [10, 12] } — two elements, the gap stays open
a.Merge(b);  // [1, 12]              — one range, the gap is covered

// Empty operands are ignored; unbounded edges span accordingly:
Int32Range.CreateUnboundedStart(3, true).Merge(Int32Range.CreateUnboundedEnd(10)); // (-∞, +∞)

// RangeSet.Merge() spans the whole set:
RangeSet<Int32Range, int>.From([a, b]).Merge(); // [1, 12]
```

### Aggregates

`RangeAgg()` and `RangeIntersectAgg()` aggregate a sequence of ranges — the in-memory counterparts of PostgreSQL's `range_agg` and `range_intersect_agg`:

```csharp
new[] { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(3, 8), Int32Range.CreateFinite(20, 25) }
    .RangeAgg();           // { [1, 8], [20, 25] } — a normalized RangeSet

new[] { Int32Range.CreateFinite(1, 10), Int32Range.CreateFinite(5, 15) }
    .RangeIntersectAgg();  // [5, 10] — the common intersection; null for an empty source
```

In EF Core queries they translate to the SQL aggregates inside `GroupBy` projections — see [Entity Framework Core](efcore.md).

## RangeSet — Multirange Support

`RangeSet<TRange, T>` is the in-memory counterpart of a PostgreSQL 14+ multirange (`int4multirange`, `nummultirange`, …): an immutable, always-normalized set of disjoint ranges. Its invariant — elements sorted by lower bound, pairwise disjoint, pairwise non-adjacent — is enforced on every construction: empty ranges are dropped, overlapping or adjacent inputs are merged, and any `Infinity` input collapses the set to `RangeSet<TRange, T>.Infinite`.

```csharp
using IntSet = RangeSet<Int32Range, int>;

// Construction normalizes: [1, 5] and [6, 10] are adjacent for int and merge.
var set = IntSet.From([
    Int32Range.CreateFinite(6, 10),
    Int32Range.CreateFinite(1, 5),
    Int32Range.CreateFinite(20, 30)
]);
// { [1, 10], [20, 30] }

// Query operations
set.Contains(7);                              // true
set.Contains(Int32Range.CreateFinite(2, 8));  // true  — within a single element
set.Overlaps(Int32Range.CreateFinite(15, 25)); // true

// Set operations — single-range and bulk variants, with operator aliases (|, &, -)
set.Union(Int32Range.CreateFinite(11, 19));    // { [1, 30] } — bridges the gap
set | Int32Range.CreateFinite(11, 19);         // { [1, 30] }

set.Intersect(Int32Range.CreateFinite(5, 25)); // { [5, 10], [20, 25] }
set & Int32Range.CreateFinite(5, 25);          // { [5, 10], [20, 25] }

set.Except(Int32Range.CreateFinite(4, 6));     // { [1, 3], [7, 10], [20, 30] }
set - Int32Range.CreateFinite(4, 6);           // { [1, 3], [7, 10], [20, 30] }

// Complement — every value not covered by the set
set.Complement();  // { (-∞, 0], [11, 19], [31, +∞) }

// State checks — isempty / lower_inf / upper_inf equivalents, plus the two derived shapes
set.IsEmpty();           // false
set.IsUnboundedStart();  // false
set.IsUnboundedEnd();    // false
set.IsFinite();          // true  — non-empty and bounded at both ends
set.IsInfinity();        // false — only the set covering the whole domain answers true

// Set-operand comparisons — the full multirange operator matrix
set.Contains(IntSet.From([Int32Range.CreateFinite(2, 8)]));   // true   (@>)
set.Overlaps(IntSet.From([Int32Range.CreateFinite(25, 40)])); // true   (&&)
set.IsStrictlyLeftOf(Int32Range.CreateFinite(40, 50));        // true   (<<)
set.DoesNotExtendRightOf(Int32Range.CreateFinite(1, 30));     // true   (&<)
set.IsAdjacentTo(Int32Range.CreateFinite(31, 40));            // true   (-|-)
```

**Adjacency mirrors PostgreSQL exactly:** it is *directional through the outer edges* — the operand must end exactly where the set's first element begins, or begin exactly where the set's last element ends. Touching any interior boundary, even the inner side of the first or last element, does not count ([verified against live PostgreSQL](../README.md#verified-against-postgresql)):

```csharp
var three = IntSet.From([
    Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9), Int32Range.CreateFinite(20, 22)
]);
three.IsAdjacentTo(Int32Range.CreateFinite(23, 25)); // true  — attaches after the last element
three.IsAdjacentTo(Int32Range.CreateFinite(4, 6));   // false — inner side of the first element
three.IsAdjacentTo(Int32Range.CreateFinite(10, 12)); // false — touches only the interior [7, 9]
```

The positional operators (`<<`, `>>`, `&<`, `&>`) likewise compare the first/last element's bounds.

**`IsInfinity()` is not the conjunction of the two unbounded checks.** For a single range it would be — a range is contiguous, so unbounded on both sides means the whole domain. A set can be open at both ends and still have a hole:

```csharp
var gapped = IntSet.From([
    Int32Range.CreateUnboundedStart(5, true),  // (-∞, 5]
    Int32Range.CreateUnboundedEnd(10)          // [10, +∞)
]);

gapped.IsUnboundedStart();  // true
gapped.IsUnboundedEnd();    // true
gapped.Contains(7);         // false — the gap
gapped.IsInfinity();        // false

IntSet.Infinite.IsInfinity();  // true — the only set that covers everything
```

Because normalization collapses any `Infinity` input to the one-element `Infinite` set, that set is the unique representation of full coverage, which is what lets the check be exact both in memory and in SQL.

### Collection expressions

`RangeSet<TRange, T>` supports collection expressions, and they normalize exactly as `From` does:

```csharp
RangeSet<Int32Range, int> set = [
    Int32Range.CreateFinite(10, 12),
    Int32Range.CreateFinite(1, 3),
    Int32Range.CreateFinite(2, 5)
];
// { [1, 5], [10, 12] } — sorted and merged, not wrapped as written

RangeSet<Int32Range, int> none = [];                                  // the Empty singleton
RangeSet<Int32Range, int> all  = [Int32Range.Infinite, someRange];    // the Infinite singleton
```

The builder behind it is the non-generic `RangeSet.Create<TRange, T>`, since a `[CollectionBuilder]` target cannot itself be generic. Prefer the collection expression over calling it: C# does not infer type arguments from constraints, so `T` cannot be deduced from `TRange` and a direct call has to name both. There is also a `From(params ReadOnlySpan<TRange>)` overload beside `From(IEnumerable<TRange>)`.

The set implements `IReadOnlyList<TRange>` (enumeration in lower-bound order, `Count`, indexer) and structural equality, including `==`/`!=`: two sets built from different inputs that normalize identically are equal.

```csharp
var a = IntSet.From([Int32Range.CreateFinite(1, 10)]);
var b = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(6, 10)]);
a.Equals(b);  // true — both normalize to { [1, 10] }
a == b;       // true — same value semantics as the ranges themselves
```

### Sorting ranges externally

The set's internal lower-bound ordering is exposed as `RangeSet<TRange, T>.LowerBoundComparer` — an `IComparer<TRange>` singleton for sorting arbitrary `List<TRange>`s the same way the set does, for example to pre-sort inputs before handing them to `From`. `IUnboundedStartRange<T>` sorts first (its lower bound is -∞); at the same finite value, an inclusive lower bound sorts before an exclusive one (`[5, …` before `(5, …`).

```csharp
var unsorted = new List<Int32Range>
{
    Int32Range.CreateFinite(20, 30),
    Int32Range.CreateFinite(1, 5),
    Int32Range.CreateUnboundedStart(10, true)
};

unsorted.Sort(RangeSet<Int32Range, int>.LowerBoundComparer);
// { (-∞, 10], [1, 5], [20, 30] }
```

The same instance is available as `RangeLowerBoundComparer<Int32Range, int>.Instance` for contexts where you only have the comparer type and not the set type.

## Interface Overview

The library exposes a structured set of interfaces for writing generic code:

| Interface                  | Purpose                                                            |
|----------------------------|--------------------------------------------------------------------|
| `IRange<T>`                | Base marker for all range types                                    |
| `IFiniteRange<T>`          | `Start`, `End`, and their inclusiveness flags                      |
| `IUnboundedStartRange<T>`  | `End` and `EndInclusive`                                           |
| `IUnboundedEndRange<T>`    | `Start` and `StartInclusive`                                       |
| `IEmptyRange<T>`           | Marker for the empty range; no bound properties                    |
| `IInfinityRange<T>`        | Marker for the range covering the entire domain                    |
| `IRangeFactory<TRange, T>` | Abstract static factories; also `NextValueAfter`/`PreviousValueBefore` for step-aware (discrete) types |

`T` is constrained to `struct, IComparable<T>, IEquatable<T>` throughout.

`IRangeFactory<TRange, T>` and `IValueSetFactory<TSet, T>` both extend `ISpanParsable<TSelf>` (and so `IParsable<TSelf>`) and `IFormattable`, which is what lets generic code parse and format any range or set without knowing the concrete type:

```csharp
static T Load<T>(ReadOnlySpan<char> literal) where T : ISpanParsable<T>
    => T.Parse(literal, CultureInfo.InvariantCulture);
```

For sorting ranges externally, `RangeLowerBoundComparer<TRange, T>` (an `IComparer<TRange>` singleton) exposes the same lower-bound ordering the set uses internally. See [Sorting ranges externally](#sorting-ranges-externally).

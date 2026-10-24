# Ranges and range sets

This page shows you how to create ranges, match on their kind, ask questions about them and
combine them. It also covers `RangeSet<TRange, T>`, which holds several ranges as one value, like a
PostgreSQL multirange. Everything here runs in memory, with no database involved.
[Entity Framework Core](efcore.md) shows what the same calls become in SQL, and
[What runs where](efcore.md#what-runs-where) lists exactly which ones run in the database.

## Create a range

Every range type has three factory methods and two properties, one for each kind of range:

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

`CreateUnboundedStart` excludes its end unless you pass `endInclusive: true`. `CreateUnboundedEnd`
includes its start by default.

Which bounds `CreateFinite` includes by default depends on the element type:

| Range type                                                 | `CreateFinite()` default   |
|------------------------------------------------------------|----------------------------|
| `Int32Range`, `Int64Range`, `DateRange`                    | `[start, end]` — closed    |
| `DecimalRange`, `Float64Range`, `Float32Range`, `DateTimeRange`, `DateTimeOffsetRange` | `[start, end)` — half-open |

Discrete types, whose values have a next value (integers and dates), include both bounds.
Continuous types include the start and exclude the end. That half-open form is the usual
convention for money amounts and timestamps.

Good to know:

- **An impossible range comes back empty.** `CreateFinite()` returns `Empty` when the bounds form
  an empty or inverted interval. Examples are `start > end`, or equal bounds that are both
  exclusive. Parsing is stricter: an inverted literal such as `[5,1)` fails, as it does in
  PostgreSQL ([Parse a literal](serialization.md#parse-a-literal)).

### Create a month, a quarter, a year or a week

The date ranges have factories for the calendar ranges you would otherwise build by hand:

```csharp
DateRange.Month(2026, 10);    // [2026-10-01,2026-10-31]
DateRange.Quarter(2026, 4);   // [2026-10-01,2026-12-31]
DateRange.Year(2026);         // [2026-01-01,2026-12-31]
DateRange.IsoWeek(2026, 1);   // [2025-12-29,2026-01-04] — Monday to Sunday

var today = new DateOnly(2026, 8, 15);
DateRange.Month(today);       // [2026-08-01,2026-08-31] — the month that holds today
DateRange.IsoWeek(today);     // [2026-08-10,2026-08-16]
```

Each factory takes either the numbers of the period or any day inside it. You get a `DateRange`
like any other, so you can pass it to `Overlaps`, `Except` or `Gaps`.

`LocalDateRange` in the NodaTime package has the same factories, taking a `LocalDate`.
`YearMonthRange` has `Quarter` and `Year`: `YearMonthRange.Quarter(2026, 4)` is `[2026-10,2026-12]`.

Good to know:

- **Weeks are always ISO 8601, never your culture's.** A week starts on Monday, and week 1 is the
  week that holds the year's first Thursday. The current culture's first day of the week plays no
  part, the same way it plays no part in a literal.
- **The week-year can differ from the calendar year.** January 1, 2027 is in week 53 of 2026, so
  `DateRange.IsoWeek(new DateOnly(2027, 1, 1))` is `DateRange.IsoWeek(2026, 53)`. A week-year has
  52 or 53 weeks.
- **A number out of range throws.** A month outside 1 to 12, a quarter outside 1 to 4, or a week
  past the last of its week-year throws `ArgumentOutOfRangeException`. So does a week that runs past
  the last date the type can hold, such as week 52 of 9999.
- **A `LocalDate` in another calendar is read as its ISO day.** `LocalDateRange.Month` returns the
  ISO month that holds that day, as every bound of this type is ISO.
- **In an EF Core query, pass values your code already has.** `b.Period.Overlaps(DateRange.Month(2026, 10))`
  runs as SQL with the month as one constant. A factory that reads a column, such as
  `DateRange.Month(b.Day)`, runs in memory only
  ([What runs where](efcore.md#what-runs-where)).

## Pattern matching

Every range is one of five kinds, which this page calls its *shape*: `Finite`, `UnboundedStart`,
`UnboundedEnd`, `Infinity` and `EmptyRange`. Each shape is a nested sealed record, so you can
switch on it:

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

Every shape that carries a bound also deconstructs into exactly what its constructor takes. So
positional patterns and deconstruction work on all of them:

- **A discrete type's `Finite`** gives `(start, end)`, both inclusive. That is its canonical form.
- **A continuous type's `Finite`** gives `(start, end, startInclusive, endInclusive)`.

```csharp
var (first, last) = (DateRange.Finite)period;   // pass straight back to DateRange.CreateFinite

if (price is DecimalRange.Finite(var low, var high, var lowInclusive, var highInclusive)) { … }
```

A continuous type has no two-value form, on purpose. If you passed only the bounds back to
`CreateFinite`, they would pick up its half-open default, and `(1.5,2]` would come back as
`[1.5,2)`.

### Why the discard arm must throw

The five shapes are the only ranges that can exist. A record derived from a range type compiles,
because C# gives every record a protected copy constructor, but the first `new` of it throws
(`InvalidOperationException`). Only the five variants nested in the type can be constructed. Your
five arms are complete in fact, and the discard arm can never run.

C# can't prove that, though. Its exhaustiveness check doesn't reason about a closed class
hierarchy. So a switch *expression* over the shapes warns `CS8509` unless it has a discard arm.
Adding a `null` arm doesn't help: the warning moves to `not null`.

Keep the discard arm, and make it **throw** rather than return a value. The library follows the
same rule in its own shape dispatches, for the same reason. A fallback that returns a plausible
answer is how four range-algebra bugs stayed hidden ([architecture](architecture.md)).

Good to know:

- **A switch *statement* needs no discard arm.** `if`/`is` chains aren't affected either.

## Ask questions about a range

Every query method is an extension method on `IRange<T>`. Each one works for any combination of
shapes.

### Check containment

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

`Contains` takes a value or a range, and `IsContainedBy` asks the same question the other way
round.

The empty range is contained by every range, including itself. That is the vacuous reading of
"every value of the inner range is also in the outer one", and it matches PostgreSQL's `@>`.

Good to know:

- **Containment and overlap differ for the empty range.** `sprint.Overlaps(DateRange.Empty)` is
  `false`, because overlap needs a shared value, and the empty range has none.

### Check overlap

```csharp
var a = Int32Range.CreateFinite(1, 5);
var b = Int32Range.CreateFinite(5, 10);
var c = Int32Range.CreateFinite(6, 10);

a.Overlaps(b);  // true  — they share the point 5
a.Overlaps(c);  // false
```

### Check adjacency

Two ranges are adjacent when they touch with no gap and no overlap. Their union would be a single
range.

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

- **For a discrete type**, consecutive values touch. `[1, 5]` and `[6, 10]` are adjacent because
  `NextValueAfter(5)` is `6`.
- **For a continuous type**, the two bounds must be equal, and exactly one of the two ranges
  includes that value.

An unbounded range is adjacent on its bounded edge. The relation is symmetric: the shape of the
range you call it on doesn't change the result. This matches PostgreSQL's `-|-`:

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

Good to know:

- **The empty and infinite ranges are adjacent to nothing.**
- **Two ranges open at the same end are never adjacent.** They always overlap.
- **In the database, a date at the extreme is adjacent to nothing.** `DateOnly.MinValue` and
  `MaxValue` reach PostgreSQL as `-infinity` and `infinity`, so a query answers `false` where
  memory answers `true` for `[0001-01-01,0001-01-01]` and `[0001-01-02,)`
  ([EF Core](efcore.md#store-timestamp-ranges)).
- **Changed in 6.2.1.** Before 6.2.1, `IsAdjacentTo` returned `false` whenever the range you
  called it on was unbounded. So the relation was not symmetric, and it disagreed with PostgreSQL.
  `RangeSet` merges neighbours after sorting them by lower bound, which always puts an
  unbounded-start element in the position of the range `IsAdjacentTo` is called on. As a result,
  `RangeSet.From([(,0], [1,)])` returned `{(,0],[1,)}` instead of `{(,)}`. See the
  [changelog](../CHANGELOG.md).

### Measure a range

`Length` tells you how much a range covers, and the rule depends on the element type. A discrete
range counts its values, including both bounds. A continuous range measures the span between its
bounds.

```csharp
DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)).Length;  // 31 (days)
Int32Range.CreateFinite(1, 10).Length;                                              // 10 (integers)
DecimalRange.CreateFinite(1m, 5m).Length;                                           // 4  (span)

DateTimeRange.CreateFinite(nineAm, fiveThirtyPm).Length;  // TimeSpan of 8.5 hours
```

An empty range and an unbounded one give different results, so you can always tell them apart. The
empty range contains nothing, and an unbounded one contains too much to measure:

```csharp
Int32Range.Empty.Length;     // 0
Int32Range.Infinite.Length;  // null
```

The type of `Length` also depends on the element type:

| Range type | `Length` type |
|---|---|
| The integer ranges | `long?` |
| `DateRange` | `int?`, in days |
| The timestamp ranges | `TimeSpan?` |
| `DecimalRange` | `decimal?` |
| `Float64Range` | `double?`, which can be `+∞` |
| `Float32Range` | `float?`, which can be `+∞` |
| The NodaTime ranges | `Duration?` or `Period?` |

In the NodaTime package, an instant range measures exact elapsed time (`Duration?`). A wall-clock
range measures a calendar quantity (`Period?`).

A `Float64Range` measures the difference `End - Start` as a `double`. For
`[double.MinValue, double.MaxValue]` that difference is larger than any `double`, so `Length` returns
`double.PositiveInfinity`. The range itself is still finite: the infinity is only the measurement. `Float32Range` works the same
way with `float`.

Good to know:

- **`Length` runs in memory only.** It does not translate to SQL.

### List the values of a discrete range

A discrete range can list the values it contains with `Values()`. The continuous types don't
declare `Values()` at all, so the compiler catches the mistake, not a failure at runtime:

```csharp
foreach (var day in DateRange.CreateFinite(monday, friday).Values())
    Schedule(day);

Int32Range.CreateFinite(1, 5).Values();   // 1, 2, 3, 4, 5
DecimalRange.CreateFinite(1m, 5m).Values();  // does not compile — no step to walk
```

Good to know:

- **An unbounded range throws at the call.** `Values()` throws `NotSupportedException` when you
  call it, not at the first iteration. So the error points at the line that was wrong.
- **`Values(step)` skips ahead.** `Int32Range.CreateFinite(1, 10).Values(3)` gives 1, 4, 7, 10. The
  step is in the element type's unit: days for `DateRange`, months for `YearMonthRange`, a `Period`
  for `LocalDateRange`.

### Split a range into pieces

`Split` cuts a range into consecutive pieces of one step each, from its start. The last piece is
clipped to the range's end:

```csharp
Int32Range.CreateFinite(1, 10).Split(4);   // [1,4] [5,8] [9,10]

var hours = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 30));
hours.Split(TimeSpan.FromMinutes(30));     // 17 half-hour slots, 09:00–09:30 through 17:00–17:30
```

Pass a calendar factory instead of a step to cut at calendar boundaries. Each piece is one period,
and the first and last are clipped to the range:

```csharp
var contract = DateRange.CreateFinite(new DateOnly(2026, 1, 15), new DateOnly(2026, 4, 14));
contract.Split(DateRange.Month);
// [2026-01-15,2026-01-31] [2026-02-01,2026-02-28] [2026-03-01,2026-03-31] [2026-04-01,2026-04-14]

DateRange.Quarter(2026, 1).Split(DateRange.IsoWeek);   // 14 pieces, Monday to Sunday
```

The pieces are adjacent and hold exactly the values of the range. So
`RangeSet<DateRange, DateOnly>.From(contract.Split(DateRange.Month))` is `{contract}` again.

Good to know:

- **Each range type takes its own step.** An `int` for `Int32Range`, a `double` for `Float64Range`, a `float` for `Float32Range`, a number of days for
  `DateRange`, a `TimeSpan` for the timestamp ranges and `TimeRange`, a `Period` for
  `LocalDateRange` and `LocalDateTimeRange`, a `Duration` for `InstantRange`, and a number of months
  for `YearMonthRange`.
- **On a continuous type, the pieces are half-open.** Each one ends where the next begins. The last
  piece keeps the range's own end, so `[0,10]` split by 5 is `[0,5) [5,10]`.
- **Any period function works.** `Split` calls it with the first day of each piece, and the piece
  runs to the end of the period it returns. A fiscal year or a billing cycle of your own works the
  same way. The function must return a bounded range that holds the day it was given, or `Split`
  throws `ArgumentException`.
- **Weeks follow the same ISO 8601 rule as `IsoWeek`.** They never depend on the current culture.
- **A period of months counts from the start.** `LocalDateRange.Split(Period.FromMonths(1))` from
  January 31 starts its pieces on February 28, March 31 and April 30.
- **Errors come at the call.** An unbounded range throws `NotSupportedException`. A step of zero or
  less throws `ArgumentOutOfRangeException`. So does a step too small to move the start, such as
  `0.5m` near `decimal.MaxValue`.
- **A `Float64Range` or `Float32Range` step is rounded at each boundary, once.** The boundaries are `Start + n × step`,
  so the error doesn't build up from piece to piece. Near the top of the domain, where `n × step`
  alone is too large for the type, a boundary is the one before it plus the step. A step too small to move the start at its
  magnitude, such as `1.0` near `1e17`, throws `ArgumentOutOfRangeException`.
- **`Split` and `Values(step)` run in memory only.** PostgreSQL's `generate_series` returns rows, not
  a value ([What runs where](efcore.md#what-runs-where)).

### Clamp a value into a range

```csharp
var year = DateRange.CreateFinite(jan1, dec31);

year.Clamp(new DateOnly(2020, 5, 5));  // jan1  — pulled up to the lower bound
year.Clamp(new DateOnly(2024, 6, 15)); // unchanged — already inside
DateRange.Empty.Clamp(anyDate);        // null — nothing to snap to
```

`Clamp` returns the value itself when the range contains it. Otherwise it returns the bound the
value falls outside of.

Good to know:

- **The empty range returns `null`.** There is no value to snap to.
- **An unbounded side never constrains.** Clamping into `(-∞, 10]` only ever pulls a value down.

### Compare positions

```csharp
Int32Range.CreateFinite(1, 3).IsStrictlyLeftOf(Int32Range.CreateFinite(5, 9));  // true
Int32Range.CreateFinite(1, 5).IsStrictlyLeftOf(Int32Range.CreateFinite(5, 9));  // false — they share 5

Int32Range.CreateFinite(7, 9).IsStrictlyRightOf(Int32Range.CreateFinite(1, 5)); // true
```

Two ranges that share a value are not strictly left or right of each other.

Two more methods match PostgreSQL's `&<` and `&>`:

```csharp
// Does not extend to the right of other  (&<)
Int32Range.CreateFinite(1, 5).DoesNotExtendRightOf(Int32Range.CreateFinite(1, 10));  // true

// Does not extend to the left of other  (&>)
Int32Range.CreateFinite(3, 10).DoesNotExtendLeftOf(Int32Range.CreateFinite(1, 10));  // true
```

### Sort ranges

Every range type implements `IComparable<TRange>` and `IComparable`, so `Order()`, `List.Sort()` and
`SortedSet` work with no comparer, also on a `List<IRange<T>>` or an `ArrayList`. The order is
PostgreSQL's, the one `ORDER BY` and `<` use on a range column:

```csharp
var ranges = new[]
{
    DecimalRange.CreateFinite(1m, 5m, false, false),  // (1,5)
    DecimalRange.CreateFinite(1m, 5m, true, true),    // [1,5]
    DecimalRange.CreateUnboundedStart(3m),            // (,3)
    DecimalRange.CreateFinite(1m, 5m),                // [1,5)
    DecimalRange.Empty
};

ranges.Order();  // empty, (,3), [1,5), [1,5], (1,5)
```

The empty range sorts first. Every other range sorts by its lower bound, then by its upper bound. A
missing bound is the smallest lower bound and the largest upper bound. At the same value, an
inclusive lower bound sorts first and an inclusive upper bound sorts last.

`CompareTo` returns 0 exactly when the two ranges are equal, so a `SortedSet` keeps every distinct
range. `null` sorts before every range. The non-generic `CompareTo(object)` throws `ArgumentException` for an
argument that is not the same range type.

Good to know:

- **In a query, `CompareTo` against 0 runs as the comparison operator.** `b.Period.CompareTo(other) < 0`
  runs as `b."Period" < @other`, and `OrderBy(b => b.Period)` as `ORDER BY b."Period"`. With a
  `null` range on the right, the query returns the rows memory returns: `> 0`, `>= 0` and `!= 0` hold
  for every row, and `< 0`, `<= 0` and `== 0` for none. The same holds when you compare `CompareTo`
  with `-1` or `1`, which EF Core also turns into operators. `CompareTo` answers only `-1`, `0` or `1`,
  so `== 1` means "sorts after" in memory as in SQL. A projected `CompareTo`, such as
  `Select(b => b.Period.CompareTo(other))`, returns the same number in the query as in memory, also for
  a `null` operand.
- **A NULL column compares as `Comparer<T>.Default` orders a null.** It is equal to a `null` range,
  and before every other range. So with a `null` operand, a NULL column matches `== 0`, `>= 0` and
  `<= 0`, and with a non-null operand it compares lower, so it matches `< 0`, `<= 0` and `!= 0`. In
  memory, calling `CompareTo` on a null throws.
- **PostgreSQL puts `NULL` last in an ascending `ORDER BY`.** In memory, `null` sorts first. On a
  nullable column, the two orders differ only in where the nulls go.
- **This is not the set's lower-bound order.** `RangeSet<TRange, T>.LowerBoundComparer` compares only
  the lower bound, so it ties `[1,5]` with `[1,9]` ([Sort ranges the way a set does](#sort-ranges-the-way-a-set-does)).

### Read the bounds

These methods are PostgreSQL's `lower`, `upper`, `lower_inc` and `upper_inc`, and they work on any
shape. The shapes only have `Start` and `End` properties where that bound exists. The bound methods
give you the same information on every shape, as `T?`, with `null` for a missing bound. That is
exactly how PostgreSQL returns `NULL`.

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

On a `RangeSet`, `LowerBound()` is the first element's lower bound, and `UpperBound()` the last
element's upper bound.

## Move or widen a range

`Shift` moves a range, and `Expand` grows it on both sides. Both keep the shape and the
inclusiveness of each bound, unless the move leaves nothing between the bounds:

```csharp
var week = DateRange.CreateFinite(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7));
week.Shift(7);    // [2026-06-08,2026-06-14] — the same week, one week later
week.Shift(-7);   // [2026-05-25,2026-05-31]

var meeting = DateTimeRange.CreateFinite(nineAm, tenAm);           // [09:00, 10:00)
meeting.Expand(TimeSpan.FromMinutes(15));                         // [08:45, 10:15) — a buffer on each side

Int32Range.CreateFinite(1, 10).Expand(-2);                        // [3, 8] — a negative amount shrinks
```

Each range type takes the step its element type uses:

| Range type | `Shift` and `Expand` take |
|---|---|
| `Int32Range`, `Int64Range`, `DecimalRange` | an `int`, a `long`, a `decimal` |
| `Float64Range`, `Float32Range` | a finite `double`, a finite `float` |
| `DateRange` | a number of days |
| `DateTimeRange`, `DateTimeOffsetRange`, `TimeRange` | a `TimeSpan` |
| `LocalDateRange`, `LocalDateTimeRange` | a NodaTime `Period` |
| `InstantRange` | a NodaTime `Duration` |
| `YearMonthRange` | a number of months |

Good to know:

- **An empty or infinite range comes back as it is.** It has no bound to move. An unbounded side
  stays unbounded.
- **Shrinking past the width gives you `Empty`.** `Expand` with a negative amount moves the bounds
  inward, and bounds that cross make the empty range, as `CreateFinite` does.
- **A bound moved past the last value throws.** `Shift` and `Expand` throw `OverflowException` when
  a bound would leave the element type, such as past `int.MaxValue` or `DateOnly.MaxValue`. They
  never wrap around.
- **A bound that stands for infinity stays where it is.** On the date and time ranges,
  `DateOnly.MaxValue`, `DateTime.MaxValue` and the other extremes stand for PostgreSQL's
  `infinity` and `-infinity`. `Shift` and `Expand` leave such a bound alone, as `infinity - 1`
  is still `infinity` in PostgreSQL. A closed date range counts the day before the maximum too,
  because a column holding `[2026-01-01,infinity)` loads as `[2026-01-01,9999-12-30]`. So that
  range shifted back a day is `[2025-12-31,9999-12-30]`, and it still ends at infinity. A closed
  date range's end at the minimum is a date, though, and moves. The integer and float ranges have
  no infinity, so their extremes still throw.
- **A finite bound can't move onto infinity.** `Shift` and `Expand` throw `OverflowException` when
  they would move a finite bound onto a value that stands for infinity, as they do for a move past
  it. Saved, the bound would come back as infinity, and shifting back wouldn't restore the range.
  So `DateRange.CreateFinite(new DateOnly(9999, 12, 1), new DateOnly(9999, 12, 29)).Shift(1)`
  throws, because its end would land on 9999-12-30.
- **A `Float64Range` or `Float32Range` moves by IEEE addition.** Each moved bound is rounded to the nearest `double`,
  so `[0.1,1).Shift(0.2)` starts at `0.30000000000000004`. A bound pushed past `double.MaxValue`
  throws `OverflowException`, as for the other types. A `NaN` or infinite step throws
  `ArgumentException`.
- **A `TimeRange` can't cross midnight.** Moving a bound past the end of the day throws
  `OverflowException`, where `TimeOnly.Add` would wrap to the next morning.
- **A `DateTimeOffsetRange` keeps its offsets.** A day is exactly 24 hours, whatever a time zone
  does that night.
- **A period of months follows the calendar.** `LocalDateRange` and `LocalDateTimeRange` move by
  NodaTime's calendar arithmetic, which clips to the end of a shorter month. A `LocalDateRange`
  moves the day after its last day, as PostgreSQL stores a `daterange`, so a whole month moves to a
  whole month: `LocalDateRange.Month(2026, 2).Shift(Period.FromMonths(1))` is
  `LocalDateRange.Month(2026, 3)`, and moving it back gives February again. A start on the 31st
  still clips: January 31 plus one month is February 28.
- **A month or year shift of a short range can give you `Empty`.** The start and the day after the
  end clip separately, so they can meet. `[2024-02-28,2024-02-28]` moved by one year starts on
  2025-02-28 and would end the day before, so it is `Empty`. PostgreSQL gives the same answer for
  that `daterange`, and so does `LocalDateTimeRange` for `[2024-02-28,2024-02-29)`. It happens only
  for a range ending on the 28th to the 31st, a few days long at most. A whole month never empties.
- **They run in memory only.** PostgreSQL has no range arithmetic. See
  [What runs where](efcore.md#what-runs-where).

## Combine two ranges

The set operations are extension methods on the range types. They are written against
`IRangeFactory<TRange, T>`, which every range type in the library implements.

### Intersect two ranges

`Intersect` returns the largest range that is contained by both ranges.

```csharp
var a = Int32Range.CreateFinite(1, 10);
var b = Int32Range.CreateFinite(5, 15);

Int32Range intersection = a.Intersect(b);       // [5, 10]
a.Intersect(Int32Range.CreateFinite(11, 20));   // Empty — no overlap
```

The intersection of two ranges is always a single range, so `Intersect` returns the range type
itself. An `Empty` result really means that the two ranges have no value in common.

Every combination of shapes works, such as `Finite ∩ UnboundedStart` or
`UnboundedEnd ∩ UnboundedStart`. Each one returns a result of the right shape.

### Union of two ranges

`Union` returns a `RangeSet<TRange, T>` that holds every value of both ranges.

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

- **Ranges that overlap or are adjacent** give a set with one merged range.
- **Ranges with a gap between them** give a set with both. The union of two separate ranges really
  is two ranges, and the result type says so.

Good to know:

- **Unbounded ranges merge into unbounded ones.** An `UnboundedEnd` merged with an overlapping
  `Finite` gives an `UnboundedEnd`. An `UnboundedStart` that overlaps an `UnboundedEnd` covers the
  entire domain, and gives `{ Infinity }`.

### Subtract one range from another

`Except` removes from the range you call it on every value it shares with `other`. You get a
`RangeSet<TRange, T>`, and its number of elements tells you what happened.

```csharp
var range  = Int32Range.CreateFinite(1, 10);
var remove = Int32Range.CreateFinite(4, 6);

// [4, 6] is interior to [1, 10] — the result is split in two
var result = range.Except(remove);
// result[0] = [1, 4) ≡ [1, 3]
// result[1] = (6, 10] ≡ [7, 10]
```

| Result       | Meaning                                                                         |
|--------------|---------------------------------------------------------------------------------|
| `0` elements | The range you call it on is fully contained by `other`; nothing remains         |
| `1` element  | One-sided trim or no overlap; the remaining range                               |
| `2` elements | `other` was strictly inside the range you call it on; that range is split in two |

At each cut, the bound flips between inclusive and exclusive. So no value is lost, and no value is
counted twice across the pieces.

### Cover two ranges and the gap between them

`Merge` returns the smallest single range that contains both ranges. It is PostgreSQL's
`range_merge`, also known as the convex hull. Unlike `Union`, the result also covers any gap
between two separate ranges.

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

`Merge` ignores an empty range, and an unbounded edge stays unbounded in the result. On a
`RangeSet`, `Merge()` covers the whole set.

### Find the gap between two ranges

```csharp
var a = Int32Range.CreateFinite(1, 3);
var b = Int32Range.CreateFinite(10, 12);

a.Gap(b);  // [4, 9]
b.Gap(a);  // [4, 9] — the order doesn't matter

a.Gap(Int32Range.CreateFinite(4, 6));  // Empty — adjacent, nothing lies between them
```

`Gap` returns the range that lies between two ranges. It holds every value that `Merge` covers and
neither range holds. So the two ranges and their gap together make up exactly `a.Merge(b)`.

Good to know:

- **No gap gives you `Empty`.** `Gap` returns `Empty` when the ranges overlap or are adjacent, and
  when either of them is empty.
- **Each bound of the gap flips at the edge it meets.** On a continuous type, `[1,3)` and `(3,5]`
  leave the single value 3, so their gap is `[3,3]`.
- **An unbounded range still has a gap on its bounded side.** `(-∞, 3]` and `[10, +∞)` leave
  `[4, 9]`.
- **`Gap` runs in the database.** In an EF Core query it becomes `range_merge` over the multirange
  difference, because PostgreSQL has no gap function. See
  [What runs where](efcore.md#what-runs-where).

### Aggregate a sequence of ranges

`RangeAgg()` and `RangeIntersectAgg()` combine a sequence of ranges. They are the in-memory
versions of PostgreSQL's `range_agg` and `range_intersect_agg`:

```csharp
new[] { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(3, 8), Int32Range.CreateFinite(20, 25) }
    .RangeAgg();           // { [1, 8], [20, 25] } — a normalized RangeSet

new[] { Int32Range.CreateFinite(1, 10), Int32Range.CreateFinite(5, 15) }
    .RangeIntersectAgg();  // [5, 10] — the common intersection; null for an empty source
```

- **`RangeAgg()`** returns a normalized `RangeSet` with every value of every range.
- **`RangeIntersectAgg()`** returns the intersection of all the ranges, or `null` when the
  sequence is empty.
- **A `null` range is skipped**, as PostgreSQL skips a `NULL` row. So a group over a nullable
  column aggregates the ranges it has, and a sequence of nothing but `null` counts as empty.

Both also take a sequence of sets, as PostgreSQL's aggregates also take multiranges:

```csharp
IntSet[] blocked =
[
    IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(20, 25)]),
    IntSet.From([Int32Range.CreateFinite(3, 22)])
];

blocked.RangeAgg();           // { [1, 25] } — every value of every set
blocked.RangeIntersectAgg();  // { [3, 5], [20, 22] } — the values every set holds
```

Over sets, `RangeIntersectAgg()` returns a `RangeSet`, again `null` for an empty sequence. An empty
set among the inputs makes it the empty set. A `null` set is skipped. These two are generic, so they
work for every range type, the NodaTime ones included.

In an EF Core query, all four run as the SQL aggregates inside a `GroupBy` projection. See
[Entity Framework Core](efcore.md).

## RangeSet — multirange support

`RangeSet<TRange, T>` holds several ranges as one value. It is the in-memory version of a
PostgreSQL multirange (`int4multirange`, `nummultirange`, …). A set is immutable, and it is
always normalized.

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

// Set-operand comparisons — with a range on the left too, see below
set.Contains(IntSet.From([Int32Range.CreateFinite(2, 8)]));   // true   (@>)
set.Overlaps(IntSet.From([Int32Range.CreateFinite(25, 40)])); // true   (&&)
set.IsStrictlyLeftOf(Int32Range.CreateFinite(40, 50));        // true   (<<)
set.DoesNotExtendRightOf(Int32Range.CreateFinite(1, 30));     // true   (&<)
set.IsAdjacentTo(Int32Range.CreateFinite(31, 40));            // true   (-|-)
```

Every way you create a set keeps three rules. The elements are sorted by lower bound, no two of
them overlap, and no two are adjacent. To keep those rules, creating a set:

- **Drops empty ranges.**
- **Merges ranges that overlap or are adjacent.**
- **Collapses the set to `RangeSet<TRange, T>.Infinite` when any input is `Infinity`.**

What the example shows:

- **Union, intersection and difference** each take a single range or a whole set. They also have
  the operators `|`, `&` and `-`.
- **`Complement()`** returns every value the set does not cover.
- **The state checks** match PostgreSQL's `isempty`, `lower_inf` and `upper_inf`. `IsFinite()`
  and `IsInfinity()` are the two derived shapes.
- **The comparisons** also take a whole set. With the range on either side, you get every range and
  multirange operator PostgreSQL has ([Compare a range with a set](#compare-a-range-with-a-set)).

### Adjacency counts only at the outer edges

A set's adjacency matches PostgreSQL exactly. It is directional, through the set's outer edges.
The range you pass must end exactly where the set's first element begins, or begin exactly where
the set's last element ends. Touching any boundary inside the set doesn't count, not even the inner
side of the first or last element
([verified against live PostgreSQL](../README.md#verified-against-postgresql)):

```csharp
var three = IntSet.From([
    Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(7, 9), Int32Range.CreateFinite(20, 22)
]);
three.IsAdjacentTo(Int32Range.CreateFinite(23, 25)); // true  — attaches after the last element
three.IsAdjacentTo(Int32Range.CreateFinite(4, 6));   // false — inner side of the first element
three.IsAdjacentTo(Int32Range.CreateFinite(10, 12)); // false — touches only the interior [7, 9]
```

The positional operators (`<<`, `>>`, `&<`, `&>`) work the same way. They compare the bounds of the
first and last element.

### A set open at both ends can still have a gap

`IsInfinity()` is not the same as `IsUnboundedStart()` and `IsUnboundedEnd()` both being true. For
a single range it would be. A range has no gaps, so a range unbounded on both sides covers the whole
domain. A set can be open at both ends and still have a hole:

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

Creating a set collapses any `Infinity` input to the one-element `Infinite` set. So that set is the
only way to represent full coverage. That is what makes the check exact, both in memory and in SQL.

### Compare a range with a set

You can call the comparisons on a range and pass a set. Each one is the PostgreSQL operator with the
range on the left:

```csharp
var blocked = IntSet.From([Int32Range.CreateFinite(3, 4), Int32Range.CreateFinite(8, 9)]);

Int32Range.CreateFinite(1, 10).Contains(blocked);             // true   (@>)
Int32Range.CreateFinite(8, 9).IsContainedBy(blocked);         // true   (<@)
Int32Range.CreateFinite(5, 7).Overlaps(blocked);              // false  (&&) — 5 to 7 is a gap
Int32Range.CreateFinite(1, 2).IsStrictlyLeftOf(blocked);      // true   (<<)
Int32Range.CreateFinite(5, 7).DoesNotExtendRightOf(blocked);  // true   (&<) — 7 ≤ 9
Int32Range.CreateFinite(5, 7).DoesNotExtendLeftOf(blocked);   // true   (&>) — 5 ≥ 3
Int32Range.CreateFinite(10, 12).IsAdjacentTo(blocked);        // true   (-|-)

blocked.IsContainedBy(Int32Range.CreateFinite(3, 9));         // true   (<@)
blocked.IsContainedBy(blocked | Int32Range.CreateFinite(20, 30)); // true   (<@)
```

A range compares with a set through the set's outer elements. `IsStrictlyLeftOf` and
`DoesNotExtendLeftOf` read the first element, and `IsStrictlyRightOf` and `DoesNotExtendRightOf`
read the last. `Contains` is `true` when the range holds the first and the last element, and so
everything between them.

Good to know:

- **`&<` and `&>` are not the set's methods turned around.** `range.DoesNotExtendRightOf(set)`
  compares upper bounds, while `set.DoesNotExtendLeftOf(range)` compares lower bounds. So
  `[4,5) &< {[3,9)}` is `true` and `{[3,9)} &> [4,5)` is `false`.
- **An empty set is contained by every range**, the empty range included. With an empty set on
  either side, `IsStrictlyLeftOf`, `IsStrictlyRightOf`, `DoesNotExtendLeftOf`,
  `DoesNotExtendRightOf`, `Overlaps` and `IsAdjacentTo` return `false`.

### Find the free slots in a set

```csharp
var booked = IntSet.From([
    Int32Range.CreateFinite(1, 10), Int32Range.CreateFinite(20, 30), Int32Range.CreateFinite(35, 40)
]);

booked.Gaps();                                // { [11, 19], [31, 34] }
booked.Complement();                          // { (-∞, 0], [11, 19], [31, 34], [41, +∞) }

booked.Gaps(Int32Range.CreateFinite(5, 50));  // { [11, 19], [31, 34], [41, 50] }
```

`Gaps()` returns every value between the set's first and last element that the set doesn't cover.
Unlike `Complement()`, it leaves out what lies before the first element and after the last.

`Gaps(window)` returns the parts of `window` that the set doesn't cover, such as the free days of a
month. It gives the same values as `Complement().Intersect(window)`.

Good to know:

- **A set with fewer than two elements has no gaps.** `Gaps()` returns the `Empty` set for it.
- **An unbounded window keeps its open side.** `booked.Gaps(Int32Range.CreateUnboundedEnd(5))`
  ends with `[41, +∞)`.
- **Both run in the database.** `Gaps()` becomes `datemultirange(range_merge(x)) - x`, and
  `Gaps(window)` becomes `datemultirange(window) - x`.

### Build a set with a collection expression

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

A collection expression normalizes the ranges exactly as `From` does. An empty one gives you the
`Empty` singleton, and one that contains `Infinite` gives you the `Infinite` singleton.

Good to know:

- **The builder is `RangeSet.Create<TRange, T>`.** It lives on the non-generic `RangeSet`, because a
  `[CollectionBuilder]` target can't itself be generic.
- **Prefer the collection expression to calling `Create` yourself.** C# doesn't infer type
  arguments from constraints. It can't deduce `T` from `TRange`, so a direct call has to name both.
- **`From` has two overloads.** There is `From(params ReadOnlySpan<TRange>)` beside
  `From(IEnumerable<TRange>)`.

### Read and compare sets

A set implements `IReadOnlyList<TRange>`. You can enumerate it in lower-bound order, and use
`Count` and the indexer.

Sets also have structural equality, including `==` and `!=`. Two sets built from different ranges
are equal when they normalize to the same ranges:

```csharp
var a = IntSet.From([Int32Range.CreateFinite(1, 10)]);
var b = IntSet.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(6, 10)]);
a.Equals(b);  // true — both normalize to { [1, 10] }
a == b;       // true — same value semantics as the ranges themselves
```

Sets implement `IComparable<RangeSet<TRange, T>>` and `IComparable` in PostgreSQL's multirange order. Two sets compare
element by element in the [range order](#sort-ranges), and a set that runs out of elements first
sorts first. So the empty set sorts before every other set, and `{[1,3]}` before `{[1,3],[5,7]}`.
As for ranges, `CompareTo` returns 0 exactly when the two sets are equal.

### Sort ranges the way a set does

`RangeSet<TRange, T>.LowerBoundComparer` sorts ranges by lower bound, in the same order the set uses
inside. It is an `IComparer<TRange>` singleton. Use it to sort any `List<TRange>`, for example to
sort your ranges before you pass them to `From`.

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

The order follows two rules:

- **An `IUnboundedStartRange<T>` sorts first.** Its lower bound is -∞.
- **At the same finite value, an inclusive lower bound sorts first.** `[5, …` comes before
  `(5, …`.

The same instance is also available as `RangeLowerBoundComparer<Int32Range, int>.Instance`. Use it
where you only have the comparer type, not the set type.

## Write generic code over any range

The library has a set of interfaces for writing generic code:

| Interface                  | Purpose                                                            |
|----------------------------|--------------------------------------------------------------------|
| `IRange<T>`                | Base marker for all range types                                    |
| `IFiniteRange<T>`          | `Start`, `End`, and their inclusiveness flags                      |
| `IUnboundedStartRange<T>`  | `End` and `EndInclusive`                                           |
| `IUnboundedEndRange<T>`    | `Start` and `StartInclusive`                                       |
| `IEmptyRange<T>`           | Marker for the empty range; no bound properties                    |
| `IInfinityRange<T>`        | Marker for the range covering the entire domain                    |
| `IRangeFactory<TRange, T>` | Abstract static factories; also `NextValueAfter`/`PreviousValueBefore` for step-aware (discrete) types |

In all of these interfaces, `T` is constrained to `struct, IComparable<T>, IEquatable<T>`.

`IRangeFactory<TRange, T>` and `IValueSetFactory<TSet, T>` both extend `ISpanParsable<TSelf>`,
and so `IParsable<TSelf>`, and `IFormattable`. That lets generic code parse and format any range
or set without knowing its concrete type:

```csharp
static T Load<T>(ReadOnlySpan<char> literal) where T : ISpanParsable<T>
    => T.Parse(literal, CultureInfo.InvariantCulture);
```

`IRangeFactory<TRange, T>` also extends `IComparable<TRange>`, so generic code can sort any range
type in the [range order](#sort-ranges).

For the lower-bound order the set uses inside, use `RangeLowerBoundComparer<TRange, T>`, an
`IComparer<TRange>` singleton. See
[Sort ranges the way a set does](#sort-ranges-the-way-a-set-does).

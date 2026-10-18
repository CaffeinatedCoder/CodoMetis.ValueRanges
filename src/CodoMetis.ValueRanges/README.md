# CodoMetis.ValueRanges

Range types and value sets for .NET 10 that work entirely in memory. You get PostgreSQL's complete
range and set algebra, with no database dependency.

You get two families of types. Both are immutable, and both are in canonical form from the moment
you create them:

- **Range types.** The six element types PostgreSQL ships built-in range types for, plus
  `TimeRange`. Each range type is a discriminated union of five sealed variants.
  `RangeSet<TRange, T>` is the matching multirange, and it is always kept normalized.
- **Value sets (v6).** Sets of scalar values, deduplicated and sorted, that PostgreSQL stores as a
  native array.

Everything runs in your process. The EF Core package,
[CodoMetis.ValueRanges.EFCore.PostgreSQL](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL),
maps both families to PostgreSQL columns and runs the same calls as SQL. So the same code works in
memory and in a query.

```sh
dotnet add package CodoMetis.ValueRanges
```

> Requires .NET 10 or later.

**Native AOT and trimming compatible.** Your publish gets no trim or AOT warnings, and a NativeAOT
consumer in CI verifies this. For JSON under Native AOT you need a source-generated context
([details](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).

## Why not `NpgsqlRange<T>`?

Because you can't use it in a domain model. `NpgsqlRange<T>` is declared in `NpgsqlTypes`, in
`Npgsql.dll`, so a model that uses it references the database driver. The struct also has no
interval algebra of its own. `Contains`, `Overlaps`, `Union` and the rest are EF Core extension
methods, each documented as *"only intended for use via SQL translation as part of an EF Core LINQ
query"*. If you call one from a unit test or a domain service, it throws. So the algebra exists only
inside a query, on a type that exists only inside the driver.

These types work the other way round. The algebra runs in your process, with no database
dependency. The [EF Core package](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL)
runs the same calls as the same PostgreSQL operators, and the results are verified against a live
server.

The other options each cover only part of this:

- **NodaTime's `Interval` and `DateInterval`** are real domain types, and the Npgsql NodaTime plugin
  maps them. But they cover only two date/time shapes, and a `DateInterval` is always closed and
  bounded. It can never be half-open, unbounded or empty.
- **In-memory range libraries such as [FRange](https://www.nuget.org/packages/FRange/)** have an
  algebra but no persistence. They have no discrete element types either: `[1,10)` and `[1,9]` stay
  different values, so they can't decide adjacency for integers and dates. And unboundedness stays
  a runtime fact. Asking an unbounded range for its bound value throws. Here that property doesn't
  exist, so there is nothing to ask.
- **A PostgreSQL array maps to `T[]` or `List<T>`.** Those are *mutable* references. Your code can
  change an element after loading it, so the domain can't protect a rule once it has handed the
  value out. And a list is not a set: order and duplicates are part of its value. The value sets
  here are immutable end to end. Every way of creating one puts it in canonical form, and no member
  can change it afterwards.

The full comparison is in
[Why this exists](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md).

## Range types

| .NET type             | PostgreSQL equivalent | Element type     | Discrete |
|-----------------------|-----------------------|------------------|----------|
| `Int32Range`          | `int4range`           | `int`            | ✓        |
| `Int64Range`          | `int8range`           | `long`           | ✓        |
| `DecimalRange`        | `numrange`            | `decimal`        | —        |
| `DateRange`           | `daterange`           | `DateOnly`       | ✓        |
| `DateTimeRange`       | `tsrange`             | `DateTime`       | —        |
| `DateTimeOffsetRange` | `tstzrange`           | `DateTimeOffset` | —        |
| `TimeRange`           | `timerange` (custom)  | `TimeOnly`       | —        |
| `Float64Range`        | `float8range` (custom) | `double`, finite only | —   |
| `Float32Range`        | `float4range` (custom) | `float`, finite only  | —   |

The NodaTime versions are `LocalDateRange`, `LocalDateTimeRange`, `InstantRange` and
`YearMonthRange`, which works at month granularity. They are in the NodaTime package,
[CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime).

### Unboundedness is a shape, not a bound value

Each range is a discriminated union of five kinds of range: `Finite`, `UnboundedStart`,
`UnboundedEnd`, `EmptyRange` and `Infinity`. This shape is part of the static type:

- **`UnboundedEnd` has no `End` property**, so there is nowhere to put a sentinel.
- **`Finite` has no flag** that could disown its `End`.
- **Neither has a nullable bound.**

So invalid states can't be represented. You can't declare your own subtype either, which makes these
five the only ranges that can exist. A switch over them is therefore complete in fact. C# can't
prove that, though, so a switch expression still needs a discard arm that is never reached
([pattern matching](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md#pattern-matching)).

```csharp
DateTimeRange.CreateUnboundedEnd(start)               // genuinely open-ended
DateTimeRange.CreateFinite(start, DateTime.MaxValue)  // ends at a specific instant
```

These two are not interchangeable, and the compiler won't let you mix them up. A representation
built from bounds plus flags has to sort this out at runtime. The difference still matters at the
database boundary, where `DateTime.MaxValue` maps to PostgreSQL `infinity`. See
[Unboundedness is a shape, not a bound value](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md#unboundedness-is-a-shape-not-a-bound-value)
for the full reasoning.

### Operations

- **Containment, overlap and adjacency:** `Contains`, `Overlaps`, `IsContainedBy`, `IsAdjacentTo`.
- **Directional comparisons:** `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf`.
- **Set operations:** `Intersect`, `Union`, `Except`, `Complement`, `Merge`, `Gap`, `Gaps`.
- **Arithmetic:** `Shift` moves a range, `Expand` grows it on both sides.
- **Bound accessors:** `LowerBound`/`UpperBound`/`LowerBoundInclusive`/`UpperBoundInclusive`. They
  return `null` exactly where PostgreSQL returns `NULL`.
- **Aggregates:** `RangeAgg`/`RangeIntersectAgg`.
- **Calendar ranges:** `DateRange.Month`, `Quarter`, `Year` and `IsoWeek`. Weeks are ISO 8601, Monday to Sunday.
- **Splitting:** `Split` cuts a range into steps or calendar periods; `Values(step)` skips ahead on a discrete range.

Every one of them returns exactly what PostgreSQL returns, including how PostgreSQL canonicalizes
discrete ranges.
[Ranges and range sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)
shows each operation with examples.

### Why these element types

Interval algebra needs two things from the element type:

- **A total order** that the type's own comparisons agree with.
- **A defined step**, for adjacency.

`double` meets the first one only without `NaN`. .NET sorts `NaN` below every number, and PostgreSQL
sorts it above `Infinity`, so no comparison involving it could agree on both sides. The float
ranges and sets (`Float64Range`, `Float32Range`, `Float64Set`, `Float32Set`) therefore hold finite numbers only, and creating, parsing or loading `NaN` or an
infinity throws. Limiting the element types to a checked list is what keeps the algebra sound. See
[Why these element types](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md#why-these-element-types).

## Value sets (v6)

A value set is an immutable set of scalar values in canonical form: deduplicated, sorted, and free
of nulls. Two sets with the same values are equal. A set relates to a PostgreSQL array column the
way `RangeSet<DateRange, DateOnly>` relates to a `datemultirange`. The .NET type is the idea, and the
column is how it is stored.

| .NET type           | Element type     | PostgreSQL column | With your own element type    |
|---------------------|------------------|-------------------|-------------------------------|
| `StringSet`         | `string`         | `text[]`          | `StringSet<TElement>`         |
| `GuidSet`           | `Guid`           | `uuid[]`          | `GuidSet<TElement>`           |
| `Int16Set`          | `short`          | `smallint[]`      | `Int16Set<TElement>`          |
| `Int32Set`          | `int`            | `integer[]`       | `Int32Set<TElement>`          |
| `Int64Set`          | `long`           | `bigint[]`        | `Int64Set<TElement>`          |
| `DecimalSet`        | `decimal`        | `numeric[]`       | `DecimalSet<TElement>`        |
| `Float64Set`        | `double`, finite | `double precision[]` | `Float64Set<TElement>`     |
| `Float32Set`        | `float`, finite  | `real[]`          | `Float32Set<TElement>`        |
| `DateSet`           | `DateOnly`       | `date[]`          | `DateSet<TElement>`           |
| `TimeSet`           | `TimeOnly`       | `time[]`          | `TimeSet<TElement>`           |
| `DateTimeSet`       | `DateTime`       | `timestamp[]`     | `DateTimeSet<TElement>`       |
| `DateTimeOffsetSet` | `DateTimeOffset` | `timestamptz[]`   | `DateTimeOffsetSet<TElement>` |

```csharp
var tags = StringSet.From("beta", "alpha", "beta");   // {alpha,beta} — deduplicated, sorted
StringSet more = ["gamma", "alpha"];                  // collection expressions work

tags.Contains("alpha");      // true
tags.Overlaps(more);         // true — shares "alpha"
tags.IsProperSubsetOf(more); // false — proper containment excludes equality
tags.Union(more);            // {alpha,beta,gamma}
tags.Count;                  // 2
```

The full guide is
[Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md).

### Use your own element types

Every set family has a version for your own element types, such as typed keys generated by Vogen,
Metalama or StronglyTypedId, or written by hand. These versions (the wrapper arities) require only
standard .NET interfaces, so your types never reference this package. They are `StringSet<T>`,
`GuidSet<T>`, `Int16Set<T>`, `Int32Set<T>`, `Int64Set<T>`, `DecimalSet<T>`, `Float64Set<T>`, `Float32Set<T>`, `DateSet<T>`,
`TimeSet<T>`, `DateTimeSet<T>` and `DateTimeOffsetSet<T>`. The NodaTime package adds five more.

One rule is a convention, because no constraint can express it: *your element's text must be
exactly the text of the value it wraps*. A wrapper whose `ToString(format, provider)` passes both
arguments on to that value follows the rule. This matters most for the temporal versions. They ask
for a round-trip format because the default format drops fractions of a second. See
[Keep the text form of the underlying type](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md#keep-the-text-form-of-the-underlying-type).

### Why every set is sorted and deduplicated

Every way of creating a set sorts it and removes duplicates: `From`, parsing, JSON, and loading it
from the database. You get two things from that:

- **Change detection is cheap and exact.** EF Core's `ValueComparer` comes down to a cheap equality
  check, and it never reports a change that isn't one.
- **SQL `=` means set equality.** Two stored arrays are equal exactly when the sets are.

The sort order is chosen on purpose:

- **String-backed sets sort ordinal**, never culture-sensitive. The order is a storage rule that
  every writer shares, not a display order. With a culture sort, two machines could disagree about
  the same set.
- **Every other set sorts by the element's own comparison.**

Good to know:

- **`Intersect`, `Except` and `Add` run in memory only.** PostgreSQL's array type has no
  intersection, no difference and no sorted insert.

## Literals, parsing and JSON

Every type implements `IParsable<T>` and `IFormattable` with PostgreSQL literal syntax. Every type
also carries its own System.Text.Json converter, so it works in ASP.NET Core APIs without any
registration:

```csharp
Int32Range.Parse("[1,10)", null).ToString();   // [1,9] — discrete ranges canonicalize
StringSet.Parse("{alpha,beta}", null);         // {alpha,beta}

JsonSerializer.Serialize(Int32Range.CreateFinite(1, 10));   // "[1,10]"
```

[Literals, parsing, and JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)
covers the parsing rules and the JSON options.

## Documentation

The [project README](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/README.md)
is the starting point. The guides go into detail:

- **[Getting started](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/getting-started.md)**
  takes you from an empty project to a first query that runs in PostgreSQL.
- **[Ranges and range sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/ranges.md)**
  covers every operation with examples, and the multirange operators.
- **[Value sets](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/value-sets.md)**
  covers the set types and your own element types.
- **[Literals, parsing, and JSON](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md)**
  covers the parsing and JSON rules.
- **[Entity Framework Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)**
  covers the EF Core mapping and which calls run as SQL.
- **[Migration guide](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/migration.md)**
  lists the source changes each major version needs.

See also the
[changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges/CHANGELOG.md).

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

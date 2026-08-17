# CodoMetis.ValueRanges

In-memory range and set types for .NET 10 — the complete PostgreSQL interval and membership algebra
with no database dependency.

Two type families, both immutable and canonical at construction:

- **Range types** — the six value domains PostgreSQL ships as built-in range types, plus
  `TimeRange`, each modelled as a discriminated union of five sealed variants, with
  `RangeSet<TRange, T>` as their always-normalized multirange counterpart.
- **Value sets** (v6) — canonical sets of scalar values whose PostgreSQL storage shape is a native
  array.

Everything executes in process. The companion package
[CodoMetis.ValueRanges.EFCore.PostgreSQL](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL)
maps both families to PostgreSQL columns and translates the algebra to SQL, so the same code works
in memory and as a query.

```sh
dotnet add package CodoMetis.ValueRanges
```

> Requires .NET 10 or later.

## Why not `NpgsqlRange<T>`?

Because it cannot live in a domain model. `NpgsqlRange<T>` is declared in `NpgsqlTypes`, in
`Npgsql.dll`, so a model that uses it references the database driver — and the struct carries no
interval algebra of its own. `Contains`, `Overlaps`, `Union` and the rest are EF Core extension
methods, each documented as *"only intended for use via SQL translation as part of an EF Core LINQ
query"*; calling one from a unit test or a domain service throws. The algebra exists only inside a
query, on a type that only exists inside the driver.

These types are the other way round. The algebra runs in process with no database dependency, and
the [EF Core companion](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL)
translates the same calls to the same PostgreSQL operators, verified against a live server.

The rest of the ecosystem covers the pieces separately. NodaTime's `Interval`/`DateInterval` are
real domain types the Npgsql plugin maps, but only two date/time shapes, and `DateInterval` is
always closed and bounded — never half-open, unbounded or empty. In-memory range libraries such as
[FRange](https://www.nuget.org/packages/FRange/) carry an algebra but no persistence, no discrete
domain (`[1,10)` and `[1,9]` stay different values, so integer and date adjacency cannot be
decided), and keep unboundedness a runtime fact — asking an unbounded range for its bound value
throws, where here the property does not exist to be asked.

And a PostgreSQL array maps to `T[]` or `List<T>`: *mutable* references, so a caller can rewrite an
element after load and the domain cannot defend an invariant it has already handed out — and a list
rather than a set, with order and duplicates part of the value. These sets are immutable end to end,
canonical on every construction path, with no mutating member to undo it.

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

NodaTime equivalents — `LocalDateRange`, `LocalDateTimeRange`, `InstantRange` and the
month-granularity `YearMonthRange` — live in
[CodoMetis.ValueRanges.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.NodaTime).

### Unboundedness is a shape, not a bound value

Each range is a discriminated union of `Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange` and
`Infinity`. The shape lives in the static type: `UnboundedEnd` has no `End` property to put a
sentinel in, `Finite` has no flag to disown its `End`, and neither carries a nullable bound. Invalid
states are unrepresentable, and because no external subtype can be declared, these five are the only
ranges that can exist — so a switch over them is complete in fact, though C# cannot prove it and a
switch expression still needs a (unreachable) discard arm.

```csharp
DateTimeRange.CreateUnboundedEnd(start)               // genuinely open-ended
DateTimeRange.CreateFinite(start, DateTime.MaxValue)  // ends at a specific instant
```

The two are not interchangeable and the compiler will not let them be confused — a distinction a
bounds-plus-flags representation has to reconcile at runtime, and one that still matters at the
database boundary, where `DateTime.MaxValue` maps to PostgreSQL `infinity`.

### Operations

Containment, overlap and adjacency (`Contains`, `Overlaps`, `IsContainedBy`, `IsAdjacentTo`),
directional comparisons (`IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf`), set
operations (`Intersect`, `Union`, `Except`, `Complement`, `Merge`), bound accessors
(`LowerBound`/`UpperBound`/`LowerBoundInclusive`/`UpperBoundInclusive`, matching PostgreSQL's
`NULL` semantics) and the `RangeAgg`/`RangeIntersectAgg` aggregates — all matching PostgreSQL's
results exactly, including its canonicalization of discrete ranges.

### Why these element types

Interval algebra needs a total order the type's own comparisons agree with and, for adjacency, a
defined step. `double` and `float` have neither and fail *quietly*: `double.CompareTo` reports `NaN`
as less than every value and equal to itself, while the IEEE operators disagree, so a library
generic over `IComparable<T>` accepts `double` and answers containment against a `NaN` bound with a
straight face. Restricting the element types to a vetted set is what makes the algebra sound.

## Value sets (v6)

A value set is an immutable, canonical set of scalar values — deduplicated, sorted, never
containing null, with structural equality. It relates to a PostgreSQL array column exactly as
`RangeSet<DateRange, DateOnly>` relates to `datemultirange`: the CLR type models the domain concept,
the column is its storage encoding.

| .NET type           | Element type     | PostgreSQL column | Wrapper arity        |
|---------------------|------------------|-------------------|----------------------|
| `StringSet`         | `string`         | `text[]`          | `StringSet<TElement>` |
| `GuidSet`           | `Guid`           | `uuid[]`          | `GuidSet<TElement>`  |
| `Int16Set`          | `short`          | `smallint[]`      | —                    |
| `Int32Set`          | `int`            | `integer[]`       | `Int32Set<TElement>` |
| `Int64Set`          | `long`           | `bigint[]`        | `Int64Set<TElement>` |
| `DecimalSet`        | `decimal`        | `numeric[]`       | —                    |
| `DateSet`           | `DateOnly`       | `date[]`          | —                    |
| `TimeSet`           | `TimeOnly`       | `time[]`          | —                    |
| `DateTimeSet`       | `DateTime`       | `timestamp[]`     | —                    |
| `DateTimeOffsetSet` | `DateTimeOffset` | `timestamptz[]`   | —                    |

```csharp
var tags = StringSet.From("beta", "alpha", "beta");   // {alpha,beta} — deduplicated, sorted
StringSet more = ["gamma", "alpha"];                  // collection expressions work

tags.Contains("alpha");      // true
tags.Overlaps(more);         // true — shares "alpha"
tags.IsProperSubsetOf(more); // false — proper containment excludes equality
tags.Union(more);            // {alpha,beta,gamma}
tags.Count;                  // 2
```

The **wrapper arities** take generator-produced domain values — Vogen, Metalama, StronglyTypedId or
hand-written — and are constrained only on BCL interfaces, so your domain types never reference this
package. Every family has one: `StringSet<T>`, `GuidSet<T>`, `Int16Set<T>`, `Int32Set<T>`,
`Int64Set<T>`, `DecimalSet<T>`, `DateSet<T>`, `TimeSet<T>`, `DateTimeSet<T>` and
`DateTimeOffsetSet<T>`, plus five more in the NodaTime satellite.

One contract is convention rather than constraint: the element's text form must be exactly the
backing primitive's. A wrapper whose `ToString(format, provider)` forwards both arguments to the
value it wraps satisfies it — which matters most for the temporal arities, since those ask for a
round-trip format precisely because the default one drops sub-seconds.

### Canonical form is the contract

Every construction path deduplicates and sorts: `From`, parsing, JSON, and materialization from the
database. That is load-bearing twice — the EF `ValueComparer` collapses to a cheap equality with no
false diffs, and SQL `=` on the stored array coincides with set equality.

String-backed sets sort **ordinal**, never culture-sensitive: canonical form is a cross-writer
storage contract, not a display order, and a culture sort would make two machines disagree about the
same set. Everything else sorts by the element's own comparison.

`Intersect`, `Except` and `Add` are client-side only — PostgreSQL's array type has no intersection,
difference or sorted insert.

## Literals, parsing and JSON

Every type implements `IParsable<T>` and `IFormattable` using PostgreSQL literal syntax, and ships
System.Text.Json converters for direct use in ASP.NET Core APIs:

```csharp
Int32Range.Parse("[1,10)", null).ToString();   // [1,9] — discrete ranges canonicalize
StringSet.Parse("{alpha,beta}", null);         // {alpha,beta}

options.AddRangeConverters();                  // registers both families
```

## Documentation

The [full README](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/README.md)
covers every operation with examples, the multirange operator matrix, the parsing and JSON rules,
and the EF Core mapping. See also the
[changelog](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/src/CodoMetis.ValueRanges/CHANGELOG.md).

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

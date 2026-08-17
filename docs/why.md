# Why this exists

The design rationale, the landscape it was written against, and the reasoning behind what the
library deliberately does *not* accept. None of this is needed to use the package — the
[README](../README.md) installs it and [Getting started](getting-started.md) walks through a first
query. This page is for deciding whether the model is the right one, and why the boundaries sit
where they do.

## No single type holds all of it

Every piece of this problem already has a solution in .NET. The gap is that no single type holds
them at once.

| What you would reach for today | Where it stops |
|---|---|
| Two properties (`From`, `To`) | No algebra, no empty or unbounded case; "no end date" becomes a nullable that every reader reinterprets |
| [`NpgsqlRange<T>`](https://www.npgsql.org/doc/api/NpgsqlTypes.NpgsqlRange-1.html) | Declared in `NpgsqlTypes`, in `Npgsql.dll` — a domain model that uses it references the database driver. The struct carries no algebra of its own ([and reconciles unboundedness at runtime](#unboundedness-is-a-shape-not-a-bound-value)) |
| NodaTime `Interval` / `DateInterval` with the [Npgsql NodaTime plugin](https://www.npgsql.org/efcore/mapping/nodatime.html) | Two date/time shapes only, and `DateInterval` is always closed and always bounded — no half-open, no unbounded, no empty |
| [FRange](https://www.nuget.org/packages/FRange/), [Open.Range](https://www.nuget.org/packages/Open.Range) | In-memory only — no PostgreSQL mapping, no SQL translation, no range literals. Neither has a discrete domain, so `[1,10)` and `[1,9]` stay different values and integer or date adjacency cannot be decided |
| `T[]` / `List<T>` with EF Core primitive collections | Mutable references, so the domain cannot defend an invariant it has already returned; and a list, not a set — order and multiplicity are part of the value |

**On the range side, the SQL translation is not what is missing.** Npgsql already translates the
full operator set, and this package does not improve on that. What is missing is a domain type to
hang it on. `NpgsqlRange<T>`'s operations are EF Core extension methods, each documented as *"only
intended for use via SQL translation as part of an EF Core LINQ query"* — call one from a unit test
or a domain service and it throws. The algebra therefore exists only inside a query, on a type that
only exists inside the driver. A domain model that wants both has neither.

The in-memory libraries make the opposite trade, and both keep unboundedness a runtime fact.
[FRange](https://www.nuget.org/packages/FRange/) comes closest — it has unbounded bounds and
multiranges — but its C# surface answers `LowerBoundValue` on an unbounded range by throwing
(`failwith "No bound"`), paired with a `HasLowerBound` you are expected to remember to call first.
That is the same question [this library refuses to let you
ask](#unboundedness-is-a-shape-not-a-bound-value): `UnboundedStart` has no `Start` property to
throw from.

**On the set side there is no equivalent at all.** Persisting a PostgreSQL array today means
exposing `T[]` or `List<T>` — *mutable* references, so a caller can rewrite an element after load
and the domain cannot defend an invariant it has already handed out. Order and duplicates are part
of the value, and canonical form is left to every writer to remember. A set of tags, permissions or
IDs has no type that says so. These sets are immutable end to end: every construction path —
`From`, parsing, JSON, materialization from the database — deduplicates and sorts, and there is no
mutating member to undo it.

This package is the two halves joined: immutable domain types carrying the complete algebra in
process with no database dependency, which the EF Core companion also translates to the same
PostgreSQL operators — [checked against a live server](../README.md#verified-against-postgresql)
rather than asserted.

*Landscape surveyed August 2026. If something here is out of date or a comparable library was
missed, please [open an issue](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues) —
the claim is meant to be falsifiable.*

## Unboundedness is a shape, not a bound value

Encoding the shape in the type also keeps *"there is no upper bound"* apart from *"the upper bound is the largest representable value"* — two different facts that a bounds-plus-flags representation stores in the same object.

In a representation built from two nullable bounds plus an `IsUpperInfinite` bit, the two facts occupy the same fields and have to be reconciled at runtime. `NpgsqlRange<T>` reconciles them by discarding: pass an upper bound together with `upperBoundInfinite: true` and the constructor keeps the flag and silently drops the value. That is a sound invariant, but it is enforced by a constructor rather than by the type, and it leaves `LowerBound`/`UpperBound` typed `T?` on *every* instance — so even code that has already established the range is bounded still has a nullable to answer for.

Here the question cannot be asked in the first place. `UnboundedEnd` has no `End` property to put a sentinel in; `Finite` has no flag to disown its `End`, and its `Start`/`End` are not nullable. The distinction is carried by the type rather than by a constructor rule that callers have to know about:

```csharp
DateTimeRange.CreateUnboundedEnd(start)                  // UnboundedEnd — genuinely open-ended
DateTimeRange.CreateFinite(start, DateTime.MaxValue)     // Finite — ends at a specific instant
```

The two are not interchangeable, and the compiler will not let them be confused. This matters at the database boundary as well, where Npgsql maps `DateTime.MaxValue` to PostgreSQL `infinity` — a *finite bound that happens to be infinite*, which is still distinct from an unbounded side. See [Entity Framework Core](efcore.md) for how that round-trips.

## Why these element types

The list is deliberately vetted. Interval algebra needs a total order that the type's own comparisons agree with, and — for adjacency — a defined step between neighbouring values. The first six domains have both and are the six PostgreSQL ships as built-ins; `TimeOnly` (and, in the NodaTime satellite, `YearMonth`) clear the same bar and joined in v5.

`double` and `float` have neither, and fail *quietly*. `double.CompareTo` reports `NaN` as less than every value and equal to itself, which is a total order; the IEEE operators disagree, since `NaN < 5.0`, `NaN > 5.0` and `NaN == NaN` are all `false`. A range library generic over `IComparable<T>` therefore accepts `double` without complaint and answers containment against a `NaN` bound with a straight face. There is no exception to catch and no bound to reject at construction — the result is simply wrong. Restricting `T` to a vetted set is what makes the algebra sound, not a limitation left in for later.

`Guid` is absent for a different reason: v7 values are ordered, so the algebra would be well-defined, but "every GUID between these two" is not a question with a domain meaning.

`ZonedDateTime` and `OffsetDateTime` are excluded from the NodaTime satellite by the same reasoning as `double` — NodaTime deliberately gives them no default ordering (instant order and local order disagree), so the `IComparable<T>` constraint rejects them at compile time. The satellite's README carries the full rationale and the `Interval`/`DateInterval` interop.

## Where the model is stricter than the database

One point where the model is *stricter* than the database it mirrors: PostgreSQL's `numeric` has a `NaN` value (sorted above all others by fiat), so a `numrange` bound can be `NaN`. .NET's `decimal` has no such value, so `DecimalRange` cannot form one — the case that `numrange` has to define away does not arise.

The NodaTime types avoid a caveat the BCL ones carry, for a related reason. The two [timestamp
caveats](efcore.md) that `DateTimeRange` and `DateTimeOffsetRange` carry do not arise there:
`LocalDateTime` is wall-clock time by construction and `Instant` is an instant by construction, so
there is no `Kind` to reinterpret and no offset to normalize away.

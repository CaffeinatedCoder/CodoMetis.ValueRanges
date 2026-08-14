# CodoMetis.ValueRanges.NodaTime

NodaTime types for [CodoMetis.ValueRanges](https://www.nuget.org/packages/CodoMetis.ValueRanges): the full PostgreSQL interval algebra over NodaTime's temporal primitives — and, since v6, the value-set family over the same elements.

### Range types

| Type                 | Element         | PostgreSQL equivalent | Discrete |
|----------------------|-----------------|-----------------------|----------|
| `LocalDateRange`     | `LocalDate`     | `daterange`           | ✓ (step: one day) |
| `LocalDateTimeRange` | `LocalDateTime` | `tsrange`             | —        |
| `InstantRange`       | `Instant`       | `tstzrange`           | —        |
| `YearMonthRange`     | `YearMonth`     | `daterange` (month-aligned) | ✓ (step: one month) |

Each type is the same discriminated union of five sealed variants as the core package (`Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange`, `Infinity`), and every operation of the core algebra works unchanged: `Contains`, `Overlaps`, `IsAdjacentTo`, the directional comparisons, `Intersect`, `Union`, `Except`, `Merge`, bound accessors, `RangeAgg`/`RangeIntersectAgg`, `RangeSet<TRange, T>` multiranges, PostgreSQL literal parsing/formatting, and `System.Text.Json` serialization.

### Value set types (v6)

A range says *"every moment between these two"*; a set says *"exactly these moments"* — public holidays, billing months, appointment slots. Immutable, canonical (deduplicated, sorted, never null), and stored as a native PostgreSQL array rather than a range.

| Type               | Element         | PostgreSQL equivalent |
|--------------------|-----------------|-----------------------|
| `LocalDateSet`     | `LocalDate`     | `date[]`              |
| `LocalDateTimeSet` | `LocalDateTime` | `timestamp[]`         |
| `InstantSet`       | `Instant`       | `timestamptz[]`       |
| `LocalTimeSet`     | `LocalTime`     | `time[]`              |
| `YearMonthSet`     | `YearMonth`     | `date[]` (month-aligned) |

The whole core set algebra applies: `Contains`, `Overlaps`, `IsSubsetOf`/`IsSupersetOf` and their proper variants, `Union`, `Remove`, `Count`, `IsEmpty`, plus client-side `Intersect`/`Except`/`Add` — along with array-literal parsing/formatting, JSON, and collection expressions.

```csharp
using CodoMetis.ValueRanges;
using NodaTime;

var sprint = LocalDateRange.CreateFinite(new LocalDate(2025, 1, 6), new LocalDate(2025, 1, 17));
sprint.Contains(new LocalDate(2025, 1, 10));   // true

var deploy = InstantRange.CreateFinite(
    Instant.FromUtc(2025, 6, 1, 22, 0),
    Instant.FromUtc(2025, 6, 2, 2, 0));        // [start, end) — half-open, like tstzrange

var blocked = RangeSet<LocalDateRange, LocalDate>.From([
    LocalDateRange.CreateFinite(new LocalDate(2025, 1, 1), new LocalDate(2025, 1, 31)),
    LocalDateRange.CreateFinite(new LocalDate(2025, 2, 1), new LocalDate(2025, 2, 28))
]);   // { [2025-01-01,2025-02-28] } — adjacent months merge (discrete step)

var billing = YearMonthRange.CreateFinite(new YearMonth(2025, 1), new YearMonth(2025, 12));
billing.Contains(new YearMonth(2025, 6));      // true — month-granularity periods (v5)

// Value sets (v6) — "exactly these", not "everything between"
LocalDateSet holidays = [new LocalDate(2025, 12, 26), new LocalDate(2025, 1, 1)];
holidays.ToString();                           // {2025-01-01,2025-12-26} — sorted, deduplicated
holidays.Contains(new LocalDate(2025, 1, 1));  // true

var closed = LocalDateSet.From(new LocalDate(2025, 1, 1));
closed.IsProperSubsetOf(holidays);             // true

var slots = LocalTimeSet.From(new LocalTime(17, 30), new LocalTime(9, 0));
slots.ToString();                              // {09:00:00,17:30:00}
```

## Installation

```sh
dotnet add package CodoMetis.ValueRanges.NodaTime
```

> Requires .NET 10 or later. A companion EF Core package, [CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime), maps these types to PostgreSQL columns via `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`.

## Two documented caveats, dissolved

The core package documents two reinterpretation rules at the database boundary for the BCL-based types. With NodaTime they do not arise, because the types cannot express the ambiguity in the first place:

- **`tsrange` / `DateTimeRange`**: a UTC-kinded `DateTime` is *reinterpreted* as wall-clock time. A `LocalDateTime` **is** wall-clock time by construction — there is no `Kind` to reinterpret.
- **`tstzrange` / `DateTimeOffsetRange`**: bounds are *normalized* to UTC and the original offset is not round-tripped. An `Instant` **is** what `timestamptz` stores — a point on the global timeline with no offset attached — so there is nothing to normalize and nothing to lose. Zoned or offset values convert explicitly (`zonedDateTime.ToInstant()`) before entering a range, which is exactly NodaTime's own philosophy.

This is the same design move the core package makes for unboundedness: the invalid state is not validated away, it is unrepresentable.

## Why these element types

The core package restricts its element types to domains with a total order that the type's own comparisons agree with, plus — for adjacency — a defined step between neighbours ("Why these element types" in the core README). Applying the same bar to NodaTime:

- **`LocalDate`, `LocalDateTime`, `Instant`** pass, and each maps onto a PostgreSQL built-in range domain.
- **`YearMonth`** (v5) passes too — totally ordered with a one-month step — and although PostgreSQL has no month-granularity type, every month range *is* a month-aligned `daterange`, which is how the EF Core satellite stores it. Billing and reporting periods finally get a type instead of a `daterange` plus a CHECK constraint.
- **`ZonedDateTime` and `OffsetDateTime`** have *no default ordering at all* — NodaTime deliberately declines to implement `IComparable<T>` on them, because ordering by instant and ordering by local time give different answers, and ships named comparers (`Comparer.Instant`, `Comparer.Local`) instead. The core's `T : struct, IComparable<T>, IEquatable<T>` constraint therefore rejects them **at compile time**. This is the `double`/`NaN` argument from the core README with the enforcement moved a level earlier: `double` slipped through the constraint and had to be excluded by policy; here the type system does the excluding. Hold instants in an `InstantRange` and convert at the boundary — the zone is presentation, the instant is the value (and the offset is exactly what `tstzrange` discards on the server, too).
- **`LocalTime`** is totally ordered, and since v6 it has a set type — `LocalTimeSet`, over the built-in `time[]`. There is still no `LocalTimeRange`: a time-of-day *interval* is covered by the core package's `TimeRange` (over `TimeOnly`, mapping to the custom `timerange` type, which the database must `CREATE TYPE` first); convert with `localTime.ToTimeOnly()`. The asymmetry is PostgreSQL's, not ours — `time[]` is built in, `timerange` is not. **`Duration`, `Offset`** remain excluded — no PostgreSQL domain and no interval-algebra meaning.
- **`Period`** is not comparable at all — NodaTime refuses to rank 30 days against 1 month, for the same reason this library refuses `double`: an ordering would have to lie.

## Semantics worth knowing

- **Defaults match the core conventions.** `LocalDateRange.CreateFinite` is closed `[start, end]` (discrete); `LocalDateTimeRange`/`InstantRange` default to half-open `[start, end)` (continuous timestamp convention). Discrete canonicalization applies: `(2025-01-01, 2025-01-31)` normalizes to `[2025-01-02, 2025-01-30]`.
- **The ISO calendar is a construction rule.** `LocalDate.CompareTo` is only defined between dates of the same calendar system, and PostgreSQL `date`/`timestamp` are proleptic Gregorian. `LocalDateRange` and `LocalDateTimeRange` therefore normalize bounds to the ISO calendar at construction (`WithCalendar(CalendarSystem.Iso)` — same day on the timeline, ISO representation). Ranges never hold mixed-calendar bounds, so comparisons cannot throw. A date outside the ISO calendar's year range (far-future non-ISO dates) throws `ArgumentOutOfRangeException` at construction. `YearMonthRange` is stricter: a non-ISO year-month spans parts of two ISO months and has no lossless equivalent, so it **rejects** non-ISO bounds with `ArgumentException` instead of reinterpreting them.
- **The same calendar rule covers the set types, at every entry point.** `LocalDateSet`/`LocalDateTimeSet` normalize to ISO and `YearMonthSet` rejects non-ISO — not only in `From`, but in `Contains`, `Add` and `Remove`, which take a bare element rather than a set. That matters because `LocalDate.Equals` is calendar-sensitive and `LocalDate.CompareTo` *throws* across calendars: without it, `holidays.Contains(copticDate)` would quietly answer `false` for a date the set actually holds. The EF Core package applies the same normalization to a bare probe bound as a query parameter.
- **`YearMonthRange` follows the discrete conventions.** Closed `[start, end]` by default, one-month step: `[2025-01, 2025-03]` and `[2025-04, 2025-06]` are adjacent and merge; `[2025-01, 2026-01)` canonicalizes to `[2025-01, 2025-12]`. Literals use the ISO `uuuu-MM` form: `[2025-01,2025-12]`.
- **Formatting is culture-free.** Literals use NodaTime's ISO patterns: `[2025-01-01,2025-03-31]`, `[2024-06-01T08:00:00,2024-06-01T17:30:00)`, `[2024-06-01T00:00:00Z,2024-07-01T00:00:00Z)`. Subsecond digits appear only when present, up to nanosecond precision.
- **Parsing accepts the PostgreSQL wire form too.** Besides its own canonical output, `Parse` handles literals as `psql` prints them: space-separated timestamps (`"2024-06-01 00:00:00"`) and numeric offsets (`"2024-06-01 14:30:00+02"` — converted to the instant they denote).
- **Precision at the database boundary.** NodaTime carries nanoseconds; PostgreSQL stores microseconds. Sub-microsecond precision is reduced when persisting through the EF Core package (pinned by the live-PostgreSQL integration suite). In-memory operations keep full nanosecond precision.
- **`Instant.MinValue` / `Instant.MaxValue`** map to PostgreSQL `-infinity` / `infinity` by default (an Npgsql rule) — a *finite bound that happens to be infinite*, still distinct from an unbounded side, exactly as the core README describes for `DateTime.MinValue`/`MaxValue`.

## Interop with NodaTime's own interval types

NodaTime ships two interval types of its own; both are deliberately narrower than the range model, and conversions are provided in both directions:

| NodaTime type  | Shape it can express                          | Conversions |
|----------------|-----------------------------------------------|-------------|
| `Interval`     | `[start, end)` over instants; ends may be absent; no empty | `interval.ToInstantRange()` (total) · `range.ToInterval()` (throws for shapes an `Interval` cannot express) |
| `DateInterval` | finite, fully closed `[start, end]` over dates | `dateInterval.ToLocalDateRange()` (total) · `finite.ToDateInterval()` (declared on `LocalDateRange.Finite` — pattern match first) |

`YearMonthRange` additionally converts through its days: `range.ToLocalDateRange()` (total — each bound month expands to its first/last day), `localDateRange.ToYearMonthRange()` (inverse — throws when a canonical bound is not a month boundary), and `finite.ToDateInterval()` on `YearMonthRange.Finite`.

```csharp
var interval = new Interval(Instant.FromUtc(2025, 1, 1, 0, 0), Instant.FromUtc(2025, 2, 1, 0, 0));
InstantRange range = interval.ToInstantRange();          // [start, end) Finite
Interval back      = range.ToInterval();                 // round-trips

if (LocalDateRange.Parse("[2025-01-01,2025-01-31]", null) is LocalDateRange.Finite finite)
    DateInterval dates = finite.ToDateInterval();
```

What the range types add over `Interval`/`DateInterval`: the empty range, unbounded date ranges, all four bound-inclusiveness combinations, the full set algebra (`Union`/`Except`/`Intersect`/`Merge`/`Complement`), multiranges, PostgreSQL literals, and LINQ-to-SQL translation.

## Entity Framework Core

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime
```

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
```

One line — it implies both `UseNodaTime()` (the Npgsql NodaTime plugin) and `UseValueRanges()` (the base plugin), so BCL-based and NodaTime-based range types coexist in one model. The full algebra translates to SQL exactly as documented in the core README, including the discrete `upper(x) - 1` compensation for `LocalDateRange` and the satellite's `RangeAgg`/`RangeIntersectAgg` overloads inside `GroupBy` projections.

The same call registers the five **value set** types, mapped to native array columns — no configuration beyond the property itself:

```csharp
public LocalDateSet Holidays { get; set; } = LocalDateSet.Empty;   // date[]
public YearMonthSet BillingMonths { get; set; } = YearMonthSet.Empty;   // month-aligned date[]

reservations.Where(r => r.Holidays.Contains(day));        // r."Holidays" @> ARRAY['2025-01-01']::date[]
reservations.Where(r => r.Holidays.Overlaps(closures));   // r."Holidays" && @closures
reservations.Where(r => r.BillingMonths.Count > 2);       // cardinality(r."BillingMonths") > 2
```

Containment always translates as `@>`, so a plain GIN index serves it.

`YearMonthRange` columns are stored as **month-aligned `daterange`** (`[2025-01, 2025-03]` ⇒ `[2025-01-01, 2025-04-01)`) — no custom database type involved. Every operator, bound accessor and aggregate translates; reads validate month alignment rather than silently shifting boundaries. Only server-side construction from column values (`YearMonthRange.CreateFinite(x.SomeColumn, …)` inside a query) is unsupported, because months are coarser than the `date` subtype.

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

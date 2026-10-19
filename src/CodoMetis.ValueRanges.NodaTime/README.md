# CodoMetis.ValueRanges.NodaTime

NodaTime types for [CodoMetis.ValueRanges](https://www.nuget.org/packages/CodoMetis.ValueRanges). You get the full PostgreSQL range algebra over NodaTime's date and time types. Since v6 you also get value sets over the same elements.

**Native AOT and trimming compatible**, like the core package. A NativeAOT consumer in CI verifies this ([details](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/serialization.md#native-aot)).

## Range types

| Type                 | Element         | PostgreSQL equivalent | Discrete |
|----------------------|-----------------|-----------------------|----------|
| `LocalDateRange`     | `LocalDate`     | `daterange`           | ✓ (step: one day) |
| `LocalDateTimeRange` | `LocalDateTime` | `tsrange`             | —        |
| `InstantRange`       | `Instant`       | `tstzrange`           | —        |
| `YearMonthRange`     | `YearMonth`     | `daterange` (month-aligned) | ✓ (step: one month) |

Each type is the same discriminated union of five sealed variants as in the core package: `Finite`, `UnboundedStart`, `UnboundedEnd`, `EmptyRange` and `Infinity`. Every operation of the core algebra works unchanged:

- **Queries:** `Contains`, `Overlaps`, `IsAdjacentTo` and the directional comparisons.
- **Set operations:** `Intersect`, `Union`, `Except`, `Merge`, `Gap` and `Gaps`.
- **Bounds and aggregates:** the bound accessors, and `RangeAgg`/`RangeIntersectAgg`.
- **Arithmetic:** `Shift` and `Expand`, by a `Period`, a `Duration` or a number of months.
- **Multiranges:** `RangeSet<TRange, T>`.
- **Calendar ranges:** `LocalDateRange.Month`, `Quarter`, `Year` and `IsoWeek` (ISO 8601 weeks), and `YearMonthRange.Quarter` and `Year`.
- **Splitting:** `Split` by a step or a calendar period, and `Values(step)` on `LocalDateRange` and `YearMonthRange`.
- **Text and JSON:** PostgreSQL literal parsing and formatting, and `System.Text.Json` serialization.

## Value set types (v6)

A range means *"every moment between these two"*. A set means *"exactly these moments"*: public holidays, billing months, appointment slots. Like the core sets, these are immutable and canonical (deduplicated, sorted, no nulls). PostgreSQL stores them as a native array, not as a range.

| Type               | Element         | PostgreSQL equivalent |
|--------------------|-----------------|-----------------------|
| `LocalDateSet`     | `LocalDate`     | `date[]`              |
| `LocalDateTimeSet` | `LocalDateTime` | `timestamp[]`         |
| `InstantSet`       | `Instant`       | `timestamptz[]`       |
| `LocalTimeSet`     | `LocalTime`     | `time[]`              |
| `YearMonthSet`     | `YearMonth`     | `date[]` (month-aligned) |

The whole core set algebra applies:

- **Queries:** `Contains`, `Overlaps`, `IsSubsetOf`/`IsSupersetOf` and their proper versions, `Count` and `IsEmpty`.
- **Set operations:** `Union` and `Remove`, plus `Intersect`/`Except`/`Add`, which run in memory only.
- **Text and JSON:** array literal parsing and formatting, and JSON with [no setup needed](#json).
- **Collection expressions.**

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

> Requires .NET 11 or later; the 10.x line stays on .NET 10. The EF Core package, [CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime](https://www.nuget.org/packages/CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime), maps these types to PostgreSQL columns through `Npgsql.EntityFrameworkCore.PostgreSQL.NodaTime`.

## JSON

Ranges and value sets carry their own converters, as the core types do. You don't need any setup:

```csharp
JsonSerializer.Serialize(sprint);      // "[2025-01-06,2025-01-17]"
JsonSerializer.Serialize(holidays);    // ["2025-01-01","2025-12-26"]
```

The range types format themselves. The set types work differently. They pass each element to System.Text.Json, which has no built-in converter for NodaTime types. So each set family supplies an ISO 8601 fallback through `IValueSetFactory<TSet, T>.ElementJsonConverter`. The fallback writes the same text as the family's array literals. The set consults it last, so any converter you register for the element type still takes precedence.

If the payload also carries **bare** NodaTime values next to the sets, call `AddNodaTimeRangeConverters()`:

```csharp
var options = new JsonSerializerOptions().AddNodaTimeRangeConverters();

JsonSerializer.Serialize(new { Day = new LocalDate(2025, 1, 1), Days = holidays }, options);
// {"Day":"2025-01-01","Days":["2025-01-01","2025-12-26"]}
```

It registers the converter factory on the options, plus the same five element converters. So a `LocalDate` property outside a set gets the ISO 8601 form too. These element converters are a fallback as well. If any other converter on the same options handles one of the five types, that converter is used, wherever it was registered. That makes the call idempotent, and NodaTime.Serialization.SystemTextJson takes precedence in either order:

```csharp
var options = new JsonSerializerOptions()
    .ConfigureForNodaTime(DateTimeZoneProviders.Tzdb)   // Duration, Period, ZonedDateTime, …
    .AddNodaTimeRangeConverters();                      // ranges, sets, and anything left over
```

Use that package when the payload carries NodaTime types beyond these five.

Good to know:

- **A bare value in another calendar fails on write.** Writing a bare `LocalDate`, `LocalDateTime` or `YearMonth` in a calendar other than ISO throws (`ArgumentException`), as NodaTime's own converter does. Its ISO 8601 text would spell the other calendar's fields. A Julian `LocalDate(2024, 1, 1)`, which is ISO 2024-01-14, would read back as ISO 2024-01-01. Convert it with `WithCalendar(CalendarSystem.Iso)` first.
- **Sets and ranges never get that far.** They convert a non-ISO value to ISO when you create them, or reject it (`YearMonth`).
- **Before 9.0, the element converters took over a type by being registered first.** So `AddNodaTimeRangeConverters().ConfigureForNodaTime(…)` hid NodaTime's converters. A bare non-ISO value was written in its own calendar's fields, with no error.

## Two timestamp caveats you don't have

The core package documents two rules where the BCL types reinterpret a value at the database boundary ([details](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md)). With NodaTime this doesn't happen, because the types can't express the ambiguity in the first place:

- **`tsrange` / `DateTimeRange`:** a UTC-kinded `DateTime` is *reinterpreted* as wall-clock time. A `LocalDateTime` **is** wall-clock time by construction. It has no `Kind` to reinterpret.
- **`tstzrange` / `DateTimeOffsetRange`:** bounds are *normalized* to UTC, and the original offset is not round-tripped. An `Instant` **is** what `timestamptz` stores: a point on the global timeline with no offset attached. So there is nothing to normalize and nothing to lose. Convert zoned or offset values explicitly before they go into a range (`zonedDateTime.ToInstant()`). That is exactly NodaTime's own approach.

The core package does the same for unboundedness. The invalid state isn't checked and rejected. It can't be represented at all.

## Why these element types

The core package accepts only element types with a total order that the type's own comparisons agree with. For adjacency, it also needs a defined step between neighbouring values ([Why these element types](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md#why-these-element-types)). Here is the same bar applied to NodaTime:

- **`LocalDate`, `LocalDateTime` and `Instant` pass.** Each maps to a PostgreSQL built-in range type.
- **`YearMonth` (v5) passes too.** It is totally ordered, with a one-month step. PostgreSQL has no month-granularity type, but every month range *is* a month-aligned `daterange`. That is how the EF Core package stores it. So billing and reporting periods get a type of their own, instead of a `daterange` plus a CHECK constraint.
- **`ZonedDateTime` and `OffsetDateTime` have *no default ordering at all*.** NodaTime deliberately doesn't implement `IComparable<T>` on them, because ordering by instant and ordering by local time give different results. It ships named comparers instead (`Comparer.Instant`, `Comparer.Local`). So the core's `T : struct, IComparable<T>, IEquatable<T>` constraint rejects them **at compile time**. This is the `NaN` argument from the core package, enforced one level earlier. `double` gets past the constraint, so `Float64Range` has to reject `NaN` on every value. Here the type system does the excluding. Hold instants in an `InstantRange` and convert at the boundary. The zone is presentation, and the instant is the value. The offset is also exactly what `tstzrange` discards on the server.
- **`LocalTime` is totally ordered, and since v6 it has a set type.** `LocalTimeSet` stores as the built-in `time[]`. There is still no `LocalTimeRange`. For a time-of-day *interval*, use the core package's `TimeRange` over `TimeOnly`, and convert with `localTime.ToTimeOnly()`. `TimeRange` maps to the custom `timerange` type, which your database must `CREATE TYPE` first. The difference comes from PostgreSQL: `time[]` is built in, and `timerange` is not.
- **`Duration` and `Offset` stay excluded.** PostgreSQL has no range type for them, and they have no meaning in interval algebra.
- **`Period` is not comparable at all.** NodaTime won't rank 30 days against 1 month, for the same reason this library rejects `NaN`: any ordering would give wrong results.

## How the NodaTime types behave

- **The defaults match the core package.** `LocalDateRange.CreateFinite` is closed, `[start, end]`, because it is discrete. `LocalDateTimeRange` and `InstantRange` default to half-open `[start, end)`, the usual convention for continuous timestamps. Discrete canonicalization applies: `(2025-01-01, 2025-01-31)` normalizes to `[2025-01-02, 2025-01-30]`.
- **Ranges convert to the ISO calendar when you create them.** `LocalDate.CompareTo` is only defined between dates of the same calendar system, and PostgreSQL `date`/`timestamp` are proleptic Gregorian. So `LocalDateRange` and `LocalDateTimeRange` normalize their bounds to ISO at construction (`WithCalendar(CalendarSystem.Iso)`: the same day on the timeline, in ISO form). A range never holds bounds in mixed calendars, so comparisons can't throw. The conversion can't fail either: every calendar NodaTime ships fits inside the ISO calendar's range (the latest, Coptic, ends on ISO 9999-11-10).
- **`YearMonthRange` is stricter.** A non-ISO year-month spans parts of two ISO months, so it has no lossless ISO equivalent. `YearMonthRange` therefore **rejects** non-ISO bounds (`ArgumentException`) instead of reinterpreting them.
- **The set types follow the same calendar rule, at every entry point.** `LocalDateSet` and `LocalDateTimeSet` normalize to ISO, and `YearMonthSet` rejects non-ISO. That happens in `From`, and also in `Contains`, `Add` and `Remove`, which take a bare element rather than a set. It matters because `LocalDate.Equals` is calendar-sensitive and `LocalDate.CompareTo` *throws* across calendars. Without the rule, `holidays.Contains(copticDate)` would quietly return `false` for a date the set actually holds. The EF Core package applies the same normalization to a bare element bound as a query parameter.
- **The versions for your own element types reject a non-ISO element instead.** `LocalDateSet<T>`, `LocalDateTimeSet<T>` and `YearMonthSet<T>` can't convert an element to ISO, because the only way to build a `T` from an ISO value is to parse it. So an element backed by a value in another calendar fails (`ArgumentException`). That applies in `From`, in a collection expression, when reading (JSON through a registered element converter), and to the element you pass to `Contains`/`Add`/`Remove`. The check is a round trip: the element's ISO text must read back as that element. The EF Core package also rejects one as a bare value in a query. Before 9.0, a wrapper over a Julian 2024-01-01 (ISO 2024-01-14) was written as `2024-01-01` to the literal, JSON and the database, and read back thirteen days off.
- **`YearMonthRange` follows the discrete conventions.** It is closed `[start, end]` by default, with a one-month step. `[2025-01, 2025-03]` and `[2025-04, 2025-06]` are adjacent and merge, and `[2025-01, 2026-01)` canonicalizes to `[2025-01, 2025-12]`. Literals use the ISO `uuuu-MM` form: `[2025-01,2025-12]`.
- **Formatting doesn't depend on the culture.** Literals use NodaTime's ISO patterns: `[2025-01-01,2025-03-31]`, `[2024-06-01T08:00:00,2024-06-01T17:30:00)`, `[2024-06-01T00:00:00Z,2024-07-01T00:00:00Z)`. Subsecond digits appear only when present, up to nanosecond precision.
- **Parsing also accepts the literal as PostgreSQL prints it, and nothing looser.** Besides its own canonical output, `Parse` reads literals as `psql` prints them. That covers space-separated timestamps (`"2024-06-01 00:00:00"`) and numeric offsets (`"2024-06-01 14:30:00+02"`, converted to the instant they denote). It also covers `infinity` / `-infinity` for an `Instant`, `LocalDate` or `LocalDateTime` bound, read as the extreme value Npgsql maps them from. The versions for your own element types read the text with the same ISO patterns before your element sees it.
- **The database keeps microseconds.** NodaTime carries nanoseconds, and PostgreSQL stores microseconds. When you persist through the EF Core package, anything below a microsecond is truncated toward 2000-01-01, as Npgsql truncates. That applies in a column, a JSON document and a SQL constant alike, and the live-PostgreSQL integration suite pins it. In-memory operations keep full nanosecond precision. A literal with more than six fraction digits keeps them too, where PostgreSQL reads the same text rounded half-to-even to the microsecond, so text from another tool can read up to 1 µs apart from the server's reading of it.
- **`Instant.MinValue` / `Instant.MaxValue` map to PostgreSQL `-infinity` / `infinity` by default.** That is an Npgsql rule. Each is a *finite bound that happens to be infinite*, still distinct from an unbounded side. The core package describes the same for `DateTime.MinValue`/`MaxValue` ([Unboundedness is a shape, not a bound value](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/why.md#unboundedness-is-a-shape-not-a-bound-value)).

## Convert to and from NodaTime's interval types

NodaTime ships two interval types of its own. Both are deliberately narrower than the range types, and you can convert in both directions:

| NodaTime type  | Shape it can express                          | Conversions |
|----------------|-----------------------------------------------|-------------|
| `Interval`     | `[start, end)` over instants; ends may be absent; no empty | `interval.ToInstantRange()` (total) · `range.ToInterval()` (throws for shapes an `Interval` cannot express) |
| `DateInterval` | finite, fully closed `[start, end]` over dates | `dateInterval.ToLocalDateRange()` (total) · `finite.ToDateInterval()` (declared on `LocalDateRange.Finite` — pattern match first) |

`YearMonthRange` also converts through its days:

- **`range.ToLocalDateRange()`** always succeeds. Each bound month expands to its first or last day.
- **`localDateRange.ToYearMonthRange()`** is the inverse. It throws when a canonical bound is not a month boundary.
- **`finite.ToDateInterval()`** is declared on `YearMonthRange.Finite`.

```csharp
var interval = new Interval(Instant.FromUtc(2025, 1, 1, 0, 0), Instant.FromUtc(2025, 2, 1, 0, 0));
InstantRange range = interval.ToInstantRange();          // [start, end) Finite
Interval back      = range.ToInterval();                 // round-trips

if (LocalDateRange.Parse("[2025-01-01,2025-01-31]", null) is LocalDateRange.Finite finite)
{
    DateInterval dates = finite.ToDateInterval();
}
```

The range types give you more than `Interval` and `DateInterval`: the empty range, unbounded date ranges, all four combinations of inclusive and exclusive bounds, the full set algebra (`Union`/`Except`/`Intersect`/`Merge`/`Complement`), multiranges, PostgreSQL literals, and queries that run as SQL.

## Use them with EF Core

```sh
dotnet add package CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime
```

```csharp
options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());
```

That one line implies both `UseNodaTime()` (the Npgsql NodaTime plugin) and `UseValueRanges()` (the core EF Core package). So BCL and NodaTime range types work side by side in one model. The full algebra runs as SQL, as described in [Entity Framework Core](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/efcore.md). That includes the discrete `upper(x) - 1` compensation for `LocalDateRange`, and this package's `RangeAgg`/`RangeIntersectAgg` overloads inside `GroupBy` projections, which the EF Core NodaTime package registers.

The same call registers the five **value set** types. They map to native array columns, and you configure nothing beyond the property itself:

```csharp
public LocalDateSet Holidays { get; set; } = LocalDateSet.Empty;   // date[]
public YearMonthSet BillingMonths { get; set; } = YearMonthSet.Empty;   // month-aligned date[]

reservations.Where(r => r.Holidays.Contains(day));        // r."Holidays" @> ARRAY['2025-01-01']::date[]
reservations.Where(r => r.Holidays.Overlaps(closures));   // r."Holidays" && @closures
reservations.Where(r => r.BillingMonths.Count > 2);       // cardinality(r."BillingMonths") > 2
```

Containment always runs as `@>`, so a plain GIN index serves it.

`YearMonthRange` columns are stored as a **month-aligned `daterange`** (`[2025-01, 2025-03]` ⇒ `[2025-01-01, 2025-04-01)`), with no custom database type. Every operator, bound accessor and aggregate runs as SQL. Reads check month alignment instead of silently shifting boundaries.

Good to know:

- **You can't build a `YearMonthRange` from column values inside a query.** `YearMonthRange.CreateFinite(x.SomeColumn, …)` doesn't run as SQL, because months are coarser than the `date` subtype.

## License

MIT — see [LICENSE](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/LICENSE).

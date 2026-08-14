# Architecture

## Overview

CodoMetis.ValueRanges is a .NET 10 class library providing type-safe range types that mirror PostgreSQL's six built-in range domains. Each range is a discriminated union of five sealed variants, making invalid states unrepresentable and pattern matching exhaustive by contract. A companion EF Core package (`CodoMetis.ValueRanges.EFCore.PostgreSQL`) bridges these types to `NpgsqlRange<T>` for LINQ-to-SQL translation.

## Range Types

| C# Type | PostgreSQL | Element Type | Discrete |
|---|---|---|---|
| `Int32Range` | `int4range` | `int` | ✓ |
| `Int64Range` | `int8range` | `long` | ✓ |
| `DateRange` | `daterange` | `DateOnly` | ✓ |
| `DecimalRange` | `numrange` | `decimal` | — |
| `DateTimeRange` | `tsrange` | `DateTime` | — |
| `DateTimeOffsetRange` | `tstzrange` | `DateTimeOffset` | — |
| `TimeRange` | `timerange` (custom) | `TimeOnly` | — |

Discrete types (int, long, DateOnly) implement `NextValueAfter`/`PreviousValueBefore` to return the adjacent value. Continuous types leave them returning `null`. Discrete ranges canonicalize to closed `[lower, upper]` at construction (`Internals/DiscreteCanonical.cs`); continuous ranges default to half-open `[lower, upper)`.

`TimeRange` (v5) is the first domain beyond the six built-ins: `timerange` does not exist in PostgreSQL until the database runs `CREATE TYPE timerange AS RANGE (subtype = time)` (via `HasPostgresRange`), and the Npgsql data source needs `EnableUnmappedTypes()`. A single range cannot cross midnight — overnight windows are two-element `RangeSet`s. PostgreSQL `time`'s special value `24:00:00` is not representable in `TimeOnly`.

## NodaTime Satellites

`CodoMetis.ValueRanges.NodaTime` adds three range types over NodaTime elements, following the identical discriminated-union pattern:

| C# Type | PostgreSQL | Element Type | Discrete |
|---|---|---|---|
| `LocalDateRange` | `daterange` | `LocalDate` | ✓ |
| `LocalDateTimeRange` | `tsrange` | `LocalDateTime` | — |
| `InstantRange` | `tstzrange` | `Instant` | — |
| `YearMonthRange` | `daterange` (month-aligned) | `YearMonth` | ✓ |

Because the whole algebra (extensions, engines, `RangeSet`, parsing defaults, JSON factory) is generic over `IRange<T>`/`IRangeFactory<TRange, T>`, the satellite only defines the unions themselves plus per-type `RangeAgg`/`RangeIntersectAgg` overloads (`NodaTimeRangeAggregateExtensions`) and `Interval`/`DateInterval` interop adapters. It accesses `Internals/` (`DiscreteCanonical`, `RangeFormat`) via `InternalsVisibleTo` — the public API stays closed to third parties, preserving the exhaustive-matching guarantee. Types live in the shared `CodoMetis.ValueRanges` namespace (a `NodaTime` namespace segment would shadow the NodaTime root namespace).

Satellite-specific construction rules: `LocalDate`/`LocalDateTime` bounds normalize to the ISO calendar (`WithCalendar`) so `CompareTo` can never see mixed calendars; text I/O uses NodaTime's culture-free ISO patterns, with PostgreSQL wire-form fallbacks (space separator, numeric offsets) on parse. `YearMonth` bounds **reject** non-ISO calendars instead of normalizing — a non-ISO year-month spans parts of two ISO months, so unlike a date there is no lossless conversion. `YearMonthRange` (v5, one-month discrete step) interops with `LocalDateRange` via `ToLocalDateRange()` (total, expands months to days) and `ToYearMonthRange()` (partial, validates month alignment).

`CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime` registers the `RangeTypeDefinition`s and the aggregate overload class via `UseValueRangesNodaTime()`, which also chains Npgsql's `UseNodaTime()` (element mappings) and `UseValueRanges()`. `YearMonthRange` gets a hand-written `YearMonthRangeTypeDefinition` instead of the generic one: `YearMonth` has no wire representation, so its mappings convert through month-aligned `NpgsqlRange<LocalDate>`/`daterange` values (`Internal/YearMonthRangeTypeDefinition.cs`), it supplies its own element mapping (`YearMonth` ⇄ first-of-month `date`), and it opts out of SQL factory construction (`SupportsSqlConstruction = false`) because months are coarser than the `date` subtype — see the extended `IRangeTypeDefinition` members.

## Discriminated Union Pattern

Every range type is an abstract record with five sealed nested variants:

```
RangeType (abstract, private ctor)
├── EmptyRange     : IEmptyRange<T>       — contains no values
├── Finite         : IFiniteRange<T>      — [start, end] (bounded both sides)
├── UnboundedStart : IUnboundedStartRange<T> — (-∞, end]
├── UnboundedEnd   : IUnboundedEndRange<T>   — [start, +∞)
└── Infinity       : IInfinityRange<T>    — (-∞, +∞)
```

The private base constructor prevents external subtyping, so the compiler guarantees exhaustive switch expressions. Invalid ranges (inverted bounds, degenerate half-open) normalize to `EmptyRange` at construction time.

## Interface Hierarchy (`Core/`)

- **`IRange<T>`** — Marker interface. Carries `internal default methods` `IntersectWith<TRange>()` and `MergeWith<TRange>()` that dispatch per-shape to the engines in `Internals/`.
- **`IRangeFactory<TRange, T>`** — Abstract static factory: `Empty`, `Infinite`, `CreateFinite()`, `CreateUnboundedStart()`, `CreateUnboundedEnd()`, plus virtual `NextValueAfter`/`PreviousValueBefore`. Also implements `IParsable<TRange>` and `IFormattable` with PostgreSQL range literal syntax.
- **Structural interfaces** — `IFiniteRange<T>`, `IUnboundedStartRange<T>`, `IUnboundedEndRange<T>`, `IEmptyRange<T>`, `IInfinityRange<T>` — each provides its own concrete `IntersectWith`/`MergeWith` implementations (e.g., `IInfinityRange<T>` always returns the other operand for intersection, always returns `Infinite` for merge).

All type parameters are constrained to `struct, IComparable<T>, IEquatable<T>`.

## Extension Methods (`RangeExtensions.cs`)

Uses the C# 14 `extension` keyword. Two `extension<T>` blocks:

1. **Query operations** on `IRange<T>` — state checks (`IsEmpty`, `IsInfinity`, etc.), bound accessors (`LowerBound`/`UpperBound` returning `T?`, `LowerBoundInclusive`/`UpperBoundInclusive` — PostgreSQL `lower`/`upper`/`lower_inc`/`upper_inc`), containment, overlap, adjacency, directional comparisons
2. **Set operations** on `IRangeFactory<TRange, T>` — `Intersect` (returns `TRange`), `Merge` (convex hull, `range_merge`, returns `TRange`), `Union`/`Except` (return `RangeSet<TRange, T>`)

See `CodoMetis.ValueRanges/RangeExtensions.cs` for the full implementation.

## Aggregates (`RangeAggregateExtensions.cs`)

`RangeAgg()` (→ `RangeSet`, PostgreSQL `range_agg`) and `RangeIntersectAgg()` (→ `TRange?`, `null` on empty source, PostgreSQL `range_intersect_agg`) over `IEnumerable<TRange>`. Declared as **per-type overloads** (six each) because C# cannot infer the element type `T` from `TRange` alone — constraints do not participate in type inference. A generic private core holds the intersect-fold logic.

## RangeSet<TRange, T> (`RangeSet.cs`)

Immutable multirange counterpart of PostgreSQL's `int4multirange`, etc. A sealed class over `ImmutableArray<TRange>` with a strict invariant:

- Sorted by lower bound
- Pairwise disjoint, pairwise non-adjacent
- No empty elements
- Any `Infinity` input collapses the set to `Infinite` singleton

Key methods:
- `From(IEnumerable<TRange>)` — normalizes (filter → sort via `Internals/RangeSetHelpers.CompareByLowerBound` → greedy merge)
- Bulk ops (`Union`, `Intersect`, `Except`) use O(n+m) merge-join instead of nested loops
- Operators: `\|` for union, `&` for intersect, `-` for except; `==`/`!=` are structural equality (delegating to `Equals`)
- State checks `IsEmpty()`/`IsUnboundedStart()`/`IsUnboundedEnd()`, bound accessors (`LowerBound` = first element's lower, `UpperBound` = last element's upper), and `Merge()` (convex hull of first + last element)
- Set-operand comparisons `Contains(RangeSet)`/`Overlaps(RangeSet)` delegate per element (O(m log n))
- Positional comparisons (`IsAdjacentTo`, `IsStrictlyLeftOf`/`RightOf`, `DoesNotExtendLeftOf`/`RightOf`) mirror PostgreSQL exactly: they consult the **outermost elements only**, and adjacency is **directional through the outer edges** — the operand must end where the set begins or begin where the set ends; interior boundaries (even the inner side of the first/last element) never count. Verified against live PostgreSQL in the integration suite.
- `LowerBoundComparer` — static `IComparer<TRange>` for external sorting

See `CodoMetis.ValueRanges/RangeSet.cs` and `CodoMetis.ValueRanges/RangeLowerBoundComparer.cs`.

## Value Set Types (`Sets/`, v6)

The second type family: immutable, canonical sets of scalar values whose PostgreSQL storage
shape is a native array — `StringSet`/`text[]` is to arrays what `RangeSet`/multirange is to
ranges. Ten closed types (`StringSet`, `GuidSet`, `Int16Set`, `Int32Set`, `Int64Set`,
`DecimalSet`, `DateSet`, `TimeSet`, `DateTimeSet`, `DateTimeOffsetSet`) plus validated-wrapper
arities (`StringSet<T>`, `GuidSet<T>`, `Int32Set<T>`, `Int64Set<T>`) whose `TElement` is
constrained **only on BCL interfaces** (`struct, IEquatable, IFormattable, IParsable`, numeric
families add `IComparable`) so generator-produced domain values never reference this package.
The text-form contract (convention, not constraint): the element's invariant text form must be
exactly the backing primitive's text form.

- **Interfaces** (`Core/`): `IValueSet<T>` (public `Values`) + `IValueSetFactory<TSet, T>`
  (abstract static `Empty`/`From`, internal abstract static `FromTrusted` — which is what keeps
  the family closed to external implementations — and `static virtual` policy hooks
  `CanonicalComparer`/`ParseValue`/`FormatValue`, mirroring `IRangeFactory`).
- **Canonical form** enforced on every construction path (`From`, parse, JSON, materialization):
  deduplicated, sorted, no nulls. String-backed families sort **ordinal** (never culture, never
  the element's own `IComparable` — generated wrappers delegate to culture-sensitive
  `string.CompareTo`); all others sort by element comparison. Load-bearing twice: cheap
  EF change detection and SQL `=` ⇔ set equality.
- **Algebra** lives in one `ValueSetExtensions` class (two C# 14 extension blocks over the
  interfaces) backed by `Internals/ValueSetCore` (stable-sort canonicalization, O(n+m) merge
  scans, instance-preserving results). `Count`/`IsEmpty` are **instance properties** on each
  concrete type — C# forbids extension properties in expression trees (CS9296), which would
  make them untranslatable.
- Deliberately **no `IEnumerable<T>`** (duck-typed struct `GetEnumerator()` + `Values` only):
  EF's conventions discover `IEnumerable<primitive>` types as primitive collections, the exact
  machinery the scalar-mapping design avoids. `[CollectionBuilder]` enables collection
  expressions.
- **NodaTime satellite** adds `LocalDateSet`, `LocalDateTimeSet`, `InstantSet`, `LocalTimeSet`
  (native `time[]` — no `CREATE TYPE`, unlike `timerange`) and `YearMonthSet`. `LocalDate`/
  `LocalDateTime` elements normalize to ISO calendar at construction; `YearMonth` rejects
  non-ISO (no lossless conversion) — mirroring the range types.
- `IValueSet<T>.NormalizeElement` (internal, default identity) is the seam that extends that
  normalization past `From` to the **element-level** operations — `Contains`, `Add`, `Remove`
  take a bare element, not a set, and would otherwise compare an un-normalized probe against
  normalized storage. A set type that normalizes or validates elements **must** override it:
  `LocalDate.CompareTo` throws across calendars and `Equals` silently returns `false`, so the
  failure modes are a wrong answer, a leaked NodaTime exception, or a non-ISO element smuggled
  into an empty set. The EF satellite mirrors it through the definition's `normalizeValue`,
  which now also backs an `ElementTypeMapping` so the same probe normalizes on the server.
- `Internals/SetFormat` implements PostgreSQL array-literal parse/format (quoting, escaping,
  unquoted `NULL` rejected — sets never contain null; reads throw on corrupt data).

## JSON Serialization (`Serialization/`)

- `RangeJsonConverter<TRange, T>` — serializes to/from PostgreSQL range literal strings
- `ValueSetJsonConverter<TSet, T>` — serializes value sets as plain JSON arrays, delegating
  element serialization to System.Text.Json (element converters apply); reads normalize and
  reject null elements
- `RangeJsonConverterFactory` — auto-registers for any type implementing `IRangeFactory<TRange, T>`
  or `IValueSetFactory<TSet, T>`, or `RangeSet<TRange, T>`
- Extension: `AddRangeConverters()` registers all at once

## EF Core PostgreSQL (`CodoMetis.ValueRanges.EFCore.PostgreSQL/`)

- **`ValueRangesMethodCallTranslator`** — translates LINQ methods to PostgreSQL operators (`@>`, `&&`, `<@`, `<<`, `>>`, `&<`, `&>`, `-|-`, `*`, `+`, `-`) and functions (`lower`, `upper`, `lower_inc`, `upper_inc`, `isempty`, `lower_inf`, `upper_inf`, `range_merge`), for ranges and multiranges
- **`ValueRangesAggregateMethodCallTranslator`** — translates `RangeAgg`/`RangeIntersectAgg` to `range_agg`/`range_intersect_agg` inside grouped queries, for every declaring class registered via `RangeTypeRegistry.RegisterAggregateExtensions`
- **Type mapping** — maps range types to PostgreSQL range columns, RangeSet to multirange columns
- **`RangeTypeRegistry`** (`Internal/`) — the single wiring point. Process-wide and additive: the seven core types (six built-ins + `timerange`) are registered up front; satellites contribute `RangeTypeDefinition`s at options-configuration time via `Register` (idempotent per range CLR type, thread-safe immutable-snapshot swap). Lookups: by range/set CLR type, by element type (the `IRange<T>`-typed-operand fallback — one range type per element type, enforced), and by store type name (first registration owns the name; BCL and NodaTime types share `daterange` etc., so store-name-only resolution stays with the BCL types)
- **`IRangeTypeDefinition` extension points** — `ElementTypeMapping` (default `null`: resolve the subtype mapping from the type mapping source) lets a definition supply a converting element mapping when the element CLR type is unknown to the provider (NodaTime `YearMonth` ⇄ `date`); `SupportsSqlConstruction` (default `true`) lets a definition whose model granularity is coarser than its store subtype opt out of server-side factory-constructor translation
- **Value sets** (v6) — mirror wiring beside the range machinery:
  - **`SetTypeRegistry`** (`Internal/`, sibling of `RangeTypeRegistry`): ten closed core definitions up front; the four wrapper families are matched by **open generic definition** with closed instantiations built lazily and cached — no per-element registration exists, so there is nothing to misconfigure. Satellites register closed definitions (the NodaTime satellite adds its five from `UseValueRangesNodaTime()`). **Deliberately no store-name lookup**: `text[]` etc. stay with the provider's native array mappings, so plain `string[]` properties and scaffolding are untouched
  - **`ValueSetTypeMapping<TSet, TElement, TPrimitive>`** converts sets to primitive arrays at the provider boundary (Npgsql binds those natively); reads route through `From` — non-canonical rows normalize, null elements throw. Literals render uniformly as `ARRAY['…',…]::type[]`, with a per-definition literal-text hook (NodaTime's null-format `IFormattable` is the culture long form, not ISO)
  - **Element mappings** (`BridgedElementTypeMapping`): a bare element parameter in `col @> ARRAY[@p]` binds as its backing primitive — the same definition-supplied converting-element-mapping seam `YearMonth` uses. Two producers: the wrapper families (text-form contract fails loudly here with an error naming it), and any `SetTypeDefinition` carrying a `normalizeValue`, which gets an element mapping applying that same normalization (the server-side half of `NormalizeElement`)
  - **Translators**: `ValueSetsMethodCallTranslator` (Contains → `@>` `ARRAY[value]` unconditionally — always GIN-servable; Overlaps/IsSubsetOf/IsSupersetOf → `&&`/`<@`/`@>`; IsProperSubsetOf/IsProperSupersetOf → the operator paired with its **negated converse** rather than `<>`, keeping both halves multiplicity-insensitive; Remove → `array_remove`; Union → `array_cat`) and `ValueSetsMemberTranslator` (`Count`/`IsEmpty` → `cardinality`). `Intersect`/`Except`/`Add` have no translation: PostgreSQL's array type has only 9 operators (`@> <@ && = <> < <= > >=`) against the range type's 17 — there is no array intersection or difference, and no sorted insert (`array_sort` orders by collation, not ordinal). They stay in the core library because it has no EF dependency and is an in-memory type family first. Set `==` is translated by EF itself as `col = @p` and assumes canonical writers; all package-translated operators are order-insensitive and stay correct against non-canonical rows
  - **Composing on a translated `Union`**: `array_cat` concatenates rather than canonicalizing. That is invisible to the operators above and to materialization (reads route through `From`), but `cardinality` would double-count shared elements — so `Count` over an `array_cat` operand is **refused** by the member translator (EF reports the query as untranslatable, which beats a quietly inflated number), while `IsEmpty` stays translated (a concatenation is empty exactly when both sides are). **Equality over a union is wrong and cannot be intercepted** — EF emits the `=` itself. Canonicalizing server-side is not available: PostgreSQL has no array-distinct function (verified against 18.4 — `array_sort`/`array_reverse`/`array_shuffle` exist, nothing that deduplicates; `intarray.uniq` is `int[]`-only and not installed by default), so it would need a `SELECT DISTINCT … ORDER BY` subquery whose ordering could not match CLR canonical order anyway (`text` orders by database collation, not ordinal; `uuid` orders byte-wise where `Guid.CompareTo` orders field-wise)
  - `YearMonthSet` gets a hand-written `YearMonthSetTypeDefinition` (month-aligned `date[]`, reads throw on non-aligned dates), reusing the range family's `YearMonthTypeMapping` as its element mapping
- **Enable**: `options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());` — or `npgsql.UseValueRangesNodaTime()` from the NodaTime satellite, which implies it

## Engine Internals (`Internals/`)

- `IntersectEngine.cs`, `MergeEngine.cs` — per-shape intersection and merge logic
- `ExceptEngine.cs` — set difference with boundary inversion at cut points
- `DiscreteCanonical.cs` — canonicalizes discrete ranges to closed form
- `RangeBoundHelpers.cs`, `RangeFormat.cs`, `RangeSetHelpers.cs` — shared utilities
- `ValueSetCore.cs` — the value set engine: canonicalization, membership (binary search over the canonical order — `IValueSet<T>.CanonicalOrder` is the instance-side view of `CanonicalComparer` that `Contains` needs and cannot reach statically), equality, merge-scan algebra. `Union` keeps the **left** operand's representative among comparer-equal elements, the same "first in input order survives" tie-break `Canonicalize` applies; only the left-hand identity shortcut is sound, since a count matching the right operand merely means the left was a subset
- `SetFormat.cs` — PostgreSQL array-literal parse/format for value sets (sibling of `RangeFormat`)

# Axes

What a consumer varies, per surface family of `claims.json`, and which test already varies it. Written by
hand, like BLite's catalogue tests (#105): an axis is a setting of an API a claim touches, its values are
the ones a consumer sets, and a **pair no test varies** is the next probe class (`*ProbeTests`, registered
in `oracles.json` with a seed, named in the `heldBy` of the claims it holds). #114, #116 and #117 were each
found by one such pair, as a one-off that was not kept.

## Literals and JSON (J01–J12, J16–J30, J32–J33)

| Axis | Values | Varied by |
|---|---|---|
| Type | 11 range unions × 5 shapes, 11 `RangeSet<…>`, 30 value set types (15 families × closed, wrapper) | every conventions sweep, through `RangeProbes` and `SetProbes` |
| Declared type | union, sealed variant, `IRange<T>` / shape interface / `IValueSet<T>`, `object`, `List<object>`, record property, dictionary value, dictionary key | `JsonDefaultContractTests` (union, variant, interface, `object`, `RangeSet`, key); `JsonOptionsProbeTests` (all but key, × options) |
| `JsonSerializerOptions` | default, camelCase, snake_case, `ReferenceHandler.Preserve`, source-generated context, `NumberHandling.AllowReadingFromString`; `WriteAsString`; `AllowDuplicateProperties = false`; `DictionaryKeyPolicy`; a union converter on the options; a converter on a property | `JsonOptionsProbeTests` (the first six × declared type); `SetJsonConverterTests` (`WriteAsString`, closed vs wrapper); `JsonDictionaryKeyTests` (duplicates, key policy); `JsonConverterPrecedenceTests`, `JsonDefaultContractTests` (union converter × declared type); `SetSourceGenerationTests`, `InterfaceDeclaredJsonTests` (a context missing the element or runtime type) |
| Current culture | invariant, de-DE, sv-SE, fa-IR, th-TH | `LiteralCultureContractTests` (`ToString` × type × shape) |
| Explicit provider | null, invariant, th-TH, de-DE, a custom provider | `LiteralProviderContractTests` (format and parse × type × shape) |
| Entry point | `Parse(string)`, `Parse(span)`, `TryParse`, JSON value, JSON dictionary key, `TypeConverter` | `TemporalLiteralStrictnessContractTests` (a non-ISO element × every entry point × temporal type); `SpanParsingTests` (string vs span) |
| Literal corpus | quoted, escaped and whitespace bounds; empty quoted bound; element whitespace; inverted bounds; `infinity` spellings; `24:00:00`; more than 28 digits | `RangeLiteralGrammarContractTests`, `SetElementWhiteSpaceContractTests`, `LiteralGrammarParityTests` (the first three, against PostgreSQL); the rest are #114 and #115, found once and not swept |
| Element value | one pair per element type (`RangeProbes.Bounds`), one probe list per element type (`SetProbes`) | every conventions sweep; extremes only in `DifferentialFuzzParityTests` (EF) and `TemporalLiteralGrammarTests` |

Pairs no test varies: current culture × JSON (the culture sweep stops at `ToString`); entry point × culture ×
type (`th-TH` through `Parse`, `TypeConverter`, configuration, MVC and a minimal API, including a
variant-typed parameter — #116 is one cell); literal corpus × entry point against `psql -At` on the same text
(#114 and #115 are cells); a NodaTime element converter registered by `ConfigureForNodaTime` × declared type;
options × dictionary key beyond default and `DictionaryKeyPolicy`.

## TypeConverter, configuration and web binding (J13–J15, J31)

| Axis | Values | Varied by |
|---|---|---|
| Entry point | `TypeDescriptor` to and from text, `IConfiguration` binding, MVC `[FromQuery]` / `[FromBody]`, minimal API parameter / body, OpenAPI schema | `TypeConverterContractTests` (every type × shape); `ConfigurationBindingTests` (one property per family); `HostBindingTests` (a `DateRange` and a `StringSet` query, a booking body, both hosts); `OpenApiSchemaTests` |
| Declared type | union, sealed variant, nullable, `RangeSet`, set | `TypeConverterContractTests` (variant refuses another shape); `ConfigurationBindingTests` (variant property); `HostBindingTests` (union only — #116) |
| Current culture | invariant, de-DE, th-TH | `ConfigurationBindingTests` (configuration only); `TypeConverterContractTests` (de-DE handed to the converter) |
| Input | a valid literal, `""`, `empty`, malformed, another shape | `ConfigurationBindingTests`, `HostBindingTests` (malformed → 400) |
| Host JSON options | ASP.NET Core defaults (camelCase, case-insensitive, `AllowReadingFromString`); `ConfigureHttpJsonOptions` with a context or `AllowDuplicateProperties = false` | `HostBindingTests` (defaults only) |

Pairs no test varies: host × variant-typed parameter (#116; its fix adds the cell); host × request culture
(`RequestLocalization`); configuration × every type (one per family today); host × a source-generated
context in `ConfigureHttpJsonOptions`; MVC × `AllowInputFormatterExceptionMessages` (the excerpt a client sees, J26).

## EF Core (E01–E38)

| Axis | Values | Varied by |
|---|---|---|
| Type | every range, multirange and set type, BCL and NodaTime | `MappingParityTests`, `DifferentialFuzzParityTests`; `ShapeMatrixParityTests` over `int4range`, `numrange`, `daterange` only |
| Shape pair | 5 × 5 ordered | `ShapeMatrixParityTests` (against PostgreSQL), `SmallModelOracleTests` (against set theory) |
| Operand position | column × column, column × parameter, column × inlined constant, constant element, server-side constructor | `MultirangeCoverageTests`, `InlineRangeConstantTests`, `InlineRangeConstantExecutionTests`; `DifferentialFuzzParityTests` (three legs, constant and parameter per probe) |
| Store text route | binary parameter, SQL literal, JSON document | `TemporalStoreParityTests`, `TemporalStoreTextTests`, `JsonColumnTests` |
| `Npgsql.DisableDateTimeInfinityConversions` | off, on | every integration project (off); `InfinityConversionsDisabledTests` (on, temporal extremes only — per process, hence its own project) |
| Temporal value | sub-microsecond, the extremes, each `DateTimeKind` | `TemporalStoreParityTests`, `RangeRoundTripTests`, `ProviderConversionTests` |
| Composition on a translated `Union` | every operator and clause the guard knows | `UnionCanonicalityTests` (results, not SQL) |
| Rows written by other tools | non-canonical arrays; `infinity` spelled by another writer | `SetIntegrationTests` (arrays, E21); #117 (ranges, found once) |
| Model | runtime model, compiled model | `TemporalStoreTextTests` (compiled model, wrapper family) |
| Server | `postgres:18-alpine`; the surface listing for 17 and 18 | `ContainerLifecycle`; `postgres-surface.catalog.txt` |

Pairs no test varies: `DisableDateTimeInfinityConversions` × JSON document × NodaTime; shape pair × every
range type against PostgreSQL (three domains today; the fuzz sweep samples the rest); another writer's
spelling × every read path (column, JSON document, `ExecuteUpdate`); compiled model × every wrapper family;
a second PostgreSQL major in CI.

## Hosting and AOT (A01–A03, J31)

| Axis | Values | Varied by |
|---|---|---|
| Runtime | JIT, Native AOT | every test project (JIT); `test/consumer-smoke-test.sh` (AOT, core + NodaTime) |
| Serializer | reflection, source-generated context, a context missing the element type | `SetSourceGenerationTests` (A01); the smoke test under AOT (A02, A03) |
| Package graph | core alone; core + NodaTime; the EF NodaTime satellite pulling all four | the smoke test's three consumers |
| Delivery | packed from the branch; the release `dist` | the smoke test, both modes on every PR |

Pairs no test varies: AOT × every type (the smoke test picks a `RangeSet` and one wrapper set); AOT ×
declared as `object` or an interface; `TrimMode=full` without AOT; EF Core under AOT (not claimed).

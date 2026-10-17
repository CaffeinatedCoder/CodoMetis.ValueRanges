# Oracles

How bug-finding in this repository works as a system rather than as one sweep per bug: a catalogue of
what the documents promise, an enumeration of what the API offers, oracle tests that check the library
against an independent answer over generated cases, seeds that prove each oracle can fail, a registry
for the disagreements that are found but not yet fixed, and an enumeration of what PostgreSQL offers
with what the library does about each row. Everything lives under
`test/oracles/`; the convention tests in `test/CodoMetis.ValueRanges.Conventions.Tests/Oracle*.cs` hold
it consistent, and `dotnet run tools/oracles.cs` proves the seeds locally.

## Why

Every high-yield check in this repository was written *after* a bug of its kind had shipped. The
shape matrix against PostgreSQL came after `IsAdjacentTo` (6.2.1); the small-model oracle after
`IsStrictlyLeftOf` and `Except` (7.0.0); the multirange oracle after `RangeSet.Except` (8.0.0); the
literal-culture and temporal-strictness sweeps after `de-DE` turned `[1.5,2)` into `[1,52)` and
`th-TH` read a date as the year 1483 (9.0.0). Each sweep found more than the bug it was written for,
which is the point of an oracle: one test, a whole class of divergence. But which sweep to write next
was decided by which bug had just been reported, and nothing said which promises had no sweep at all.

The machinery here turns that around. The claims the documents make are listed, each with the test
that holds it or an empty slot. The public members are listed by reflection, each named by a claim or
waived with a reason. The empty slots and the unwaived members are the worklist. An oracle only counts
once a seed shows it failing. A disagreement an oracle finds is registered against an issue, so the
suite stays green without hiding it, and the entry is forced out when the fix lands.

This is the same approach as the `prober` tooling in the sibling repository CodoMetis.TypeKit, kept to
the parts that pay for themselves here: no covering arrays, no ledger, no replay footprints, no agent.
Claims are extracted by a person (or a model) once per document version and held by a test; the
sweeps themselves are ordinary MSTest classes.

## The seven files

### `claims.json` — what the documents promise

One entry per sentence a consumer-facing document states about behaviour, in a form a test can decide:

```json
{
  "id": "R03-discrete-canonical",
  "source": "docs/ranges.md#creating-ranges",
  "quote": "Discrete ranges canonicalize to closed `[lower, upper]` at construction",
  "claim": "for a discrete element type, any bound form passed to CreateFinite yields the closed canonical form",
  "oracle": "metamorphic",
  "surfaces": ["IRangeFactory.CreateFinite", "*Range.CreateFinite"],
  "heldBy": ["SmallModelOracleTests", "DomainParityOracleTests"]
}
```

- `source` is `file#heading-slug`; the heading must exist.
- `quote` is verbatim (whitespace collapsed), and `OracleCatalogueTests` fails when the document no
  longer contains it. A reworded promise therefore stales its claim loudly, which is what makes the
  catalogue follow the documents instead of drifting from them.
- `oracle` names the kind: **claim** (the document states the result and a test asserts it),
  **differential** (two ways to the same result must agree: memory and PostgreSQL, two entry points, two
  declared types), **metamorphic** (a relation that holds for any input: round trip, idempotence, equal
  inputs give equal outputs and hash codes), **exception** (a documented rejection, or the absence of
  an undocumented one), **silent-default** (a value that should have been produced or rejected comes
  back as `default`, `null`, empty, or a success that did nothing).
- `surfaces` are the public members a probe of the claim calls, as `TypeName.Member` keys with `*` as a
  wildcard (`*Range.Parse`, `Int32Range+Finite.Start`, `RangeSet.Union`). Each must match a real member.
- `heldBy` names the test classes that assert the claim. **Empty is allowed and is the worklist.**

The claim sources, in order of authority: `README.md`, `docs/ranges.md`, `docs/value-sets.md`,
`docs/serialization.md`, `docs/efcore.md`, `docs/getting-started.md`, then the package READMEs for
anything not already claimed. Every one of the six must carry at least one claim. `architecture.md`,
`testing.md`, `why.md`, `migration.md` and the changelog explain and record; they are not promises.

### The API surface — what the library offers

`ApiSurface` (conventions project) enumerates every public member declared on a public type of the
four shipping assemblies, one key per member, overloads collapsed. `OracleSurfaceCoverageTests`
requires every key to be matched by some claim's `surfaces` or by an entry in
**`surface-waivers.json`** (`{ "pattern": "reason" }`). A new public member therefore cannot be
merged without a decision: which claim covers it, or why none needs to. A waiver whose members have
all since been claimed fails as stale, so the waiver list shrinks as the catalogue grows.

Waive plumbing, never behaviour: a converter's `CanConvert`, the EF plugin members EF Core calls, a
record's compiler-generated operators that an equality claim already reaches through the union.

### `oracles.json` — the sweeps

The registry of test classes that are oracles: checks against an independent answer over generated
cases, not worked examples. Per entry the class, its project, the oracle kind and what it checks
against. A class named like an oracle (`*OracleTests`, `*ParityTests`) that holds a claim must be
registered, so that the seed rule reaches it.

### `seeds/*.patch` — proof that each oracle can fail

A test that passes with and without a defect is not a guard. The repository's rule has always been to
prove a guard by reverting its fix ([verify-the-guard](../.claude/skills/verify-the-guard/SKILL.md));
a seed is that revert kept as a file, so it is checked every time instead of once.

```
Seed: IsStrictlyLeftOf decides by the receiver's shape: an UnboundedStart receiver answers false
Oracles: SmallModelOracleTests, DomainParityOracleTests, ShapeMatrixParityTests
Fails: every pair with an UnboundedStart receiver strictly left of its operand
History: shipped in 6.x, fixed in 7.0.0

diff --git a/src/CodoMetis.ValueRanges/RangeExtensions.cs b/src/CodoMetis.ValueRanges/RangeExtensions.cs
…
```

- A seed changes `src/` only and names every oracle it was shown to fail. One defect may seed several
  oracles; every registered oracle must be named by at least one seed.
- `OracleSeedTests` holds in CI without building: every oracle has a seed, every seed names a
  registered oracle and a defect, touches only `src/`, and still applies to the current tree. A
  refactor that strands a seed fails CI instead of leaving the oracle unproven.
- `dotnet run tools/oracles.cs seeds [filter]` proves locally that each seed is still **caught**: a
  scratch copy of the working tree, the patch applied, each named oracle built and run in its project,
  one `CAUGHT` / `MISSED` / `ERROR` line per (seed, oracle), exit code 1 unless everything is caught.
  Oracles that need Docker run against a container like the integration suite does. It is not part of
  CI — a build and a run per seed — and it is run before a release.

### `candidates.json` — disagreements found but not fixed

```json
{ "oracle": "DomainParityOracleTests", "row": "TimeRange [09:00,10:00) IsAdjacentTo TimeRange", "family": "midnight wrap", "issue": "#<n>" }
```

A sweep hands its failure lines to `Candidates.Verdict` (`test/oracles/Candidates.cs`, linked into
every project that holds an oracle) instead of asserting them away. The rule has three outcomes for a
line that matches a candidate's `row` text:

- the candidate's `issue` is **filed**: the line is reported as `SKIPPED [known candidate: family,
  issue]` on the test output and does not fail;
- the `issue` is **empty**: the line fails, labelled `[known candidate: family]`, so nothing is
  quarantined before it is filed;
- **no line matches** the candidate any more: the test fails with `candidate fixed: remove the entry`,
  so the fix's PR removes it and the registry cannot outlive the defect.

The `row` is the stable prefix of the sweep's own failure line — the operands and the operation, not
the actual value, which changes with the fix. An optional `method` scopes the row to one test method
of the class; use it when a class has several sweeps with disjoint rows, since "candidate fixed" is
otherwise judged over every row of the class.

### `postgres-surface.json` — what PostgreSQL offers

The two catalogues above hold the library to its documents and the documents to the library. Neither
says which of **PostgreSQL's own** range, multirange and array operators and functions the library
mirrors, which it translates, and which it leaves out on purpose. That list was enumerated by hand on
2026-10-17 and found seven gaps (#92–#98); this file keeps it as data so it cannot drift. One entry
per `pg_operator` / `pg_proc` row over `anyrange`, `anymultirange`, `anyarray` and
`anycompatiblearray`, plus the constructors of the six built-in range types and their multiranges,
keyed by the server's own signature:

```json
{
  "signature": "anyrange &< anymultirange",
  "kind": "operator",
  "since": 14,
  "member": null,
  "translation": null,
  "waiver": null,
  "issue": 92
}
```

- `signature` is `left op right` for an operator and `name(argtypes)` for a function or aggregate,
  exactly as the listing (below) prints it. `kind` is `operator`, `function`, `aggregate`, or
  `other` for the rows no catalogue query reaches: SQL syntax (`= ANY`, `WITHOUT OVERLAPS`, an
  exclusion constraint) and functions typed over concrete types (`generate_series`).
- `since` is the PostgreSQL major the row first appeared in; `8` stands for anything older than 8.4.
- `member` is the `TypeName.Member` key of the member that mirrors the row, in `ApiSurface`'s
  format (`*` wildcard), and must match a real public member. `translation` is the SQL fragment the
  no-database EF tests assert for it (`" &< "`, `"range_merge("`), which must occur in the sources of
  `test/*EFCore.PostgreSQL*.Tests/`, so a dropped assertion fails the entry; `null` means the member
  is in-memory only (`Add` on a set, `Values()` on a range).
- `waiver` is the reason no member should exist, and `issue` is the number that will add one.
  **Every entry has a member, a waiver, or an issue**; the entries with only an issue are the
  worklist, which `dotnet run tools/oracles.cs status` prints. A waiver whose member has since been
  filled fails as stale, as does an issue whose member exists — the same rule as
  `surface-waivers.json`, so the file shrinks toward "member" as the gaps close.

**`postgres-surface.catalog.txt`** is the raw listing the entries are checked against: one line per
row, `kind signature -> result`, under a `# PostgreSQL <version>` header. It is regenerated by
`dotnet run tools/oracles.cs pg-surface [image]` from a Testcontainers server (default
`postgres:18-alpine`), locally and never in CI, and checked in. The query leaves out what is not
callable surface — functions taking or returning `internal` or `cstring`, a type's I/O functions,
the function behind an operator (listed as the operator), an aggregate's transition and final
functions (listed as the aggregate), and a range type's canonical and subtype-difference functions —
and keeps the operator-class support functions that are plain SQL functions (`hash_range`,
`range_cmp`, `btarraycmp`, …), which are waived by name. 17 and 18 list the same operators; 18 adds
`array_sort` (three overloads) and `array_reverse`.

`OraclePostgresSurfaceTests` holds all of it without a database: every listed row has an entry of its
kind and every operator, function or aggregate entry is a listed row (a misspelt signature fails, and
so does a row the server dropped), every member exists, every translation is asserted, no waiver or
issue outlives its member, and a waiver that points at a `docs/*.md#heading` points at one that
exists. Refreshing the listing against a new major therefore turns its new rows into failures naming
them, each a decision: a member, a waiver, or an issue.

### `axes.md` — what a consumer varies

The claims say what is promised and the surface says what is offered. Neither says which *settings* a
consumer combines: the declared type of a property, the `JsonSerializerOptions` of the host, the
current culture, the entry point, `Npgsql.DisableDateTimeInfinityConversions`. #114, #116 and #117 were
each found by one such combination that no test had varied, as a one-off that was not kept. `axes.md` is
the hand-written list (route B of #105, the BLite way: no covering arrays, no generated axes): one
section per surface family, each axis with its values, the test that already varies it, and **the pairs
no test varies yet**, which are the next probe class.

A **probe class** (`*ProbeTests`) is an ordinary MSTest sweep over the full product of a few axes, in
the project that already has the harness: the conventions project for type discovery, `AspNetCore.Tests`
for a `TestServer`, the integration project for a live server. Its oracle is the document's promise
(differential across declarations, or the refusal the document names), it ends in `Candidates.Verdict`,
and it is registered in `oracles.json` with a seed and named in the `heldBy` of the claims it holds like
every other oracle — `OracleCatalogueTests` requires the registration of any `*ProbeTests` that holds a
claim. It stays under a minute, because nothing replays by footprint here. A probe whose test method runs
once per `[DynamicData]` row passes the row's label as `rowPrefix` to the verdict, so a candidate is
scoped to the row whose lines start with it and another row does not report it fixed.

The first is `JsonOptionsProbeTests`: every range, multirange and value set type × {union, sealed
variant, interface, `object`, record property, dictionary value} × {default, camelCase, snake_case,
`ReferenceHandler.Preserve`, a source-generated context, `NumberHandling.AllowReadingFromString`}.

The second (#124) is entry point × culture × type, in two classes because half the entry points need a host.
`CultureEntryPointProbeTests` (conventions project) reads and writes every type through `Parse`, `TryParse`,
`TypeDescriptor`, configuration binding, JSON and interpolation under four *current* cultures, against what the
invariant culture reads and writes. `HostCultureProbeTests` (AspNetCore project) binds every type, as the union,
each variant, nullable, the multirange and the set, from a query, a route value, a form field and a JSON body
through MVC and a minimal API, under four *request* cultures set by `RequestLocalization`; a valid value answers
200 with itself, an invalid one 400, never 200 with `null`. Its only disagreement is #116, measured wider than
the issue first said: every variant of every range type, bound by a minimal API through `TryParse` from a
query, nullable query, route value or form field, answers 200 with `null` for the right shape and for another
shape alike. It is registered as one candidate per range type, scoped by the line's `variant minimal TryParse`
prefix so the JSON-body and MVC cells of the same variants stay held.

Route B's step 3 is `LiteralCorpusProbeTests` (integration project, #123): literal corpus × entry point × type,
judged by PostgreSQL's own text input on the same text. The corpus is generated per element kind from the
families `axes.md` lists; the authority is `pg_input_is_valid($1, type)` and the canonical text, asked
for every literal of a type in one statement; the entry points are `Parse` and `TryParse` over a string
and a span, JSON, the `TypeConverter` and the EF Core JSON document reader, plus the EF Core write-back
as a SQL constant and a JSON document, compared on the server as typed values. Its first run turned the
pair's two known cells (#114, #115) into 120 rows over 23 types and found eight more defects (#126–#133): a
sub-microsecond literal is a different value on each side, integer and array forms PostgreSQL 16 reads
are refused, `GuidSet` follows `Guid.Parse` instead of `uuid_in`, `LocalDateTime` refuses an offset,
`DateTimeOffset` refuses offsets beyond ±14:00, `(,max]` is an undocumented boundary row, and a NUL
character reaches a `text` element. A row the documents name as a refusal (J06, J07), a value outside the
CLR type's domain, and an extreme the store spells `±infinity` are counted, not reported, so the
classification is part of what the class asserts and lives in its `DocumentedRefusal`.

## How to use it

**Run the oracles.** They are ordinary tests; `dotnet test` runs them all. The registered classes are
listed in `oracles.json`; the integration ones need Docker.

**See the worklist.** `dotnet run tools/oracles.cs status` prints the claims without a holder, the
oracles without a seed, the candidates and their issues, the PostgreSQL rows that have only an issue,
and the document headings no claim quotes.

**Hold a claim.** Pick an unheld claim, write or extend an oracle for it, add the class to `heldBy`.
If the class is a new sweep, register it in `oracles.json` and seed it (below). Prefer extending an
existing sweep — a new operation is one more line in the small-model sweep, a new type is discovered
by the sweeps that enumerate types — over a new class.

**Seed an oracle.** Plant the defect in `src/` (revert a past fix, or break the thing the oracle
checks), run the oracle and read the failure: it must fail *for the defect*, not with a compile error
or an exception from elsewhere. Then:

```bash
{ printf 'Seed: <what is broken>\nOracles: <Class, Class>\nFails: <what fails>\n\n'; git diff -- src/; } \
  > test/oracles/seeds/<defect>.patch
git checkout -- src/
dotnet run tools/oracles.cs seeds <defect>
```

**Register a candidate.** When a sweep disagrees and the cause is not a harness mistake, add the row
under a family label with an empty issue; it keeps failing with the label. Write up the repro, file
the issue (labelled `bug` and `found-by/oracle`, plus `package/*`, `area/*` and `severity/*`, under
the release epic it ships in), put the number in. Remove the entry in the PR that fixes it, and give that commit the
`Found-By: OT` trailer ([CONTRIBUTING](../CONTRIBUTING.md#commit-trailers)).

**Triage a disagreement, in this order.**

1. Is the oracle wrong? Set theory and PostgreSQL rarely are; the *comparison* can be. Check the
   model's grid (bounds inside the universe, half-step for continuous types), the embedding, and
   whether the row is one the document calls intended.
2. Is it the documented divergence? `docs/architecture.md#domain-width-and-the-boundary` lists where
   the model and PostgreSQL answer over different domains on purpose; `DomainBoundaryDivergenceTests`
   pins each row. A new row there is a documentation change, not a candidate.
3. Otherwise it is a candidate. The disagreement names the operands and the operation; a one-line
   repro in a unit test is the next step, and `verify-the-guard` applies to the fix.

**Refresh the PostgreSQL listing.** With Docker running, `dotnet run tools/oracles.cs pg-surface`
(or `pg-surface postgres:19-alpine` for a newer major) rewrites `postgres-surface.catalog.txt`; then
run `OraclePostgresSurfaceTests`, and give every row it names an entry. A row the library now mirrors
gets its `member` and the fragment a translation test asserts; a row it should not mirror gets a
`waiver` that says why in terms of the set or range semantics (order, randomness, dimensions, a
canonical form the server cannot keep); anything else gets an issue, filed under the release epic it
ships in with the package and area labels, and the number goes in `issue`.

**Write a probe.** Take a "pairs no test varies" line from `axes.md`, write the product as a
`*ProbeTests` class (one row per type, the other axes inside the row, the oracle from the claims it
holds), register and seed it, name it in those claims' `heldBy`, and move the pair into the axis table
with the class as what varies it.

**Add a claim.** Read the document section, copy the sentence verbatim into `quote`, restate it as
"under conditions, observable result", name the surfaces a probe calls, and leave `heldBy` empty if
nothing holds it. Ids are a letter (R ranges, M multirange, S sets, J literals/JSON/TypeConverter, E
EF Core, A hosting/AOT), two digits and a slug, sorted.

## Generated operands

Most sweeps enumerate their cases: shapes, small universes, five cultures. `DifferentialFuzzParityTests`
draws them, with a seeded generator, so its rows are reproducible but nobody chose them
([testing.md](testing.md#the-differential-fuzzing-sweep)). Two rules follow from that:

- **A candidate row is the stable prefix of a drawn line**: type, operation, optional `at the domain
  edge`, and the shape pair, e.g. `"> Overlaps(set) "` or `"DateRange IsAdjacentTo at the domain edge "`.
  Never include the operands, because the next budget or seed draws other ones.
- **Candidates belong to the default seed.** With `VALUERANGES_FUZZ_SEED` set, the sweep passes
  `requireEveryCandidate: false` to `Candidates.Verdict`. Unknown lines still fail, but an unmet
  candidate is not reported as fixed. Raising `VALUERANGES_FUZZ_ROWS` keeps the default rows as a
  prefix, so it changes nothing here.

To replay a line, run with its seed and enough rows to reach its row number. To shrink one, keep
the operation and change one operand at a time toward a literal you can paste into `psql`. The
issue gets that shrunk row.

## What it does not see

A promise no document makes. A behaviour whose correct answer has no independent reference (API
design, error wording). Native AOT compilation, which only `test/consumer-smoke-test.sh` observes.
Performance. Those stay with review and with the integration suite's hand-picked tests.

# Oracles

How bug-finding in this repository works as a system rather than as one sweep per bug: a catalogue of
what the documents promise, an enumeration of what the API offers, oracle tests that check the library
against an independent answer over generated cases, seeds that prove each oracle can fail, and a
registry for the disagreements that are found but not yet fixed. Everything lives under
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

## The five files

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

## How to use it

**Run the oracles.** They are ordinary tests; `dotnet test` runs them all. The registered classes are
listed in `oracles.json`; the integration ones need Docker.

**See the worklist.** `dotnet run tools/oracles.cs status` prints the claims without a holder, the
oracles without a seed, the candidates and their issues, and the document headings no claim quotes.

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

**Add a claim.** Read the document section, copy the sentence verbatim into `quote`, restate it as
"under conditions, observable result", name the surfaces a probe calls, and leave `heldBy` empty if
nothing holds it. Ids are a letter (R ranges, M multirange, S sets, J literals/JSON/TypeConverter, E
EF Core, A hosting/AOT), two digits and a slug, sorted.

## What it does not see

A promise no document makes. A behaviour whose correct answer has no independent reference (API
design, error wording). Native AOT compilation, which only `test/consumer-smoke-test.sh` observes.
Performance. Those stay with review and with the integration suite's hand-picked tests.

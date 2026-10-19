# Contributing

Issues and pull requests are welcome. For a security report, follow [SECURITY.md](SECURITY.md)
instead — please don't open a public issue for those.

## Getting set up

```bash
dotnet build CodoMetis.ValueRanges.slnx
dotnet test
```

`global.json` pins the SDK to the line's major (10.0.x on `main`), so install a .NET 10 SDK; a
newer major on the machine is refused rather than silently used ([branches and
releases](#branches-and-releases)).

Shipping projects live under `src/`, test projects under `test/`.
[AGENTS.md](AGENTS.md) is the architecture guide, with [docs/architecture.md](docs/architecture.md)
and [docs/testing.md](docs/testing.md) behind it — worth reading before changing a range type, a
value set family, or the EF plugin.

The two integration projects (`test/*IntegrationTests/`) spin up a real PostgreSQL container via
Testcontainers. Without Docker they go inconclusive locally, so skip them if you need to:

```bash
dotnet test --filter "FullyQualifiedName!~IntegrationTests"
```

On CI they **fail** rather than skip. An unreachable container must not quietly
retire the layer that is the authority on PostgreSQL semantics while the build reports green.

The container runs PostgreSQL 16, the oldest version 10.x supports. CI also runs both projects on 18.
To run them on another version, name its image:

```bash
VALUERANGES_POSTGRES_IMAGE=postgres:18-alpine dotnet test test/CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests
```

A server older than 16 fails every test that needs it, with the server's version in the message.

The EF packages are built against their floor (Npgsql EF 10.0.0, so EF Core 10.0.0), but a consumer's
application resolves the newest patch. CI therefore runs the four EF test projects twice: on the floor,
and with the test hosts floated to the newest published EF Core and Npgsql EF 10 patch while `src/`
stays on the floor (`test/Directory.Build.targets`). Both are required. To run the float locally, pass
the two versions, which CI keeps in the `integration-legs` matrix of `.github/workflows/dotnet.yml`:

```bash
dotnet test test/CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests -p:EfFloat=10.0.13 -p:NpgsqlEfFloat=10.0.3
```

A floor-only CI missed #302 that way: an empty captured list failed on the server on every EF Core
patch from 10.0.2 on.

## The quality bar

This package's characteristic failure is not a crash. It is a value that looks right, compares
equal to itself, round-trips without error — and means something different from what was declared.
A boundary that is inclusive on one side of the database wire and exclusive on the other. A set
that does not contain the element it was built from. A JSON payload that reads back as `default`.
Nothing throws, and the caller gets an answer with a straight face.

Everything below exists because of that failure mode.

**A bug fix needs a regression test, and the test must be proven to work.** Write the test, watch
it fail, apply the fix, watch it pass — then *revert the fix and confirm the test fails again*. A
test that passes both with and without the fix is not a regression test. This has caught vacuous
tests in this repository more than once, including two written during the hardening pass that added
the convention suite. See [`.claude/skills/verify-the-guard`](.claude/skills/verify-the-guard/SKILL.md).
For an oracle sweep the revert is kept as a seed patch under `test/oracles/seeds/`, so the proof is
repeated rather than remembered — see [Oracle tests](#oracle-tests).

**Changes to a range type, a value set family, the canonical form, a type mapping, or a translator
get a value-semantics review.** The lens is written down in
[`.claude/skills/value-semantics-review`](.claude/skills/value-semantics-review/SKILL.md): what the
value means on both sides of the wire, whether every construction path canonicalizes, whether a
probe is comparable with what was stored, and whether the ordering the array was sorted by is the
ordering it is searched with.

**PostgreSQL is the specification.** Where the in-memory algebra and PostgreSQL could disagree, the
integration suite decides — it has already caught the discrete `upper()` canonicalization offset
and the directional multirange adjacency rule. A translation change without an integration test is
an assertion about PostgreSQL, not a demonstration.

**Prefer a loud failure to a silent one.** Where the package cannot represent something correctly,
it should throw with an actionable message — never return a plausible value that is wrong. A query
that refuses to translate beats one that translates to different semantics: `Count` over a `Union`
is deliberately refused for exactly this reason.

**Prefer a test to a convention.** Mechanical rules belong in the suite, not in a document nobody
re-reads. `test/CodoMetis.ValueRanges.Conventions.Tests` exists because the alternative was
remembering — and the value set rules in AGENTS.md are asserted there behaviourally, not merely
written down.

## Issues

Use the **Bug** or **Enhancement** form. The bug form asks whether the failure is silent or loud,
because that decides how the fix ships. A wrong answer without an exception found before
10.0.0 ships is fixed in 10.0.0, even if it's old. After that it ships as a minor on every
supported line, under "Changed answers" ([below](#a-fix-that-changes-an-answer)). A loud failure
can wait for a minor either way.

Releases and longer themes are tracked as `epic` issues, with the work as sub-issues. The pinned
[Roadmap](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/34) lists them all,
along with what each label means. A maintainer sets the `package/*`, `area/*` and `severity/*`
labels when triaging, and a pull request gets the same labels as the issue it closes.

## Pull requests

- One concern per PR, with the reasoning in the description rather than only the diff.
- Full suite green, including integration if you have Docker.
- User-visible changes get a changelog entry in the root [CHANGELOG.md](CHANGELOG.md) *and* in the
  affected package changelogs under `src/`, in the form [below](#changelog). The convention tests
  enforce that the version being shipped is documented everywhere it needs to be.
- New range or set types must be wired through the registries — the parity tests will tell you if
  they are not.
- Match the surrounding style. Comments explain *why*, especially where behaviour is load-bearing
  and non-obvious.

## Branches and releases

Up to two lines are supported at once, one per .NET major: 10.x, the LTS line on .NET 10, and
11.x on .NET 11 ([SECURITY.md](SECURITY.md#supported-versions) lists what each line gets). `main`
is the next release: 10.0.0 until its tag, the 11 line after it. Each line has one branch, one `Version` in its
`Directory.Build.props`, and the same CI and protection, and a release is a `v*` tag on the branch
it ships from. The tag must match that `Version`, or the release workflow stops before publishing.

### Until the `v10.0.0` tag

- **`main`** is 10.0.0, the last release made under the old rule: it takes the silent fixes as a
  major.
- **`release/9.x`** holds 9.x patches and ends at the `v10.0.0` tag. A change for both goes into
  `main` first; once it has merged, cherry-pick it onto a branch from `release/9.x`
  (`git cherry-pick -x`, so the commit names the original) and open a PR into `release/9.x` that
  links the `main` PR. Only loud fixes and additions go to 9.x. A silent fix waits for 10.0.0.
- **There is no preview branch.** The .NET 11 retarget waits for the tag, so 11.0.0 is validated
  against a 10.0.0 that is on NuGet and starts with every 10.0.0 fix.

### At the `v10.0.0` tag

Cut `release/10.x` from the tag. Then retarget `main` to .NET 11 (#103) in a PR that also moves
`Version` to `11.0.0-preview.1` and the ApiCompat baseline and SECURITY.md row to 10.0.0, and tag
the previews `v11.0.0-preview.N` on `main`. `release/9.x` takes no changes from then on, and a fix
reaches 9.x users only by upgrading.

### After the `v10.0.0` tag

- **`main`** is the 11 line: previews until .NET 11 is released, then `v11.0.0` and its minors.
- **`release/10.x`** is the LTS line. A change for both goes into `main` first and is cherry-picked
  with `-x` into a PR against `release/10.x` that links the `main` PR. A change for 10.x only, such
  as its version bump or a fix to code `main` no longer has, goes straight into `release/10.x`.
- **Additions** ship as 10.x minors for six months after `v11.0.0`, and always reach `main`
  first, so the LTS line never carries something the current line lacks. The cut-off date is
  written here when `v11.0.0` is tagged (the tag date plus six months). After it, 10.x takes
  fixes only: loud ones in a patch or a minor, silent ones in a minor.

### A fix that changes an answer

A silent fix is one where a call returned a wrong value without throwing, and the fix changes
what it returns. With two lines, a major means a new .NET release and nothing else (11.0.0 is the
first), so a silent fix has no major to wait for on the LTS line. After 10.0.0 it ships as a minor
on every supported line, never a patch, and that minor says so:

- **Its changelog entry opens with `### Changed answers`**, one row per changed answer, each
  linking its issue and its section of [docs/migration.md](docs/migration.md).
- **docs/migration.md** gets a `## Changed answers in x.y.0` section for that minor, saying how to
  find the calls whose answer changed.

10.0.0 is the last release under the old rule, and `main` keeps it until its tag: a silent fix is
listed under Breaking in the major, and no 9.x minor carries one. `ChangelogConventionTests` holds the form: `Changed answers`
appears only in a minor, each row links an issue and an existing migration heading, and after
10.0.0 a change that says it used to produce a value ("where it returned …") must sit there. That
test reads the wording, not the label. The label half is `.github/scripts/pr-hygiene.sh`: a PR
labelled `silent` into a minor fails until its closing issue has a row there, and into a patch it
fails outright.

### Releases on two lines

- **Issues:** `Closes #n` closes an issue only when the PR merges into the default branch, `main`.
  An issue fixed only on a release branch is closed by hand, with a comment linking the PR.
- **Changelog:** a change goes under the version it ships in on each branch, never under the next
  major too. `main`'s changelog keeps every release's section, of both lines, so it stays the full
  history; a cherry-pick brings its 10.x entry with it. A preview has its own
  `## [11.0.0-preview.N]` section, which the release workflow publishes as its notes.
- **SECURITY.md's supported-versions table** names the latest release of each line on NuGet,
  never the version being prepared, and has no row for a preview. `SecurityPolicyConventionTests`
  compares this branch's row with `PackageValidationBaselineVersion`, so move the row when you
  move the baseline, which happens when you open the next version right after a tag; the tagged
  commit still names the release before it. The other line's row is compared with
  `CompanionLineRelease` in `Directory.Build.props`, which is empty until 11.0.0 ships. From then
  on, a minor on one line moves its row and `CompanionLineRelease` on the other branch in a PR of
  its own. The table gets its second row in the PR that opens the first version after `v11.0.0`.
- **The ApiCompat baseline** is the latest stable release before the version being prepared,
  never a preview: the 11 line validates against 10.0.0 until 11.0.0 ships.
- **GitHub's Latest release** is the highest stable version. The release workflow decides it
  rather than trusting the API's default, so a 10.x tag pushed after 11.0.0 exists, or any
  preview, can't take Latest from it.

## Changelog

A version's section of the root `CHANGELOG.md` is the GitHub release's notes, word for word, so it
is written for someone deciding what to change before they upgrade. The package changelogs use the
same form, with the entries that affect that package.

- **The heading** is `## [x.y.z] — yyyy-mm-dd`. Write entries under the version being prepared,
  never under `Unreleased`. The date is the tag's: set it in all five changelogs in the commit that
  gets tagged, because no test or workflow checks it.
- **One optional line first**, for a version where something can break you: what kind of change it
  is, and a link to its section of [docs/migration.md](docs/migration.md).
- **Then the sections, in this order:** `### Changed answers` (a minor only, see
  [above](#a-fix-that-changes-an-answer)), `### Breaking`, `### Added`, `### Changed`, `### Fixed`.
  Leave out the ones you don't need. A version with nothing for a package says so in one line:
  "No change in this package."
- **Breaking** holds everything that can break existing code or data, in a major or not: a call
  that answers, accepts, writes or translates differently, a payload that changes shape, a query
  that now throws. Each line says what to do about it.
- **One change per line, on one line.** Say what happens now and, for a fix, what happened before:
  "`RangeSet.Contains(T)` on the infinite set returns `true`, where it threw …". Start with the area
  where it helps a reader skim (`JSON:`, `Literals:`, `EF Core:`, `NodaTime:`). In `Added`, lead
  with the new member in bold.
- **Keep each section in area order:** `Ranges`, `Value sets`, `Literals`, `JSON`, `ASP.NET Core`,
  `EF Core`, `NodaTime`, `Docs`, so a reader finds an area's lines together. In `Added`, the core
  members come first, then `EF Core:`, then `Docs:`. Use no headings below the three sections.
- **End with a link** to the guide that explains it. Links are absolute
  (`https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/docs/…`), because the
  release page can't resolve a relative one.
- **Leave out** the reasoning, the measurements and the history of how it was found, test names,
  internal refactoring with no effect you can observe, and fixes to something new in the same
  release. The reasoning goes in the guide or the migration guide, the history in the commit and
  the issue.
- **Write in the plain voice of the guides:** short sentences, active voice, no emphasis markers or
  emoji.

`ChangelogConventionTests` holds the form: known sections in order, one line per change, at most
60 words a change and 40 for the introduction, absolute links, and the `Changed answers` rules.

## Oracle tests

An oracle test checks the library against an independent answer over generated cases instead of a
hand-written expectation: set theory over every representable range of a small universe
(`SmallModelOracleTests`), PostgreSQL over every ordered pair of shapes (`ShapeMatrixParityTests`),
the invariant literal under five cultures for every discovered type (`LiteralCultureContractTests`).
[docs/oracles.md](docs/oracles.md) describes the machinery around them, which lives under
`test/oracles/`:

- **Claims.** `claims.json` lists what the consumer-facing documents promise, each quoting its sentence
  verbatim and naming the tests that hold it. An empty `heldBy` is the worklist. A reworded document
  fails the catalogue test until the claim follows.
- **Surface.** Every public member of the four packages is named by a claim or waived with a reason in
  `surface-waivers.json`. A new public member needs one or the other.
- **Seeds.** Every registered oracle (`oracles.json`) ships a seed under `seeds/`: a patch to `src/`
  that plants a defect the oracle was shown to catch. `OracleSeedTests` keeps the seeds present and
  applicable in CI; `dotnet run tools/oracles.cs seeds` re-proves locally that each is caught. Run it
  before a release, and refresh a seed a refactor has stranded.
- **Candidates.** A disagreement an oracle finds goes into `candidates.json` with its family and the
  issue that tracks it. It fails labelled until the issue is filed, is reported as skipped while the
  issue is open, and fails as `candidate fixed` once the fix lands, so the entry is removed in the same
  PR.
- **Clones.** The hand-copied type families — the value sets and their wrapper arities, the range
  unions, the EF registrations — are compared sibling by sibling from the source text
  (`CloneSweep.cs`, `CloneDivergenceTests`). A member one sibling lacks or alone has, or a body that
  drifted from the one the others share, is either waived with a reason in `clone-waivers.json` or
  fails CI. `dotnet run tools/oracles.cs clones` prints the list with the JSON line to paste for a new
  one; a fix that lands at one site and not its siblings is what this catches.

## Commit trailers

A fix commit says how the defect was found and what kind it is, as git trailers after the body:

```
Found-By: OT
Defect-Class: ALG
```

- `Found-By:` — `OT` oracle test, `RV` code review, `IS` issue report, `US` user, `CA` analyst,
  `SM` the consumer smoke test. An issue an oracle found carries the `found-by/oracle` label, so
  the trailer and the label count the same finds from either side.
- `Defect-Class:` — `ALG` range or set algebra answering wrongly, `CAN` canonical form, `LIT` literal
  parsing or formatting, `JSON` System.Text.Json or TypeConverter, `SQL` translation or type mapping,
  `STORE` the value stored differs from the value declared, `PKG` packaging or release wiring.

`git log --format='%(trailers:key=Found-By,valueonly)' | grep . | sort | uniq -c` counts the finders.

## AI-assisted development

A substantial portion of this codebase — including much of the range algebra, the value set family
and the test suite — was written with AI assistance, using
[Claude Code](https://claude.com/claude-code). This is stated plainly because the project has real
adopters who deserve to know how it is built, not because anything about it is unusual.

What that does and does not mean:

- **Direction, architecture, and acceptance are the maintainer's.** Design decisions, trade-offs,
  and what merges are human calls. AI is a tool used under review, not an autonomous committer.
- **Nothing is accepted on the basis that it looks right.** The verification discipline above
  exists precisely because plausible-looking code is cheap to produce. Fixes are proven against the
  bug they claim to fix; convention tests are proven by seeding the defect they claim to catch.
- **The audit trail is public.** Commit messages record what was wrong, what a consumer would have
  experienced, and how the fix was verified. That is the evidence, and it is more informative than
  any statement about authorship.
- **Code is judged on behaviour, not provenance.** A defect is a defect whoever typed it. AI
  assistance neither excuses a bug nor makes one more likely to be excused.

If you contribute with AI assistance, that is fine — the same bar applies. Please make sure you
understand and can defend what you are submitting, and that it is yours to contribute.

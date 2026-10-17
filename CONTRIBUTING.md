# Contributing

Issues and pull requests are welcome. For a security report, follow [SECURITY.md](SECURITY.md)
instead — please don't open a public issue for those.

## Getting set up

```bash
dotnet build CodoMetis.ValueRanges.slnx
dotnet test
```

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
because that decides which release the fix goes in: a wrong answer without an exception is fixed
in the next major even if it's old, and a loud failure can wait for a minor.

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

`main` is the next release. `release/9.x` is the maintained 9.x line, cut from `main` before the
first 10.0.0 change landed. Both get the same CI and the same protection, and a release is a `v*`
tag on the branch it ships from: `v9.1.1` on `release/9.x`, `v10.0.0` on `main`. The tag must
match `Version` in that branch's `Directory.Build.props`, or the release workflow stops before
publishing.

- **A change for both lines** goes into `main` first. Once it has merged, cherry-pick it onto a
  branch from `release/9.x` (`git cherry-pick -x`, so the commit names the original) and open a PR
  into `release/9.x` that links the `main` PR. Only loud fixes and additions go to 9.x. A silent
  fix, or one that changes an answer, waits for the next major, as on `main`.
- **A change for 9.x only**, such as its version bump or a fix to code `main` no longer has, goes
  straight into `release/9.x`.
- **Issues:** `Closes #n` closes an issue only when the PR merges into `main`. An issue fixed only
  on `release/9.x` is closed by hand, with a comment linking the PR.
- **Changelog:** a 9.x change goes under its 9.x version on both branches, never under the next
  major too. `main`'s changelog keeps every 9.x section, so it stays the full history.
- **SECURITY.md's supported-versions table** names the latest release on NuGet, never the version
  being prepared. `SecurityPolicyConventionTests` compares it with `PackageValidationBaselineVersion`,
  so move the table when you move the baseline, which happens when you open the next version right
  after a tag. The tagged commit still names the release before it.
- **GitHub's Latest release** is the highest version. The release workflow publishes a 9.x patch
  that comes out after 10.0.0 without marking it Latest.

## Changelog

A version's section of the root `CHANGELOG.md` is the GitHub release's notes, word for word, so it
is written for someone deciding what to change before they upgrade. The package changelogs use the
same form, with the entries that affect that package.

- **The heading** is `## [x.y.z] — yyyy-mm-dd`. Write entries under the version being prepared,
  never under `Unreleased`.
- **One optional line first**, for a version where something can break you: what kind of change it
  is, and a link to its section of [docs/migration.md](docs/migration.md).
- **Then the sections, in this order:** `### Breaking`, `### Added`, `### Changed`, `### Fixed`.
  Leave out the ones you don't need. A version with nothing for a package says so in one line:
  "No change in this package."
- **Breaking** holds everything that can break existing code or data, in a major or not: a call
  that answers, accepts, writes or translates differently, a payload that changes shape, a query
  that now throws. Each line says what to do about it.
- **One change per line, on one line.** Say what happens now and, for a fix, what happened before:
  "`RangeSet.Contains(T)` on the infinite set returns `true`, where it threw …". Start with the area
  where it helps a reader skim (`JSON:`, `Literals:`, `EF Core:`, `NodaTime:`). In `Added`, lead
  with the new member in bold.
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
60 words a change and 40 for the introduction, and absolute links.

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

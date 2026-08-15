# Contributing

Issues and pull requests are welcome. For a security report, follow [SECURITY.md](SECURITY.md)
instead — please don't open a public issue for those.

## Getting set up

```bash
dotnet build CodoMetis.ValueRanges.slnx
dotnet test
```

Shipping projects live under `src/`, test projects under `test/`.
[CLAUDE.md](CLAUDE.md) is the architecture guide, with [docs/architecture.md](docs/architecture.md)
and [docs/testing.md](docs/testing.md) behind it — worth reading before changing a range type, a
value set family, or the EF plugin.

The integration suite spins up a real PostgreSQL container via Testcontainers. Without Docker it
goes inconclusive locally, so skip it if you need to:

```bash
dotnet test test/CodoMetis.ValueRanges.Tests
```

On CI the same suite **fails** rather than skipping. An unreachable container must not quietly
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
remembering — and the value set rules in CLAUDE.md are asserted there behaviourally, not merely
written down.

## Pull requests

- One concern per PR, with the reasoning in the description rather than only the diff.
- Full suite green, including integration if you have Docker.
- User-visible changes get a changelog entry in the root [CHANGELOG.md](CHANGELOG.md) *and* in the
  affected package changelogs under `src/`. The convention tests enforce that the version being
  shipped is documented everywhere it needs to be.
- New range or set types must be wired through the registries — the parity tests will tell you if
  they are not.
- Match the surrounding style. Comments explain *why*, especially where behaviour is load-bearing
  and non-obvious.

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

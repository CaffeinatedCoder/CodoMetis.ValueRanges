# Security policy

## Reporting a vulnerability

**Please do not open a public issue for security reports.**

Report privately through GitHub:

> **[Report a vulnerability](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/security/advisories/new)**
> (Security → Advisories → Report a vulnerability)

If you cannot use GitHub advisories, email **meistercoder@mr-gross.de** with `SECURITY` in the
subject.

Helpful to include, as far as you have it:

- The package and version (`CodoMetis.ValueRanges`, `.NodaTime`, `.EFCore.PostgreSQL`,
  `.EFCore.PostgreSQL.NodaTime`)
- The type involved, and whether the value came from application code or from parsed input
- A minimal reproduction — the literal or JSON payload, or the LINQ query and the SQL it generated
- What you expected instead
- Why you consider it security-relevant — the impact you have in mind

## What to expect

This is a single-maintainer, non-commercial open-source project. There is no SLA, and the
following is a good-faith intention rather than a guarantee:

| | |
|---|---|
| Acknowledgement | within 7 days |
| Initial assessment | within 30 days |
| Fix or documented decision not to fix | best effort, tracked in the advisory |

You will be told either way. If a report turns out not to be a security issue, it gets handled as
an ordinary bug and you will be told that too, with reasoning.

## Disclosure

Coordinated disclosure. The intent is to publish a fix and a GitHub Security Advisory together,
and to keep the report private until then. Ninety days from acknowledgement is the default ceiling
for going public regardless of fix status — if that timing does not suit you, say so in the report
and it can be discussed.

Reporters are credited in the advisory unless they ask not to be.

## Supported versions

Security fixes land on the latest released minor version. Older majors are not maintained — the
remedy for those is to upgrade.

| Version | Supported |
|---|---|
| 6.1.x | ✅ |
| < 6.1 | ❌ |

## Where this package sits

Useful context for judging impact, and for anyone doing supply-chain due diligence.

Unlike a design-time or build-time library, **these types run inside your application's request
path**, and they reach untrusted input by design:

1. **Parsing.** Every type implements `IParsable<T>` over PostgreSQL literal syntax, and ships
   System.Text.Json converters intended for ASP.NET Core APIs. A range or set arriving in a request
   body or query string is parsed by this package.
2. **Query generation.** The EF Core plugin translates the algebra to SQL. Values normally bind as
   parameters, but constants render as SQL literals — element text is written into the statement
   with PostgreSQL quote-doubling.
3. **In-memory algebra.** Containment, overlap and membership answers computed from those values,
   frequently in code that is deciding something.

There is no raw-SQL surface: the package exposes no API that takes a SQL fragment and passes it
through, so there is no documented "you asked for it" escape hatch here.

Published packages carry Source Link metadata (repository and commit) and symbol packages, and are
published through GitHub Actions Trusted Publishing — no long-lived credential exists that could
publish on this project's behalf. Each release also carries a CycloneDX SBOM per package
(`*.cdx.json`), attached to the [GitHub release](https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/releases)
for that version. Nothing in this repository is referenced with `PrivateAssets=all`, so those SBOMs
list exactly the dependencies a consumer receives, and a convention test fails the build if that
stops being true.

## In scope

- **Escaping failures in generated SQL.** Element and bound values are quoted before being written
  into a SQL literal. A value that escapes its quoting and injects arbitrary SQL is a
  vulnerability, including when the value originated in application code — the whole point of the
  boundary is that callers do not have to think about it.
- **A membership or containment answer that is silently wrong, where it was acting as a security
  control.** If `Contains`, `IsSubsetOf` or `Overlaps` returns the wrong answer without throwing —
  a probe compared un-normalized against normalized storage, a canonical array searched with an
  order it was not sorted by, a value that does not survive a JSON or database round trip — and
  the set was carrying something like a permission list or a tenant's allowed keys, treat it as
  security-relevant and report it privately. This is the honest item: it is the class of defect
  this codebase has actually produced, and several convention tests exist specifically to catch it.
- **Denial of service through parsing.** Input that makes `Parse` or a JSON converter consume
  disproportionate time or memory relative to its length.
- **Supply-chain integrity issues** — anything suggesting a published package does not correspond
  to the source at its recorded commit, or a weakness in the release workflow.

## Out of scope

- **Vulnerabilities in EF Core, Npgsql, NodaTime, or PostgreSQL itself.** Report those to their
  projects; if one affects this package's behaviour, a report here is still welcome so it can be
  documented.
- **Anything that fails loudly.** A `FormatException` on malformed input, an
  `InvalidOperationException` from a rejected value, or a query that refuses to translate is the
  package working as intended. Rejecting bad input is not a vulnerability; accepting it silently
  would be.
- **Your own authorization logic.** That a `StringSet<PermissionKey>` can hold permissions does not
  make this package an authorization system. A missing check in your application is your defect —
  a wrong answer from a check you did make is the in-scope item above.
- **Culture or locale differences in display formatting.** Canonical form and literal generation
  are invariant by contract and any deviation there is a bug; how you choose to render a value to a
  user is not.

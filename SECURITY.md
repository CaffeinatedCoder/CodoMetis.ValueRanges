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

Up to two lines are supported at once, one per .NET major: the LTS line and the current line. Each
receives security fixes on its latest released minor. Older minors of a line are fixed by
upgrading to its latest minor, which never changes the platform you target. Older lines are not
maintained.

| Version | Supported |
|---|---|
| 10.3.x | ✅ |
| < 10.3 | ❌ |

Previews (`11.0.0-preview.N`) are not supported versions and have no row: a fix reaches a preview
in the next preview.

### How the table changes

- **Until 11.0.0 ships**, one line is supported: the latest 10.x minor. 9.x stopped receiving fixes
  when 10.0.0 shipped. The 11.0.0 previews on `main` have no row.
- **When 11.0.0 ships**, the table gets its second row: 11.0.x, and below it the latest 10.x minor.
  From then on each line's row moves when that line ships a minor.

### For how long

There is no fixed end-of-support date. Each line is tied to the .NET release it targets:

- **10.x**, the LTS line, targets **.NET 10** (`net10.0`, EF Core 10, PostgreSQL 16+). The intention
  is to maintain it for as long as Microsoft supports .NET 10 (LTS, until November 2028).
- **11.x** targets **.NET 11** (`net11.0`, EF Core 11, Npgsql 11, PostgreSQL 16+), as previews until
  .NET 11 is released. The intention is to maintain it for as long as Microsoft supports .NET 11.

From 11.0.0 on, a new major means a new .NET release, and nothing else. A fix that changes what a
call returns ships as a minor on every supported line, with a "Changed answers" section at the top of
that version's changelog entry, so a line never stops receiving the fixes that matter most. If this
intention changes, it is recorded here before anywhere else.

### If this project stops being maintained

This is a single-maintainer project, and that is the honest continuity risk. The signal would be
unambiguous: the repository archived, the packages marked deprecated on nuget.org, and a note here.
Published versions stay on nuget.org regardless (a package can be unlisted, not removed), the code
is MIT-licensed, and the release path needs nothing but this repository — forking is the intended
continuity mechanism, not a fallback.

Advisories are published as GitHub Security Advisories, which reach the GitHub Advisory Database
and from there `dotnet restore` (NuGetAudit): a consumer on an affected version sees a build
warning without subscribing to anything.

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

---
name: plain-docs
description: The voice for every page a consumer reads in CodoMetis.ValueRanges: the root README, the package READMEs and the guides in docs/. Plain, friendly, example-first and exact. Use when writing or rewriting any of them, including a new section, a new docs page, or a paragraph added beside a feature.
---

# Plain docs

The reader is a .NET developer deciding whether to try ValueRanges, or trying it right now. They
should understand each paragraph on the first read, and finish a section knowing what to type. A page
that is correct but hard to read loses them before the details matter.

This voice is friendly, not chatty. Every claim stays as exact as before. Plain words make the docs
readable; they never make them vaguer.

## Which pages

The root `README.md`, the package READMEs under `src/*/README.md`, and the consumer guides:
`docs/getting-started.md`, `ranges.md`, `value-sets.md`, `serialization.md`, `efcore.md`,
`migration.md` and `why.md`. The contributor pages (`architecture.md`, `testing.md`,
`discard-triage.md`) and `CHANGELOG.md` keep their own style.

## The voice

- **Write to "you".** "You call `Union` and get a `RangeSet` back" rather than "`Union` yields a
  `RangeSet`".
- **Say what the reader gets first, then how it works.** "Your `Contains` call runs in PostgreSQL as
  one `@>`" comes before "the member translator maps `Contains` to the containment operator".
- **One idea per sentence.** Split a sentence at a semicolon, a colon followed by a list, or a second
  "and". About 20 words is a good ceiling.
- **Active voice, a person or a thing doing something.** "The query throws and names the operator"
  rather than "an exception naming the operator".
- **Natural word order.** Not "What memory answers, the database answers too", not "Every shape pair
  the engine dispatches".
- **Don't personify the code.** A range doesn't "answer", a guard doesn't "refuse", a column doesn't
  "say". Write what happens: "`IsAdjacentTo` returns `false`", "the query throws".
- **State things positively when you can.** "Use `TryParse` when you only need to know whether the
  text is a valid literal" instead of a chain of "never", "not" and "nothing".
- **No hype and no filler.** Avoid "simply", "just", "easy", "seamless", "powerful", "blazing",
  exclamation marks and emoji. Friendly comes from clarity, not from adjectives.

## Plain words

Use the left column in prose. Keep the API's own names in code (`DateRange.Finite`,
`RangeSet<TRange, T>`, `StringSet`, `UseValueRangesNodaTime`), and introduce a term once where the
API forces it on the reader.

| Write | Instead of |
|---|---|
| the NodaTime package, the EF Core package | satellite, companion |
| the five kinds of range (`Finite`, `UnboundedStart`, …), or "shape" once you have introduced it | variant, as a bare concept |
| deduplicated and sorted, or "canonical (deduplicated, sorted, no nulls)" on first use | canonical, unexplained |
| reject, throw, fail | refuse, refusal |
| the element type (`DateOnly`, `long`…) | domain, when it means the element type |
| the range you pass, the range you call it on | operand, receiver |
| load, read back | materialize |
| runs in the database, runs as SQL; runs in memory | translates, server-side; client-side |
| the literal (`[2025-06-01,2025-06-08]`), the JSON text | the wire form |
| empty | degenerate |
| your code, your project | the consumer, the caller |
| a rule, a guarantee | a contract, an invariant |

Name an exception after the plain description, in parentheses: "reading `"empty"` into an
`Int32Range.Finite` property fails (`JsonException`)". The type helps someone catching it; the words
help everyone else.

## Page shape

1. **Open with one or two sentences** on what the page lets the reader do. Then show the smallest
   example that works.
2. **Headings are tasks or questions** in guides: "Store a range in PostgreSQL", "Match on the
   shape", "Combine two ranges". Keep the headings that code and other pages link to (see below), and
   the version headings in `migration.md`.
3. **Example first, explanation after.** Each section leads with code where there is code, then says
   what it does in a few short sentences.
4. **Edge cases go last**, under a short "Good to know" list at the end of the section, not in the
   middle of the explanation. Each one is a bullet with a bold lead phrase.
5. **Lists for three or more parallel items.** A bullet starts with a bold phrase that a skimmer can
   read alone, followed by one or two sentences.
6. **Link instead of repeating.** A guide explains and a package README is the short version for
   NuGet; the same detail lives in one place and the other links to it.

## What must not change

Rewriting the voice must not break what the tests guard, or what nothing guards. Run the conventions
and ASP.NET Core tests after every page:

```bash
dotnet test test/CodoMetis.ValueRanges.Conventions.Tests
```

```bash
dotnet test test/CodoMetis.ValueRanges.AspNetCore.Tests
```

- **Code blocks are not compiled, so nothing catches a mistake in one.** Leave the code as it is. A
  comment that states an output (`// true`, `// "[2025-06-01,2025-06-08]"`, a JSON body, SQL) must
  stay exact. You may reword a comment's prose part; if you touch the value, confirm it against the
  test that covers that behaviour, or run it.
- **One block is checked verbatim.** `docs/serialization.md` carries `ValueRangesSchemaTransformer`,
  and `OpenApiSchemaTests` fails if it drifts from
  `test/CodoMetis.ValueRanges.AspNetCore.Tests/ValueRangesSchemaTransformer.cs`. Edit the test copy
  first, then the doc.
- **The AOT phrase is checked.** A package README contains "Native AOT and trimming compatible"
  exactly when its project builds with `IsAotCompatible` (`PackagingConventionTests`). Keep the
  phrase word for word in the core and NodaTime READMEs, and keep it out of the EF Core ones.
- **Package READMEs are shown on NuGet.** Links in `src/*/README.md` are absolute GitHub URLs,
  because relative ones break there. Keep them absolute.
- **Anchors are not checked.** Renaming a heading changes its anchor, and nothing fails. Find every
  link to it with `git grep -n 'page.md#anchor'` and update them in the same change. Keep the
  headings that code and package READMEs point at — `#native-aot` is in an exception message
  (`GenericConverters.cs`), `#what-runs-where` is linked from NuGet. Check with
  `git grep -n -E '\.md#' -- src` before renaming one.
- **Facts stay exact.** If the old sentence had a condition, an exception, an operator or a version,
  the new one has it too, perhaps moved into "Good to know". When unsure what a sentence means, read
  the code or the test it describes before rewriting it. A sentence about PostgreSQL behaviour is
  backed by the integration tests; keep its meaning even when its wording changes.
- **Promises are quoted by the claim catalogue.** `test/oracles/claims.json` quotes, word for word,
  the sentence that makes each promise, under its heading (`source`). `OracleCatalogueTests` fails
  when a page no longer contains a quote or the heading is gone. When you reword or move one, point
  the claim's `quote` and `source` at the sentence that now makes the same promise. If no sentence
  makes it any more, the rewrite dropped a promise: put it back in the page, and never delete or
  weaken the claim. `dotnet run tools/oracles.cs status` lists headings that no claim quotes, which
  helps you spot a section that moved.
- **The Documentation table** in the root README links every consumer page. Keep its rows in step
  with the pages' new opening sentences.

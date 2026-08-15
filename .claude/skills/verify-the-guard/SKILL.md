---
name: verify-the-guard
description: Prove a guard actually fails without the thing it guards — revert the source fix, or seed the defect for a convention test where there is no source fix to revert. Use whenever adding a test alongside a bug fix or a new validation in this repo, whenever a check's subject is packaging or release wiring rather than code, and before reporting the work as done.
---

# Verify the guard

A regression test that passes both with and without the fix is not a regression test. It is a test
that happens to pass, and it will keep happening to pass while the bug comes back.

The discipline is one extra step: **after the test goes green, revert the source change and confirm
the test goes red.** Then restore and re-run.

This is cheap and it earns its keep. Two of the convention tests in this repository were vacuous
when first written, and neither looked wrong:

- The **mapping parity tests were iterating an empty list.** They asserted that every range type
  resolves through `IRelationalTypeMappingSource`, over a reflection query that filtered to
  concrete types. The range unions are `abstract record`s with their five variants nested inside
  them, so the query matched nothing. Four green tests, zero types examined.
- The **`CanonicalOrder` rule passed with the rule removed.** The probe strings were ordered
  identically by ordinal and culture comparison, so binary-searching an ordinal-sorted array with
  the default culture comparer still found every element. Compounding it, the wrapper element type
  the test closed the generic family over implemented `IComparable` *ordinally* — not what the
  generators emit — so it agreed with canonical order by accident.

Both were found by seeding the defect, not by review.

## The loop

1. Write the test. Run it — it should **fail**, for the reason you expect. Read the failure
   message; if it fails for a different reason than the bug, the test is testing the wrong thing.
2. Apply the fix. Run it — it should pass.
3. **Revert only the source change**, keeping the test. Run again — it must fail.
4. Restore the fix. Run the full suite and confirm the count is back where it started.

Step 1 is often skipped when the fix is already written. Step 3 recovers the same guarantee after
the fact, so do step 3 always, even when you did step 1.

## Reverting cleanly

```bash
SCRATCH=$(mktemp -d)
cp src/CodoMetis.ValueRanges/Sets/StringSet.cs "$SCRATCH/"
# remove just the new guard, then:
dotnet test test/CodoMetis.ValueRanges.Tests
cp "$SCRATCH/StringSet.cs" src/CodoMetis.ValueRanges/Sets/
```

Revert the **source**, never the test. Reverting via `git checkout --` on a file that also contains
unrelated work will lose it — copy aside instead.

**`git checkout -- .` does not undo a `git mv`.** The rename is staged, so the working tree is
restored *to the renamed state* and every later step in a batch runs against a missing file. That
happened while seeding the release-wiring tests: four seeds "failed" with `FileNotFoundException`
rather than the defect they were meant to demonstrate, which is a failure for the wrong reason and
proves nothing. Use `git reset --hard` when a seed touches file names, and never leave the working
tree partially restored.

## What counts as failing for the right reason

- **Right:** the asserted collection lacks the expected element; `Assert.ThrowsExactly` reports no
  exception; the asserted SQL fragment is absent; the convention test names the property it could
  not find.
- **Wrong:** a `NullReferenceException`, a `FileNotFoundException`, a compile error, or a failure
  in a *different* test. A compile error usually means the test depends on API introduced by the
  fix — restructure it to exercise behaviour that exists either way, or accept that it is a feature
  test rather than a regression test and say so.

## When the guard's subject is packaging or release wiring

There is no one-line source fix to revert. The counterfactual is to **seed the defect** — bump the
version without writing a changelog entry, delete a `PackageReadmeFile` property, point a package
back at the shared root README, grant the unattended CI job an `id-token` permission, add a
`PrivateAssets=all` reference without excluding it from the SBOM — and confirm the guard notices.
`scratchpad/seed*.sh` in a working session is the usual shape: apply, run the filtered test,
`git reset --hard`.

Two traps specific to this class:

**Text matching reads its own explanation.** A test asserting the verify job holds no `id-token`
permission matched the *comment* saying why it deliberately has none. Match structure — a YAML key,
an XML element — not a substring that also occurs in prose.

**A discovery-driven test that finds nothing passes everything.** Every assertion that loops over a
reflection query or a file glob needs a floor assertion on how many items it found. Both
discovery-driven classes here carry one, because the alternative already happened.

## Ways a guard passes vacuously

Worth checking before believing a green run:

- It iterated an empty list (see above). Assert the count.
- It asserted on something both the correct and broken paths produce — probes that ordinal and
  culture ordering agree on, an element type whose `IComparable` happens to match the canonical
  comparer, a value that is its own normalized form.
- It read a cached or previously packed artifact rather than the one just built.
- It greps for a phrase that no longer occurs, because the phrase was reworded.

## Tests that legitimately pass either way

Some tests are not regression guards and should not be forced through this loop:

- Tests pinning behaviour that was *already* correct, added for documentation.
- Tests guarding against a **future** over-correction — that a value the model currently accepts
  stays acceptable, protecting against a validation being made too strict later.

Both are worth having. Just do not count them as evidence that a fix works, and say which they are
when reporting.

## Reporting

State the counterfactual result explicitly, with the failure output. "Added a test" says nothing
about whether the bug is caught; "reverting the fix makes these three tests fail, with this output"
is the claim that matters.

When several fixes land together, revert them all at once and confirm the failure count matches the
number of guards — a fix whose test still passes stands out immediately.

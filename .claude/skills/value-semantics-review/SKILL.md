---
name: value-semantics-review
description: Review a change to CodoMetis.ValueRanges for values that look right, compare equal, round-trip without error, and mean something different from what was declared. Use when changing a range type, a value set family, canonical form, a comparer, a type mapping, a translator, a JSON converter, or the parsing and formatting code — and before cutting a release.
---

# Value-semantics review

The dangerous failure in this repo is not a crash. It is a value that looks right, compares equal to
itself, and means something different from what was declared — a boundary that is inclusive on one
side of the database wire and exclusive on the other, a set that does not contain the element it was
built from, a payload that reads back as `default`. Nothing throws, and the caller gets an answer
with a straight face.

Generic code review does not look for this. This skill is that lens.

## How to use this

Work through the sections that touch the change. Answer each question against the actual code, not
the docs, which may describe the intent rather than the behaviour. When a question has no obvious
answer, **write the test that answers it** — that is usually faster than reasoning, and it is what
found every confirmed defect here.

Report findings with the failure scenario spelled out concretely: the value a user would construct,
what the library answers, and what they would have expected.

## 1. Both sides of the wire

The same value exists in memory and in PostgreSQL, under two canonicalization rules that do not
agree. This is the seam where "applies cleanly, means something else" lives.

- For a **discrete** type (`int`, `long`, `DateOnly`, `LocalDate`, `YearMonth`), PostgreSQL
  canonicalizes to half-open `[lower, upper)` while the model canonicalizes to closed
  `[lower, upper]`. Does the change preserve the compensation — `upper(x) - 1`, and
  `UpperBoundInclusive()` as `NOT upper_inf AND NOT isempty`?
- Does an unbounded side stay distinguishable from a *finite bound that happens to be infinite*?
  Npgsql maps `DateTime.MaxValue` to `infinity`, and `upper_inf` must stay `false` for it.
- Does the value survive a full round trip through the database, not just through `ToString`?
  **The integration suite is the authority.** It caught the discrete `upper()` offset and the
  directional multirange adjacency rule; both looked correct in memory.

Anything that changes a translation without an integration test is an assertion about PostgreSQL
rather than a demonstration of it.

## 2. Canonical form on every construction path

Canonical form — deduplicated, sorted, null-free — is a storage contract, not a convenience. It is
load-bearing twice: the EF `ValueComparer` collapses to a cheap equality with no false diffs, and
SQL `=` on the stored array coincides with set equality.

- Does **every** path canonicalize: `From`, both `From` overloads, parsing, JSON, collection
  expressions, and materialization from the database? A path that skips it produces an instance
  that is unequal to its own equivalent.
- `FromTrusted` deliberately does not re-normalize. Is every new caller of it actually handing over
  canonical input?
- Does a read of a non-canonical row (written by another client) normalize without rewriting the
  row?

## 3. The probe must be comparable with what was stored

This is the rule that has produced the most defects, and it has two halves that fail the same way:
`Contains` returns `false` for an element the set holds.

- **If the type normalizes or validates elements in `From`, it must override
  `IValueSet<T>.NormalizeElement`** — and, on the EF side, pass the same function as the
  definition's `normalizeValue`. `Contains`/`Add`/`Remove` take a bare element, so without it the
  probe is compared un-normalized against normalized storage: a wrong answer client-side and a
  wrong bound parameter server-side. The NodaTime calendar sets are the live example.
- **If the type overrides `CanonicalComparer`, it must override `IValueSet<T>.CanonicalOrder` to
  return it.** Membership binary-searches the canonical array; searching with an order the array
  was not sorted by misses elements that are present.

Both are asserted behaviourally by `ValueSetContractTests`. When adding a set family, add probes
for its element type — including a value the type would rewrite, since a probe that is already its
own normalized form exercises nothing.

## 4. Ordering: ordinal versus the element's own comparison

String-backed families sort **ordinal**, never culture-sensitive and never the element's own
`IComparable`. Canonical form is a cross-writer contract; a culture sort makes two machines
disagree about the same set.

- Does a new comparer stay invariant? Anything reading `CultureInfo.CurrentCulture` is wrong here.
- For a wrapper element type, the generated `IComparable` typically delegates to culture-sensitive
  string comparison — which is exactly why `StringSet<TElement>` defines its own ordinal comparer
  over the invariant text form. Do not "simplify" it to `Comparer<TElement>.Default`.
- Test values must **distinguish the two orders**. `"Zebra"` before `"apple"` is ordinal; the
  reverse is culture. Probes both orders agree on prove nothing.

## 5. JSON is a second wire format

- Sets serialize as plain JSON arrays and delegate elements to System.Text.Json, which is right —
  it keeps converters registered on the options, the property and the type authoritative. It is
  also a trap: an element type the serializer does not know is written as a property dump and read
  back as `default`, silently, on both legs.
- **A family whose element type has no scalar converter must override
  `IValueSetFactory<TSet,T>.ElementJsonConverter`.** It is consulted last, so a registered
  converter still wins.
- Integer-backed wrappers must write a JSON **number**, not a string: a wrapper has to be
  indistinguishable on the wire from the primitive it wraps.
- Does the payload agree with what PostgreSQL stores? `[{"Value":"users.read"}]` disagreed with
  `{users.read}` for an entire release.
- Nullable properties, `object`-typed properties, and heterogeneous collections all reach the
  converter differently. All three threw before 6.1.0.

## 6. Registration and mapping parity

- New range types are wired exclusively through `RangeTypeRegistry.Register`, new set types through
  `SetTypeRegistry.Register`, satellites calling them from their options-builder extension. Never
  bypass the registry.
- **Never let `SetTypeRegistry` match by store-type name.** `text[]` belongs to the provider's
  native `string[]` mapping; claiming it hijacks every plain array property and everything
  scaffolding produces.
- Does a plain `string[]`/`List<Guid>` property still get its native mapping in the same model?
- `MappingParityTests` discovers types by reflection, so a new type is covered automatically — but
  check the discovery floor still matches reality.

## 7. Composition on a non-canonical result

`Union` translates to `array_cat`, which concatenates without deduplicating. Only order- and
multiplicity-insensitive operators may compose on it.

- Does a new translation compose on a `Union` result? `Count` over a union is deliberately refused
  rather than counting duplicates; equality over a union is wrong and has no translator seam, so it
  is documented rather than fixed.
- If the change adds an operation with a non-canonical SQL result, what is allowed to consume it?

## 8. Closed-world guarantees

- The range unions have private constructors so pattern matching stays exhaustive; the value set
  interfaces are closed by internal members. Does the change open either?
- `Count`/`IsEmpty` must stay instance properties — extension properties cannot appear in
  expression trees (CS9296) and would be untranslatable.
- Does a new engine handle all five shapes: `Finite`, `UnboundedStart`, `UnboundedEnd`,
  `EmptyRange`, `Infinity`?

## Finishing

Before reporting, confirm each finding by running it. A hypothesis about comparison or translation
behaviour is cheap to check — print the SQL with `ToQueryString()`, or the JSON, or the stored array
text — and the audit's most severe findings all looked speculative until the output was printed.
Discard unconfirmed suspicions silently rather than reporting them.

For anything you then fix, apply the `verify-the-guard` skill.

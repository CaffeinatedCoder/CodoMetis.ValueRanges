using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Canonical form excludes nulls, and every way into a set has to enforce that by refusing —
/// never by dropping.
/// </summary>
/// <remarks>
/// <para>
/// Silently discarding a null is the value-set shape of the fallback that produced five range bugs:
/// an input nobody wrote a case for, answered plausibly. It would give a set of size 2 for three
/// supplied elements, and the caller would have no way to tell that from a duplicate.
/// </para>
/// <para>
/// The other suites cannot cover this. <see cref="SetProbes"/> feeds sweeps that build valid sets,
/// so a null belongs nowhere in that table — it needs its own test rather than a probe entry.
/// </para>
/// <para>
/// Today exactly one family qualifies: every wrapper element is a <c>readonly record struct</c> and
/// every NodaTime element is a struct, so <c>string</c> in <see cref="StringSet"/> is the only
/// nullable element type in the 30. <c>StringSet</c>'s own null handling is already pinned by
/// hand in <c>SetCanonicalFormTests</c>; what this adds is that the rule is enforced by
/// <em>discovery</em>, so a family added later over a reference type is covered without anyone
/// remembering to come back here.
/// </para>
/// </remarks>
[TestClass]
public sealed class ValueSetNullContractTests
{
    [TestMethod]
    public void EveryNullableElementFamily_RefusesNullEverywhere()
    {
        var covered = new List<string>();

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            // A struct element cannot be null, so there is nothing to refuse.
            if (elementType.IsValueType || !SetProbes.HasProbes(elementType)) continue;

            Reflect.InvokeGeneric(typeof(ValueSetNullContractTests), nameof(AssertNullIsRefused),
                                  setType, elementType);

            covered.Add(setType.Name);
        }

        Assert.IsTrue(
            covered.Count >= 1,
            "No set family with a nullable element type was found, so this test asserted nothing. "
          + "Every element type is currently a struct except string; if that changed, or if the "
          + "discovery predicate stopped matching, this is what says so.");
    }

    private static void AssertNullIsRefused<TSet, TElement>()
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        // Only invoked for reference element types, so default is null.
        TElement missing = default!;
        var      present = SetProbes.For<TElement>()[0];
        var      set     = TSet.From(present);
        var      name    = typeof(TSet).Name;

        // Construction, both overloads: a null anywhere in the input invalidates the whole call.
        Assert.ThrowsExactly<ArgumentException>(
            () => TSet.From(new[] { present, missing }.AsEnumerable()),
            $"{name}.From(IEnumerable) accepted a null element. Canonical form excludes nulls, and "
          + "dropping one would report a smaller set than the caller supplied with nothing raised.");

        Assert.ThrowsExactly<ArgumentException>(
            () => TSet.From(present, missing),
            $"{name}.From(span) accepted a null element, though the IEnumerable overload refuses it. "
          + "The two construction paths must agree.");

        // The single-element operations, which take a bare element and so are the easiest to
        // forget: each reaches the canonical array directly.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => set.Add(missing), $"{name}.Add(null) did not refuse.");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => set.Remove(missing), $"{name}.Remove(null) did not refuse.");

        Assert.ThrowsExactly<ArgumentNullException>(
            () => set.Contains(missing),
            $"{name}.Contains(null) did not refuse. Answering false would be the plausible-looking "
          + "wrong answer: it is indistinguishable from a genuine miss.");

        // The two wire formats, where a null arrives as text rather than as a reference.
        Assert.ThrowsExactly<FormatException>(
            () => TSet.Parse("{NULL}".AsSpan(), null),
            $"{name}.Parse accepted an unquoted NULL. PostgreSQL writes a genuine array null that "
          + "way, so accepting it would let a null in from the database.");

        var options = new JsonSerializerOptions();
        options.AddRangeConverters();

        Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<TSet>("[null]", options),
            $"{name} accepted a null JSON element.");
    }
}

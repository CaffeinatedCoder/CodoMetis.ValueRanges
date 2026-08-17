using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// An exhaustive oracle for the value set families: every subset of a small probe universe is
/// built, every operation is checked against set theory, and every ordered pair of subsets is
/// swept — for every set type that exists, core and NodaTime, plain and wrapper arity.
/// </summary>
/// <remarks>
/// <para>
/// The ranges have PostgreSQL to disagree with. The sets have nothing: <c>Intersect</c>,
/// <c>Except</c> and <c>Add</c> are deliberately client-side only, because a PostgreSQL array has
/// no intersection, no difference and no sorted insert. That makes this the least-defended surface
/// in the repository and the one where an oracle is worth the most — there is no second
/// implementation anywhere to cross-check against.
/// </para>
/// <para>
/// Where the range oracle enumerates every <em>representable range</em>, this enumerates every
/// <em>subset</em> of the probe elements — all 2^n of them, so the sweep is exhaustive over the
/// whole value space of a set drawn from that universe, not a sample of it. The probes are
/// deliberately awkward: values a normalizing family rewrites, strings whose ordinal and cultural
/// orders disagree, decimals that lose scale through <c>double</c>, timestamps with sub-second
/// components that a default format truncates.
/// </para>
/// <para>
/// The model has one axiom, the counterpart of <c>Contains(T)</c> in the range oracle: that
/// <c>From(x)</c> on a single element normalizes it correctly and holds exactly it. Element
/// normalization is family-specific and internal — <c>TextKey</c> trims, the NodaTime calendar
/// types convert to ISO — so the model cannot derive it externally the way it can derive a range's
/// values from its bounds. It asks <c>From</c> once per probe instead, and everything after that
/// (deduplication, ordering, every construction path, all eleven operations, equality, hashing,
/// both round trips) is checked against <see cref="SortedSet{T}"/> ground truth. That axiom is
/// itself pinned by <see cref="ValueSetContractTests.EverySetType_FindsAnElementItWasBuiltFrom"/>.
/// </para>
/// <para>
/// Probes come from <see cref="SetProbes"/>, shared with the contract tests so the two suites
/// cannot drift apart on which families exist or what to feed them.
/// </para>
/// <para>
/// <b>What this cannot check.</b> The model reads the canonical <em>order</em> from
/// <c>TSet.CanonicalComparer</c>, so it verifies that every construction path and every operation
/// agrees with the declared order — not that the declared order is the specified one. Swapping
/// <c>StringComparer.Ordinal</c> for <c>StringComparer.InvariantCulture</c> moves the model with
/// the implementation and this sweep stays green. That claim needs an oracle outside the type, and
/// is pinned by <see cref="ValueSetContractTests.StringBackedFamilies_SortOrdinal"/>.
/// </para>
/// </remarks>
[TestClass]
public sealed class SmallModelSetOracleTests
{
    [TestMethod]
    public void EverySetType_EverySubset_MatchesSetTheory()
        => ForEverySetType(nameof(SweepSubsets));

    [TestMethod]
    public void EverySetType_EveryOrderedPairOfSubsets_MatchesSetTheory()
        => ForEverySetType(nameof(SweepPairs));

    [TestMethod]
    public void EverySetType_EverySubset_SurvivesEveryConstructionPath()
        => ForEverySetType(nameof(SweepConstruction));

    private static void ForEverySetType(string sweep)
    {
        var swept = 0;

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            if (!SetProbes.HasProbes(elementType)) continue; // reported by EverySetType_IsCoveredByAProbe

            typeof(SmallModelSetOracleTests)
               .GetMethod(sweep, BindingFlags.NonPublic | BindingFlags.Static)!
               .MakeGenericMethod(setType, elementType)
               .Invoke(null, null);

            swept++;
        }

        // The discovery-driven suite's worst failure is finding nothing and passing.
        Assert.IsTrue(swept >= 30,
                      $"{sweep} ran over {swept} set types, fewer than the 30 known to exist. "
                    + "A reflection predicate that stopped matching would retire this sweep silently.");
    }

    // ---------------------------------------------------------------- per-subset

    private static void SweepSubsets<TSet, TElement>()
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var universe = Universe<TSet, TElement>();
        var failures = new List<string>();

        foreach (int mask in Masks(universe.Probes.Length))
        {
            var model  = universe.Model(mask);
            var actual = universe.Build(mask);

            if (!actual.Values.SequenceEqual(model))
                failures.Add($"  {universe.Name(mask)}: Values = [{Join(actual.Values)}], "
                           + $"canonical form is [{Join(model)}]");

            // Count and IsEmpty are declared per concrete type rather than on IValueSet<T> — they
            // have to stay instance properties to appear in expression trees — so the sweep reaches
            // the real members reflectively instead of recomputing them from Values.
            int  count   = (int)  universe.CountProperty.GetValue(actual)!;
            bool isEmpty = (bool) universe.IsEmptyProperty.GetValue(actual)!;

            if (count != model.Count)
                failures.Add($"  {universe.Name(mask)}: Count = {count}, set theory says {model.Count}");

            if (isEmpty != (model.Count == 0))
                failures.Add($"  {universe.Name(mask)}: IsEmpty = {isEmpty}, set theory says {model.Count == 0}");

            // Membership is probed with the *un-normalized* element, which is the whole point:
            // a family that normalizes in From but not in NormalizeElement answers false here.
            for (int i = 0; i < universe.Probes.Length; i++)
            {
                bool expected = model.Contains(universe.Normalized[i]);
                if (actual.Contains(universe.Probes[i]) != expected)
                    failures.Add($"  {universe.Name(mask)}.Contains({universe.Probes[i]}) = {!expected}, "
                               + $"set theory says {expected}");

                var added = actual.Add(universe.Probes[i]);
                if (!added.Values.SequenceEqual(Plus(model, universe.Normalized[i], universe.Comparer)))
                    failures.Add($"  {universe.Name(mask)}.Add({universe.Probes[i]}) = [{Join(added.Values)}], "
                               + $"set theory says [{Join(Plus(model, universe.Normalized[i], universe.Comparer))}]");

                var removed = actual.Remove(universe.Probes[i]);
                if (!removed.Values.SequenceEqual(Minus(model, universe.Normalized[i], universe.Comparer)))
                    failures.Add($"  {universe.Name(mask)}.Remove({universe.Probes[i]}) = [{Join(removed.Values)}], "
                               + $"set theory says [{Join(Minus(model, universe.Normalized[i], universe.Comparer))}]");
            }
        }

        Report<TSet>("subsets", failures);
    }

    // ---------------------------------------------------------------- per-pair

    private static void SweepPairs<TSet, TElement>()
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var universe = Universe<TSet, TElement>();
        var failures = new List<string>();
        var comparer = universe.Comparer;

        foreach (int leftMask in Masks(universe.Probes.Length))
        {
            var leftModel = universe.Model(leftMask);
            var left      = universe.Build(leftMask);

            foreach (int rightMask in Masks(universe.Probes.Length))
            {
                var rightModel = universe.Model(rightMask);
                var right      = universe.Build(rightMask);

                var union        = Sorted(leftModel.Concat(rightModel), comparer);
                var intersection = Sorted(leftModel.Where(rightModel.Contains), comparer);
                var difference   = Sorted(leftModel.Where(value => !rightModel.Contains(value)), comparer);

                CheckSet("Union",     left.Union(right).Values,     union);
                CheckSet("Intersect", left.Intersect(right).Values, intersection);
                CheckSet("Except",    left.Except(right).Values,    difference);

                Check("Overlaps",           left.Overlaps(right),           intersection.Count > 0);
                Check("IsSubsetOf",         left.IsSubsetOf(right),         leftModel.IsSubsetOf(rightModel));
                Check("IsSupersetOf",       left.IsSupersetOf(right),       leftModel.IsSupersetOf(rightModel));
                Check("IsProperSubsetOf",   left.IsProperSubsetOf(right),   leftModel.IsProperSubsetOf(rightModel));
                Check("IsProperSupersetOf", left.IsProperSupersetOf(right), leftModel.IsProperSupersetOf(rightModel));

                bool equal = leftModel.SetEquals(rightModel);
                Check("Equals", left.Equals(right), equal);

                // Equal values must hash equally, or a set used as a dictionary key goes missing.
                if (equal && left.GetHashCode() != right.GetHashCode())
                    failures.Add($"  {universe.Name(leftMask)} and {universe.Name(rightMask)} are equal "
                               + "but hash differently");

                void Check(string operation, bool actual, bool expected)
                {
                    if (actual != expected)
                        failures.Add($"  {universe.Name(leftMask)} {operation} {universe.Name(rightMask)} "
                                   + $"= {actual}, set theory says {expected}");
                }

                void CheckSet(string operation, IEnumerable<TElement> actual, List<TElement> expected)
                {
                    if (!actual.SequenceEqual(expected))
                        failures.Add($"  {universe.Name(leftMask)} {operation} {universe.Name(rightMask)} "
                                   + $"= [{Join(actual)}], set theory says [{Join(expected)}]");
                }
            }
        }

        Report<TSet>("pairs", failures);
    }

    // ---------------------------------------------------------------- construction paths

    private static void SweepConstruction<TSet, TElement>()
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var universe = Universe<TSet, TElement>();
        var failures = new List<string>();
        var options  = new JsonSerializerOptions();
        options.AddRangeConverters();

        foreach (int mask in Masks(universe.Probes.Length))
        {
            var model    = universe.Model(mask);
            var expected = universe.Build(mask);
            var elements = universe.Elements(mask);

            // Canonical form is a property of the *value*, not of the order it arrived in, so
            // every path that can build this set must land on the identical array.
            Check("reversed input",   TSet.From([.. elements.Reverse()]));
            Check("duplicated input", TSet.From([.. elements, .. elements]));
            Check("IEnumerable",      TSet.From(elements.AsEnumerable()));

            // Repeated Add from empty is the incremental path, which never sees the whole input.
            var built = elements.Aggregate(TSet.Empty, (set, element) => set.Add(element));
            Check("repeated Add", built);

            Check("Parse(ToString())",
                  TSet.Parse(expected.ToString()!.AsSpan(), CultureInfo.InvariantCulture));

            Check("JSON round trip",
                  JsonSerializer.Deserialize<TSet>(JsonSerializer.Serialize(expected, options), options)!);

            void Check(string path, TSet actual)
            {
                if (!actual.Values.SequenceEqual(model))
                    failures.Add($"  {universe.Name(mask)} via {path} = [{Join(actual.Values)}], "
                               + $"canonical form is [{Join(model)}]");

                if (!actual.Equals(expected))
                    failures.Add($"  {universe.Name(mask)} via {path} does not equal the same set built by From");
            }
        }

        Report<TSet>("construction paths", failures);
    }

    // ---------------------------------------------------------------- the model

    /// <summary>
    /// The probe universe for one set type, with each probe's normalized form resolved once
    /// through the model's single axiom: <c>From(x)</c> on one element holds exactly that element,
    /// normalized. Everything else is derived from set theory over those normalized values.
    /// </summary>
    private sealed record ProbeUniverse<TSet, TElement>(
        TElement[]          Probes,
        TElement[]          Normalized,
        IComparer<TElement> Comparer,
        PropertyInfo        CountProperty,
        PropertyInfo        IsEmptyProperty
    )
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        internal SortedSet<TElement> Model(int mask) =>
            new(Indices(mask).Select(index => Normalized[index]), Comparer);

        internal TElement[] Elements(int mask) => [.. Indices(mask).Select(index => Probes[index])];

        internal TSet Build(int mask) => TSet.From(Elements(mask));

        internal string Name(int mask) =>
            mask == 0 ? "{}" : $"{{{string.Join(", ", Indices(mask).Select(index => Probes[index]))}}}";

        private IEnumerable<int> Indices(int mask) =>
            Enumerable.Range(0, Probes.Length).Where(index => (mask & (1 << index)) != 0);
    }

    private static ProbeUniverse<TSet, TElement> Universe<TSet, TElement>()
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var probes = SetProbes.For<TElement>();

        // The axiom. A single-element From is the simplest construction path there is, and it is
        // what ValueSetContractTests already pins; asking it for each probe is how the model
        // learns a normalization it cannot compute itself.
        var normalized = probes.Select(probe => TSet.From(probe).Values.Single()).ToArray();

        return new ProbeUniverse<TSet, TElement>(
            probes,
            normalized,
            TSet.CanonicalComparer,
            Required("Count"),
            Required("IsEmpty"));

        static PropertyInfo Required(string name) =>
            typeof(TSet).GetProperty(name)
         ?? throw new InvalidOperationException(
                $"{typeof(TSet).Name} has no public {name} property. Value sets must keep Count and "
              + "IsEmpty as instance properties — extension properties cannot appear in expression "
              + "trees (CS9296) and would be untranslatable.");
    }

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<int> Masks(int size) => Enumerable.Range(0, 1 << size);

    private static List<TElement> Sorted<TElement>(IEnumerable<TElement> values, IComparer<TElement> comparer) =>
        [.. new SortedSet<TElement>(values, comparer)];

    private static List<TElement> Plus<TElement>(SortedSet<TElement> model, TElement value, IComparer<TElement> comparer) =>
        Sorted(model.Append(value), comparer);

    private static List<TElement> Minus<TElement>(SortedSet<TElement> model, TElement value, IComparer<TElement> comparer) =>
        Sorted(model.Where(existing => comparer.Compare(existing, value) != 0), comparer);

    private static string Join<TElement>(IEnumerable<TElement> values) => string.Join(", ", values);

    private static void Report<TSet>(string sweep, List<string> failures) =>
        Assert.AreEqual(0, failures.Count,
                        $"{typeof(TSet).Name} disagrees with set theory over {sweep} "
                      + $"({failures.Count} failures):" + Environment.NewLine
                      + string.Join(Environment.NewLine, failures.Take(15))
                      + (failures.Count > 15 ? $"{Environment.NewLine}  … and {failures.Count - 15} more" : ""));
}

using System.Diagnostics;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using static CodoMetis.ValueRanges.Conventions.Tests.RangeSpecs;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every range type in the shipping assemblies answers the twelve binary operations as set theory
/// does over a small universe embedded into its element type — the small-model oracle, generalized
/// from <c>Int32Range</c> and <c>DecimalRange</c> to every discovered type.
/// </summary>
/// <remarks>
/// <para>
/// The engines are generic, so a defect in them shows up on the two reference types already
/// (<c>SmallModelOracleTests</c>). What is <em>not</em> generic is everything a type supplies
/// itself: the discrete step (<c>NextValueAfter</c> over days, months, <c>long</c>), the
/// canonicalizing factories, the comparison of the element type (<c>DateTimeOffset</c> compares
/// instants, NodaTime types carry a calendar), and the half-open default of a continuous type.
/// Before this sweep, nine of the eleven range types had only worked examples for that.
/// </para>
/// <para>
/// The embedding maps grid index <c>k</c> to an element in order: a day, a month, a half hour. A
/// discrete type gets a grid whose consecutive points are consecutive values of the domain, so the
/// model's canonical form is the type's; a continuous type gets the half-step grid of the reference
/// oracle, where an inclusive bound sits on an even index and an exclusive one on the odd index
/// beside it. Which grid a type takes is read from <c>IsDiscrete</c>, which
/// <c>RangeFactoryContractTests</c> holds to the truth. A type whose element has no embedding fails
/// the discovery test, so a new range type cannot ship outside the sweep.
/// </para>
/// <para>
/// Expected values come from the specification alone, never from the range; results are read back
/// through <c>Contains(T)</c> at every grid point, which is the one axiom the reference oracle pins
/// per domain and the integration suite checks against PostgreSQL's element <c>@&gt;</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class DomainParityOracleTests
{
    private const string OracleName = nameof(DomainParityOracleTests);

    private sealed record Case<TRange>(string Label, TRange Range, HashSet<int> Model) where TRange : notnull;

    [TestMethod]
    public void Discovery_FindsEveryRangeType_AndEveryElementHasAnEmbedding()
    {
        var types = RangeProbes.AllRangeTypes().ToList();

        Assert.IsTrue(types.Count >= 11, $"Discovered {types.Count} range types, fewer than the 11 known to exist.");

        var unembedded = types.Where(pair => !Embeddings.ContainsKey(pair.Element)).Select(pair => pair.Range.Name).ToList();
        Assert.AreEqual(0, unembedded.Count,
                        "A range type's element has no embedding, so the sweep cannot reach it — add one: " + string.Join(", ", unembedded));
    }

    [TestMethod]
    public void EveryRangeType_EveryOrderedPair_MatchesSetTheory()
    {
        var failures = new List<string>();
        var swept    = 0;

        foreach (var (range, element) in RangeProbes.AllRangeTypes())
        {
            if (!Embeddings.ContainsKey(element)) continue; // reported by the discovery test
            var result = Reflect.InvokeGeneric<(int Checks, List<string> Failures)>(typeof(DomainParityOracleTests), nameof(Sweep), range, element);
            Assert.IsTrue(result.Checks > 100_000, $"{range.Name}: expected well over 100,000 checks, made {result.Checks}.");
            failures.AddRange(result.Failures);
            swept++;
        }

        Assert.IsTrue(swept >= 11, $"Swept {swept} range types.");
        Candidates.Verdict(OracleName, failures, "Range types disagreeing with set theory over their embedded universe", shown: 30);
    }

    private static (int Checks, List<string> Failures) Sweep<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var valueOf  = (Func<int, T>)Embeddings[typeof(T)];
        var discrete = TRange.IsDiscrete;

        // The discrete grid: one index per value, bounds inside the universe. The continuous grid:
        // two indices per value, bounds on even indices, so [1,2) and (2,3] leave a point between.
        var gridMax = discrete ? 7 : 14;
        int[] bounds = discrete ? [1, 2, 3, 4, 5, 6] : [2, 4, 6, 8, 10, 12];

        var cases = EnumerateSpecs(bounds)
                   .Select(spec => new Case<TRange>($"{typeof(TRange).Name} {Describe<TRange, T>(spec, valueOf)}", Build<TRange, T>(spec, valueOf), ModelOf(spec, gridMax)))
                   .ToList();

        var failures = new List<string>();
        int checks   = 0;

        // The axiom: the specification and Contains(T) agree on every grid point.
        foreach (var probe in cases)
        {
            checks++;
            HashSet<int> actual = [.. Enumerable.Range(0, gridMax + 1).Where(k => probe.Range.Contains(valueOf(k)))];
            if (!actual.SetEquals(probe.Model))
                failures.Add($"  {probe.Label}: spec says {{{Join(probe.Model)}}}, Contains(T) says {{{Join(actual)}}}");
        }

        foreach (var a in cases)
        foreach (var b in cases)
        {
            HashSet<int> union        = [.. a.Model, .. b.Model];
            HashSet<int> intersection = [.. a.Model.Where(b.Model.Contains)];
            HashSet<int> difference   = [.. a.Model.Where(k => !b.Model.Contains(k))];
            bool         bothNonEmpty = a.Model.Count > 0 && b.Model.Count > 0;

            Check("Contains",             a.Range.Contains(b.Range),             b.Model.IsSubsetOf(a.Model));
            Check("IsContainedBy",        a.Range.IsContainedBy(b.Range),        a.Model.IsSubsetOf(b.Model));
            Check("Overlaps",             a.Range.Overlaps(b.Range),             intersection.Count > 0);
            Check("IsAdjacentTo",         a.Range.IsAdjacentTo(b.Range),         bothNonEmpty && intersection.Count == 0 && IsContiguous(union));
            Check("IsStrictlyLeftOf",     a.Range.IsStrictlyLeftOf(b.Range),     bothNonEmpty && a.Model.Max() < b.Model.Min());
            Check("IsStrictlyRightOf",    a.Range.IsStrictlyRightOf(b.Range),    bothNonEmpty && a.Model.Min() > b.Model.Max());
            Check("DoesNotExtendRightOf", a.Range.DoesNotExtendRightOf(b.Range), bothNonEmpty && a.Model.Max() <= b.Model.Max());
            Check("DoesNotExtendLeftOf",  a.Range.DoesNotExtendLeftOf(b.Range),  bothNonEmpty && a.Model.Min() >= b.Model.Min());

            CheckSet("Intersect", ModelOfRange(a.Range.Intersect(b.Range)), intersection);
            CheckSet("Merge",     ModelOfRange(a.Range.Merge(b.Range)),     Hull(union));
            CheckSet("Union",     ModelOfSet(a.Range.Union(b.Range)),       union);
            CheckSet("Except",    ModelOfSet(a.Range.Except(b.Range)),      difference);

            var liftedA = RangeSet<TRange, T>.From([a.Range]);
            var liftedB = RangeSet<TRange, T>.From([b.Range]);

            Check("Set.Contains(range)",   liftedA.Contains(b.Range), b.Model.IsSubsetOf(a.Model));
            Check("Set.Contains(set)",     liftedA.Contains(liftedB), b.Model.IsSubsetOf(a.Model));
            Check("Set.Overlaps(range)",   liftedA.Overlaps(b.Range), intersection.Count > 0);
            CheckSet("Set.Except(range)",  ModelOfSet(liftedA.Except(b.Range)),    difference);
            CheckSet("Set.Except(set)",    ModelOfSet(liftedA.Except(liftedB)),    difference);
            CheckSet("Set.Union(set)",     ModelOfSet(liftedA.Union(liftedB)),     union);
            CheckSet("Set.Intersect(set)", ModelOfSet(liftedA.Intersect(liftedB)), intersection);

            void Check(string operation, bool actual, bool expected)
            {
                checks++;
                if (actual != expected)
                    failures.Add($"  {a.Label} {operation} {b.Label} → {actual}, set theory says {expected}");
            }

            void CheckSet(string operation, HashSet<int> actual, HashSet<int> expected)
            {
                checks++;
                if (!actual.SetEquals(expected))
                    failures.Add($"  {a.Label} {operation} {b.Label} → {{{Join(actual)}}}, set theory says {{{Join(expected)}}}");
            }
        }

        return (checks, failures);

        HashSet<int> ModelOfRange(TRange range) => [.. Enumerable.Range(0, gridMax + 1).Where(k => range.Contains(valueOf(k)))];
        HashSet<int> ModelOfSet(RangeSet<TRange, T> set) => [.. Enumerable.Range(0, gridMax + 1).Where(k => set.Contains(valueOf(k)))];
    }

    /// <summary>The values a specification holds, from the specification alone.</summary>
    private static HashSet<int> ModelOf(Spec spec, int gridMax)
    {
        var grid = Enumerable.Range(0, gridMax + 1);

        return spec.Shape switch
               {
                   Shape.Empty          => [],
                   Shape.Infinity       => [.. grid],
                   Shape.Finite         => [.. grid.Where(k => AtOrAfter(k, spec.Start, spec.StartInclusive) && AtOrBefore(k, spec.End, spec.EndInclusive))],
                   Shape.UnboundedStart => [.. grid.Where(k => AtOrBefore(k, spec.End, spec.EndInclusive))],
                   Shape.UnboundedEnd   => [.. grid.Where(k => AtOrAfter(k, spec.Start, spec.StartInclusive))],
                   _                    => throw new UnreachableException()
               };

        static bool AtOrAfter(int k, int bound, bool inclusive)  => k > bound || (k == bound && inclusive);
        static bool AtOrBefore(int k, int bound, bool inclusive) => k < bound || (k == bound && inclusive);
    }

    private static bool IsContiguous(HashSet<int> values) => values.Count == 0 || values.Max() - values.Min() + 1 == values.Count;

    private static HashSet<int> Hull(HashSet<int> values) => values.Count == 0 ? [] : [.. Enumerable.Range(values.Min(), values.Max() - values.Min() + 1)];

    private static string Join(HashSet<int> values) => string.Join(",", values.Order());
}

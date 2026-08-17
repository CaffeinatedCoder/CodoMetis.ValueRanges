using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The multirange half of the small-model oracle: every subset of a tiny universe is built as a
/// <see cref="RangeSet{TRange,T}"/>, and every ordered pair is checked against set theory — for
/// both the values the result holds and the elements it holds them in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SmallModelOracleTests"/> only ever lifts a <em>single</em> range into a set, so the
/// multi-element algorithms were outside every oracle in the repository: the greedy merge, the
/// sorted merge behind <c>Union</c>, the two-pointer merge-join behind <c>Except</c>, and the
/// single-pass gap walk behind <c>Complement</c>. Those are the most intricate functions here —
/// each carries a hand-written correctness argument in its doc comment — and they were covered by
/// worked examples only. This sweep is what actually exercises them.
/// </para>
/// <para>
/// Over a universe of eight grid points, <em>every</em> subset is a representable multirange, so
/// enumerating all 256 of them is exhaustive over the whole multirange value space rather than a
/// sample of it. The decomposition into maximal runs of consecutive grid points is also the unique
/// canonical form, which is what lets the same expectation check the representation.
/// </para>
/// <para>
/// The outermost grid points double as the unbounded sentinels, the same convention the
/// single-range oracle uses: a run that reaches point 0 is built unbounded below, one that reaches
/// the last point is built unbounded above, and the run covering everything is
/// <c>Infinity</c>. Unbounded elements and the infinite set therefore appear in the enumeration
/// without being special-cased.
/// </para>
/// <para>
/// <b>Discrete domains only, and the reason is not incidental.</b> A run of consecutive grid points
/// is the canonical decomposition exactly when consecutive points are contiguous — which is true of
/// the integers and the days, and false of the reals. Over a half-step decimal grid the subset
/// <c>{0, 0.5}</c> is ambiguous: it is both the interval <c>[0, 0.5]</c>, which also holds 0.25, and
/// the two singletons <c>{0} ∪ {0.5}</c>, which do not, and the model cannot tell them apart at any
/// grid resolution. A continuous sweep built this way reported ~37,000 disagreements on its first
/// run, every one of them the model being wrong rather than the library — <c>(-∞,0] ∪ [0.5,0.5]</c>
/// really does have two elements. The merge-join algorithms themselves are generic over the range
/// type, so the discrete sweep does exercise all of them; what it cannot reach is continuous
/// adjacency deciding a merge.
/// </para>
/// <para>
/// That half is swept against PostgreSQL instead, by <c>ContinuousMultirangeParityTests</c> in the
/// integration suite. A point-set model is the wrong oracle for the reals; the database is a
/// working one, because its multirange constructor merges adjacent ranges by comparing bounds
/// rather than by enumerating values, which is the same rule stated the same way. Between the two,
/// the multirange surface is swept end to end — this file over the discrete domains with no
/// database, that one over the continuous domain with one. <see cref="SmallModelOracleTests"/>
/// still reaches the continuous two-element cases through <c>Set.Union</c> and <c>Set.Except</c>,
/// and <c>RangeSetTests</c> keeps its worked examples.
/// </para>
/// <para>
/// <b>Why the element-level check matters.</b> The single-range oracle reads results back through
/// <c>Contains(value)</c> alone, which is blind to representation: a set holding
/// <c>{[1,2],[3,4]}</c> where canonical form demands <c>{[1,4]}</c> contains exactly the right
/// values and passes every membership check, while <c>Count</c>, <c>Equals</c>,
/// <c>GetHashCode</c>, <c>ToString</c>, the indexer and the EF multirange literal all disagree.
/// The invariant CLAUDE.md calls load-bearing on every code path was not asserted by any sweep.
/// Here each result is compared element by element against the canonical decomposition, so an
/// unmerged neighbour, a stray empty, or elements out of order fails.
/// </para>
/// </remarks>
[TestClass]
public class SmallModelMultirangeOracleTests
{
    // Eight grid points: 256 subsets, 65,536 ordered pairs per domain.
    private const int GridMax = 7;

    [TestMethod]
    public void Int32Multirange_EverySubset_IsCanonical()
        => SweepSubsets<Int32Range, int>("Int32Range", index => index);

    [TestMethod]
    public void Int32Multirange_EveryOrderedPair_MatchesSetTheory()
        => SweepPairs<Int32Range, int>("Int32Range", index => index);

    [TestMethod]
    public void DateMultirange_EverySubset_IsCanonical()
        => SweepSubsets<DateRange, DateOnly>("DateRange", Day);

    [TestMethod]
    public void DateMultirange_EveryOrderedPair_MatchesSetTheory()
        => SweepPairs<DateRange, DateOnly>("DateRange", Day);

    private static DateOnly Day(int index) => new DateOnly(2024, 6, 15).AddDays(index);

    // ---------------------------------------------------------------- per-subset

    private static void SweepSubsets<TRange, T>(string domain, Func<int, T> valueOf)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var failures = new List<string>();
        var universe = Full();

        foreach (var model in AllSubsets())
        {
            var set = Build<TRange, T>(model, valueOf);

            Check(failures, domain, "From", model, set, valueOf);

            // From is idempotent: feeding a normalized set's own elements back in must be a no-op.
            // Anything else means normalization is not a fixed point and the invariant depends on
            // how the set was reached.
            Check(failures, domain, "From(its own elements)", model,
                  RangeSet<TRange, T>.From([.. set]), valueOf);

            // Building from maximal runs hands From input that is already canonical and asks it to
            // do nothing, so on its own it never reaches the normalization path at all. Shattering
            // each run into one range per value and reversing the order is what actually exercises
            // the sort and the greedy merge's adjacency branch — the arm that collapses
            // discretely adjacent neighbours, and the one a seeded defect walked straight through
            // while every other check here stayed green.
            Check(failures, domain, "From(shattered, reversed)", model,
                  RangeSet<TRange, T>.From([.. Shatter<TRange, T>(model, valueOf).Reverse()]), valueOf);

            var complement = set.Complement();
            Check(failures, domain, "Complement", Difference(universe, model), complement, valueOf);
            Check(failures, domain, "Complement twice", model, complement.Complement(), valueOf);
        }

        Assert.AreEqual(0, failures.Count, Message(domain, "subsets", failures));
    }

    // ---------------------------------------------------------------- per-pair

    private static void SweepPairs<TRange, T>(string domain, Func<int, T> valueOf)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var failures = new List<string>();
        var subsets  = AllSubsets().Select(model => (Model: model, Set: Build<TRange, T>(model, valueOf))).ToList();
        int checks   = 0;

        foreach (var (leftModel, left) in subsets)
        {
            foreach (var (rightModel, right) in subsets)
            {
                var union        = Union(leftModel, rightModel);
                var intersection = Intersection(leftModel, rightModel);
                var difference   = Difference(leftModel, rightModel);
                bool bothNonEmpty = leftModel.Count > 0 && rightModel.Count > 0;

                Check(failures, domain, $"{Name(leftModel)} Union {Name(rightModel)}",
                      union, left.Union(right), valueOf);
                Check(failures, domain, $"{Name(leftModel)} Intersect {Name(rightModel)}",
                      intersection, left.Intersect(right), valueOf);
                Check(failures, domain, $"{Name(leftModel)} Except {Name(rightModel)}",
                      difference, left.Except(right), valueOf);

                Bool("Contains",          left.Contains(right),          rightModel.IsSubsetOf(leftModel));
                Bool("Overlaps",          left.Overlaps(right),          intersection.Count > 0);
                Bool("Equals",            left.Equals(right),            leftModel.SetEquals(rightModel));
                Bool("IsStrictlyLeftOf",  left.IsStrictlyLeftOf(right),  bothNonEmpty && leftModel.Max() < rightModel.Min());
                Bool("IsStrictlyRightOf", left.IsStrictlyRightOf(right), bothNonEmpty && leftModel.Min() > rightModel.Max());

                if (leftModel.SetEquals(rightModel) && left.GetHashCode() != right.GetHashCode())
                    failures.Add($"  {Name(leftModel)} and {Name(rightModel)} are equal but hash differently");

                void Bool(string operation, bool actual, bool expected)
                {
                    checks++;
                    if (actual != expected)
                        failures.Add($"  {Name(leftModel)} {operation} {Name(rightModel)} → {actual}, "
                                   + $"set theory says {expected}");
                }
            }
        }

        Assert.IsTrue(checks > 100_000,
                      $"{domain}: expected well over 100,000 predicate checks, made {checks}.");
        Assert.AreEqual(0, failures.Count, Message(domain, "ordered pairs", failures));
    }

    // ---------------------------------------------------------------- the model

    /// <summary>
    /// Checks a result for the values it holds <em>and</em> the elements it holds them in. The
    /// decomposition into maximal runs is the unique canonical form, so one comparison covers
    /// membership, element count, element order, merging of neighbours, and the absence of empties.
    /// </summary>
    private static void Check<TRange, T>(
        List<string>  failures,
        string        domain,
        string        operation,
        HashSet<int>  expected,
        RangeSet<TRange, T> actual,
        Func<int, T>  valueOf
    )
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var actualModel = ModelOfSet(actual, valueOf);
        if (!actualModel.SetEquals(expected))
        {
            failures.Add($"  {operation} → holds {{{Join(actualModel)}}}, set theory says {{{Join(expected)}}}");
            return;
        }

        var runs = Runs(expected);
        if (actual.Count != runs.Count)
        {
            failures.Add($"  {operation} → {actual.Count} elements ({actual}), canonical form has "
                       + $"{runs.Count} ({string.Join(",", runs.Select(run => $"[{run.Start}..{run.End}]"))}). "
                       + "Same values, wrong representation: a neighbour was left unmerged, or an "
                       + "empty was kept.");
            return;
        }

        for (int index = 0; index < runs.Count; index++)
        {
            var elementModel = ModelOfRange(actual[index], valueOf);
            var runModel     = new HashSet<int>(Enumerable.Range(runs[index].Start,
                                                                runs[index].End - runs[index].Start + 1));

            if (!elementModel.SetEquals(runModel))
                failures.Add($"  {operation} → element {index} holds {{{Join(elementModel)}}}, "
                           + $"canonical form puts {{{Join(runModel)}}} there. Elements are out of "
                           + "order, or split where canonical form merges.");
        }
    }

    private static RangeSet<TRange, T> Build<TRange, T>(HashSet<int> model, Func<int, T> valueOf)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => RangeSet<TRange, T>.From(
            [.. Runs(model).Select(run => run switch
                {
                    // Grid 0 and GridMax are the values no bound names in the single-range oracle;
                    // here a run that reaches one is what an unbounded side looks like.
                    (0, GridMax) => TRange.Infinite,
                    (0, var end) => TRange.CreateUnboundedStart(valueOf(end), true),
                    (var start, GridMax) => TRange.CreateUnboundedEnd(valueOf(start), true),
                    var (start, end) => TRange.CreateFinite(valueOf(start), valueOf(end), true, true)
                })]);

    /// <summary>
    /// The same values as <see cref="Build{TRange,T}"/> but deliberately non-canonical: one range
    /// per grid point, so every neighbour within a run is adjacent and must be merged away. The
    /// first and last elements keep their run's unboundedness, so the collapse of an unbounded
    /// element into its neighbour — and of a full run into <c>Infinity</c> — is exercised too.
    /// </summary>
    private static IEnumerable<TRange> Shatter<TRange, T>(HashSet<int> model, Func<int, T> valueOf)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => Runs(model).SelectMany(run =>
               Enumerable.Range(run.Start, run.End - run.Start + 1)
                         .Select(index => index == run.Start && run.Start == 0
                                              ? TRange.CreateUnboundedStart(valueOf(index), true)
                                              : index == run.End && run.End == GridMax
                                                  ? TRange.CreateUnboundedEnd(valueOf(index), true)
                                                  : TRange.CreateFinite(valueOf(index), valueOf(index), true, true)));

    /// <summary>Maximal runs of consecutive grid points — the unique canonical decomposition.</summary>
    private static List<(int Start, int End)> Runs(HashSet<int> model)
    {
        var runs = new List<(int Start, int End)>();

        for (int index = 0; index <= GridMax; index++)
        {
            if (!model.Contains(index)) continue;

            int start = index;
            while (index + 1 <= GridMax && model.Contains(index + 1)) index++;
            runs.Add((start, index));
        }

        return runs;
    }

    private static HashSet<int> ModelOfSet<TRange, T>(RangeSet<TRange, T> set, Func<int, T> valueOf)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => [.. Enumerable.Range(0, GridMax + 1).Where(index => set.Contains(valueOf(index)))];

    private static HashSet<int> ModelOfRange<TRange, T>(TRange range, Func<int, T> valueOf)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => [.. Enumerable.Range(0, GridMax + 1).Where(index => range.Contains(valueOf(index)))];

    // ---------------------------------------------------------------- helpers

    private static IEnumerable<HashSet<int>> AllSubsets() =>
        Enumerable.Range(0, 1 << (GridMax + 1))
                  .Select(mask => new HashSet<int>(
                              Enumerable.Range(0, GridMax + 1)
                                        .Where(index => (mask & (1 << index)) != 0)));

    private static HashSet<int> Full() => [.. Enumerable.Range(0, GridMax + 1)];

    private static HashSet<int> Union(HashSet<int> left, HashSet<int> right) => [.. left, .. right];

    private static HashSet<int> Intersection(HashSet<int> left, HashSet<int> right) =>
        [.. left.Where(right.Contains)];

    private static HashSet<int> Difference(HashSet<int> left, HashSet<int> right) =>
        [.. left.Where(value => !right.Contains(value))];

    private static string Name(HashSet<int> model) =>
        model.Count == 0 ? "{}" : $"{{{Join(model)}}}";

    private static string Join(HashSet<int> values) => string.Join(",", values.Order());

    private static string Message(string domain, string sweep, List<string> failures) =>
        $"{domain}: {failures.Count} disagreements with set theory over {sweep}:"
      + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(25))
      + (failures.Count > 25 ? $"{Environment.NewLine}  … and {failures.Count - 25} more" : "");
}

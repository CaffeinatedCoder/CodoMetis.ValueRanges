using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using static CodoMetis.ValueRanges.Conventions.Tests.RangeSpecs;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// A range and the one-element <see cref="RangeSet{TRange,T}"/> holding it answer every question alike:
/// over every ordered pair of the shape matrix, a set operation on the two one-element sets (and on the
/// set with the bare range) equals the set built from the range operation's result, every predicate
/// gives the same answer, and every unary accessor reads the same bound — for every discovered range type.
/// </summary>
/// <remarks>
/// <para>
/// The range engines and the multirange code are two implementations of the same algebra, and the
/// receiver-shaped dispatch bug has crossed between them: <c>IsAdjacentTo</c> (6.2.1), then
/// <c>IsStrictlyLeftOf</c> and <c>Except</c> (7.0.0), then <c>RangeSet.Except(TRange)</c> (8.0.0). The
/// set-theory oracles find such a bug where they look — at grid points inside a small universe. This
/// sweep needs no model at all, so it also looks where no model reaches: bounds at the domain's minimum
/// and maximum, results compared structurally (bound values, inclusivity, shape) rather than through
/// <c>Contains</c> at a few points, and the operator spellings (<c>|</c>, <c>&amp;</c>, <c>-</c>) beside
/// the named methods.
/// </para>
/// <para>
/// The matrix is <see cref="RangeSpecs.EnumerateSpecs"/> over four grid bounds plus the two domain
/// extremes. A specification the type cannot construct (a discrete bound past the domain's last step)
/// is skipped — <see cref="DiscreteDomainBoundaryTests"/> holds construction there. One
/// <c>[DynamicData]</c> row per range type; a failure line starts with the type's name.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeSetLiftParityTests
{
    public sealed record LiftRow(string Label, Func<List<string>, int> Sweep)
    {
        public override string ToString() => Label;
    }

    public static IEnumerable<object[]> Rows =>
        RangeProbes.AllRangeTypes().Select(pair => new object[]
        {
            new LiftRow(pair.Range.Name, failures => Reflect.InvokeGeneric<int>(
                typeof(RangeSetLiftParityTests), nameof(Sweep), [pair.Range, pair.Element], failures))
        });

    [TestMethod]
    public void Discovery_FindsEveryRangeType()
    {
        const int knownRangeTypes = 11;
        var rows = Rows.Count();
        Assert.IsTrue(rows >= knownRangeTypes, $"Found {rows} range types, fewer than {knownRangeTypes}.");

        var missing = RangeProbes.AllRangeTypes().Where(pair => !Embeddings.ContainsKey(pair.Element) || !Extremes.ContainsKey(pair.Element))
                                 .Select(pair => pair.Range.Name).ToList();
        Assert.AreEqual(0, missing.Count, "A range type's element has no embedding or no extremes in RangeSpecs: " + string.Join(", ", missing));
    }

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void Range_AndItsOneElementRangeSet_AnswerAlike(LiftRow row)
    {
        var failures = new List<string>();
        var checks   = row.Sweep(failures);

        Assert.IsTrue(checks >= 100_000, $"{row}: only {checks} checks ran.");

        Candidates.Verdict(nameof(RangeSetLiftParityTests), failures,
            $"{row}: of {checks} checks, the one-element RangeSet disagrees with its range", shown: 30, rowPrefix: row.Label + " ");
    }

    private static int Sweep<TRange, T>(List<string> failures)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var label   = typeof(TRange).Name;
        var valueOf = ValueOf<T>();
        var checks  = 0;

        // Four grid bounds — enough for disjoint, adjacent, overlapping and nested pairs — and the
        // domain's extremes. A continuous type takes even positions, so an exclusive bound leaves a gap.
        int[] bounds = TRange.IsDiscrete ? [DomainMin, 1, 2, 3, 4, DomainMax] : [DomainMin, 2, 4, 6, 8, DomainMax];

        var cases = new List<(string Name, TRange Range, RangeSet<TRange, T> Lifted)>();
        foreach (var spec in EnumerateSpecs(bounds))
        {
            TRange range;
            try
            {
                range = Build<TRange, T>(spec, valueOf);
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidOperationException)
            {
                continue;
            }

            cases.Add((Describe<TRange, T>(spec, valueOf), range, RangeSet<TRange, T>.From([range])));
        }

        // The points Contains(T) is asked about: every grid position, the half-steps between, the extremes.
        T[] points = [.. Enumerable.Range(0, 11).Select(valueOf), valueOf(DomainMin), valueOf(DomainMax)];

        foreach (var (name, a, lifted) in cases)
        {
            Unary("IsEmpty",             a.IsEmpty().ToString(),             lifted.IsEmpty().ToString());
            Unary("IsInfinity",          a.IsInfinity().ToString(),          lifted.IsInfinity().ToString());
            Unary("IsFinite",            a.IsFinite().ToString(),            lifted.IsFinite().ToString());
            Unary("IsUnboundedStart",    a.IsUnboundedStart().ToString(),    lifted.IsUnboundedStart().ToString());
            Unary("IsUnboundedEnd",      a.IsUnboundedEnd().ToString(),      lifted.IsUnboundedEnd().ToString());
            Unary("LowerBound",          Value(a.LowerBound()),              Value(lifted.LowerBound()));
            Unary("UpperBound",          Value(a.UpperBound()),              Value(lifted.UpperBound()));
            Unary("LowerBoundInclusive", a.LowerBoundInclusive().ToString(), lifted.LowerBoundInclusive().ToString());
            Unary("UpperBoundInclusive", a.UpperBoundInclusive().ToString(), lifted.UpperBoundInclusive().ToString());
            Unary("Merge",               Outcome(() => Show(a)),             Outcome(() => Show(lifted.Merge())));
            Unary("Complement",          Outcome(() => TRange.Infinite.Except(a).ToString()), Outcome(() => lifted.Complement().ToString()));
            Unary("ToString",            a.IsEmpty() ? "{}" : "{" + Show(a) + "}",       Outcome(() => lifted.ToString()));

            foreach (var point in points)
                Unary($"Contains({TRange.FormatValue(point, null, CultureInfo.InvariantCulture)})",
                      Outcome(() => a.Contains(point).ToString()), Outcome(() => lifted.Contains(point).ToString()));

            void Unary(string operation, string range, string set)
            {
                checks++;
                if (!string.Equals(range, set, StringComparison.Ordinal))
                    failures.Add($"{label} {name} {operation}: range → {range}, RangeSet → {set}");
            }
        }

        foreach (var (aName, a, setA) in cases)
        foreach (var (bName, b, setB) in cases)
        {
            Predicate("Contains",             () => a.Contains(b),             () => setA.Contains(setB),             () => setA.Contains(b));
            Predicate("IsContainedBy",        () => a.IsContainedBy(b),        () => setB.Contains(setA),             () => setB.Contains(a));
            Predicate("Overlaps",             () => a.Overlaps(b),             () => setA.Overlaps(setB),             () => setA.Overlaps(b));
            Predicate("IsAdjacentTo",         () => a.IsAdjacentTo(b),         () => setA.IsAdjacentTo(setB),         () => setA.IsAdjacentTo(b));
            Predicate("IsStrictlyLeftOf",     () => a.IsStrictlyLeftOf(b),     () => setA.IsStrictlyLeftOf(setB),     () => setA.IsStrictlyLeftOf(b));
            Predicate("IsStrictlyRightOf",    () => a.IsStrictlyRightOf(b),    () => setA.IsStrictlyRightOf(setB),    () => setA.IsStrictlyRightOf(b));
            Predicate("DoesNotExtendRightOf", () => a.DoesNotExtendRightOf(b), () => setA.DoesNotExtendRightOf(setB), () => setA.DoesNotExtendRightOf(b));
            Predicate("DoesNotExtendLeftOf",  () => a.DoesNotExtendLeftOf(b),  () => setA.DoesNotExtendLeftOf(setB),  () => setA.DoesNotExtendLeftOf(b));
            Predicate("Equals",               () => a.Equals(b),               () => setA.Equals(setB),               () => setA == setB);

            Operation("Intersect", () => RangeSet<TRange, T>.From([a.Intersect(b)]),
                      ("Intersect(set)", () => setA.Intersect(setB)), ("Intersect(range)", () => setA.Intersect(b)),
                      ("& set", () => setA & setB), ("& range", () => setA & b));
            Operation("Union", () => a.Union(b),
                      ("Union(set)", () => setA.Union(setB)), ("Union(range)", () => setA.Union(b)),
                      ("| set", () => setA | setB), ("| range", () => setA | b));
            Operation("Except", () => a.Except(b),
                      ("Except(set)", () => setA.Except(setB)), ("Except(range)", () => setA.Except(b)),
                      ("- set", () => setA - setB), ("- range", () => setA - b));
            Operation("Merge", () => RangeSet<TRange, T>.From([a.Merge(b)]),
                      ("Union(set).Merge()", () => RangeSet<TRange, T>.From([setA.Union(setB).Merge()])));

            void Predicate(string operation, Func<bool> range, Func<bool> setSet, Func<bool> setRange)
            {
                var expected = Outcome(() => range().ToString());
                Compare(operation, expected, $"{operation}(set)",   Outcome(() => setSet().ToString()));
                Compare(operation, expected, $"{operation}(range)", Outcome(() => setRange().ToString()));
            }

            void Operation(string operation, Func<RangeSet<TRange, T>> range, params (string Name, Func<RangeSet<TRange, T>> Run)[] lifted)
            {
                var expected = Outcome(() => range().ToString());
                foreach (var (setOperation, run) in lifted)
                    Compare(operation, expected, setOperation, Outcome(() => run().ToString()));
            }

            void Compare(string operation, string range, string setOperation, string set)
            {
                checks++;
                if (!string.Equals(range, set, StringComparison.Ordinal))
                    failures.Add($"{label} {aName} {operation} {bName}: range → {range}, RangeSet {setOperation} → {set}");
            }
        }

        return checks;

        string Show(TRange range) => ((IFormattable)range).ToString(null, CultureInfo.InvariantCulture);

        string Value(T? value) => value is { } v ? TRange.FormatValue(v, null, CultureInfo.InvariantCulture) : "null";
    }

    private static string Outcome(Func<string?> run)
    {
        try
        {
            return run() ?? "null";
        }
        catch (Exception ex)
        {
            return $"throws {ex.GetType().Name}";
        }
    }
}

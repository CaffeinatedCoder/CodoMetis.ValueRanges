using System.Diagnostics;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// An exhaustive oracle over a tiny domain: every representable range is enumerated from its
/// specification, the specification alone says which values it should hold, and every binary
/// operation is checked against set theory over every ordered pair.
/// </summary>
/// <remarks>
/// <para>
/// This is a second oracle beside <c>ShapeMatrixParityTests</c>, and it answers a different
/// question. That one asks whether the model agrees with PostgreSQL, using about ten hand-picked
/// representatives per domain; this one asks whether the model agrees with set theory, using
/// <em>every</em> bound configuration. Hand-picked fixtures are how the first version of the 7.0.0
/// Except sweep came back with zero disagreements on the exact defect it was written to catch —
/// its two operands happened to be disjoint, so the failing pair never arose.
/// </para>
/// <para>
/// The trick that makes the small domain faithful: bounds are only ever drawn from the interior of
/// the universe, so the outermost grid points are values no bound names. An unbounded side then
/// genuinely reaches a point a finite side cannot, which is what lets the model tell
/// <c>[1,6]</c> from <c>[1,+∞)</c> — a distinction the bound-level predicates
/// (<c>&amp;&lt;</c>, <c>&amp;&gt;</c>) turn on and a naive value model would lose.
/// </para>
/// <para>
/// The continuous domain uses a half-step grid so that an exclusive bound is distinguishable from
/// an inclusive one: <c>[1,2)</c> and <c>[2,3]</c> are adjacent and their grids abut, while
/// <c>[1,2)</c> and <c>(2,3]</c> are not and their grids leave the point 2 between them.
/// </para>
/// <para>
/// The model has exactly one axiom — that <c>Contains(T)</c> is correct — and it is checked
/// separately per domain rather than assumed. Everything else is derived arithmetically from the
/// specification without asking the library anything.
/// </para>
/// <para>
/// All four bugs of the receiver-shaped-dispatch family are inside this sweep's reach:
/// <c>IsAdjacentTo</c>'s asymmetry (6.2.1) fails the adjacency law in one direction,
/// <c>IsStrictlyLeftOf</c> on an unbounded start (7.0.0) fails <c>max A &lt; min B</c>,
/// <c>Except</c> between opposing unbounded operands (7.0.0) fails set difference, and
/// <c>RangeSet.Except(TRange)</c> with an infinity operand (7.0.1) fails it at the set arity.
/// </para>
/// </remarks>
[TestClass]
public class SmallModelOracleTests
{
    private enum Shape { Empty, Infinity, Finite, UnboundedStart, UnboundedEnd }

    private sealed record Spec(Shape Shape, int Start, int End, bool StartInclusive, bool EndInclusive);

    private sealed record Case<TRange>(string Label, TRange Range, HashSet<int> Model, Shape Shape);

    // Grid indices. Int32 uses one index per integer; decimal uses one per half-step, so an
    // exclusive bound and an inclusive one at the same value differ by exactly one grid point.
    private const int IntGridMax     = 7;
    private const int DecimalGridMax = 14;

    private static readonly int[] IntBounds     = [1, 2, 3, 4, 5, 6];
    private static readonly int[] DecimalBounds = [2, 4, 6, 8, 10, 12];

    // ---------------------------------------------------------------- Int32Range (discrete)

    [TestMethod]
    public void Int32Range_ModelMatchesElementContainment()
        => AssertModelIsGrounded(IntCases(), IntGridMax, index => index, "Int32Range");

    [TestMethod]
    public void Int32Range_Accessors_AgreeWithShape()
        => SweepAccessors<Int32Range, int>("Int32Range", IntCases(), IntGridMax, index => index);

    [TestMethod]
    public void DecimalRange_Accessors_AgreeWithShape()
        => SweepAccessors<DecimalRange, decimal>("DecimalRange", DecimalCases(), DecimalGridMax, index => index * 0.5m);

    [TestMethod]
    public void Int32Range_EveryOrderedPair_MatchesSetTheory()
        => Sweep<Int32Range, int>("Int32Range", IntCases(), IntGridMax, index => index);

    // ---------------------------------------------------------------- DecimalRange (continuous)

    [TestMethod]
    public void DecimalRange_ModelMatchesElementContainment()
        => AssertModelIsGrounded(DecimalCases(), DecimalGridMax, index => index * 0.5m, "DecimalRange");

    [TestMethod]
    public void DecimalRange_EveryOrderedPair_MatchesSetTheory()
        => Sweep<DecimalRange, decimal>("DecimalRange", DecimalCases(), DecimalGridMax, index => index * 0.5m);

    // ---------------------------------------------------------------- enumeration

    private static List<Case<Int32Range>> IntCases() =>
    [
        .. EnumerateSpecs(IntBounds)
          .Select(spec => new Case<Int32Range>(Describe(spec, index => index.ToString()),
                                               BuildInt(spec),
                                               ModelOf(spec, IntGridMax),
                                               spec.Shape))
    ];

    private static List<Case<DecimalRange>> DecimalCases() =>
    [
        .. EnumerateSpecs(DecimalBounds)
          .Select(spec => new Case<DecimalRange>(Describe(spec, index => (index * 0.5m).ToString("0.#")),
                                                 BuildDecimal(spec),
                                                 ModelOf(spec, DecimalGridMax),
                                                 spec.Shape))
    ];

    private static IEnumerable<Spec> EnumerateSpecs(int[] bounds)
    {
        yield return new(Shape.Empty, 0, 0, false, false);
        yield return new(Shape.Infinity, 0, 0, false, false);

        foreach (int bound in bounds)
        {
            foreach (bool inclusive in (bool[]) [true, false])
            {
                yield return new(Shape.UnboundedStart, 0, bound, false, inclusive);
                yield return new(Shape.UnboundedEnd, bound, 0, inclusive, false);
            }
        }

        foreach (int start in bounds)
        {
            foreach (int end in bounds.Where(end => end >= start))
            {
                foreach (bool startInclusive in (bool[]) [true, false])
                {
                    foreach (bool endInclusive in (bool[]) [true, false])
                        yield return new(Shape.Finite, start, end, startInclusive, endInclusive);
                }
            }
        }
    }

    // The expected value set, computed from the specification alone. Nothing here consults the
    // range it describes — that independence is what makes this an oracle rather than a mirror.
    private static HashSet<int> ModelOf(Spec spec, int gridMax)
    {
        var grid = Enumerable.Range(0, gridMax + 1);

        return spec.Shape switch
               {
                   Shape.Empty           => [],
                   Shape.Infinity        => [.. grid],
                   Shape.Finite          => [.. grid.Where(k => AtOrAfter(k, spec.Start, spec.StartInclusive)
                                                             && AtOrBefore(k, spec.End, spec.EndInclusive))],
                   Shape.UnboundedStart  => [.. grid.Where(k => AtOrBefore(k, spec.End, spec.EndInclusive))],
                   Shape.UnboundedEnd    => [.. grid.Where(k => AtOrAfter(k, spec.Start, spec.StartInclusive))],
                   _                     => throw new UnreachableException()
               };

        static bool AtOrAfter(int k, int bound, bool inclusive)  => k > bound || (k == bound && inclusive);
        static bool AtOrBefore(int k, int bound, bool inclusive) => k < bound || (k == bound && inclusive);
    }

    private static Int32Range BuildInt(Spec spec) =>
        spec.Shape switch
        {
            Shape.Empty          => Int32Range.Empty,
            Shape.Infinity       => Int32Range.Infinite,
            Shape.Finite         => Int32Range.CreateFinite(spec.Start, spec.End, spec.StartInclusive, spec.EndInclusive),
            Shape.UnboundedStart => Int32Range.CreateUnboundedStart(spec.End, spec.EndInclusive),
            Shape.UnboundedEnd   => Int32Range.CreateUnboundedEnd(spec.Start, spec.StartInclusive),
            _                    => throw new UnreachableException()
        };

    private static DecimalRange BuildDecimal(Spec spec) =>
        spec.Shape switch
        {
            Shape.Empty          => DecimalRange.Empty,
            Shape.Infinity       => DecimalRange.Infinite,
            Shape.Finite         => DecimalRange.CreateFinite(spec.Start * 0.5m, spec.End * 0.5m, spec.StartInclusive, spec.EndInclusive),
            Shape.UnboundedStart => DecimalRange.CreateUnboundedStart(spec.End * 0.5m, spec.EndInclusive),
            Shape.UnboundedEnd   => DecimalRange.CreateUnboundedEnd(spec.Start * 0.5m, spec.StartInclusive),
            _                    => throw new UnreachableException()
        };

    private static string Describe(Spec spec, Func<int, string> text) =>
        spec.Shape switch
        {
            Shape.Empty          => "empty",
            Shape.Infinity       => "(,)",
            Shape.Finite         => $"{(spec.StartInclusive ? '[' : '(')}{text(spec.Start)},{text(spec.End)}{(spec.EndInclusive ? ']' : ')')}",
            Shape.UnboundedStart => $"(,{text(spec.End)}{(spec.EndInclusive ? ']' : ')')}",
            Shape.UnboundedEnd   => $"{(spec.StartInclusive ? '[' : '(')}{text(spec.Start)},)",
            _                    => throw new UnreachableException()
        };

    // ---------------------------------------------------------------- accessors

    /// <summary>
    /// The bound accessors and <c>Clamp</c>, which the pair sweep never touches — they take an
    /// element or nothing at all, so no shape-pair matrix reaches them. Their discard arms answer
    /// <see langword="null"/> or <see langword="false"/> for three shapes at once, which is correct
    /// but was argued rather than checked.
    /// </summary>
    /// <remarks>
    /// Grounded in three independent links rather than one: nullness comes from the specification's
    /// shape, inclusivity is cross-checked against <c>Contains</c> (the model's axiom, pinned
    /// separately), and <c>Clamp</c> is checked against the bounds those two have just established.
    /// Predicting a bound's *value* directly would not work — a discrete range canonicalizes, so
    /// <c>(1,5)</c> reports its lower bound as 2, and an exclusive continuous bound is a value the
    /// range does not contain.
    /// </remarks>
    private static void SweepAccessors<TRange, T>(
        string             domain,
        List<Case<TRange>> cases,
        int                gridMax,
        Func<int, T>       valueOf
    )
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var failures = new List<string>();

        foreach (var probe in cases)
        {
            // A finite specification with no values between its bounds — [1,1), (1,1), and on a
            // discrete domain (1,2) — is the empty range, because CreateFinite collapses it. The
            // shape to predict from is therefore the collapsed one, not the one asked for.
            bool isEmpty      = probe.Model.Count == 0;
            bool boundedBelow = !isEmpty && probe.Shape is Shape.Finite or Shape.UnboundedEnd;
            bool boundedAbove = !isEmpty && probe.Shape is Shape.Finite or Shape.UnboundedStart;

            var lower = probe.Range.LowerBound();
            var upper = probe.Range.UpperBound();

            Check($"LowerBound() is null", lower is null, !boundedBelow);
            Check($"UpperBound() is null", upper is null, !boundedAbove);

            // An absent bound is never inclusive — PostgreSQL's lower_inc/upper_inc agree.
            if (lower is null) Check("LowerBoundInclusive()", probe.Range.LowerBoundInclusive(), false);
            if (upper is null) Check("UpperBoundInclusive()", probe.Range.UpperBoundInclusive(), false);

            // A present bound belongs to the range exactly when it says it is inclusive.
            if (lower is { } lowerValue)
                Check("Contains(LowerBound())", probe.Range.Contains(lowerValue), probe.Range.LowerBoundInclusive());

            if (upper is { } upperValue)
                Check("Contains(UpperBound())", probe.Range.Contains(upperValue), probe.Range.UpperBoundInclusive());

            for (int k = 0; k <= gridMax; k++)
            {
                var value    = valueOf(k);
                var clamped  = probe.Range.Clamp(value);
                var expected = probe.Model.Count == 0 ? null
                             : probe.Model.Contains(k) ? value
                             : k < probe.Model.Min()   ? lower
                                                       : upper;

                if (!Nullable.Equals(clamped, expected))
                    failures.Add($"  {probe.Label}.Clamp({value}) = {Describe(clamped)}, expected {Describe(expected)}");
            }

            void Check(string accessor, bool actual, bool expected)
            {
                if (actual != expected)
                    failures.Add($"  {probe.Label}.{accessor} = {actual}, its shape says {expected}");
            }
        }

        Assert.AreEqual(0, failures.Count,
                        $"{domain}: the bound accessors disagree with the shapes they describe:"
                      + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(20)));

        static string Describe(T? value) => value?.ToString() ?? "null";
    }

    // ---------------------------------------------------------------- the axiom

    /// <summary>
    /// The model reads a result back through <c>Contains(T)</c>, so that one operation cannot be
    /// verified by the sweep without circularity. It is pinned here instead: for every enumerated
    /// range, the values it reports containing must be exactly the values its specification says.
    /// </summary>
    private static void AssertModelIsGrounded<TRange>(
        List<Case<TRange>>          cases,
        int                         gridMax,
        Func<int, object>           valueOf,
        string                      domain
    )
    {
        var failures = new List<string>();

        foreach (var probe in cases)
        {
            var actual = new HashSet<int>();
            for (int k = 0; k <= gridMax; k++)
            {
                bool contains = probe.Range switch
                                {
                                    Int32Range r   => r.Contains((int) valueOf(k)),
                                    DecimalRange r => r.Contains((decimal) valueOf(k)),
                                    _              => throw new UnreachableException()
                                };

                if (contains) actual.Add(k);
            }

            if (!actual.SetEquals(probe.Model))
                failures.Add($"  {probe.Label}: spec says {{{Join(probe.Model)}}}, Contains(T) says {{{Join(actual)}}}");
        }

        Assert.IsTrue(cases.Count >= 100, $"{domain}: expected the enumeration to produce at least 100 ranges, got {cases.Count}.");
        Assert.AreEqual(0, failures.Count,
                        $"{domain}: the model's one axiom — that Contains(T) is correct — does not hold:"
                      + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(20)));
    }

    // ---------------------------------------------------------------- the sweep

    private static void Sweep<TRange, T>(
        string             domain,
        List<Case<TRange>> cases,
        int                gridMax,
        Func<int, T>       valueOf
    )
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var failures = new List<string>();
        int checks   = 0;

        foreach (var a in cases)
        {
            foreach (var b in cases)
            {
                HashSet<int> union        = [.. a.Model, .. b.Model];
                HashSet<int> intersection = [.. a.Model.Where(b.Model.Contains)];
                HashSet<int> difference   = [.. a.Model.Where(k => !b.Model.Contains(k))];
                bool         bothNonEmpty = a.Model.Count > 0 && b.Model.Count > 0;

                // --- predicates
                Check("Contains",             a.Range.Contains(b.Range),             b.Model.IsSubsetOf(a.Model));
                Check("IsContainedBy",        a.Range.IsContainedBy(b.Range),        a.Model.IsSubsetOf(b.Model));
                Check("Overlaps",             a.Range.Overlaps(b.Range),             intersection.Count > 0);
                Check("IsAdjacentTo",         a.Range.IsAdjacentTo(b.Range),         bothNonEmpty && intersection.Count == 0 && IsContiguous(union));
                Check("IsStrictlyLeftOf",     a.Range.IsStrictlyLeftOf(b.Range),     bothNonEmpty && a.Model.Max() < b.Model.Min());
                Check("IsStrictlyRightOf",    a.Range.IsStrictlyRightOf(b.Range),    bothNonEmpty && a.Model.Min() > b.Model.Max());
                Check("DoesNotExtendRightOf", a.Range.DoesNotExtendRightOf(b.Range), bothNonEmpty && a.Model.Max() <= b.Model.Max());
                Check("DoesNotExtendLeftOf",  a.Range.DoesNotExtendLeftOf(b.Range),  bothNonEmpty && a.Model.Min() >= b.Model.Min());

                // --- value-producing operations
                CheckSet("Intersect", ModelOfRange(a.Range.Intersect(b.Range)), intersection);
                CheckSet("Merge",     ModelOfRange(a.Range.Merge(b.Range)),     Hull(union));
                CheckSet("Union",     ModelOfSet(a.Range.Union(b.Range)),       union);
                CheckSet("Except",    ModelOfSet(a.Range.Except(b.Range)),      difference);

                // --- the same questions at the set arities. Two of the four bugs were the
                //     single-range overload and a set overload disagreeing, so an operation
                //     lifted through RangeSet.From([r]) must give the lifted answer.
                var liftedA = RangeSet<TRange, T>.From([a.Range]);
                var liftedB = RangeSet<TRange, T>.From([b.Range]);

                Check("Set.Contains(range)",  liftedA.Contains(b.Range), b.Model.IsSubsetOf(a.Model));
                Check("Set.Contains(set)",    liftedA.Contains(liftedB), b.Model.IsSubsetOf(a.Model));
                Check("Set.Overlaps(range)",  liftedA.Overlaps(b.Range), intersection.Count > 0);

                CheckSet("Set.Except(range)", ModelOfSet(liftedA.Except(b.Range)), difference);
                CheckSet("Set.Except(set)",   ModelOfSet(liftedA.Except(liftedB)), difference);
                CheckSet("Set.Union(set)",    ModelOfSet(liftedA.Union(liftedB)),  union);
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
        }

        Assert.IsTrue(checks > 100_000,
                      $"{domain}: expected the sweep to make well over 100,000 checks, made {checks}. "
                    + "Has the enumeration stopped producing ranges?");

        Assert.AreEqual(0, failures.Count,
                        $"{domain}: {failures.Count} of {checks} checks disagree with set theory:"
                      + Environment.NewLine + string.Join(Environment.NewLine, failures.Take(25))
                      + (failures.Count > 25 ? $"{Environment.NewLine}  … and {failures.Count - 25} more" : ""));

        HashSet<int> ModelOfRange(TRange range) =>
            [.. Enumerable.Range(0, gridMax + 1).Where(k => range.Contains(valueOf(k)))];

        HashSet<int> ModelOfSet(RangeSet<TRange, T> set) =>
            [.. Enumerable.Range(0, gridMax + 1).Where(k => set.Contains(valueOf(k)))];
    }

    // ---------------------------------------------------------------- set-theory helpers

    private static bool IsContiguous(HashSet<int> values) =>
        values.Count == 0 || values.Max() - values.Min() + 1 == values.Count;

    private static HashSet<int> Hull(HashSet<int> values) =>
        values.Count == 0 ? [] : [.. Enumerable.Range(values.Min(), values.Max() - values.Min() + 1)];

    private static string Join(HashSet<int> values) => string.Join(",", values.Order());
}

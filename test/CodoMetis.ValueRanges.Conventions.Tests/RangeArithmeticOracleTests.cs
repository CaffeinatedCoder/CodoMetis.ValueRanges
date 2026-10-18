using System.Reflection;
using System.Runtime.ExceptionServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using NodaTime;
using static CodoMetis.ValueRanges.Conventions.Tests.RangeSpecs;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// <c>Shift</c> and <c>Expand</c> on every discovered range type, against the shape matrix moved on its
/// own grid, and against the laws that hold between them.
/// </summary>
/// <remarks>
/// <para>
/// Every type declares its own <c>Shift</c> and <c>Expand</c>, because the step is the element type's:
/// days, a <see cref="TimeSpan"/>, a NodaTime <see cref="Period"/>. The sweep moves by whole steps of
/// the <see cref="RangeSpecs"/> embedding, so a range moved by <c>k</c> steps is the specification with
/// both bounds moved by <c>k</c> grid positions, built through the type's own factory. That is the
/// independent answer, and it covers every shape and every inclusivity, which is where a rebuild
/// per shape goes wrong.
/// </para>
/// <para>
/// The laws need no grid: moving back restores the range, a grown range contains the original, and
/// growing by <c>a</c> is the hull of the range moved down and up by <c>a</c>, while shrinking is
/// their intersection. At the domain's extremes the promise is a loud one: a bound moved past the
/// last value throws <see cref="OverflowException"/>, never wraps and never clips.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeArithmeticOracleTests
{
    /// <summary>One grid step of each embedding, as the argument type of that range's <c>Shift</c>.</summary>
    private static readonly Dictionary<Type, Func<int, object>> GridSteps = new()
    {
        [typeof(int)]            = k => k,
        [typeof(long)]           = k => (long)k,
        [typeof(decimal)]        = k => k * 0.5m,
        [typeof(double)]         = k => k * 0.5,
        [typeof(float)]          = k => k * 0.5f,
        [typeof(DateOnly)]       = k => k,
        [typeof(TimeOnly)]       = k => TimeSpan.FromMinutes(30 * k),
        [typeof(DateTime)]       = k => TimeSpan.FromMinutes(30 * k),
        [typeof(DateTimeOffset)] = k => TimeSpan.FromMinutes(30 * k),
        [typeof(LocalDate)]      = k => Period.FromDays(k),
        [typeof(LocalDateTime)]  = k => Period.FromMinutes(30 * k),
        [typeof(Instant)]        = k => Duration.FromMinutes(30 * k),
        [typeof(YearMonth)]      = k => k
    };

    private static readonly int[] Steps = [-3, -1, 0, 1, 2, 3];

    /// <summary>
    /// The step at the domain's extremes, where it differs from the grid step. Adding half a unit to
    /// <see cref="double.MaxValue"/> rounds back to it, as IEEE addition does, so the overflow past the last
    /// value is asked for with one unit in the last place there (2^971) instead.
    /// </summary>
    /// <summary>
    /// The element types whose extremes stand for PostgreSQL's <c>-infinity</c> and <c>infinity</c>, each
    /// with a value two grid steps below its maximum. On these a bound at an extreme stays where it is
    /// under <c>Shift</c> and <c>Expand</c> (#224), and a finite bound moved past one still throws.
    /// </summary>
    private static readonly Dictionary<Type, object> InfinityBacked = new()
    {
        [typeof(DateOnly)]       = DateOnly.MaxValue.AddDays(-2),
        [typeof(DateTime)]       = DateTime.MaxValue.AddMinutes(-60),
        [typeof(DateTimeOffset)] = DateTimeOffset.MaxValue.AddMinutes(-60),
        [typeof(LocalDate)]      = LocalDate.MaxIsoValue.PlusDays(-2),
        [typeof(LocalDateTime)]  = LocalDateTime.MaxIsoValue.PlusMinutes(-60),
        [typeof(Instant)]        = Instant.MaxValue.Minus(Duration.FromMinutes(60)),
        [typeof(YearMonth)]      = new YearMonth(9999, 10)
    };

    private static readonly Dictionary<Type, Func<int, object>> EdgeSteps = new()
    {
        [typeof(double)] = k => k * Math.ScaleB(1.0, 971),
        [typeof(float)]  = k => k * MathF.ScaleB(1f, 104)
    };

    public sealed record ArithmeticRow(string Label, Func<List<string>, int> Sweep)
    {
        public override string ToString() => Label;
    }

    public static IEnumerable<object[]> Rows =>
        RangeProbes.AllRangeTypes().Select(pair => new object[]
        {
            new ArithmeticRow(pair.Range.Name, failures => Reflect.InvokeGeneric<int>(
                typeof(RangeArithmeticOracleTests), nameof(Sweep), [pair.Range, pair.Element], failures))
        });

    [TestMethod]
    public void Discovery_EveryRangeType_DeclaresShiftAndExpandOverItsStep()
    {
        var types = RangeProbes.AllRangeTypes().ToList();
        Assert.IsTrue(types.Count >= 11, $"Found {types.Count} range types, fewer than 11.");

        var missing = types
                     .Where(pair => !GridSteps.TryGetValue(pair.Element, out var step)
                                 || pair.Range.GetMethod("Shift", [step(1).GetType()]) is null
                                 || pair.Range.GetMethod("Expand", [step(1).GetType()]) is null)
                     .Select(pair => pair.Range.Name)
                     .ToList();

        Assert.AreEqual(0, missing.Count,
                        "A range type has no grid step here, or no Shift/Expand over it: " + string.Join(", ", missing));
    }

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void ShiftAndExpand_MatchTheMovedSpecification(ArithmeticRow row)
    {
        var failures = new List<string>();
        var checks   = row.Sweep(failures);

        Assert.IsTrue(checks >= 2_000, $"{row}: only {checks} checks ran.");

        Candidates.Verdict(nameof(RangeArithmeticOracleTests), failures,
            $"{row}: of {checks} checks, Shift or Expand disagrees with the moved range", shown: 30, rowPrefix: row.Label + " ");
    }

    private static int Sweep<TRange, T>(List<string> failures)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var label   = typeof(TRange).Name;
        var valueOf = ValueOf<T>();
        var step    = GridSteps[typeof(T)];
        var shift   = typeof(TRange).GetMethod("Shift", [step(1).GetType()])!;
        var expand  = typeof(TRange).GetMethod("Expand", [step(1).GetType()])!;
        var checks  = 0;

        TRange Call(MethodInfo method, TRange range, int k, Func<int, object>? by = null)
        {
            try
            {
                return (TRange)method.Invoke(range, [(by ?? step)(k)])!;
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } actual)
            {
                ExceptionDispatchInfo.Capture(actual).Throw();
                throw;
            }
        }

        // ---- the grid: every shape and inclusivity, moved by whole steps
        int[] bounds = TRange.IsDiscrete ? [1, 2, 3, 4] : [2, 4, 6, 8];

        foreach (var spec in EnumerateSpecs(bounds))
        {
            var range = Build<TRange, T>(spec, valueOf);
            var name  = Describe<TRange, T>(spec, valueOf);

            foreach (var k in Steps)
            {
                var shifted  = spec with { Start = spec.Start + k, End = spec.End + k };
                var expanded = spec with { Start = spec.Start - k, End = spec.End + k };

                // A specification with nothing between its bounds, such as [2,2), builds the empty
                // range, which has no bounds left to move: it stays empty however far it is grown.
                Same($"Shift({k})", () => Call(shift, range, k), () => range.IsEmpty() ? range : Build<TRange, T>(shifted, valueOf));
                Same($"Expand({k})", () => Call(expand, range, k), () => range.IsEmpty() ? range : Build<TRange, T>(expanded, valueOf));
                Same($"Shift({k}).Shift({-k})", () => Call(shift, Call(shift, range, k), -k), () => range);

                var magnitude = Math.Abs(k);
                var down      = Call(shift, range, -magnitude);
                var up        = Call(shift, range, magnitude);
                Same($"Expand({magnitude}) is the hull of Shift(±{magnitude})", () => Call(expand, range, magnitude), () => down.Merge(up));
                Same($"Expand({-magnitude}) is the overlap of Shift(±{magnitude})", () => Call(expand, range, -magnitude), () => up.Intersect(down));

                checks++;
                if (!Call(expand, range, magnitude).Contains(range))
                    failures.Add($"{label} {name} Expand({magnitude}) does not contain the range");
            }

            void Same(string operation, Func<TRange> actual, Func<TRange> expected)
            {
                checks++;
                var (got, want) = (Outcome(actual), Outcome(expected));
                if (got != want)
                    failures.Add($"{label} {name} {operation} → {got}, expected {want}");
            }
        }

        // ---- the extremes: past the last value throws, away from it does not
        var (min, max) = ((T)Extremes[typeof(T)].Min, (T)Extremes[typeof(T)].Max);
        var middle     = valueOf(4);

        (string Name, TRange Range, bool AtMin, bool AtMax)[] edges =
        [
            ("[min,middle]",  TRange.CreateFinite(min, middle, true, true), true, false),
            ("[middle,max]",  TRange.CreateFinite(middle, max, true, true), false, true),
            ("[min,)",        TRange.CreateUnboundedEnd(min, true), true, false),
            ("(,max]",        TRange.CreateUnboundedStart(max, true), false, true),
            ("[min,max]",     TRange.CreateFinite(min, max, true, true), true, true),
        ];

        var edge     = EdgeSteps.GetValueOrDefault(typeof(T), step);
        var infinite = InfinityBacked.TryGetValue(typeof(T), out var belowMax);

        foreach (var (name, range, atMin, atMax) in edges)
        {
            if (infinite)
            {
                // The extreme is an infinity: it stays, whichever way the range moves, and the
                // finite bound moves alone. Nothing throws, and moving back is the identity.
                foreach (var (operation, k) in new[] { ("Shift", 1), ("Shift", -1), ("Expand", 1), ("Expand", -1) })
                {
                    checks++;
                    var method = operation == "Shift" ? shift : expand;
                    var moved  = Outcome(() => Call(method, range, k, edge));
                    var kept   = Outcome(() =>
                    {
                        var result = Call(method, range, k, edge);
                        return (!atMin || Equals(result.LowerBound(), min)) && (!atMax || Equals(result.UpperBound(), max))
                                   ? "kept"
                                   : $"moved to {result}";
                    });
                    if (kept != "kept")
                        failures.Add($"{label} {name} {operation}({k}) at the domain edge → {moved}, expected the infinite bound to stay");
                }

                checks++;
                var back = Outcome(() => Call(shift, Call(shift, range, 1, edge), -1, edge));
                if (back != range.ToString())
                    failures.Add($"{label} {name} Shift(1).Shift(-1) at the domain edge → {back}, expected {range}");
            }
            else
            {
                Throws("Shift(1)",   () => Call(shift, range, 1, edge),   atMax);
                Throws("Shift(-1)",  () => Call(shift, range, -1, edge),  atMin);
                Throws("Expand(1)",  () => Call(expand, range, 1, edge),  atMin || atMax);
                Throws("Expand(-1)", () => Call(expand, range, -1, edge), false);
            }

            // Nothing moves an empty or infinite range, so nothing there can overflow.
            Throws("Empty.Shift", () => Call(shift, TRange.Empty, 1), false);
            Throws("Infinite.Expand", () => Call(expand, TRange.Infinite, 1), false);

            void Throws(string operation, Func<TRange> run, bool expected)
            {
                checks++;
                var outcome = Outcome(run);
                var threw   = outcome == $"throws {nameof(OverflowException)}";
                if (threw != expected)
                    failures.Add($"{label} {name} {operation} at the domain edge → {outcome}, expected "
                               + (expected ? nameof(OverflowException) : "a range"));
            }
        }

        // A finite bound two steps below the maximum moves up to it, and past it throws, infinity or not.
        if (infinite)
        {
            var near = TRange.CreateFinite((T)belowMax!, (T)belowMax!, true, true);
            foreach (var (k, expected) in new[] { (1, false), (3, true) })
            {
                checks++;
                var outcome = Outcome(() => Call(shift, near, k, edge));
                if ((outcome == $"throws {nameof(OverflowException)}") != expected)
                    failures.Add($"{label} {near} Shift({k}) below the domain edge → {outcome}, expected "
                               + (expected ? nameof(OverflowException) : "a range"));
            }
        }

        return checks;
    }

    private static string Outcome<TRange>(Func<TRange> run)
    {
        try
        {
            return run()?.ToString() ?? "null";
        }
        catch (Exception exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }
}

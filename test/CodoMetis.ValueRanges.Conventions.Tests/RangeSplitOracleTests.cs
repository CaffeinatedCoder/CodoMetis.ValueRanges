using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using NodaTime;
using static CodoMetis.ValueRanges.Conventions.Tests.RangeSpecs;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// <c>Split(step)</c> on every discovered range type, <c>Values(step)</c> on every discrete one, and
/// the calendar <c>Split(period)</c> of the date ranges, against what a cut must be: pieces that
/// together are the range, each adjacent to the next, every piece but the last exactly one step long.
/// </summary>
/// <remarks>
/// <para>
/// The relations need no model of where a piece should end. <c>RangeSet.From(range.Split(step))</c> is
/// the range itself, which holds every value exactly once; consecutive pieces are adjacent, which
/// holds the inclusiveness at every cut; and piece <c>i</c> starts at the range's start moved by
/// <c>i</c> whole steps of the <see cref="RangeSpecs"/> grid, which holds the step length and the
/// clipping of the last piece.
/// </para>
/// <para>
/// A range ending at the domain's maximum is in the sweep too: the boundary past it cannot be
/// computed, and the last piece must still end at the range's own end.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeSplitOracleTests
{
    /// <summary>One grid step of each embedding, as the argument type of that range's <c>Split</c>.</summary>
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

    /// <summary>A value a few grid steps below each domain's maximum.</summary>
    private static readonly Dictionary<Type, object> NearMax = new()
    {
        [typeof(int)]            = int.MaxValue - 3,
        [typeof(long)]           = long.MaxValue - 3,
        [typeof(decimal)]        = decimal.MaxValue - 3m,
        [typeof(double)]         = double.MaxValue - 4 * Math.ScaleB(1.0, 971),
        [typeof(float)]          = float.MaxValue - 4 * MathF.ScaleB(1f, 104),
        [typeof(DateOnly)]       = DateOnly.MaxValue.AddDays(-3),
        [typeof(TimeOnly)]       = TimeOnly.MaxValue.AddMinutes(-90),
        [typeof(DateTime)]       = DateTime.MaxValue.AddMinutes(-90),
        [typeof(DateTimeOffset)] = DateTimeOffset.MaxValue.AddMinutes(-90),
        [typeof(LocalDate)]      = LocalDate.MaxIsoValue.PlusDays(-3),
        [typeof(LocalDateTime)]  = LocalDateTime.MaxIsoValue.PlusMinutes(-90),
        [typeof(Instant)]        = Instant.MaxValue.Minus(Duration.FromMinutes(90)),
        [typeof(YearMonth)]      = new YearMonth(9999, 9)
    };

    /// <summary>
    /// The step at the domain's maximum, where it differs from the grid step. Near
    /// <see cref="double.MaxValue"/> one unit in the last place is 2^971, so half a unit of the grid is no
    /// step at all — <c>Split</c> throws for a step too small to move the start, as it does for decimal —
    /// and the edge is approached in halves of that unit instead.
    /// </summary>
    private static readonly Dictionary<Type, Func<int, object>> EdgeSteps = new()
    {
        [typeof(double)] = k => k * Math.ScaleB(1.0, 970),
        [typeof(float)]  = k => k * MathF.ScaleB(1f, 103)
    };

    public sealed record SplitRow(string Label, Func<List<string>, int> Sweep)
    {
        public override string ToString() => Label;
    }

    public static IEnumerable<object[]> Rows =>
        RangeProbes.AllRangeTypes().Select(pair => new object[]
        {
            new SplitRow(pair.Range.Name, failures => Reflect.InvokeGeneric<int>(
                typeof(RangeSplitOracleTests), nameof(Sweep), [pair.Range, pair.Element], failures))
        });

    [TestMethod]
    public void Discovery_EveryRangeType_DeclaresSplitOverItsStep()
    {
        var types = RangeProbes.AllRangeTypes().ToList();
        Assert.IsTrue(types.Count >= 11, $"Found {types.Count} range types, fewer than 11.");

        var missing = types
                     .Where(pair => !GridSteps.TryGetValue(pair.Element, out var step)
                                 || pair.Range.GetMethod("Split", [step(1).GetType()]) is null)
                     .Select(pair => pair.Range.Name)
                     .ToList();

        Assert.AreEqual(0, missing.Count, "A range type has no grid step here, or no Split over it: " + string.Join(", ", missing));
    }

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void Split_PiecesAreTheRange_AdjacentAndOneStepLong(SplitRow row)
    {
        var failures = new List<string>();
        var checks   = row.Sweep(failures);

        Assert.IsTrue(checks >= 500, $"{row}: only {checks} checks ran.");

        Candidates.Verdict(nameof(RangeSplitOracleTests), failures,
            $"{row}: of {checks} checks, Split disagrees with the range it cuts", shown: 30, rowPrefix: row.Label + " ");
    }

    private static int Sweep<TRange, T>(List<string> failures)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var label   = typeof(TRange).Name;
        var valueOf = ValueOf<T>();
        var step    = GridSteps[typeof(T)];
        var split   = typeof(TRange).GetMethod("Split", [step(1).GetType()])!;
        var values  = typeof(TRange).GetMethod("Values", [step(1).GetType()]);
        var checks  = 0;

        Assert.AreEqual(TRange.IsDiscrete, values is not null, $"{label}: Values(step) belongs exactly to the discrete types.");

        int[] bounds = TRange.IsDiscrete ? [1, 2, 3, 5, 8] : [2, 4, 6, 10, 16];

        foreach (var spec in EnumerateSpecs(bounds).Where(spec => spec.Shape == Shape.Finite))
        {
            var range = Build<TRange, T>(spec, valueOf);
            var name  = Describe<TRange, T>(spec, valueOf);

            // A discrete range canonicalizes an exclusive start to the next value, where its pieces count from.
            var first = spec.Start + (TRange.IsDiscrete && !spec.StartInclusive ? 1 : 0);

            foreach (var k in (int[]) [1, 2, 3, 7])
                CheckSplit(range, name, k, first);
        }

        // At the domain's maximum the next boundary does not exist; the last piece still ends there.
        var max     = (T)Extremes[typeof(T)].Max;
        var nearMax = TRange.CreateFinite((T)NearMax[typeof(T)], max, true, true);
        // Two and four grid steps: near its maximum decimal holds no fraction, so half a unit is no step at all.
        foreach (var k in (int[]) [2, 4])
        {
            checks++;
            var outcome = Outcome(() =>
            {
                var pieces = Run(split, nearMax, k, EdgeSteps.GetValueOrDefault(typeof(T), step));
                return RangeSet<TRange, T>.From(pieces).Equals(RangeSet<TRange, T>.From([nearMax])) && Equals(pieces[^1].UpperBound(), max)
                           ? "ok"
                           : $"pieces {Join(pieces)}";
            });
            if (outcome != "ok")
                failures.Add($"{label} {nearMax} Split({k}) at the domain edge → {outcome}, expected pieces ending at the maximum");
        }

        // The rejections, at the call rather than at the first MoveNext.
        foreach (var (what, range, k, exception) in new (string, TRange, int, string)[]
                 {
                     ("unbounded end",   TRange.CreateUnboundedEnd(valueOf(1), true), 1, nameof(NotSupportedException)),
                     ("unbounded start", TRange.CreateUnboundedStart(valueOf(1), true), 1, nameof(NotSupportedException)),
                     ("infinite",        TRange.Infinite, 1, nameof(NotSupportedException)),
                     ("zero step",       TRange.CreateFinite(valueOf(1), valueOf(4), true, true), 0, nameof(ArgumentOutOfRangeException)),
                     ("negative step",   TRange.CreateFinite(valueOf(1), valueOf(4), true, true), -1, nameof(ArgumentOutOfRangeException)),
                 })
        {
            Rejects($"Split {what}", () => Invoke(split, range, k));
            if (values is not null) Rejects($"Values(step) {what}", () => Invoke(values, range, k));

            void Rejects(string operation, Func<object> call)
            {
                checks++;
                var outcome = Outcome(() => call().GetType().Name);
                if (outcome != $"throws {exception}")
                    failures.Add($"{label} {operation} at the call → {outcome}, expected {exception}");
            }
        }

        checks++;
        if (Run(split, TRange.Empty, 1).Count != 0)
            failures.Add($"{label} Empty.Split(1) yields pieces");

        return checks;

        void CheckSplit(TRange range, string name, int k, int first)
        {
            var pieces = Run(split, range, k);

            checks++;
            var union = RangeSet<TRange, T>.From(pieces);
            if (!union.Equals(RangeSet<TRange, T>.From([range])))
                failures.Add($"{label} {name} Split({k}) → pieces {Join(pieces)} hold {union}, expected {range}");

            for (var i = 0; i < pieces.Count; i++)
            {
                checks++;
                if (pieces[i].IsEmpty())
                    failures.Add($"{label} {name} Split({k}) → piece {i} is empty: {Join(pieces)}");

                if (i + 1 < pieces.Count && !(pieces[i].IsAdjacentTo(pieces[i + 1]) && pieces[i].IsStrictlyLeftOf(pieces[i + 1])))
                    failures.Add($"{label} {name} Split({k}) → piece {i} {pieces[i]} is not followed by an adjacent {pieces[i + 1]}");

                // Every piece after the first starts a whole number of steps from the range's start.
                if (i > 0 && !Nullable.Equals(pieces[i].LowerBound(), valueOf(first + i * k)))
                    failures.Add($"{label} {name} Split({k}) → piece {i} {pieces[i]} does not start {i} steps from the start");
            }

            // ...and the last is at most one step long: the boundary after it lies past the range,
            // or, on a continuous type, on the range's end, which the last piece takes.
            if (pieces.Count > 0 && range.UpperBound() is { } upper)
            {
                checks++;
                var after = valueOf(first + pieces.Count * k).CompareTo(upper);
                if (TRange.IsDiscrete ? after <= 0 : after < 0)
                    failures.Add($"{label} {name} Split({k}) → {Join(pieces)} stops early: its last piece is longer than one step");
            }

            if (values is null) return;

            checks++;
            var stepped  = ((IEnumerable)Invoke(values, range, k)).Cast<T>().ToList();
            var expected = EveryKth(range, k);
            if (!stepped.SequenceEqual(expected))
                failures.Add($"{label} {name} Values({k}) → {string.Join(",", stepped)}, expected {string.Join(",", expected)}");
        }

        List<TRange> Run(MethodInfo method, TRange range, int k, Func<int, object>? by = null)
            => [.. ((IEnumerable)Invoke(method, range, k, by)).Cast<TRange>()];

        object Invoke(MethodInfo method, TRange range, int k, Func<int, object>? by = null)
        {
            try
            {
                return method.Invoke(range, [(by ?? step)(k)])!;
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } actual)
            {
                ExceptionDispatchInfo.Capture(actual).Throw();
                throw;
            }
        }

        // Values() is held by its own claim; every k-th of it is the independent answer here.
        static List<T> EveryKth(TRange range, int k)
            => [.. ((IEnumerable)typeof(TRange).GetMethod("Values", Type.EmptyTypes)!.Invoke(range, null)!)
                    .Cast<T>().Where((_, index) => index % k == 0)];
    }

    /// <summary>The calendar split of the date ranges: one piece per period, clipped to the range.</summary>
    [TestMethod]
    public void SplitByCalendarPeriod_OnePiecePerPeriod_ClippedToTheRange()
    {
        var failures = new List<string>();
        Func<DateOnly, DateRange>[] periods = [DateRange.Month, DateRange.Quarter, DateRange.Year, DateRange.IsoWeek];
        string[] names = ["Month", "Quarter", "Year", "IsoWeek"];

        for (var start = new DateOnly(2025, 12, 20); start <= new DateOnly(2026, 2, 5); start = start.AddDays(3))
        foreach (var length in (int[]) [0, 1, 6, 30, 95, 400])
        for (var p = 0; p < periods.Length; p++)
        {
            var range  = DateRange.CreateFinite(start, start.AddDays(length));
            var pieces = range.Split(periods[p]).ToList();
            var label  = $"{range} Split({names[p]})";

            if (!RangeSet<DateRange, DateOnly>.From(pieces).Equals(RangeSet<DateRange, DateOnly>.From([range])))
                failures.Add($"{label} → {Join(pieces)} is not the range");

            for (var i = 0; i < pieces.Count; i++)
            {
                var period = periods[p](pieces[i].LowerBound()!.Value);
                if (!period.Contains(pieces[i]))
                    failures.Add($"{label} → piece {pieces[i]} crosses the boundary of {period}");

                // A piece away from both ends is a whole period.
                if (i > 0 && i < pieces.Count - 1 && pieces[i] != period)
                    failures.Add($"{label} → inner piece {pieces[i]} is not the whole period {period}");

                if (i + 1 < pieces.Count && !pieces[i].IsAdjacentTo(pieces[i + 1]))
                    failures.Add($"{label} → piece {pieces[i]} is not adjacent to {pieces[i + 1]}");
            }

            // The NodaTime split agrees day for day.
            var local      = LocalDateRange.CreateFinite(LocalDate.FromDateOnly(start), LocalDate.FromDateOnly(start.AddDays(length)));
            Func<LocalDate, LocalDateRange>[] localPeriods = [LocalDateRange.Month, LocalDateRange.Quarter, LocalDateRange.Year, LocalDateRange.IsoWeek];
            var localText  = string.Join(" ", local.Split(localPeriods[p]).Select(piece => piece.ToString()));
            var pieceText  = string.Join(" ", pieces.Select(piece => piece.ToString()));
            if (localText != pieceText)
                failures.Add($"{label} → LocalDateRange gives {localText}, DateRange {pieceText}");
        }

        Candidates.Verdict(nameof(RangeSplitOracleTests), failures, "the calendar split disagrees with its periods",
                           method: nameof(SplitByCalendarPeriod_OnePiecePerPeriod_ClippedToTheRange));
    }

    private static string Join<TRange>(IEnumerable<TRange> pieces) => "{" + string.Join(",", pieces) + "}";

    private static string Outcome(Func<object> run)
    {
        try
        {
            return run().ToString() ?? "null";
        }
        catch (Exception exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }
}

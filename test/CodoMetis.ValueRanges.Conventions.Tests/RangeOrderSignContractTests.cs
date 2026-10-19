using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every range type's <c>CompareTo</c>, and its <see cref="RangeSet{TRange,T}"/>'s, answers exactly
/// -1, 0 or 1.
/// </summary>
/// <remarks>
/// EF Core reads <c>CompareTo(x) == 1</c> as <c>&gt;</c> and <c>CompareTo(x) == -1</c> as <c>&lt;</c>, so
/// the SQL means "sorts after" where memory would mean "answered exactly 1". The two agree only if
/// the order never answers another magnitude, and an element type's own <c>CompareTo</c> may
/// (#256). Every type is discovered.
/// </remarks>
[TestClass]
public sealed class RangeOrderSignContractTests
{
    [TestMethod]
    public void EveryRangeAndRangeSet_ComparesAsMinusOneZeroOrOne()
    {
        var failures = RangeProbes.AllRangeTypes()
                                  .SelectMany(pair => Reflect.InvokeGeneric<List<string>>(
                                       typeof(RangeOrderSignContractTests), nameof(Magnitudes), pair.Range, pair.Element))
                                  .ToList();

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    private static List<string> Magnitudes<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (low, high) = ((T)RangeProbes.Bounds[typeof(T)].Low, (T)RangeProbes.Bounds[typeof(T)].High);
        TRange[] ranges =
        [
            .. RangeProbes.Shapes<TRange, T>(),
            TRange.CreateFinite(low, low, true, true),
            TRange.CreateFinite(high, high, true, true),
            TRange.CreateUnboundedEnd(high, true)
        ];

        var failures = new List<string>();
        foreach (var a in ranges)
        foreach (var b in ranges)
        {
            if (a.CompareTo(b) is var c and not (-1 or 0 or 1))
                failures.Add($"{typeof(TRange).Name}: {a} CompareTo {b} = {c}");

            var (setA, setB) = (RangeSet<TRange, T>.From([a]), RangeSet<TRange, T>.From([b]));
            if (setA.CompareTo(setB) is var s and not (-1 or 0 or 1))
                failures.Add($"RangeSet<{typeof(TRange).Name}>: {setA} CompareTo {setB} = {s}");
        }

        return failures;
    }
}

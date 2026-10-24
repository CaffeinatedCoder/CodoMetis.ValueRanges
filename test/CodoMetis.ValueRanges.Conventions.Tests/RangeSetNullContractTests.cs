using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// A range set holds no <see langword="null"/> range, and every way into one enforces that by
/// refusing, never by dropping or storing it, as <see cref="ValueSetNullContractTests"/> holds for
/// the value sets (#356).
/// </summary>
/// <remarks>
/// <para>
/// A null passed to <c>RangeSet.From</c> built a set whose one element was <see langword="null"/>:
/// <c>Count</c> was 1, <c>IsEmpty()</c> and <c>Equals(Empty)</c> answered <see langword="false"/>,
/// and the first <c>Contains</c> threw <see cref="NullReferenceException"/>. <c>Except</c> and
/// <c>Union</c> called on a null range reached it the same way. Dropping the null instead would make
/// it an empty range, which it is not, and report fewer ranges than the caller supplied.
/// </para>
/// <para>
/// The null is placed first, after a bounded range, after the empty range and after the infinite
/// one, which decides the result before the rest of the input is read. Every range type of both
/// libraries is discovered, so one added later is covered with no edit here.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeSetNullContractTests
{
    [TestMethod]
    public void EveryRangeType_RefusesANullRangeInEveryConstructionPath()
    {
        var covered = new List<string>();

        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
        {
            Reflect.InvokeGeneric(typeof(RangeSetNullContractTests), nameof(AssertNullIsRefused), rangeType, elementType);
            covered.Add(rangeType.Name);
        }

        Assert.IsGreaterThanOrEqualTo(13, covered.Count,
                                      $"Only {covered.Count} range types were discovered ({string.Join(", ", covered)}), "
                                    + "so this test covered fewer than the libraries ship.");
    }

    private static void AssertNullIsRefused<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        TRange missing = null!;
        var    finite  = RangeProbes.Shapes<TRange, T>()[2];
        var    name    = typeof(TRange).Name;

        TRange[][] inputs =
        [
            [missing],
            [missing, finite],
            [finite, missing],
            [TRange.Empty, missing],
            [TRange.Infinite, missing]
        ];

        foreach (var input in inputs)
        {
            var shown = $"[{string.Join(", ", input.Select(range => range?.ToString() ?? "null"))}]";

            Assert.ThrowsExactly<ArgumentException>(
                () => RangeSet<TRange, T>.From(input.AsEnumerable()),
                $"RangeSet<{name}>.From(IEnumerable) over {shown} accepted a null range.");

            Assert.ThrowsExactly<ArgumentException>(
                () => RangeSet<TRange, T>.From((ReadOnlySpan<TRange>)input),
                $"RangeSet<{name}>.From(span) over {shown} accepted a null range, though the IEnumerable "
              + "overload refuses it. The two construction paths must agree.");

            Assert.ThrowsExactly<ArgumentException>(
                () => RangeSet.Create<TRange, T>(input),
                $"RangeSet.Create<{name}> over {shown}, the builder behind a collection expression, accepted a "
              + "null range.");
        }

        // The extensions that build a set from the range they are called on. Throws, not
        // ThrowsExactly: #350 turns a null receiver into ArgumentNullException, which is one.
        Assert.Throws<ArgumentException>(
            () => RangeExtensions.Union<TRange, T>(missing, finite),
            $"{name}: Union on a null range built a set holding null.");

        Assert.Throws<ArgumentException>(
            () => RangeExtensions.Except<TRange, T>(missing, finite),
            $"{name}: Except on a null range built a set holding null.");
    }
}

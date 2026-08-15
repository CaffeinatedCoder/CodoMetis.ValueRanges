using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An <see cref="IComparer{TRange}"/> that orders ranges by their lower bound, matching the
/// internal sort used by <see cref="RangeSet{TRange,T}.From"/> to establish the sorted,
/// disjoint, non-adjacent invariant.
/// </summary>
/// <remarks>
/// <para>
/// Ordering rules (identical to <see cref="RangeSet{TRange,T}.LowerBoundComparer"/>):
/// </para>
/// <list type="bullet">
///   <item><description>
///   <see cref="IUnboundedStartRange{T}"/> (lower bound = -∞) sorts before every range
///   that has a finite lower bound.
///   </description></item>
///   <item><description>
///   At the same finite lower-bound value, an inclusive bound sorts before an exclusive
///   one — <c>[5, ...)</c> before <c>(5, ...)</c>.
///   </description></item>
///   <item><description>
///   <see cref="IEmptyRange{T}"/> and <see cref="IInfinityRange{T}"/> are not expected as
///   inputs: a <see cref="RangeSet{TRange,T}"/> never contains them as ordinary elements,
///   and pre-sorting inputs that still contain them is the caller's responsibility (they
///   are filtered out by <see cref="RangeSet{TRange,T}.From"/> before sorting).
///   </description></item>
/// </list>
/// <para>
/// Use this comparer to sort <see cref="List{TRange}"/>s the same way the set does, for
/// example to pre-sort inputs before handing them to <see cref="RangeSet{TRange,T}.From"/>,
/// or to compare two sequences of ranges structurally.
/// </para>
/// </remarks>
/// <typeparam name="TRange">The concrete range type to compare.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public sealed class RangeLowerBoundComparer<TRange, T> : IComparer<TRange>
    where TRange : IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>
    /// The singleton instance. Stateless — safe to reuse everywhere a
    /// <see cref="IComparer{TRange}"/> is accepted.
    /// </summary>
    public static RangeLowerBoundComparer<TRange, T> Instance { get; } = new();

    private RangeLowerBoundComparer()
    {
    }

    /// <summary>
    /// Compares two ranges by lower bound. Nulls sort first (consistent with
    /// <see cref="Comparer{T}.Default"/> conventions).
    /// </summary>
    /// <param name="x">The first range.</param>
    /// <param name="y">The second range.</param>
    /// <returns>
    /// A negative value if <paramref name="x"/> sorts before <paramref name="y"/>,
    /// zero if they sort equally, a positive value if <paramref name="x"/> sorts after.
    /// </returns>
    public int Compare(TRange? x, TRange? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return RangeSetHelpers.CompareByLowerBound<TRange, T>(x, y);
    }
}
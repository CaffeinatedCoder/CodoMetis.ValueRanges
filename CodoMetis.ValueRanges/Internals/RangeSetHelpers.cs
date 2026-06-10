using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

internal static class RangeSetHelpers
{
    // Total order on lower bounds across all non-empty, non-infinity shapes.
    // IUnboundedStartRange sorts first (its lower bound is -∞); all other shapes
    // carry a finite lower bound and sort by value, with an inclusive bound
    // sorting before an exclusive one at the same value ("[5, ..." starts
    // before "(5, ...").
    // IEmptyRange and IInfinityRange must be filtered out before sorting.
    internal static int CompareByLowerBound<TRange, T>(TRange a, TRange b)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        bool aUnboundedStart = a is IUnboundedStartRange<T>;
        bool bUnboundedStart = b is IUnboundedStartRange<T>;
        if (aUnboundedStart || bUnboundedStart)
            return (aUnboundedStart, bUnboundedStart) switch
                   {
                       (true, true) => 0,
                       (true, _)    => -1,
                       _            => 1
                   };

        var (aValue, aInclusive) = LowerBoundOf<T>(a);
        var (bValue, bInclusive) = LowerBoundOf<T>(b);

        int cmp = aValue.CompareTo(bValue);
        if (cmp != 0) return cmp;
        return (aInclusive, bInclusive) switch
               {
                   (true, false) => -1,
                   (false, true) => 1,
                   _             => 0
               };
    }

    private static (T Value, bool Inclusive) LowerBoundOf<T>(IRange<T> range)
        where T : struct, IComparable<T>, IEquatable<T> =>
        range switch
        {
            IFiniteRange<T> b       => (b.Start, b.StartInclusive),
            IUnboundedEndRange<T> e => (e.Start, e.StartInclusive),
            _                       => throw new InvalidOperationException(
                $"Range shape '{range.GetType().Name}' has no finite lower bound."
            )
        };
}

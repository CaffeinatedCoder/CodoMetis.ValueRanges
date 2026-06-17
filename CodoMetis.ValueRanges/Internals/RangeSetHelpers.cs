using System.Collections.Immutable;
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

        var (aValue, aInclusive) = FiniteLowerBoundOf<T>(a);
        var (bValue, bInclusive) = FiniteLowerBoundOf<T>(b);

        int cmp = aValue.CompareTo(bValue);
        if (cmp != 0) return cmp;
        return (aInclusive, bInclusive) switch
               {
                   (true, false) => -1,
                   (false, true) => 1,
                   _             => 0
               };
    }

    // Returns the lower bound of any non-empty range shape as (Value, Inclusive, Infinite).
    // IUnboundedStartRange is signaled by Infinite=true with a default Value (its lower bound is -∞).
    // IEmptyRange and IInfinityRange are not expected here — IInfinityRange is filtered out before
    // any operation that uses this helper (a set containing Infinity is the Infinite singleton).
    internal static (T Value, bool Inclusive, bool Infinite) RangeLowerBound<T>(IRange<T> range)
        where T : struct, IComparable<T>, IEquatable<T>
        => range switch
        {
            IUnboundedStartRange<T> => (default, false, true),
            IFiniteRange<T> f       => (f.Start, f.StartInclusive, false),
            IUnboundedEndRange<T> e => (e.Start, e.StartInclusive, false),
            _                       => throw new InvalidOperationException(
                $"Range shape '{range.GetType().Name}' has no lower bound.")
        };

    // Returns the upper bound of any non-empty range shape as (Value, Inclusive, Infinite).
    // IUnboundedEndRange is signaled by Infinite=true with a default Value (its upper bound is +∞).
    internal static (T Value, bool Inclusive, bool Infinite) RangeUpperBound<T>(IRange<T> range)
        where T : struct, IComparable<T>, IEquatable<T>
        => range switch
        {
            IUnboundedEndRange<T>   => (default, false, true),
            IFiniteRange<T> f       => (f.End, f.EndInclusive, false),
            IUnboundedStartRange<T> s => (s.End, s.EndInclusive, false),
            _                       => throw new InvalidOperationException(
                $"Range shape '{range.GetType().Name}' has no upper bound.")
        };

    // True if a's upper bound is strictly less than b's lower bound — a lies entirely to the
    // left of b with no shared point. At equal values, true iff at least one side is exclusive
    // at that point (so they don't share it). IUnboundedEndRange (upper = +∞) is never strictly
    // left of anything; IUnboundedStartRange (lower = -∞) has nothing strictly left of it.
    internal static bool IsStrictlyLeftOf<T>(IRange<T> a, IRange<T> b)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (_, _, aUpperInfinite) = RangeUpperBound<T>(a);
        var (_, _, bLowerInfinite) = RangeLowerBound<T>(b);
        if (aUpperInfinite || bLowerInfinite) return false;

        var (av, ai, _) = RangeUpperBound<T>(a);
        var (bv, bi, _) = RangeLowerBound<T>(b);
        int cmp = av.CompareTo(bv);
        if (cmp != 0) return cmp < 0;
        return !ai || !bi;
    }

    // True if a's upper bound is strictly less than b's upper bound (a ends before b).
    // Used by the Intersect merge-join to decide which pointer to advance: the element
    // that ends first can no longer overlap any element the other iterator will reach.
    // At equal finite values, an exclusive bound ends "earlier" than an inclusive one.
    internal static bool UpperBoundLessThan<T>(IRange<T> a, IRange<T> b)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (_, _, aInfinite) = RangeUpperBound<T>(a);
        var (_, _, bInfinite) = RangeUpperBound<T>(b);
        if (aInfinite) return false; // +∞ is not less than anything
        if (bInfinite) return true;  // finite < +∞

        var (av, ai, _) = RangeUpperBound<T>(a);
        var (bv, bi, _) = RangeUpperBound<T>(b);
        int cmp = av.CompareTo(bv);
        if (cmp != 0) return cmp < 0;
        return !ai && bi; // exclusive < inclusive at the same value
    }

    // Binary search: returns the index of the last element whose lower bound is at or below
    // `value` (≤ value), or -1 if every element's lower bound is strictly greater than `value`.
    // IUnboundedStartRange elements (lower bound = -∞) always satisfy the predicate, so they
    // are always candidates. `elements` must be sorted by lower bound (the RangeSet invariant).
    internal static int LastIndexWithLowerBoundAtOrBelow<TRange, T>(
        ImmutableArray<TRange> elements,
        T                      value)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int lo = 0, hi = elements.Length - 1, result = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (LowerBoundAtOrBelow(elements[mid], value))
            {
                result = mid;
                lo = mid + 1; // keep looking for a later candidate
            }
            else
            {
                hi = mid - 1;
            }
        }
        return result;
    }

    private static bool LowerBoundAtOrBelow<T>(IRange<T> range, T value)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (v, _, infinite) = RangeLowerBound<T>(range);
        return infinite || v.CompareTo(value) <= 0;
    }

    // Used only by CompareByLowerBound after IUnboundedStartRange has been handled separately.
    private static (T Value, bool Inclusive) FiniteLowerBoundOf<T>(IRange<T> range)
        where T : struct, IComparable<T>, IEquatable<T> =>
        range switch
        {
            IFiniteRange<T> b       => (b.Start, b.StartInclusive),
            IUnboundedEndRange<T> e => (e.Start, e.StartInclusive),
            _                       => throw new InvalidOperationException(
                $"Range shape '{range.GetType().Name}' has no finite lower bound.")
        };
}

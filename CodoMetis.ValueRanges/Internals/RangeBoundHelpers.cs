using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

internal static class RangeBoundHelpers
{
    // True when lhs-end and rhs-start touch and at least one is inclusive,
    // meaning the two sides share that boundary point.
    internal static bool TouchingBoundsOverlap<T>(T lhsEnd, bool lhsEndInclusive, T rhsStart, bool rhsStartInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = lhsEnd.CompareTo(rhsStart);
        return cmp > 0 || (cmp == 0 && lhsEndInclusive && rhsStartInclusive);
    }

    // True when outer-start is at or before inner-start in containment terms:
    // equal values are fine as long as outer is inclusive or inner is exclusive
    // (an exclusive outer cannot contain an inclusive inner at the same point).
    internal static bool OuterStartCoversInnerStart<T>(T outerStart, bool outerStartInclusive, T innerStart, bool innerStartInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = outerStart.CompareTo(innerStart);
        return cmp < 0 || (cmp == 0 && (outerStartInclusive || !innerStartInclusive));
    }

    // Symmetric for the end side.
    internal static bool OuterEndCoversInnerEnd<T>(T outerEnd, bool outerEndInclusive, T innerEnd, bool innerEndInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = innerEnd.CompareTo(outerEnd);
        return cmp < 0 || (cmp == 0 && (outerEndInclusive || !innerEndInclusive));
    }

    // True when leftEnd and rightStart form a valid adjacency boundary.
    // Two cases:
    //   1. Equal values with XOR inclusiveness — one side claims the point, the other doesn't.
    //   2. Discrete domains only: rightStart is the immediate successor of leftEnd and both
    //      bounds are inclusive — the one-step gap is exactly closed.
    //      Continuous domains return null from NextValueAfter and never hit this case.
    internal static bool BoundaryMeetsAdjacently<TRange, T>(
        T    leftEnd,
        bool leftEndInclusive,
        T    rightStart,
        bool rightStartInclusive
    ) where TRange : IRangeFactory<TRange, T>
      where T : struct, IComparable<T>, IEquatable<T> =>
        leftEnd.CompareTo(rightStart) switch
        {
            0 => leftEndInclusive != rightStartInclusive,
            < 0 when leftEndInclusive && rightStartInclusive =>
                TRange.NextValueAfter(leftEnd) is { } next && next.CompareTo(rightStart) == 0,
            _ => false
        };

    // Selects the later (more restrictive) of two start bounds for intersection.
    // At equal values the stricter inclusiveness wins: only inclusive if both are.
    internal static (T Value, bool Inclusive) LaterStart<T>(T aValue, bool aInclusive, T bValue, bool bInclusive)
        where T : struct, IComparable<T>, IEquatable<T> =>
        aValue.CompareTo(bValue) switch
        {
            > 0 => (aValue, aInclusive),
            < 0 => (bValue, bInclusive),
            _   => (aValue, aInclusive && bInclusive)
        };

    // Selects the earlier (more restrictive) of two end bounds for intersection.
    // At equal values the stricter inclusiveness wins: only inclusive if both are.
    internal static (T Value, bool Inclusive) EarlierEnd<T>(T aValue, bool aInclusive, T bValue, bool bInclusive)
        where T : struct, IComparable<T>, IEquatable<T> =>
        aValue.CompareTo(bValue) switch
        {
            < 0 => (aValue, aInclusive),
            > 0 => (bValue, bInclusive),
            _   => (aValue, aInclusive && bInclusive)
        };

    // Selects the earlier (more permissive) of two start bounds for merge/union.
    // At equal values the more inclusive flag wins: inclusive if either is.
    internal static (T Value, bool Inclusive) EarlierStart<T>(T aValue, bool aInclusive, T bValue, bool bInclusive)
        where T : struct, IComparable<T>, IEquatable<T> =>
        aValue.CompareTo(bValue) switch
        {
            < 0 => (aValue, aInclusive),
            > 0 => (bValue, bInclusive),
            _   => (aValue, aInclusive || bInclusive)
        };

    // Selects the later (more permissive) of two end bounds for merge/union.
    // At equal values the more inclusive flag wins: inclusive if either is.
    internal static (T Value, bool Inclusive) LaterEnd<T>(T aValue, bool aInclusive, T bValue, bool bInclusive)
        where T : struct, IComparable<T>, IEquatable<T> =>
        aValue.CompareTo(bValue) switch
        {
            > 0 => (aValue, aInclusive),
            < 0 => (bValue, bInclusive),
            _   => (aValue, aInclusive || bInclusive)
        };

    // Re-expresses source as a TRange with the same shape.
    // Used by IInfinityRange<T>.IntersectWith: ∞ ∩ X = X, re-expressed in the target type.
    internal static TRange RecreateAs<TRange, T>(IRange<T> source)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T> =>
        source switch
        {
            IInfinityRange<T>         => TRange.Infinite,
            IFiniteRange<T> f         => TRange.CreateFinite(f.Start, f.End, f.StartInclusive, f.EndInclusive),
            IUnboundedStartRange<T> s => TRange.CreateOpenStart(s.End, s.EndInclusive),
            IUnboundedEndRange<T> e   => TRange.CreateOpenEnd(e.Start, e.StartInclusive),
            _                         => TRange.Empty
        };
}
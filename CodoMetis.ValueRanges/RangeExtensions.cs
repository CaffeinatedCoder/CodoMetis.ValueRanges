namespace CodoMetis.ValueRanges;

public static class RangeExtensions
{
    // True when lhs-end and rhs-start touch and at least one is inclusive,
    // meaning the two sides share that boundary point.
    private static bool TouchingBoundsOverlap<T>(T lhsEnd, bool lhsEndInclusive, T rhsStart, bool rhsStartInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = lhsEnd.CompareTo(rhsStart);
        return cmp > 0 || (cmp == 0 && lhsEndInclusive && rhsStartInclusive);
    }

    // True when outer-start is at or before inner-start in containment terms:
    // equal values are fine as long as outer is inclusive or inner is exclusive
    // (an exclusive outer cannot contain an inclusive inner at the same point).
    private static bool OuterStartCoversInnerStart<T>(T outerStart, bool outerStartInclusive, T innerStart, bool innerStartInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = outerStart.CompareTo(innerStart);
        return cmp < 0 || (cmp == 0 && (outerStartInclusive || !innerStartInclusive));
    }

    // Symmetric for the end side.
    private static bool OuterEndCoversInnerEnd<T>(T outerEnd, bool outerEndInclusive, T innerEnd, bool innerEndInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = innerEnd.CompareTo(outerEnd);
        return cmp < 0 || (cmp == 0 && (outerEndInclusive || !innerEndInclusive));
    }

    // True when leftEnd and rightStart form a valid adjacency boundary.
    // Two cases:
    //   1. Equal values with XOR inclusiveness — one side claims the point, the other doesn't.
    //   2. Discrete only: leftEnd is exclusive and rightStart is inclusive at the very next
    //      discrete value — the gap of one step is exactly closed by the exclusiveness.
    //      Any other inclusiveness combination at step-apart values produces either a gap
    //      (both exclusive, or leftEnd inclusive) or cannot arise (rightStart exclusive means
    //      the next value is also unclaimed).
    private static bool BoundaryMeetsAdjacently<T>(
        T                  leftEnd,
        bool               leftEndInclusive,
        T                  rightStart,
        bool               rightStartInclusive,
        IDiscreteRange<T>? discrete
    ) where T : struct, IComparable<T>, IEquatable<T> =>
        leftEnd.CompareTo(rightStart) switch
        {
            0 => leftEndInclusive != rightStartInclusive,
            < 0 when discrete is not null && leftEndInclusive && rightStartInclusive =>
                discrete.GetNextValueFor(leftEnd).CompareTo(rightStart) == 0,
            _ => false
        };

    // Selects the later (more restrictive) of two start bounds for intersection.
    // At equal values the stricter inclusiveness wins: only inclusive if both are.
    private static (T Value, bool Inclusive) LaterStart<T>(T aValue, bool aInclusive, T bValue, bool bInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = aValue.CompareTo(bValue);
        if (cmp > 0) return (aValue, aInclusive);
        if (cmp < 0) return (bValue, bInclusive);
        return (aValue, aInclusive && bInclusive);
    }

    // Selects the earlier (more restrictive) of two end bounds for intersection.
    // At equal values the stricter inclusiveness wins: only inclusive if both are.
    private static (T Value, bool Inclusive) EarlierEnd<T>(T aValue, bool aInclusive, T bValue, bool bInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = aValue.CompareTo(bValue);
        if (cmp < 0) return (aValue, aInclusive);
        if (cmp > 0) return (bValue, bInclusive);
        return (aValue, aInclusive && bInclusive);
    }

    // Selects the earlier (more permissive) of two start bounds for merge/union.
    // At equal values the more inclusive flag wins: inclusive if either is.
    private static (T Value, bool Inclusive) EarlierStart<T>(T aValue, bool aInclusive, T bValue, bool bInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = aValue.CompareTo(bValue);
        if (cmp < 0) return (aValue, aInclusive);
        if (cmp > 0) return (bValue, bInclusive);
        return (aValue, aInclusive || bInclusive);
    }

    // Selects the later (more permissive) of two end bounds for merge/union.
    // At equal values the more inclusive flag wins: inclusive if either is.
    private static (T Value, bool Inclusive) LaterEnd<T>(T aValue, bool aInclusive, T bValue, bool bInclusive)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        int cmp = aValue.CompareTo(bValue);
        if (cmp > 0) return (aValue, aInclusive);
        if (cmp < 0) return (bValue, bInclusive);
        return (aValue, aInclusive || bInclusive);
    }

    extension<T>(IRange<T> range) where T : struct, IComparable<T>, IEquatable<T>
    {
        public bool Contains(T value) =>
            range switch
            {
                IFiniteRange<T> b =>
                    (b.LowerBoundInclusive ? b.LowerBound.CompareTo(value) <= 0 : b.LowerBound.CompareTo(value) < 0) &&
                    (b.UpperBoundInclusive ? value.CompareTo(b.UpperBound) <= 0 : value.CompareTo(b.UpperBound) < 0),
                IOpenStartRange<T> s =>
                    s.UpperBoundInclusive ? value.CompareTo(s.UpperBound) <= 0 : value.CompareTo(s.UpperBound) < 0,
                IOpenEndRange<T> e =>
                    e.LowerBoundInclusive ? e.LowerBound.CompareTo(value) <= 0 : e.LowerBound.CompareTo(value) < 0,
                _ => false
            };

        public bool Contains(IRange<T> other) =>
            range switch
            {
                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            OuterStartCoversInnerStart(b.LowerBound, b.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive) &&
                            OuterEndCoversInnerEnd(b.UpperBound, b.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive),
                        _ => false
                    },

                // (-∞, s.End] or (-∞, s.End): no lower constraint — only the upper bound matters.
                // An IOpenEndRange inner goes to +∞ and can never be contained.
                IOpenStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            OuterEndCoversInnerEnd(s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive),
                        IOpenStartRange<T> o =>
                            OuterEndCoversInnerEnd(s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive),
                        _ => false
                    },

                // [e.Start, +∞) or (e.Start, +∞): no upper constraint — only the lower bound matters.
                // An IOpenStartRange inner goes to -∞ and can never be contained.
                IOpenEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            OuterStartCoversInnerStart(e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive),
                        IOpenEndRange<T> o =>
                            OuterStartCoversInnerStart(e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive),
                        _ => false
                    },

                _ => false
            };

        public bool Overlaps(IRange<T> other) =>
            range switch
            {
                IEmptyRange<T> => false,

                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            TouchingBoundsOverlap(b.UpperBound, b.UpperBoundInclusive, o.LowerBound, o.LowerBoundInclusive) &&
                            TouchingBoundsOverlap(o.UpperBound, o.UpperBoundInclusive, b.LowerBound, b.LowerBoundInclusive),
                        IOpenStartRange<T> s =>
                            TouchingBoundsOverlap(s.UpperBound, s.UpperBoundInclusive, b.LowerBound, b.LowerBoundInclusive),
                        IOpenEndRange<T> e =>
                            TouchingBoundsOverlap(b.UpperBound, b.UpperBoundInclusive, e.LowerBound, e.LowerBoundInclusive),
                        _ => false
                    },

                IOpenStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            TouchingBoundsOverlap(s.UpperBound, s.UpperBoundInclusive, o.LowerBound, o.LowerBoundInclusive),
                        IOpenEndRange<T> e =>
                            TouchingBoundsOverlap(s.UpperBound, s.UpperBoundInclusive, e.LowerBound, e.LowerBoundInclusive),
                        IOpenStartRange<T> => true,
                        _                  => false
                    },

                IOpenEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            TouchingBoundsOverlap(o.UpperBound, o.UpperBoundInclusive, e.LowerBound, e.LowerBoundInclusive),
                        IOpenStartRange<T> s =>
                            TouchingBoundsOverlap(s.UpperBound, s.UpperBoundInclusive, e.LowerBound, e.LowerBoundInclusive),
                        IOpenEndRange<T> => true,
                        _                => false
                    },

                _ => false
            };

        // range lies entirely to the left of other with no shared point.
        // At a touching boundary: strictly left iff not (both inclusive).
        public bool IsStrictlyLeftOf(IRange<T> other) =>
            range switch
            {
                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            b.UpperBound.CompareTo(o.LowerBound) < 0 ||
                            (b.UpperBound.CompareTo(o.LowerBound) == 0 && !(b.UpperBoundInclusive && o.LowerBoundInclusive)),
                        IOpenEndRange<T> e =>
                            b.UpperBound.CompareTo(e.LowerBound) < 0 ||
                            (b.UpperBound.CompareTo(e.LowerBound) == 0 && !(b.UpperBoundInclusive && e.LowerBoundInclusive)),
                        _ => false
                    },
                _ => false
            };

        public bool IsStrictlyRightOf(IRange<T> other) => other.IsStrictlyLeftOf(range);

        public bool IsContainedBy(IRange<T> other) => other.Contains(range);

        // range does not extend to the right of other (&< in Postgres).
        // The receiver's upper bound must be ≤ other's upper bound.
        // An IOpenEndRange has no upper bound, so it always extends right — returns false.
        // An IOpenStartRange has no upper bound constraint from the type, but its UpperBound
        // field is the finite cap, so we compare it against other's upper bound normally.
        public bool DoesNotExtendRightOf(IRange<T> other) =>
            range switch
            {
                IOpenEndRange<T> => false,

                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            b.UpperBound.CompareTo(o.UpperBound) < 0 ||
                            (b.UpperBound.CompareTo(o.UpperBound) == 0 && (!b.UpperBoundInclusive || o.UpperBoundInclusive)),
                        IOpenStartRange<T> s =>
                            b.UpperBound.CompareTo(s.UpperBound) < 0 ||
                            (b.UpperBound.CompareTo(s.UpperBound) == 0 && (!b.UpperBoundInclusive || s.UpperBoundInclusive)),
                        IOpenEndRange<T> => true,
                        _                => false
                    },

                IOpenStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            s.UpperBound.CompareTo(o.UpperBound) < 0 ||
                            (s.UpperBound.CompareTo(o.UpperBound) == 0 && (!s.UpperBoundInclusive || o.UpperBoundInclusive)),
                        IOpenStartRange<T> o =>
                            s.UpperBound.CompareTo(o.UpperBound) < 0 ||
                            (s.UpperBound.CompareTo(o.UpperBound) == 0 && (!s.UpperBoundInclusive || o.UpperBoundInclusive)),
                        IOpenEndRange<T> => true,
                        _                => false
                    },

                _ => false
            };

        // range does not extend to the left of other (&> in Postgres).
        // The receiver's lower bound must be ≥ other's lower bound.
        // An IOpenStartRange has no lower bound, so it always extends left — returns false.
        public bool DoesNotExtendLeftOf(IRange<T> other) =>
            range switch
            {
                IOpenStartRange<T> => false,

                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            b.LowerBound.CompareTo(o.LowerBound) > 0 ||
                            (b.LowerBound.CompareTo(o.LowerBound) == 0 && (!b.LowerBoundInclusive || o.LowerBoundInclusive)),
                        IOpenEndRange<T> e =>
                            b.LowerBound.CompareTo(e.LowerBound) > 0 ||
                            (b.LowerBound.CompareTo(e.LowerBound) == 0 && (!b.LowerBoundInclusive || e.LowerBoundInclusive)),
                        IOpenStartRange<T> => true,
                        _                  => false
                    },

                IOpenEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            e.LowerBound.CompareTo(o.LowerBound) > 0 ||
                            (e.LowerBound.CompareTo(o.LowerBound) == 0 && (!e.LowerBoundInclusive || o.LowerBoundInclusive)),
                        IOpenEndRange<T> o =>
                            e.LowerBound.CompareTo(o.LowerBound) > 0 ||
                            (e.LowerBound.CompareTo(o.LowerBound) == 0 && (!e.LowerBoundInclusive || o.LowerBoundInclusive)),
                        IOpenStartRange<T> => true,
                        _                  => false
                    },

                _ => false
            };

        // Adjacent means no gap and no overlap between the ranges — their union
        // would form a single contiguous range. Exactly one of the two possible
        // boundary meetings (b.End→o.Start or o.End→b.Start) must hold.
        public bool IsAdjacentTo(IRange<T> other)
        {
            var discrete = range as IDiscreteRange<T>;

            return range switch
                   {
                       IFiniteRange<T> b =>
                           other switch
                           {
                               IFiniteRange<T> o =>
                                   BoundaryMeetsAdjacently(
                                       b.UpperBound, b.UpperBoundInclusive, o.LowerBound, o.LowerBoundInclusive, discrete
                                   ) ||
                                   BoundaryMeetsAdjacently(
                                       o.UpperBound, o.UpperBoundInclusive, b.LowerBound, b.LowerBoundInclusive, discrete
                                   ),

                               // s extends left from s.End; b's start must meet s.End
                               IOpenStartRange<T> s =>
                                   BoundaryMeetsAdjacently(
                                       s.UpperBound, s.UpperBoundInclusive, b.LowerBound, b.LowerBoundInclusive, discrete
                                   ),

                               // e extends right from e.Start; b's end must meet e.Start
                               IOpenEndRange<T> e =>
                                   BoundaryMeetsAdjacently(
                                       b.UpperBound, b.UpperBoundInclusive, e.LowerBound, e.LowerBoundInclusive, discrete
                                   ),

                               _ => false
                           },

                       _ => false
                   };
        }
    }

    // -------------------------------------------------------------------------
    // Set operation extensions
    // -------------------------------------------------------------------------

    extension<TRange, T>(TRange range)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        // Returns the largest range contained by both ranges.
        // Returns null when the ranges do not overlap.
        public TRange? Intersect(IRange<T> other)
        {
            if (!range.Overlaps(other)) return default;

            return (range, other) switch
                   {
                       // Both finite — select the later start and earlier end
                       (IFiniteRange<T> b, IFiniteRange<T> o) =>
                           TRange.Closed(
                               lowerBound: LaterStart(b.LowerBound, b.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive)
                                               is var (lv, li)
                                               ? lv
                                               : default,
                               upperBound: EarlierEnd(b.UpperBound, b.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive)
                                               is var (uv, ui)
                                               ? uv
                                               : default,
                               lowerBoundInclusive: LaterStart(
                                   b.LowerBound, b.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Inclusive,
                               upperBoundInclusive: EarlierEnd(
                                   b.UpperBound, b.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // OpenStart ∩ OpenStart — result is OpenStart at the earlier (more restrictive) end
                       (IOpenStartRange<T> s, IOpenStartRange<T> o) =>
                           TRange.WithOpenStart(
                               upperBound: EarlierEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Value,
                               upperBoundInclusive: EarlierEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // OpenEnd ∩ OpenEnd — result is OpenEnd at the later (more restrictive) start
                       (IOpenEndRange<T> e, IOpenEndRange<T> o) =>
                           TRange.WithOpenEnd(
                               lowerBound: LaterStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Value,
                               lowerBoundInclusive: LaterStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Inclusive
                           ),

                       // OpenStart ∩ Finite — the finite end is always more restrictive on the right;
                       // the finite start is more restrictive on the left since OpenStart has no lower bound
                       (IOpenStartRange<T> s, IFiniteRange<T> o) =>
                           TRange.Closed(
                               lowerBound: o.LowerBound,
                               upperBound: EarlierEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Value,
                               lowerBoundInclusive: o.LowerBoundInclusive,
                               upperBoundInclusive: EarlierEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // Finite ∩ OpenStart — symmetric
                       (IFiniteRange<T> b, IOpenStartRange<T> s) =>
                           TRange.Closed(
                               lowerBound: b.LowerBound,
                               upperBound: EarlierEnd(
                                   b.UpperBound, b.UpperBoundInclusive, s.UpperBound, s.UpperBoundInclusive
                               ).Value,
                               lowerBoundInclusive: b.LowerBoundInclusive,
                               upperBoundInclusive: EarlierEnd(
                                   b.UpperBound, b.UpperBoundInclusive, s.UpperBound, s.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // OpenEnd ∩ Finite — symmetric reasoning
                       (IOpenEndRange<T> e, IFiniteRange<T> o) =>
                           TRange.Closed(
                               lowerBound: LaterStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Value,
                               upperBound: o.UpperBound,
                               lowerBoundInclusive: LaterStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Inclusive,
                               upperBoundInclusive: o.UpperBoundInclusive),

                       // Finite ∩ OpenEnd — symmetric
                       (IFiniteRange<T> b, IOpenEndRange<T> e) =>
                           TRange.Closed(
                               lowerBound: LaterStart(
                                   b.LowerBound, b.LowerBoundInclusive, e.LowerBound, e.LowerBoundInclusive
                               ).Value,
                               upperBound: b.UpperBound,
                               lowerBoundInclusive: LaterStart(
                                   b.LowerBound, b.LowerBoundInclusive, e.LowerBound, e.LowerBoundInclusive
                               ).Inclusive,
                               upperBoundInclusive: b.UpperBoundInclusive),

                       // OpenStart ∩ OpenEnd — overlapping region is finite; verified by Overlaps above
                       (IOpenStartRange<T> s, IOpenEndRange<T> e) =>
                           TRange.Closed(
                               lowerBound: e.LowerBound,
                               upperBound: s.UpperBound,
                               lowerBoundInclusive: e.LowerBoundInclusive,
                               upperBoundInclusive: s.UpperBoundInclusive),

                       // OpenEnd ∩ OpenStart — symmetric
                       (IOpenEndRange<T> e, IOpenStartRange<T> s) =>
                           TRange.Closed(
                               lowerBound: e.LowerBound,
                               upperBound: s.UpperBound,
                               lowerBoundInclusive: e.LowerBoundInclusive,
                               upperBoundInclusive: s.UpperBoundInclusive),

                       _ => default
                   };
        }

        // Returns the smallest range that contains both ranges.
        // Returns null when the ranges are neither overlapping nor adjacent —
        // their union would not form a single contiguous range.
        public TRange? Merge(IRange<T> other)
        {
            if (!range.Overlaps(other) && !range.IsAdjacentTo(other))
                return default;

            return (range, other) switch
                   {
                       (IFiniteRange<T> b, IFiniteRange<T> o) =>
                           TRange.Closed(
                               lowerBound: EarlierStart(
                                   b.LowerBound, b.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Value,
                               upperBound: LaterEnd(
                                   b.UpperBound, b.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Value,
                               lowerBoundInclusive: EarlierStart(
                                   b.LowerBound, b.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Inclusive,
                               upperBoundInclusive: LaterEnd(
                                   b.UpperBound, b.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // OpenStart absorbs any finite lower bound — result stays OpenStart at the later end
                       (IOpenStartRange<T> s, IFiniteRange<T> o) =>
                           TRange.WithOpenStart(
                               upperBound: LaterEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Value,
                               upperBoundInclusive: LaterEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       (IFiniteRange<T> b, IOpenStartRange<T> s) =>
                           TRange.WithOpenStart(
                               upperBound: LaterEnd(
                                   b.UpperBound, b.UpperBoundInclusive, s.UpperBound, s.UpperBoundInclusive
                               ).Value,
                               upperBoundInclusive: LaterEnd(
                                   b.UpperBound, b.UpperBoundInclusive, s.UpperBound, s.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // OpenEnd absorbs any finite upper bound — result stays OpenEnd at the earlier start
                       (IOpenEndRange<T> e, IFiniteRange<T> o) =>
                           TRange.WithOpenEnd(
                               lowerBound: EarlierStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Value,
                               lowerBoundInclusive: EarlierStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Inclusive
                           ),

                       (IFiniteRange<T> b, IOpenEndRange<T> e) =>
                           TRange.WithOpenEnd(
                               lowerBound: EarlierStart(
                                   b.LowerBound, b.LowerBoundInclusive, e.LowerBound, e.LowerBoundInclusive
                               ).Value,
                               lowerBoundInclusive: EarlierStart(
                                   b.LowerBound, b.LowerBoundInclusive, e.LowerBound, e.LowerBoundInclusive
                               ).Inclusive
                           ),

                       // Two OpenStart ranges — result is OpenStart at the later end
                       (IOpenStartRange<T> s, IOpenStartRange<T> o) =>
                           TRange.WithOpenStart(
                               upperBound: LaterEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Value,
                               upperBoundInclusive: LaterEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // Two OpenEnd ranges — result is OpenEnd at the earlier start
                       (IOpenEndRange<T> e, IOpenEndRange<T> o) =>
                           TRange.WithOpenEnd(
                               lowerBound: EarlierStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Value,
                               lowerBoundInclusive: EarlierStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Inclusive
                           ),

                       // OpenStart + OpenEnd (or reverse) — together they cover the entire domain
                       // represented as OpenStart at +∞ end and OpenEnd at -∞ start, but we have no
                       // way to express an unbounded range on both sides in this type system.
                       // Return null — callers should check for this case explicitly if needed.
                       (IOpenStartRange<T>, IOpenEndRange<T>) => default,
                       (IOpenEndRange<T>, IOpenStartRange<T>) => default,

                       _ => default
                   };
        }

        // Union is identical to Merge in semantics. Provided as a named alias
        // matching Npgsql's surface for callers who prefer the set-operation vocabulary.
        public TRange? Union(IRange<T> other) =>
            range.Merge(other);

        // Returns what remains of range after removing the overlap with other.
        //
        // Return value semantics:
        //   null                  — range is fully contained by other; nothing remains
        //   (remainder, null)     — one-sided trim or no overlap; Left is the remaining range
        //   (left, right)         — other was strictly interior to range; range is split in two
        //
        // The boundary flip on trim: when other removes the left side of range, the new lower
        // bound is other's upper bound with flipped inclusiveness — the point that other ends
        // at is no longer covered by other, so range now starts there with the opposite flag.
        public (TRange Left, TRange? Right)? Except(IRange<T> other)
        {
            // No overlap — range is entirely unaffected
            if (!range.Overlaps(other))
                return (range, default);

            // other fully contains range — nothing remains
            if (other.Contains(range))
                return null;

            return (range, other) switch
                   {
                       (IFiniteRange<T> b, IFiniteRange<T> o) =>
                           // Interior split: other sits strictly inside range
                           OuterStartCoversInnerStart(b.LowerBound, b.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive) &&
                           OuterEndCoversInnerEnd(b.UpperBound, b.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive)
                               ? (TRange.Closed(b.LowerBound, o.LowerBound, b.LowerBoundInclusive, !o.LowerBoundInclusive),
                                  (TRange?)TRange.Closed(o.UpperBound, b.UpperBound, !o.UpperBoundInclusive, b.UpperBoundInclusive))
                               // Left-side overlap: other covers the start of range
                               : OuterStartCoversInnerStart(o.LowerBound, o.LowerBoundInclusive, b.LowerBound, b.LowerBoundInclusive)
                                   ? (TRange.Closed(o.UpperBound, b.UpperBound, !o.UpperBoundInclusive, b.UpperBoundInclusive),
                                      (TRange?)default)
                                   // Right-side overlap: other covers the end of range
                                   : (TRange.Closed(b.LowerBound, o.LowerBound, b.LowerBoundInclusive, !o.LowerBoundInclusive),
                                      (TRange?)default),

                       // OpenStart split by Finite interior: left part stays OpenStart, right part is Finite  
                       (IOpenStartRange<T> s, IFiniteRange<T> o)
                           when OuterEndCoversInnerEnd(s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive) =>
                           (TRange.WithOpenStart(o.LowerBound, !o.LowerBoundInclusive),
                            (TRange?)TRange.Closed(o.UpperBound, s.UpperBound, !o.UpperBoundInclusive, s.UpperBoundInclusive)),

                       // OpenStart trimmed by Finite from the right — result is OpenStart at a new end
                       (IOpenStartRange<T> s, IFiniteRange<T> o) =>
                           TRange.WithOpenStart(o.LowerBound, !o.LowerBoundInclusive) is var left
                               ? (left, default)
                               : default,

                       // OpenEnd split by Finite interior: left part is Finite, right part stays OpenEnd
                       (IOpenEndRange<T> e, IFiniteRange<T> o)
                           when OuterStartCoversInnerStart(e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive) =>
                           (TRange.Closed(e.LowerBound, o.LowerBound, e.LowerBoundInclusive, !o.LowerBoundInclusive),
                            (TRange?)TRange.WithOpenEnd(o.UpperBound, !o.UpperBoundInclusive)),

                       // OpenEnd trimmed by Finite from the left — result is OpenEnd at a new start
                       (IOpenEndRange<T> e, IFiniteRange<T> o) =>
                           TRange.WithOpenEnd(o.UpperBound, !o.UpperBoundInclusive) is var left
                               ? (left, default)
                               : default,

                       _ => (range, default)
                   };
        }
    }
}
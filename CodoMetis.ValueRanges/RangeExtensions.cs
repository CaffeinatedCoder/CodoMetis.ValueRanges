namespace CodoMetis.ValueRanges;

/// <summary>
/// Extension methods providing query and set operations on <see cref="IRange{T}"/> instances.
/// </summary>
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

    // Recreates source as a TRange by dispatching on its shape.
    // Used when Infinity.Intersect(other) needs to re-express other in the target range type.
    private static TRange RecreateAs<TRange, T>(IRange<T> source)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T> =>
        source switch
        {
            IInfinityRange<T>    => TRange.Infinite,
            IFiniteRange<T> f    => TRange.CreateFinite(f.LowerBound, f.UpperBound, f.LowerBoundInclusive, f.UpperBoundInclusive),
            IOpenStartRange<T> s => TRange.CreateOpenStart(s.UpperBound, s.UpperBoundInclusive),
            IOpenEndRange<T> e   => TRange.CreateOpenEnd(e.LowerBound, e.LowerBoundInclusive),
            _                    => TRange.Empty
        };

    extension<T>(IRange<T> range) where T : struct, IComparable<T>, IEquatable<T>
    {
        /// <summary>
        /// Determines whether <paramref name="value"/> is contained in the range.
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="value"/> satisfies the range's boundary conditions;
        /// <see langword="false"/> for the empty range or when the value lies outside the bounds.
        /// Always <see langword="true"/> for <see cref="IInfinityRange{T}"/>.
        /// </returns>
        public bool Contains(T value) =>
            range switch
            {
                IInfinityRange<T> => true,
                IFiniteRange<T> b =>
                    (b.LowerBoundInclusive ? b.LowerBound.CompareTo(value) <= 0 : b.LowerBound.CompareTo(value) < 0) &&
                    (b.UpperBoundInclusive ? value.CompareTo(b.UpperBound) <= 0 : value.CompareTo(b.UpperBound) < 0),
                IOpenStartRange<T> s =>
                    s.UpperBoundInclusive ? value.CompareTo(s.UpperBound) <= 0 : value.CompareTo(s.UpperBound) < 0,
                IOpenEndRange<T> e =>
                    e.LowerBoundInclusive ? e.LowerBound.CompareTo(value) <= 0 : e.LowerBound.CompareTo(value) < 0,
                _ => false
            };

        /// <summary>
        /// Determines whether <paramref name="other"/> is entirely contained within this range.
        /// </summary>
        /// <param name="other">The range to test.</param>
        /// <returns>
        /// <see langword="true"/> if every value in <paramref name="other"/> also belongs to this range.
        /// Always <see langword="true"/> for <see cref="IInfinityRange{T}"/> (unless <paramref name="other"/>
        /// is <see cref="IEmptyRange{T}"/>).
        /// Always <see langword="false"/> when <paramref name="other"/> extends in a direction that
        /// this range does not bound, or when this range is <see cref="IEmptyRange{T}"/>.
        /// </returns>
        public bool Contains(IRange<T> other) =>
            range switch
            {
                IInfinityRange<T> => other is not IEmptyRange<T>,

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

        /// <summary>
        /// Determines whether this range and <paramref name="other"/> share at least one common value.
        /// </summary>
        /// <param name="other">The range to test against.</param>
        /// <returns>
        /// <see langword="true"/> if the ranges overlap.
        /// <see langword="false"/> if either range is <see cref="IEmptyRange{T}"/> or the ranges are disjoint.
        /// Two ranges that touch at a single boundary point overlap only when both are inclusive at that point.
        /// <see cref="IInfinityRange{T}"/> overlaps with every non-empty range.
        /// </returns>
        public bool Overlaps(IRange<T> other) =>
            range switch
            {
                IEmptyRange<T> => false,

                IInfinityRange<T> => other is not IEmptyRange<T>,

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
                        IInfinityRange<T> => true,
                        _                 => false
                    },

                IOpenStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            TouchingBoundsOverlap(s.UpperBound, s.UpperBoundInclusive, o.LowerBound, o.LowerBoundInclusive),
                        IOpenEndRange<T> e =>
                            TouchingBoundsOverlap(s.UpperBound, s.UpperBoundInclusive, e.LowerBound, e.LowerBoundInclusive),
                        IOpenStartRange<T> => true,
                        IInfinityRange<T>  => true,
                        _                  => false
                    },

                IOpenEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            TouchingBoundsOverlap(o.UpperBound, o.UpperBoundInclusive, e.LowerBound, e.LowerBoundInclusive),
                        IOpenStartRange<T> s =>
                            TouchingBoundsOverlap(s.UpperBound, s.UpperBoundInclusive, e.LowerBound, e.LowerBoundInclusive),
                        IOpenEndRange<T>  => true,
                        IInfinityRange<T> => true,
                        _                 => false
                    },

                _ => false
            };

        /// <summary>
        /// Determines whether this range ends strictly before <paramref name="other"/> begins,
        /// with no shared point between them.
        /// </summary>
        /// <param name="other">The range to compare against.</param>
        /// <returns>
        /// <see langword="true"/> if the upper bound of this range is less than the lower bound of
        /// <paramref name="other"/>, or if they meet at a single point but at least one side is exclusive there.
        /// Always <see langword="false"/> when this range is <see cref="IOpenEndRange{T}"/>,
        /// <see cref="IOpenStartRange{T}"/>, <see cref="IInfinityRange{T}"/>, or <see cref="IEmptyRange{T}"/>.
        /// </returns>
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

        /// <summary>
        /// Determines whether this range begins strictly after <paramref name="other"/> ends,
        /// with no shared point between them.
        /// Equivalent to <c>other.IsStrictlyLeftOf(this)</c>.
        /// </summary>
        /// <param name="other">The range to compare against.</param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="other"/> is strictly left of this range.
        /// </returns>
        public bool IsStrictlyRightOf(IRange<T> other) => other.IsStrictlyLeftOf(range);

        /// <summary>
        /// Determines whether this range is entirely contained within <paramref name="other"/>.
        /// Equivalent to <c>other.Contains(this)</c>.
        /// </summary>
        /// <param name="other">The range that should contain this range.</param>
        /// <returns>
        /// <see langword="true"/> if every value in this range also belongs to <paramref name="other"/>.
        /// </returns>
        public bool IsContainedBy(IRange<T> other) => other.Contains(range);

        /// <summary>
        /// Determines whether this range does not extend to the right of <paramref name="other"/>.
        /// Corresponds to the PostgreSQL <c>&amp;&lt;</c> operator.
        /// </summary>
        /// <remarks>
        /// The upper bound of this range must be less than or equal to the upper bound of
        /// <paramref name="other"/>. When the upper bounds are equal, this range must not be inclusive
        /// where <paramref name="other"/> is exclusive at that point.
        /// An <see cref="IOpenEndRange{T}"/> or <see cref="IInfinityRange{T}"/> has no finite upper bound
        /// and always returns <see langword="false"/>.
        /// </remarks>
        /// <param name="other">The range to compare against.</param>
        /// <returns>
        /// <see langword="true"/> if the upper bound of this range does not exceed that of <paramref name="other"/>;
        /// always <see langword="false"/> for <see cref="IOpenEndRange{T}"/> or <see cref="IInfinityRange{T}"/>.
        /// </returns>
        public bool DoesNotExtendRightOf(IRange<T> other) =>
            range switch
            {
                IOpenEndRange<T>  => false,
                IInfinityRange<T> => false,

                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            b.UpperBound.CompareTo(o.UpperBound) < 0 ||
                            (b.UpperBound.CompareTo(o.UpperBound) == 0 && (!b.UpperBoundInclusive || o.UpperBoundInclusive)),
                        IOpenStartRange<T> s =>
                            b.UpperBound.CompareTo(s.UpperBound) < 0 ||
                            (b.UpperBound.CompareTo(s.UpperBound) == 0 && (!b.UpperBoundInclusive || s.UpperBoundInclusive)),
                        IOpenEndRange<T>  => true,
                        IInfinityRange<T> => true,
                        _                 => false
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
                        IOpenEndRange<T>  => true,
                        IInfinityRange<T> => true,
                        _                 => false
                    },

                _ => false
            };

        /// <summary>
        /// Determines whether this range does not extend to the left of <paramref name="other"/>.
        /// Corresponds to the PostgreSQL <c>&amp;&gt;</c> operator.
        /// </summary>
        /// <remarks>
        /// The lower bound of this range must be greater than or equal to the lower bound of
        /// <paramref name="other"/>. When the lower bounds are equal, this range must not be inclusive
        /// where <paramref name="other"/> is exclusive at that point.
        /// An <see cref="IOpenStartRange{T}"/> or <see cref="IInfinityRange{T}"/> has no finite lower bound
        /// and always returns <see langword="false"/>.
        /// </remarks>
        /// <param name="other">The range to compare against.</param>
        /// <returns>
        /// <see langword="true"/> if the lower bound of this range is not less than that of <paramref name="other"/>;
        /// always <see langword="false"/> for <see cref="IOpenStartRange{T}"/> or <see cref="IInfinityRange{T}"/>.
        /// </returns>
        public bool DoesNotExtendLeftOf(IRange<T> other) =>
            range switch
            {
                IOpenStartRange<T> => false,
                IInfinityRange<T>  => false,

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
                        IInfinityRange<T>  => true,
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
                        IInfinityRange<T>  => true,
                        _                  => false
                    },

                _ => false
            };

        /// <summary>
        /// Determines whether this range and <paramref name="other"/> are contiguous —
        /// no gap and no overlap — such that their union forms a single range.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For discrete types (<see cref="IDiscreteRange{T}"/>), two ranges whose boundaries are
        /// exactly one step apart are also considered adjacent. For example, <c>[1, 5]</c> and
        /// <c>[6, 10]</c> are adjacent for <see cref="int"/>.
        /// </para>
        /// <para>
        /// For continuous types, adjacency requires the ranges to touch at exactly one point with
        /// complementary inclusiveness: one side must claim the boundary point and the other must not.
        /// </para>
        /// <para>
        /// Only <see cref="IFiniteRange{T}"/> instances can be adjacent to other ranges;
        /// <see cref="IOpenEndRange{T}"/>, <see cref="IOpenStartRange{T}"/>, and
        /// <see cref="IInfinityRange{T}"/> always return <see langword="false"/>.
        /// </para>
        /// </remarks>
        /// <param name="other">The range to test against.</param>
        /// <returns>
        /// <see langword="true"/> if the ranges are contiguous with no gap and no overlap.
        /// </returns>
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
        /// <summary>
        /// Returns the largest range contained by both this range and <paramref name="other"/>.
        /// </summary>
        /// <remarks>
        /// All combinations of <see cref="IFiniteRange{T}"/>, <see cref="IOpenStartRange{T}"/>,
        /// <see cref="IOpenEndRange{T}"/>, and <see cref="IInfinityRange{T}"/> are handled and produce
        /// the appropriately shaped result type. For example, intersecting an <see cref="IInfinityRange{T}"/>
        /// with any range returns that range unchanged.
        /// </remarks>
        /// <param name="other">The range to intersect with.</param>
        /// <returns>
        /// The intersection of this range and <paramref name="other"/>,
        /// or <see cref="IRangeFactory{TRange,T}.Empty"/> if the ranges do not overlap.
        /// </returns>
        public TRange Intersect(IRange<T> other)
        {
            if (!range.Overlaps(other)) return TRange.Empty;

            // Infinity ∩ X = X; X ∩ Infinity = X
            if (range is IInfinityRange<T>) return RecreateAs<TRange, T>(other);
            if (other is IInfinityRange<T>) return range;

            return (range, other) switch
                   {
                       // Both finite — select the later start and earlier end
                       (IFiniteRange<T> b, IFiniteRange<T> o) =>
                           TRange.CreateFinite(
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
                           TRange.CreateOpenStart(
                               upperBound: EarlierEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Value,
                               upperBoundInclusive: EarlierEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // OpenEnd ∩ OpenEnd — result is OpenEnd at the later (more restrictive) start
                       (IOpenEndRange<T> e, IOpenEndRange<T> o) =>
                           TRange.CreateOpenEnd(
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
                           TRange.CreateFinite(
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
                           TRange.CreateFinite(
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
                           TRange.CreateFinite(
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
                           TRange.CreateFinite(
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
                           TRange.CreateFinite(
                               lowerBound: e.LowerBound,
                               upperBound: s.UpperBound,
                               lowerBoundInclusive: e.LowerBoundInclusive,
                               upperBoundInclusive: s.UpperBoundInclusive),

                       // OpenEnd ∩ OpenStart — symmetric
                       (IOpenEndRange<T> e, IOpenStartRange<T> s) =>
                           TRange.CreateFinite(
                               lowerBound: e.LowerBound,
                               upperBound: s.UpperBound,
                               lowerBoundInclusive: e.LowerBoundInclusive,
                               upperBoundInclusive: s.UpperBoundInclusive),

                       _ => TRange.Empty
                   };
        }

        /// <summary>
        /// Returns the smallest range that contains both this range and <paramref name="other"/>.
        /// </summary>
        /// <remarks>
        /// The result type reflects the most general bounds of the two operands: merging an
        /// <see cref="IOpenEndRange{T}"/> with a <see cref="IFiniteRange{T}"/> yields an
        /// <see cref="IOpenEndRange{T}"/>, and so on. Merging an <see cref="IOpenStartRange{T}"/>
        /// with an <see cref="IOpenEndRange{T}"/> spans the entire domain and yields
        /// <see cref="IInfinityRange{T}"/>. Merging two disjoint non-adjacent ranges that cannot
        /// be expressed as a single range returns <see cref="IRangeFactory{TRange,T}.Empty"/>.
        /// </remarks>
        /// <param name="other">The range to merge with.</param>
        /// <returns>
        /// The merged range; <see cref="IRangeFactory{TRange,T}.Empty"/> if the ranges are neither
        /// overlapping nor adjacent (result cannot be expressed as a single range).
        /// </returns>
        public TRange Merge(IRange<T> other)
        {
            if (!range.Overlaps(other) && !range.IsAdjacentTo(other))
                return TRange.Empty;

            // Any operand being Infinity, or the union covering both sides, yields Infinity
            if (range is IInfinityRange<T> || other is IInfinityRange<T>)
                return TRange.Infinite;

            return (range, other) switch
                   {
                       (IFiniteRange<T> b, IFiniteRange<T> o) =>
                           TRange.CreateFinite(
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
                           TRange.CreateOpenStart(
                               upperBound: LaterEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Value,
                               upperBoundInclusive: LaterEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       (IFiniteRange<T> b, IOpenStartRange<T> s) =>
                           TRange.CreateOpenStart(
                               upperBound: LaterEnd(
                                   b.UpperBound, b.UpperBoundInclusive, s.UpperBound, s.UpperBoundInclusive
                               ).Value,
                               upperBoundInclusive: LaterEnd(
                                   b.UpperBound, b.UpperBoundInclusive, s.UpperBound, s.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // OpenEnd absorbs any finite upper bound — result stays OpenEnd at the earlier start
                       (IOpenEndRange<T> e, IFiniteRange<T> o) =>
                           TRange.CreateOpenEnd(
                               lowerBound: EarlierStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Value,
                               lowerBoundInclusive: EarlierStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Inclusive
                           ),

                       (IFiniteRange<T> b, IOpenEndRange<T> e) =>
                           TRange.CreateOpenEnd(
                               lowerBound: EarlierStart(
                                   b.LowerBound, b.LowerBoundInclusive, e.LowerBound, e.LowerBoundInclusive
                               ).Value,
                               lowerBoundInclusive: EarlierStart(
                                   b.LowerBound, b.LowerBoundInclusive, e.LowerBound, e.LowerBoundInclusive
                               ).Inclusive
                           ),

                       // Two OpenStart ranges — result is OpenStart at the later end
                       (IOpenStartRange<T> s, IOpenStartRange<T> o) =>
                           TRange.CreateOpenStart(
                               upperBound: LaterEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Value,
                               upperBoundInclusive: LaterEnd(
                                   s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive
                               ).Inclusive
                           ),

                       // Two OpenEnd ranges — result is OpenEnd at the earlier start
                       (IOpenEndRange<T> e, IOpenEndRange<T> o) =>
                           TRange.CreateOpenEnd(
                               lowerBound: EarlierStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Value,
                               lowerBoundInclusive: EarlierStart(
                                   e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive
                               ).Inclusive
                           ),

                       // OpenStart + OpenEnd (or reverse) — together they cover the entire domain
                       (IOpenStartRange<T>, IOpenEndRange<T>) => TRange.Infinite,
                       (IOpenEndRange<T>, IOpenStartRange<T>) => TRange.Infinite,

                       _ => TRange.Empty
                   };
        }

        /// <summary>
        /// Returns the smallest range that contains both this range and <paramref name="other"/>.
        /// Identical to <see cref="Merge"/>.
        /// </summary>
        /// <param name="other">The range to compute the union with.</param>
        /// <returns>
        /// The union of this range and <paramref name="other"/>; <see cref="IRangeFactory{TRange,T}.Empty"/>
        /// if the ranges are neither overlapping nor adjacent.
        /// </returns>
        public TRange Union(IRange<T> other) =>
            range.Merge(other);

        /// <summary>
        /// Returns what remains of this range after removing the portion that overlaps with <paramref name="other"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Boundary inclusiveness is inverted at the cut point: the new boundary at the edge of the
        /// removed region takes the opposite inclusiveness of <paramref name="other"/>'s bound at that
        /// point, ensuring no value is lost or counted twice.
        /// </para>
        /// <para>
        /// Return value semantics:
        /// <list type="bullet">
        ///   <item>
        ///     <term><c>(Empty, null)</c></term>
        ///     <description>This range is fully contained by <paramref name="other"/>; nothing remains.</description>
        ///   </item>
        ///   <item>
        ///     <term><c>(Left, null)</c></term>
        ///     <description>
        ///     A one-sided trim or no overlap; <c>Left</c> holds the unaffected portion.
        ///     </description>
        ///   </item>
        ///   <item>
        ///     <term><c>(Left, Right)</c></term>
        ///     <description>
        ///     <paramref name="other"/> was strictly interior to this range; the result is split in two.
        ///     </description>
        ///   </item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="other">The range whose overlap should be subtracted from this range.</param>
        /// <returns>
        /// A tuple containing the remaining range pieces. <c>Left</c> is <see cref="IRangeFactory{TRange,T}.Empty"/>
        /// when this range is fully consumed by <paramref name="other"/>.
        /// </returns>
        public (TRange Left, TRange? Right) Except(IRange<T> other)
        {
            // No overlap — range is entirely unaffected
            if (!range.Overlaps(other))
                return (range, default);

            // other fully contains range — nothing remains
            if (other.Contains(range))
                return (TRange.Empty, default);

            // Infinity minus a bounded-on-one-side range leaves the complementary half
            if (range is IInfinityRange<T>)
                return other switch
                       {
                           IFiniteRange<T> o =>
                               (TRange.CreateOpenStart(o.LowerBound, !o.LowerBoundInclusive),
                                (TRange?)TRange.CreateOpenEnd(o.UpperBound, !o.UpperBoundInclusive)),
                           IOpenStartRange<T> s =>
                               (TRange.CreateOpenEnd(s.UpperBound, !s.UpperBoundInclusive), default),
                           IOpenEndRange<T> e =>
                               (TRange.CreateOpenStart(e.LowerBound, !e.LowerBoundInclusive), default),
                           _ => (range, default)
                       };

            return (range, other) switch
                   {
                       (IFiniteRange<T> b, IFiniteRange<T> o) =>
                           // Interior split: other sits strictly inside range
                           OuterStartCoversInnerStart(b.LowerBound, b.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive) &&
                           OuterEndCoversInnerEnd(b.UpperBound, b.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive)
                               ? (TRange.CreateFinite(b.LowerBound, o.LowerBound, b.LowerBoundInclusive, !o.LowerBoundInclusive),
                                  (TRange?)TRange.CreateFinite(o.UpperBound, b.UpperBound, !o.UpperBoundInclusive,
                                                               b.UpperBoundInclusive))
                               // Left-side overlap: other covers the start of range
                               : OuterStartCoversInnerStart(o.LowerBound, o.LowerBoundInclusive, b.LowerBound, b.LowerBoundInclusive)
                                   ? (TRange.CreateFinite(o.UpperBound, b.UpperBound, !o.UpperBoundInclusive, b.UpperBoundInclusive),
                                      (TRange?)default)
                                   // Right-side overlap: other covers the end of range
                                   : (TRange.CreateFinite(b.LowerBound, o.LowerBound, b.LowerBoundInclusive, !o.LowerBoundInclusive),
                                      (TRange?)default),

                       // OpenStart split by Finite interior: left part stays OpenStart, right part is Finite
                       (IOpenStartRange<T> s, IFiniteRange<T> o)
                           when OuterEndCoversInnerEnd(s.UpperBound, s.UpperBoundInclusive, o.UpperBound, o.UpperBoundInclusive) =>
                           (TRange.CreateOpenStart(o.LowerBound, !o.LowerBoundInclusive),
                            (TRange?)TRange.CreateFinite(o.UpperBound, s.UpperBound, !o.UpperBoundInclusive, s.UpperBoundInclusive)),

                       // OpenStart trimmed by Finite from the right — result is OpenStart at a new end
                       (IOpenStartRange<T> s, IFiniteRange<T> o) =>
                           TRange.CreateOpenStart(o.LowerBound, !o.LowerBoundInclusive) is var left
                               ? (left, default)
                               : default,

                       // OpenEnd split by Finite interior: left part is Finite, right part stays OpenEnd
                       (IOpenEndRange<T> e, IFiniteRange<T> o)
                           when OuterStartCoversInnerStart(e.LowerBound, e.LowerBoundInclusive, o.LowerBound, o.LowerBoundInclusive) =>
                           (TRange.CreateFinite(e.LowerBound, o.LowerBound, e.LowerBoundInclusive, !o.LowerBoundInclusive),
                            (TRange?)TRange.CreateOpenEnd(o.UpperBound, !o.UpperBoundInclusive)),

                       // OpenEnd trimmed by Finite from the left — result is OpenEnd at a new start
                       (IOpenEndRange<T> e, IFiniteRange<T> o) =>
                           TRange.CreateOpenEnd(o.UpperBound, !o.UpperBoundInclusive) is var left
                               ? (left, default)
                               : default,

                       _ => (range, default)
                   };
        }
    }
}
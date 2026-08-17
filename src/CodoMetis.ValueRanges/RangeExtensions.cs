using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

using static RangeBoundHelpers;

/// <summary>
/// Extension methods providing query and set operations on <see cref="IRange{T}"/> instances.
/// </summary>
public static class RangeExtensions
{
    extension<T>(IRange<T> range) where T : struct, IComparable<T>, IEquatable<T>
    {
        /// <summary>
        /// Returns <see langword="true"/> if the range contains no values — equivalent to the PostgreSQL <c>isempty</c> function.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> for <see cref="IEmptyRange{T}"/>; <see langword="false"/> for all other shapes.
        /// </returns>
        public bool IsEmpty()          => range is IEmptyRange<T>;

        /// <summary>
        /// Returns <see langword="true"/> if the range is unbounded in both directions — equivalent to <c>(-∞, +∞)</c>.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> for <see cref="IInfinityRange{T}"/>; <see langword="false"/> for all other shapes.
        /// </returns>
        public bool IsInfinity()       => range is IInfinityRange<T>;

        /// <summary>
        /// Returns <see langword="true"/> if the range has both a lower and an upper bound.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> for <see cref="IFiniteRange{T}"/>; <see langword="false"/> for all other shapes.
        /// </returns>
        public bool IsFinite()         => range is IFiniteRange<T>;

        /// <summary>
        /// Returns <see langword="true"/> if the range has no lower bound but has an upper bound — equivalent to <c>(-∞, end]</c>.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> for <see cref="IUnboundedStartRange{T}"/>; <see langword="false"/> for all other shapes.
        /// </returns>
        public bool IsUnboundedStart() => range is IUnboundedStartRange<T>;

        /// <summary>
        /// Returns <see langword="true"/> if the range has a lower bound but no upper bound — equivalent to <c>[start, +∞)</c>.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> for <see cref="IUnboundedEndRange{T}"/>; <see langword="false"/> for all other shapes.
        /// </returns>
        public bool IsUnboundedEnd()   => range is IUnboundedEndRange<T>;

        /// <summary>
        /// Returns the lower bound of the range, or <see langword="null"/> when the range is
        /// empty or unbounded on the left — equivalent to the PostgreSQL <c>lower</c> function.
        /// </summary>
        /// <returns>
        /// The <c>Start</c> value for <see cref="IFiniteRange{T}"/> and <see cref="IUnboundedEndRange{T}"/>;
        /// <see langword="null"/> for all other shapes.
        /// </returns>
        public T? LowerBound() =>
            range switch
            {
                IFiniteRange<T> b       => b.Start,
                IUnboundedEndRange<T> e => e.Start,
                _                       => null
            };

        /// <summary>
        /// Returns the upper bound of the range, or <see langword="null"/> when the range is
        /// empty or unbounded on the right — equivalent to the PostgreSQL <c>upper</c> function.
        /// </summary>
        /// <returns>
        /// The <c>End</c> value for <see cref="IFiniteRange{T}"/> and <see cref="IUnboundedStartRange{T}"/>;
        /// <see langword="null"/> for all other shapes.
        /// </returns>
        public T? UpperBound() =>
            range switch
            {
                IFiniteRange<T> b         => b.End,
                IUnboundedStartRange<T> s => s.End,
                _                         => null
            };

        /// <summary>
        /// Returns <see langword="true"/> if the range has an inclusive lower bound —
        /// equivalent to the PostgreSQL <c>lower_inc</c> function.
        /// </summary>
        /// <returns>
        /// The <c>StartInclusive</c> flag for <see cref="IFiniteRange{T}"/> and
        /// <see cref="IUnboundedEndRange{T}"/>; <see langword="false"/> for all other shapes.
        /// </returns>
        public bool LowerBoundInclusive() =>
            range switch
            {
                IFiniteRange<T> b       => b.StartInclusive,
                IUnboundedEndRange<T> e => e.StartInclusive,
                _                       => false
            };

        /// <summary>
        /// Returns <see langword="true"/> if the range has an inclusive upper bound —
        /// equivalent to the PostgreSQL <c>upper_inc</c> function.
        /// </summary>
        /// <returns>
        /// The <c>EndInclusive</c> flag for <see cref="IFiniteRange{T}"/> and
        /// <see cref="IUnboundedStartRange{T}"/>; <see langword="false"/> for all other shapes.
        /// </returns>
        public bool UpperBoundInclusive() =>
            range switch
            {
                IFiniteRange<T> b         => b.EndInclusive,
                IUnboundedStartRange<T> s => s.EndInclusive,
                _                         => false
            };

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
                IFiniteRange<T> b => (b.StartInclusive ? b.Start.CompareTo(value) <= 0 : b.Start.CompareTo(value) < 0) &&
                                     (b.EndInclusive ? value.CompareTo(b.End)     <= 0 : value.CompareTo(b.End)   < 0),
                IUnboundedStartRange<T> s => s.EndInclusive ? value.CompareTo(s.End)     <= 0 : value.CompareTo(s.End)   < 0,
                IUnboundedEndRange<T> e   => e.StartInclusive ? e.Start.CompareTo(value) <= 0 : e.Start.CompareTo(value) < 0,
                _                         => false
            };

        /// <summary>
        /// Returns the value in the range closest to <paramref name="value"/> — the value itself
        /// when the range contains it, otherwise the bound it falls outside of.
        /// </summary>
        /// <remarks>
        /// Returns <see langword="null"/> for the empty range, which has no value to snap to.
        /// An unbounded side never constrains: clamping into <c>(-∞, 10]</c> only ever pulls a
        /// value down. On a continuous domain an exclusive bound is returned even though the
        /// range does not contain it — the nearest contained value does not exist there, so
        /// pair this with <c>Contains</c> when the distinction matters.
        /// </remarks>
        /// <param name="value">The value to bring into the range.</param>
        /// <returns>The clamped value, or <see langword="null"/> for the empty range.</returns>
        public T? Clamp(T value) =>
            range switch
            {
                IInfinityRange<T> => value,

                IFiniteRange<T> b => value.CompareTo(b.Start) < 0 ? b.Start
                                   : value.CompareTo(b.End) > 0   ? b.End
                                                                  : value,

                IUnboundedStartRange<T> s => value.CompareTo(s.End) > 0 ? s.End : value,
                IUnboundedEndRange<T> e   => value.CompareTo(e.Start) < 0 ? e.Start : value,

                _ => null
            };

        /// <summary>
        /// Determines whether <paramref name="other"/> is entirely contained within this range.
        /// </summary>
        /// <param name="other">The range to test.</param>
        /// <returns>
        /// <see langword="true"/> if every value in <paramref name="other"/> also belongs to this range.
        /// Always <see langword="true"/> when <paramref name="other"/> is <see cref="IEmptyRange{T}"/> —
        /// the empty range is contained by every range, including itself — and always
        /// <see langword="true"/> for an <see cref="IInfinityRange{T}"/> receiver.
        /// Always <see langword="false"/> when <paramref name="other"/> extends in a direction that
        /// this range does not bound, or when this range is <see cref="IEmptyRange{T}"/> and
        /// <paramref name="other"/> is not.
        /// </returns>
        public bool Contains(IRange<T> other)
        {
            // ∅ ⊆ S for every S. "Every value in other also belongs to this range" is vacuously
            // satisfied when other has no values, so there is nothing to check and no receiver
            // shape that can refuse — the empty receiver included, since ∅ ⊆ ∅. This is also what
            // PostgreSQL's @> answers, and what Contains(RangeSet) has always answered by
            // iterating zero elements; before 6.4.0 the single-range overload disagreed with both.
            if (other.IsEmpty()) return true;

            return range switch
            {
                IInfinityRange<T> => true,

                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o => OuterStartCoversInnerStart(b.Start, b.StartInclusive, o.Start, o.StartInclusive) &&
                                             OuterEndCoversInnerEnd(b.End, b.EndInclusive, o.End, o.EndInclusive),
                        _ => false
                    },

                // (-∞, s.End] or (-∞, s.End): no lower constraint — only the upper bound matters.
                // An IUnboundedEndRange inner goes to +∞ and can never be contained.
                IUnboundedStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o         => OuterEndCoversInnerEnd(s.End, s.EndInclusive, o.End, o.EndInclusive),
                        IUnboundedStartRange<T> o => OuterEndCoversInnerEnd(s.End, s.EndInclusive, o.End, o.EndInclusive),
                        _                         => false
                    },

                // [e.Start, +∞) or (e.Start, +∞): no upper constraint — only the lower bound matters.
                // An IUnboundedStartRange inner goes to -∞ and can never be contained.
                IUnboundedEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o       => OuterStartCoversInnerStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive),
                        IUnboundedEndRange<T> o => OuterStartCoversInnerStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive),
                        _                       => false
                    },

                // An empty receiver, with a non-empty other: ∅ contains nothing.
                _ => false
            };
        }

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

                IInfinityRange<T> => !other.IsEmpty(),

                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o => TouchingBoundsOverlap(b.End, b.EndInclusive, o.Start, o.StartInclusive) &&
                                             TouchingBoundsOverlap(o.End, o.EndInclusive, b.Start, b.StartInclusive),
                        IUnboundedStartRange<T> s => TouchingBoundsOverlap(s.End, s.EndInclusive, b.Start, b.StartInclusive),
                        IUnboundedEndRange<T> e   => TouchingBoundsOverlap(b.End, b.EndInclusive, e.Start, e.StartInclusive),
                        IInfinityRange<T>         => true,
                        _                         => false
                    },

                IUnboundedStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o => TouchingBoundsOverlap(s.End, s.EndInclusive, o.Start, o.StartInclusive),
                        IUnboundedEndRange<T> e => TouchingBoundsOverlap(s.End, s.EndInclusive, e.Start, e.StartInclusive),
                        IUnboundedStartRange<T> or IInfinityRange<T> => true,
                        _ => false
                    },

                IUnboundedEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o => TouchingBoundsOverlap(o.End, o.EndInclusive, e.Start, e.StartInclusive),
                        IUnboundedStartRange<T> s => TouchingBoundsOverlap(s.End, s.EndInclusive, e.Start, e.StartInclusive),
                        IUnboundedEndRange<T> or IInfinityRange<T> => true,
                        _ => false
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
        /// Always <see langword="false"/> when this range has no upper bound
        /// (<see cref="IUnboundedEndRange{T}"/>, <see cref="IInfinityRange{T}"/>) or when
        /// <paramref name="other"/> has no lower bound (<see cref="IUnboundedStartRange{T}"/>,
        /// <see cref="IInfinityRange{T}"/>), and whenever either range is <see cref="IEmptyRange{T}"/>.
        /// An <see cref="IUnboundedStartRange{T}"/> receiver is *not* excluded: <c>(-∞, e]</c> has a
        /// finite upper bound, so it is strictly left of anything starting after <c>e</c>.
        /// </returns>
        public bool IsStrictlyLeftOf(IRange<T> other)
        {
            // <<  compares this range's UPPER bound against other's LOWER bound, so what decides is
            // which side each operand is unbounded on — not whether it is unbounded at all. Reading
            // the two bounds separately, rather than switching on the receiver's shape and handling
            // unbounded operands only in the inner switch, is what keeps the two directions in step:
            // deciding per receiver is how (-∞, e] came to answer false where PostgreSQL's << answers
            // true, the same trap IsAdjacentTo fell into before 6.3.0.
            (T Value, bool Inclusive)? upper = range switch
            {
                IFiniteRange<T> f         => (f.End, f.EndInclusive),
                IUnboundedStartRange<T> s => (s.End, s.EndInclusive),
                _                         => null // +∞ (or empty): never left of anything
            };

            (T Value, bool Inclusive)? lower = other switch
            {
                IFiniteRange<T> f       => (f.Start, f.StartInclusive),
                IUnboundedEndRange<T> e => (e.Start, e.StartInclusive),
                _                       => null // -∞ (or empty): nothing is left of it
            };

            if (upper is not { } end || lower is not { } start) return false;

            int comparison = end.Value.CompareTo(start.Value);
            return comparison < 0 || (comparison == 0 && !(end.Inclusive && start.Inclusive));
        }

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
        /// where <paramref name="other"/> is exclusive at that point. An infinite upper bound compares
        /// equal to another infinite upper bound — PostgreSQL semantics: <c>+∞ ≤ +∞</c> —
        /// so an <see cref="IUnboundedEndRange{T}"/> or <see cref="IInfinityRange{T}"/> receiver
        /// returns <see langword="true"/> exactly when <paramref name="other"/> is also unbounded above.
        /// </remarks>
        /// <param name="other">The range to compare against.</param>
        /// <returns>
        /// <see langword="true"/> if the upper bound of this range does not exceed that of
        /// <paramref name="other"/>; always <see langword="false"/> when either range is
        /// <see cref="IEmptyRange{T}"/>.
        /// </returns>
        public bool DoesNotExtendRightOf(IRange<T> other) =>
            range switch
            {
                // Upper bound +∞: only another +∞ upper compares equal (PostgreSQL &<).
                IUnboundedEndRange<T> or IInfinityRange<T> =>
                    other is IUnboundedEndRange<T> or IInfinityRange<T>,

                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            b.End.CompareTo(o.End) < 0 || (b.End.CompareTo(o.End) == 0 && (!b.EndInclusive || o.EndInclusive)),
                        IUnboundedStartRange<T> s =>
                            b.End.CompareTo(s.End) < 0 || (b.End.CompareTo(s.End) == 0 && (!b.EndInclusive || s.EndInclusive)),
                        IUnboundedEndRange<T> or IInfinityRange<T> => true,
                        _                                          => false
                    },

                IUnboundedStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o => s.End.CompareTo(o.End) < 0 ||
                                             (s.End.CompareTo(o.End) == 0 && (!s.EndInclusive || o.EndInclusive)),
                        IUnboundedStartRange<T> o => s.End.CompareTo(o.End) < 0 ||
                                                     (s.End.CompareTo(o.End) == 0 && (!s.EndInclusive || o.EndInclusive)),
                        IUnboundedEndRange<T> or IInfinityRange<T> => true,
                        _                                          => false
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
        /// where <paramref name="other"/> is exclusive at that point. An infinite lower bound compares
        /// equal to another infinite lower bound — PostgreSQL semantics: <c>-∞ ≥ -∞</c> —
        /// so an <see cref="IUnboundedStartRange{T}"/> or <see cref="IInfinityRange{T}"/> receiver
        /// returns <see langword="true"/> exactly when <paramref name="other"/> is also unbounded below.
        /// </remarks>
        /// <param name="other">The range to compare against.</param>
        /// <returns>
        /// <see langword="true"/> if the lower bound of this range is not less than that of
        /// <paramref name="other"/>; always <see langword="false"/> when either range is
        /// <see cref="IEmptyRange{T}"/>.
        /// </returns>
        public bool DoesNotExtendLeftOf(IRange<T> other) =>
            range switch
            {
                // Lower bound -∞: only another -∞ lower compares equal (PostgreSQL &>).
                IUnboundedStartRange<T> or IInfinityRange<T> =>
                    other is IUnboundedStartRange<T> or IInfinityRange<T>,

                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o => b.Start.CompareTo(o.Start) > 0 ||
                                             (b.Start.CompareTo(o.Start) == 0 && (!b.StartInclusive || o.StartInclusive)),
                        IUnboundedEndRange<T> e => b.Start.CompareTo(e.Start) > 0 ||
                                                   (b.Start.CompareTo(e.Start) == 0 && (!b.StartInclusive || e.StartInclusive)),
                        IUnboundedStartRange<T> or IInfinityRange<T> => true,
                        _                                            => false
                    },

                IUnboundedEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o => e.Start.CompareTo(o.Start) > 0 ||
                                             (e.Start.CompareTo(o.Start) == 0 && (!e.StartInclusive || o.StartInclusive)),
                        IUnboundedEndRange<T> o => e.Start.CompareTo(o.Start) > 0 ||
                                                   (e.Start.CompareTo(o.Start) == 0 && (!e.StartInclusive || o.StartInclusive)),
                        IUnboundedStartRange<T> or IInfinityRange<T> => true,
                        _                                            => false
                    },

                _ => false
            };
    }

    // -------------------------------------------------------------------------
    // Set operation extensions
    // -------------------------------------------------------------------------

    extension<TRange, T>(TRange range)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        /// <summary>
        /// Determines whether this range and <paramref name="other"/> are contiguous —
        /// no gap and no overlap — such that their union forms a single range.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For discrete types, two ranges whose boundaries are
        /// exactly one step apart are also considered adjacent. For example, <c>[1, 5]</c> and
        /// <c>[6, 10]</c> are adjacent for <see cref="int"/>.
        /// </para>
        /// <para>
        /// For continuous types, adjacency requires the ranges to touch at exactly one point with
        /// complementary inclusiveness: one side must claim the boundary point and the other must not.
        /// </para>
        /// <para>
        /// An unbounded range is adjacent on its bounded edge: <c>(-∞, e]</c> is adjacent to a
        /// range starting immediately after <c>e</c>, and <c>[s, +∞)</c> to one ending
        /// immediately before <c>s</c> — including to each other, where the two halves close
        /// the domain. <see cref="IEmptyRange{T}"/> and <see cref="IInfinityRange{T}"/> are never
        /// adjacent to anything, and two ranges open at the same end always overlap.
        /// </para>
        /// </remarks>
        /// <param name="other">The range to test against.</param>
        /// <returns>
        /// <see langword="true"/> if the ranges are contiguous with no gap and no overlap.
        /// </returns>
        public bool IsAdjacentTo(IRange<T> other)
        {
            // Adjacency is symmetric — PostgreSQL's -|- is too — so each unordered pair of
            // shapes is decided once and both receiver orders route to the same test. Deciding
            // per receiver is how the two unbounded receivers came to answer false while their
            // mirrored operand cases answered true.
            static bool Meets(T leftEnd, bool leftInc, T rightStart, bool rightInc)
                => BoundaryMeetsAdjacently<TRange, T>(leftEnd, leftInc, rightStart, rightInc);

            // Either may come first, so both orders are tried.
            static bool FiniteFinite(IFiniteRange<T> a, IFiniteRange<T> b)
                => Meets(a.End, a.EndInclusive, b.Start, b.StartInclusive)
                || Meets(b.End, b.EndInclusive, a.Start, a.StartInclusive);

            // (-∞, s.End] runs to negative infinity, so it can only be followed by f.
            static bool StartThenFinite(IUnboundedStartRange<T> s, IFiniteRange<T> f)
                => Meets(s.End, s.EndInclusive, f.Start, f.StartInclusive);

            // [e.Start, +∞) runs to positive infinity, so it can only follow f.
            static bool FiniteThenEnd(IFiniteRange<T> f, IUnboundedEndRange<T> e)
                => Meets(f.End, f.EndInclusive, e.Start, e.StartInclusive);

            // The two halves meeting exactly: no gap, no overlap, and together the whole domain.
            static bool StartThenEnd(IUnboundedStartRange<T> s, IUnboundedEndRange<T> e)
                => Meets(s.End, s.EndInclusive, e.Start, e.StartInclusive);

            return (range, other) switch
            {
                (IFiniteRange<T> a, IFiniteRange<T> b) => FiniteFinite(a, b),

                (IUnboundedStartRange<T> s, IFiniteRange<T> f) => StartThenFinite(s, f),
                (IFiniteRange<T> f, IUnboundedStartRange<T> s) => StartThenFinite(s, f),

                (IFiniteRange<T> f, IUnboundedEndRange<T> e) => FiniteThenEnd(f, e),
                (IUnboundedEndRange<T> e, IFiniteRange<T> f) => FiniteThenEnd(f, e),

                (IUnboundedStartRange<T> s, IUnboundedEndRange<T> e) => StartThenEnd(s, e),
                (IUnboundedEndRange<T> e, IUnboundedStartRange<T> s) => StartThenEnd(s, e),

                // Empty is adjacent to nothing; Infinity overlaps everything non-empty; two
                // ranges open at the same end always overlap.
                _ => false
            };
        }

        /// <summary>
        /// Returns the smallest single range containing both this range and
        /// <paramref name="other"/> — equivalent to the PostgreSQL <c>range_merge</c> function.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Union"/>, the result also covers any gap between disjoint
        /// operands: <c>[1,3].Merge([10,12])</c> is <c>[1,12]</c>. Empty operands are
        /// ignored; merging two empty ranges yields <see cref="IRangeFactory{TRange,T}.Empty"/>.
        /// </remarks>
        /// <param name="other">The range to span together with this range.</param>
        /// <returns>The convex hull of the two operands.</returns>
        public TRange Merge(IRange<T> other)
        {
            if (range.IsEmpty()) return RecreateAs<TRange, T>(other);
            if (other.IsEmpty()) return range;
            if (range.IsInfinity() || other.IsInfinity()) return TRange.Infinite;

            var (aLower, aLowerInc, aLowerInf) = RangeSetHelpers.RangeLowerBound<T>(range);
            var (bLower, bLowerInc, bLowerInf) = RangeSetHelpers.RangeLowerBound<T>(other);
            var (aUpper, aUpperInc, aUpperInf) = RangeSetHelpers.RangeUpperBound<T>(range);
            var (bUpper, bUpperInc, bUpperInf) = RangeSetHelpers.RangeUpperBound<T>(other);

            bool lowerInfinite = aLowerInf || bLowerInf;
            bool upperInfinite = aUpperInf || bUpperInf;

            if (lowerInfinite && upperInfinite) return TRange.Infinite;

            if (lowerInfinite)
            {
                var (value, inclusive) = LaterEnd(aUpper, aUpperInc, bUpper, bUpperInc);
                return TRange.CreateUnboundedStart(value, inclusive);
            }

            if (upperInfinite)
            {
                var (value, inclusive) = EarlierStart(aLower, aLowerInc, bLower, bLowerInc);
                return TRange.CreateUnboundedEnd(value, inclusive);
            }

            var lower = EarlierStart(aLower, aLowerInc, bLower, bLowerInc);
            var upper = LaterEnd(aUpper, aUpperInc, bUpper, bUpperInc);
            return TRange.CreateFinite(lower.Value, upper.Value, lower.Inclusive, upper.Inclusive);
        }

        /// <summary>
        /// Returns the largest range contained by both this range and <paramref name="other"/>.
        /// </summary>
        /// <remarks>
        /// All combinations of <see cref="IFiniteRange{T}"/>, <see cref="IUnboundedStartRange{T}"/>,
        /// <see cref="IUnboundedEndRange{T}"/>, and <see cref="IInfinityRange{T}"/> are handled and produce
        /// the appropriately shaped result type. For example, intersecting an <see cref="IInfinityRange{T}"/>
        /// with any range returns that range unchanged.
        /// </remarks>
        /// <param name="other">The range to intersect with.</param>
        /// <returns>
        /// The intersection of this range and <paramref name="other"/>,
        /// or <see cref="IRangeFactory{TRange,T}.Empty"/> if the ranges do not overlap.
        /// </returns>
        public TRange Intersect(IRange<T> other) =>
            !range.Overlaps(other)
                ? TRange.Empty
                : other is IInfinityRange<T>
                    ? range
                    : range.IntersectWith<TRange>(other);

        /// <summary>
        /// Returns the union of this range and <paramref name="other"/> as a normalized
        /// <see cref="RangeSet{TRange, T}"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When the ranges overlap or are adjacent, the result is a one-element set holding the merged
        /// range, whose shape reflects the most general bounds of the two operands: merging an
        /// <see cref="IUnboundedEndRange{T}"/> with a <see cref="IFiniteRange{T}"/> yields an
        /// <see cref="IUnboundedEndRange{T}"/>, and so on. Merging an <see cref="IUnboundedStartRange{T}"/>
        /// with an overlapping or adjacent <see cref="IUnboundedEndRange{T}"/> spans the entire domain
        /// and yields <see cref="IInfinityRange{T}"/>.
        /// </para>
        /// <para>
        /// When the ranges are disjoint and non-adjacent, the result is a two-element set — the union
        /// genuinely consists of two separate ranges. Empty operands are dropped, so the result has
        /// zero elements only when both operands are empty.
        /// </para>
        /// </remarks>
        /// <param name="other">The range to compute the union with.</param>
        /// <returns>
        /// A normalized set containing every value of this range and of <paramref name="other"/>.
        /// </returns>
        public RangeSet<TRange, T> Union(IRange<T> other) =>
            RangeSet<TRange, T>.From([range, RangeBoundHelpers.RecreateAs<TRange, T>(other)]);

        /// <summary>
        /// Returns what remains of this range after removing the portion that overlaps with
        /// <paramref name="other"/>, as a normalized <see cref="RangeSet{TRange, T}"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Boundary inclusiveness is inverted at the cut point: the new boundary at the edge of the
        /// removed region takes the opposite inclusiveness of <paramref name="other"/>'s bound at that
        /// point, ensuring no value is lost or counted twice.
        /// </para>
        /// <para>
        /// The set's cardinality reflects the structural outcome directly:
        /// <list type="bullet">
        ///   <item>
        ///     <term>0 elements</term>
        ///     <description>This range is fully contained by <paramref name="other"/>; nothing remains.</description>
        ///   </item>
        ///   <item>
        ///     <term>1 element</term>
        ///     <description>A one-sided trim or no overlap; the unaffected portion remains.</description>
        ///   </item>
        ///   <item>
        ///     <term>2 elements</term>
        ///     <description>
        ///     <paramref name="other"/> was strictly interior to this range; the result is split in two.
        ///     </description>
        ///   </item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="other">The range whose overlap should be subtracted from this range.</param>
        /// <returns>A normalized set of the remaining pieces.</returns>
        public RangeSet<TRange, T> Except(IRange<T> other)
        {
            if (!range.Overlaps(other)) return RangeSet<TRange, T>.From([range]);
            if (other.Contains(range)) return RangeSet<TRange, T>.Empty;
            var (left, right) = range is IInfinityRange<T>
                                    ? ExceptEngine.InfinityExcept<TRange, T>(other)
                                    : range switch
                                      {
                                          IFiniteRange<T> b         => ExceptEngine.Execute<TRange, T>(b, other),
                                          IUnboundedStartRange<T> s => ExceptEngine.Execute<TRange, T>(s, other),
                                          IUnboundedEndRange<T> e   => ExceptEngine.Execute<TRange, T>(e, other),
                                          _                         => (range, default)
                                      };
            return right is null
                       ? RangeSet<TRange, T>.From([left])
                       : RangeSet<TRange, T>.From([left, right]);
        }

    }
}
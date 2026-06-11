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
                IInfinityRange<T> => !other.IsEmpty(),

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
        /// Always <see langword="false"/> when this range is <see cref="IUnboundedEndRange{T}"/>,
        /// <see cref="IUnboundedStartRange{T}"/>, <see cref="IInfinityRange{T}"/>, or <see cref="IEmptyRange{T}"/>.
        /// </returns>
        public bool IsStrictlyLeftOf(IRange<T> other) =>
            range switch
            {
                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o =>
                            b.End.CompareTo(o.Start) < 0 || (b.End.CompareTo(o.Start) == 0 && !(b.EndInclusive && o.StartInclusive)),
                        IUnboundedEndRange<T> e =>
                            b.End.CompareTo(e.Start) < 0 || (b.End.CompareTo(e.Start) == 0 && !(b.EndInclusive && e.StartInclusive)),
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
        /// An <see cref="IUnboundedEndRange{T}"/> or <see cref="IInfinityRange{T}"/> has no finite upper bound
        /// and always returns <see langword="false"/>.
        /// </remarks>
        /// <param name="other">The range to compare against.</param>
        /// <returns>
        /// <see langword="true"/> if the upper bound of this range does not exceed that of <paramref name="other"/>;
        /// always <see langword="false"/> for <see cref="IUnboundedEndRange{T}"/> or <see cref="IInfinityRange{T}"/>.
        /// </returns>
        public bool DoesNotExtendRightOf(IRange<T> other) =>
            range switch
            {
                IUnboundedEndRange<T> => false,
                IInfinityRange<T>     => false,

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
        /// where <paramref name="other"/> is exclusive at that point.
        /// An <see cref="IUnboundedStartRange{T}"/> or <see cref="IInfinityRange{T}"/> has no finite lower bound
        /// and always returns <see langword="false"/>.
        /// </remarks>
        /// <param name="other">The range to compare against.</param>
        /// <returns>
        /// <see langword="true"/> if the lower bound of this range is not less than that of <paramref name="other"/>;
        /// always <see langword="false"/> for <see cref="IUnboundedStartRange{T}"/> or <see cref="IInfinityRange{T}"/>.
        /// </returns>
        public bool DoesNotExtendLeftOf(IRange<T> other) =>
            range switch
            {
                IUnboundedStartRange<T> => false,
                IInfinityRange<T>       => false,

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
        /// Only <see cref="IFiniteRange{T}"/> instances can be adjacent to other ranges;
        /// <see cref="IUnboundedEndRange{T}"/>, <see cref="IUnboundedStartRange{T}"/>, and
        /// <see cref="IInfinityRange{T}"/> always return <see langword="false"/>.
        /// </para>
        /// </remarks>
        /// <param name="other">The range to test against.</param>
        /// <returns>
        /// <see langword="true"/> if the ranges are contiguous with no gap and no overlap.
        /// </returns>
        public bool IsAdjacentTo(IRange<T> other) =>
            range switch
            {
                IFiniteRange<T> b =>
                    other switch
                    {
                        IFiniteRange<T> o => BoundaryMeetsAdjacently<TRange, T>(
                                                 b.End, b.EndInclusive, o.Start, o.StartInclusive
                                             ) ||
                                             BoundaryMeetsAdjacently<TRange, T>(
                                                 o.End, o.EndInclusive, b.Start, b.StartInclusive
                                             ),

                        IUnboundedStartRange<T> s => BoundaryMeetsAdjacently<TRange, T>(
                            s.End, s.EndInclusive, b.Start, b.StartInclusive
                        ),

                        IUnboundedEndRange<T> e => BoundaryMeetsAdjacently<TRange, T>(
                            b.End, b.EndInclusive, e.Start, e.StartInclusive
                        ),

                        _ => false
                    },

                _ => false
            };

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
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
                IOpenStartRange<T> s => s.EndInclusive ? value.CompareTo(s.End)     <= 0 : value.CompareTo(s.End)   < 0,
                IOpenEndRange<T> e   => e.StartInclusive ? e.Start.CompareTo(value) <= 0 : e.Start.CompareTo(value) < 0,
                _                    => false
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
                        IFiniteRange<T> o => OuterStartCoversInnerStart(b.Start, b.StartInclusive, o.Start, o.StartInclusive) &&
                                             OuterEndCoversInnerEnd(b.End, b.EndInclusive, o.End, o.EndInclusive),
                        _ => false
                    },

                // (-∞, s.End] or (-∞, s.End): no lower constraint — only the upper bound matters.
                // An IOpenEndRange inner goes to +∞ and can never be contained.
                IOpenStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o    => OuterEndCoversInnerEnd(s.End, s.EndInclusive, o.End, o.EndInclusive),
                        IOpenStartRange<T> o => OuterEndCoversInnerEnd(s.End, s.EndInclusive, o.End, o.EndInclusive),
                        _                    => false
                    },

                // [e.Start, +∞) or (e.Start, +∞): no upper constraint — only the lower bound matters.
                // An IOpenStartRange inner goes to -∞ and can never be contained.
                IOpenEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o  => OuterStartCoversInnerStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive),
                        IOpenEndRange<T> o => OuterStartCoversInnerStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive),
                        _                  => false
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
                        IFiniteRange<T> o => TouchingBoundsOverlap(b.End, b.EndInclusive, o.Start, o.StartInclusive) &&
                                             TouchingBoundsOverlap(o.End, o.EndInclusive, b.Start, b.StartInclusive),
                        IOpenStartRange<T> s => TouchingBoundsOverlap(s.End, s.EndInclusive, b.Start, b.StartInclusive),
                        IOpenEndRange<T> e   => TouchingBoundsOverlap(b.End, b.EndInclusive, e.Start, e.StartInclusive),
                        IInfinityRange<T>    => true,
                        _                    => false
                    },

                IOpenStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o  => TouchingBoundsOverlap(s.End, s.EndInclusive, o.Start, o.StartInclusive),
                        IOpenEndRange<T> e => TouchingBoundsOverlap(s.End, s.EndInclusive, e.Start, e.StartInclusive),
                        IOpenStartRange<T> => true,
                        IInfinityRange<T>  => true,
                        _                  => false
                    },

                IOpenEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o    => TouchingBoundsOverlap(o.End, o.EndInclusive, e.Start, e.StartInclusive),
                        IOpenStartRange<T> s => TouchingBoundsOverlap(s.End, s.EndInclusive, e.Start, e.StartInclusive),
                        IOpenEndRange<T>     => true,
                        IInfinityRange<T>    => true,
                        _                    => false
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
                            b.End.CompareTo(o.Start) < 0 || (b.End.CompareTo(o.Start) == 0 && !(b.EndInclusive && o.StartInclusive)),
                        IOpenEndRange<T> e =>
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
                            b.End.CompareTo(o.End) < 0 || (b.End.CompareTo(o.End) == 0 && (!b.EndInclusive || o.EndInclusive)),
                        IOpenStartRange<T> s =>
                            b.End.CompareTo(s.End) < 0 || (b.End.CompareTo(s.End) == 0 && (!b.EndInclusive || s.EndInclusive)),
                        IOpenEndRange<T>  => true,
                        IInfinityRange<T> => true,
                        _                 => false
                    },

                IOpenStartRange<T> s =>
                    other switch
                    {
                        IFiniteRange<T> o => s.End.CompareTo(o.End) < 0 || 
                                             (s.End.CompareTo(o.End) == 0 && (!s.EndInclusive || o.EndInclusive)),
                        IOpenStartRange<T> o => s.End.CompareTo(o.End) < 0 ||
                                                (s.End.CompareTo(o.End) == 0 && (!s.EndInclusive || o.EndInclusive)),
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
                        IFiniteRange<T> o => b.Start.CompareTo(o.Start) > 0 ||
                                             (b.Start.CompareTo(o.Start) == 0 && (!b.StartInclusive || o.StartInclusive)),
                        IOpenEndRange<T> e => b.Start.CompareTo(e.Start) > 0 ||
                                              (b.Start.CompareTo(e.Start) == 0 && (!b.StartInclusive || e.StartInclusive)),
                        IOpenStartRange<T> => true,
                        IInfinityRange<T>  => true,
                        _                  => false
                    },

                IOpenEndRange<T> e =>
                    other switch
                    {
                        IFiniteRange<T> o => e.Start.CompareTo(o.Start) > 0 ||
                                             (e.Start.CompareTo(o.Start) == 0 && (!e.StartInclusive || o.StartInclusive)),
                        IOpenEndRange<T> o => e.Start.CompareTo(o.Start) > 0 ||
                                              (e.Start.CompareTo(o.Start) == 0 && (!e.StartInclusive || o.StartInclusive)),
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
                               IFiniteRange<T> o => BoundaryMeetsAdjacently(
                                                        b.End, b.EndInclusive, o.Start, o.StartInclusive, discrete
                                                    ) ||
                                                    BoundaryMeetsAdjacently(
                                                        o.End, o.EndInclusive, b.Start, b.StartInclusive, discrete
                                                    ),

                               // s extends left from s.End; b's start must meet s.End
                               IOpenStartRange<T> s => BoundaryMeetsAdjacently(
                                   s.End, s.EndInclusive, b.Start, b.StartInclusive, discrete
                               ),

                               // e extends right from e.Start; b's end must meet e.Start
                               IOpenEndRange<T> e => BoundaryMeetsAdjacently(
                                   b.End, b.EndInclusive, e.Start, e.StartInclusive, discrete
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
        public TRange Intersect(IRange<T> other) =>
            !range.Overlaps(other)
                ? TRange.Empty
                : other is IInfinityRange<T>
                    ? range
                    : range.IntersectWith<TRange>(other);

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
        public TRange Merge(IRange<T> other) =>
            !range.Overlaps(other) && !range.IsAdjacentTo(other) ? TRange.Empty :
            other is IInfinityRange<T>                           ? TRange.Infinite : range.MergeWith<TRange>(other);

        /// <summary>
        /// Returns the smallest range that contains both this range and <paramref name="other"/>.
        /// Identical to <see cref="Merge"/>.
        /// </summary>
        /// <param name="other">The range to compute the union with.</param>
        /// <returns>
        /// The union of this range and <paramref name="other"/>; <see cref="IRangeFactory{TRange,T}.Empty"/>
        /// if the ranges are neither overlapping nor adjacent.
        /// </returns>
        public TRange Union(IRange<T> other) => range.Merge(other);

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
            if (!range.Overlaps(other)) return (range, default);
            if (other.Contains(range)) return (TRange.Empty, default);
            if (range is IInfinityRange<T>) return ExceptEngine.InfinityExcept<TRange, T>(other);
            return range switch
                   {
                       IFiniteRange<T> b    => ExceptEngine.Execute<TRange, T>(b, other),
                       IOpenStartRange<T> s => ExceptEngine.Execute<TRange, T>(s, other),
                       IOpenEndRange<T> e   => ExceptEngine.Execute<TRange, T>(e, other),
                       _                    => (range, default)
                   };
        }
    }
}
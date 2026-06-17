using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, normalized set of disjoint ranges — the in-memory counterpart of a
/// PostgreSQL multirange (e.g. <c>int4multirange</c>, <c>nummultirange</c>).
/// </summary>
/// <remarks>
/// <para>
/// The set maintains a structural invariant at all times: its elements are sorted by lower
/// bound, pairwise disjoint, and pairwise non-adjacent. Empty ranges are filtered out on
/// construction, overlapping or adjacent inputs are merged, and any
/// <see cref="IInfinityRange{T}"/> element collapses the whole set to <see cref="Infinite"/>.
/// </para>
/// <para>
/// Because of this invariant, the set can represent results that a single range cannot:
/// the union of two disjoint ranges is simply a two-element set, and a subtraction that
/// splits a range yields its pieces side by side. Zero elements means the empty set.
/// </para>
/// <para>
/// The set operations (<see cref="Union(RangeSet{TRange,T})"/>, <see cref="Intersect(RangeSet{TRange,T})"/>,
/// <see cref="Except(RangeSet{TRange,T})"/>) and the query operations (<see cref="Contains(T)"/>,
/// <see cref="Contains(IRange{T})"/>, <see cref="Overlaps(IRange{T})"/>) exploit the invariant
/// for sub-linear or merge-join complexity rather than nested loops.
/// </para>
/// </remarks>
/// <typeparam name="TRange">The concrete range type the set is composed of.</typeparam>
/// <typeparam name="T">The element type of the ranges.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class RangeSet<TRange, T>
    : IReadOnlyList<TRange>, IEquatable<RangeSet<TRange, T>>, IFormattable, IParsable<RangeSet<TRange, T>>
    where TRange : IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>The empty set — contains no ranges and no values.</summary>
    public static RangeSet<TRange, T> Empty { get; } = new([]);

    /// <summary>The set covering the entire domain — a single <see cref="IInfinityRange{T}"/> element.</summary>
    public static RangeSet<TRange, T> Infinite { get; } = new([TRange.Infinite]);

    /// <summary>
    /// A singleton <see cref="IComparer{TRange}"/> that orders ranges by their lower bound,
    /// matching the internal sort used by <see cref="From"/>. Use this to sort arbitrary
    /// <see cref="List{TRange}"/>s the same way the set does — for example, to pre-sort inputs
    /// before handing them to <see cref="From"/>, or to compare two sequences of ranges
    /// structurally. <see cref="IUnboundedStartRange{T}"/> sorts first; an inclusive lower
    /// bound sorts before an exclusive one at the same value.
    /// </summary>
    public static IComparer<TRange> LowerBoundComparer => RangeLowerBoundComparer<TRange, T>.Instance;

    private readonly ImmutableArray<TRange> _elements;

    // True when the set is the Infinite singleton (a single IInfinityRange element).
    // Because Normalize collapses any Infinity input to [TRange.Infinite], a set containing
    // an Infinity element always has exactly one element.
    private bool IsInfiniteSet => _elements.Length == 1 && _elements[0] is IInfinityRange<T>;

    // Trusted: the caller guarantees the elements already satisfy the invariant
    // (sorted, disjoint, non-adjacent, no empties, no infinity alongside others).
    private RangeSet(ImmutableArray<TRange> elements) => _elements = elements;

    /// <summary>
    /// Creates a normalized set from arbitrary ranges: empty ranges are dropped, the rest
    /// are sorted by lower bound, and overlapping or adjacent neighbours are merged.
    /// Any <see cref="IInfinityRange{T}"/> input collapses the result to <see cref="Infinite"/>.
    /// </summary>
    /// <param name="ranges">The ranges to build the set from, in any order.</param>
    /// <returns>The normalized range set.</returns>
    public static RangeSet<TRange, T> From(IEnumerable<TRange> ranges)
        => CollapseSingletons(Normalize(ranges));

    // Applies the singleton collapses (empty → Empty, single Infinity → Infinite) to an
    // already-normalized array. Used by From and by the merge-join set operations, which
    // produce output that already satisfies the invariant and so can skip re-normalization.
    private static RangeSet<TRange, T> CollapseSingletons(ImmutableArray<TRange> normalized)
        => normalized.Length switch
           {
               0                                         => Empty,
               1 when normalized[0] is IInfinityRange<T> => Infinite,
               _                                         => new(normalized)
           };

    private static ImmutableArray<TRange> Normalize(IEnumerable<TRange> ranges)
    {
        // Fast path: a materialized single-element collection holding a non-empty,
        // non-infinity range is already normalized — skip the list/sort/merge entirely.
        // This is the common case for RangeSet.From([range]) and the wrap path used by
        // RangeExtensions.Union/Except when one operand is a single range.
        if (ranges is IReadOnlyCollection<TRange> { Count: 1 } single)
        {
            var only = single.First();
            if (only is IEmptyRange<T>) return [];
            if (only is IInfinityRange<T>) return [TRange.Infinite];
            return [only];
        }

        var working = new List<TRange>();
        foreach (var range in ranges)
        {
            if (range is IEmptyRange<T>) continue;
            if (range is IInfinityRange<T>) return [TRange.Infinite];
            working.Add(range);
        }

        return working.Count switch
               {
                   0 => [],
                   1 => [working[0]], // already trivially sorted and merged
                   _ => GreedyMerge(working, alreadySorted: false)
               };
    }

    // Greedy left-to-right merge of adjacent or overlapping neighbours. The input must be
    // sorted by lower bound (the RangeSet invariant); if `alreadySorted` is false the
    // working list is sorted in place first. The output preserves the sorted invariant and
    // is pairwise disjoint and pairwise non-adjacent.
    private static ImmutableArray<TRange> GreedyMerge(List<TRange> sorted, bool alreadySorted)
    {
        if (!alreadySorted)
            sorted.Sort(RangeSetHelpers.CompareByLowerBound<TRange, T>);

        var builder = ImmutableArray.CreateBuilder<TRange>(sorted.Count);
        var current = sorted[0];
        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            if (current.Overlaps(next) || current.IsAdjacentTo(next))
            {
                current = current.MergeWith<TRange>(next);
            }
            else
            {
                builder.Add(current);
                current = next;
            }
        }

        builder.Add(current);
        return builder.ToImmutable();
    }

    // Merges two already-normalized element arrays (each sorted, disjoint, non-adjacent,
    // no empties, no Infinity) into a single stream sorted by lower bound. Used by Union
    // to avoid re-sorting two inputs that are each already sorted.
    private static ImmutableArray<TRange> MergeSorted(
        ImmutableArray<TRange> a,
        ImmutableArray<TRange> b
    )
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;

        var builder = ImmutableArray.CreateBuilder<TRange>(a.Length + b.Length);
        int i       = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (RangeSetHelpers.CompareByLowerBound<TRange, T>(a[i], b[j]) <= 0)
                builder.Add(a[i++]);
            else
                builder.Add(b[j++]);
        }

        while (i < a.Length) builder.Add(a[i++]);
        while (j < b.Length) builder.Add(b[j++]);
        return builder.MoveToImmutable();
    }

    // -------------------------------------------------------------------------
    // IReadOnlyList<TRange>
    // -------------------------------------------------------------------------

    /// <summary>The number of disjoint ranges in the set.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the range at <paramref name="index"/>, in lower-bound order.</summary>
    /// <param name="index">The zero-based index.</param>
    public TRange this[int index] => _elements[index];

    /// <summary>Enumerates the disjoint ranges in lower-bound order.</summary>
    public IEnumerator<TRange> GetEnumerator() => ((IEnumerable<TRange>)_elements).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // -------------------------------------------------------------------------
    // Query operations
    // -------------------------------------------------------------------------

    /// <summary>
    /// Determines whether <paramref name="value"/> is contained in any range of the set.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if some element contains the value.</returns>
    /// <remarks>
    /// Uses a binary search on the sorted lower bounds to locate the single element that
    /// could possibly contain <paramref name="value"/> (the last element whose lower bound
    /// is at or below the value), giving O(log n) instead of O(n).
    /// </remarks>
    public bool Contains(T value)
    {
        int idx = RangeSetHelpers.LastIndexWithLowerBoundAtOrBelow<TRange, T>(_elements, value);
        return idx >= 0 && _elements[idx].Contains(value);
    }

    /// <summary>
    /// Determines whether <paramref name="range"/> is entirely contained within the set.
    /// </summary>
    /// <remarks>
    /// Because elements are pairwise disjoint and non-adjacent, a contiguous range can only
    /// be contained by a single element — spanning two elements would cross a real gap.
    /// A binary search on lower bounds locates the only candidate element in O(log n).
    /// </remarks>
    /// <param name="range">The range to test.</param>
    /// <returns><see langword="true"/> if some element contains <paramref name="range"/>.</returns>
    public bool Contains(IRange<T> range)
    {
        if (_elements.IsEmpty) return false;
        if (range is IEmptyRange<T>) return false;
        if (range is IInfinityRange<T>) return IsInfiniteSet;

        // Unbounded-start query (-∞, E]: only an IUnboundedStartRange element (which sorts
        // first) could contain it; if the first element isn't one, nothing can.
        if (range is IUnboundedStartRange<T>)
            return _elements[0] is IUnboundedStartRange<T> && _elements[0].Contains(range);

        // Query has a finite lower bound S: find the last element whose lower bound ≤ S —
        // the only candidate that could contain the query. Earlier elements end before S
        // (disjoint + sorted); later elements start after S.
        var (lowerValue, _, lowerInfinite) = RangeSetHelpers.RangeLowerBound<T>(range);
        if (lowerInfinite) return false; // unreachable: handled by the IUnboundedStartRange branch above
        int idx = RangeSetHelpers.LastIndexWithLowerBoundAtOrBelow<TRange, T>(_elements, lowerValue);
        return idx >= 0 && _elements[idx].Contains(range);
    }

    /// <summary>
    /// Determines whether <paramref name="range"/> shares at least one value with the set.
    /// </summary>
    /// <param name="range">The range to test.</param>
    /// <returns><see langword="true"/> if some element overlaps <paramref name="range"/>.</returns>
    /// <remarks>
    /// Binary search on lower bounds narrows the test to a single candidate element in
    /// O(log n). The candidate is the rightmost element whose lower bound is at or below
    /// <paramref name="range"/>'s upper bound; if its upper bound is below the query's
    /// lower bound, no element overlaps (upper bounds are strictly increasing in a
    /// normalized set, so all earlier elements end even earlier).
    /// </remarks>
    public bool Overlaps(IRange<T> range)
    {
        if (_elements.IsEmpty || range is IEmptyRange<T>) return false;
        if (range is IInfinityRange<T>) return true; // any non-empty element overlaps infinity

        // Unbounded-end query [S, +∞): only the last element could possibly extend past S,
        // because upper bounds are strictly increasing. If the last element's upper bound
        // is below S, no element reaches S; otherwise the last element overlaps.
        if (range is IUnboundedEndRange<T>)
            return _elements[^1].Overlaps(range);

        // Query has a finite upper bound E: find the last element whose lower bound ≤ E.
        var (_, _, upperInfinite) = RangeSetHelpers.RangeUpperBound<T>(range);
        if (upperInfinite) return false; // unreachable: handled by the IUnboundedEndRange branch above
        var (upperValue, _, _) = RangeSetHelpers.RangeUpperBound<T>(range);
        int idx = RangeSetHelpers.LastIndexWithLowerBoundAtOrBelow<TRange, T>(_elements, upperValue);
        return idx >= 0 && _elements[idx].Overlaps(range);
    }

    // -------------------------------------------------------------------------
    // Set operations
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the union of this set and <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The range to add.</param>
    /// <returns>
    /// A normalized set containing every value of this set and of <paramref name="other"/>;
    /// the range is merged into existing elements where it overlaps or is adjacent.
    /// </returns>
    public RangeSet<TRange, T> Union(TRange other) =>
        other is IEmptyRange<T> ? this : From(_elements.Append(other));

    /// <summary>
    /// Returns the union of this set and <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The set to combine with.</param>
    /// <returns>A normalized set containing every value of both sets.</returns>
    /// <remarks>
    /// Both inputs are already sorted by lower bound, so they are merged into a single
    /// sorted stream in O(n + m) (rather than re-sorting the concatenation), then the
    /// greedy neighbour-merge runs once over the merged stream.
    /// </remarks>
    public RangeSet<TRange, T> Union(RangeSet<TRange, T> other)
    {
        if (other.Count == 0) return this;
        if (Count       == 0) return other;
        if (IsInfiniteSet || other.IsInfiniteSet) return Infinite;

        var merged = MergeSorted(_elements, other._elements);
        // The merged stream is sorted but may contain overlapping/adjacent neighbours
        // across the two inputs — run the greedy merge once.
        var builder = ImmutableArray.CreateBuilder<TRange>(merged.Length);
        var current = merged[0];
        for (int i = 1; i < merged.Length; i++)
        {
            var next = merged[i];
            if (current.Overlaps(next) || current.IsAdjacentTo(next))
                current = current.MergeWith<TRange>(next);
            else
            {
                builder.Add(current);
                current = next;
            }
        }

        builder.Add(current);
        return CollapseSingletons(builder.ToImmutable());
    }

    /// <summary>
    /// Returns the union of <paramref name="left"/> and <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The set to add to.</param>
    /// <param name="right">The range to add.</param>
    /// <returns>
    /// A normalized set containing every value of <paramref name="left"/> and of <paramref name="right"/>;
    /// the range is merged into existing elements where it overlaps or is adjacent.
    /// </returns>
    public static RangeSet<TRange, T> operator |(RangeSet<TRange, T> left, TRange right) =>
        left.Union(right);

    /// <summary>
    /// Returns the union of <paramref name="left"/> and <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The set to add to.</param>
    /// <param name="right">The set to combine with.</param>
    /// <returns>A normalized set containing every value of both sets.</returns>
    public static RangeSet<TRange, T> operator |(RangeSet<TRange, T> left, RangeSet<TRange, T> right) =>
        left.Union(right);

    /// <summary>
    /// Returns the intersection of this set with <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The range to intersect with.</param>
    /// <returns>
    /// A set containing, for each element, its overlap with <paramref name="other"/>;
    /// <see cref="Empty"/> when nothing overlaps.
    /// </returns>
    public RangeSet<TRange, T> Intersect(TRange other)
    {
        if (other is IEmptyRange<T>) return Empty;

        // Clipping each element against the same range preserves order, disjointness,
        // and non-adjacency (gaps can only grow) — the invariant holds without re-normalizing.
        var builder = ImmutableArray.CreateBuilder<TRange>();
        foreach (var element in _elements)
        {
            var intersection = element.Intersect(other);
            if (intersection is not IEmptyRange<T>) builder.Add(intersection);
        }

        return builder.Count == 0 ? Empty : new(builder.ToImmutable());
    }

    /// <summary>
    /// Returns the intersection of this set with <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The set to intersect with.</param>
    /// <returns>A normalized set containing the values common to both sets.</returns>
    /// <remarks>
    /// Both inputs are sorted, disjoint, and non-adjacent, so a two-pointer merge-join
    /// walks them in O(n + m) intersection calls (instead of the naive O(n · m) nested
    /// loop). The output is already sorted, disjoint, and non-adjacent — proved by case
    /// analysis on the pointer advance: each advance emits at most one intersection whose
    /// bounds lie strictly inside both input elements, so consecutive outputs inherit the
    /// real gaps of whichever input advanced — so the private constructor is used directly,
    /// skipping the re-normalization that <see cref="From"/> would perform.
    /// </remarks>
    public RangeSet<TRange, T> Intersect(RangeSet<TRange, T> other)
    {
        if (Count == 0 || other.Count == 0) return Empty;
        if (IsInfiniteSet) return other;
        if (other.IsInfiniteSet) return this;

        var results = ImmutableArray.CreateBuilder<TRange>();
        int i       = 0, j = 0;
        while (i < _elements.Length && j < other._elements.Length)
        {
            var a            = _elements[i];
            var b            = other._elements[j];
            var intersection = a.Intersect(b);
            if (intersection is not IEmptyRange<T>)
                results.Add(intersection);
            // Advance the pointer whose element ends first — that element can no longer
            // overlap any element the other iterator will reach.
            if (RangeSetHelpers.UpperBoundLessThan<T>(a, b))
                i++;
            else
                j++;
        }

        return CollapseSingletons(results.ToImmutable());
    }

    /// <summary>
    /// Returns the intersection of <paramref name="left"/> with <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The set to intersect.</param>
    /// <param name="right">The range to intersect with.</param>
    /// <returns>
    /// A set containing, for each element of <paramref name="left"/>, its overlap with <paramref name="right"/>;
    /// <see cref="Empty"/> when nothing overlaps.
    /// </returns>
    public static RangeSet<TRange, T> operator &(RangeSet<TRange, T> left, TRange right) =>
        left.Intersect(right);

    /// <summary>
    /// Returns the intersection of <paramref name="left"/> with <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The set to intersect.</param>
    /// <param name="right">The set to intersect with.</param>
    /// <returns>A normalized set containing the values common to both sets.</returns>
    public static RangeSet<TRange, T> operator &(RangeSet<TRange, T> left, RangeSet<TRange, T> right) =>
        left.Intersect(right);

    /// <summary>
    /// Returns what remains of this set after removing every value covered by <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The range to subtract.</param>
    /// <returns>
    /// A normalized set of the remaining pieces; an element split by
    /// <paramref name="other"/> contributes both of its parts.
    /// </returns>
    public RangeSet<TRange, T> Except(TRange other)
    {
        if (other is IEmptyRange<T>) return this;
        if (IsInfiniteSet) return ComplementOfSingle(other);

        var results = new List<TRange>();
        foreach (var element in _elements)
            results.AddRange(element.Except(other));

        return From(results);
    }

    /// <summary>
    /// Returns what remains of this set after removing every value covered by <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The set to subtract.</param>
    /// <returns>A normalized set of the remaining pieces.</returns>
    /// <remarks>
    /// Both inputs are sorted, disjoint, and non-adjacent, so a two-pointer walk subtracts
    /// <paramref name="other"/>'s elements from this set's elements in a single pass. Each
    /// element of this set is trimmed by the (contiguous run of) <paramref name="other"/>
    /// elements that overlap it; the <c>j</c> pointer only ever advances because
    /// <paramref name="other"/>'s elements that end before the current <c>this</c> element
    /// cannot overlap any later <c>this</c> element either. This avoids the
    /// O(|other| · normalization) cost of subtracting one element at a time and
    /// re-normalizing after each step.
    /// </remarks>
    public RangeSet<TRange, T> Except(RangeSet<TRange, T> other)
    {
        if (other.Count == 0) return this;
        if (other.IsInfiniteSet) return Empty;
        if (Count == 0) return this;
        if (IsInfiniteSet) return ComplementOfSet(other);

        return MergeExcept(other);
    }

    // Two-pointer subtraction: walks `this` and `other` together. For each element of `this`,
    // finds the run of `other` elements that overlap it and trims the element in place,
    // emitting left pieces as they are split off and keeping the right piece as `current`.
    // `j` is the durable cursor into `other` (monotonic across elements of `this`); `jj`
    // is the per-element probe cursor so the next element of `this` starts where the
    // previous one left off (an `other` element that ended inside the previous `this`
    // element may also overlap the next one).
    private RangeSet<TRange, T> MergeExcept(RangeSet<TRange, T> other)
    {
        var results       = new List<TRange>();
        int j             = 0;
        var otherElements = other._elements;

        foreach (var ti in _elements)
        {
            // Skip other elements that end strictly before ti starts — they can no longer
            // overlap ti or any later element of this set.
            while (j < otherElements.Length && RangeSetHelpers.IsStrictlyLeftOf<T>(otherElements[j], ti))
                j++;

            var current = ti;
            int jj      = j;
            while (jj < otherElements.Length)
            {
                var o = otherElements[jj];
                // `other` is sorted by lower bound, so once `o` starts strictly right of
                // `current`'s end, no later `o` can overlap `current` either.
                if (!current.Overlaps(o)) break;
                // `o` fully consumes `current`: nothing left to emit from this element.
                if (o.Contains(current))
                {
                    current = TRange.Empty; // sentinel: fully consumed
                    break;
                }

                // Subtract o from current via the same engine RangeExtensions.Except uses,
                // returning the leftover piece(s) directly — no intermediate RangeSet.
                var (left, right) = SubtractOne(current, o);
                if (right is null)
                {
                    current = left;
                }
                else
                {
                    results.Add(left);
                    current = right;
                }

                jj++;
            }

            if (current is not IEmptyRange<T>)
                results.Add(current);
        }

        return From(results);
    }

    // Dispatches a single subtraction (current \ o) to ExceptEngine by current's shape.
    // Callers guarantee `current.Overlaps(o)` and `!o.Contains(current)`, so the engine
    // always returns a non-empty Left and an optional non-empty Right (one-piece trim or
    // two-piece split). `current` is never IInfinityRange here (the Infinite case is
    // handled by ComplementOfSet/ComplementOfSingle before this is reached).
    private static (TRange Left, TRange? Right) SubtractOne(TRange current, IRange<T> o)
        => current switch
           {
               IFiniteRange<T> b         => ExceptEngine.Execute<TRange, T>(b, o),
               IUnboundedStartRange<T> s => ExceptEngine.Execute<TRange, T>(s, o),
               IUnboundedEndRange<T> e   => ExceptEngine.Execute<TRange, T>(e, o),
               _                         => (current, default)
           };

    // Complement of a single range within the entire domain: (-∞, +∞) \ r.
    // Delegates to the existing ExceptEngine.InfinityExcept, which already returns the
    // correct one- or two-piece result for every shape of r.
    private RangeSet<TRange, T> ComplementOfSingle(TRange r)
    {
        var (left, right) = ExceptEngine.InfinityExcept<TRange, T>(r);
        return right is null
                   ? RangeSet<TRange, T>.From([left])
                   : RangeSet<TRange, T>.From([left, right]);
    }

    // Complement of a normalized (sorted, disjoint, non-adjacent, non-Infinite) set within
    // the entire domain: (-∞, +∞) \ other. Walks `other` once, emitting the gaps between
    // consecutive elements plus the unbounded stretches before the first and after the last
    // element, with boundary inclusiveness flipped at each cut point. Runs in O(|other|)
    // instead of O(|other|²) which the repeated `Except(TRange)` accumulation would cost.
    private static RangeSet<TRange, T> ComplementOfSet(RangeSet<TRange, T> other)
    {
        var results = new List<TRange>(other._elements.Length + 1);

        // The lower "cursor" starts at -∞ and advances to each element's upper bound
        // (with inclusiveness flipped) after the gap before that element is emitted.
        bool cursorInfinite  = true; // cursor is -∞ initially
        T    cursorValue     = default!;
        bool cursorInclusive = false;

        foreach (var o in other._elements)
        {
            var (oLower, oLowerIncl, oLowerInf, oUpper, oUpperIncl, oUpperInf) =
                BoundsOf(o);

            // Emit the piece from the cursor to o's lower bound, with inclusiveness
            // flipped at o's lower bound. If o is unbounded on the left, this piece
            // would be (-∞, -∞) — empty — so it is skipped.
            if (!oLowerInf)
            {
                if (cursorInfinite)
                    results.Add(TRange.CreateUnboundedStart(oLower, !oLowerIncl));
                else
                    results.Add(TRange.CreateFinite(
                                    cursorValue, oLower, cursorInclusive, !oLowerIncl));
            }

            // Advance the cursor to o's upper bound (with inclusiveness flipped).
            if (oUpperInf)
            {
                // o extends to +∞, so the "after" stretch is empty. Since `other` is
                // sorted + disjoint, no later element can follow — stop emitting.
                cursorInfinite = true;
                break;
            }

            cursorInfinite  = false;
            cursorValue     = oUpper;
            cursorInclusive = !oUpperIncl;
        }

        // Final piece: from the cursor to +∞. Skipped if the cursor is +∞ (we broke out
        // above after an IUnboundedEndRange element) or -∞ (only when `other` is empty,
        // which the caller excludes — ComplementOfSet is only reached for non-empty other).
        if (!cursorInfinite)
            results.Add(TRange.CreateUnboundedEnd(cursorValue, cursorInclusive));

        return From(results);
    }

    // Returns (Lower, LowerIncl, LowerInfinite, Upper, UpperIncl, UpperInfinite) for any
    // non-empty range shape. The Lower/Upper values are `default` for unbounded sides and
    // must not be read when the corresponding Infinite flag is true. IInfinityRange is
    // included for completeness; callers that reach BoundsOf have already excluded it via
    // IsInfiniteSet.
    private static (T Lower, bool LowerIncl, bool LowerInfinite,
        T Upper, bool UpperIncl, bool UpperInfinite) BoundsOf(IRange<T> range)
        => range switch
           {
               IFiniteRange<T> f => (f.Start, f.StartInclusive, false,
                                     f.End, f.EndInclusive, false),
               IUnboundedStartRange<T> s => (default!, false, true,
                                             s.End, s.EndInclusive, false),
               IUnboundedEndRange<T> e => (e.Start, e.StartInclusive, false,
                                           default!, false, true),
               IInfinityRange<T> => (default!, false, true,
                                     default!, false, true),
               _ => throw new InvalidOperationException(
                        $"Range shape '{range.GetType().Name}' has no bounds.")
           };

    /// <summary>
    /// Returns what remains of <paramref name="left"/> after removing every value covered by <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The set to subtract from.</param>
    /// <param name="right">The range to subtract.</param>
    /// <returns>
    /// A normalized set of the remaining pieces; an element split by
    /// <paramref name="right"/> contributes both of its parts.
    /// </returns>
    public static RangeSet<TRange, T> operator -(RangeSet<TRange, T> left, TRange right) =>
        left.Except(right);

    /// <summary>
    /// Returns what remains of <paramref name="left"/> after removing every value covered by <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The set to subtract from.</param>
    /// <param name="right">The set to subtract.</param>
    /// <returns>A normalized set of the remaining pieces.</returns>
    public static RangeSet<TRange, T> operator -(RangeSet<TRange, T> left, RangeSet<TRange, T> right) =>
        left.Except(right);

    /// <summary>
    /// Returns the complement of this set — every value of the domain not covered by it.
    /// </summary>
    /// <returns>
    /// <see cref="Infinite"/> minus this set: the gaps between elements plus the unbounded
    /// stretches before the first and after the last element.
    /// </returns>
    public RangeSet<TRange, T> Complement() => Infinite.Except(this);

    // -------------------------------------------------------------------------
    // Equality
    // -------------------------------------------------------------------------

    /// <summary>
    /// Determines structural equality: two sets are equal when their normalized element
    /// sequences are equal, regardless of the inputs they were built from.
    /// </summary>
    /// <param name="other">The set to compare with.</param>
    /// <returns><see langword="true"/> if both sets contain the same ranges.</returns>
    public bool Equals(RangeSet<TRange, T>? other) =>
        other is not null && (ReferenceEquals(this, other) || _elements.SequenceEqual(other._elements));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RangeSet<TRange, T>);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var element in _elements) hash.Add(element);
        return hash.ToHashCode();
    }

    // -------------------------------------------------------------------------
    // IFormattable / IParsable
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the PostgreSQL multirange literal for this set, e.g. <c>{[1,3],[5,7]}</c>.
    /// The optional <paramref name="format"/> string is forwarded to each element's formatter.
    /// </summary>
    public string ToString(string? format, IFormatProvider? provider)
    {
        if (_elements.IsEmpty)
            return "{}";

        var sb = new StringBuilder("{");
        sb.Append(_elements[0].ToString(format, provider));
        for (var i = 1; i < _elements.Length; i++)
        {
            sb.Append(',');
            sb.Append(_elements[i].ToString(format, provider));
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>Returns the PostgreSQL multirange literal for this set, e.g. <c>{[1,3],[5,7]}</c>.</summary>
    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL multirange literal (e.g. <c>{[1,5],[7,10]}</c> or <c>{}</c>)
    /// into a <see cref="RangeSet{TRange,T}"/>.
    /// </summary>
    public static RangeSet<TRange, T> Parse(string s, IFormatProvider? provider)
    {
        var literals = RangeFormat.SplitSetLiterals(s.AsSpan());
        return From(literals.Select(l => TRange.Parse(l, provider)));
    }

    /// <summary>
    /// Tries to parse a PostgreSQL multirange literal into a <see cref="RangeSet{TRange,T}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out RangeSet<TRange, T> result)
    {
        try
        {
            var literals = RangeFormat.SplitSetLiterals((s ?? string.Empty).AsSpan());
            result = From(literals.Select(l => TRange.Parse(l, provider)));
            return true;
        }
        catch
        {
            result = Empty;
            return false;
        }
    }

    // Explicit interface implementations delegate to the public methods above.
    static RangeSet<TRange, T> IParsable<RangeSet<TRange, T>>.Parse(string s, IFormatProvider? provider)
        => Parse(s, provider);

    static bool IParsable<RangeSet<TRange, T>>.TryParse(string? s, IFormatProvider? provider, out RangeSet<TRange, T> result)
        => TryParse(s, provider, out result);
}
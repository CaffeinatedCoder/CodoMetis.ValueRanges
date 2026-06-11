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

    private readonly ImmutableArray<TRange> _elements;

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
    {
        var normalized = Normalize(ranges);
        return normalized.Length switch
               {
                   0                                         => Empty,
                   1 when normalized[0] is IInfinityRange<T> => Infinite,
                   _                                         => new(normalized)
               };
    }

    private static ImmutableArray<TRange> Normalize(IEnumerable<TRange> ranges)
    {
        var working = new List<TRange>();
        foreach (var range in ranges)
        {
            if (range is IEmptyRange<T>) continue;
            if (range is IInfinityRange<T>) return [TRange.Infinite];
            working.Add(range);
        }

        if (working.Count == 0) return [];

        working.Sort(RangeSetHelpers.CompareByLowerBound<TRange, T>);

        var builder = ImmutableArray.CreateBuilder<TRange>();
        var current = working[0];
        for (int i = 1; i < working.Count; i++)
        {
            var next = working[i];
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
    public bool Contains(T value) =>
        Enumerable.Any(_elements, element => element.Contains(value));

    /// <summary>
    /// Determines whether <paramref name="range"/> is entirely contained within the set.
    /// </summary>
    /// <remarks>
    /// Because elements are pairwise disjoint and non-adjacent, a contiguous range can only
    /// be contained by a single element — spanning two elements would cross a real gap.
    /// </remarks>
    /// <param name="range">The range to test.</param>
    /// <returns><see langword="true"/> if some element contains <paramref name="range"/>.</returns>
    public bool Contains(IRange<T> range) =>
        Enumerable.Any(_elements, element => element.Contains(range));

    /// <summary>
    /// Determines whether <paramref name="range"/> shares at least one value with the set.
    /// </summary>
    /// <param name="range">The range to test.</param>
    /// <returns><see langword="true"/> if some element overlaps <paramref name="range"/>.</returns>
    public bool Overlaps(IRange<T> range) =>
        Enumerable.Any(_elements, element => element.Overlaps(range));

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
    public RangeSet<TRange, T> Union(RangeSet<TRange, T> other) =>
        other.Count == 0 ? this :
        Count       == 0 ? other : From(_elements.Concat(other._elements));

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
    public RangeSet<TRange, T> Intersect(RangeSet<TRange, T> other)
    {
        var results = new List<TRange>();
        foreach (var a in _elements)
        foreach (var b in other._elements)
        {
            var intersection = a.Intersect(b);
            if (intersection is not IEmptyRange<T>) results.Add(intersection);
        }

        return From(results);
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
    public RangeSet<TRange, T> Except(RangeSet<TRange, T> other) =>
        Enumerable.Aggregate(other._elements, this, (current, range) => current.Except(range));

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
using System.Diagnostics;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over 64-bit signed integers, equivalent to the PostgreSQL <c>int8range</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[start, end]</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public abstract record Int64Range : IRange<long>, IRangeFactory<Int64Range, long>
{
    private Int64Range()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="Int64Range"/> that contains no values.
    /// </summary>
    public sealed record EmptyRange : Int64Range, IEmptyRange<long>;

    /// <summary>
    /// Represents an <see cref="Int64Range"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : Int64Range, IFiniteRange<long>
    {
        internal Finite(long start, long end)
        {
            Start = start;
            End   = end;
        }

        /// <inheritdoc/>
        public long Start { get; }

        /// <inheritdoc/>
        public long End { get; }

        /// <inheritdoc/>
        public bool StartInclusive => true;

        /// <inheritdoc/>
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    public sealed record UnboundedStart(long End) : Int64Range, IUnboundedStartRange<long>
    {
        /// <inheritdoc />
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    public sealed record UnboundedEnd(long Start) : Int64Range, IUnboundedEndRange<long>
    {
        /// <inheritdoc />
        public bool StartInclusive => true;
    }

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : Int64Range, IInfinityRange<long>;

    /// <summary>
    /// Creates an <see cref="Int64Range"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static Int64Range CreateUnboundedStart(long end, bool endInclusive = false) =>
        endInclusive
            ? new UnboundedStart(end)
            : PreviousValueBefore(end) is { } e
                ? new UnboundedStart(e) // (-∞, end) ≡ (-∞, end - 1]
                : Empty;                // (-∞, int.MinValue) contains nothing

    /// <summary>
    /// Creates an <see cref="Int64Range"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static Int64Range CreateUnboundedEnd(long start, bool startInclusive = true) =>
        startInclusive
            ? new UnboundedEnd(start)
            : NextValueAfter(start) is { } s
                ? new UnboundedEnd(s) // (start, +∞) ≡ [start + 1, +∞)
                : Empty;              // (int.MaxValue, +∞) contains nothing

    /// <summary>
    /// Creates an <see cref="Int64Range"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all long integer values.</returns>
    public static Int64Range Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="Int64Range"/> that contains no values.
    /// </summary>
    public static Int64Range Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates an <see cref="Int64Range"/> bounded on both sides.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Finite"/> range when <paramref name="start"/> is strictly less than
    /// <paramref name="end"/>, or when they are equal and both bounds are inclusive.
    /// Returns <see cref="EmptyRange"/> when <paramref name="start"/> is greater than
    /// <paramref name="end"/>, or when the bounds are equal but not both inclusive.
    /// </returns>
    public static Int64Range CreateFinite(
        long start,
        long end,
        bool startInclusive = true,
        bool endInclusive   = true
    ) => DiscreteCanonical.Finite<Int64Range, long>(start, end, startInclusive, endInclusive) is { } b
             ? new Finite(b.Start, b.End)
             : Empty;

    /// <inheritdoc />
    public static bool IsDiscrete => true;

    /// <inheritdoc />
    public static long? NextValueAfter(long value) => value == long.MaxValue ? null : value + 1;

    /// <inheritdoc />
    public static long? PreviousValueBefore(long value) => value == long.MinValue ? null : value - 1;

    /// <summary>
    /// Enumerates the integers the range contains, ascending and inclusive of both bounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Available here because the domain is discrete. The continuous range types have no
    /// step to walk and do not declare this member at all, so asking for the values of a
    /// <c>DecimalRange</c> is a compile error rather than a runtime failure.
    /// </para>
    /// <para>
    /// The empty range yields nothing. An unbounded range would not terminate and throws
    /// immediately — the check is eager, so the failure surfaces at this call rather than at
    /// the <c>foreach</c> that consumes it.
    /// </para>
    /// </remarks>
    /// <returns>The contained integers, ascending.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    public IEnumerable<long> Values() => DiscreteEnumeration.Values<Int64Range, long>(this);

    /// <summary>
    /// The number of integers the range contains, or <see langword="null"/> when it is unbounded
    /// or the count is too large to represent. The empty range measures 0.
    /// </summary>
    /// <remarks>
    /// A count rather than a span, as for <c>Int32Range</c>. Unlike it, the count can exceed the
    /// widest integer available: a range spanning almost the whole <see cref="long"/> domain holds
    /// more values than <see cref="long.MaxValue"/>. That case is computed in
    /// <see cref="decimal"/> and returns <see langword="null"/> rather than wrapping to a
    /// plausible-looking negative.
    /// </remarks>
    public long? Length =>
        this switch
        {
            IEmptyRange<long>    => 0L,
            IFiniteRange<long> f => FiniteLength(f.Start, f.End),
            _                    => null
        };

    private static long? FiniteLength(long start, long end)
    {
        var count = (decimal)end - start + 1m;
        return count <= long.MaxValue ? (long)count : null;
    }

    /// <inheritdoc />
    public static long ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => long.Parse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[1,10]</c>, <c>empty</c>, <c>(,)</c>)
    /// into an <see cref="Int64Range"/>.
    /// </summary>
    public static Int64Range Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<Int64Range, long>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static Int64Range Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<Int64Range, long>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into an <see cref="Int64Range"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int64Range result)
        => RangeFormat.TryParse<Int64Range, long>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Int64Range result)
        => RangeFormat.TryParse<Int64Range, long>(s, provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
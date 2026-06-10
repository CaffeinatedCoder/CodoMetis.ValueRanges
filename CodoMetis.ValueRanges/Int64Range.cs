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
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
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
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
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
    public static long? NextValueAfter(long value) => value == long.MaxValue ? null : value + 1;

    /// <inheritdoc />
    public static long? PreviousValueBefore(long value) => value == long.MinValue ? null : value - 1;
}
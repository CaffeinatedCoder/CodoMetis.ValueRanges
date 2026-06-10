using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over 32-bit signed integers, equivalent to the PostgreSQL <c>int4range</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[start, end]</c>.
/// </remarks>
public abstract record Int32Range : IRange<int>, IRangeFactory<Int32Range, int>
{
    private Int32Range()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="Int32Range"/> that contains no values.
    /// </summary>
    public sealed record EmptyRange : Int32Range, IEmptyRange<int>;

    /// <summary>
    /// Represents an <see cref="Int32Range"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : Int32Range, IFiniteRange<int>
    {
        internal Finite(int start, int end)
        {
            Start = start;
            End   = end;
        }

        /// <inheritdoc/>
        public int Start { get; }

        /// <inheritdoc/>
        public int End { get; }

        /// <inheritdoc/>
        public bool StartInclusive => true;

        /// <inheritdoc/>
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents an <see cref="Int32Range"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    public sealed record UnboundedStart(int End) : Int32Range, IUnboundedStartRange<int>
    {
        /// <inheritdoc />
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents an <see cref="Int32Range"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    public sealed record UnboundedEnd(int Start) : Int32Range, IUnboundedEndRange<int>
    {
        /// <inheritdoc />
        public bool StartInclusive => true;
    }

    /// <summary>
    /// Represents an <see cref="Int32Range"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : Int32Range, IInfinityRange<int>;

    /// <summary>
    /// Creates an <see cref="Int32Range"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static Int32Range CreateUnboundedStart(int end, bool endInclusive = false) =>
        endInclusive
            ? new UnboundedStart(end)
            : PreviousValueBefore(end) is { } e
                ? new UnboundedStart(e) // (-∞, end) ≡ (-∞, end - 1]
                : Empty;                // (-∞, int.MinValue) contains nothing

    /// <summary>
    /// Creates an <see cref="Int32Range"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static Int32Range CreateUnboundedEnd(int start, bool startInclusive = true) =>
        startInclusive
            ? new UnboundedEnd(start)
            : NextValueAfter(start) is { } s
                ? new UnboundedEnd(s) // (start, +∞) ≡ [start + 1, +∞)
                : Empty;              // (int.MaxValue, +∞) contains nothing

    /// <summary>
    /// Creates an <see cref="Int32Range"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all integer values.</returns>
    public static Int32Range Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="Int32Range"/> that contains no values.
    /// </summary>
    public static Int32Range Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates an <see cref="Int32Range"/> bounded on both sides.
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
    public static Int32Range CreateFinite(
        int  start,
        int  end,
        bool startInclusive = true,
        bool endInclusive   = true
    ) => DiscreteCanonical.Finite<Int32Range, int>(start, end, startInclusive, endInclusive) is { } b
             ? new Finite(b.Start, b.End)
             : Empty;

    /// <inheritdoc />
    public static int? NextValueAfter(int value) => value == int.MaxValue ? null : value + 1;

    /// <inheritdoc />
    public static int? PreviousValueBefore(int value) => value == int.MinValue ? null : value - 1;
}
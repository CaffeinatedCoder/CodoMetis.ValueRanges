using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="decimal"/> values, equivalent to the PostgreSQL <c>numrange</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for continuous numeric types such as monetary amounts.
/// </remarks>
public abstract record DecimalRange : IRange<decimal>, IRangeFactory<DecimalRange, decimal>
{
    private DecimalRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="DecimalRange"/> that contains no values.
    /// </summary>
    public sealed record EmptyRange : DecimalRange, IEmptyRange<decimal>;

    /// <summary>
    /// Represents a <see cref="DecimalRange"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : DecimalRange, IFiniteRange<decimal>
    {
        internal Finite(decimal start, decimal end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public decimal Start { get; }

        /// <inheritdoc/>
        public decimal End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="DecimalRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    public sealed record UnboundedStart(decimal End, bool EndInclusive) : DecimalRange, IUnboundedStartRange<decimal>;

    /// <summary>
    /// Represents a <see cref="DecimalRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    public sealed record UnboundedEnd(decimal Start, bool StartInclusive) : DecimalRange, IUnboundedEndRange<decimal>;

    /// <summary>
    /// Represents a <see cref="DecimalRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : DecimalRange, IInfinityRange<decimal>;

    /// <summary>
    /// Creates a <see cref="DecimalRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DecimalRange CreateUnboundedStart(decimal end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="DecimalRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DecimalRange CreateUnboundedEnd(decimal start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="DecimalRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all decimal values.</returns>
    public static DecimalRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="DecimalRange"/> that contains no values.
    /// </summary>
    public static DecimalRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DecimalRange"/> bounded on both sides.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/> (half-open convention).
    /// </param>
    /// <returns>
    /// A <see cref="Finite"/> range when <paramref name="start"/> is strictly less than
    /// <paramref name="end"/>, or when they are equal and both bounds are inclusive.
    /// Returns <see cref="EmptyRange"/> when <paramref name="start"/> is greater than
    /// <paramref name="end"/>, or when the bounds are equal but not both inclusive.
    /// </returns>
    public static DecimalRange CreateFinite(
        decimal start,
        decimal end,
        bool    startInclusive = true,
        bool    endInclusive   = false
    ) =>
        start.CompareTo(end) switch
        {
            > 0 => Empty,
            0 => startInclusive && endInclusive
                     ? new Finite(start, end, startInclusive, endInclusive)
                     : Empty,
            _ => new Finite(start, end, startInclusive, endInclusive)
        };
}
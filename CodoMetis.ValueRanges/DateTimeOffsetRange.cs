namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateTimeOffset"/> values, equivalent to the PostgreSQL <c>tstzrange</c>
/// (timestamp with time zone) type.
/// </summary>
/// <remarks>
/// This is a discriminated union with four variants: <see cref="Finite"/>, <see cref="OpenStart"/>,
/// <see cref="OpenEnd"/>, and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[lower, upper)</c>,
/// which is conventional for timestamp ranges.
/// </remarks>
public abstract record DateTimeOffsetRange : IRange<DateTimeOffset>, IRangeFactory<DateTimeOffsetRange, DateTimeOffset>
{
    private DateTimeOffsetRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="DateTimeOffsetRange"/> that contains no values.
    /// </summary>
    private sealed record EmptyRange : DateTimeOffsetRange, IEmptyRange<DateTimeOffset>;

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> bounded on both sides.
    /// </summary>
    private sealed record Finite : DateTimeOffsetRange, IFiniteRange<DateTimeOffset>
    {
        internal Finite(DateTimeOffset lowerBound, DateTimeOffset upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        /// <inheritdoc/>
        public DateTimeOffset LowerBound { get; }

        /// <inheritdoc/>
        public DateTimeOffset UpperBound { get; }

        /// <inheritdoc/>
        public bool LowerBoundInclusive { get; }

        /// <inheritdoc/>
        public bool UpperBoundInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> unbounded on the left:
    /// <c>(-∞, UpperBound]</c> or <c>(-∞, UpperBound)</c>.
    /// </summary>
    /// <param name="UpperBound">The upper (right) bound of the range.</param>
    /// <param name="UpperBoundInclusive"><see langword="true"/> to include <paramref name="UpperBound"/> in the range.</param>
    private sealed record OpenStart(DateTimeOffset UpperBound, bool UpperBoundInclusive)
        : DateTimeOffsetRange, IOpenStartRange<DateTimeOffset>;

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> unbounded on the right:
    /// <c>[LowerBound, +∞)</c> or <c>(LowerBound, +∞)</c>.
    /// </summary>
    /// <param name="LowerBound">The lower (left) bound of the range.</param>
    /// <param name="LowerBoundInclusive"><see langword="true"/> to include <paramref name="LowerBound"/> in the range.</param>
    private sealed record OpenEnd(DateTimeOffset LowerBound, bool LowerBoundInclusive)
        : DateTimeOffsetRange, IOpenEndRange<DateTimeOffset>;

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> unbounded on the left.
    /// </summary>
    /// <param name="upperBound">The upper (right) bound of the range.</param>
    /// <param name="upperBoundInclusive">
    /// <see langword="true"/> to include <paramref name="upperBound"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="OpenStart"/> range: <c>(-∞, upperBound]</c> or <c>(-∞, upperBound)</c>.</returns>
    public static DateTimeOffsetRange CreateOpenStart(DateTimeOffset upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> unbounded on the right.
    /// </summary>
    /// <param name="lowerBound">The lower (left) bound of the range.</param>
    /// <param name="lowerBoundInclusive">
    /// <see langword="true"/> to include <paramref name="lowerBound"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="OpenEnd"/> range: <c>[lowerBound, +∞)</c> or <c>(lowerBound, +∞)</c>.</returns>
    public static DateTimeOffsetRange CreateOpenEnd(DateTimeOffset lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    /// <summary>
    /// Returns an empty <see cref="DateTimeOffsetRange"/> that contains no values.
    /// </summary>
    public static DateTimeOffsetRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> bounded on both sides.
    /// </summary>
    /// <param name="lowerBound">The lower (left) bound of the range.</param>
    /// <param name="upperBound">The upper (right) bound of the range.</param>
    /// <param name="lowerBoundInclusive">
    /// <see langword="true"/> to include <paramref name="lowerBound"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <param name="upperBoundInclusive">
    /// <see langword="true"/> to include <paramref name="upperBound"/> in the range.
    /// Defaults to <see langword="false"/> (half-open convention).
    /// </param>
    /// <returns>
    /// A <see cref="Finite"/> range when <paramref name="lowerBound"/> is strictly less than
    /// <paramref name="upperBound"/>, or when they are equal and both bounds are inclusive.
    /// Returns <see cref="EmptyRange"/> when <paramref name="lowerBound"/> is greater than
    /// <paramref name="upperBound"/>, or when the bounds are equal but not both inclusive.
    /// </returns>
    public static DateTimeOffsetRange CreateFinite(
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        bool           lowerBoundInclusive = true,
        bool           upperBoundInclusive = false
    ) =>
        lowerBound.CompareTo(upperBound) switch
        {
            > 0 => Empty,
            0 => lowerBoundInclusive && upperBoundInclusive
                     ? new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
                     : new EmptyRange(),
            _ => new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
        };
}

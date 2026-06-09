namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateTime"/> values, equivalent to the PostgreSQL <c>tsrange</c>
/// (timestamp without time zone) type.
/// </summary>
/// <remarks>
/// This is a discriminated union with four variants: <see cref="Finite"/>, <see cref="OpenStart"/>,
/// <see cref="OpenEnd"/>, and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[lower, upper)</c>,
/// which is conventional for timestamp ranges.
/// </remarks>
public abstract record DateTimeRange : IRange<DateTime>, IRangeFactory<DateTimeRange, DateTime>
{
    private DateTimeRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="DateTimeRange"/> that contains no values.
    /// </summary>
    private sealed record EmptyRange : DateTimeRange, IEmptyRange<DateTime>;

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> bounded on both sides.
    /// </summary>
    private sealed record Finite : DateTimeRange, IFiniteRange<DateTime>
    {
        internal Finite(DateTime lowerBound, DateTime upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        /// <inheritdoc/>
        public DateTime LowerBound { get; }

        /// <inheritdoc/>
        public DateTime UpperBound { get; }

        /// <inheritdoc/>
        public bool LowerBoundInclusive { get; }

        /// <inheritdoc/>
        public bool UpperBoundInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> unbounded on the left:
    /// <c>(-∞, UpperBound]</c> or <c>(-∞, UpperBound)</c>.
    /// </summary>
    /// <param name="UpperBound">The upper (right) bound of the range.</param>
    /// <param name="UpperBoundInclusive"><see langword="true"/> to include <paramref name="UpperBound"/> in the range.</param>
    private sealed record OpenStart(DateTime UpperBound, bool UpperBoundInclusive) : DateTimeRange, IOpenStartRange<DateTime>;

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> unbounded on the right:
    /// <c>[LowerBound, +∞)</c> or <c>(LowerBound, +∞)</c>.
    /// </summary>
    /// <param name="LowerBound">The lower (left) bound of the range.</param>
    /// <param name="LowerBoundInclusive"><see langword="true"/> to include <paramref name="LowerBound"/> in the range.</param>
    private sealed record OpenEnd(DateTime LowerBound, bool LowerBoundInclusive) : DateTimeRange, IOpenEndRange<DateTime>;

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> unbounded on the left.
    /// </summary>
    /// <param name="upperBound">The upper (right) bound of the range.</param>
    /// <param name="upperBoundInclusive">
    /// <see langword="true"/> to include <paramref name="upperBound"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="OpenStart"/> range: <c>(-∞, upperBound]</c> or <c>(-∞, upperBound)</c>.</returns>
    public static DateTimeRange CreateOpenStart(DateTime upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> unbounded on the right.
    /// </summary>
    /// <param name="lowerBound">The lower (left) bound of the range.</param>
    /// <param name="lowerBoundInclusive">
    /// <see langword="true"/> to include <paramref name="lowerBound"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="OpenEnd"/> range: <c>[lowerBound, +∞)</c> or <c>(lowerBound, +∞)</c>.</returns>
    public static DateTimeRange CreateOpenEnd(DateTime lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    /// <summary>
    /// Returns an empty <see cref="DateTimeRange"/> that contains no values.
    /// </summary>
    public static DateTimeRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> bounded on both sides.
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
    public static DateTimeRange CreateFinite(
        DateTime lowerBound,
        DateTime upperBound,
        bool     lowerBoundInclusive = true,
        bool     upperBoundInclusive = false
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

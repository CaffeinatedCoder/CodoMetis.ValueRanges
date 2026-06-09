namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateOnly"/> values, equivalent to the PostgreSQL <c>daterange</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with four variants: <see cref="Finite"/>, <see cref="OpenStart"/>,
/// <see cref="OpenEnd"/>, and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[lower, upper]</c>.
/// As a discrete type, two ranges whose boundaries are exactly one day apart are considered adjacent.
/// </remarks>
public abstract record DateRange : IDiscreteRange<DateOnly>, IRangeFactory<DateRange, DateOnly>
{
    private DateRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="DateRange"/> that contains no values.
    /// </summary>
    private sealed record EmptyRange : DateRange, IEmptyRange<DateOnly>;

    /// <summary>
    /// Represents a <see cref="DateRange"/> bounded on both sides.
    /// </summary>
    private sealed record Finite : DateRange, IFiniteRange<DateOnly>
    {
        internal Finite(DateOnly lowerBound, DateOnly upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        /// <inheritdoc/>
        public DateOnly LowerBound { get; }

        /// <inheritdoc/>
        public DateOnly UpperBound { get; }

        /// <inheritdoc/>
        public bool LowerBoundInclusive { get; }

        /// <inheritdoc/>
        public bool UpperBoundInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on the left:
    /// <c>(-∞, UpperBound]</c> or <c>(-∞, UpperBound)</c>.
    /// </summary>
    /// <param name="UpperBound">The upper (right) bound of the range.</param>
    /// <param name="UpperBoundInclusive"><see langword="true"/> to include <paramref name="UpperBound"/> in the range.</param>
    private sealed record OpenStart(DateOnly UpperBound, bool UpperBoundInclusive) : DateRange, IOpenStartRange<DateOnly>;

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on the right:
    /// <c>[LowerBound, +∞)</c> or <c>(LowerBound, +∞)</c>.
    /// </summary>
    /// <param name="LowerBound">The lower (left) bound of the range.</param>
    /// <param name="LowerBoundInclusive"><see langword="true"/> to include <paramref name="LowerBound"/> in the range.</param>
    private sealed record OpenEnd(DateOnly LowerBound, bool LowerBoundInclusive) : DateRange, IOpenEndRange<DateOnly>;

    /// <summary>
    /// Creates a <see cref="DateRange"/> unbounded on the left.
    /// </summary>
    /// <param name="upperBound">The upper (right) bound of the range.</param>
    /// <param name="upperBoundInclusive">
    /// <see langword="true"/> to include <paramref name="upperBound"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="OpenStart"/> range: <c>(-∞, upperBound]</c> or <c>(-∞, upperBound)</c>.</returns>
    public static DateRange CreateOpenStart(DateOnly upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    /// <summary>
    /// Creates a <see cref="DateRange"/> unbounded on the right.
    /// </summary>
    /// <param name="lowerBound">The lower (left) bound of the range.</param>
    /// <param name="lowerBoundInclusive">
    /// <see langword="true"/> to include <paramref name="lowerBound"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="OpenEnd"/> range: <c>[lowerBound, +∞)</c> or <c>(lowerBound, +∞)</c>.</returns>
    public static DateRange CreateOpenEnd(DateOnly lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    /// <summary>
    /// Returns an empty <see cref="DateRange"/> that contains no values.
    /// </summary>
    public static DateRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateRange"/> bounded on both sides.
    /// </summary>
    /// <param name="lowerBound">The lower (left) bound of the range.</param>
    /// <param name="upperBound">The upper (right) bound of the range.</param>
    /// <param name="lowerBoundInclusive">
    /// <see langword="true"/> to include <paramref name="lowerBound"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <param name="upperBoundInclusive">
    /// <see langword="true"/> to include <paramref name="upperBound"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Finite"/> range when <paramref name="lowerBound"/> is strictly less than
    /// <paramref name="upperBound"/>, or when they are equal and both bounds are inclusive.
    /// Returns <see cref="EmptyRange"/> when <paramref name="lowerBound"/> is greater than
    /// <paramref name="upperBound"/>, or when the bounds are equal but not both inclusive.
    /// </returns>
    public static DateRange CreateFinite(
        DateOnly lowerBound,
        DateOnly upperBound,
        bool     lowerBoundInclusive = true,
        bool     upperBoundInclusive = true
    ) =>
        lowerBound.CompareTo(upperBound) switch
        {
            > 0 => Empty,
            0 => lowerBoundInclusive && upperBoundInclusive
                     ? new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
                     : new EmptyRange(),
            _ => new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
        };

    /// <summary>
    /// Returns the day immediately following <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The date whose successor is requested.</param>
    /// <returns><paramref name="value"/> advanced by one day.</returns>
    public DateOnly GetNextValueFor(DateOnly value) => value.AddDays(1);
}

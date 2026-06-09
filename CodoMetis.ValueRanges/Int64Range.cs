namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over 64-bit signed integers, equivalent to the PostgreSQL <c>int8range</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with four variants: <see cref="Finite"/>, <see cref="OpenStart"/>,
/// <see cref="OpenEnd"/>, and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[lower, upper]</c>.
/// </remarks>
public abstract record Int64Range : IDiscreteRange<long>, IRangeFactory<Int64Range, long>
{
    private Int64Range()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="Int64Range"/> that contains no values.
    /// </summary>
    private sealed record EmptyRange : Int64Range, IEmptyRange<long>;

    /// <summary>
    /// Represents an <see cref="Int64Range"/> bounded on both sides.
    /// </summary>
    private sealed record Finite : Int64Range, IFiniteRange<long>
    {
        internal Finite(long lowerBound, long upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        /// <inheritdoc/>
        public long LowerBound { get; }

        /// <inheritdoc/>
        public long UpperBound { get; }

        /// <inheritdoc/>
        public bool LowerBoundInclusive { get; }

        /// <inheritdoc/>
        public bool UpperBoundInclusive { get; }
    }

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on the left:
    /// <c>(-∞, UpperBound]</c> or <c>(-∞, UpperBound)</c>.
    /// </summary>
    /// <param name="UpperBound">The upper (right) bound of the range.</param>
    /// <param name="UpperBoundInclusive"><see langword="true"/> to include <paramref name="UpperBound"/> in the range.</param>
    private sealed record OpenStart(long UpperBound, bool UpperBoundInclusive) : Int64Range, IOpenStartRange<long>;

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on the right:
    /// <c>[LowerBound, +∞)</c> or <c>(LowerBound, +∞)</c>.
    /// </summary>
    /// <param name="LowerBound">The lower (left) bound of the range.</param>
    /// <param name="LowerBoundInclusive"><see langword="true"/> to include <paramref name="LowerBound"/> in the range.</param>
    private sealed record OpenEnd(long LowerBound, bool LowerBoundInclusive) : Int64Range, IOpenEndRange<long>;

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    private sealed record Infinity : Int64Range, IInfinityRange<long>;

    /// <summary>
    /// Creates an <see cref="Int64Range"/> unbounded on the left.
    /// </summary>
    /// <param name="upperBound">The upper (right) bound of the range.</param>
    /// <param name="upperBoundInclusive">
    /// <see langword="true"/> to include <paramref name="upperBound"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="OpenStart"/> range: <c>(-∞, upperBound]</c> or <c>(-∞, upperBound)</c>.</returns>
    public static Int64Range CreateOpenStart(long upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    /// <summary>
    /// Creates an <see cref="Int64Range"/> unbounded on the right.
    /// </summary>
    /// <param name="lowerBound">The lower (left) bound of the range.</param>
    /// <param name="lowerBoundInclusive">
    /// <see langword="true"/> to include <paramref name="lowerBound"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="OpenEnd"/> range: <c>[lowerBound, +∞)</c> or <c>(lowerBound, +∞)</c>.</returns>
    public static Int64Range CreateOpenEnd(long lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

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
    public static Int64Range CreateFinite(
        long lowerBound,
        long upperBound,
        bool lowerBoundInclusive = true,
        bool upperBoundInclusive = true
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
    /// Returns the immediate successor of <paramref name="value"/> by adding one.
    /// </summary>
    /// <param name="value">The integer value whose successor is requested.</param>
    /// <returns><paramref name="value"/> + 1.</returns>
    /// <exception cref="OverflowException">
    /// Thrown when <paramref name="value"/> equals <see cref="long.MaxValue"/>.
    /// </exception>
    public long GetNextValueFor(long value) => checked(value + 1);
}
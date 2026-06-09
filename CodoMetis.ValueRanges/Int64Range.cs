namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over 64-bit signed integers, equivalent to the PostgreSQL <c>int8range</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with four variants: <see cref="Finite"/>, <see cref="OpenStart"/>,
/// <see cref="OpenEnd"/>, and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[start, end]</c>.
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
        internal Finite(long start, long end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public long Start { get; }

        /// <inheritdoc/>
        public long End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    private sealed record OpenStart(long End, bool EndInclusive) : Int64Range, IOpenStartRange<long>;

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    private sealed record OpenEnd(long Start, bool StartInclusive) : Int64Range, IOpenEndRange<long>;

    /// <summary>
    /// Represents an <see cref="Int64Range"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    private sealed record Infinity : Int64Range, IInfinityRange<long>;

    /// <summary>
    /// Creates an <see cref="Int64Range"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="OpenStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static Int64Range CreateOpenStart(long end, bool endInclusive = false)
        => new OpenStart(end, endInclusive);

    /// <summary>
    /// Creates an <see cref="Int64Range"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="OpenEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static Int64Range CreateOpenEnd(long start, bool startInclusive = true)
        => new OpenEnd(start, startInclusive);

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
    ) =>
        start.CompareTo(end) switch
        {
            > 0 => Empty,
            0 => startInclusive && endInclusive
                     ? new Finite(start, end, startInclusive, endInclusive)
                     : new EmptyRange(),
            _ => new Finite(start, end, startInclusive, endInclusive)
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
namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateTimeOffset"/> values, equivalent to the PostgreSQL <c>tstzrange</c>
/// (timestamp with time zone) type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
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
    public sealed record EmptyRange : DateTimeOffsetRange, IEmptyRange<DateTimeOffset>;

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : DateTimeOffsetRange, IFiniteRange<DateTimeOffset>
    {
        internal Finite(DateTimeOffset start, DateTimeOffset end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public DateTimeOffset Start { get; }

        /// <inheritdoc/>
        public DateTimeOffset End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    public sealed record UnboundedStart(DateTimeOffset End, bool EndInclusive)
        : DateTimeOffsetRange, IUnboundedStartRange<DateTimeOffset>;

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    public sealed record UnboundedEnd(DateTimeOffset Start, bool StartInclusive)
        : DateTimeOffsetRange, IUnboundedEndRange<DateTimeOffset>;

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : DateTimeOffsetRange, IInfinityRange<DateTimeOffset>;

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DateTimeOffsetRange CreateOpenStart(DateTimeOffset end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DateTimeOffsetRange CreateOpenEnd(DateTimeOffset start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all timestamp-with-offset values.</returns>
    public static DateTimeOffsetRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="DateTimeOffsetRange"/> that contains no values.
    /// </summary>
    public static DateTimeOffsetRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> bounded on both sides.
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
    public static DateTimeOffsetRange CreateFinite(
        DateTimeOffset start,
        DateTimeOffset end,
        bool           startInclusive = true,
        bool           endInclusive   = false
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
namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateOnly"/> values, equivalent to the PostgreSQL <c>daterange</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[start, end]</c>.
/// As a discrete type, two ranges whose boundaries are exactly one day apart are considered adjacent.
/// </remarks>
public abstract record DateRange : IRange<DateOnly>, IRangeFactory<DateRange, DateOnly>
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
        internal Finite(DateOnly start, DateOnly end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public DateOnly Start { get; }

        /// <inheritdoc/>
        public DateOnly End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    private sealed record UnboundedStart(DateOnly End, bool EndInclusive) : DateRange, IUnboundedStartRange<DateOnly>;

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    private sealed record UnboundedEnd(DateOnly Start, bool StartInclusive) : DateRange, IUnboundedEndRange<DateOnly>;

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    private sealed record Infinity : DateRange, IInfinityRange<DateOnly>;

    /// <summary>
    /// Creates a <see cref="DateRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DateRange CreateOpenStart(DateOnly end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="DateRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DateRange CreateOpenEnd(DateOnly start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="DateRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all date values.</returns>
    public static DateRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="DateRange"/> that contains no values.
    /// </summary>
    public static DateRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateRange"/> bounded on both sides.
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
    public static DateRange CreateFinite(
        DateOnly start,
        DateOnly end,
        bool     startInclusive = true,
        bool     endInclusive   = true
    ) =>
        start.CompareTo(end) switch
        {
            > 0 => Empty,
            0 => startInclusive && endInclusive
                     ? new Finite(start, end, startInclusive, endInclusive)
                     : Empty,
            _ => new Finite(start, end, startInclusive, endInclusive)
        };

    /// <inheritdoc />
    public static DateOnly? NextValueAfter(DateOnly value) => value == DateOnly.MaxValue ? null : value.AddDays(1);

    /// <inheritdoc />
    public static DateOnly? PreviousValueBefore(DateOnly value) => value == DateOnly.MinValue ? null : value.AddDays(-1);
}
using System.Diagnostics;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="TimeOnly"/> values — a time-of-day range, equivalent to a custom
/// PostgreSQL range type over <c>time</c> (<c>CREATE TYPE timerange AS RANGE (subtype = time)</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for continuous types and matches how opening hours and shifts compose:
/// <c>[09:00, 12:00)</c> and <c>[12:00, 17:00)</c> are adjacent, not overlapping.
/// </para>
/// <para>
/// A single range cannot cross midnight — a window such as 22:00–06:00 is two ranges,
/// naturally represented as a <see cref="RangeSet{TRange,T}"/> of two elements.
/// PostgreSQL's <c>time</c> additionally admits the special value <c>24:00:00</c>, which
/// <see cref="TimeOnly"/> cannot represent; express "until end of day" as an unbounded end
/// or an inclusive upper bound of <see cref="TimeOnly.MaxValue"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public abstract record TimeRange : IRange<TimeOnly>, IRangeFactory<TimeRange, TimeOnly>
{
    private TimeRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="TimeRange"/> that contains no values.
    /// </summary>
    public sealed record EmptyRange : TimeRange, IEmptyRange<TimeOnly>;

    /// <summary>
    /// Represents a <see cref="TimeRange"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : TimeRange, IFiniteRange<TimeOnly>
    {
        internal Finite(TimeOnly start, TimeOnly end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public TimeOnly Start { get; }

        /// <inheritdoc/>
        public TimeOnly End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="TimeRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    public sealed record UnboundedStart(TimeOnly End, bool EndInclusive) : TimeRange, IUnboundedStartRange<TimeOnly>;

    /// <summary>
    /// Represents a <see cref="TimeRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    public sealed record UnboundedEnd(TimeOnly Start, bool StartInclusive) : TimeRange, IUnboundedEndRange<TimeOnly>;

    /// <summary>
    /// Represents a <see cref="TimeRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : TimeRange, IInfinityRange<TimeOnly>;

    /// <summary>
    /// Creates a <see cref="TimeRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static TimeRange CreateUnboundedStart(TimeOnly end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="TimeRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static TimeRange CreateUnboundedEnd(TimeOnly start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="TimeRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all time values.</returns>
    public static TimeRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="TimeRange"/> that contains no values.
    /// </summary>
    public static TimeRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="TimeRange"/> bounded on both sides.
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
    public static TimeRange CreateFinite(
        TimeOnly start,
        TimeOnly end,
        bool     startInclusive = true,
        bool     endInclusive   = false
    ) =>
        start.CompareTo(end) switch
        {
            > 0 => Empty,
            0 => startInclusive && endInclusive
                     ? new Finite(start, end, startInclusive, endInclusive)
                     : Empty,
            _ => new Finite(start, end, startInclusive, endInclusive)
        };

    /// <summary>
    /// The elapsed time between the bounds, or <see langword="null"/> when the range is
    /// unbounded. The empty range measures <see cref="TimeSpan.Zero"/>.
    /// </summary>
    /// <remarks>
    /// A span rather than a count: the domain is continuous. A window crossing midnight is two
    /// ranges rather than one, so this never wraps — measure the set, not a single range.
    /// </remarks>
    public TimeSpan? Length =>
        this switch
        {
            IEmptyRange<TimeOnly>    => TimeSpan.Zero,
            IFiniteRange<TimeOnly> f => f.End - f.Start,
            _                        => null
        };

    /// <inheritdoc />
    public static TimeOnly ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TimeOnly.Parse(s, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a <see cref="TimeOnly"/> value using the round-trip format specifier (<c>O</c>) by default,
    /// preserving full precision.
    /// </summary>
    public static string FormatValue(TimeOnly value, string? format, IFormatProvider? provider)
        => value.ToString(format ?? "O", provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[09:00:00,17:00:00)</c>, <c>empty</c>, <c>(,)</c>)
    /// into a <see cref="TimeRange"/>.
    /// </summary>
    public static TimeRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<TimeRange, TimeOnly>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static TimeRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<TimeRange, TimeOnly>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="TimeRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out TimeRange result)
        => RangeFormat.TryParse<TimeRange, TimeOnly>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TimeRange result)
        => RangeFormat.TryParse<TimeRange, TimeOnly>(s, provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

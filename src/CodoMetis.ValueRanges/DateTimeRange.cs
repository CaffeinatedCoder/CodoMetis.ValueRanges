using System.Diagnostics;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateTime"/> values, equivalent to the PostgreSQL <c>tsrange</c>
/// (timestamp without time zone) type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for timestamp ranges.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public abstract record DateTimeRange : IRange<DateTime>, IRangeFactory<DateTimeRange, DateTime>
{
    private DateTimeRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="DateTimeRange"/> that contains no values.
    /// </summary>
    public sealed record EmptyRange : DateTimeRange, IEmptyRange<DateTime>;

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : DateTimeRange, IFiniteRange<DateTime>
    {
        internal Finite(DateTime start, DateTime end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public DateTime Start { get; }

        /// <inheritdoc/>
        public DateTime End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    public sealed record UnboundedStart(DateTime End, bool EndInclusive) : DateTimeRange, IUnboundedStartRange<DateTime>;

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    public sealed record UnboundedEnd(DateTime Start, bool StartInclusive) : DateTimeRange, IUnboundedEndRange<DateTime>;

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : DateTimeRange, IInfinityRange<DateTime>;

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DateTimeRange CreateUnboundedStart(DateTime end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DateTimeRange CreateUnboundedEnd(DateTime start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all timestamp values.</returns>
    public static DateTimeRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="DateTimeRange"/> that contains no values.
    /// </summary>
    public static DateTimeRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> bounded on both sides.
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
    public static DateTimeRange CreateFinite(
        DateTime start,
        DateTime end,
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

    /// <inheritdoc />
    public static DateTime ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => DateTime.Parse(s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>
    /// Formats a <see cref="DateTime"/> value using the round-trip format specifier (<c>O</c>) by default,
    /// preserving full precision and <see cref="DateTimeKind"/>.
    /// </summary>
    public static string FormatValue(DateTime value, string? format, IFormatProvider? provider)
        => value.ToString(format ?? "O", provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[2024-01-01T00:00:00,2024-12-31T23:59:59)</c>,
    /// <c>empty</c>, <c>(,)</c>) into a <see cref="DateTimeRange"/>.
    /// </summary>
    public static DateTimeRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<DateTimeRange, DateTime>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="DateTimeRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeRange result)
        => RangeFormat.TryParse<DateTimeRange, DateTime>(s.AsSpan(), provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
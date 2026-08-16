using System.Diagnostics;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="Instant"/> values, equivalent to the PostgreSQL <c>tstzrange</c>
/// (timestamp with time zone) type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for timestamp ranges.
/// An <see cref="Instant"/> is exactly what <c>timestamptz</c> stores — a point on the global
/// timeline with no offset or zone attached — so no normalization happens at the database
/// boundary. Zoned or offset values convert explicitly via <c>ZonedDateTime.ToInstant()</c> /
/// <c>OffsetDateTime.ToInstant()</c> before entering a range.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public abstract record InstantRange : IRange<Instant>, IRangeFactory<InstantRange, Instant>
{
    private InstantRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="InstantRange"/> that contains no values.
    /// </summary>
    public sealed record EmptyRange : InstantRange, IEmptyRange<Instant>;

    /// <summary>
    /// Represents an <see cref="InstantRange"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : InstantRange, IFiniteRange<Instant>
    {
        internal Finite(Instant start, Instant end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public Instant Start { get; }

        /// <inheritdoc/>
        public Instant End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents an <see cref="InstantRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    public sealed record UnboundedStart(Instant End, bool EndInclusive) : InstantRange, IUnboundedStartRange<Instant>;

    /// <summary>
    /// Represents an <see cref="InstantRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    public sealed record UnboundedEnd(Instant Start, bool StartInclusive) : InstantRange, IUnboundedEndRange<Instant>;

    /// <summary>
    /// Represents an <see cref="InstantRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : InstantRange, IInfinityRange<Instant>;

    /// <summary>
    /// Creates an <see cref="InstantRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static InstantRange CreateUnboundedStart(Instant end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates an <see cref="InstantRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static InstantRange CreateUnboundedEnd(Instant start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates an <see cref="InstantRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all instants.</returns>
    public static InstantRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="InstantRange"/> that contains no values.
    /// </summary>
    public static InstantRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates an <see cref="InstantRange"/> bounded on both sides.
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
    public static InstantRange CreateFinite(
        Instant start,
        Instant end,
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

    /// <summary>
    /// The elapsed time between the bounds, or <see langword="null"/> when the range is
    /// unbounded. The empty range measures <see cref="Duration.Zero"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="Duration"/>, not a <see cref="Period"/>: both bounds are instants on the
    /// time line, so the elapsed time is exact and calendar-independent.
    /// </remarks>
    public Duration? Length =>
        this switch
        {
            IEmptyRange<Instant>    => Duration.Zero,
            IFiniteRange<Instant> f => f.End - f.Start,
            _                       => null
        };

    /// <inheritdoc />
    public static Instant ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseInstant(s.ToString());

    /// <summary>
    /// Formats an <see cref="Instant"/> value using extended ISO 8601
    /// (<c>uuuu-MM-ddTHH:mm:ss.FFFFFFFFFZ</c>, subsecond digits only when present) by default.
    /// </summary>
    public static string FormatValue(Instant value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.Instant.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[2024-01-01T00:00:00Z,2024-12-31T00:00:00Z)</c>,
    /// <c>empty</c>, <c>(,)</c>) into an <see cref="InstantRange"/>. Bounds are accepted in
    /// extended ISO 8601 form or in the PostgreSQL wire form with a numeric offset
    /// (e.g. <c>2024-06-01 14:30:00+02</c>) — offsets are converted to the instant they denote.
    /// </summary>
    public static InstantRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<InstantRange, Instant>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static InstantRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<InstantRange, Instant>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into an <see cref="InstantRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out InstantRange result)
        => RangeFormat.TryParse<InstantRange, Instant>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out InstantRange result)
        => RangeFormat.TryParse<InstantRange, Instant>(s, provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

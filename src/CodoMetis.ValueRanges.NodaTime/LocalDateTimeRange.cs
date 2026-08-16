using System.Diagnostics;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="LocalDateTime"/> values, equivalent to the PostgreSQL <c>tsrange</c>
/// (timestamp without time zone) type.
/// </summary>
/// <remarks>
/// <para>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for timestamp ranges.
/// </para>
/// <para>
/// <see cref="LocalDateTime"/> is wall-clock time by construction — unlike <see cref="DateTime"/>,
/// there is no <c>Kind</c> to reinterpret at the database boundary.
/// Bounds are normalized to the ISO calendar at construction — <see cref="LocalDateTime.CompareTo"/>
/// is only defined between values of the same calendar system, and PostgreSQL <c>timestamp</c> is
/// proleptic Gregorian. A value whose date is out of range of the ISO calendar throws
/// <see cref="ArgumentOutOfRangeException"/> at construction.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public abstract record LocalDateTimeRange : IRange<LocalDateTime>, IRangeFactory<LocalDateTimeRange, LocalDateTime>
{
    private LocalDateTimeRange()
    {
    }

    private static LocalDateTime ToIso(LocalDateTime value)
        => value.Calendar == CalendarSystem.Iso ? value : value.WithCalendar(CalendarSystem.Iso);

    /// <summary>
    /// Represents an empty <see cref="LocalDateTimeRange"/> that contains no values.
    /// </summary>
    public sealed record EmptyRange : LocalDateTimeRange, IEmptyRange<LocalDateTime>;

    /// <summary>
    /// Represents a <see cref="LocalDateTimeRange"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : LocalDateTimeRange, IFiniteRange<LocalDateTime>
    {
        internal Finite(LocalDateTime start, LocalDateTime end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public LocalDateTime Start { get; }

        /// <inheritdoc/>
        public LocalDateTime End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="LocalDateTimeRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    public sealed record UnboundedStart : LocalDateTimeRange, IUnboundedStartRange<LocalDateTime>
    {
        /// <summary>
        /// Creates a range unbounded on the left with the upper bound <paramref name="end"/>
        /// (normalized to the ISO calendar).
        /// </summary>
        /// <param name="end">The upper (right) bound of the range.</param>
        /// <param name="endInclusive"><see langword="true"/> to include <paramref name="end"/> in the range.</param>
        public UnboundedStart(LocalDateTime end, bool endInclusive)
        {
            End          = ToIso(end);
            EndInclusive = endInclusive;
        }

        /// <inheritdoc/>
        public LocalDateTime End { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="LocalDateTimeRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    public sealed record UnboundedEnd : LocalDateTimeRange, IUnboundedEndRange<LocalDateTime>
    {
        /// <summary>
        /// Creates a range unbounded on the right with the lower bound <paramref name="start"/>
        /// (normalized to the ISO calendar).
        /// </summary>
        /// <param name="start">The lower (left) bound of the range.</param>
        /// <param name="startInclusive"><see langword="true"/> to include <paramref name="start"/> in the range.</param>
        public UnboundedEnd(LocalDateTime start, bool startInclusive)
        {
            Start          = ToIso(start);
            StartInclusive = startInclusive;
        }

        /// <inheritdoc/>
        public LocalDateTime Start { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }
    }

    /// <summary>
    /// Represents a <see cref="LocalDateTimeRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : LocalDateTimeRange, IInfinityRange<LocalDateTime>;

    /// <summary>
    /// Creates a <see cref="LocalDateTimeRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static LocalDateTimeRange CreateUnboundedStart(LocalDateTime end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="LocalDateTimeRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static LocalDateTimeRange CreateUnboundedEnd(LocalDateTime start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="LocalDateTimeRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all timestamp values.</returns>
    public static LocalDateTimeRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="LocalDateTimeRange"/> that contains no values.
    /// </summary>
    public static LocalDateTimeRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="LocalDateTimeRange"/> bounded on both sides.
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
    public static LocalDateTimeRange CreateFinite(
        LocalDateTime start,
        LocalDateTime end,
        bool          startInclusive = true,
        bool          endInclusive   = false
    )
    {
        start = ToIso(start);
        end   = ToIso(end);
        return start.CompareTo(end) switch
        {
            > 0 => Empty,
            0 => startInclusive && endInclusive
                     ? new Finite(start, end, startInclusive, endInclusive)
                     : Empty,
            _ => new Finite(start, end, startInclusive, endInclusive)
        };
    }

    /// <inheritdoc />
    public static LocalDateTime ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseDateTime(s.ToString());

    /// <summary>
    /// Formats a <see cref="LocalDateTime"/> value using extended ISO 8601
    /// (<c>uuuu-MM-ddTHH:mm:ss.FFFFFFFFF</c>, subsecond digits only when present) by default.
    /// </summary>
    public static string FormatValue(LocalDateTime value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.DateTime.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[2024-01-01T00:00:00,2024-12-31T23:59:59)</c>,
    /// <c>empty</c>, <c>(,)</c>) into a <see cref="LocalDateTimeRange"/>.
    /// </summary>
    public static LocalDateTimeRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<LocalDateTimeRange, LocalDateTime>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static LocalDateTimeRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<LocalDateTimeRange, LocalDateTime>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="LocalDateTimeRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out LocalDateTimeRange result)
        => RangeFormat.TryParse<LocalDateTimeRange, LocalDateTime>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out LocalDateTimeRange result)
        => RangeFormat.TryParse<LocalDateTimeRange, LocalDateTime>(s, provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

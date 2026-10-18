using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;
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
/// proleptic Gregorian. Every calendar NodaTime ships spans a subset of the ISO calendar's range,
/// so the conversion cannot fail.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<LocalDateTimeRange, LocalDateTime>))]
[TypeConverter(typeof(RangeTypeConverter<LocalDateTimeRange, LocalDateTime>))]
public abstract partial record LocalDateTimeRange : IRange<LocalDateTime>, IRangeFactory<LocalDateTimeRange, LocalDateTime>
{
    private LocalDateTimeRange()
    {
    }

    private static LocalDateTime ToIso(LocalDateTime value)
        => value.Calendar == CalendarSystem.Iso ? value : value.WithCalendar(CalendarSystem.Iso);

    /// <summary>
    /// Represents an empty <see cref="LocalDateTimeRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, LocalDateTimeRange, LocalDateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, LocalDateTimeRange, LocalDateTime>))]
    public sealed partial record EmptyRange : LocalDateTimeRange, IEmptyRange<LocalDateTime>;

    /// <summary>
    /// Represents a <see cref="LocalDateTimeRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, LocalDateTimeRange, LocalDateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, LocalDateTimeRange, LocalDateTime>))]
    public sealed partial record Finite : LocalDateTimeRange, IFiniteRange<LocalDateTime>
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

        /// <summary>
        /// Deconstructs the range into its bounds and their inclusiveness:
        /// <c>var (start, end, startInclusive, endInclusive) = finite;</c>.
        /// </summary>
        /// <remarks>
        /// There is deliberately no form without the inclusiveness: passing only the bounds back to
        /// <see cref="LocalDateTimeRange.CreateFinite"/> would silently apply its half-open default.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        /// <param name="startInclusive">Receives <see cref="StartInclusive"/>.</param>
        /// <param name="endInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out LocalDateTime start, out LocalDateTime end, out bool startInclusive, out bool endInclusive) =>
            (start, end, startInclusive, endInclusive) = (Start, End, StartInclusive, EndInclusive);
    }

    /// <summary>
    /// Represents a <see cref="LocalDateTimeRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, LocalDateTimeRange, LocalDateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, LocalDateTimeRange, LocalDateTime>))]
    public sealed partial record UnboundedStart : LocalDateTimeRange, IUnboundedStartRange<LocalDateTime>
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

        /// <summary>
        /// Deconstructs the range into its upper bound and its inclusiveness, as the positional
        /// variants of the other range types do: <c>range is LocalDateTimeRange.UnboundedStart(var end, var endInclusive)</c>.
        /// </summary>
        /// <param name="end">Receives <see cref="End"/>.</param>
        /// <param name="endInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out LocalDateTime end, out bool endInclusive) =>
            (end, endInclusive) = (End, EndInclusive);
    }

    /// <summary>
    /// Represents a <see cref="LocalDateTimeRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, LocalDateTimeRange, LocalDateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, LocalDateTimeRange, LocalDateTime>))]
    public sealed partial record UnboundedEnd : LocalDateTimeRange, IUnboundedEndRange<LocalDateTime>
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

        /// <summary>
        /// Deconstructs the range into its lower bound and its inclusiveness, as the positional
        /// variants of the other range types do: <c>range is LocalDateTimeRange.UnboundedEnd(var start, var startInclusive)</c>.
        /// </summary>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="startInclusive">Receives <see cref="StartInclusive"/>.</param>
        public void Deconstruct(out LocalDateTime start, out bool startInclusive) =>
            (start, startInclusive) = (Start, StartInclusive);
    }

    /// <summary>
    /// Represents a <see cref="LocalDateTimeRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, LocalDateTimeRange, LocalDateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, LocalDateTimeRange, LocalDateTime>))]
    public sealed partial record Infinity : LocalDateTimeRange, IInfinityRange<LocalDateTime>;

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

    /// <summary>
    /// The calendar period between the bounds, or <see langword="null"/> when the range is
    /// unbounded. The empty range measures <see cref="Period.Zero"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="Period"/> rather than a <see cref="Duration"/>, because the bounds are
    /// wall-clock and a period is what NodaTime defines between them: months and years are
    /// calendar quantities whose length in time depends on where they start.
    /// </remarks>
    public Period? Length =>
        this switch
        {
            IEmptyRange<LocalDateTime>    => Period.Zero,
            IFiniteRange<LocalDateTime> f => Period.Between(f.Start, f.End),
            _                             => null
        };

    /// <inheritdoc />
    public static LocalDateTime ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseDateTime(s.ToString());

    /// <summary>
    /// Formats a <see cref="LocalDateTime"/> value using extended ISO 8601
    /// (<c>uuuu-MM-ddTHH:mm:ss.FFFFFFFFF</c>, subsecond digits only when present) by default.
    /// </summary>
    public static string FormatValue(LocalDateTime value, string? format, IFormatProvider? provider)
        => string.IsNullOrEmpty(format)
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

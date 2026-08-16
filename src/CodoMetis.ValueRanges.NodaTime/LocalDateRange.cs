using System.Diagnostics;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="LocalDate"/> values, equivalent to the PostgreSQL <c>daterange</c> type.
/// </summary>
/// <remarks>
/// <para>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[start, end]</c>.
/// As a discrete type, two ranges whose boundaries are exactly one day apart are considered adjacent.
/// </para>
/// <para>
/// Bounds are normalized to the ISO calendar at construction — <see cref="LocalDate.CompareTo"/>
/// is only defined between dates of the same calendar system, and PostgreSQL <c>date</c> is
/// proleptic Gregorian. A date whose day is out of range of the ISO calendar (e.g. a far-future
/// Coptic date) throws <see cref="ArgumentOutOfRangeException"/> at construction.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public abstract record LocalDateRange : IRange<LocalDate>, IRangeFactory<LocalDateRange, LocalDate>
{
    private LocalDateRange()
    {
    }

    private static LocalDate ToIso(LocalDate value)
        => value.Calendar == CalendarSystem.Iso ? value : value.WithCalendar(CalendarSystem.Iso);

    /// <summary>
    /// Represents an empty <see cref="LocalDateRange"/> that contains no values.
    /// </summary>
    public sealed record EmptyRange : LocalDateRange, IEmptyRange<LocalDate>;

    /// <summary>
    /// Represents a <see cref="LocalDateRange"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : LocalDateRange, IFiniteRange<LocalDate>
    {
        internal Finite(LocalDate start, LocalDate end)
        {
            Start = start;
            End   = end;
        }

        /// <inheritdoc/>
        public LocalDate Start { get; }

        /// <inheritdoc/>
        public LocalDate End { get; }

        /// <inheritdoc/>
        public bool StartInclusive => true;

        /// <inheritdoc/>
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents a <see cref="LocalDateRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    public sealed record UnboundedStart : LocalDateRange, IUnboundedStartRange<LocalDate>
    {
        /// <summary>
        /// Creates a range unbounded on the left with the inclusive upper bound
        /// <paramref name="end"/> (normalized to the ISO calendar).
        /// </summary>
        /// <param name="end">The upper (right) bound of the range.</param>
        public UnboundedStart(LocalDate end) => End = ToIso(end);

        /// <inheritdoc/>
        public LocalDate End { get; }

        /// <inheritdoc />
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents a <see cref="LocalDateRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    public sealed record UnboundedEnd : LocalDateRange, IUnboundedEndRange<LocalDate>
    {
        /// <summary>
        /// Creates a range unbounded on the right with the inclusive lower bound
        /// <paramref name="start"/> (normalized to the ISO calendar).
        /// </summary>
        /// <param name="start">The lower (left) bound of the range.</param>
        public UnboundedEnd(LocalDate start) => Start = ToIso(start);

        /// <inheritdoc/>
        public LocalDate Start { get; }

        /// <inheritdoc />
        public bool StartInclusive => true;
    }

    /// <summary>
    /// Represents a <see cref="LocalDateRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : LocalDateRange, IInfinityRange<LocalDate>;

    /// <summary>
    /// Creates a <see cref="LocalDateRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static LocalDateRange CreateUnboundedStart(LocalDate end, bool endInclusive = false)
    {
        end = ToIso(end);
        return endInclusive
                   ? new UnboundedStart(end)
                   : PreviousValueBefore(end) is { } e
                       ? new UnboundedStart(e)
                       : Empty;
    }

    /// <summary>
    /// Creates a <see cref="LocalDateRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static LocalDateRange CreateUnboundedEnd(LocalDate start, bool startInclusive = true)
    {
        start = ToIso(start);
        return startInclusive
                   ? new UnboundedEnd(start)
                   : NextValueAfter(start) is { } s
                       ? new UnboundedEnd(s)
                       : Empty;
    }

    /// <summary>
    /// Creates a <see cref="LocalDateRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all date values.</returns>
    public static LocalDateRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="LocalDateRange"/> that contains no values.
    /// </summary>
    public static LocalDateRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="LocalDateRange"/> bounded on both sides.
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
    public static LocalDateRange CreateFinite(
        LocalDate start,
        LocalDate end,
        bool      startInclusive = true,
        bool      endInclusive   = true
    ) => DiscreteCanonical.Finite<LocalDateRange, LocalDate>(ToIso(start), ToIso(end), startInclusive, endInclusive) is { } b
             ? new Finite(b.Start, b.End)
             : Empty;

    /// <inheritdoc />
    public static bool IsDiscrete => true;

    /// <inheritdoc />
    public static LocalDate? NextValueAfter(LocalDate value)
        => value == LocalDate.MaxIsoValue ? null : value.PlusDays(1);

    /// <inheritdoc />
    public static LocalDate? PreviousValueBefore(LocalDate value)
        => value == LocalDate.MinIsoValue ? null : value.PlusDays(-1);

    /// <summary>
    /// Enumerates the dates the range contains, ascending and inclusive of both bounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Available here because the domain is discrete. The continuous range types have no
    /// step to walk and do not declare this member at all, so asking for the values of a
    /// <c>DecimalRange</c> is a compile error rather than a runtime failure.
    /// </para>
    /// <para>
    /// The empty range yields nothing. An unbounded range would not terminate and throws
    /// immediately — the check is eager, so the failure surfaces at this call rather than at
    /// the <c>foreach</c> that consumes it.
    /// </para>
    /// </remarks>
    /// <returns>The contained dates, ascending.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    public IEnumerable<LocalDate> Values() => DiscreteEnumeration.Values<LocalDateRange, LocalDate>(this);

    /// <summary>
    /// The number of days the range covers, or <see langword="null"/> when it is unbounded.
    /// The empty range measures 0.
    /// </summary>
    /// <remarks>
    /// Inclusive of both ends, because the domain is discrete and canonicalizes closed —
    /// the same convention as the BCL <c>DateRange</c>.
    /// </remarks>
    public int? Length =>
        this switch
        {
            IEmptyRange<LocalDate>    => 0,
            IFiniteRange<LocalDate> f => Period.Between(f.Start, f.End, PeriodUnits.Days).Days + 1,
            _                         => null
        };

    /// <inheritdoc />
    public static LocalDate ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseDate(s.ToString());

    /// <summary>
    /// Formats a <see cref="LocalDate"/> value using ISO 8601 (<c>uuuu-MM-dd</c>) by default.
    /// </summary>
    public static string FormatValue(LocalDate value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.Date.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[2024-01-01,2024-12-31]</c>, <c>empty</c>, <c>(,)</c>)
    /// into a <see cref="LocalDateRange"/>.
    /// </summary>
    public static LocalDateRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<LocalDateRange, LocalDate>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static LocalDateRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<LocalDateRange, LocalDate>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="LocalDateRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out LocalDateRange result)
        => RangeFormat.TryParse<LocalDateRange, LocalDate>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out LocalDateRange result)
        => RangeFormat.TryParse<LocalDateRange, LocalDate>(s, provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

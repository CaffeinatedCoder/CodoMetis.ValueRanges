using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

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
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<DateRange, DateOnly>))]
[TypeConverter(typeof(RangeTypeConverter<DateRange, DateOnly>))]
public abstract partial record DateRange : IRange<DateOnly>, IRangeFactory<DateRange, DateOnly>
{
    private DateRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="DateRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, DateRange, DateOnly>))]
    public sealed partial record EmptyRange : DateRange, IEmptyRange<DateOnly>;

    /// <summary>
    /// Represents a <see cref="DateRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, DateRange, DateOnly>))]
    public sealed partial record Finite : DateRange, IFiniteRange<DateOnly>
    {
        internal Finite(DateOnly start, DateOnly end)
        {
            Start = start;
            End   = end;
        }

        /// <inheritdoc/>
        public DateOnly Start { get; }

        /// <inheritdoc/>
        public DateOnly End { get; }

        /// <inheritdoc/>
        public bool StartInclusive => true;

        /// <inheritdoc/>
        public bool EndInclusive => true;

        /// <summary>
        /// Deconstructs the range into its bounds: <c>var (start, end) = finite;</c>.
        /// </summary>
        /// <remarks>
        /// Both bounds are inclusive — the canonical form of a discrete range — so the pair passes
        /// straight back to <see cref="DateRange.CreateFinite"/>, whose default is closed.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        public void Deconstruct(out DateOnly start, out DateOnly end) => (start, end) = (Start, End);
    }

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, DateRange, DateOnly>))]
    public sealed partial record UnboundedStart(DateOnly End) : DateRange, IUnboundedStartRange<DateOnly>
    {
        /// <inheritdoc />
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, DateRange, DateOnly>))]
    public sealed partial record UnboundedEnd(DateOnly Start) : DateRange, IUnboundedEndRange<DateOnly>
    {
        /// <inheritdoc />
        public bool StartInclusive => true;
    }

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, DateRange, DateOnly>))]
    public sealed partial record Infinity : DateRange, IInfinityRange<DateOnly>;

    /// <summary>
    /// Creates a <see cref="DateRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DateRange CreateUnboundedStart(DateOnly end, bool endInclusive = false) =>
        endInclusive
            ? new UnboundedStart(end)
            : PreviousValueBefore(end) is { } e
                ? new UnboundedStart(e)
                : Empty;

    /// <summary>
    /// Creates a <see cref="DateRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DateRange CreateUnboundedEnd(DateOnly start, bool startInclusive = true) =>
        startInclusive
            ? new UnboundedEnd(start)
            : NextValueAfter(start) is { } s
                ? new UnboundedEnd(s)
                : Empty;

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
    ) => DiscreteCanonical.Finite<DateRange, DateOnly>(start, end, startInclusive, endInclusive) is { } b
             ? new Finite(b.Start, b.End)
             : Empty;

    /// <inheritdoc />
    public static bool IsDiscrete => true;

    /// <inheritdoc />
    public static DateOnly? NextValueAfter(DateOnly value) => value == DateOnly.MaxValue ? null : value.AddDays(1);

    /// <inheritdoc />
    public static DateOnly? PreviousValueBefore(DateOnly value) => value == DateOnly.MinValue ? null : value.AddDays(-1);

    // -------------------------------------------------------------------------
    // Calendar ranges
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the calendar month <paramref name="month"/> of <paramref name="year"/>, from its
    /// first day to its last: <c>DateRange.Month(2026, 2)</c> is <c>[2026-02-01,2026-02-28]</c>.
    /// </summary>
    /// <param name="year">The year, 1 to 9999.</param>
    /// <param name="month">The month, 1 to 12.</param>
    /// <returns>The month as a closed range of days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year or the month is out of range.</exception>
    public static DateRange Month(int year, int month)
        => new Finite(new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

    /// <summary>
    /// Returns the calendar month that contains <paramref name="date"/>.
    /// </summary>
    /// <param name="date">Any day of the month.</param>
    /// <returns>The month as a closed range of days.</returns>
    public static DateRange Month(DateOnly date) => Month(date.Year, date.Month);

    /// <summary>
    /// Returns quarter <paramref name="quarter"/> of <paramref name="year"/>: three calendar months
    /// starting in January, April, July or October. <c>DateRange.Quarter(2026, 4)</c> is
    /// <c>[2026-10-01,2026-12-31]</c>.
    /// </summary>
    /// <param name="year">The year, 1 to 9999.</param>
    /// <param name="quarter">The quarter, 1 to 4.</param>
    /// <returns>The quarter as a closed range of days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year or the quarter is out of range.</exception>
    public static DateRange Quarter(int year, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);

        return new Finite(new DateOnly(year, quarter * 3 - 2, 1), new DateOnly(year, quarter * 3, DateTime.DaysInMonth(year, quarter * 3)));
    }

    /// <summary>
    /// Returns the quarter that contains <paramref name="date"/>.
    /// </summary>
    /// <param name="date">Any day of the quarter.</param>
    /// <returns>The quarter as a closed range of days.</returns>
    public static DateRange Quarter(DateOnly date) => Quarter(date.Year, (date.Month + 2) / 3);

    /// <summary>
    /// Returns the calendar year <paramref name="year"/>, from January 1 to December 31.
    /// </summary>
    /// <param name="year">The year, 1 to 9999.</param>
    /// <returns>The year as a closed range of days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is out of range.</exception>
    public static DateRange Year(int year) => new Finite(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));

    /// <summary>
    /// Returns the calendar year that contains <paramref name="date"/>.
    /// </summary>
    /// <param name="date">Any day of the year.</param>
    /// <returns>The year as a closed range of days.</returns>
    public static DateRange Year(DateOnly date) => Year(date.Year);

    /// <summary>
    /// Returns ISO 8601 week <paramref name="week"/> of the ISO week-year <paramref name="weekYear"/>:
    /// Monday to Sunday. <c>DateRange.IsoWeek(2026, 1)</c> is <c>[2025-12-29,2026-01-04]</c>.
    /// </summary>
    /// <remarks>
    /// Weeks are always ISO 8601, never the current culture's: a week starts on Monday, and week 1
    /// is the week that holds the year's first Thursday. So the week-year can differ from the
    /// calendar year near January 1, and a week-year has 52 or 53 weeks
    /// (<see cref="System.Globalization.ISOWeek.GetWeeksInYear"/>).
    /// </remarks>
    /// <param name="weekYear">The ISO week-year, 1 to 9999.</param>
    /// <param name="week">The week, 1 to 52, or 53 in a long week-year.</param>
    /// <returns>The week as a closed range of seven days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The week-year or the week is out of range, or the week runs past <see cref="DateOnly.MaxValue"/>,
    /// as week 52 of 9999 does.
    /// </exception>
    public static DateRange IsoWeek(int weekYear, int week)
    {
        // ISOWeek.ToDateTime accepts week 53 of a 52-week year and answers with week 1 of the next.
        ArgumentOutOfRangeException.ThrowIfLessThan(week, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(week, ISOWeek.GetWeeksInYear(weekYear));

        return IsoWeekStartingOn(DateOnly.FromDateTime(ISOWeek.ToDateTime(weekYear, week, DayOfWeek.Monday)));
    }

    /// <summary>
    /// Returns the ISO 8601 week that contains <paramref name="date"/>: the Monday on or before it to
    /// the Sunday after that Monday.
    /// </summary>
    /// <remarks>
    /// Weeks are always ISO 8601, never the current culture's; see <see cref="IsoWeek(int, int)"/>.
    /// </remarks>
    /// <param name="date">Any day of the week.</param>
    /// <returns>The week as a closed range of seven days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The week runs past <see cref="DateOnly.MaxValue"/>.</exception>
    public static DateRange IsoWeek(DateOnly date)
        => IsoWeekStartingOn(date.AddDays(-(((int)date.DayOfWeek + 6) % 7)));

    private static DateRange IsoWeekStartingOn(DateOnly monday)
        => monday.DayNumber > DateOnly.MaxValue.DayNumber - 6
               ? throw new ArgumentOutOfRangeException(
                   nameof(monday), monday, "The ISO week starting on this Monday runs past DateOnly.MaxValue.")
               : new Finite(monday, monday.AddDays(6));

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
    public IEnumerable<DateOnly> Values() => DiscreteEnumeration.Values<DateRange, DateOnly>(this);

    /// <summary>
    /// Cuts the range into consecutive pieces of <paramref name="days"/>, in days each, from its start:
    /// <c>[2026-06-01,2026-06-30].Split(7)</c> is four weeks from June 1 and <c>[2026-06-29,2026-06-30]</c>.
    /// </summary>
    /// <remarks>
    /// The pieces are adjacent and hold exactly the values of the range, so
    /// <c>RangeSet.From(range.Split(step))</c> is the range itself. The last piece is clipped to the range's end and keeps the range's upper bound.
    /// The empty range yields nothing. This runs in memory only: PostgreSQL's
    /// <c>generate_series</c> returns rows, not a value.
    /// </remarks>
    /// <param name="days">The length of each piece, in days; it must be positive.</param>
    /// <returns>The pieces, in order. The checks run at the call; the pieces are computed as you enumerate.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="days"/> is zero or negative.</exception>
    public IEnumerable<DateRange> Split(int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        return RangeSplitting.Split<DateRange, DateOnly>(this, RangeSplitting.Steps<DateOnly>(this, (start, n) => start.AddDays(checked((int)(n * days)))));
    }

    /// <summary>
    /// Cuts the range at the boundaries of a calendar period: <c>range.Split(DateRange.Month)</c> gives one
    /// piece per calendar month, the first and the last clipped to the range.
    /// </summary>
    /// <remarks>
    /// <paramref name="period"/> is called with the first day of each piece and returns the period that holds
    /// it, and the piece runs to that period's end. Pass <see cref="Month(DateOnly)"/>, <see cref="Quarter(DateOnly)"/>, <see cref="Year(DateOnly)"/> or <see cref="IsoWeek(DateOnly)"/>, or a function of your own,
    /// such as a fiscal year. The pieces are adjacent and hold exactly the values of the range. The
    /// empty range yields nothing. This runs in memory only.
    /// </remarks>
    /// <param name="period">Returns the period that holds a given day.</param>
    /// <returns>The pieces, in order. The checks run at the call; the pieces are computed as you enumerate.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="period"/> returned a range that is not bounded on both sides or does not hold
    /// the day it was given.
    /// </exception>
    public IEnumerable<DateRange> Split(Func<DateOnly, DateRange> period)
        => RangeSplitting.Split<DateRange, DateOnly>(this, RangeSplitting.Periods<DateRange, DateOnly>(period));

    /// <summary>
    /// Enumerates the range's values from its start, <paramref name="days"/>, in days apart:
    /// <c>[2026-06-01,2026-06-30].Values(7)</c> is every Monday of June 2026.
    /// </summary>
    /// <remarks>
    /// The stepping form of <see cref="Values()"/>, under the same rules: ascending, the empty range
    /// yields nothing, and an unbounded range throws at the call. This runs in memory only.
    /// </remarks>
    /// <param name="days">The distance between two values, in days; it must be positive.</param>
    /// <returns>The values, ascending.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="days"/> is zero or negative.</exception>
    public IEnumerable<DateOnly> Values(int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        return RangeSplitting.Values<DateRange, DateOnly>(this, RangeSplitting.Steps<DateOnly>(this, (start, n) => start.AddDays(checked((int)(n * days)))));
    }

    /// <summary>
    /// The number of days the range covers, or <see langword="null"/> when it is unbounded.
    /// The empty range measures 0.
    /// </summary>
    /// <remarks>
    /// Inclusive of both ends, because the domain is discrete and canonicalizes closed:
    /// <c>[2024-01-01, 2024-01-31]</c> measures 31, which is the answer a calendar gives.
    /// </remarks>
    public int? Length =>
        this switch
        {
            IEmptyRange<DateOnly>    => 0,
            IFiniteRange<DateOnly> f => f.End.DayNumber - f.Start.DayNumber + 1,
            _                        => null
        };

    /// <inheritdoc />
    /// <remarks>Strict ISO 8601 — the forms this library writes and PostgreSQL's ISO output, plus
    /// <c>infinity</c> / <c>-infinity</c> as the maximum / minimum value; the provider is ignored. A lenient parse read <c>01.02.2027</c> month first.</remarks>
    public static DateOnly ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => IsoTemporal.ParseDate(s);

    /// <summary>
    /// Formats a <see cref="DateOnly"/> value using ISO 8601 (<c>yyyy-MM-dd</c>) by default.
    /// </summary>
    public static string FormatValue(DateOnly value, string? format, IFormatProvider? provider)
        => value.ToString(string.IsNullOrEmpty(format) ? "yyyy-MM-dd" : format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[2024-01-01,2024-12-31]</c>, <c>empty</c>, <c>(,)</c>)
    /// into a <see cref="DateRange"/>.
    /// </summary>
    public static DateRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<DateRange, DateOnly>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static DateRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<DateRange, DateOnly>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="DateRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateRange result)
        => RangeFormat.TryParse<DateRange, DateOnly>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateRange result)
        => RangeFormat.TryParse<DateRange, DateOnly>(s, provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
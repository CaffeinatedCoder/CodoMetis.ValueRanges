using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;
using NodaTime.Calendars;

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
/// proleptic Gregorian. Every calendar NodaTime ships spans a subset of the ISO calendar's range,
/// so the conversion cannot fail.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<LocalDateRange, LocalDate>))]
[TypeConverter(typeof(RangeTypeConverter<LocalDateRange, LocalDate>))]
public abstract partial record LocalDateRange : IRange<LocalDate>, IRangeFactory<LocalDateRange, LocalDate>
{
    private LocalDateRange()
    {
    }

    /// <summary>
    /// The copy constructor a <c>with</c> expression on a variant chains to.
    /// </summary>
    /// <remarks>
    /// A record's copy constructor cannot be private, so this check, not the private constructor
    /// above, is what keeps the union closed: a type declared outside the nested variants is
    /// refused here, before it can reach a switch.
    /// </remarks>
    /// <param name="original">The variant being copied.</param>
    /// <exception cref="InvalidOperationException">The runtime type is not one of the nested variants.</exception>
    protected LocalDateRange(LocalDateRange original) => ClosedUnion.Require(this);

    private static LocalDate ToIso(LocalDate value)
        => value.Calendar == CalendarSystem.Iso ? value : value.WithCalendar(CalendarSystem.Iso);

    /// <summary>
    /// Represents an empty <see cref="LocalDateRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, LocalDateRange, LocalDate>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, LocalDateRange, LocalDate>))]
    public sealed partial record EmptyRange : LocalDateRange, IEmptyRange<LocalDate>;

    /// <summary>
    /// Represents a <see cref="LocalDateRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, LocalDateRange, LocalDate>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, LocalDateRange, LocalDate>))]
    public sealed partial record Finite : LocalDateRange, IFiniteRange<LocalDate>
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

        /// <summary>
        /// Deconstructs the range into its bounds: <c>var (start, end) = finite;</c>.
        /// </summary>
        /// <remarks>
        /// Both bounds are inclusive — the canonical form of a discrete range — so the pair passes
        /// straight back to <see cref="LocalDateRange.CreateFinite"/>, whose default is closed.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        public void Deconstruct(out LocalDate start, out LocalDate end) => (start, end) = (Start, End);
    }

    /// <summary>
    /// Represents a <see cref="LocalDateRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, LocalDateRange, LocalDate>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, LocalDateRange, LocalDate>))]
    public sealed partial record UnboundedStart : LocalDateRange, IUnboundedStartRange<LocalDate>
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

        /// <summary>
        /// Deconstructs the range into its upper bound, as the positional variants of the
        /// other range types do: <c>range is LocalDateRange.UnboundedStart(var end)</c>.
        /// </summary>
        /// <param name="end">Receives <see cref="End"/>.</param>
        public void Deconstruct(out LocalDate end) => end = End;
    }

    /// <summary>
    /// Represents a <see cref="LocalDateRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, LocalDateRange, LocalDate>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, LocalDateRange, LocalDate>))]
    public sealed partial record UnboundedEnd : LocalDateRange, IUnboundedEndRange<LocalDate>
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

        /// <summary>
        /// Deconstructs the range into its lower bound, as the positional variants of the
        /// other range types do: <c>range is LocalDateRange.UnboundedEnd(var start)</c>.
        /// </summary>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        public void Deconstruct(out LocalDate start) => start = Start;
    }

    /// <summary>
    /// Represents a <see cref="LocalDateRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, LocalDateRange, LocalDate>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, LocalDateRange, LocalDate>))]
    public sealed partial record Infinity : LocalDateRange, IInfinityRange<LocalDate>;

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
        => ToIso(value) == LocalDate.MaxIsoValue ? null : value.PlusDays(1);

    /// <inheritdoc />
    public static LocalDate? PreviousValueBefore(LocalDate value)
        => ToIso(value) == LocalDate.MinIsoValue ? null : value.PlusDays(-1);

    // -------------------------------------------------------------------------
    // Calendar ranges
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the calendar month <paramref name="month"/> of <paramref name="year"/> in the ISO
    /// calendar, from its first day to its last: <c>LocalDateRange.Month(2026, 2)</c> is
    /// <c>[2026-02-01,2026-02-28]</c>.
    /// </summary>
    /// <param name="year">The ISO year, -9998 to 9999.</param>
    /// <param name="month">The month, 1 to 12.</param>
    /// <returns>The month as a closed range of days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year or the month is out of range.</exception>
    public static LocalDateRange Month(int year, int month)
        => new Finite(new LocalDate(year, month, 1), new LocalDate(year, month, CalendarSystem.Iso.GetDaysInMonth(year, month)));

    /// <summary>
    /// Returns the ISO calendar month that contains <paramref name="date"/>. A date in another
    /// calendar is converted to ISO first, as every bound of this type is.
    /// </summary>
    /// <param name="date">Any day of the month.</param>
    /// <returns>The month as a closed range of days.</returns>
    public static LocalDateRange Month(LocalDate date)
    {
        date = ToIso(date);
        return Month(date.Year, date.Month);
    }

    /// <summary>
    /// Returns quarter <paramref name="quarter"/> of <paramref name="year"/>: three ISO calendar
    /// months starting in January, April, July or October.
    /// </summary>
    /// <param name="year">The ISO year, -9998 to 9999.</param>
    /// <param name="quarter">The quarter, 1 to 4.</param>
    /// <returns>The quarter as a closed range of days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year or the quarter is out of range.</exception>
    public static LocalDateRange Quarter(int year, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);

        return new Finite(new LocalDate(year, quarter * 3 - 2, 1), new LocalDate(year, quarter * 3, CalendarSystem.Iso.GetDaysInMonth(year, quarter * 3)));
    }

    /// <summary>
    /// Returns the quarter that contains <paramref name="date"/>, in the ISO calendar.
    /// </summary>
    /// <param name="date">Any day of the quarter.</param>
    /// <returns>The quarter as a closed range of days.</returns>
    public static LocalDateRange Quarter(LocalDate date)
    {
        date = ToIso(date);
        return Quarter(date.Year, (date.Month + 2) / 3);
    }

    /// <summary>
    /// Returns the ISO calendar year <paramref name="year"/>, from January 1 to December 31.
    /// </summary>
    /// <param name="year">The ISO year, -9998 to 9999.</param>
    /// <returns>The year as a closed range of days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is out of range.</exception>
    public static LocalDateRange Year(int year) => new Finite(new LocalDate(year, 1, 1), new LocalDate(year, 12, 31));

    /// <summary>
    /// Returns the ISO calendar year that contains <paramref name="date"/>.
    /// </summary>
    /// <param name="date">Any day of the year.</param>
    /// <returns>The year as a closed range of days.</returns>
    public static LocalDateRange Year(LocalDate date) => Year(ToIso(date).Year);

    /// <summary>
    /// Returns ISO 8601 week <paramref name="week"/> of the ISO week-year <paramref name="weekYear"/>:
    /// Monday to Sunday.
    /// </summary>
    /// <remarks>
    /// Weeks are always ISO 8601 (<see cref="WeekYearRules.Iso"/>), never the current culture's: a
    /// week starts on Monday, and week 1 is the week that holds the year's first Thursday. The same
    /// rule as <c>DateRange.IsoWeek</c>.
    /// </remarks>
    /// <param name="weekYear">The ISO week-year.</param>
    /// <param name="week">The week, 1 to 52, or 53 in a long week-year.</param>
    /// <returns>The week as a closed range of seven days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The week-year or the week is out of range, or the week runs past either end of the ISO
    /// calendar.
    /// </exception>
    public static LocalDateRange IsoWeek(int weekYear, int week)
    {
        // The first week of -9998 starts before LocalDate.MinIsoValue, and the last of 9999 ends
        // after LocalDate.MaxIsoValue; NodaTime throws for the Monday or the Sunday, whichever is out.
        var monday = WeekYearRules.Iso.GetLocalDate(weekYear, week, IsoDayOfWeek.Monday);
        return new Finite(monday, WeekYearRules.Iso.GetLocalDate(weekYear, week, IsoDayOfWeek.Sunday));
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
    /// <exception cref="ArgumentOutOfRangeException">The week runs past either end of the ISO calendar.</exception>
    public static LocalDateRange IsoWeek(LocalDate date)
    {
        date = ToIso(date);
        return IsoWeek(WeekYearRules.Iso.GetWeekYear(date), WeekYearRules.Iso.GetWeekOfWeekYear(date));
    }

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
    /// Cuts the range into consecutive pieces of <paramref name="step"/> each, from its start:
    /// <c>[2026-01-15,2026-04-14].Split(Period.FromMonths(1))</c> is <c>[2026-01-15,2026-02-14]</c>, <c>[2026-02-15,2026-03-14]</c>, <c>[2026-03-15,2026-04-14]</c>.
    /// </summary>
    /// <remarks>
    /// The pieces are adjacent and hold exactly the values of the range, so
    /// <c>RangeSet.From(range.Split(step))</c> is the range itself. The last piece is clipped to the range's end and keeps the range's upper bound. Each boundary is counted from the range's start, so a split by months keeps the start's day where the calendar has it: from January 31, the pieces start on February 28, March 31 and April 30.
    /// The empty range yields nothing. This runs in memory only: PostgreSQL's
    /// <c>generate_series</c> returns rows, not a value.
    /// </remarks>
    /// <param name="step">The length of each piece; it must be positive.</param>
    /// <returns>The pieces, in order. The checks run at the call; the pieces are computed as you enumerate.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is zero or negative.</exception>
    public IEnumerable<LocalDateRange> Split(Period step)
    {
        NodaPeriods.RequirePositive(step, nameof(step));
        return RangeSplitting.Split<LocalDateRange, LocalDate>(this, RangeSplitting.Steps<LocalDate>(this, (start, n) => start.Plus(NodaPeriods.Times(step, n))));
    }

    /// <summary>
    /// Cuts the range at the boundaries of a calendar period: <c>range.Split(LocalDateRange.Month)</c> gives one
    /// piece per calendar month, the first and the last clipped to the range.
    /// </summary>
    /// <remarks>
    /// <paramref name="period"/> is called with the first day of each piece and returns the period that holds
    /// it, and the piece runs to that period's end. Pass <see cref="Month(LocalDate)"/>, <see cref="Quarter(LocalDate)"/>, <see cref="Year(LocalDate)"/> or <see cref="IsoWeek(LocalDate)"/>, or a function of your own,
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
    public IEnumerable<LocalDateRange> Split(Func<LocalDate, LocalDateRange> period)
        => RangeSplitting.Split<LocalDateRange, LocalDate>(this, RangeSplitting.Periods<LocalDateRange, LocalDate>(period));

    /// <summary>
    /// Enumerates the range's values from its start, <paramref name="step"/> apart:
    /// <c>[2026-06-01,2026-06-30].Values(Period.FromWeeks(1))</c> is every Monday of June 2026.
    /// </summary>
    /// <remarks>
    /// The stepping form of <see cref="Values()"/>, under the same rules: ascending, the empty range
    /// yields nothing, and an unbounded range throws at the call. Each boundary is counted from the range's start, so a split by months keeps the start's day where the calendar has it: from January 31, the pieces start on February 28, March 31 and April 30. This runs in memory only.
    /// </remarks>
    /// <param name="step">The distance between two values; it must be positive.</param>
    /// <returns>The values, ascending.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is zero or negative.</exception>
    public IEnumerable<LocalDate> Values(Period step)
    {
        NodaPeriods.RequirePositive(step, nameof(step));
        return RangeSplitting.Values<LocalDateRange, LocalDate>(this, RangeSplitting.Steps<LocalDate>(this, (start, n) => start.Plus(NodaPeriods.Times(step, n))));
    }

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
        => string.IsNullOrEmpty(format)
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

    /// <summary>
    /// Compares this range with <paramref name="other"/> in PostgreSQL's range order, the order
    /// <c>ORDER BY</c> and <c>&lt;</c> use on a range column: the empty range first, then by lower
    /// bound, then by upper bound.
    /// </summary>
    /// <remarks>
    /// An unbounded side is the smallest lower bound and the largest upper bound. At the same value
    /// an inclusive lower bound sorts first and an inclusive upper bound sorts last, so
    /// <c>[1,5)</c> sorts before <c>[1,5]</c>, which sorts before <c>(1,5)</c>. The result is 0 exactly
    /// when the two ranges are equal, and <see langword="null"/> sorts before every range.
    /// </remarks>
    /// <param name="other">The range to compare with.</param>
    /// <returns>
    /// A negative number when this range sorts first, 0 when the two are equal, and a positive
    /// number when <paramref name="other"/> sorts first or is <see langword="null"/>.
    /// </returns>
    public int CompareTo(LocalDateRange? other) => RangeOrder.Compare<LocalDate>(this, other);

    /// <inheritdoc cref="CompareTo(LocalDateRange)"/>
    /// <exception cref="ArgumentException"><paramref name="other"/> is not a <see cref="LocalDateRange"/>.</exception>
    int IComparable.CompareTo(object? other) => RangeOrder.CompareObject<LocalDateRange>(this, other, static (a, b) => a.CompareTo(b));

    /// <summary>
    /// Returns the range moved by <paramref name="delta"/>: both bounds move together and keep their
    /// inclusiveness, so <c>[2026-06-01,2026-06-08].Shift(Period.FromWeeks(1))</c> is <c>[2026-06-08,2026-06-15]</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="delta"/> moves the range down.
    /// A period with months or years follows NodaTime's calendar arithmetic, which clips to the end of a
    /// shorter month: <c>2026-01-31</c> plus one month is <c>2026-02-28</c>, and moving back gives
    /// <c>2026-01-28</c>. Days and weeks move every date by the same amount, so moving back restores
    /// the range. A period with a time part throws <see cref="ArgumentException"/>, as
    /// <see cref="LocalDate.Plus(Period)"/> does.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="delta">How far to move the range.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="LocalDate"/> can hold.</exception>
    public LocalDateRange Shift(Period delta) => RangeArithmetic.Move<LocalDateRange, LocalDate>(this, value => RangeArithmetic.Checked(() => value.Plus(delta)), value => RangeArithmetic.Checked(() => value.Plus(delta)));

    /// <summary>
    /// Returns the range grown by <paramref name="amount"/> on both sides: the lower bound moves down
    /// and the upper bound up, each keeping its inclusiveness, so <c>[2026-06-10,2026-06-12].Expand(Period.FromDays(1))</c> is <c>[2026-06-09,2026-06-13]</c>.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="amount"/> shrinks the range, and a range shrunk past its own width is
    /// <see cref="Empty"/>. <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an
    /// unbounded side stays unbounded. This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="amount">How far to move each bound outward.</param>
    /// <returns>The grown, or shrunk, range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="LocalDate"/> can hold.</exception>
    public LocalDateRange Expand(Period amount) => RangeArithmetic.Move<LocalDateRange, LocalDate>(this, value => RangeArithmetic.Checked(() => value.Minus(amount)), value => RangeArithmetic.Checked(() => value.Plus(amount)));

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

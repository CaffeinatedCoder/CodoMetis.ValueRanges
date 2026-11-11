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
/// A range over <see cref="YearMonth"/> values — a month-granularity range for billing and
/// reporting periods. PostgreSQL has no month-granularity range type; the EF Core companion
/// stores this type as a month-aligned <c>daterange</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[start, end]</c>.
/// As a discrete type, two ranges whose boundaries are exactly one month apart are considered adjacent.
/// </para>
/// <para>
/// Bounds must be in the ISO calendar. Unlike a <see cref="LocalDate"/> — where a non-ISO date
/// names the same physical day and normalizes losslessly — a non-ISO year-month spans parts of
/// two ISO months and has no ISO equivalent, so construction throws
/// <see cref="ArgumentException"/> instead of reinterpreting the value.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<YearMonthRange, YearMonth>))]
[TypeConverter(typeof(RangeTypeConverter<YearMonthRange, YearMonth>))]
public abstract partial record YearMonthRange : IRange<YearMonth>, IRangeFactory<YearMonthRange, YearMonth>
{
    private YearMonthRange()
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
    protected YearMonthRange(YearMonthRange original) => ClosedUnion.Require(this);

    private static readonly YearMonth MinIso = LocalDate.MinIsoValue.ToYearMonth();
    private static readonly YearMonth MaxIso = LocalDate.MaxIsoValue.ToYearMonth();

    private static YearMonth RequireIso(YearMonth value)
        => value.Calendar == CalendarSystem.Iso
               ? value
               : throw new ArgumentException(
                     $"YearMonthRange bounds must be in the ISO calendar; got {value} ({value.Calendar}). "
                   + "A non-ISO year-month spans parts of two ISO months and has no lossless ISO equivalent.");

    /// <summary>
    /// Represents an empty <see cref="YearMonthRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, YearMonthRange, YearMonth>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, YearMonthRange, YearMonth>))]
    public sealed partial record EmptyRange : YearMonthRange, IEmptyRange<YearMonth>;

    /// <summary>
    /// Represents a <see cref="YearMonthRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, YearMonthRange, YearMonth>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, YearMonthRange, YearMonth>))]
    public sealed partial record Finite : YearMonthRange, IFiniteRange<YearMonth>
    {
        internal Finite(YearMonth start, YearMonth end)
        {
            Start = start;
            End   = end;
        }

        /// <inheritdoc/>
        public YearMonth Start { get; }

        /// <inheritdoc/>
        public YearMonth End { get; }

        /// <inheritdoc/>
        public bool StartInclusive => true;

        /// <inheritdoc/>
        public bool EndInclusive => true;

        /// <summary>
        /// Deconstructs the range into its bounds: <c>var (start, end) = finite;</c>.
        /// </summary>
        /// <remarks>
        /// Both bounds are inclusive — the canonical form of a discrete range — so the pair passes
        /// straight back to <see cref="YearMonthRange.CreateFinite"/>, whose default is closed.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        public void Deconstruct(out YearMonth start, out YearMonth end) => (start, end) = (Start, End);
    }

    /// <summary>
    /// Represents a <see cref="YearMonthRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, YearMonthRange, YearMonth>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, YearMonthRange, YearMonth>))]
    public sealed partial record UnboundedStart : YearMonthRange, IUnboundedStartRange<YearMonth>
    {
        /// <summary>
        /// Creates a range unbounded on the left with the inclusive upper bound
        /// <paramref name="end"/> (which must be in the ISO calendar).
        /// </summary>
        /// <param name="end">The upper (right) bound of the range.</param>
        public UnboundedStart(YearMonth end) => End = RequireIso(end);

        /// <inheritdoc/>
        public YearMonth End { get; }

        /// <inheritdoc />
        public bool EndInclusive => true;

        /// <summary>
        /// Deconstructs the range into its upper bound, as the positional variants of the
        /// other range types do: <c>range is YearMonthRange.UnboundedStart(var end)</c>.
        /// </summary>
        /// <param name="end">Receives <see cref="End"/>.</param>
        public void Deconstruct(out YearMonth end) => end = End;
    }

    /// <summary>
    /// Represents a <see cref="YearMonthRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, YearMonthRange, YearMonth>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, YearMonthRange, YearMonth>))]
    public sealed partial record UnboundedEnd : YearMonthRange, IUnboundedEndRange<YearMonth>
    {
        /// <summary>
        /// Creates a range unbounded on the right with the inclusive lower bound
        /// <paramref name="start"/> (which must be in the ISO calendar).
        /// </summary>
        /// <param name="start">The lower (left) bound of the range.</param>
        public UnboundedEnd(YearMonth start) => Start = RequireIso(start);

        /// <inheritdoc/>
        public YearMonth Start { get; }

        /// <inheritdoc />
        public bool StartInclusive => true;

        /// <summary>
        /// Deconstructs the range into its lower bound, as the positional variants of the
        /// other range types do: <c>range is YearMonthRange.UnboundedEnd(var start)</c>.
        /// </summary>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        public void Deconstruct(out YearMonth start) => start = Start;
    }

    /// <summary>
    /// Represents a <see cref="YearMonthRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, YearMonthRange, YearMonth>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, YearMonthRange, YearMonth>))]
    public sealed partial record Infinity : YearMonthRange, IInfinityRange<YearMonth>;

    /// <summary>
    /// Creates a <see cref="YearMonthRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static YearMonthRange CreateUnboundedStart(YearMonth end, bool endInclusive = false)
    {
        end = RequireIso(end);
        return endInclusive
                   ? new UnboundedStart(end)
                   : PreviousValueBefore(end) is { } e
                       ? new UnboundedStart(e)
                       : Empty;
    }

    /// <summary>
    /// Creates a <see cref="YearMonthRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static YearMonthRange CreateUnboundedEnd(YearMonth start, bool startInclusive = true)
    {
        start = RequireIso(start);
        return startInclusive
                   ? new UnboundedEnd(start)
                   : NextValueAfter(start) is { } s
                       ? new UnboundedEnd(s)
                       : Empty;
    }

    /// <summary>
    /// Creates a <see cref="YearMonthRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all year-month values.</returns>
    public static YearMonthRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="YearMonthRange"/> that contains no values.
    /// </summary>
    public static YearMonthRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="YearMonthRange"/> bounded on both sides.
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
    public static YearMonthRange CreateFinite(
        YearMonth start,
        YearMonth end,
        bool      startInclusive = true,
        bool      endInclusive   = true
    ) => DiscreteCanonical.Finite<YearMonthRange, YearMonth>(RequireIso(start), RequireIso(end), startInclusive, endInclusive) is { } b
             ? new Finite(b.Start, b.End)
             : Empty;

    /// <inheritdoc />
    public static bool IsDiscrete => true;

    /// <inheritdoc />
    public static YearMonth? NextValueAfter(YearMonth value)
        => RequireIso(value) == MaxIso ? null : value.PlusMonths(1);

    /// <inheritdoc />
    public static YearMonth? PreviousValueBefore(YearMonth value)
        => RequireIso(value) == MinIso ? null : value.PlusMonths(-1);

    // -------------------------------------------------------------------------
    // Calendar ranges
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns quarter <paramref name="quarter"/> of <paramref name="year"/>: three months starting in
    /// January, April, July or October. <c>YearMonthRange.Quarter(2026, 4)</c> is
    /// <c>[2026-10,2026-12]</c>.
    /// </summary>
    /// <param name="year">The ISO year, -9998 to 9999.</param>
    /// <param name="quarter">The quarter, 1 to 4.</param>
    /// <returns>The quarter as a closed range of months.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year or the quarter is out of range.</exception>
    public static YearMonthRange Quarter(int year, int quarter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quarter, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarter, 4);

        return new Finite(new YearMonth(year, quarter * 3 - 2), new YearMonth(year, quarter * 3));
    }

    /// <summary>
    /// Returns the quarter that contains <paramref name="month"/>.
    /// </summary>
    /// <param name="month">Any month of the quarter, in the ISO calendar.</param>
    /// <returns>The quarter as a closed range of months.</returns>
    /// <exception cref="ArgumentException"><paramref name="month"/> is not in the ISO calendar.</exception>
    public static YearMonthRange Quarter(YearMonth month)
    {
        month = RequireIso(month);
        return Quarter(month.Year, (month.Month + 2) / 3);
    }

    /// <summary>
    /// Returns the twelve months of <paramref name="year"/>: <c>[2026-01,2026-12]</c>.
    /// </summary>
    /// <param name="year">The ISO year, -9998 to 9999.</param>
    /// <returns>The year as a closed range of months.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is out of range.</exception>
    public static YearMonthRange Year(int year) => new Finite(new YearMonth(year, 1), new YearMonth(year, 12));

    /// <summary>
    /// Returns the year that contains <paramref name="month"/>.
    /// </summary>
    /// <param name="month">Any month of the year, in the ISO calendar.</param>
    /// <returns>The year as a closed range of months.</returns>
    /// <exception cref="ArgumentException"><paramref name="month"/> is not in the ISO calendar.</exception>
    public static YearMonthRange Year(YearMonth month) => Year(RequireIso(month).Year);

    /// <summary>
    /// Enumerates the months the range contains, ascending and inclusive of both bounds.
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
    /// <returns>The contained months, ascending.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    public IEnumerable<YearMonth> Values() => DiscreteEnumeration.Values<YearMonthRange, YearMonth>(this);

    /// <summary>
    /// Cuts the range into consecutive pieces of <paramref name="months"/>, in months each, from its start:
    /// <c>[2026-01,2026-12].Split(5)</c> is <c>[2026-01,2026-05]</c>, <c>[2026-06,2026-10]</c>, <c>[2026-11,2026-12]</c>.
    /// </summary>
    /// <remarks>
    /// The pieces are adjacent and hold exactly the values of the range, so
    /// <c>RangeSet.From(range.Split(step))</c> is the range itself. The last piece is clipped to the range's end and keeps the range's upper bound.
    /// The empty range yields nothing. This runs in memory only; an EF Core
    /// query doesn't translate it.
    /// </remarks>
    /// <param name="months">The length of each piece, in months; it must be positive.</param>
    /// <returns>The pieces, in order. The checks run at the call; the pieces are computed as you enumerate.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="months"/> is zero or negative.</exception>
    public IEnumerable<YearMonthRange> Split(int months)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(months);
        return RangeSplitting.Split<YearMonthRange, YearMonth>(this, RangeSplitting.Steps<YearMonth>(this, (start, n) => start.PlusMonths(checked((int)(n * months)))));
    }

    /// <summary>
    /// Cuts the range at the boundaries of a calendar period: <c>range.Split(YearMonthRange.Quarter)</c> gives one
    /// piece per calendar quarter, the first and the last clipped to the range.
    /// </summary>
    /// <remarks>
    /// <paramref name="period"/> is called with the first month of each piece and returns the period that holds
    /// it, and the piece runs to that period's end. Pass <see cref="Quarter(YearMonth)"/> or <see cref="Year(YearMonth)"/>, or a function of your own,
    /// such as a fiscal year. The pieces are adjacent and hold exactly the values of the range. The
    /// empty range yields nothing. This runs in memory only.
    /// </remarks>
    /// <param name="period">Returns the period that holds a given month.</param>
    /// <returns>The pieces, in order. The checks run at the call; the pieces are computed as you enumerate.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="period"/> returned a range that is not bounded on both sides or does not hold
    /// the month it was given.
    /// </exception>
    public IEnumerable<YearMonthRange> Split(Func<YearMonth, YearMonthRange> period)
        => RangeSplitting.Split<YearMonthRange, YearMonth>(this, RangeSplitting.Periods<YearMonthRange, YearMonth>(period));

    /// <summary>
    /// Enumerates the range's values from its start, <paramref name="months"/>, in months apart:
    /// <c>[2026-01,2026-12].Values(3)</c> is January, April, July and October.
    /// </summary>
    /// <remarks>
    /// The stepping form of <see cref="Values()"/>, under the same rules: ascending, the empty range
    /// yields nothing, and an unbounded range throws at the call. This runs in memory only.
    /// </remarks>
    /// <param name="months">The distance between two values, in months; it must be positive.</param>
    /// <returns>The values, ascending.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="months"/> is zero or negative.</exception>
    public IEnumerable<YearMonth> Values(int months)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(months);
        return RangeSplitting.Values<YearMonthRange, YearMonth>(this, RangeSplitting.Steps<YearMonth>(this, (start, n) => start.PlusMonths(checked((int)(n * months)))));
    }

    /// <summary>
    /// The number of months the range covers, or <see langword="null"/> when it is unbounded.
    /// The empty range measures 0.
    /// </summary>
    /// <remarks>
    /// Inclusive of both ends, because the domain is discrete with a one-month step:
    /// <c>[2024-01, 2024-03]</c> measures 3, which is the answer a billing period gives.
    /// </remarks>
    public int? Length =>
        this switch
        {
            IEmptyRange<YearMonth>    => 0,
            IFiniteRange<YearMonth> f => (f.End.Year - f.Start.Year) * 12 + (f.End.Month - f.Start.Month) + 1,
            _                         => null
        };

    /// <inheritdoc />
    public static YearMonth ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseYearMonth(s.ToString());

    /// <summary>
    /// Formats a <see cref="YearMonth"/> value using ISO 8601 (<c>uuuu-MM</c>) by default.
    /// </summary>
    public static string FormatValue(YearMonth value, string? format, IFormatProvider? provider)
        => string.IsNullOrEmpty(format)
               ? NodaPatterns.YearMonth.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL-style range literal over ISO year-months
    /// (e.g. <c>[2024-01,2024-12]</c>, <c>empty</c>, <c>(,)</c>) into a <see cref="YearMonthRange"/>.
    /// </summary>
    public static YearMonthRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<YearMonthRange, YearMonth>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static YearMonthRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<YearMonthRange, YearMonth>(s, provider);

    /// <summary>
    /// Tries to parse a range literal over ISO year-months into a <see cref="YearMonthRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out YearMonthRange result)
        => RangeFormat.TryParse<YearMonthRange, YearMonth>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out YearMonthRange result)
        => RangeFormat.TryParse<YearMonthRange, YearMonth>(s, provider, out result);

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
    public int CompareTo(YearMonthRange? other) => RangeOrder.Compare<YearMonth>(this, other);

    /// <inheritdoc cref="CompareTo(YearMonthRange)"/>
    /// <exception cref="ArgumentException"><paramref name="other"/> is not a <see cref="YearMonthRange"/>.</exception>
    int IComparable.CompareTo(object? other) => RangeOrder.CompareObject<YearMonthRange>(this, other, static (a, b) => a.CompareTo(b));

    /// <summary>
    /// Returns the range moved by <paramref name="months"/>, in months: both bounds move together and keep their
    /// inclusiveness, so <c>[2026-01,2026-03].Shift(12)</c> is <c>[2027-01,2027-03]</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="months"/> moves the range down.
    /// This runs in memory only; an EF Core query doesn't translate it.
    /// The first and last month of the ISO calendar stand for PostgreSQL's <c>-infinity</c> and
    /// <c>infinity</c>, as their first and last day do, and they stay where they are, as
    /// <c>infinity ± d</c> does in PostgreSQL. A finite bound moved past either one still throws.
    /// </remarks>
    /// <param name="months">How far to move the range, in months.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="YearMonth"/> can hold.</exception>
    public YearMonthRange Shift(int months) => RangeArithmetic.MoveKeepingInfinity<YearMonthRange, YearMonth>(this, value => RangeArithmetic.Checked(() => value.PlusMonths(months)), value => RangeArithmetic.Checked(() => value.PlusMonths(months)), StartIsInfinity, EndIsInfinity);

    /// <summary>
    /// Returns the range grown by <paramref name="months"/>, in months on both sides: the lower bound moves down
    /// and the upper bound up, each keeping its inclusiveness, so <c>[2026-02,2026-03].Expand(1)</c> is <c>[2026-01,2026-04]</c>.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="months"/> shrinks the range, and a range shrunk past its own width is
    /// <see cref="Empty"/>. <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an
    /// unbounded side stays unbounded. This runs in memory only; an EF Core query doesn't translate it.
    /// The first and last month of the ISO calendar stand for PostgreSQL's <c>-infinity</c> and
    /// <c>infinity</c>, as their first and last day do, and they stay where they are, as
    /// <c>infinity ± d</c> does in PostgreSQL. A finite bound moved past either one still throws.
    /// </remarks>
    /// <param name="months">How far to move each bound outward, in months.</param>
    /// <returns>The grown, or shrunk, range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="YearMonth"/> can hold.</exception>
    public YearMonthRange Expand(int months) => RangeArithmetic.MoveKeepingInfinity<YearMonthRange, YearMonth>(this, value => RangeArithmetic.Checked(() => value.PlusMonths(checked(-months))), value => RangeArithmetic.Checked(() => value.PlusMonths(months)), StartIsInfinity, EndIsInfinity);

    /// <summary>
    /// Whether a lower bound stands for PostgreSQL's <c>-infinity</c>, which <see cref="Shift"/> and
    /// <see cref="Expand"/> leave where it is (#224): the first month, whose first day the store writes
    /// as <c>-infinity</c>. The last month as a lower bound starts on 9999-12-01, a date, so it moves.
    /// </summary>
    private static bool StartIsInfinity(YearMonth start) => start == MinIso;

    /// <summary>
    /// Whether an upper bound stands for <c>infinity</c>: the last month, whose last day the store
    /// writes as <c>infinity</c>. The first month as an upper bound ends on a date, so it moves (#257).
    /// </summary>
    private static bool EndIsInfinity(YearMonth end) => end == MaxIso;

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

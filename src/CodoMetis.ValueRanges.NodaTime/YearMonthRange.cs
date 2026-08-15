using System.Diagnostics;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
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
public abstract record YearMonthRange : IRange<YearMonth>, IRangeFactory<YearMonthRange, YearMonth>
{
    private YearMonthRange()
    {
    }

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
    public sealed record EmptyRange : YearMonthRange, IEmptyRange<YearMonth>;

    /// <summary>
    /// Represents a <see cref="YearMonthRange"/> bounded on both sides.
    /// </summary>
    public sealed record Finite : YearMonthRange, IFiniteRange<YearMonth>
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
    }

    /// <summary>
    /// Represents a <see cref="YearMonthRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    public sealed record UnboundedStart : YearMonthRange, IUnboundedStartRange<YearMonth>
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
    }

    /// <summary>
    /// Represents a <see cref="YearMonthRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    public sealed record UnboundedEnd : YearMonthRange, IUnboundedEndRange<YearMonth>
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
    }

    /// <summary>
    /// Represents a <see cref="YearMonthRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    public sealed record Infinity : YearMonthRange, IInfinityRange<YearMonth>;

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
    public static YearMonth? NextValueAfter(YearMonth value)
        => value == MaxIso ? null : value.PlusMonths(1);

    /// <inheritdoc />
    public static YearMonth? PreviousValueBefore(YearMonth value)
        => value == MinIso ? null : value.PlusMonths(-1);

    /// <inheritdoc />
    public static YearMonth ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseYearMonth(s.ToString());

    /// <summary>
    /// Formats a <see cref="YearMonth"/> value using ISO 8601 (<c>uuuu-MM</c>) by default.
    /// </summary>
    public static string FormatValue(YearMonth value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.YearMonth.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL-style range literal over ISO year-months
    /// (e.g. <c>[2024-01,2024-12]</c>, <c>empty</c>, <c>(,)</c>) into a <see cref="YearMonthRange"/>.
    /// </summary>
    public static YearMonthRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<YearMonthRange, YearMonth>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a range literal over ISO year-months into a <see cref="YearMonthRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out YearMonthRange result)
        => RangeFormat.TryParse<YearMonthRange, YearMonth>(s.AsSpan(), provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

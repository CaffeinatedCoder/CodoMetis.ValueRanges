using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// Conversions between the range types of this package and NodaTime's own interval types,
/// <see cref="Interval"/> and <see cref="DateInterval"/>.
/// </summary>
/// <remarks>
/// The NodaTime types are deliberately narrower than the range model: <see cref="Interval"/>
/// is always half-open <c>[start, end)</c> (with optionally absent ends) and cannot be empty
/// at no particular location; <see cref="DateInterval"/> is always finite and fully closed.
/// Conversions <em>from</em> the NodaTime types are therefore total, while conversions
/// <em>to</em> them are only defined for the shapes they can represent.
/// </remarks>
public static class NodaTimeInteropExtensions
{
    /// <summary>
    /// Converts a NodaTime <see cref="Interval"/> to an <see cref="InstantRange"/>.
    /// This conversion is total: an absent start or end maps to the corresponding unbounded
    /// shape, and an interval whose start equals its end maps to <see cref="InstantRange.Empty"/>.
    /// </summary>
    /// <param name="interval">The interval to convert.</param>
    /// <returns>The equivalent <see cref="InstantRange"/>.</returns>
    public static InstantRange ToInstantRange(this Interval interval)
        => (interval.HasStart, interval.HasEnd) switch
           {
               (true,  true)  => InstantRange.CreateFinite(interval.Start, interval.End),
               (true,  false) => InstantRange.CreateUnboundedEnd(interval.Start),
               (false, true)  => InstantRange.CreateUnboundedStart(interval.End),
               (false, false) => InstantRange.Infinite
           };

    /// <summary>
    /// Converts an <see cref="InstantRange"/> to a NodaTime <see cref="Interval"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Interval"/> is always half-open <c>[start, end)</c>, so only ranges of that
    /// form convert: a <see cref="InstantRange.Finite"/> with the default <c>[start, end)</c>
    /// bounds, an <see cref="InstantRange.UnboundedStart"/> with an exclusive end, an
    /// <see cref="InstantRange.UnboundedEnd"/> with an inclusive start, or
    /// <see cref="InstantRange.Infinity"/>.
    /// </remarks>
    /// <param name="range">The range to convert.</param>
    /// <returns>The equivalent <see cref="Interval"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The range is empty (an <see cref="Interval"/> has no empty representation), or its
    /// bound inclusiveness differs from the <c>[start, end)</c> form.
    /// </exception>
    public static Interval ToInterval(this InstantRange range)
        => range switch
           {
               InstantRange.Finite { StartInclusive: true, EndInclusive: false } f
                   => new Interval(f.Start, f.End),
               InstantRange.UnboundedStart { EndInclusive: false } s
                   => new Interval(null, s.End),
               InstantRange.UnboundedEnd { StartInclusive: true } e
                   => new Interval(e.Start, null),
               InstantRange.Infinity
                   => new Interval(null, null),
               InstantRange.EmptyRange
                   => throw new InvalidOperationException(
                          "An empty range cannot be converted to a NodaTime Interval: an Interval is anchored at instants and has no empty representation."),
               _
                   => throw new InvalidOperationException(
                          $"The range {range} cannot be converted to a NodaTime Interval: an Interval is always half-open [start, end).")
           };

    /// <summary>
    /// Converts a NodaTime <see cref="DateInterval"/> to a <see cref="LocalDateRange"/>.
    /// This conversion is total: a <see cref="DateInterval"/> is always finite and fully
    /// closed, exactly the canonical form of <see cref="LocalDateRange.Finite"/>.
    /// </summary>
    /// <param name="interval">The date interval to convert.</param>
    /// <returns>The equivalent finite <see cref="LocalDateRange"/>.</returns>
    public static LocalDateRange ToLocalDateRange(this DateInterval interval)
        => LocalDateRange.CreateFinite(interval.Start, interval.End);

    /// <summary>
    /// Converts a finite <see cref="LocalDateRange"/> to a NodaTime <see cref="DateInterval"/>.
    /// Declared on <see cref="LocalDateRange.Finite"/> because a <see cref="DateInterval"/>
    /// cannot represent the unbounded or empty shapes — pattern match first.
    /// </summary>
    /// <param name="range">The finite range to convert.</param>
    /// <returns>The equivalent <see cref="DateInterval"/>, fully closed on both sides.</returns>
    public static DateInterval ToDateInterval(this LocalDateRange.Finite range)
        => new(range.Start, range.End);

    /// <summary>
    /// Converts a <see cref="YearMonthRange"/> to the <see cref="LocalDateRange"/> covering
    /// exactly the days of its months. This conversion is total: each bound month expands to
    /// its first or last day, and the empty and unbounded shapes map to their counterparts.
    /// This is also how the EF Core companion stores a <see cref="YearMonthRange"/> in a
    /// PostgreSQL <c>daterange</c> column.
    /// </summary>
    /// <param name="range">The range to convert.</param>
    /// <returns>The equivalent month-aligned <see cref="LocalDateRange"/>.</returns>
    public static LocalDateRange ToLocalDateRange(this YearMonthRange range)
        => range switch
           {
               YearMonthRange.EmptyRange     => LocalDateRange.Empty,
               YearMonthRange.Infinity       => LocalDateRange.Infinite,
               YearMonthRange.Finite f       => LocalDateRange.CreateFinite(f.Start.OnDayOfMonth(1), LastDayOf(f.End)),
               YearMonthRange.UnboundedStart s => LocalDateRange.CreateUnboundedStart(LastDayOf(s.End), endInclusive: true),
               YearMonthRange.UnboundedEnd e => LocalDateRange.CreateUnboundedEnd(e.Start.OnDayOfMonth(1)),
               _ => throw new InvalidOperationException($"Unknown range variant: {range.GetType()}.")
           };

    /// <summary>
    /// Converts a month-aligned <see cref="LocalDateRange"/> back to a <see cref="YearMonthRange"/> —
    /// the inverse of <see cref="ToLocalDateRange(YearMonthRange)"/>.
    /// </summary>
    /// <param name="range">The range to convert. Its canonical closed bounds must lie on month
    /// boundaries: lower bounds on the first day of a month, upper bounds on the last.</param>
    /// <returns>The equivalent <see cref="YearMonthRange"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// A bound does not lie on a month boundary, so the range covers a partial month and has
    /// no month-granularity equivalent.
    /// </exception>
    public static YearMonthRange ToYearMonthRange(this LocalDateRange range)
        => range switch
           {
               LocalDateRange.EmptyRange     => YearMonthRange.Empty,
               LocalDateRange.Infinity       => YearMonthRange.Infinite,
               LocalDateRange.Finite f       => YearMonthRange.CreateFinite(MonthOfFirstDay(f.Start), MonthOfLastDay(f.End)),
               LocalDateRange.UnboundedStart s => YearMonthRange.CreateUnboundedStart(MonthOfLastDay(s.End), endInclusive: true),
               LocalDateRange.UnboundedEnd e => YearMonthRange.CreateUnboundedEnd(MonthOfFirstDay(e.Start)),
               _ => throw new InvalidOperationException($"Unknown range variant: {range.GetType()}.")
           };

    /// <summary>
    /// Converts a finite <see cref="YearMonthRange"/> to the NodaTime <see cref="DateInterval"/>
    /// covering exactly the days of its months. Declared on <see cref="YearMonthRange.Finite"/>
    /// because a <see cref="DateInterval"/> cannot represent the unbounded or empty shapes —
    /// pattern match first.
    /// </summary>
    /// <param name="range">The finite range to convert.</param>
    /// <returns>The equivalent <see cref="DateInterval"/>, from the first day of the start
    /// month through the last day of the end month.</returns>
    public static DateInterval ToDateInterval(this YearMonthRange.Finite range)
        => new(range.Start.OnDayOfMonth(1), LastDayOf(range.End));

    private static LocalDate LastDayOf(YearMonth month) => month.ToDateInterval().End;

    private static YearMonth MonthOfFirstDay(LocalDate date)
        => date.Day == 1
               ? date.ToYearMonth()
               : throw new InvalidOperationException(
                     $"The date range bound {date} is not the first day of a month; the range covers a partial month and cannot convert to a YearMonthRange.");

    private static YearMonth MonthOfLastDay(LocalDate date)
        => date == LastDayOf(date.ToYearMonth())
               ? date.ToYearMonth()
               : throw new InvalidOperationException(
                     $"The date range bound {date} is not the last day of a month; the range covers a partial month and cannot convert to a YearMonthRange.");
}

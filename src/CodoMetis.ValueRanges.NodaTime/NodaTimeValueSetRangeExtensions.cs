using CodoMetis.ValueRanges.Internals;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// The NodaTime half of the value-set ↔ range-set bridge, for the two discrete NodaTime
/// domains. See <see cref="ValueSetRangeExtensions"/> for what the conversion is for.
/// </summary>
/// <remarks>
/// <c>LocalDateTimeSet</c>, <c>InstantSet</c> and <c>LocalTimeSet</c> are absent deliberately:
/// their domains are continuous, so there is no step to collapse runs along.
/// </remarks>
public static class NodaTimeValueSetRangeExtensions
{
    /// <summary>
    /// Collapses runs of consecutive dates into ranges. The empty set becomes the empty range set.
    /// </summary>
    public static RangeSet<LocalDateRange, LocalDate> ToRangeSet(this LocalDateSet set)
        => SetRangeBridge.ToRangeSet<LocalDateRange, LocalDate>(set.Values);

    /// <summary>
    /// Collapses runs of consecutive months into ranges: the twelve months of a year become one
    /// <c>[2024-01, 2024-12]</c> range. The empty set becomes the empty range set.
    /// </summary>
    public static RangeSet<YearMonthRange, YearMonth> ToRangeSet(this YearMonthSet set)
        => SetRangeBridge.ToRangeSet<YearMonthRange, YearMonth>(set.Values);

    /// <summary>
    /// Expands every range to the dates it contains, both bounds included.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The set is unbounded on either side, so the expansion would not terminate.
    /// </exception>
    public static LocalDateSet ToLocalDateSet(this RangeSet<LocalDateRange, LocalDate> ranges)
        => LocalDateSet.From(SetRangeBridge.ToValues(ranges));

    /// <summary>
    /// Expands every range to the months it contains, both bounds included.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The set is unbounded on either side, so the expansion would not terminate.
    /// </exception>
    public static YearMonthSet ToYearMonthSet(this RangeSet<YearMonthRange, YearMonth> ranges)
        => YearMonthSet.From(SetRangeBridge.ToValues(ranges));
}

using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// Converts between the value sets and the range sets over the same discrete domain.
/// <c>{1,2,3,7}</c> and <c>{[1,3],[7,7]}</c> describe the same membership; these move between
/// the two representations without changing what is contained.
/// </summary>
/// <remarks>
/// <para>
/// Only the discrete families convert: <see cref="Int32Set"/>, <see cref="Int64Set"/> and
/// <see cref="DateSet"/> here, plus <c>LocalDateSet</c> and <c>YearMonthSet</c> in the NodaTime
/// satellite. The continuous domains have no step, so there is no set of values to expand to.
/// </para>
/// <para>
/// The conversion is worth making when density changes: a set of a thousand consecutive dates is
/// one range, and a `daterange` column serves <c>@&gt;</c> against it far better than a
/// thousand-element array does. Going the other way suits sparse data, where ranges of one value
/// each cost more than the values.
/// </para>
/// <para>
/// Both directions run client-side and neither is translatable — PostgreSQL converts between
/// arrays and multiranges only through <c>unnest</c> and a custom aggregate.
/// </para>
/// </remarks>
public static class ValueSetRangeExtensions
{
    /// <summary>
    /// Collapses runs of consecutive integers into ranges: <c>{1,2,3,7}</c> becomes
    /// <c>{[1,3],[7,7]}</c>. The empty set becomes the empty range set.
    /// </summary>
    public static RangeSet<Int32Range, int> ToRangeSet(this Int32Set set)
        => SetRangeBridge.ToRangeSet<Int32Range, int>(set.Values);

    /// <inheritdoc cref="ToRangeSet(Int32Set)"/>
    public static RangeSet<Int64Range, long> ToRangeSet(this Int64Set set)
        => SetRangeBridge.ToRangeSet<Int64Range, long>(set.Values);

    /// <summary>
    /// Collapses runs of consecutive dates into ranges: four dates spanning a long weekend
    /// become one <c>[Fri, Mon]</c> range. The empty set becomes the empty range set.
    /// </summary>
    public static RangeSet<DateRange, DateOnly> ToRangeSet(this DateSet set)
        => SetRangeBridge.ToRangeSet<DateRange, DateOnly>(set.Values);

    /// <summary>
    /// Expands every range to the integers it contains: <c>{[1,3],[7,7]}</c> becomes
    /// <c>{1,2,3,7}</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The set is unbounded on either side, so the expansion would not terminate.
    /// </exception>
    public static Int32Set ToInt32Set(this RangeSet<Int32Range, int> ranges)
        => Int32Set.From(SetRangeBridge.ToValues(ranges));

    /// <inheritdoc cref="ToInt32Set(RangeSet{Int32Range, int})"/>
    public static Int64Set ToInt64Set(this RangeSet<Int64Range, long> ranges)
        => Int64Set.From(SetRangeBridge.ToValues(ranges));

    /// <summary>
    /// Expands every range to the dates it contains, both bounds included.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The set is unbounded on either side, so the expansion would not terminate.
    /// </exception>
    public static DateSet ToDateSet(this RangeSet<DateRange, DateOnly> ranges)
        => DateSet.From(SetRangeBridge.ToValues(ranges));
}

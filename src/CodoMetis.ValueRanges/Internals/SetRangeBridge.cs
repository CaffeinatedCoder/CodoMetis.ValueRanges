using System.Collections.Immutable;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// Converts between the two shapes a discrete value domain can take: a set of individual values
/// and a set of ranges over them. <c>{1,2,3,7}</c> and <c>{[1,3],[7,7]}</c> describe the same
/// membership; which one to store is a question of density.
/// </summary>
/// <remarks>
/// Both directions are client-side. PostgreSQL can convert neither an array to a multirange nor
/// the reverse without <c>unnest</c> and a custom aggregate, and doing it in SQL would move a
/// row's whole contents through the server to answer a question the client already has the data
/// for.
/// </remarks>
internal static class SetRangeBridge
{
    /// <summary>
    /// Collapses runs of consecutive values into ranges. The input must already be in canonical
    /// form — sorted and deduplicated — which every value set guarantees.
    /// </summary>
    internal static RangeSet<TRange, T> ToRangeSet<TRange, T>(ImmutableArray<T> values)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (values.IsEmpty) return RangeSet<TRange, T>.Empty;

        var ranges = new List<TRange>();
        var start  = values[0];
        var last   = values[0];

        foreach (var value in values.AsSpan()[1..])
        {
            // A run continues only when the next value is the immediate successor. Equality on
            // NextValueAfter rather than a comparison, so a gap of any size ends the run.
            if (TRange.NextValueAfter(last) is { } successor && successor.Equals(value))
            {
                last = value;
                continue;
            }

            ranges.Add(TRange.CreateFinite(start, last, true, true));
            start = last = value;
        }

        ranges.Add(TRange.CreateFinite(start, last, true, true));

        // From rather than a trusted wrap: the runs are already sorted and disjoint, but
        // adjacent runs cannot occur by construction and letting From prove that is cheap.
        return RangeSet<TRange, T>.From(ranges);
    }

    /// <summary>
    /// Expands every range in the set to the values it contains, ascending.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The set is unbounded on either side, so the expansion would not terminate.
    /// </exception>
    internal static IEnumerable<T> ToValues<TRange, T>(RangeSet<TRange, T> ranges)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        // Eager, so an unbounded set fails at the call rather than mid-enumeration.
        foreach (var range in ranges)
            if (range is not IFiniteRange<T>)
                throw new NotSupportedException(
                    $"Cannot expand '{ranges}' to values: the element '{range}' is unbounded and "
                  + "would not terminate. Intersect the set with a bounded range first.");

        return ranges.SelectMany(range => DiscreteEnumeration.Values<TRange, T>(range));
    }
}

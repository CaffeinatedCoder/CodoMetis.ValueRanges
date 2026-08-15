using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges;

/// <summary>
/// Aggregate operations over sequences of ranges — the in-memory counterparts of the
/// PostgreSQL <c>range_agg</c> and <c>range_intersect_agg</c> aggregate functions.
/// The EF Core provider translates them to those aggregates inside grouped queries.
/// </summary>
/// <remarks>
/// The overloads are declared per range type because C# cannot infer the element type
/// <c>T</c> from the range type alone (constraints do not participate in type inference).
/// </remarks>
public static class RangeAggregateExtensions
{
    /// <summary>
    /// Aggregates the ranges into a normalized <see cref="RangeSet{TRange,T}"/> —
    /// equivalent to the PostgreSQL <c>range_agg</c> aggregate. Overlapping and adjacent
    /// inputs are merged; empty inputs are dropped.
    /// </summary>
    /// <remarks>
    /// An empty source yields <see cref="RangeSet{TRange,T}.Empty"/>. Note that PostgreSQL
    /// aggregates return <c>NULL</c> over zero rows, so the translated SQL can materialize
    /// <see langword="null"/> where the in-memory operation returns the empty set.
    /// </remarks>
    /// <param name="source">The ranges to aggregate.</param>
    /// <returns>A normalized set covering every value of every input range.</returns>
    public static RangeSet<Int32Range, int> RangeAgg(this IEnumerable<Int32Range> source)
        => RangeSet<Int32Range, int>.From(source);

    /// <inheritdoc cref="RangeAgg(IEnumerable{Int32Range})"/>
    public static RangeSet<Int64Range, long> RangeAgg(this IEnumerable<Int64Range> source)
        => RangeSet<Int64Range, long>.From(source);

    /// <inheritdoc cref="RangeAgg(IEnumerable{Int32Range})"/>
    public static RangeSet<DecimalRange, decimal> RangeAgg(this IEnumerable<DecimalRange> source)
        => RangeSet<DecimalRange, decimal>.From(source);

    /// <inheritdoc cref="RangeAgg(IEnumerable{Int32Range})"/>
    public static RangeSet<DateRange, DateOnly> RangeAgg(this IEnumerable<DateRange> source)
        => RangeSet<DateRange, DateOnly>.From(source);

    /// <inheritdoc cref="RangeAgg(IEnumerable{Int32Range})"/>
    public static RangeSet<DateTimeRange, DateTime> RangeAgg(this IEnumerable<DateTimeRange> source)
        => RangeSet<DateTimeRange, DateTime>.From(source);

    /// <inheritdoc cref="RangeAgg(IEnumerable{Int32Range})"/>
    public static RangeSet<DateTimeOffsetRange, DateTimeOffset> RangeAgg(this IEnumerable<DateTimeOffsetRange> source)
        => RangeSet<DateTimeOffsetRange, DateTimeOffset>.From(source);

    /// <inheritdoc cref="RangeAgg(IEnumerable{Int32Range})"/>
    public static RangeSet<TimeRange, TimeOnly> RangeAgg(this IEnumerable<TimeRange> source)
        => RangeSet<TimeRange, TimeOnly>.From(source);

    /// <summary>
    /// Intersects all ranges of the sequence into a single range — equivalent to the
    /// PostgreSQL <c>range_intersect_agg</c> aggregate.
    /// </summary>
    /// <remarks>
    /// An empty source yields <see langword="null"/>, matching the <c>NULL</c> PostgreSQL
    /// returns for aggregates over zero rows. Disjoint inputs collapse to
    /// <see cref="IRangeFactory{TRange,T}.Empty"/>.
    /// </remarks>
    /// <param name="source">The ranges to intersect.</param>
    /// <returns>
    /// The intersection of all input ranges, or <see langword="null"/> for an empty source.
    /// </returns>
    public static Int32Range? RangeIntersectAgg(this IEnumerable<Int32Range> source)
        => IntersectAggCore<Int32Range, int>(source);

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{Int32Range})"/>
    public static Int64Range? RangeIntersectAgg(this IEnumerable<Int64Range> source)
        => IntersectAggCore<Int64Range, long>(source);

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{Int32Range})"/>
    public static DecimalRange? RangeIntersectAgg(this IEnumerable<DecimalRange> source)
        => IntersectAggCore<DecimalRange, decimal>(source);

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{Int32Range})"/>
    public static DateRange? RangeIntersectAgg(this IEnumerable<DateRange> source)
        => IntersectAggCore<DateRange, DateOnly>(source);

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{Int32Range})"/>
    public static DateTimeRange? RangeIntersectAgg(this IEnumerable<DateTimeRange> source)
        => IntersectAggCore<DateTimeRange, DateTime>(source);

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{Int32Range})"/>
    public static DateTimeOffsetRange? RangeIntersectAgg(this IEnumerable<DateTimeOffsetRange> source)
        => IntersectAggCore<DateTimeOffsetRange, DateTimeOffset>(source);

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{Int32Range})"/>
    public static TimeRange? RangeIntersectAgg(this IEnumerable<TimeRange> source)
        => IntersectAggCore<TimeRange, TimeOnly>(source);

    private static TRange? IntersectAggCore<TRange, T>(IEnumerable<TRange> source)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        TRange? accumulator = null;
        foreach (var range in source)
        {
            accumulator = accumulator is null ? range : accumulator.Intersect(range);
        }

        return accumulator;
    }
}

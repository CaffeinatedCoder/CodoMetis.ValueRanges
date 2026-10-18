using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges;

/// <summary>
/// Aggregate operations over sequences of ranges — the in-memory counterparts of the
/// PostgreSQL <c>range_agg</c> and <c>range_intersect_agg</c> aggregate functions.
/// The EF Core provider translates them to those aggregates inside grouped queries.
/// </summary>
/// <remarks>
/// The overloads over ranges are declared per range type because C# cannot infer the element type
/// <c>T</c> from the range type alone (constraints do not participate in type inference). The
/// overloads over sets are generic, because <see cref="RangeSet{TRange,T}"/> names both, and so
/// serve every range type, the NodaTime ones included.
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
    public static RangeSet<Float64Range, double> RangeAgg(this IEnumerable<Float64Range> source)
        => RangeSet<Float64Range, double>.From(source);

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
    public static Float64Range? RangeIntersectAgg(this IEnumerable<Float64Range> source)
        => IntersectAggCore<Float64Range, double>(source);

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

    /// <summary>
    /// Aggregates the sets into one normalized <see cref="RangeSet{TRange,T}"/>, the union of all of
    /// them — equivalent to the PostgreSQL <c>range_agg</c> aggregate over multiranges.
    /// </summary>
    /// <remarks>
    /// An empty source yields <see cref="RangeSet{TRange,T}.Empty"/>, and an empty set adds nothing.
    /// As with the range overloads, PostgreSQL returns <c>NULL</c> over zero rows.
    /// </remarks>
    /// <param name="source">The sets to aggregate.</param>
    /// <typeparam name="TRange">The range type of the sets.</typeparam>
    /// <typeparam name="T">The element type of the ranges.</typeparam>
    /// <returns>A normalized set covering every value of every input set.</returns>
    public static RangeSet<TRange, T> RangeAgg<TRange, T>(this IEnumerable<RangeSet<TRange, T>> source)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => RangeSet<TRange, T>.From(source.SelectMany(set => set));

    /// <summary>
    /// Intersects all sets of the sequence into one set — equivalent to the PostgreSQL
    /// <c>range_intersect_agg</c> aggregate over multiranges.
    /// </summary>
    /// <remarks>
    /// An empty source yields <see langword="null"/>, matching the <c>NULL</c> PostgreSQL returns
    /// for aggregates over zero rows. Sets with no value in common, or an empty set among the
    /// inputs, give <see cref="RangeSet{TRange,T}.Empty"/>.
    /// </remarks>
    /// <param name="source">The sets to intersect.</param>
    /// <typeparam name="TRange">The range type of the sets.</typeparam>
    /// <typeparam name="T">The element type of the ranges.</typeparam>
    /// <returns>
    /// The values common to every input set, or <see langword="null"/> for an empty source.
    /// </returns>
    public static RangeSet<TRange, T>? RangeIntersectAgg<TRange, T>(this IEnumerable<RangeSet<TRange, T>> source)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        RangeSet<TRange, T>? accumulator = null;
        foreach (var set in source)
        {
            accumulator = accumulator is null ? set : accumulator.Intersect(set);
        }

        return accumulator;
    }

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

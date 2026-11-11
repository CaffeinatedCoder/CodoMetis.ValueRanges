using CodoMetis.ValueRanges.Core;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// Aggregate operations over sequences of NodaTime ranges — the in-memory counterparts of the
/// PostgreSQL <c>range_agg</c> and <c>range_intersect_agg</c> aggregate functions.
/// </summary>
/// <remarks>
/// The overloads are declared per range type because C# cannot infer the element type
/// <c>T</c> from the range type alone (constraints do not participate in type inference).
/// </remarks>
public static class NodaTimeRangeAggregateExtensions
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
    /// A <see langword="null"/> range is skipped, as PostgreSQL skips a <c>NULL</c> input, so a source
    /// of nothing but nulls is an empty source.
    /// </remarks>
    /// <param name="source">The ranges to aggregate.</param>
    /// <returns>A normalized set covering every value of every input range.</returns>
    public static RangeSet<LocalDateRange, LocalDate> RangeAgg(this IEnumerable<LocalDateRange> source)
        => RangeSet<LocalDateRange, LocalDate>.From(WithoutNulls(source));

    /// <inheritdoc cref="RangeAgg(IEnumerable{LocalDateRange})"/>
    public static RangeSet<LocalDateTimeRange, LocalDateTime> RangeAgg(this IEnumerable<LocalDateTimeRange> source)
        => RangeSet<LocalDateTimeRange, LocalDateTime>.From(WithoutNulls(source));

    /// <inheritdoc cref="RangeAgg(IEnumerable{LocalDateRange})"/>
    public static RangeSet<InstantRange, Instant> RangeAgg(this IEnumerable<InstantRange> source)
        => RangeSet<InstantRange, Instant>.From(WithoutNulls(source));

    /// <inheritdoc cref="RangeAgg(IEnumerable{LocalDateRange})"/>
    public static RangeSet<YearMonthRange, YearMonth> RangeAgg(this IEnumerable<YearMonthRange> source)
        => RangeSet<YearMonthRange, YearMonth>.From(WithoutNulls(source));

    /// <summary>
    /// Intersects all ranges of the sequence into a single range — equivalent to the
    /// PostgreSQL <c>range_intersect_agg</c> aggregate.
    /// </summary>
    /// <remarks>
    /// An empty source yields <see langword="null"/>, matching the <c>NULL</c> PostgreSQL
    /// returns for aggregates over zero rows. Disjoint inputs collapse to
    /// <see cref="IRangeFactory{TRange,T}.Empty"/>.
    /// A <see langword="null"/> range is skipped, as PostgreSQL skips a <c>NULL</c> input, so a source
    /// of nothing but nulls is an empty source.
    /// </remarks>
    /// <param name="source">The ranges to intersect.</param>
    /// <returns>
    /// The intersection of all input ranges, or <see langword="null"/> for an empty source.
    /// </returns>
    public static LocalDateRange? RangeIntersectAgg(this IEnumerable<LocalDateRange> source)
        => IntersectAggCore<LocalDateRange, LocalDate>(WithoutNulls(source));

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{LocalDateRange})"/>
    public static LocalDateTimeRange? RangeIntersectAgg(this IEnumerable<LocalDateTimeRange> source)
        => IntersectAggCore<LocalDateTimeRange, LocalDateTime>(WithoutNulls(source));

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{LocalDateRange})"/>
    public static InstantRange? RangeIntersectAgg(this IEnumerable<InstantRange> source)
        => IntersectAggCore<InstantRange, Instant>(WithoutNulls(source));

    /// <inheritdoc cref="RangeIntersectAgg(IEnumerable{LocalDateRange})"/>
    public static YearMonthRange? RangeIntersectAgg(this IEnumerable<YearMonthRange> source)
        => IntersectAggCore<YearMonthRange, YearMonth>(WithoutNulls(source));

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

    /// <summary>
    /// The source without its <see langword="null"/> elements. PostgreSQL's aggregates skip a
    /// <c>NULL</c> input, so a group over a nullable column aggregates the values it has.
    /// </summary>
    private static IEnumerable<TItem> WithoutNulls<TItem>(IEnumerable<TItem?> source)
        where TItem : class
        => source.OfType<TItem>();
}

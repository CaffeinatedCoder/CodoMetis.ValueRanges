using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The generic implementation of <see cref="IRangeTypeDefinition"/>: given only the two
/// store type names (and an optional element normalization for the way to the database),
/// it derives all CLR types and builds the range and multirange type mappings.
/// </summary>
/// <typeparam name="TRange">The range type, e.g. <c>DateRange</c>.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
internal sealed class RangeTypeDefinition<TRange, T>(
    string      rangeStoreType,
    string      multirangeStoreType,
    string      elementStoreType,
    Func<T, T>? normalizeValue = null
)
    : IRangeTypeDefinition
    where TRange : class, IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    public Type RangeClrType => typeof(TRange);

    public Type ElementClrType => typeof(T);

    public Type RangeSetClrType => typeof(RangeSet<TRange, T>);

    public string RangeStoreType { get; } = rangeStoreType;

    public string MultirangeStoreType { get; } = multirangeStoreType;

    public string ElementStoreType { get; } = elementStoreType;

    public RelationalTypeMapping RangeTypeMapping { get; } = new ValueRangeTypeMapping<TRange, T>(rangeStoreType, normalizeValue);

    public RelationalTypeMapping RangeSetTypeMapping { get; } = 
        new ValueRangeSetTypeMapping<TRange, T>(multirangeStoreType, normalizeValue);

    public object EmptyRange => TRange.Empty;

    public object InfiniteRangeSet => RangeSet<TRange, T>.Infinite;
}
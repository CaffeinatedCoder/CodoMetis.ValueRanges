namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Marker interface for all range types over a comparable, equatable value type.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    internal TRange IntersectWith<TRange>(IRange<T> other)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        => TRange.Empty;

    internal TRange MergeWith<TRange>(IRange<T> other)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        => TRange.Empty;
}
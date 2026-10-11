using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Marker interface for a range that is unbounded on both sides: <c>(-∞, +∞)</c>.
/// An infinity range contains every value of the element type.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
[JsonConverter(typeof(InterfaceDeclaredJsonConverter))]
public interface IInfinityRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    TRange IRange<T>.IntersectWith<TRange>(IRange<T> other) => RangeBoundHelpers.RecreateAs<TRange, T>(other);

    TRange IRange<T>.MergeWith<TRange>(IRange<T> other) => TRange.Infinite;
}
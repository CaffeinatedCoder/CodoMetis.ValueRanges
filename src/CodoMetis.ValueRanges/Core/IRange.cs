using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Serialization;
namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Marker interface for all range types over a comparable, equatable value type.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
[JsonConverter(typeof(InterfaceDeclaredJsonConverter))]
public interface IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>
    /// Checks a scalar operand (<c>Contains(T)</c>, <c>Clamp(T)</c>, <c>RangeSet.Contains(T)</c>)
    /// against the values the type accepts as a bound, so an element the factories refuse is refused
    /// as an operand too, loudly, rather than answered.
    /// </summary>
    /// <remarks>
    /// The identity by default. <c>Float64Range</c> and <c>Float32Range</c> refuse <c>NaN</c> and the
    /// infinities here as they do at construction: under <c>CompareTo</c>, <c>[0,)</c> does not contain
    /// <c>NaN</c>, while PostgreSQL's <c>@&gt;</c> answers <see langword="true"/> (#153). It is the
    /// range-side sibling of <c>IValueSet&lt;T&gt;.NormalizeElement</c>.
    /// </remarks>
    internal T NormalizeElement(T value) => value;

    internal TRange IntersectWith<TRange>(IRange<T> other)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        => TRange.Empty;

    internal TRange MergeWith<TRange>(IRange<T> other)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        => TRange.Empty;
}
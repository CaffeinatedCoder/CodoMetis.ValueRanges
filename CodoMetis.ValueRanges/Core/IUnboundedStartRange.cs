using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Represents a range that is unbounded on the left: <c>(-∞, UpperBound]</c> or <c>(-∞, UpperBound)</c>.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IUnboundedStartRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>Gets the upper (right) bound of the range.</summary>
    T End { get; }

    /// <summary>Gets a value indicating whether <see cref="End"/> is included in the range.</summary>
    bool EndInclusive { get; }

    TRange IRange<T>.IntersectWith<TRange>(IRange<T> other) => IntersectEngine.Execute<TRange, T>(this, other);

    TRange IRange<T>.MergeWith<TRange>(IRange<T> other) => MergeEngine.Execute<TRange, T>(this, other);
}
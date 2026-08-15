using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Represents a range that is unbounded on the right: <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IUnboundedEndRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>Gets the lower (left) bound of the range.</summary>
    T Start { get; }

    /// <summary>Gets a value indicating whether <see cref="Start"/> is included in the range.</summary>
    bool StartInclusive { get; }

    TRange IRange<T>.IntersectWith<TRange>(IRange<T> other) => IntersectEngine.Execute<TRange, T>(this, other);

    TRange IRange<T>.MergeWith<TRange>(IRange<T> other) => MergeEngine.Execute<TRange, T>(this, other);
}
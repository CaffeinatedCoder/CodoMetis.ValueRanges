namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Provides abstract static factory methods for constructing range instances.
/// Implement this interface on a concrete range type to gain access to the set operation
/// extension methods (<c>Intersect</c>, <c>Merge</c>, <c>Union</c>, <c>Except</c>).
/// </summary>
/// <typeparam name="TRange">The concrete range type being constructed.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IRangeFactory<TRange, T>
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>
    /// Returns the empty range — a range that contains no values.
    /// </summary>
    abstract static TRange Empty { get; }

    /// <summary>
    /// Creates a range that is unbounded on both sides: <c>(-∞, +∞)</c> — the entire domain.
    /// </summary>
    abstract static TRange Infinite { get; }

    /// <summary>
    /// Creates a range bounded on both sides.
    /// Returns the empty range when <paramref name="start"/> is greater than
    /// <paramref name="end"/>, or when the bounds are equal but not both inclusive.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="startInclusive"><see langword="true"/> to include <paramref name="start"/> in the range.</param>
    /// <param name="endInclusive"><see langword="true"/> to include <paramref name="end"/> in the range.</param>
    abstract static TRange CreateFinite(T start, T end, bool startInclusive, bool endInclusive);

    /// <summary>
    /// Creates a range that is unbounded on the right: <c>[start, +∞)</c> or <c>(start, +∞)</c>.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive"><see langword="true"/> to include <paramref name="start"/> in the range.</param>
    abstract static TRange CreateUnboundedEnd(T start, bool startInclusive);

    /// <summary>
    /// Creates a range that is unbounded on the left: <c>(-∞, end]</c> or <c>(-∞, end)</c>.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive"><see langword="true"/> to include <paramref name="end"/> in the range.</param>
    abstract static TRange CreateUnboundedStart(T end, bool endInclusive);

    /// <summary>
    /// Returns the immediate successor of <paramref name="value"/> in a discrete domain.
    /// The default implementation returns <see langword="null"/>, marking the domain as continuous.
    /// Discrete domains also return <see langword="null"/> when <paramref name="value"/> is the
    /// last representable value.
    /// </summary>
    virtual static T? NextValueAfter(T value) => null;

    /// <summary>
    /// Returns the immediate predecessor of <paramref name="value"/> in a discrete domain.
    /// The default implementation returns <see langword="null"/>, marking the domain as continuous.
    /// </summary>
    virtual static T? PreviousValueBefore(T value) => null;
}
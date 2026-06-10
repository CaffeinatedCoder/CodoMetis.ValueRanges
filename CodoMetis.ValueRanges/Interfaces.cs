namespace CodoMetis.ValueRanges;

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

/// <summary>
/// Provides abstract static factory methods for constructing range instances.
/// Implement this interface on a concrete range type to gain access to the set operation
/// extension methods (<c>Intersect</c>, <c>Merge</c>, <c>Union</c>, <c>Except</c>).
/// </summary>
/// <typeparam name="TRange">The concrete range type being constructed.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IRangeFactory<out TRange, T>
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
    /// Creates a range that is unbounded on the right: <c>[lowerBound, +∞)</c> or <c>(lowerBound, +∞)</c>.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive"><see langword="true"/> to include <paramref name="start"/> in the range.</param>
    abstract static TRange CreateOpenEnd(T start, bool startInclusive);

    /// <summary>
    /// Creates a range that is unbounded on the left: <c>(-∞, upperBound]</c> or <c>(-∞, upperBound)</c>.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive"><see langword="true"/> to include <paramref name="end"/> in the range.</param>
    abstract static TRange CreateOpenStart(T end, bool endInclusive);

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

/// <summary>
/// Represents a range that is bounded on both sides.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IFiniteRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>Gets the lower (left) bound of the range.</summary>
    T Start { get; }

    /// <summary>Gets the upper (right) bound of the range.</summary>
    T End { get; }

    /// <summary>Gets a value indicating whether <see cref="Start"/> is included in the range.</summary>
    bool StartInclusive { get; }

    /// <summary>Gets a value indicating whether <see cref="End"/> is included in the range.</summary>
    bool EndInclusive { get; }

    TRange IRange<T>.IntersectWith<TRange>(IRange<T> other) => IntersectEngine.Execute<TRange, T>(this, other);

    TRange IRange<T>.MergeWith<TRange>(IRange<T> other) => MergeEngine.Execute<TRange, T>(this, other);
}

/// <summary>
/// Represents a range that is unbounded on the right: <c>[LowerBound, +∞)</c> or <c>(LowerBound, +∞)</c>.
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

/// <summary>
/// Marker interface for the empty range — a range that contains no values.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IEmptyRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>;

/// <summary>
/// Marker interface for a range that is unbounded on both sides: <c>(-∞, +∞)</c>.
/// An infinity range contains every value of the element type.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IInfinityRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    TRange IRange<T>.IntersectWith<TRange>(IRange<T> other) => RangeBoundHelpers.RecreateAs<TRange, T>(other);

    TRange IRange<T>.MergeWith<TRange>(IRange<T> other) => TRange.Infinite;
}
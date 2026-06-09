namespace CodoMetis.ValueRanges;

/// <summary>
/// Marker interface for all range types over a comparable, equatable value type.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IRange<out T> where T : struct, IComparable<T>, IEquatable<T>;

/// <summary>
/// Provides abstract static factory methods for constructing range instances.
/// Implement this interface on a concrete range type to gain access to the set operation
/// extension methods (<c>Intersect</c>, <c>Merge</c>, <c>Union</c>, <c>Except</c>).
/// </summary>
/// <typeparam name="TRange">The concrete range type being constructed.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IRangeFactory<out TRange, in T>
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>
    /// Returns the empty range — a range that contains no values.
    /// </summary>
    abstract static TRange Empty { get; }

    /// <summary>
    /// Creates a range bounded on both sides.
    /// Returns the empty range when <paramref name="lowerBound"/> is greater than
    /// <paramref name="upperBound"/>, or when the bounds are equal but not both inclusive.
    /// </summary>
    /// <param name="lowerBound">The lower (left) bound of the range.</param>
    /// <param name="upperBound">The upper (right) bound of the range.</param>
    /// <param name="lowerBoundInclusive"><see langword="true"/> to include <paramref name="lowerBound"/> in the range.</param>
    /// <param name="upperBoundInclusive"><see langword="true"/> to include <paramref name="upperBound"/> in the range.</param>
    abstract static TRange CreateFinite(T lowerBound, T upperBound, bool lowerBoundInclusive, bool upperBoundInclusive);

    /// <summary>
    /// Creates a range that is unbounded on the right: <c>[lowerBound, +∞)</c> or <c>(lowerBound, +∞)</c>.
    /// </summary>
    /// <param name="lowerBound">The lower (left) bound of the range.</param>
    /// <param name="lowerBoundInclusive"><see langword="true"/> to include <paramref name="lowerBound"/> in the range.</param>
    abstract static TRange CreateOpenEnd(T lowerBound, bool lowerBoundInclusive);

    /// <summary>
    /// Creates a range that is unbounded on the left: <c>(-∞, upperBound]</c> or <c>(-∞, upperBound)</c>.
    /// </summary>
    /// <param name="upperBound">The upper (right) bound of the range.</param>
    /// <param name="upperBoundInclusive"><see langword="true"/> to include <paramref name="upperBound"/> in the range.</param>
    abstract static TRange CreateOpenStart(T upperBound, bool upperBoundInclusive);
}

/// <summary>
/// Extends <see cref="IRange{T}"/> for element types with a well-defined successor value.
/// The discrete step is used by adjacency checks: two ranges whose boundaries are exactly
/// one step apart are treated as adjacent.
/// </summary>
/// <typeparam name="T">
/// The discrete element type (e.g. <see cref="int"/>, <see cref="long"/>, <see cref="DateOnly"/>).
/// </typeparam>
public interface IDiscreteRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>
    /// Returns the immediate successor of <paramref name="value"/> in the element type's ordering.
    /// </summary>
    /// <param name="value">The value whose successor is requested.</param>
    /// <returns>The next discrete value after <paramref name="value"/>.</returns>
    T GetNextValueFor(T value);
}

/// <summary>
/// Represents a range that is bounded on both sides.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IFiniteRange<out T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>Gets the lower (left) bound of the range.</summary>
    T LowerBound { get; }

    /// <summary>Gets the upper (right) bound of the range.</summary>
    T UpperBound { get; }

    /// <summary>Gets a value indicating whether <see cref="LowerBound"/> is included in the range.</summary>
    bool LowerBoundInclusive { get; }

    /// <summary>Gets a value indicating whether <see cref="UpperBound"/> is included in the range.</summary>
    bool UpperBoundInclusive { get; }
}

/// <summary>
/// Represents a range that is unbounded on the right: <c>[LowerBound, +∞)</c> or <c>(LowerBound, +∞)</c>.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IOpenEndRange<out T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>Gets the lower (left) bound of the range.</summary>
    T LowerBound { get; }

    /// <summary>Gets a value indicating whether <see cref="LowerBound"/> is included in the range.</summary>
    bool LowerBoundInclusive { get; }
}

/// <summary>
/// Represents a range that is unbounded on the left: <c>(-∞, UpperBound]</c> or <c>(-∞, UpperBound)</c>.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IOpenStartRange<out T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>Gets the upper (right) bound of the range.</summary>
    T UpperBound { get; }

    /// <summary>Gets a value indicating whether <see cref="UpperBound"/> is included in the range.</summary>
    bool UpperBoundInclusive { get; }
}

/// <summary>
/// Marker interface for the empty range — a range that contains no values.
/// </summary>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IEmptyRange<out T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>;

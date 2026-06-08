namespace CodoMetis.ValueRanges;

public interface IRange<out T> where T : struct, IComparable<T>, IEquatable<T>;

public interface IRangeFactory<out TRange, in T>
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    abstract static TRange EmptyRange();

    abstract static TRange Closed(T lowerBound, T upperBound, bool lowerBoundInclusive, bool upperBoundInclusive);

    abstract static TRange WithOpenEnd(T lowerBound, bool lowerBoundInclusive);

    abstract static TRange WithOpenStart(T upperBound, bool upperBoundInclusive);
}

public interface IDiscreteRange<T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    T GetNextValueFor(T value);
}

public interface IFiniteRange<out T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    T    LowerBound          { get; }
    T    UpperBound          { get; }
    bool LowerBoundInclusive { get; }
    bool UpperBoundInclusive { get; }
}

public interface IOpenEndRange<out T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    T    LowerBound          { get; }
    bool LowerBoundInclusive { get; }
}

public interface IOpenStartRange<out T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>
{
    T    UpperBound          { get; }
    bool UpperBoundInclusive { get; }
}

public interface IEmptyRange<out T> : IRange<T> where T : struct, IComparable<T>, IEquatable<T>;
namespace CodoMetis.ValueRanges;

public abstract record Int64Range : IDiscreteRange<long>, IRangeFactory<Int64Range, long>
{
    private Int64Range()
    {
    }

    public sealed record Empty : Int64Range, IEmptyRange<long>;

    public sealed record Finite : Int64Range, IFiniteRange<long>
    {
        internal Finite(long lowerBound, long upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        public long LowerBound          { get; }
        public long UpperBound          { get; }
        public bool LowerBoundInclusive { get; }
        public bool UpperBoundInclusive { get; }
    }

    public sealed record OpenStart(long UpperBound, bool UpperBoundInclusive) : Int64Range, IOpenStartRange<long>;

    public sealed record OpenEnd(long LowerBound, bool LowerBoundInclusive) : Int64Range, IOpenEndRange<long>;

    public static Int64Range WithOpenStart(long upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    public static Int64Range WithOpenEnd(long lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    public static Int64Range EmptyRange() => new Empty();

    public static Int64Range Closed(
        long lowerBound,
        long upperBound,
        bool lowerBoundInclusive = true,
        bool upperBoundInclusive = true
    ) =>
        lowerBound.CompareTo(upperBound) switch
        {
            > 0 => new Empty(),
            0 => lowerBoundInclusive && upperBoundInclusive
                     ? new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
                     : new Empty(),
            _ => new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
        };

    public long GetNextValueFor(long value) => checked(value + 1);
}
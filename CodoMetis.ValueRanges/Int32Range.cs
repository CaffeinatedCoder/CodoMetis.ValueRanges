namespace CodoMetis.ValueRanges;

public abstract record Int32Range : IDiscreteRange<int>, IRangeFactory<Int32Range, int>
{
    private Int32Range()
    {
    }

    public sealed record Empty : Int32Range, IEmptyRange<int>;

    public sealed record Finite : Int32Range, IFiniteRange<int>
    {
        internal Finite(int lowerBound, int upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        public int  LowerBound          { get; }
        public int  UpperBound          { get; }
        public bool LowerBoundInclusive { get; }
        public bool UpperBoundInclusive { get; }
    }

    public sealed record OpenStart(int UpperBound, bool UpperBoundInclusive) : Int32Range, IOpenStartRange<int>;

    public sealed record OpenEnd(int LowerBound, bool LowerBoundInclusive) : Int32Range, IOpenEndRange<int>;

    public static Int32Range WithOpenStart(int upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    public static Int32Range WithOpenEnd(int lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    public static Int32Range EmptyRange() => new Empty();

    public static Int32Range Closed(
        int  lowerBound,
        int  upperBound,
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

    public int GetNextValueFor(int value) => checked(value + 1);
}
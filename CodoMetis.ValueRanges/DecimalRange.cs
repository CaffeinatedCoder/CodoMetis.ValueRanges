namespace CodoMetis.ValueRanges;

public abstract record DecimalRange : IRange<decimal>, IRangeFactory<DecimalRange, decimal>
{
    private DecimalRange()
    {
    }

    public sealed record Empty : DecimalRange, IEmptyRange<decimal>;

    public sealed record Finite : DecimalRange, IFiniteRange<decimal>
    {
        internal Finite(decimal lowerBound, decimal upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        public decimal LowerBound          { get; }
        public decimal UpperBound          { get; }
        public bool    LowerBoundInclusive { get; }
        public bool    UpperBoundInclusive { get; }
    }

    public sealed record OpenStart(decimal UpperBound, bool UpperBoundInclusive) : DecimalRange, IOpenStartRange<decimal>;

    public sealed record OpenEnd(decimal LowerBound, bool LowerBoundInclusive) : DecimalRange, IOpenEndRange<decimal>;

    public static DecimalRange WithOpenStart(decimal upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    public static DecimalRange WithOpenEnd(decimal lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    public static DecimalRange EmptyRange() => new Empty();

    public static DecimalRange Closed(
        decimal lowerBound,
        decimal upperBound,
        bool    lowerBoundInclusive = true,
        bool    upperBoundInclusive = false
    ) =>
        lowerBound.CompareTo(upperBound) switch
        {
            > 0 => new Empty(),
            0 => lowerBoundInclusive && upperBoundInclusive
                     ? new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
                     : new Empty(),
            _ => new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
        };
}
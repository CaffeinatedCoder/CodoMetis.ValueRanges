namespace CodoMetis.ValueRanges;

public abstract record DateTimeRange : IRange<DateTime>, IRangeFactory<DateTimeRange, DateTime>
{
    private DateTimeRange()
    {
    }

    public sealed record Empty : DateTimeRange, IEmptyRange<DateTime>;

    public sealed record Finite : DateTimeRange, IFiniteRange<DateTime>
    {
        internal Finite(DateTime lowerBound, DateTime upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        public DateTime LowerBound          { get; }
        public DateTime UpperBound          { get; }
        public bool     LowerBoundInclusive { get; }
        public bool     UpperBoundInclusive { get; }
    }

    public sealed record OpenStart(DateTime UpperBound, bool UpperBoundInclusive) : DateTimeRange, IOpenStartRange<DateTime>;

    public sealed record OpenEnd(DateTime LowerBound, bool LowerBoundInclusive) : DateTimeRange, IOpenEndRange<DateTime>;

    public static DateTimeRange WithOpenStart(DateTime upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    public static DateTimeRange WithOpenEnd(DateTime lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    public static DateTimeRange EmptyRange() => new Empty();

    public static DateTimeRange Closed(
        DateTime lowerBound,
        DateTime upperBound,
        bool     lowerBoundInclusive = true,
        bool     upperBoundInclusive = false
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
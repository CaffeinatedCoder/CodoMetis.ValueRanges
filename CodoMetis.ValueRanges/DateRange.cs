namespace CodoMetis.ValueRanges;

public abstract record DateRange : IDiscreteRange<DateOnly>, IRangeFactory<DateRange, DateOnly>
{
    private DateRange()
    {
    }

    public sealed record Empty : DateRange, IEmptyRange<DateOnly>;

    public sealed record Finite : DateRange, IFiniteRange<DateOnly>
    {
        internal Finite(DateOnly lowerBound, DateOnly upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        public DateOnly LowerBound          { get; }
        public DateOnly UpperBound          { get; }
        public bool     LowerBoundInclusive { get; }
        public bool     UpperBoundInclusive { get; }
    }

    public sealed record OpenStart(DateOnly UpperBound, bool UpperBoundInclusive) : DateRange, IOpenStartRange<DateOnly>;

    public sealed record OpenEnd(DateOnly LowerBound, bool LowerBoundInclusive) : DateRange, IOpenEndRange<DateOnly>;

    public static DateRange WithOpenStart(DateOnly upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    public static DateRange WithOpenEnd(DateOnly lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    public static DateRange EmptyRange() => new Empty();

    public static DateRange Closed(
        DateOnly lowerBound,
        DateOnly upperBound,
        bool     lowerBoundInclusive = true,
        bool     upperBoundInclusive = true
    ) =>
        lowerBound.CompareTo(upperBound) switch
        {
            > 0 => new Empty(),
            0 => lowerBoundInclusive && upperBoundInclusive
                     ? new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
                     : new Empty(),
            _ => new Finite(lowerBound, upperBound, lowerBoundInclusive, upperBoundInclusive)
        };

    public DateOnly GetNextValueFor(DateOnly value) => value.AddDays(1);
}
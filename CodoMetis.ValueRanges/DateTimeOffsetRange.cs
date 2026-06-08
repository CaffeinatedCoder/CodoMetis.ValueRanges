namespace CodoMetis.ValueRanges;

public abstract record DateTimeOffsetRange : IRange<DateTimeOffset>, IRangeFactory<DateTimeOffsetRange, DateTimeOffset>
{
    private DateTimeOffsetRange()
    {
    }

    public sealed record Empty : DateTimeOffsetRange, IEmptyRange<DateTimeOffset>;

    public sealed record Finite : DateTimeOffsetRange, IFiniteRange<DateTimeOffset>
    {
        internal Finite(DateTimeOffset lowerBound, DateTimeOffset upperBound, bool lowerBoundInclusive, bool upperBoundInclusive)
        {
            LowerBound          = lowerBound;
            UpperBound          = upperBound;
            LowerBoundInclusive = lowerBoundInclusive;
            UpperBoundInclusive = upperBoundInclusive;
        }

        public DateTimeOffset LowerBound          { get; }
        public DateTimeOffset UpperBound          { get; }
        public bool           LowerBoundInclusive { get; }
        public bool           UpperBoundInclusive { get; }
    }

    public sealed record OpenStart(DateTimeOffset UpperBound, bool UpperBoundInclusive)
        : DateTimeOffsetRange, IOpenStartRange<DateTimeOffset>;

    public sealed record OpenEnd(DateTimeOffset LowerBound, bool LowerBoundInclusive)
        : DateTimeOffsetRange, IOpenEndRange<DateTimeOffset>;

    public static DateTimeOffsetRange WithOpenStart(DateTimeOffset upperBound, bool upperBoundInclusive = false)
        => new OpenStart(upperBound, upperBoundInclusive);

    public static DateTimeOffsetRange WithOpenEnd(DateTimeOffset lowerBound, bool lowerBoundInclusive = true)
        => new OpenEnd(lowerBound, lowerBoundInclusive);

    public static DateTimeOffsetRange EmptyRange() => new Empty();

    public static DateTimeOffsetRange Closed(
        DateTimeOffset lowerBound,
        DateTimeOffset upperBound,
        bool           lowerBoundInclusive = true,
        bool           upperBoundInclusive = false
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
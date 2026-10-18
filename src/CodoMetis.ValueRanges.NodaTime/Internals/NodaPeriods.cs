using NodaTime;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// Period arithmetic NodaTime leaves out.
/// </summary>
internal static class NodaPeriods
{
    /// <summary>
    /// <paramref name="period"/> taken <paramref name="times"/> times, unit by unit, so that a split
    /// by months counts each boundary from the range's start: January 31 plus two months is March 31,
    /// where adding one month twice gives March 28. Throws <see cref="OverflowException"/> when a
    /// unit no longer fits.
    /// </summary>
    internal static Period Times(Period period, long times)
        => new PeriodBuilder
        {
            Years        = checked((int)(period.Years * times)),
            Months       = checked((int)(period.Months * times)),
            Weeks        = checked((int)(period.Weeks * times)),
            Days         = checked((int)(period.Days * times)),
            Hours        = checked(period.Hours * times),
            Minutes      = checked(period.Minutes * times),
            Seconds      = checked(period.Seconds * times),
            Milliseconds = checked(period.Milliseconds * times),
            Ticks        = checked(period.Ticks * times),
            Nanoseconds  = checked(period.Nanoseconds * times)
        }.Build();

    /// <summary>
    /// Throws unless <paramref name="period"/> moves a value forward: every unit zero or positive,
    /// and at least one positive. A step of mixed signs, such as one month less a day, is refused
    /// rather than guessed at.
    /// </summary>
    internal static Period RequirePositive(Period period, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(period, parameterName);

        long[] units = [period.Years, period.Months, period.Weeks, period.Days, period.Hours, period.Minutes,
                        period.Seconds, period.Milliseconds, period.Ticks, period.Nanoseconds];

        return units.All(unit => unit >= 0) && units.Any(unit => unit > 0)
                   ? period
                   : throw new ArgumentOutOfRangeException(
                       parameterName, period, "The step must be positive: every unit zero or more, and at least one more than zero.");
    }
}

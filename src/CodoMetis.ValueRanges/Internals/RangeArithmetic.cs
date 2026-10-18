using System.Globalization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// Moves the bounds of a range, for <c>Shift</c> and <c>Expand</c> on each range type.
/// </summary>
/// <remarks>
/// The arithmetic is the element type's own — days, ticks, a NodaTime period — so each type passes
/// the step for its lower and its upper bound, and this rebuilds the range from them. The rebuild
/// goes through the type's factory, so a discrete range canonicalizes and a bound pair that crosses
/// collapses to <c>Empty</c>, exactly as <c>CreateFinite</c> does.
/// </remarks>
internal static class RangeArithmetic
{
    /// <summary>
    /// Rebuilds <paramref name="range"/> with its lower bound moved by <paramref name="lower"/> and
    /// its upper bound by <paramref name="upper"/>, each keeping its inclusiveness. <c>Empty</c> and
    /// <c>Infinity</c> have no bound to move and come back as they are; an unbounded side stays
    /// unbounded.
    /// </summary>
    internal static TRange Move<TRange, T>(TRange range, Func<T, T> lower, Func<T, T> upper)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => range switch
        {
            IEmptyRange<T> or IInfinityRange<T> => range,
            IFiniteRange<T> f => TRange.CreateFinite(lower(f.Start), upper(f.End), f.StartInclusive, f.EndInclusive),
            IUnboundedStartRange<T> s => TRange.CreateUnboundedStart(upper(s.End), s.EndInclusive),
            IUnboundedEndRange<T> e => TRange.CreateUnboundedEnd(lower(e.Start), e.StartInclusive),

            // Reachable only through an external implementation of IRange<T>, which the
            // sealed-variant rule forbids.
            _ => throw ShapePair.Unreachable(nameof(Move), range, range)
        };

    /// <summary>
    /// Runs one step of an element type's arithmetic and reports a result outside the type's values
    /// as <see cref="OverflowException"/>, whichever exception the type itself throws for it — the
    /// BCL date and time types throw <see cref="ArgumentOutOfRangeException"/>, the integers and
    /// <see cref="decimal"/> <see cref="OverflowException"/>.
    /// </summary>
    internal static T Checked<T>(Func<T> step)
    {
        try
        {
            return step();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new OverflowException(
                $"The moved bound is outside the values {typeof(T).Name} can hold.", exception);
        }
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to a time of day without wrapping at midnight, which
    /// <see cref="TimeOnly.Add(TimeSpan)"/> does: a <c>TimeRange</c> cannot cross midnight, so a bound
    /// moved past either end of the day throws instead of reappearing at the other.
    /// </summary>
    internal static TimeOnly AddWithinDay(TimeOnly value, TimeSpan delta)
    {
        long ticks = checked(value.Ticks + delta.Ticks);
        if (ticks < TimeOnly.MinValue.Ticks || ticks > TimeOnly.MaxValue.Ticks)
            throw new OverflowException(
                $"Moving {value.ToString("HH':'mm':'ss.FFFFFFF", CultureInfo.InvariantCulture)} by {delta.ToString("c", CultureInfo.InvariantCulture)} leaves the day; a TimeRange cannot cross midnight.");

        return new TimeOnly(ticks);
    }
}

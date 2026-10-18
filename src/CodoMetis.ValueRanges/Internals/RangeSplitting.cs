using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// Cuts a bounded range into consecutive pieces, for <c>Split</c> and the stepping <c>Values</c> on
/// each range type.
/// </summary>
/// <remarks>
/// <para>
/// A type supplies only where each piece starts — <see cref="Steps{T}"/> for a fixed step, the
/// <c>n</c>-th boundary computed from the range's start rather than from the previous boundary, so a
/// NodaTime period of months does not drift after a short month; <see cref="Periods{TRange,T}"/> for
/// a calendar period such as <c>DateRange.Month</c>. A boundary past the element type's last value
/// is <see langword="null"/>, and the piece before it is the last.
/// </para>
/// <para>
/// Validation is eager and the walk deferred, as in <see cref="DiscreteEnumeration"/>: an unbounded
/// range or a step that does not move forward throws at the call, not at the first
/// <c>MoveNext</c>.
/// </para>
/// </remarks>
internal static class RangeSplitting
{
    /// <summary>
    /// Where piece <paramref name="n"/> + 1 starts, given where piece <paramref name="n"/> starts;
    /// <see langword="null"/> past the element type's last value. Both are passed so that a fixed
    /// step can count from the range's start and a calendar period can follow the previous piece,
    /// and neither keeps state, so the result can be enumerated more than once.
    /// </summary>
    internal delegate T? Boundary<T>(T previous, long n) where T : struct;

    /// <summary>
    /// The pieces of <paramref name="range"/>: each from one boundary up to the next, the first
    /// keeping the range's lower bound and the last its upper bound.
    /// </summary>
    internal static IEnumerable<TRange> Split<TRange, T>(TRange range, Boundary<T> next)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (range is IEmptyRange<T>) return [];

        var finite = Bounded<T>(range, "split");
        return SplitWalk<TRange, T>(finite, FirstBoundary(finite, next), next);
    }

    /// <summary>
    /// The values of a discrete <paramref name="range"/> from its start, one boundary apart.
    /// </summary>
    internal static IEnumerable<T> Values<TRange, T>(TRange range, Boundary<T> next)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        System.Diagnostics.Debug.Assert(TRange.IsDiscrete, $"{typeof(TRange).Name} is not discrete");

        if (range is IEmptyRange<T>) return [];

        var finite = Bounded<T>(range, "enumerate");
        return ValuesWalk(finite, FirstBoundary(finite, next), next);
    }

    /// <summary>
    /// The boundaries of a fixed step: the <c>n</c>-th is <paramref name="at"/>(start, n), and one
    /// the element type cannot hold is <see langword="null"/>.
    /// </summary>
    internal static Boundary<T> Steps<T>(IRange<T> range, Func<T, long, T> at)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var start = range is IFiniteRange<T> finite ? finite.Start : default;

        return (_, n) =>
        {
            try
            {
                return at(start, n);
            }
            catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
            {
                return null;
            }
        };
    }

    /// <summary>
    /// The boundaries of a calendar period: the day after the period that holds the previous
    /// boundary. The period must be a bounded range holding the day it was asked about.
    /// </summary>
    internal static Boundary<T> Periods<TRange, T>(Func<T, TRange> period)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        ArgumentNullException.ThrowIfNull(period);

        return (from, _) =>
        {
            var piece = period(from);
            if (piece is not IFiniteRange<T> finite || !piece.Contains(from))
                throw new ArgumentException(
                    $"The period function returned {piece} for {from}; it must return a bounded range that holds the value it is given.",
                    nameof(period));

            return TRange.NextValueAfter(finite.End);
        };
    }

    private static IFiniteRange<T> Bounded<T>(IRange<T> range, string verb)
        where T : struct, IComparable<T>, IEquatable<T>
        => range as IFiniteRange<T>
        ?? throw new NotSupportedException(
               $"Cannot {verb} an unbounded range ('{range}'): the sequence would not terminate. "
             + "Intersect it with a bounded range first.");

    private static T? FirstBoundary<T>(IFiniteRange<T> range, Boundary<T> next)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var first = next(range.Start, 1);
        if (first is { } boundary && boundary.CompareTo(range.Start) <= 0)
            throw new ArgumentOutOfRangeException(
                "step",
                $"The step must move the start forward, and from {range.Start} it does not: it is zero or less, "
              + "or too small for the element type to tell apart at that size (decimal near its maximum).");

        return first;
    }

    private static IEnumerable<TRange> SplitWalk<TRange, T>(IFiniteRange<T> range, T? to, Boundary<T> next)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        // Where the range stops: the successor of its last value on a discrete domain, its end
        // on a continuous one. A piece whose window reaches that point is the last and takes the
        // range's own end, so a closed continuous range ends [5,10] rather than [5,10), [10,10].
        // A discrete range ending at the domain's maximum stops only when the boundaries run out.
        T? stop = TRange.IsDiscrete ? TRange.NextValueAfter(range.End) : range.End;

        var from          = range.Start;
        var fromInclusive = range.StartInclusive;
        long n            = 1;

        while (true)
        {
            if (to is not { } boundary || (stop is { } end && boundary.CompareTo(end) >= 0))
            {
                yield return TRange.CreateFinite(from, range.End, fromInclusive, range.EndInclusive);
                yield break;
            }

            yield return TRange.CreateFinite(from, boundary, fromInclusive, false);

            (from, fromInclusive) = (boundary, true);
            to = next(from, checked(++n));

            if (to is { } after && after.CompareTo(from) <= 0)
                throw new InvalidOperationException($"The step stopped moving forward at {from}.");
        }
    }

    private static IEnumerable<T> ValuesWalk<T>(IFiniteRange<T> range, T? to, Boundary<T> next)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var current = range.Start;
        long n      = 1;

        while (true)
        {
            yield return current;

            if (to is not { } following || following.CompareTo(range.End) > 0) yield break;

            current = following;
            to      = next(current, checked(++n));
        }
    }
}

using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

using static RangeBoundHelpers;

internal static class ExceptEngine
{
    // Called when range is IInfinityRange<T> — removes a bounded region from the entire domain.
    internal static (TRange Left, TRange? Right) InfinityExcept<TRange, T>(IRange<T> other)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => other switch
           {
               IFiniteRange<T> o => (TRange.CreateUnboundedStart(o.Start, !o.StartInclusive),
                                     (TRange?)TRange.CreateUnboundedEnd(o.End, !o.EndInclusive)),
               IUnboundedStartRange<T> s => (TRange.CreateUnboundedEnd(s.End, !s.EndInclusive), default),
               IUnboundedEndRange<T> e   => (TRange.CreateUnboundedStart(e.Start, !e.StartInclusive), default),
               _                         => (TRange.Infinite, default)
           };

    internal static (TRange Left, TRange? Right) Execute<TRange, T>(IFiniteRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IFiniteRange<T> o         => FiniteExceptFinite<TRange, T>(left, o),
               IUnboundedStartRange<T> s => (TRange.CreateFinite(s.End,      left.End, !s.EndInclusive, left.EndInclusive), default),
               IUnboundedEndRange<T> e   => (TRange.CreateFinite(left.Start, e.Start, left.StartInclusive, !e.StartInclusive), default),
               _                         => (TRange.CreateFinite(left.Start, left.End, left.StartInclusive, left.EndInclusive), default)
           };

    internal static (TRange Left, TRange? Right) Execute<TRange, T>(IUnboundedStartRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IFiniteRange<T> o         => OpenStartExceptFinite<TRange, T>(left, o),
               IUnboundedStartRange<T> o => (TRange.CreateFinite(o.End, left.End, !o.EndInclusive, left.EndInclusive), default),
               _                         => (TRange.CreateUnboundedStart(left.End, left.EndInclusive), default)
           };

    internal static (TRange Left, TRange? Right) Execute<TRange, T>(IUnboundedEndRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IFiniteRange<T> o       => OpenEndExceptFinite<TRange, T>(left, o),
               IUnboundedEndRange<T> o => (TRange.CreateFinite(left.Start, o.Start, left.StartInclusive, !o.StartInclusive), default),
               _                       => (TRange.CreateUnboundedEnd(left.Start, left.StartInclusive), default)
           };

    // Three cases: o sits strictly inside b (split), o covers b's start (left-trim), o covers b's end (right-trim).
    private static (TRange Left, TRange? Right) FiniteExceptFinite<TRange, T>(IFiniteRange<T> b, IFiniteRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        bool oStartInsideB = OuterStartCoversInnerStart(b.Start, b.StartInclusive, o.Start, o.StartInclusive);
        bool oEndInsideB   = OuterEndCoversInnerEnd(b.End, b.EndInclusive, o.End, o.EndInclusive);

        if (oStartInsideB && oEndInsideB)
            return (TRange.CreateFinite(b.Start, o.Start, b.StartInclusive, !o.StartInclusive),
                    (TRange?)TRange.CreateFinite(o.End, b.End, !o.EndInclusive, b.EndInclusive));

        if (OuterStartCoversInnerStart(o.Start, o.StartInclusive, b.Start, b.StartInclusive))
            return (TRange.CreateFinite(o.End, b.End, !o.EndInclusive, b.EndInclusive), default);

        return (TRange.CreateFinite(b.Start, o.Start, b.StartInclusive, !o.StartInclusive), default);
    }

    // o sits strictly inside s (split into UnboundedStart + Finite), or o trims s from the right (new UnboundedStart).
    private static (TRange Left, TRange? Right) OpenStartExceptFinite<TRange, T>(IUnboundedStartRange<T> s, IFiniteRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (OuterEndCoversInnerEnd(s.End, s.EndInclusive, o.End, o.EndInclusive))
            return (TRange.CreateUnboundedStart(o.Start, !o.StartInclusive),
                    (TRange?)TRange.CreateFinite(o.End, s.End, !o.EndInclusive, s.EndInclusive));

        return (TRange.CreateUnboundedStart(o.Start, !o.StartInclusive), default);
    }

    // o sits strictly inside e (split into Finite + UnboundedEnd), or o trims e from the left (new UnboundedEnd).
    private static (TRange Left, TRange? Right) OpenEndExceptFinite<TRange, T>(IUnboundedEndRange<T> e, IFiniteRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (OuterStartCoversInnerStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive))
            return (TRange.CreateFinite(e.Start, o.Start, e.StartInclusive, !o.StartInclusive),
                    (TRange?)TRange.CreateUnboundedEnd(o.End, !o.EndInclusive));

        return (TRange.CreateUnboundedEnd(o.End, !o.EndInclusive), default);
    }
}
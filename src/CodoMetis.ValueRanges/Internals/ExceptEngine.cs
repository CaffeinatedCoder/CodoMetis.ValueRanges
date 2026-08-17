using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

using static RangeBoundHelpers;

internal static class ExceptEngine
{
    // Subtraction is a function of the pair of shapes, so the dispatch is one switch over the
    // pair. Callers guarantee both operands are non-empty and the operand is not Infinity
    // (Except filters an empty operand through its Overlaps guard and a containing one through
    // its Contains guard), which is why those pairs have no arm and throw rather than falling
    // back — see ShapePair.
    internal static (TRange Left, TRange? Right) Execute<TRange, T>(IRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => (left, right) switch
           {
               // Infinity receiver — removes a bounded region from the entire domain.
               (IInfinityRange<T>, IFiniteRange<T> o) => (TRange.CreateUnboundedStart(o.Start, !o.StartInclusive),
                                                          (TRange?)TRange.CreateUnboundedEnd(o.End, !o.EndInclusive)),
               (IInfinityRange<T>, IUnboundedStartRange<T> o) => (TRange.CreateUnboundedEnd(o.End, !o.EndInclusive), default),
               (IInfinityRange<T>, IUnboundedEndRange<T> o)   => (TRange.CreateUnboundedStart(o.Start, !o.StartInclusive), default),

               // Finite receiver.
               (IFiniteRange<T> l, IFiniteRange<T> o)         => FiniteExceptFinite<TRange, T>(l, o),
               (IFiniteRange<T> l, IUnboundedStartRange<T> o) => (TRange.CreateFinite(o.End,   l.End,   !o.EndInclusive,   l.EndInclusive), default),
               (IFiniteRange<T> l, IUnboundedEndRange<T> o)   => (TRange.CreateFinite(l.Start, o.Start, l.StartInclusive, !o.StartInclusive), default),

               // UnboundedStart receiver.
               (IUnboundedStartRange<T> l, IFiniteRange<T> o)         => OpenStartExceptFinite<TRange, T>(l, o),
               (IUnboundedStartRange<T> l, IUnboundedStartRange<T> o) => (TRange.CreateFinite(o.End, l.End, !o.EndInclusive, l.EndInclusive), default),

               // (-∞, l.End] minus [o.Start, +∞): the operand runs to +∞, so it removes everything
               // from its own start upwards and what survives is (-∞, o.Start). Callers guarantee
               // the two overlap, so o.Start is at or below l.End and this bound is the binding one.
               (IUnboundedStartRange<T> _, IUnboundedEndRange<T> o) => (TRange.CreateUnboundedStart(o.Start, !o.StartInclusive), default),

               // UnboundedEnd receiver.
               (IUnboundedEndRange<T> l, IFiniteRange<T> o)       => OpenEndExceptFinite<TRange, T>(l, o),
               (IUnboundedEndRange<T> l, IUnboundedEndRange<T> o) => (TRange.CreateFinite(l.Start, o.Start, l.StartInclusive, !o.StartInclusive), default),

               // The mirror of the case above: [l.Start, +∞) minus (-∞, o.End] leaves (o.End, +∞).
               (IUnboundedEndRange<T> _, IUnboundedStartRange<T> o) => (TRange.CreateUnboundedEnd(o.End, !o.EndInclusive), default),

               _ => throw ShapePair.Unreachable(nameof(ExceptEngine), left, right)
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
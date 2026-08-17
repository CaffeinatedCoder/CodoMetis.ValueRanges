using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

using static RangeBoundHelpers;

internal static class IntersectEngine
{
    // One switch over the shape pair. Callers guarantee the operands overlap (so neither is
    // Empty) and that an Infinity operand was answered before dispatch — RangeExtensions.Intersect
    // returns the other side for it, and IInfinityRange.IntersectWith re-expresses. Those pairs
    // therefore have no arm and throw rather than falling back to Empty; see ShapePair.
    internal static TRange Execute<TRange, T>(IRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => (left, right) switch
           {
               (IFiniteRange<T> l, IFiniteRange<T> o)         => FiniteWithFinite<TRange, T>(l, o),
               (IFiniteRange<T> l, IUnboundedStartRange<T> o) => FiniteWithOpenStart<TRange, T>(l, o),
               (IFiniteRange<T> l, IUnboundedEndRange<T> o)   => FiniteWithOpenEnd<TRange, T>(l, o),

               (IUnboundedStartRange<T> l, IFiniteRange<T> o)         => FiniteWithOpenStart<TRange, T>(o, l),
               (IUnboundedStartRange<T> l, IUnboundedStartRange<T> o) => OpenStartWithOpenStart<TRange, T>(l, o),
               (IUnboundedStartRange<T> l, IUnboundedEndRange<T> o)   => OpenStartWithOpenEnd<TRange, T>(l, o),

               (IUnboundedEndRange<T> l, IFiniteRange<T> o)         => FiniteWithOpenEnd<TRange, T>(o, l),
               (IUnboundedEndRange<T> l, IUnboundedStartRange<T> o) => OpenStartWithOpenEnd<TRange, T>(o, l),
               (IUnboundedEndRange<T> l, IUnboundedEndRange<T> o)   => OpenEndWithOpenEnd<TRange, T>(l, o),

               _ => throw ShapePair.Unreachable(nameof(IntersectEngine), left, right)
           };

    private static TRange FiniteWithFinite<TRange, T>(IFiniteRange<T> b, IFiniteRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = LaterStart(b.Start, b.StartInclusive, o.Start, o.StartInclusive);
        var (uv, ui) = EarlierEnd(b.End, b.EndInclusive, o.End, o.EndInclusive);
        return TRange.CreateFinite(lv, uv, li, ui);
    }

    // Finite ∩ UnboundedStart: the finite lower bound is more restrictive; upper bound is the earlier of the two.
    private static TRange FiniteWithOpenStart<TRange, T>(IFiniteRange<T> b, IUnboundedStartRange<T> s)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (uv, ui) = EarlierEnd(b.End, b.EndInclusive, s.End, s.EndInclusive);
        return TRange.CreateFinite(b.Start, uv, b.StartInclusive, ui);
    }

    // Finite ∩ UnboundedEnd: the finite upper bound is more restrictive; lower bound is the later of the two.
    private static TRange FiniteWithOpenEnd<TRange, T>(IFiniteRange<T> b, IUnboundedEndRange<T> e)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = LaterStart(b.Start, b.StartInclusive, e.Start, e.StartInclusive);
        return TRange.CreateFinite(lv, b.End, li, b.EndInclusive);
    }

    // UnboundedStart ∩ UnboundedStart: result is UnboundedStart at the earlier (more restrictive) upper bound.
    private static TRange OpenStartWithOpenStart<TRange, T>(IUnboundedStartRange<T> s, IUnboundedStartRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (uv, ui) = EarlierEnd(s.End, s.EndInclusive, o.End, o.EndInclusive);
        return TRange.CreateUnboundedStart(uv, ui);
    }

    // UnboundedEnd ∩ UnboundedEnd: result is UnboundedEnd at the later (more restrictive) lower bound.
    private static TRange OpenEndWithOpenEnd<TRange, T>(IUnboundedEndRange<T> e, IUnboundedEndRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = LaterStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive);
        return TRange.CreateUnboundedEnd(lv, li);
    }

    // UnboundedStart ∩ UnboundedEnd: the overlapping region is finite — verified by Overlaps before this call.
    private static TRange OpenStartWithOpenEnd<TRange, T>(IUnboundedStartRange<T> s, IUnboundedEndRange<T> e)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
        => TRange.CreateFinite(e.Start, s.End, e.StartInclusive, s.EndInclusive);
}
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

using static RangeBoundHelpers;

internal static class IntersectEngine
{
    internal static TRange Execute<TRange, T>(IFiniteRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IFiniteRange<T> o         => FiniteWithFinite<TRange, T>(left, o),
               IUnboundedStartRange<T> s => FiniteWithOpenStart<TRange, T>(left, s),
               IUnboundedEndRange<T> e   => FiniteWithOpenEnd<TRange, T>(left, e),
               _                         => TRange.Empty
           };

    internal static TRange Execute<TRange, T>(IUnboundedStartRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IUnboundedStartRange<T> o => OpenStartWithOpenStart<TRange, T>(left, o),
               IFiniteRange<T> f         => FiniteWithOpenStart<TRange, T>(f, left),
               IUnboundedEndRange<T> e   => OpenStartWithOpenEnd<TRange, T>(left, e),
               _                         => TRange.Empty
           };

    internal static TRange Execute<TRange, T>(IUnboundedEndRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IUnboundedEndRange<T> o   => OpenEndWithOpenEnd<TRange, T>(left, o),
               IFiniteRange<T> f         => FiniteWithOpenEnd<TRange, T>(f, left),
               IUnboundedStartRange<T> s => OpenStartWithOpenEnd<TRange, T>(s, left),
               _                         => TRange.Empty
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
        return TRange.CreateOpenStart(uv, ui);
    }

    // UnboundedEnd ∩ UnboundedEnd: result is UnboundedEnd at the later (more restrictive) lower bound.
    private static TRange OpenEndWithOpenEnd<TRange, T>(IUnboundedEndRange<T> e, IUnboundedEndRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = LaterStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive);
        return TRange.CreateOpenEnd(lv, li);
    }

    // UnboundedStart ∩ UnboundedEnd: the overlapping region is finite — verified by Overlaps before this call.
    private static TRange OpenStartWithOpenEnd<TRange, T>(IUnboundedStartRange<T> s, IUnboundedEndRange<T> e)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
        => TRange.CreateFinite(e.Start, s.End, e.StartInclusive, s.EndInclusive);
}
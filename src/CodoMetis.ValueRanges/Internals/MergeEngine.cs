using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

using static RangeBoundHelpers;

internal static class MergeEngine
{
    // One switch over the shape pair. The only callers are RangeSet's greedy merges, which reach
    // this behind `current.Overlaps(next) || current.IsAdjacentTo(next)` — both false for an empty
    // operand — and behind Normalize, which collapses any Infinity input to the Infinite singleton
    // before an element ever reaches here. Those pairs therefore have no arm and throw rather than
    // falling back to Empty, which would have been the wrong answer for both; see ShapePair.
    internal static TRange Execute<TRange, T>(IRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => (left, right) switch
           {
               (IFiniteRange<T> l, IFiniteRange<T> o)         => FiniteWithFinite<TRange, T>(l, o),
               (IFiniteRange<T> l, IUnboundedStartRange<T> o) => OpenStartWithFinite<TRange, T>(o, l),
               (IFiniteRange<T> l, IUnboundedEndRange<T> o)   => OpenEndWithFinite<TRange, T>(o, l),

               (IUnboundedStartRange<T> l, IFiniteRange<T> o)         => OpenStartWithFinite<TRange, T>(l, o),
               (IUnboundedStartRange<T> l, IUnboundedStartRange<T> o) => OpenStartWithOpenStart<TRange, T>(l, o),
               (IUnboundedStartRange<T>, IUnboundedEndRange<T>)       => TRange.Infinite,

               (IUnboundedEndRange<T> l, IFiniteRange<T> o)         => OpenEndWithFinite<TRange, T>(l, o),
               (IUnboundedEndRange<T>, IUnboundedStartRange<T>)     => TRange.Infinite,
               (IUnboundedEndRange<T> l, IUnboundedEndRange<T> o)   => OpenEndWithOpenEnd<TRange, T>(l, o),

               _ => throw ShapePair.Unreachable(nameof(MergeEngine), left, right)
           };

    private static TRange FiniteWithFinite<TRange, T>(IFiniteRange<T> b, IFiniteRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = EarlierStart(b.Start, b.StartInclusive, o.Start, o.StartInclusive);
        var (uv, ui) = LaterEnd(b.End, b.EndInclusive, o.End, o.EndInclusive);
        return TRange.CreateFinite(lv, uv, li, ui);
    }

    // UnboundedStart absorbs any finite lower bound — result is UnboundedStart at the later upper bound.
    private static TRange OpenStartWithFinite<TRange, T>(IUnboundedStartRange<T> s, IFiniteRange<T> b)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (uv, ui) = LaterEnd(s.End, s.EndInclusive, b.End, b.EndInclusive);
        return TRange.CreateUnboundedStart(uv, ui);
    }

    // UnboundedEnd absorbs any finite upper bound — result is UnboundedEnd at the earlier lower bound.
    private static TRange OpenEndWithFinite<TRange, T>(IUnboundedEndRange<T> e, IFiniteRange<T> b)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = EarlierStart(e.Start, e.StartInclusive, b.Start, b.StartInclusive);
        return TRange.CreateUnboundedEnd(lv, li);
    }

    // Two UnboundedStart ranges — result is UnboundedStart at the later upper bound.
    private static TRange OpenStartWithOpenStart<TRange, T>(IUnboundedStartRange<T> s, IUnboundedStartRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (uv, ui) = LaterEnd(s.End, s.EndInclusive, o.End, o.EndInclusive);
        return TRange.CreateUnboundedStart(uv, ui);
    }

    // Two UnboundedEnd ranges — result is UnboundedEnd at the earlier lower bound.
    private static TRange OpenEndWithOpenEnd<TRange, T>(IUnboundedEndRange<T> e, IUnboundedEndRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = EarlierStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive);
        return TRange.CreateUnboundedEnd(lv, li);
    }
}
namespace CodoMetis.ValueRanges;

using static RangeBoundHelpers;

internal static class MergeEngine
{
    internal static TRange Execute<TRange, T>(IFiniteRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IFiniteRange<T> o         => FiniteWithFinite<TRange, T>(left, o),
               IUnboundedStartRange<T> s => OpenStartWithFinite<TRange, T>(s, left),
               IUnboundedEndRange<T> e   => OpenEndWithFinite<TRange, T>(e, left),
               _                         => TRange.Empty
           };

    internal static TRange Execute<TRange, T>(IUnboundedStartRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IFiniteRange<T> f         => OpenStartWithFinite<TRange, T>(left, f),
               IUnboundedStartRange<T> o => OpenStartWithOpenStart<TRange, T>(left, o),
               IUnboundedEndRange<T>     => TRange.Infinite,
               _                         => TRange.Empty
           };

    internal static TRange Execute<TRange, T>(IUnboundedEndRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => right switch
           {
               IFiniteRange<T> f       => OpenEndWithFinite<TRange, T>(left, f),
               IUnboundedStartRange<T> => TRange.Infinite,
               IUnboundedEndRange<T> o => OpenEndWithOpenEnd<TRange, T>(left, o),
               _                       => TRange.Empty
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
        return TRange.CreateOpenStart(uv, ui);
    }

    // UnboundedEnd absorbs any finite upper bound — result is UnboundedEnd at the earlier lower bound.
    private static TRange OpenEndWithFinite<TRange, T>(IUnboundedEndRange<T> e, IFiniteRange<T> b)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = EarlierStart(e.Start, e.StartInclusive, b.Start, b.StartInclusive);
        return TRange.CreateOpenEnd(lv, li);
    }

    // Two UnboundedStart ranges — result is UnboundedStart at the later upper bound.
    private static TRange OpenStartWithOpenStart<TRange, T>(IUnboundedStartRange<T> s, IUnboundedStartRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (uv, ui) = LaterEnd(s.End, s.EndInclusive, o.End, o.EndInclusive);
        return TRange.CreateOpenStart(uv, ui);
    }

    // Two UnboundedEnd ranges — result is UnboundedEnd at the earlier lower bound.
    private static TRange OpenEndWithOpenEnd<TRange, T>(IUnboundedEndRange<T> e, IUnboundedEndRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = EarlierStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive);
        return TRange.CreateOpenEnd(lv, li);
    }
}
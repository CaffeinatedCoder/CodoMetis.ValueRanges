namespace CodoMetis.ValueRanges;

using static RangeBoundHelpers;

internal static class IntersectEngine
{
    internal static TRange Execute<TRange, T>(IFiniteRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    => right switch
    {
        IFiniteRange<T> o    => FiniteWithFinite<TRange, T>(left, o),
        IOpenStartRange<T> s => FiniteWithOpenStart<TRange, T>(left, s),
        IOpenEndRange<T> e   => FiniteWithOpenEnd<TRange, T>(left, e),
        _                    => TRange.Empty
    };

    internal static TRange Execute<TRange, T>(IOpenStartRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    => right switch
    {
        IOpenStartRange<T> o => OpenStartWithOpenStart<TRange, T>(left, o),
        IFiniteRange<T> f    => FiniteWithOpenStart<TRange, T>(f, left),
        IOpenEndRange<T> e   => OpenStartWithOpenEnd<TRange, T>(left, e),
        _                    => TRange.Empty
    };

    internal static TRange Execute<TRange, T>(IOpenEndRange<T> left, IRange<T> right)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    => right switch
    {
        IOpenEndRange<T> o   => OpenEndWithOpenEnd<TRange, T>(left, o),
        IFiniteRange<T> f    => FiniteWithOpenEnd<TRange, T>(f, left),
        IOpenStartRange<T> s => OpenStartWithOpenEnd<TRange, T>(s, left),
        _                    => TRange.Empty
    };

    private static TRange FiniteWithFinite<TRange, T>(IFiniteRange<T> b, IFiniteRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = LaterStart(b.Start, b.StartInclusive, o.Start, o.StartInclusive);
        var (uv, ui) = EarlierEnd(b.End, b.EndInclusive, o.End, o.EndInclusive);
        return TRange.CreateFinite(lv, uv, li, ui);
    }

    // Finite ∩ OpenStart: the finite lower bound is more restrictive; upper bound is the earlier of the two.
    private static TRange FiniteWithOpenStart<TRange, T>(IFiniteRange<T> b, IOpenStartRange<T> s)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (uv, ui) = EarlierEnd(b.End, b.EndInclusive, s.End, s.EndInclusive);
        return TRange.CreateFinite(b.Start, uv, b.StartInclusive, ui);
    }

    // Finite ∩ OpenEnd: the finite upper bound is more restrictive; lower bound is the later of the two.
    private static TRange FiniteWithOpenEnd<TRange, T>(IFiniteRange<T> b, IOpenEndRange<T> e)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = LaterStart(b.Start, b.StartInclusive, e.Start, e.StartInclusive);
        return TRange.CreateFinite(lv, b.End, li, b.EndInclusive);
    }

    // OpenStart ∩ OpenStart: result is OpenStart at the earlier (more restrictive) upper bound.
    private static TRange OpenStartWithOpenStart<TRange, T>(IOpenStartRange<T> s, IOpenStartRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (uv, ui) = EarlierEnd(s.End, s.EndInclusive, o.End, o.EndInclusive);
        return TRange.CreateOpenStart(uv, ui);
    }

    // OpenEnd ∩ OpenEnd: result is OpenEnd at the later (more restrictive) lower bound.
    private static TRange OpenEndWithOpenEnd<TRange, T>(IOpenEndRange<T> e, IOpenEndRange<T> o)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lv, li) = LaterStart(e.Start, e.StartInclusive, o.Start, o.StartInclusive);
        return TRange.CreateOpenEnd(lv, li);
    }

    // OpenStart ∩ OpenEnd: the overlapping region is finite — verified by Overlaps before this call.
    private static TRange OpenStartWithOpenEnd<TRange, T>(IOpenStartRange<T> s, IOpenEndRange<T> e)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
        => TRange.CreateFinite(e.Start, s.End, e.StartInclusive, s.EndInclusive);
}

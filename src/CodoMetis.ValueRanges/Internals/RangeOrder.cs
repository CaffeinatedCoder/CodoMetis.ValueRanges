using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

// PostgreSQL's range order: what `range_cmp` answers, and so what `ORDER BY`, `<`, `<=`, `>` and
// `>=` on a range column answer. The empty range sorts first; every other range sorts by its lower
// bound, then by its upper bound.
//
// Comparing two bounds on the same side follows `range_cmp_bounds`: an absent bound is the
// smallest lower bound and the largest upper bound; at the same value an inclusive lower bound
// starts before an exclusive one (`[5,` before `(5,`), and an exclusive upper bound ends before an
// inclusive one (`,5)` before `,5]`).
//
// This is not `RangeSetHelpers.CompareByLowerBound`, which orders only by the lower bound so that
// `RangeSet.From` can merge neighbours, and has no answer for the empty or the infinite range. This
// one is total and decides on both bounds, so it answers 0 exactly when the two ranges are the same
// variant with the same bounds — which is what record equality answers. The discrete types keep
// their closed canonical form where PostgreSQL keeps `[)`, and the order is the same either way:
// every lower bound is inclusive in both, and `]` at `b` is `)` at `b + 1`.
internal static class RangeOrder
{
    // A null operand sorts before every range, the convention of `Comparer<T>.Default` and
    // `string.CompareTo`.
    internal static int Compare<T>(IRange<T> left, IRange<T>? right)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (right is null) return 1;
        if (ReferenceEquals(left, right)) return 0;

        // Empty first: range_cmp answers on emptiness alone before it reads a bound.
        switch (left is IEmptyRange<T>, right is IEmptyRange<T>)
        {
            case (true, true):  return 0;
            case (true, false): return -1;
            case (false, true): return 1;
        }

        int lower = CompareBounds(Lower(left, left, right), Lower(right, left, right), isLower: true);
        return lower != 0
                   ? lower
                   : CompareBounds(Upper(left, left, right), Upper(right, left, right), isLower: false);
    }

    private readonly record struct Bound<T>(T Value, bool Inclusive, bool Absent)
        where T : struct, IComparable<T>, IEquatable<T>;

    // range_cmp_bounds for two bounds on the same side.
    private static int CompareBounds<T>(Bound<T> a, Bound<T> b, bool isLower)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        // An absent bound is -∞ on the lower side and +∞ on the upper side, and equal to another
        // absent bound on the same side.
        switch (a.Absent, b.Absent)
        {
            case (true, true):  return 0;
            case (true, false): return isLower ? -1 : 1;
            case (false, true): return isLower ? 1 : -1;
        }

        int value = a.Value.CompareTo(b.Value);
        if (value != 0) return value;
        if (a.Inclusive == b.Inclusive) return 0;

        // [5 holds 5 and (5 does not, so [5 starts first; 5] holds 5 and 5) does not, so 5) ends first.
        return a.Inclusive == isLower ? -1 : 1;
    }

    // The pair is passed through only to name it if a shape nobody wrote an arm for turns up.
    private static Bound<T> Lower<T>(IRange<T> range, IRange<T> left, IRange<T> right)
        where T : struct, IComparable<T>, IEquatable<T>
        => range switch
           {
               IFiniteRange<T> f                            => new(f.Start, f.StartInclusive, false),
               IUnboundedEndRange<T> e                      => new(e.Start, e.StartInclusive, false),
               IUnboundedStartRange<T> or IInfinityRange<T> => new(default, false, true),
               _                                            => throw ShapePair.Unreachable(nameof(RangeOrder), left, right)
           };

    private static Bound<T> Upper<T>(IRange<T> range, IRange<T> left, IRange<T> right)
        where T : struct, IComparable<T>, IEquatable<T>
        => range switch
           {
               IFiniteRange<T> f                          => new(f.End, f.EndInclusive, false),
               IUnboundedStartRange<T> s                  => new(s.End, s.EndInclusive, false),
               IUnboundedEndRange<T> or IInfinityRange<T> => new(default, false, true),
               _                                          => throw ShapePair.Unreachable(nameof(RangeOrder), left, right)
           };
}

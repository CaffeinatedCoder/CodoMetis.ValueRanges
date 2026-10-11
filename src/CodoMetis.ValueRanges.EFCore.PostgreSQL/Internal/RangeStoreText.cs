using System.Globalization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The store text of a temporal range or multirange: each bound normalized as Npgsql would send it,
/// the range rebuilt through its factory, and the extremes spelled <c>-infinity</c> / <c>infinity</c>.
/// </summary>
/// <remarks>
/// <para>
/// Rebuilding matters as much as the bounds. A range narrower than a microsecond has two equal bounds
/// once truncated, and the factory collapses <c>[x,x)</c> to <c>empty</c> exactly as PostgreSQL does
/// when the parameter arrives; a multirange is rebuilt through <c>RangeSet.From</c>, which merges the
/// ranges that truncation made touch, as the server merges them. Without that, the document would
/// hold a range the server would never have stored for the same value.
/// </para>
/// <para>
/// Written from the public variant interfaces: the core does not expose its literal writer to this
/// package. The five shapes are named and anything else is refused, as in the core's own formatter.
/// </para>
/// </remarks>
internal static class RangeStoreText
{
    internal static string Format<TRange, T>(TRange range, Func<T, T> normalize, Func<T, string?> infinityText)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => Write(Normalize(range, normalize), infinityText);

    internal static string Format<TRange, T>(RangeSet<TRange, T> set, Func<T, T> normalize, Func<T, string?> infinityText)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var normalized = RangeSet<TRange, T>.From([.. set.Select(range => Normalize(range, normalize))]);
        return "{" + string.Join(",", normalized.Select(range => Write(range, infinityText))) + "}";
    }

    private static TRange Normalize<TRange, T>(TRange range, Func<T, T> normalize)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => range switch
           {
               IEmptyRange<T>             => range,
               IInfinityRange<T>          => range,
               IFiniteRange<T> f          => TRange.CreateFinite(normalize(f.Start), normalize(f.End), f.StartInclusive, f.EndInclusive),
               IUnboundedStartRange<T> us => TRange.CreateUnboundedStart(normalize(us.End), us.EndInclusive),
               IUnboundedEndRange<T> ue   => TRange.CreateUnboundedEnd(normalize(ue.Start), ue.StartInclusive),
               _ => throw new InvalidOperationException($"Unknown range variant: {range.GetType()}.")
           };

    private static string Write<TRange, T>(TRange range, Func<T, string?> infinityText)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        string Bound(T value)
        {
            var text = infinityText(value) ?? TRange.FormatValue(value, null, CultureInfo.InvariantCulture);

            // A temporal bound in its round-trip form never contains a range delimiter, so it is
            // written unquoted. Fail loudly rather than write a literal that reads back differently.
            return text.AsSpan().IndexOfAny("\"\\,()[] ") < 0
                       ? text
                       : throw new InvalidOperationException(
                             $"The {typeof(T).Name} bound text '{text}' of {typeof(TRange).Name} would need quoting in a range literal.");
        }

        return range switch
               {
                   IEmptyRange<T>             => "empty",
                   IInfinityRange<T>          => "(,)",
                   IFiniteRange<T> f          => $"{(f.StartInclusive ? '[' : '(')}{Bound(f.Start)},{Bound(f.End)}{(f.EndInclusive ? ']' : ')')}",
                   IUnboundedStartRange<T> us => $"(,{Bound(us.End)}{(us.EndInclusive ? ']' : ')')}",
                   IUnboundedEndRange<T> ue   => $"{(ue.StartInclusive ? '[' : '(')}{Bound(ue.Start)},)",
                   _ => throw new InvalidOperationException($"Unknown range variant: {range.GetType()}.")
               };
    }
}

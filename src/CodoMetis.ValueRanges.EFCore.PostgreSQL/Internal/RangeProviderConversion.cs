using CodoMetis.ValueRanges.Core;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// Shape-based conversion between the range discriminated unions and the Npgsql provider
/// representations (<see cref="NpgsqlRange{T}"/> for ranges, <see cref="NpgsqlRange{T}"/>
/// arrays for multiranges). Works for any range type purely through the structural variant
/// interfaces — no per-type code.
/// </summary>
internal static class RangeProviderConversion
{
    /// <summary>
    /// Converts a range to its <see cref="NpgsqlRange{T}"/> provider representation.
    /// </summary>
    /// <param name="range">The range to convert.</param>
    /// <param name="normalizeValue">
    /// An optional per-element normalization applied on the way to the database, e.g. forcing
    /// <see cref="DateTimeKind.Unspecified"/> for <c>timestamp</c> or UTC for <c>timestamptz</c>.
    /// </param>
    public static NpgsqlRange<T> ToProvider<T>(IRange<T> range, Func<T, T>? normalizeValue)
        where T : struct, IComparable<T>, IEquatable<T>
    {
        return range switch
               {
                   IEmptyRange<T> => NpgsqlRange<T>.Empty,
                   IInfinityRange<T> => new NpgsqlRange<T>(
                       default, lowerBoundIsInclusive: false, lowerBoundInfinite: true,
                       default, upperBoundIsInclusive: false, upperBoundInfinite: true),
                   IFiniteRange<T> finite => new NpgsqlRange<T>(
                       Normalize(finite.Start), finite.StartInclusive, lowerBoundInfinite: false,
                       Normalize(finite.End), finite.EndInclusive, upperBoundInfinite: false),
                   IUnboundedStartRange<T> unboundedStart => new NpgsqlRange<T>(
                       default, lowerBoundIsInclusive: false, lowerBoundInfinite: true,
                       Normalize(unboundedStart.End), unboundedStart.EndInclusive, upperBoundInfinite: false),
                   IUnboundedEndRange<T> unboundedEnd => new NpgsqlRange<T>(
                       Normalize(unboundedEnd.Start), unboundedEnd.StartInclusive, lowerBoundInfinite: false,
                       default, upperBoundIsInclusive: false, upperBoundInfinite: true),
                   _ => throw new InvalidOperationException($"Unknown range variant: {range.GetType()}.")
               };

        T Normalize(T value) => normalizeValue is null ? value : normalizeValue(value);
    }

    /// <summary>
    /// Converts an <see cref="NpgsqlRange{T}"/> read from the database back to the range type.
    /// Discrete types re-canonicalize to their closed form at construction.
    /// </summary>
    public static TRange FromProvider<TRange, T>(NpgsqlRange<T> value)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (value.IsEmpty) return TRange.Empty;

        return (value.LowerBoundInfinite, value.UpperBoundInfinite) switch
               {
                   (true, true)  => TRange.Infinite,
                   (true, false) => TRange.CreateUnboundedStart(value.UpperBound, value.UpperBoundIsInclusive),
                   (false, true) => TRange.CreateUnboundedEnd(value.LowerBound, value.LowerBoundIsInclusive),
                   (false, false) => TRange.CreateFinite(
                       value.LowerBound, value.UpperBound,
                       value.LowerBoundIsInclusive, value.UpperBoundIsInclusive)
               };
    }

    /// <summary>
    /// Converts a <see cref="RangeSet{TRange,T}"/> to its multirange provider representation —
    /// an array of <see cref="NpgsqlRange{T}"/> elements.
    /// </summary>
    public static NpgsqlRange<T>[] ToProvider<TRange, T>(RangeSet<TRange, T> set, Func<T, T>? normalizeValue)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => set.Select(range => ToProvider(range, normalizeValue)).ToArray();

    /// <summary>
    /// Converts a multirange read from the database back to a normalized
    /// <see cref="RangeSet{TRange,T}"/>.
    /// </summary>
    public static RangeSet<TRange, T> SetFromProvider<TRange, T>(NpgsqlRange<T>[] value)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => RangeSet<TRange, T>.From(value.Select(FromProvider<TRange, T>));
}
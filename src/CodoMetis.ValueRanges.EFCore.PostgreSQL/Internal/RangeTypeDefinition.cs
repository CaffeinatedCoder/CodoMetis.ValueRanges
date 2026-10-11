using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The generic implementation of <see cref="IRangeTypeDefinition"/>: given only the two
/// store type names (and an optional element normalization for the way to the database),
/// it derives all CLR types and builds the range and multirange type mappings.
/// </summary>
/// <typeparam name="TRange">The range type, e.g. <c>DateRange</c>.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
internal sealed class RangeTypeDefinition<TRange, T> : IRangeTypeDefinition
    where TRange : class, IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <param name="rangeStoreType">The range store type, e.g. <c>tsrange</c>.</param>
    /// <param name="multirangeStoreType">The multirange store type, e.g. <c>tsmultirange</c>.</param>
    /// <param name="elementStoreType">The element store type, e.g. <c>timestamp without time zone</c>.</param>
    /// <param name="normalizeValue">
    /// The reinterpretation a bound needs before Npgsql accepts it — <c>DateTimeKind.Unspecified</c>
    /// for <c>timestamp</c>, offset zero for <c>timestamptz</c>.
    /// </param>
    /// <param name="storeValue">
    /// The bound at the store's resolution, applied after <paramref name="normalizeValue"/>: for a
    /// temporal type <see cref="StoreTemporal"/>'s truncation, so a parameter, a JSON document and a
    /// SQL literal all carry the value Npgsql would send. Kept apart from the normalization so that
    /// one stays the element normalization the core type applies.
    /// </param>
    /// <param name="infinityText">
    /// <c>-infinity</c> / <c>infinity</c> for a bound Npgsql maps to it, otherwise <see langword="null"/>.
    /// </param>
    internal RangeTypeDefinition(
        string             rangeStoreType,
        string             multirangeStoreType,
        string             elementStoreType,
        Func<T, T>?        normalizeValue = null,
        Func<T, T>?        storeValue     = null,
        Func<T, string?>?  infinityText   = null)
    {
        RangeStoreType      = rangeStoreType;
        MultirangeStoreType = multirangeStoreType;
        ElementStoreType    = elementStoreType;

        var toStore = (normalizeValue, storeValue) switch
                      {
                          (null, null)   => null,
                          ({ } n, null)  => n,
                          (null, { } v)  => v,
                          ({ } n, { } v) => (Func<T, T>)(bound => v(n(bound)))
                      };

        RangeTypeMapping    = new ValueRangeTypeMapping<TRange, T>(rangeStoreType, toStore, infinityText);
        RangeSetTypeMapping = new ValueRangeSetTypeMapping<TRange, T>(multirangeStoreType, toStore, infinityText);

        // A bound or element operand — range @> x, a constructor argument — has to reach the store as
        // the range's own bounds do: normalized (a non-UTC DateTimeOffset otherwise throws on the way to
        // a timestamptz parameter) and at the store's resolution (a constant was Npgsql's literal).
        // Ranges without either keep the provider's element mapping.
        ElementTypeMapping = toStore is null && infinityText is null
                                 ? null
                                 : new RangeElementTypeMapping<TRange, T>(
                                     elementStoreType, toStore ?? (static value => value), infinityText ?? (static _ => null));
    }

    public Type RangeClrType => typeof(TRange);

    public Type ElementClrType => typeof(T);

    public Type RangeSetClrType => typeof(RangeSet<TRange, T>);

    public string RangeStoreType { get; }

    public string MultirangeStoreType { get; }

    public string ElementStoreType { get; }

    // A domain with a successor function is discrete; continuous types return null.
    public bool IsDiscrete { get; } = TRange.NextValueAfter(default) is not null;

    public RelationalTypeMapping RangeTypeMapping { get; }

    public RelationalTypeMapping RangeSetTypeMapping { get; }

    public RelationalTypeMapping? ElementTypeMapping { get; }

    public object EmptyRange => TRange.Empty;

    public object InfiniteRangeSet => RangeSet<TRange, T>.Infinite;
}
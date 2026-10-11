using System.Data.Common;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// Maps a <see cref="RangeSet{TRange,T}"/> to its PostgreSQL multirange column
/// (e.g. <c>datemultirange</c>), converting through an <see cref="NpgsqlRange{T}"/> array
/// at the provider boundary. Reads re-normalize through <see cref="RangeSet{TRange,T}.From(IEnumerable{TRange})"/>,
/// so the set invariant holds regardless of what the database returns.
/// </summary>
/// <typeparam name="TRange">The range type the set is composed of.</typeparam>
/// <typeparam name="T">The element type of the ranges.</typeparam>
internal sealed class ValueRangeSetTypeMapping<TRange, T> : RelationalTypeMapping, IStoreTextMapping<RangeSet<TRange, T>>
    where TRange : class, IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    private readonly Func<T, T>?       _toStore;
    private readonly Func<T, string?>? _infinityText;

    /// <param name="storeType">The PostgreSQL type, e.g. <c>tsrange</c>.</param>
    /// <param name="toStore">A bound as the store holds it — see <see cref="RangeTypeDefinition{TRange,T}"/>.</param>
    /// <param name="infinityText"><c>-infinity</c> / <c>infinity</c> for a bound Npgsql maps to it.</param>
    internal ValueRangeSetTypeMapping(string storeType, Func<T, T>? toStore, Func<T, string?>? infinityText)
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(RangeSet<TRange, T>),
                       new ValueConverter<RangeSet<TRange, T>, NpgsqlRange<T>[]>(
                           model => RangeProviderConversion.ToProvider(model, toStore),
                           provider => RangeProviderConversion.SetFromProvider<TRange, T>(provider)),
                       new ImmutableValueComparer<RangeSet<TRange, T>>(),
                       new ImmutableValueComparer<RangeSet<TRange, T>>(),
                       jsonValueReaderWriter: LiteralJsonValueReaderWriter<RangeSet<TRange, T>>.Instance),
                   storeType))
    {
        _toStore      = toStore;
        _infinityText = infinityText;
    }

    private ValueRangeSetTypeMapping(RelationalTypeMappingParameters parameters, Func<T, T>? toStore, Func<T, string?>? infinityText)
        : base(parameters)
    {
        _toStore      = toStore;
        _infinityText = infinityText;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new ValueRangeSetTypeMapping<TRange, T>(parameters, _toStore, _infinityText);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        if (npgsqlParameter.Value is RangeSet<TRange, T> model)
            npgsqlParameter.Value = RangeProviderConversion.ToProvider(model, _toStore);

        npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        // RangeSet prints PostgreSQL multirange literals itself, e.g. {[1,3],[5,7]}.
        var set = value switch
                  {
                      RangeSet<TRange, T> model => model,
                      NpgsqlRange<T>[] provider => RangeProviderConversion.SetFromProvider<TRange, T>(provider),
                      _ => throw new InvalidOperationException(
                               $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.")
                  };

        // The store text: the invariant literal, or for a temporal multirange the value as Npgsql
        // would bind it (see ValueRangeTypeMapping).
        return $"'{FormatStoreText(set)}'::{StoreType}";
    }

    /// <summary>
    /// The invariant literal, or for a temporal type the value as Npgsql would bind it: bounds
    /// truncated to the store's microsecond, the extremes as <c>infinity</c>, the range rebuilt so it
    /// collapses or merges as the server would (<see cref="RangeStoreText"/>).
    /// </summary>
    public string FormatStoreText(RangeSet<TRange, T> value)
        => _toStore is null && _infinityText is null
               ? value.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
               : RangeStoreText.Format(value, _toStore ?? (static bound => bound), _infinityText ?? (static _ => null));

    /// <inheritdoc />
    public RangeSet<TRange, T> ParseStoreText(string text)
        => RangeSet<TRange, T>.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}

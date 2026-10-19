using System.Collections;
using System.Data.Common;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// Maps a range type (e.g. <c>DateRange</c>) to its PostgreSQL range column
/// (e.g. <c>daterange</c>), converting through <see cref="NpgsqlRange{T}"/> at the
/// provider boundary.
/// </summary>
/// <typeparam name="TRange">The range type being mapped.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
internal sealed class ValueRangeTypeMapping<TRange, T> : RelationalTypeMapping, IStoreTextMapping<TRange>, INpgsqlRangeTypeMapping
    where TRange : class, IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    private readonly Func<T, T>?       _toStore;
    private readonly Func<T, string?>? _infinityText;

    /// <param name="storeType">The PostgreSQL type, e.g. <c>tsrange</c>.</param>
    /// <param name="toStore">A bound as the store holds it — see <see cref="RangeTypeDefinition{TRange,T}"/>.</param>
    /// <param name="infinityText"><c>-infinity</c> / <c>infinity</c> for a bound Npgsql maps to it.</param>
    internal ValueRangeTypeMapping(string storeType, Func<T, T>? toStore, Func<T, string?>? infinityText)
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(TRange),
                       new ValueConverter<TRange, NpgsqlRange<T>>(
                           model => RangeProviderConversion.ToProvider(model, toStore),
                           provider => RangeProviderConversion.FromProvider<TRange, T>(provider)),
                       new ImmutableValueComparer<TRange>(),
                       new ImmutableValueComparer<TRange>(),
                       jsonValueReaderWriter: LiteralJsonValueReaderWriter<TRange>.Instance),
                   storeType))
    {
        _toStore      = toStore;
        _infinityText = infinityText;
    }

    private ValueRangeTypeMapping(RelationalTypeMappingParameters parameters, Func<T, T>? toStore, Func<T, string?>? infinityText)
        : base(parameters)
    {
        _toStore      = toStore;
        _infinityText = infinityText;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new ValueRangeTypeMapping<TRange, T>(parameters, _toStore, _infinityText);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        // EF normally applies the value converter before the parameter is configured;
        // converting here as well keeps direct usage (e.g. raw SQL) working.
        if (npgsqlParameter.Value is TRange model)
            npgsqlParameter.Value = RangeProviderConversion.ToProvider(model, _toStore);

        npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        // The range types print PostgreSQL range literals themselves.
        var range = value switch
                    {
                        TRange model            => model,
                        NpgsqlRange<T> provider => RangeProviderConversion.FromProvider<TRange, T>(provider),
                        _ => throw new InvalidOperationException(
                                 $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.")
                    };

        // The store text: the invariant literal, or for a temporal range the value as Npgsql would
        // bind it — a literal with seven fraction digits would be rounded by the server where the
        // parameter is truncated, and the constant would not match its own row.
        return $"'{FormatStoreText(range)}'::{StoreType}";
    }

    /// <summary>
    /// The invariant literal, or for a temporal type the value as Npgsql would bind it: bounds
    /// truncated to the store's microsecond, the extremes as <c>infinity</c>, the range rebuilt so it
    /// collapses or merges as the server would (<see cref="RangeStoreText"/>).
    /// </summary>
    public string FormatStoreText(TRange value)
        => _toStore is null && _infinityText is null
               ? value.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
               : RangeStoreText.Format(value, _toStore ?? (static bound => bound), _infinityText ?? (static _ => null));

    /// <inheritdoc />
    public TRange ParseStoreText(string text)
        => TRange.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    // Npgsql 11 accepts a WITHOUT OVERLAPS key or a PERIOD foreign key only over a mapping that implements
    // INpgsqlRangeTypeMapping; it checks the mapping, not the store type name (#150). The comparer it asks for
    // orders key values in the update pipeline. Every range type and RangeSet is already IComparable<T> in
    // PostgreSQL's order, so EF's default applies and none is supplied. Npgsql reads the interface nowhere else.
    // It lives in Npgsql's Storage.Internal.Mapping namespace, so a new Npgsql major may move it;
    // NativeTemporalKeyTests fails if the check stops passing.
    IComparer? INpgsqlRangeTypeMapping.CreateCurrentValueComparer(Type clrType) => null;
}

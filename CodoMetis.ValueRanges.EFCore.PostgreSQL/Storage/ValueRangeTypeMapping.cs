using System.Data.Common;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// Maps a range type (e.g. <c>DateRange</c>) to its PostgreSQL range column
/// (e.g. <c>daterange</c>), converting through <see cref="NpgsqlRange{T}"/> at the
/// provider boundary.
/// </summary>
/// <typeparam name="TRange">The range type being mapped.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
internal sealed class ValueRangeTypeMapping<TRange, T> : RelationalTypeMapping
    where TRange : class, IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    private readonly Func<T, T>? _normalizeValue;

    internal ValueRangeTypeMapping(string storeType, Func<T, T>? normalizeValue)
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(TRange),
                       new ValueConverter<TRange, NpgsqlRange<T>>(
                           model => RangeProviderConversion.ToProvider(model, normalizeValue),
                           provider => RangeProviderConversion.FromProvider<TRange, T>(provider)),
                       new ImmutableValueComparer<TRange>(),
                       new ImmutableValueComparer<TRange>()),
                   storeType))
        => _normalizeValue = normalizeValue;

    private ValueRangeTypeMapping(RelationalTypeMappingParameters parameters, Func<T, T>? normalizeValue)
        : base(parameters)
        => _normalizeValue = normalizeValue;

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new ValueRangeTypeMapping<TRange, T>(parameters, _normalizeValue);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        // EF normally applies the value converter before the parameter is configured;
        // converting here as well keeps direct usage (e.g. raw SQL) working.
        if (npgsqlParameter.Value is TRange model)
            npgsqlParameter.Value = RangeProviderConversion.ToProvider(model, _normalizeValue);

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

        // ToString() formats with the invariant culture; string interpolation would route
        // through IFormattable with the current culture instead.
        return $"'{range.ToString()}'::{StoreType}";
    }
}
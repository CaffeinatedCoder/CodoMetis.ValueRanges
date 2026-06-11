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
/// at the provider boundary. Reads re-normalize through <see cref="RangeSet{TRange,T}.From"/>,
/// so the set invariant holds regardless of what the database returns.
/// </summary>
/// <typeparam name="TRange">The range type the set is composed of.</typeparam>
/// <typeparam name="T">The element type of the ranges.</typeparam>
internal sealed class ValueRangeSetTypeMapping<TRange, T> : RelationalTypeMapping
    where TRange : class, IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    private readonly Func<T, T>? _normalizeValue;

    internal ValueRangeSetTypeMapping(string storeType, Func<T, T>? normalizeValue)
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(RangeSet<TRange, T>),
                       new ValueConverter<RangeSet<TRange, T>, NpgsqlRange<T>[]>(
                           model => RangeProviderConversion.ToProvider(model, normalizeValue),
                           provider => RangeProviderConversion.SetFromProvider<TRange, T>(provider)),
                       new ImmutableValueComparer<RangeSet<TRange, T>>(),
                       new ImmutableValueComparer<RangeSet<TRange, T>>()),
                   storeType))
        => _normalizeValue = normalizeValue;

    private ValueRangeSetTypeMapping(RelationalTypeMappingParameters parameters, Func<T, T>? normalizeValue)
        : base(parameters)
        => _normalizeValue = normalizeValue;

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new ValueRangeSetTypeMapping<TRange, T>(parameters, _normalizeValue);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        if (npgsqlParameter.Value is RangeSet<TRange, T> model)
            npgsqlParameter.Value = RangeProviderConversion.ToProvider(model, _normalizeValue);

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

        // ToString() formats with the invariant culture; string interpolation would route
        // through IFormattable with the current culture instead.
        return $"'{set.ToString()}'::{StoreType}";
    }
}
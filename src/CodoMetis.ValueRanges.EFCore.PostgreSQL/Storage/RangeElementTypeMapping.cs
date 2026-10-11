using System.Data.Common;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// The element mapping of a temporal range type: a bound or element operand — <c>range @&gt; x</c>, a
/// range constructor's argument — bound and rendered as the range's own definition would store it.
/// </summary>
/// <remarks>
/// <para>
/// Without it the operand fell back to the provider's element mapping, which knows nothing of the
/// range's normalization or of the store's resolution. A <c>DateTimeOffset</c> with a non-zero offset
/// therefore threw on the way to a <c>timestamptz</c> parameter ("only offset 0 (UTC) is supported"), a
/// UTC-kinded <c>DateTime</c> on the way to a <c>timestamp</c> one, and a constant was Npgsql's literal —
/// nine fraction digits for NodaTime, which the server rounds, and before 2000 a cut toward 0001 rather
/// than toward the PostgreSQL epoch — so <c>range.Contains(EF.Constant(x))</c> could disagree with the
/// same probe as a parameter.
/// </para>
/// <para>
/// The literal stays self-describing, which the polymorphic range operators require (CLAUDE.md: a
/// constant element operand must be typed): PostgreSQL's typed-literal keyword for the four temporal
/// store types, as Npgsql writes them, and an explicit cast for anything else.
/// </para>
/// </remarks>
/// <typeparam name="TRange">The range type whose element this is — its <c>FormatValue</c> writes the text.</typeparam>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class RangeElementTypeMapping<TRange, T> : RelationalTypeMapping
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    private readonly Func<T, T>       _toStore;
    private readonly Func<T, string?> _infinityText;

    internal RangeElementTypeMapping(string storeType, Func<T, T> toStore, Func<T, string?> infinityText)
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(T),
                       new ValueConverter<T, T>(model => toStore(model), provider => provider)),
                   storeType))
    {
        _toStore      = toStore;
        _infinityText = infinityText;
    }

    private RangeElementTypeMapping(RelationalTypeMappingParameters parameters, Func<T, T> toStore, Func<T, string?> infinityText)
        : base(parameters)
    {
        _toStore      = toStore;
        _infinityText = infinityText;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new RangeElementTypeMapping<TRange, T>(parameters, _toStore, _infinityText);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        // EF normally applies the value converter before the parameter is configured; converting
        // here as well keeps direct usage working, as the range mappings do.
        if (npgsqlParameter.Value is T model) npgsqlParameter.Value = _toStore(model);

        npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        if (value is not T model)
            throw new InvalidOperationException(
                $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.");

        var stored = _toStore(model);
        var text   = _infinityText(stored) ?? TRange.FormatValue(stored, null, CultureInfo.InvariantCulture);

        return StoreType switch
               {
                   "timestamp without time zone" => $"TIMESTAMP '{text}'",
                   "timestamp with time zone"    => $"TIMESTAMPTZ '{text}'",
                   "date"                        => $"DATE '{text}'",
                   "time without time zone"      => $"TIME '{text}'",
                   _                             => $"'{text.Replace("'", "''")}'::{StoreType}"
               };
    }
}

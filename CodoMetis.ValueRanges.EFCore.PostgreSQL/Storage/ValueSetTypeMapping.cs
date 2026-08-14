using System.Data.Common;
using System.Globalization;
using System.Text;
using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// Maps a value set type (e.g. <c>StringSet</c>) to its PostgreSQL array column
/// (e.g. <c>text[]</c>), converting through a primitive array at the provider boundary —
/// Npgsql binds primitive arrays natively, so no driver-level work is involved.
/// </summary>
/// <remarks>
/// The read path routes through <c>TSet.From</c>, so non-canonical rows (unsorted,
/// duplicated) normalize on materialization and <see langword="null"/> elements throw —
/// corrupt data by the type's contract. Canonical form makes plain <see cref="object.Equals(object)"/>
/// a correct and cheap value comparer, so change detection never produces false diffs.
/// </remarks>
/// <typeparam name="TSet">The set type being mapped.</typeparam>
/// <typeparam name="TElement">The element type of the set.</typeparam>
/// <typeparam name="TPrimitive">The primitive store representation of one element.</typeparam>
internal sealed class ValueSetTypeMapping<TSet, TElement, TPrimitive> : RelationalTypeMapping
    where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
    where TElement : IEquatable<TElement>
{
    private readonly Func<TElement, TPrimitive> _toPrimitive;
    private readonly Func<TPrimitive, TElement> _fromPrimitive;

    internal ValueSetTypeMapping(
        string                      storeType,
        Func<TElement, TPrimitive>  toPrimitive,
        Func<TPrimitive, TElement>  fromPrimitive
    )
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(TSet),
                       new ValueConverter<TSet, TPrimitive[]>(
                           model => ToProvider(model, toPrimitive),
                           provider => FromProvider(provider, fromPrimitive)),
                       new ImmutableValueComparer<TSet>(),
                       new ImmutableValueComparer<TSet>()),
                   storeType))
    {
        _toPrimitive   = toPrimitive;
        _fromPrimitive = fromPrimitive;
    }

    private ValueSetTypeMapping(
        RelationalTypeMappingParameters parameters,
        Func<TElement, TPrimitive>      toPrimitive,
        Func<TPrimitive, TElement>      fromPrimitive
    )
        : base(parameters)
    {
        _toPrimitive   = toPrimitive;
        _fromPrimitive = fromPrimitive;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new ValueSetTypeMapping<TSet, TElement, TPrimitive>(parameters, _toPrimitive, _fromPrimitive);

    private static TPrimitive[] ToProvider(TSet set, Func<TElement, TPrimitive> toPrimitive)
    {
        var values = set.Values;
        var result = new TPrimitive[values.Length];
        for (var i = 0; i < values.Length; i++) result[i] = toPrimitive(values[i]);
        return result;
    }

    private static TSet FromProvider(TPrimitive[] array, Func<TPrimitive, TElement> fromPrimitive)
    {
        var elements = new TElement[array.Length];
        for (var i = 0; i < array.Length; i++) elements[i] = fromPrimitive(array[i]);
        return TSet.From(elements);
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is not NpgsqlParameter npgsqlParameter) return;

        // EF normally applies the value converter before the parameter is configured;
        // converting here as well keeps direct usage (e.g. raw SQL) working.
        if (npgsqlParameter.Value is TSet model)
            npgsqlParameter.Value = ToProvider(model, _toPrimitive);

        npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        // Every element renders as a quoted string literal and the whole array is cast to the
        // store type — PostgreSQL casts the string form of any element type ('1'::integer,
        // '2024-01-01'::date), which keeps literal generation uniform and invariant across
        // families. The cast is mandatory anyway: ARRAY[] without one is untyped.
        var sb = new StringBuilder("ARRAY[");

        var first = true;
        foreach (var text in ElementTexts(value))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('\'').Append(text.Replace("'", "''")).Append('\'');
        }

        return sb.Append("]::").Append(StoreType).ToString();
    }

    private IEnumerable<string> ElementTexts(object value)
    {
        switch (value)
        {
            case TSet model:
                // The set's own element formatter produces the per-family canonical text
                // (ISO 8601 for temporal types, invariant numerics, backing text for wrappers).
                foreach (var element in model.Values)
                    yield return TSet.FormatValue(element, null, CultureInfo.InvariantCulture);
                break;

            case TPrimitive[] provider:
                foreach (var primitive in provider)
                    yield return SetProviderText.Of(primitive);
                break;

            default:
                throw new InvalidOperationException(
                    $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.");
        }
    }
}

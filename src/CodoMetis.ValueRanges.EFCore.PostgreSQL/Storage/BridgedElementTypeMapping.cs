using System.Data.Common;
using System.Globalization;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;

/// <summary>
/// A converting element mapping for validated-wrapper set elements: presents the wrapper CLR
/// type to the query pipeline while binding and rendering as the backing primitive's store
/// type, so a bare element parameter in <c>column @&gt; ARRAY[@p]</c> binds as e.g. <c>text</c>.
/// The analogue of the definition-supplied element mapping the range family uses for
/// NodaTime <c>YearMonth</c>.
/// </summary>
/// <typeparam name="TElement">The validated wrapper element type.</typeparam>
/// <typeparam name="TPrimitive">The primitive store representation.</typeparam>
internal sealed class BridgedElementTypeMapping<TElement, TPrimitive> : RelationalTypeMapping
{
    private readonly Func<TElement, TPrimitive> _toPrimitive;
    private readonly Func<TPrimitive, string>   _literalText;

    internal BridgedElementTypeMapping(
        string                     storeType,
        Func<TElement, TPrimitive> toPrimitive,
        Func<TPrimitive, TElement> fromPrimitive,
        Func<TPrimitive, string>?  literalText = null
    )
        : base(new RelationalTypeMappingParameters(
                   new CoreTypeMappingParameters(
                       typeof(TElement),
                       new ValueConverter<TElement, TPrimitive>(
                           model => toPrimitive(model),
                           provider => fromPrimitive(provider))),
                   storeType))
    {
        _toPrimitive = toPrimitive;
        _literalText = literalText ?? SetProviderText.Of;
    }

    private BridgedElementTypeMapping(
        RelationalTypeMappingParameters parameters,
        Func<TElement, TPrimitive>      toPrimitive,
        Func<TPrimitive, string>        literalText
    )
        : base(parameters)
    {
        _toPrimitive = toPrimitive;
        _literalText = literalText;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new BridgedElementTypeMapping<TElement, TPrimitive>(parameters, _toPrimitive, _literalText);

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);

        if (parameter is NpgsqlParameter npgsqlParameter)
            npgsqlParameter.DataTypeName = StoreType;
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var text = value switch
                   {
                       TElement element => _literalText(_toPrimitive(element)),
                       TPrimitive primitive => _literalText(primitive),
                       _ => throw new InvalidOperationException(
                                $"Cannot generate a '{StoreType}' SQL literal for a value of type '{value.GetType()}'.")
                   };

        // No element-level cast: this mapping only renders inside set contexts
        // (ARRAY[...]::type[]), where the array cast types the element already.
        return $"'{text.Replace("'", "''")}'";
    }
}

/// <summary>Invariant text rendering for primitive store values in SQL literals.</summary>
internal static class SetProviderText
{
    /// <summary>
    /// The invariant text form of a primitive. BCL date/time types use the round-trip format —
    /// their null-format <see cref="IFormattable"/> output is the culture "general" form, which
    /// PostgreSQL reads DateStyle-dependently.
    /// </summary>
    internal static string Of<TPrimitive>(TPrimitive primitive)
        => primitive switch
           {
               DateTime value       => value.ToString("O", CultureInfo.InvariantCulture),
               DateTimeOffset value => value.ToString("O", CultureInfo.InvariantCulture),
               DateOnly value       => value.ToString("O", CultureInfo.InvariantCulture),
               TimeOnly value       => value.ToString("O", CultureInfo.InvariantCulture),
               IFormattable value   => value.ToString(null, CultureInfo.InvariantCulture),
               null                 => throw new InvalidOperationException("Value sets cannot contain null elements."),
               _                    => primitive.ToString()!
           };
}

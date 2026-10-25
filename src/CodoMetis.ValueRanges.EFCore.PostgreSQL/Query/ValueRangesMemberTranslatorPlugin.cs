using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Registers the <see cref="ValueRangesMemberTranslator"/> with the query pipeline.
/// </summary>
public sealed class ValueRangesMemberTranslatorPlugin : IMemberTranslatorPlugin
{
    /// <summary>Creates the plugin. Resolved by dependency injection.</summary>
    public ValueRangesMemberTranslatorPlugin(
        ISqlExpressionFactory        sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    ) => Translators = [new ValueRangesMemberTranslator(sqlExpressionFactory, typeMappingSource)];

    /// <inheritdoc />
    public IEnumerable<IMemberTranslator> Translators { get; }
}

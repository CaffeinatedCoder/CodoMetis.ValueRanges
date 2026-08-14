using Microsoft.EntityFrameworkCore.Query;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Registers the <see cref="ValueSetsMemberTranslator"/> with the query pipeline.
/// </summary>
public sealed class ValueSetsMemberTranslatorPlugin : IMemberTranslatorPlugin
{
    /// <summary>Creates the plugin. Resolved by dependency injection.</summary>
    public ValueSetsMemberTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory)
        => Translators = [new ValueSetsMemberTranslator(sqlExpressionFactory)];

    /// <inheritdoc />
    public IEnumerable<IMemberTranslator> Translators { get; }
}

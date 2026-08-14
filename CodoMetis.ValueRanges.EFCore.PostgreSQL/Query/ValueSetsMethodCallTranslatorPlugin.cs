using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Registers the <see cref="ValueSetsMethodCallTranslator"/> with the query pipeline.
/// </summary>
public sealed class ValueSetsMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    /// <summary>
    /// Creates the plugin. Resolved by dependency injection; the expression factory is
    /// guaranteed to be the Npgsql one because this plugin requires the Npgsql provider.
    /// </summary>
    public ValueSetsMethodCallTranslatorPlugin(
        ISqlExpressionFactory        sqlExpressionFactory,
        IRelationalTypeMappingSource typeMappingSource
    ) => Translators = [new ValueSetsMethodCallTranslator((NpgsqlSqlExpressionFactory)sqlExpressionFactory, typeMappingSource)];

    /// <inheritdoc />
    public IEnumerable<IMethodCallTranslator> Translators { get; }
}

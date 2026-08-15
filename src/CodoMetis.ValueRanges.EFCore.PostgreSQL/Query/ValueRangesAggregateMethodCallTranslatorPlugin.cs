using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Registers the <see cref="ValueRangesAggregateMethodCallTranslator"/> with the query
/// pipeline.
/// </summary>
public sealed class ValueRangesAggregateMethodCallTranslatorPlugin : IAggregateMethodCallTranslatorPlugin
{
    /// <summary>
    /// Creates the plugin. Resolved by dependency injection; the expression factory is
    /// guaranteed to be the Npgsql one because this plugin requires the Npgsql provider.
    /// </summary>
    public ValueRangesAggregateMethodCallTranslatorPlugin(ISqlExpressionFactory sqlExpressionFactory)
        => Translators = [new ValueRangesAggregateMethodCallTranslator((NpgsqlSqlExpressionFactory)sqlExpressionFactory)];

    /// <inheritdoc />
    public IEnumerable<IAggregateMethodCallTranslator> Translators { get; }
}

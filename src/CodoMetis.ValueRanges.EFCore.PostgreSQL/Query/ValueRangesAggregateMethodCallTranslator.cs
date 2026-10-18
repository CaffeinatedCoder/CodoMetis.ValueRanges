using System.Reflection;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Translates the <c>RangeAgg</c>/<c>RangeIntersectAgg</c> aggregates to their PostgreSQL
/// counterparts, <c>range_agg</c> (returning a multirange) and <c>range_intersect_agg</c>
/// (returning a range, or a multirange over multiranges) — for <see cref="RangeAggregateExtensions"/> and for any satellite
/// overload class registered via <c>RangeTypeRegistry.RegisterAggregateExtensions</c>.
/// </summary>
internal sealed class ValueRangesAggregateMethodCallTranslator(
    NpgsqlSqlExpressionFactory sqlExpressionFactory
) : IAggregateMethodCallTranslator
{
    /// <inheritdoc />
    public SqlExpression? Translate(
        MethodInfo                                 method,
        EnumerableExpression                       source,
        IReadOnlyList<SqlExpression>               arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (!RangeTypeRegistry.IsAggregateDeclaringType(method.DeclaringType)
            || source.Selector is not SqlExpression selector
            || !RangeTypeRegistry.TryGetByClrType(method.ReturnType, out var definition, out var isSet))
        {
            return null;
        }

        // range_agg and range_intersect_agg are each declared over anyrange and over anymultirange.
        // The method's source type says which one this is; the return type does not, since
        // RangeAgg returns a set either way.
        var overSets = method.GetParameters() is [{ ParameterType: { IsGenericType: true } sourceType }]
                    && sourceType.GetGenericArguments()[0] == definition.RangeSetClrType;

        var argument = sqlExpressionFactory.ApplyTypeMapping(
            selector, overSets ? definition.RangeSetTypeMapping : definition.RangeTypeMapping);

        return method.Name switch
        {
            nameof(RangeAggregateExtensions.RangeAgg) when isSet
                => sqlExpressionFactory.AggregateFunction(
                    "range_agg",
                    [argument],
                    source,
                    nullable: true,
                    argumentsPropagateNullability: [false],
                    definition.RangeSetClrType,
                    definition.RangeSetTypeMapping),

            nameof(RangeAggregateExtensions.RangeIntersectAgg) when !isSet && !overSets
                => sqlExpressionFactory.AggregateFunction(
                    "range_intersect_agg",
                    [argument],
                    source,
                    nullable: true,
                    argumentsPropagateNullability: [false],
                    definition.RangeClrType,
                    definition.RangeTypeMapping),

            nameof(RangeAggregateExtensions.RangeIntersectAgg) when isSet && overSets
                => sqlExpressionFactory.AggregateFunction(
                    "range_intersect_agg",
                    [argument],
                    source,
                    nullable: true,
                    argumentsPropagateNullability: [false],
                    definition.RangeSetClrType,
                    definition.RangeSetTypeMapping),

            _ => null
        };
    }
}

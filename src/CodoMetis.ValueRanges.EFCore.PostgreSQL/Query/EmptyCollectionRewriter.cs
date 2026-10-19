using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Gives the query root EF Core writes for a captured collection that expanded to no rows the
/// typed <c>NULL</c>s the plugin's operators need.
/// </summary>
/// <remarks>
/// <para>
/// A captured collection of value sets, or of ranges, is sent one parameter per element
/// (<see cref="ValueRangesQueryExpressionInterceptor"/>), so <c>Any</c> or <c>Count</c> over it reads
/// the elements from <c>VALUES (@p1), (@p2), …</c>. EF Core expands that parameter into rows in its
/// parameter-based processing, one row per element; for an empty collection that is zero rows. In
/// memory the same <c>Any</c> is <see langword="false"/> and the same <c>Count</c> is <c>0</c>
/// (issue #137).
/// </para>
/// <para>
/// Until 10.0.0 EF Core's SQL generator refused a <c>VALUES</c> with no rows ("Empty collections are
/// not supported as inline query roots"), and this gave the <c>SELECT</c> holding it one row of typed
/// <c>NULL</c>s under <c>WHERE FALSE</c>. EF Core 11 (and an EF Core 10 patch, dotnet/efcore#37216)
/// replaces the empty <c>VALUES</c> itself, in its nullability processing, with
/// <c>(SELECT NULL AS "Value" WHERE FALSE) AS w</c> — but the <c>NULL</c> is bare: Npgsql writes a
/// null constant without its type, PostgreSQL types a bare <c>NULL</c> as <c>text</c>, and
/// <c>w."Value" &amp;&amp; b."Seats"</c> then fails on the server with <c>42883</c>, "operator does
/// not exist: text &amp;&amp; int4range" (#290). So the constants are cast: <c>NULL::int4range</c>,
/// the type EF Core already attached to them from the parameter's element mapping. Every consumer of
/// the subquery then answers for an empty table: <c>EXISTS</c> is false, <c>count(*)</c> is 0,
/// <c>NOT EXISTS</c> (<c>All</c>) is true, an aggregate is <c>NULL</c>.
/// </para>
/// <para>
/// This runs after the provider's processing, from <see cref="ValueRangesParameterBasedSqlProcessorFactory"/>,
/// so it sees the replacement. Only a projection whose constant is a registered range or value set
/// type is cast: a collection of some other type that a context configured for
/// <c>MultipleParameters</c> sends the same way keeps EF Core's own SQL, since the plugin did not put
/// it on this path. Nothing here depends on how the LINQ was written, only on the shape EF Core gives
/// the empty query root, and EF Core has already disabled caching of the generated SQL when it read
/// the collection, so a later execution with elements is generated again.
/// </para>
/// </remarks>
internal static class EmptyCollectionRewriter
{
    /// <summary>Types the <c>NULL</c>s of every empty query root in <paramref name="queryExpression"/>.</summary>
    public static Expression Rewrite(Expression queryExpression, ISqlExpressionFactory sqlExpressionFactory)
        => new Visitor(sqlExpressionFactory).Visit(queryExpression);

    private sealed class Visitor(ISqlExpressionFactory factory) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            // Visit once: visiting the children again for the return doubled the work at every level,
            // so a tree of depth d took 2^d visits, on every query the plugin processes (#249).
            var visited = base.VisitExtension(node);
            if (visited is not SelectExpression select || !IsEmptyRoot(select)) return visited;

            var projections = select.Projection.Select(projection =>
                projection.Expression is SqlConstantExpression { Value: null, TypeMapping: { } mapping } constant && HoldsAPluginType(constant)
                    ? projection.Update(factory.Convert(constant, constant.Type, mapping))
                    : projection).ToList();

            return projections.SequenceEqual(select.Projection)
                ? select
                : select.Update(select.Tables, select.Predicate, select.GroupBy, select.Having, projections, select.Orderings, select.Offset, select.Limit);
        }

        /// <summary>EF Core's replacement for an empty <c>VALUES</c>: no table, <c>WHERE FALSE</c>, null constants projected.</summary>
        private static bool IsEmptyRoot(SelectExpression select)
            => select is { Tables: [], Predicate: SqlConstantExpression { Value: false }, Projection.Count: > 0 }
            && select.Projection.All(projection => projection.Expression is SqlConstantExpression { Value: null });

        /// <summary>Whether the constant's type is a registered range or value set type.</summary>
        private static bool HoldsAPluginType(SqlConstantExpression constant)
            => RangeTypeRegistry.TryGetByClrType(constant.Type, out _, out _) || SetTypeRegistry.TryGetByClrType(constant.Type, out _);
    }
}

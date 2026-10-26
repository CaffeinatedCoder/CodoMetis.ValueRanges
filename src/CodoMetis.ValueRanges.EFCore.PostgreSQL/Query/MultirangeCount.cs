using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
// Counting the ranges needs a subquery over unnest, built from EF Core's immutable SelectExpression
// constructor and Npgsql's PgUnnestExpression, both internal (EF1001): nothing public builds a subquery
// once translation is over. Acknowledged here, at the usage, rather than suppressed repo-wide.
#pragma warning disable EF1001

/// <summary>
/// Builds <c>RangeSet&lt;TRange, T&gt;.Count</c> on the final SQL tree: the number of ranges in a multirange,
/// <c>(SELECT count(*)::integer FROM unnest(x) AS vcN)</c> (#484).
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL has no multirange cardinality function, so the count is a subquery, and a member translator
/// can't build one: it has no alias manager, and EF Core's alias pass, which runs later, reads every alias
/// as one letter and a number. So <see cref="ValueRangesMemberTranslator"/> emits <see cref="Placeholder"/>,
/// and this runs after that pass, from the same decorated processor as <see cref="ValueSetSqlGuard"/>
/// (<see cref="ValueRangesParameterBasedSqlProcessorFactory"/>), as #478 builds its canonical subqueries.
/// The two-letter <c>vcN</c> aliases collide neither with EF Core's nor with the guard's <c>vrN</c>.
/// </para>
/// <para>
/// It fails closed: without the decorated processor the placeholder reaches PostgreSQL, which rejects a
/// function it does not have, rather than a query answering without the count.
/// </para>
/// <para>
/// No canonical-form caveat applies, unlike <c>array_cat</c>: PostgreSQL stores and returns a multirange
/// merged, sorted and without empty ranges, which is the form a loaded <see cref="RangeSet{TRange,T}"/> holds,
/// so the count is of the ranges memory reads. <c>count(*)</c> over <c>unnest(NULL)</c> is <c>0</c>, not
/// NULL; the translator puts the placeholder inside <c>CASE WHEN x IS NULL THEN NULL</c> for that.
/// </para>
/// </remarks>
internal static class MultirangeCount
{
    /// <summary>The function <see cref="ValueRangesMemberTranslator"/> emits for <c>Count</c> until <see cref="Expand"/> builds it.</summary>
    public const string Placeholder = "valueranges_multirange_count_without_sql_guard";

    /// <summary>The query, update or delete with every <see cref="Placeholder"/> built as its subquery.</summary>
    public static Expression Expand(Expression queryExpression, ISqlExpressionFactory sqlExpressionFactory)
        => new Expander(sqlExpressionFactory).Visit(queryExpression);

    private sealed class Expander(ISqlExpressionFactory sqlExpressionFactory) : ExpressionVisitor
    {
        private int _aliases;

        protected override Expression VisitExtension(Expression node)
        {
            if (node is not SqlFunctionExpression { Name: Placeholder, Arguments: [var multirange] } placeholder)
                return base.VisitExtension(node);

            var alias = "vc" + _aliases++;
            var count = sqlExpressionFactory.Function(
                "count", [sqlExpressionFactory.Fragment("*")], nullable: false, argumentsPropagateNullability: [false], typeof(long));
            var select = new SelectExpression(
                alias: null,
                tables: [new PgUnnestExpression(alias, (SqlExpression)Visit(multirange), "r", withOrdinality: false)],
                predicate: null,
                groupBy: [],
                having: null,
                projections: [new ProjectionExpression(sqlExpressionFactory.Convert(count, typeof(int), placeholder.TypeMapping), "")],
                distinct: false,
                orderings: [],
                offset: null,
                limit: null,
                sqlAliasManager: new SqlAliasManager());

            return new ScalarSubqueryExpression(select);
        }
    }
}

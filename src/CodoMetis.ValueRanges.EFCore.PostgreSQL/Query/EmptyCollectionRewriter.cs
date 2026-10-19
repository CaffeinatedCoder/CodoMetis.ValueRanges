using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Gives a <c>VALUES</c> query root that expanded to no rows the SQL that reads no rows, instead of
/// the <c>InvalidOperationException</c> EF Core's SQL generator raises for it.
/// </summary>
/// <remarks>
/// <para>
/// A captured collection of value sets, or of ranges, is sent one parameter per element
/// (<see cref="ValueRangesQueryExpressionInterceptor"/>), so <c>Any</c> or <c>Count</c> over it reads
/// the elements from <c>VALUES (@p1), (@p2), …</c>. EF Core expands that parameter into rows in its
/// parameter-based processing, one row per element; for an empty collection that is zero rows, and
/// its SQL generator refuses to write <c>VALUES</c> with none ("Empty collections are not supported
/// as inline query roots"). In memory the same <c>Any</c> is <see langword="false"/> and the same
/// <c>Count</c> is <c>0</c> (issue #137).
/// </para>
/// <para>
/// This runs after the provider's processing, from <see cref="ValueRangesParameterBasedSqlProcessorFactory"/>,
/// so it sees the expanded tree with the values available. A <c>SELECT</c> whose table list holds a
/// <c>VALUES</c> with no rows gets one row of typed <c>NULL</c>s in its place — so the row has the
/// column type every operator on it needs, <c>(VALUES (NULL::int4range))</c> — and <c>FALSE</c> joined
/// onto its <c>WHERE</c>, so it yields no rows. Every consumer of the subquery then answers for an
/// empty table: <c>EXISTS</c> is false, <c>count(*)</c> is 0, <c>NOT EXISTS</c> (<c>All</c>) is true,
/// an aggregate is <c>NULL</c>. Nothing here depends on how the LINQ was written, only on the shape
/// EF Core gives a parameter query root, and EF Core has already disabled caching of the generated
/// SQL when it read the collection, so a later execution with elements is generated again.
/// </para>
/// <para>
/// The column types come from the columns that reference the <c>VALUES</c> alias elsewhere in the
/// <c>SELECT</c>, which is where EF Core typed them; a column nothing reads gets an untyped
/// <c>NULL</c>, which PostgreSQL accepts for a column it never compares. Only a <c>VALUES</c> whose
/// referenced column is a registered range or value set type is rewritten: a collection of some
/// other type that a context configured for <c>MultipleParameters</c> sends the same way keeps EF
/// Core's own behaviour, since the plugin did not put it on this path.
/// </para>
/// </remarks>
internal static class EmptyCollectionRewriter
{
    /// <summary>Rewrites every empty <c>VALUES</c> query root in <paramref name="queryExpression"/>.</summary>
    public static Expression Rewrite(Expression queryExpression, ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
        => new Visitor(sqlExpressionFactory, typeMappingSource).Visit(queryExpression);

    private sealed class Visitor(ISqlExpressionFactory factory, IRelationalTypeMappingSource typeMappings) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            // Visit once: visiting the children again for the return doubled the work at every level,
            // so a tree of depth d took 2^d visits, on every query the plugin processes (#249).
            var visited = base.VisitExtension(node);
            if (visited is not SelectExpression select) return visited;
            if (!select.Tables.Any(IsEmptyValues)) return select;

            var columns = ColumnCollector.Of(select);
            if (!select.Tables.Any(table => IsEmptyValues(table) && HoldsAPluginType((ValuesExpression)table, columns))) return select;

            var tables  = select.Tables.Select(table => IsEmptyValues(table) ? OneRowOfNulls((ValuesExpression)table, columns) : table).ToList();

            SqlExpression none      = factory.Constant(false, typeMappings.FindMapping(typeof(bool)));
            var           predicate = select.Predicate is { } existing ? factory.AndAlso(none, existing) : none;

            return select.Update(tables, predicate, select.GroupBy, select.Having, select.Projection, select.Orderings, select.Offset, select.Limit);
        }

        private static bool IsEmptyValues(TableExpressionBase table)
            => table is ValuesExpression { RowValues.Count: 0 };

        /// <summary>Whether a column read from the <c>VALUES</c> is a registered range or value set type.</summary>
        private static bool HoldsAPluginType(ValuesExpression values, IReadOnlyList<ColumnExpression> columns)
            => columns.Any(c => c.TableAlias == values.Alias
                             && (RangeTypeRegistry.TryGetByClrType(c.Type, out _, out _) || SetTypeRegistry.TryGetByClrType(c.Type, out _)));

        /// <summary>One row, a typed <c>NULL</c> per column, <c>0</c> for EF Core's ordering column.</summary>
        private ValuesExpression OneRowOfNulls(ValuesExpression values, IReadOnlyList<ColumnExpression> columns)
        {
            var row = values.ColumnNames.Select(name =>
            {
                if (name == "_ord") return factory.Constant(0, typeMappings.FindMapping(typeof(int)));

                var column = columns.FirstOrDefault(c => c.TableAlias == values.Alias && c.Name == name);
                if (column?.TypeMapping is null) return factory.Constant(null, typeof(string), typeMappings.FindMapping(typeof(string)));

                return factory.Convert(factory.Constant(null, column.Type, column.TypeMapping), column.Type, column.TypeMapping);
            }).ToList();

            return values.Update([new RowValueExpression(row)]);
        }
    }

    /// <summary>Every column reference in a <c>SELECT</c>, including those of its subqueries.</summary>
    private sealed class ColumnCollector : ExpressionVisitor
    {
        private readonly List<ColumnExpression> _columns = [];

        public static IReadOnlyList<ColumnExpression> Of(SelectExpression select)
        {
            var collector = new ColumnCollector();
            collector.Visit(select);
            return collector._columns;
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is ColumnExpression column) _columns.Add(column);
            return base.VisitExtension(node);
        }
    }
}

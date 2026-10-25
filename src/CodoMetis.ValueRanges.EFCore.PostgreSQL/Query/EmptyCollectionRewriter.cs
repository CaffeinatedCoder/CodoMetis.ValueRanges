using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Gives the query root EF Core writes for a captured collection that expanded to no rows the
/// typed <c>NULL</c>s the plugin's operators need, in both shapes EF Core 10 gives it.
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
/// On EF Core 10.0.0 and 10.0.1 the empty <c>VALUES</c> reaches this pass, and EF Core's SQL
/// generator would refuse it ("Empty collections are not supported as inline query roots"). A
/// <c>SELECT</c> whose table list holds a <c>VALUES</c> with no rows gets one row of typed
/// <c>NULL</c>s in its place — so the row has the column type every operator on it needs,
/// <c>(VALUES (NULL::int4range))</c> — and <c>FALSE</c> joined onto its <c>WHERE</c>, so it yields no
/// rows. The column types come from the columns that reference the <c>VALUES</c> alias elsewhere in
/// the <c>SELECT</c>, which is where EF Core typed them; a column nothing reads gets an untyped
/// <c>NULL</c>, which PostgreSQL accepts for a column it never compares.
/// </para>
/// <para>
/// EF Core 10.0.2 and later (dotnet/efcore#37216, backported as dotnet/efcore#37248; off switch
/// <c>Microsoft.EntityFrameworkCore.Issue37216</c>) replace the empty <c>VALUES</c> themselves, in
/// their nullability processing, with <c>(SELECT NULL AS "Value" WHERE FALSE) AS w</c> — so this pass
/// sees no empty <c>VALUES</c>. The <c>NULL</c> is bare: Npgsql writes a null constant without its
/// type, PostgreSQL types a bare <c>NULL</c> as <c>text</c>, and <c>w."Value" &amp;&amp; b."Seats"</c>
/// then fails on the server with <c>42883</c>, "operator does not exist: text &amp;&amp; int4range"
/// (#302; the same on the 11 line, #290). So the constants are cast: <c>NULL::int4range</c>, the type
/// EF Core already attached to them from the parameter's element mapping. EF Core types its <c>_ord</c>
/// ordering column the same way, so that one becomes <c>0</c> instead, the integer it is when the list
/// has elements and on 10.0.0 and 10.0.1 (#319).
/// </para>
/// <para>
/// Either way every consumer of the subquery then answers for an empty table: <c>EXISTS</c> is
/// false, <c>count(*)</c> is 0, <c>NOT EXISTS</c> (<c>All</c>) is true, an aggregate is <c>NULL</c>.
/// This runs after the provider's processing, from <see cref="ValueRangesParameterBasedSqlProcessorFactory"/>,
/// so it sees the expanded tree with the values available, or EF Core's replacement. Only a
/// <c>VALUES</c> whose referenced column, or a projection whose constant, is a registered range or
/// value set type is rewritten: a collection of some other type that a context configured for
/// <c>MultipleParameters</c> sends the same way keeps EF Core's own behaviour, since the plugin did
/// not put it on this path. Nothing here depends on how the LINQ was written, only on the shape EF
/// Core gives the empty query root, and EF Core has already disabled caching of the generated SQL
/// when it read the collection, so a later execution with elements is generated again.
/// </para>
/// </remarks>
internal static class EmptyCollectionRewriter
{
    /// <summary>Rewrites every empty query root in <paramref name="queryExpression"/>.</summary>
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

            if (select.Tables.Any(IsEmptyValues)) return RewriteEmptyValues(select);
            if (IsEmptyRoot(select)) return TypeTheNulls(select);

            return select;
        }

        /// <summary>EF Core 10.0.0 and 10.0.1: the empty <c>VALUES</c> itself, which the SQL generator refuses.</summary>
        private SelectExpression RewriteEmptyValues(SelectExpression select)
        {
            var columns = ColumnCollector.Of(select);
            if (!select.Tables.Any(table => IsEmptyValues(table) && HoldsAPluginType((ValuesExpression)table, columns))) return select;

            var tables  = select.Tables.Select(table => IsEmptyValues(table) ? OneRowOfNulls((ValuesExpression)table, columns) : table).ToList();

            SqlExpression none      = factory.Constant(false, typeMappings.FindMapping(typeof(bool)));
            var           predicate = select.Predicate is { } existing ? factory.AndAlso(none, existing) : none;

            return select.Update(tables, predicate, select.GroupBy, select.Having, select.Projection, select.Orderings, select.Offset, select.Limit);
        }

        /// <summary>EF Core 10.0.2 and later: EF Core's replacement, whose bare <c>NULL</c>s PostgreSQL types as <c>text</c>.</summary>
        private SelectExpression TypeTheNulls(SelectExpression select)
        {
            var projections = select.Projection.Select(projection =>
                projection.Expression is SqlConstantExpression { Value: null, TypeMapping: { } mapping } constant && HoldsAPluginType(constant)
                    // EF Core types its ordering column like the elements; it is an integer when the list has any (#319).
                    ? projection.Update(projection.Alias == "_ord"
                        ? factory.ApplyDefaultTypeMapping(factory.Constant(0))
                        : factory.Convert(constant, constant.Type, mapping))
                    : projection).ToList();

            return projections.SequenceEqual(select.Projection)
                ? select
                : select.Update(select.Tables, select.Predicate, select.GroupBy, select.Having, projections, select.Orderings, select.Offset, select.Limit);
        }

        private static bool IsEmptyValues(TableExpressionBase table)
            => table is ValuesExpression { RowValues.Count: 0 };

        /// <summary>EF Core's replacement for an empty <c>VALUES</c>: no table, <c>WHERE FALSE</c>, null constants projected.</summary>
        private static bool IsEmptyRoot(SelectExpression select)
            => select is { Tables: [], Predicate: SqlConstantExpression { Value: false }, Projection.Count: > 0 }
            && select.Projection.All(projection => projection.Expression is SqlConstantExpression { Value: null });

        /// <summary>Whether a column read from the <c>VALUES</c> is a registered range or value set type.</summary>
        private static bool HoldsAPluginType(ValuesExpression values, IReadOnlyList<ColumnExpression> columns)
            => columns.Any(c => c.TableAlias == values.Alias && HoldsAPluginType(c.Type));

        /// <summary>Whether the constant's type is a registered range or value set type.</summary>
        private static bool HoldsAPluginType(SqlConstantExpression constant)
            => HoldsAPluginType(constant.Type);

        private static bool HoldsAPluginType(Type type)
            => RangeTypeRegistry.TryGetByClrType(type, out _, out _) || SetTypeRegistry.TryGetByClrType(type, out _);

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

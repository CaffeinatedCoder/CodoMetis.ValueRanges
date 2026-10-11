using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
// PgBinaryExpression is Npgsql-internal (EF1001): it is the node Npgsql builds for @>, <@ and &&,
// the only operators a server-computed Union may feed, and nothing public identifies them in the
// SQL tree. Acknowledged here, at the usage, rather than suppressed repo-wide.
#pragma warning disable EF1001

/// <summary>
/// Refuses, on the final SQL tree, the two value set shapes that translate without error and then
/// answer — or write — something different from what the LINQ said.
/// </summary>
/// <remarks>
/// <para>
/// Runs from <see cref="ValueRangesParameterBasedSqlProcessorFactory"/>, after the provider's own
/// parameter-based processing and immediately before SQL generation, for queries and for
/// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> alike. Working on the SQL tree rather than the LINQ is
/// what makes both checks independent of how the query was written: by then a projection, a
/// conditional, a coalesce, a join or <c>EF.Property</c> have all been reduced to the same few
/// nodes.
/// </para>
/// <para>
/// <b>A server-computed <c>Union</c> is not canonical.</b> It translates to <c>array_cat</c>,
/// which concatenates: the result carries duplicates and keeps each operand's order. An
/// expression <em>derived</em> from one — <c>array_cat</c> itself, <c>array_remove</c> over one
/// (which preserves canonical form rather than establishing it), a <c>CASE</c> or
/// <c>COALESCE</c> that may yield one, a subquery column projecting one — may only be
/// <list type="bullet">
///   <item>an operand of <c>@&gt;</c>, <c>&lt;@</c> or <c>&amp;&amp;</c>, which ignore order and
///   multiplicity (this includes the <c>&lt;@ AND NOT @&gt;</c> proper-containment form);</item>
///   <item>the input of <c>array_remove</c>, <c>array_cat</c>, <c>CASE</c> or <c>COALESCE</c>,
///   whose result is then derived in turn;</item>
///   <item>tested with <c>IS NULL</c>, or by <c>cardinality(…) = 0</c> — <c>IsEmpty</c>: a
///   concatenation is empty exactly when both operands are;</item>
///   <item>a column of the outermost <c>SELECT</c>, which is materialized, and materialization
///   canonicalizes through the set's <c>From</c>.</item>
/// </list>
/// Everything else is refused: equality, <c>cardinality</c>, <c>GROUP BY</c>, <c>DISTINCT</c>,
/// <c>ORDER BY</c>, a deduplicating set operation, <c>IN</c>, and an <c>UPDATE</c> setter — which
/// would store the concatenation, after which server-side equality and <c>Count</c> on that row are
/// wrong for good. PostgreSQL has no array-distinct to canonicalize into, and sorting in SQL would
/// order <c>text</c> by the database collation rather than ordinally, which is the same defect one
/// layer down.
/// </para>
/// <para>
/// <b>A value set inside a JSON column must not be written by <c>to_jsonb</c>.</b> Npgsql
/// translates an <c>ExecuteUpdate</c> setter targeting a scalar inside a JSON document to
/// <c>jsonb_set(doc, path, to_jsonb(value))</c>. For a value set, <c>value</c> is the PostgreSQL
/// array, so the document receives a JSON array where it holds the set's literal string: the
/// update reports success and the row no longer loads. Detecting it here, by what will be
/// written rather than by how the setter lambda named the property, covers every route to the
/// property — a join, <c>EF.Property</c>, a nested complex type.
/// </para>
/// <para>
/// <b>The guard fails closed.</b> It runs from a decorated service, and a later
/// <c>ReplaceService</c> — or another extension replacing rather than decorating the same service —
/// would drop it without a trace, after which every refused shape answers wrongly again. So
/// <see cref="ValueSetsMethodCallTranslator"/> does not emit <c>array_cat</c>: it emits
/// <see cref="UnguardedUnion"/>, a function PostgreSQL does not have, and only <see cref="Check"/>
/// renames it. Without the guard a query over a server-side <c>Union</c> fails on the server, naming
/// the placeholder; a decorator <em>around</em> the guard still reaches it and changes nothing.
/// </para>
/// </remarks>
internal static class ValueSetSqlGuard
{
    /// <summary>
    /// The function name a translated <c>Union</c> carries until <see cref="Check"/> renames it to
    /// <c>array_cat</c>. It reaches PostgreSQL — as <c>function … does not exist</c> — only when the
    /// guard did not run, which is the point.
    /// </summary>
    public const string UnguardedUnion = "valueranges_union_without_sql_guard";

    /// <summary>
    /// Checks a query, update or delete expression and throws when it holds a refused shape; returns it
    /// with every <see cref="UnguardedUnion"/> renamed to <c>array_cat</c>.
    /// </summary>
    public static Expression Check(Expression queryExpression)
    {
        new Walker().Root(queryExpression);
        return UnionNamer.Instance.Visit(queryExpression);
    }

    /// <summary>
    /// Whether an expression may evaluate to a server-computed union, judged from the expression
    /// alone. <see cref="ValueSetsMemberTranslator"/> asks this to decline <c>Count</c>, so that EF
    /// falls back to client evaluation in a projection; it sees no enclosing query, so a column
    /// projected by a subquery is left to <see cref="Check"/>, which follows it.
    /// </summary>
    public static bool MayBeConcatenation(SqlExpression? expression) => expression switch
    {
        SqlFunctionExpression { Name: UnguardedUnion } when IsSet(expression)                      => true,
        SqlFunctionExpression { Name: "array_remove", Arguments: [var input, _] } when IsSet(expression) => MayBeConcatenation(input),
        SqlFunctionExpression { Name: "COALESCE", Arguments: { } arguments }                       => arguments.Any(MayBeConcatenation),
        CaseExpression conditional => conditional.WhenClauses.Any(c => MayBeConcatenation(c.Result)) || MayBeConcatenation(conditional.ElseResult),
        SqlUnaryExpression { OperatorType: ExpressionType.Convert } convert when IsSet(convert)    => MayBeConcatenation(convert.Operand),
        _                                                                                          => false
    };

    private static bool IsSet(SqlExpression expression) => SetTypeRegistry.TryGetByClrType(expression.Type, out _);

    /// <summary>The operations a derived value is refused under, each with its own message.</summary>
    private enum Use { Other, Equality, Count, GroupBy, Distinct, OrderBy, SetOperation }

    private sealed class Walker
    {
        /// <summary>Subquery and set-operation columns that project a derived value, by table alias and column name.</summary>
        private readonly HashSet<(string Table, string Column)> _derivedColumns = [];

        public void Root(Expression expression)
        {
            switch (expression)
            {
                // The outermost projection is read by the shaper, through the set's From, so a derived
                // value is canonical again by the time anyone sees it. A subquery's projection is only
                // as safe as whatever consumes its columns, which Derived(ColumnExpression) decides.
                case SelectExpression select:
                    Select(select);
                    break;

                case UpdateExpression update:
                    Select(update.SelectExpression);
                    foreach (var setter in update.ColumnValueSetters)
                    {
                        JsonSetter(setter.Column, setter.Value, path: null);
                        if (Derived(setter.Value)) throw Refusal.Setter(setter.Column.Name);
                    }

                    break;

                case DeleteExpression delete:
                    Select(delete.SelectExpression);
                    break;

                default:
                    Consume(expression);
                    break;
            }
        }

        /// <summary>Walks a SELECT and reports, per projection, whether it projects a derived value.</summary>
        private bool[] Select(SelectExpression select)
        {
            foreach (var table in select.Tables) Table(table);

            var derived = new bool[select.Projection.Count];
            for (var i = 0; i < derived.Length; i++)
            {
                derived[i] = Derived(select.Projection[i].Expression);
                if (derived[i] && select.IsDistinct) throw Refusal.For(Use.Distinct);
            }

            Consume(select.Predicate);
            foreach (var grouping in select.GroupBy) Consume(grouping, Use.GroupBy);
            Consume(select.Having);
            foreach (var ordering in select.Orderings) Consume(ordering.Expression, Use.OrderBy);
            Consume(select.Offset);
            Consume(select.Limit);

            return derived;
        }

        private void Table(TableExpressionBase table)
        {
            switch (table)
            {
                case SelectExpression subquery:
                    Register(subquery.Alias, subquery, Select(subquery));
                    break;

                case SetOperationBase setOperation:
                    var either = Select(setOperation.Source1).Zip(Select(setOperation.Source2), (a, b) => a || b).ToArray();
                    if (setOperation.IsDistinct && either.Contains(true)) throw Refusal.For(Use.SetOperation);

                    Register(setOperation.Alias, setOperation.Source1, either);
                    break;

                case PredicateJoinExpressionBase join:
                    Table(join.Table);
                    Consume(join.JoinPredicate);
                    break;

                case JoinExpressionBase join:
                    Table(join.Table);
                    break;

                case TableExpression or FromSqlExpression:
                    break;

                default:
                    // Table-valued functions (unnest, …), VALUES and provider-specific sources: nothing
                    // derived may flow into them.
                    foreach (var child in ChildCollector.Of(table)) Consume(child);
                    break;
            }
        }

        private void Register(string? alias, SelectExpression source, bool[] derived)
        {
            if (alias is null) return;
            for (var i = 0; i < derived.Length; i++)
                if (derived[i]) _derivedColumns.Add((alias, source.Projection[i].Alias));
        }

        /// <summary>Refuses a derived value in a position that does not tolerate one.</summary>
        private void Consume(Expression? expression, Use use = Use.Other)
        {
            switch (expression)
            {
                case null:
                    return;

                case SqlExpression sql:
                    if (Derived(sql)) throw Refusal.For(use);
                    return;

                case SelectExpression select:
                    if (Select(select).Contains(true)) throw Refusal.For(use);
                    return;

                default:
                    if (expression.NodeType == ExpressionType.Extension)
                        foreach (var child in ChildCollector.Of(expression)) Consume(child, use);
                    return;
            }
        }

        /// <summary>
        /// Whether the expression's value may be a concatenation. Throws when one is consumed inside it
        /// by an operator that is sensitive to order or multiplicity.
        /// </summary>
        private bool Derived(SqlExpression? expression)
        {
            switch (expression)
            {
                case null:
                    return false;

                case ColumnExpression column:
                    return _derivedColumns.Contains((column.TableAlias, column.Name));

                case SqlFunctionExpression { Name: UnguardedUnion, Arguments: { } arguments } when IsSet(expression):
                    foreach (var argument in arguments) Derived(argument);
                    return true;

                // array_remove preserves canonical form rather than establishing it.
                case SqlFunctionExpression { Name: "array_remove", Arguments: [var input, var element] } when IsSet(expression):
                    Consume(element);
                    return Derived(input);

                case SqlFunctionExpression { Name: "COALESCE", Arguments: { } arguments }:
                    return arguments.Aggregate(false, (any, argument) => Derived(argument) | any);

                case CaseExpression conditional:
                    Consume(conditional.Operand);
                    var anyResult = false;
                    foreach (var clause in conditional.WhenClauses)
                    {
                        Consume(clause.Test);
                        anyResult |= Derived(clause.Result);
                    }

                    return Derived(conditional.ElseResult) | anyResult;

                case SqlUnaryExpression { OperatorType: ExpressionType.Convert } convert when IsSet(convert):
                    return Derived(convert.Operand);

                // IS NULL / IS NOT NULL: a concatenation is NULL exactly when an operand is.
                case SqlUnaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } nullTest:
                    Derived(nullTest.Operand);
                    return false;

                // IsEmpty, cardinality(x) = 0: a concatenation is empty exactly when both operands are.
                case SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } comparison
                    when CardinalityComparedWithZero(comparison) is { } counted:
                    Derived(counted);
                    return false;

                case SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } equality:
                    Consume(equality.Left, Use.Equality);
                    Consume(equality.Right, Use.Equality);
                    return false;

                case SqlFunctionExpression { Name: "cardinality", Arguments: [var counted] }:
                    Consume(counted, Use.Count);
                    return false;

                // @>, <@ and && ignore order and multiplicity: the reason array_cat is a valid
                // translation of Union at all.
                case PgBinaryExpression { OperatorType: PgExpressionType.Contains or PgExpressionType.ContainedBy or PgExpressionType.Overlaps } containment:
                    Derived(containment.Left);
                    Derived(containment.Right);
                    return false;

                case ScalarSubqueryExpression scalar:
                    return Select(scalar.Subquery) is [true, ..];

                case ExistsExpression exists:
                    Select(exists.Subquery);
                    return false;

                case InExpression membership:
                    Consume(membership.Item, Use.Equality);
                    Consume(membership.Subquery, Use.Equality);
                    foreach (var value in membership.Values ?? []) Consume(value, Use.Equality);
                    return false;

                default:
                    foreach (var child in ChildCollector.Of(expression)) Consume(child);
                    return false;
            }
        }

        private static SqlExpression? CardinalityComparedWithZero(SqlBinaryExpression comparison)
            => (comparison.Left, comparison.Right) switch
               {
                   (SqlFunctionExpression { Name: "cardinality", Arguments: [var operand] }, SqlConstantExpression { Value: 0 }) => operand,
                   (SqlConstantExpression { Value: 0 }, SqlFunctionExpression { Name: "cardinality", Arguments: [var operand] }) => operand,
                   _                                                                                                          => null
               };

        /// <summary>
        /// Refuses <c>to_jsonb(set)</c> (or <c>to_json</c>) inside an <c>UPDATE</c> setter, naming the
        /// JSON path from the enclosing <c>jsonb_set</c> where there is one.
        /// </summary>
        private static void JsonSetter(ColumnExpression column, SqlExpression? value, string? path)
        {
            switch (value)
            {
                case null:
                    break;

                case SqlFunctionExpression { Name: "jsonb_set" or "json_set", Arguments: [var document, SqlConstantExpression { Value: var segments }, var written, ..] }:
                    JsonSetter(column, document, path);
                    JsonSetter(column, written, segments is IEnumerable<PathSegment> named ? string.Join('.', named) : path);
                    break;

                case SqlFunctionExpression { Name: "to_jsonb" or "to_json", Arguments: [var serialized] } when IsSet(Unwrap(serialized)):
                    throw Refusal.JsonSetter(column.Name, path);

                default:
                    foreach (var child in ChildCollector.Of(value))
                        if (child is SqlExpression sql) JsonSetter(column, sql, path);
                    break;
            }
        }

        private static SqlExpression Unwrap(SqlExpression expression)
            => expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } convert ? Unwrap(convert.Operand) : expression;
    }

    /// <summary>Gives every checked <see cref="UnguardedUnion"/> the name PostgreSQL knows.</summary>
    private sealed class UnionNamer : ExpressionVisitor
    {
        public static readonly UnionNamer Instance = new();

        protected override Expression VisitExtension(Expression node)
        {
            var visited = base.VisitExtension(node);
            return visited is SqlFunctionExpression { Name: UnguardedUnion, Arguments: { } arguments } union
                ? new SqlFunctionExpression(
                    "array_cat", arguments, union.IsNullable, union.ArgumentsPropagateNullability!, union.Type, union.TypeMapping)
                : visited;
        }
    }

    /// <summary>Collects an extension node's direct children without descending into them.</summary>
    private sealed class ChildCollector : ExpressionVisitor
    {
        private readonly List<Expression> _children = [];

        public static List<Expression> Of(Expression node)
        {
            var collector = new ChildCollector();
            collector.Collect(node);
            return collector._children;
        }

        private void Collect(Expression node) => base.VisitExtension(node);

        public override Expression? Visit(Expression? node)
        {
            if (node is not null) _children.Add(node);
            return node;
        }
    }

    private static class Refusal
    {
        private const string Remedy =
            "Compose a server-side Union only with Contains, Overlaps, IsSubsetOf/IsSupersetOf, "
          + "IsProperSubsetOf/IsProperSupersetOf, Remove or IsEmpty, which ignore order and multiplicity, or materialize "
          + "it first (project it, or switch to AsEnumerable()) and work with the value.";

        public static InvalidOperationException For(Use use) => use switch
        {
            Use.Equality => Create(
                "Value set equality ('==', '!=', 'Equals', or Contains over a sequence of sets)", "array equality",
                "Compare with IsSubsetOf/IsSupersetOf, which mean the same thing for canonical sets and ignore order and "
              + "multiplicity (a.IsSubsetOf(b) && a.IsSupersetOf(b) is set equality), or materialize the union first and "
              + "compare the value."),
            Use.Count        => Create("'Count'", "cardinality", Remedy),
            Use.GroupBy      => Create("Grouping by it ('GroupBy')", "GROUP BY", Remedy),
            Use.Distinct     => Create("'Distinct' over it", "DISTINCT", Remedy),
            Use.OrderBy      => Create("Ordering by it ('OrderBy')", "ORDER BY", Remedy),
            Use.SetOperation => Create("A deduplicating set operation over it ('Union', 'Intersect' or 'Except' of two queries)",
                                       "the set operation's row comparison", Remedy),
            _                => Create("This use of it", "the operation", Remedy)
        };

        private static InvalidOperationException Create(string subject, string operation, string remedy)
            => new($"{subject} cannot be translated over a value set Union computed on the server. Union translates to "
                 + "array_cat, which concatenates rather than canonicalizes: the server-side result carries duplicates and "
                 + $"keeps each operand's order, and {operation} is sensitive to both, so the query would answer differently "
                 + $"from the same expression in memory. {remedy}");

        public static InvalidOperationException Setter(string column) => new(
            $"'ExecuteUpdate' cannot set the value set column '{column}' to a value set Union computed on the server. "
          + "Union translates to array_cat, which concatenates rather than canonicalizes, so the row would store "
          + "duplicates in operand order, and from then on server-side equality and Count on that row are wrong. "
          + "Compute the union in memory and set the value, or load the entities and save them with SaveChanges.");

        public static InvalidOperationException JsonSetter(string column, string? path) => new(
            $"'ExecuteUpdate' cannot set the value set '{column}{(path is null ? "" : "." + path)}' inside a JSON column. "
          + "EF Core writes it with PostgreSQL's to_jsonb, which stores a JSON array where the document holds the "
          + "set's literal string, so the row would no longer load and queries on the property would fail. "
          + $"Load the entity and save it with SaveChanges, or set the whole JSON property (SetProperty(e => e.{column}, …)) "
          + "or a whole complex property that contains the set.");
    }
}

using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Infrastructure;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
// PgBinaryExpression is Npgsql-internal (EF1001): it is the node Npgsql builds for @>, <@ and &&,
// the only operators a server-computed Union may feed, and nothing public identifies them in the
// SQL tree. Building canonical form needs EF Core's immutable SelectExpression constructor and
// Npgsql's PgUnnestExpression and PgNewArrayExpression, also internal: nothing public builds a
// subquery once translation is over. Acknowledged here, at the usage, rather than suppressed
// repo-wide.
#pragma warning disable EF1001

/// <summary>
/// Refuses, on the final SQL tree, the two value set shapes that translate without error and then
/// answer — or write — something different from what the LINQ said, and builds the canonical form
/// the other set operations need there (#478).
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
/// <b>A server-computed text set operation is not canonical.</b> PostgreSQL sorts <c>text</c> by the
/// database collation rather than ordinally (#479), so <c>Union</c>, <c>Add</c>, <c>Intersect</c> and
/// <c>Except</c> on text arrive as <see cref="UnguardedUnion"/>, <see cref="UnguardedAdd"/>,
/// <see cref="UnguardedIntersect"/> and <see cref="UnguardedExcept"/>, and the guard builds each unsorted:
/// <c>array_cat</c>, which also repeats an element both operands hold, <c>array_cat</c> with a one-element
/// array, or a filter of the first array. An expression <em>derived</em> from one — the operation itself,
/// <c>array_remove</c> over one (which preserves canonical form rather than establishing it), a
/// <c>CASE</c>, <c>COALESCE</c> or <c>NULLIF</c> that may yield one, a subquery column projecting one — may
/// only be
/// <list type="bullet">
///   <item>an operand of <c>@&gt;</c>, <c>&lt;@</c> or <c>&amp;&amp;</c>, which ignore order and
///   multiplicity (this includes the <c>&lt;@ AND NOT @&gt;</c> proper-containment form);</item>
///   <item>the input of an operation, <c>array_remove</c>, <c>CASE</c> or <c>COALESCE</c>,
///   whose result is then derived in turn;</item>
///   <item>tested with <c>IS NULL</c>, or by <c>cardinality(…) = 0</c> — <c>IsEmpty</c>: a
///   concatenation is empty exactly when both operands are;</item>
///   <item>counted, which the expander writes as <c>count(DISTINCT …)</c> over its elements, NULL for
///   a NULL set (#485);</item>
///   <item>compared with <c>=</c>, <c>&lt;&gt;</c> or <c>NULLIF</c>, which the expander writes as mutual
///   containment, <c>a @&gt; b AND b @&gt; a</c> (#485). A string set column compares bytes (#483), so
///   neither needs a collation, which would cost a GIN index its use;</item>
///   <item>a column of the outermost <c>SELECT</c>, which is materialized, and materialization
///   canonicalizes through the set's <c>From</c>.</item>
/// </list>
/// Everything else is refused: <c>GROUP BY</c>, <c>DISTINCT</c>, <c>ORDER BY</c>, a deduplicating set
/// operation, <c>IN</c>, and an <c>UPDATE</c> setter — which would store the unsorted array, after which
/// <c>=</c> against that row is wrong for good. Sorting in SQL would order <c>text</c> by the database
/// collation rather than ordinally, which is the same defect one layer down (#479).
/// </para>
/// <para>
/// <b>Every other family gets canonical form instead.</b> Where PostgreSQL sorts the elements as
/// memory does (<see cref="ISetTypeDefinition.CanonicalizesInSql"/>), <c>Union</c>, <c>Add</c>,
/// <c>Intersect</c> and <c>Except</c> arrive as <see cref="CanonicalUnion"/>, <see cref="CanonicalAdd"/>,
/// <see cref="CanonicalIntersect"/> and <see cref="CanonicalExcept"/>. They are canonical by
/// construction, so the walker lets them reach any use, and <see cref="CanonicalExpander"/> builds
/// each: <c>ARRAY((SELECT DISTINCT ON (vrN.x) vrN.x FROM unnest(…) WITH ORDINALITY AS vrN(x) WHERE
/// vrN.x IS NOT NULL … ORDER BY vrN.x, vrN.ordinality))</c>. <c>ORDER BY x, ordinality</c> keeps the
/// first copy of each value, as <c>ValueSetCore</c> does: <c>DecimalSet</c> keeps whichever of
/// <c>1.0</c> and <c>1.00</c> came first, and the float sets whichever of <c>-0</c> and <c>0</c>. A
/// NULL element another tool stored is dropped, as a set never holds one. Where the consumer ignores
/// order and duplicates, the expander writes the cheaper form instead (see its remarks).
/// </para>
/// <para>
/// <b>A value set inside a JSON column must not be written by <c>to_jsonb</c>.</b> Npgsql
/// translates an <c>ExecuteUpdate</c> setter targeting a scalar inside a JSON document to
/// <c>jsonb_set(doc, path, to_jsonb(value))</c> — <c>jsonb_set_lax</c> from Npgsql EF 10.0.3 on
/// PostgreSQL 16 and later when the value may be null. For a value set, <c>value</c> is the PostgreSQL
/// array, so the document receives a JSON array where it holds the set's literal string: the update
/// reports success and the row no longer loads. A <c>null</c> value set is refused too, with its own
/// message: it arrives as <c>to_jsonb(NULL)</c>, which PostgreSQL cannot type (<c>42804</c>). Detecting
/// it here, by what will be written rather than by how the setter lambda named the property, covers
/// every route to the property — a join, <c>EF.Property</c>, a nested complex type.
/// </para>
/// <para>
/// <b>The guard fails closed.</b> It runs from a decorated service, and a later
/// <c>ReplaceService</c> — or another extension replacing rather than decorating the same service —
/// would drop it without a trace, after which every refused shape answers wrongly again. So
/// <see cref="ValueSetsMethodCallTranslator"/> does not emit <c>array_cat</c>: it emits
/// an unguarded or a canonical placeholder, functions PostgreSQL does not have, and only
/// <see cref="Check"/> replaces them. Without the guard a query over a server-side set operation
/// fails on the server, naming the placeholder; a decorator <em>around</em> the guard still reaches
/// it and changes nothing.
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

    /// <summary>The placeholder a text <c>Add(a, element)</c> carries until <see cref="Check"/> builds its unsorted form (#485).</summary>
    public const string UnguardedAdd = "valueranges_add_without_sql_guard";

    /// <summary>The placeholder a text <c>Intersect(a, b)</c> carries until <see cref="Check"/> builds its unsorted form (#485).</summary>
    public const string UnguardedIntersect = "valueranges_intersect_without_sql_guard";

    /// <summary>The placeholder a text <c>Except(a, b)</c> carries until <see cref="Check"/> builds its unsorted form (#485).</summary>
    public const string UnguardedExcept = "valueranges_except_without_sql_guard";

    private static bool IsUnguarded(string? name)
        => name is UnguardedUnion or UnguardedAdd or UnguardedIntersect or UnguardedExcept;

    /// <summary>The placeholder a canonical <c>Union(a, b)</c> carries until <see cref="Check"/> builds it.</summary>
    public const string CanonicalUnion = "valueranges_canonical_union_without_sql_guard";

    /// <summary>The placeholder a canonical <c>Add(a, element)</c> carries until <see cref="Check"/> builds it.</summary>
    public const string CanonicalAdd = "valueranges_canonical_add_without_sql_guard";

    /// <summary>The placeholder a canonical <c>Intersect(a, b)</c> carries until <see cref="Check"/> builds it.</summary>
    public const string CanonicalIntersect = "valueranges_canonical_intersect_without_sql_guard";

    /// <summary>The placeholder a canonical <c>Except(a, b)</c> carries until <see cref="Check"/> builds it.</summary>
    public const string CanonicalExcept = "valueranges_canonical_except_without_sql_guard";

    private static bool IsCanonical(string? name)
        => name is CanonicalUnion or CanonicalAdd or CanonicalIntersect or CanonicalExcept;

    /// <summary>
    /// Checks a query, update or delete expression and throws when it holds a refused shape; returns it
    /// with every unguarded placeholder built in its unsorted form, every canonical placeholder built, and
    /// <c>Count</c> and equality over an unsorted text set written so they don't depend on its order.
    /// </summary>
    public static Expression Check(Expression queryExpression, IRelationalTypeMappingSource typeMappingSource)
    {
        var walker = new Walker();
        walker.Root(queryExpression);
        return new CanonicalExpander(typeMappingSource, walker.DerivedColumns).Root(queryExpression);
    }

    private static bool IsSet(SqlExpression expression) => SetTypeRegistry.TryGetByClrType(expression.Type, out _);

    private static SqlExpression? CardinalityComparedWithZero(SqlBinaryExpression comparison)
        => (comparison.Left, comparison.Right) switch
           {
               (SqlFunctionExpression { Name: "cardinality", Arguments: [var operand] }, SqlConstantExpression { Value: 0 }) => operand,
               (SqlConstantExpression { Value: 0 }, SqlFunctionExpression { Name: "cardinality", Arguments: [var operand] }) => operand,
               _                                                                                                          => null
           };

    /// <summary>The operations a derived value is refused under, each with its own message.</summary>
    private enum Use { Other, Membership, GroupBy, Distinct, OrderBy, SetOperation }

    private sealed class Walker
    {
        /// <summary>Subquery and set-operation columns that project a derived value, by table alias and column name.</summary>
        private readonly HashSet<(string Table, string Column)> _derivedColumns = [];

        /// <summary>The columns found to project a derived value, for <see cref="CanonicalExpander"/>.</summary>
        public IReadOnlySet<(string Table, string Column)> DerivedColumns => _derivedColumns;

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

                // A text Union, Add, Intersect or Except: unsorted, and a Union or Add may repeat an element.
                // Its operands need not be sorted either, since the result isn't.
                case SqlFunctionExpression { Name: var unguarded, Arguments: { } arguments } when IsUnguarded(unguarded) && IsSet(expression):
                    foreach (var argument in arguments) Derived(argument);
                    return true;

                // array_remove preserves canonical form rather than establishing it.
                case SqlFunctionExpression { Name: "array_remove", Arguments: [var input, var element] } when IsSet(expression):
                    Consume(element);
                    return Derived(input);

                // A canonical placeholder establishes canonical form, so a derived operand inside it is
                // harmless and its result is not derived.
                case SqlFunctionExpression { Name: var name, Arguments: { } arguments } when IsCanonical(name):
                    foreach (var argument in arguments) Derived(argument);
                    return false;

                case SqlFunctionExpression { Name: "COALESCE", Arguments: { } arguments }:
                    return arguments.Aggregate(false, (any, argument) => Derived(argument) | any);

                // EF Core writes `a == b ? null : a` as NULLIF(a, b). Over a derived set the expander
                // compares by mutual containment, as it does ==, and the result is derived when `a` is.
                case SqlFunctionExpression { Name: "NULLIF", Arguments: [var kept, var compared] } when IsSet(kept):
                    Derived(compared);
                    return Derived(kept);

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

                // EF.Functions.Collate over a string set: the operators would compare its elements by that
                // collation, where memory compares ordinally (#483).
                case CollateExpression { Operand: var collated, Collation: var collation }
                    when StringSetCollation.IsStringSet(collated.Type) && !StringSetCollation.ComparesBytes(collation):
                    throw Refusal.Collation(collation);

                // IS NULL / IS NOT NULL: a concatenation is NULL exactly when an operand is.
                case SqlUnaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } nullTest:
                    Derived(nullTest.Operand);
                    return false;

                // IsEmpty, cardinality(x) = 0: a concatenation is empty exactly when both operands are.
                case SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } comparison
                    when CardinalityComparedWithZero(comparison) is { } counted:
                    Derived(counted);
                    return false;

                // == and != over a derived set: the expander writes them as mutual containment (#485).
                case SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } equality:
                    Derived(equality.Left);
                    Derived(equality.Right);
                    return false;

                // Count over a derived set: the expander counts its distinct elements (#485).
                case SqlFunctionExpression { Name: "cardinality", Arguments: [var counted] }:
                    Derived(counted);
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
                    Consume(membership.Item, Use.Membership);
                    Consume(membership.Subquery, Use.Membership);
                    foreach (var value in membership.Values ?? []) Consume(value, Use.Membership);
                    return false;

                default:
                    foreach (var child in ChildCollector.Of(expression)) Consume(child);
                    return false;
            }
        }

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

                // Npgsql EF 10.0.3 writes jsonb_set_lax on PostgreSQL 16 and later when the value may be
                // null (npgsql/efcore.pg#3855).
                case SqlFunctionExpression { Name: "jsonb_set" or "jsonb_set_lax" or "json_set", Arguments: [var document, SqlConstantExpression { Value: var segments }, var written, ..] }:
                    JsonSetter(column, document, path);
                    JsonSetter(column, written, segments is IEnumerable<PathSegment> named ? string.Join('.', named) : path);
                    break;

                // A null value set — a constant or a null parameter, which the provider's processing has
                // inlined as NULL by now — gets its own message: it is refused, but not for the JSON array.
                case SqlFunctionExpression { Name: "to_jsonb" or "to_json", Arguments: [var serialized] } when IsSet(Unwrap(serialized)):
                    throw Unwrap(serialized) is SqlConstantExpression { Value: null }
                        ? Refusal.NullJsonSetter(column.Name, path)
                        : Refusal.JsonSetter(column.Name, path);

                default:
                    foreach (var child in ChildCollector.Of(value))
                        if (child is SqlExpression sql) JsonSetter(column, sql, path);
                    break;
            }
        }

        private static SqlExpression Unwrap(SqlExpression expression)
            => expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } convert ? Unwrap(convert.Operand) : expression;
    }

    /// <summary>
    /// Builds every canonical placeholder: in canonical form where the consumer can see order or
    /// duplicates, and in the cheaper multiset form where it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The multiset form is <c>array_cat(a, b)</c> for <c>Union</c> and <c>Add</c>, and the filter without
    /// <c>DISTINCT ON</c> and <c>ORDER BY</c> for <c>Intersect</c> and <c>Except</c>. It is written for an
    /// operand of <c>@&gt;</c>, <c>&lt;@</c> and <c>&amp;&amp;</c>, under <c>cardinality(…) = 0</c> and a null
    /// test, for a column of the outermost <c>SELECT</c> unless that <c>SELECT</c> groups or deduplicates
    /// (the read canonicalizes), and for the operands of a placeholder, which the form built around them
    /// canonicalizes. A <c>CASE</c>, a <c>COALESCE</c> and <c>array_remove</c> pass it on to what they may
    /// return. The canonical subquery costs about 1.5 µs a row over ten elements, roughly fifteen times a
    /// bare <c>array_cat</c> (PostgreSQL 16, #478), which is what the multiset form saves.
    /// </para>
    /// <para>
    /// It runs after EF Core's alias pass, which reads every alias as one letter and a number, so the
    /// two-letter <c>vrN</c> aliases it chooses can neither break that pass nor collide with an alias EF
    /// Core generated. Neither EF Core nor Npgsql has a node for <c>DISTINCT ON</c>, so that projection is
    /// a <see cref="SqlFragmentExpression"/> naming its alias, which is final by now. Npgsql's <c>ANY</c>
    /// node insists on an enumerable CLR type, which a value set deliberately is not, so the filters test
    /// containment, <c>b @&gt; ARRAY[x]</c>, which also answers <c>false</c> rather than NULL against a
    /// NULL element of <c>b</c>.
    /// </para>
    /// </remarks>
    private sealed class CanonicalExpander(
        IRelationalTypeMappingSource typeMappingSource, IReadOnlySet<(string Table, string Column)> derivedColumns) : ExpressionVisitor
    {
        private readonly RelationalTypeMapping? _boolMapping = typeMappingSource.FindMapping(typeof(bool));

        private int _aliases;

        /// <summary>Visits a whole query, update or delete.</summary>
        public Expression Root(Expression expression)
        {
            // The outermost SELECT's columns are read, and reading canonicalizes, unless the SELECT
            // compares them first.
            if (expression is not SelectExpression { IsDistinct: false, GroupBy.Count: 0 } select)
                return Visit(expression);

            return select.Update(
                [.. select.Tables.Select(table => (TableExpressionBase)Visit(table))],
                (SqlExpression?)Visit(select.Predicate),
                select.GroupBy,
                (SqlExpression?)Visit(select.Having),
                [.. select.Projection.Select(projection => projection.Update(Multiset(projection.Expression)))],
                [.. select.Orderings.Select(ordering => (OrderingExpression)Visit(ordering))],
                (SqlExpression?)Visit(select.Offset),
                (SqlExpression?)Visit(select.Limit));
        }

        protected override Expression VisitExtension(Expression node)
        {
            switch (node)
            {
                case PgBinaryExpression { OperatorType: PgExpressionType.Contains or PgExpressionType.ContainedBy or PgExpressionType.Overlaps } containment:
                    return containment.Update(Multiset(containment.Left), Multiset(containment.Right));

                // IsEmpty: a multiset is empty exactly when its canonical form is.
                case SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } comparison
                    when CardinalityComparedWithZero(comparison) is { } counted:
                    return comparison.Update(Counted(comparison.Left, counted), Counted(comparison.Right, counted));

                // IS NULL, IS NOT NULL: a multiset is NULL exactly when its canonical form is.
                case SqlUnaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } nullTest:
                    return nullTest.Update(Multiset(nullTest.Operand));

                // == and != with an unsorted text set on a side: two sets are equal when each contains the
                // other, whatever their order and repeats (#485). Two stored columns keep = and its index.
                case SqlBinaryExpression { OperatorType: ExpressionType.Equal or ExpressionType.NotEqual } equality
                    when (IsSet(equality.Left) || IsSet(equality.Right)) && (IsDerived(equality.Left) || IsDerived(equality.Right)):
                    var same = MutualContainment(equality.Left, equality.Right);
                    return equality.OperatorType == ExpressionType.Equal
                        ? same
                        : new SqlUnaryExpression(ExpressionType.Not, same, typeof(bool), _boolMapping);

                // NULLIF(a, b), EF Core 11's `a == b ? null : a`, compares the same way.
                case SqlFunctionExpression { Name: "NULLIF", Arguments: [var kept, var compared] } nullif
                    when IsSet(kept) && (IsDerived(kept) || IsDerived(compared)):
                    return new CaseExpression(
                        [new CaseWhenClause(MutualContainment(kept, compared), new SqlConstantExpression(null, nullif.Type, nullif.TypeMapping))],
                        Multiset(kept));

                // Count of an unsorted text set: its distinct elements (#485).
                case SqlFunctionExpression { Name: "cardinality", Arguments: [var counted] } cardinality when IsDerived(counted):
                    return DistinctCount(counted, cardinality);

                case SqlFunctionExpression { Name: var name } placeholder when IsCanonical(name):
                    return Build(placeholder, canonical: true);

                // A text operation has no canonical form in SQL; every use the walker let through ignores order and repeats.
                case SqlFunctionExpression { Name: var name } placeholder when IsUnguarded(name):
                    return Build(placeholder, canonical: false);

                default:
                    return base.VisitExtension(node);
            }
        }

        private SqlExpression Counted(SqlExpression side, SqlExpression counted)
            => side is SqlFunctionExpression { Name: "cardinality" } cardinality
                ? cardinality.Update(cardinality.Instance, [Multiset(counted)])
                : (SqlExpression)Visit(side);

        /// <summary>An expression whose consumer ignores order and duplicates.</summary>
        private SqlExpression Multiset(SqlExpression expression) => expression switch
        {
            SqlFunctionExpression { Name: var name } placeholder when IsCanonical(name) || IsUnguarded(name) => Build(placeholder, canonical: false),

            CaseExpression conditional => conditional.Update(
                (SqlExpression?)Visit(conditional.Operand),
                [.. conditional.WhenClauses.Select(clause => new CaseWhenClause((SqlExpression)Visit(clause.Test), Multiset(clause.Result)))],
                conditional.ElseResult is null ? null : Multiset(conditional.ElseResult)),

            SqlFunctionExpression { Name: "COALESCE", Arguments: { } arguments } coalesce
                => coalesce.Update(coalesce.Instance, [.. arguments.Select(Multiset)]),

            SqlFunctionExpression { Name: "array_remove", Arguments: [var input, var element] } remove when IsSet(remove)
                => remove.Update(remove.Instance, [Multiset(input), (SqlExpression)Visit(element)]),

            _ => (SqlExpression)Visit(expression)
        };

        private SqlExpression Build(SqlFunctionExpression placeholder, bool canonical)
        {
            var definition = SetTypeRegistry.TryGetByClrType(placeholder.Type, out var found)
                ? found
                : throw new InvalidOperationException($"No value set type is registered for '{placeholder.Type}'.");

            // What is built around the operands canonicalizes them, so they need only be multisets.
            var left  = Multiset(placeholder.Arguments![0]);
            var right = Multiset(placeholder.Arguments[1]);

            switch (placeholder.Name)
            {
                case CanonicalUnion or CanonicalAdd or UnguardedUnion or UnguardedAdd:
                    var concatenation = new SqlFunctionExpression(
                        "array_cat", [left, placeholder.Name is CanonicalAdd or UnguardedAdd ? Singleton(right, definition) : right],
                        nullable: false, argumentsPropagateNullability: [false, false], placeholder.Type, placeholder.TypeMapping);
                    return canonical ? Subquery(concatenation, filter: null, canonical: true, definition, placeholder) : concatenation;

                case CanonicalIntersect or UnguardedIntersect:
                    return Subquery(left, x => Contains(right, x, definition), canonical, definition, placeholder);

                default:
                    return Subquery(left, x => new SqlUnaryExpression(ExpressionType.Not, Contains(right, x, definition), typeof(bool), _boolMapping),
                                    canonical, definition, placeholder);
            }
        }

        /// <summary>
        /// <c>ARRAY((SELECT DISTINCT ON (vrN.x) vrN.x FROM unnest(source) WITH ORDINALITY AS vrN(x)
        /// WHERE vrN.x IS NOT NULL [AND filter] ORDER BY vrN.x, vrN.ordinality))</c>, or without the
        /// <c>DISTINCT ON</c>, the ordinality and the <c>ORDER BY</c> as a multiset.
        /// </summary>
        private SqlExpression Subquery(
            SqlExpression source, Func<ColumnExpression, SqlExpression>? filter, bool canonical, ISetTypeDefinition definition,
            SqlFunctionExpression placeholder)
        {
            var alias          = "vr" + _aliases++;
            var elementMapping = definition.ElementTypeMapping
                              ?? typeMappingSource.FindMapping(definition.ElementClrType, definition.ElementStoreType);
            var x              = new ColumnExpression("x", alias, definition.ElementClrType, elementMapping, nullable: true);

            // A set never holds NULL: one another tool stored is dropped, as the read would refuse it.
            SqlExpression predicate = new SqlUnaryExpression(ExpressionType.NotEqual, x, typeof(bool), _boolMapping);
            if (filter is not null)
                predicate = new SqlBinaryExpression(ExpressionType.AndAlso, predicate, filter(x), typeof(bool), _boolMapping);

            var select = canonical
                ? new SelectExpression(
                    alias: null,
                    tables: [new PgUnnestExpression(alias, source, x.Name, withOrdinality: true)],
                    predicate: predicate,
                    groupBy: [],
                    having: null,
                    projections: [new ProjectionExpression(
                        new SqlFragmentExpression($"DISTINCT ON ({alias}.{x.Name}) {alias}.{x.Name}", x.Type, elementMapping), "")],
                    distinct: false,
                    orderings:
                    [
                        new OrderingExpression(x, ascending: true),
                        new OrderingExpression(
                            new ColumnExpression("ordinality", alias, typeof(long), typeMappingSource.FindMapping(typeof(long)), nullable: false),
                            ascending: true)
                    ],
                    offset: null,
                    limit: null,
                    sqlAliasManager: new SqlAliasManager())
                : new SelectExpression(
                    alias: null,
                    tables: [new PgUnnestExpression(alias, source, x.Name, withOrdinality: false)],
                    predicate: predicate,
                    groupBy: [],
                    having: null,
                    projections: [new ProjectionExpression(x, x.Name)],
                    distinct: false,
                    orderings: [],
                    offset: null,
                    limit: null,
                    sqlAliasManager: new SqlAliasManager());

            return new SqlFunctionExpression(
                "ARRAY", [new ScalarSubqueryExpression(select)], nullable: false, argumentsPropagateNullability: [false],
                placeholder.Type, placeholder.TypeMapping);
        }

        /// <summary>
        /// Whether an expression may be an unsorted text set, judged as <see cref="Walker"/> judges it: a text
        /// operation, what passes one on, or a subquery column the walker found projecting one.
        /// </summary>
        private bool IsDerived(SqlExpression? expression) => expression switch
        {
            SqlFunctionExpression { Name: var name } when IsUnguarded(name) && IsSet(expression)              => true,
            SqlFunctionExpression { Name: "array_remove", Arguments: [var input, _] } when IsSet(expression) => IsDerived(input),
            SqlFunctionExpression { Name: "COALESCE", Arguments: { } arguments }                           => arguments.Any(IsDerived),
            SqlFunctionExpression { Name: "NULLIF", Arguments: [var kept, _] }                             => IsDerived(kept),
            CaseExpression conditional => conditional.WhenClauses.Any(clause => IsDerived(clause.Result)) || IsDerived(conditional.ElseResult),
            SqlUnaryExpression { OperatorType: ExpressionType.Convert } convert when IsSet(convert)        => IsDerived(convert.Operand),
            ColumnExpression column                                                                        => derivedColumns.Contains((column.TableAlias, column.Name)),
            _                                                                                              => false
        };

        /// <summary><c>a @&gt; b AND b @&gt; a</c>: set equality, which neither order nor repeats change.</summary>
        private SqlExpression MutualContainment(SqlExpression left, SqlExpression right)
        {
            var (l, r) = (Multiset(left), Multiset(right));
            return new SqlBinaryExpression(
                ExpressionType.AndAlso,
                new PgBinaryExpression(PgExpressionType.Contains, l, r, typeof(bool), _boolMapping),
                new PgBinaryExpression(PgExpressionType.Contains, r, l, typeof(bool), _boolMapping),
                typeof(bool),
                _boolMapping);
        }

        /// <summary>
        /// <c>(SELECT count(DISTINCT vrN.x)::int FROM unnest(set) AS vrN(x))</c>, NULL for a NULL set as
        /// <c>cardinality</c> is: <c>count</c> over <c>unnest(NULL)</c> is 0. <c>count(DISTINCT)</c> skips a NULL
        /// element another tool stored, as a set never holds one. A string set column compares bytes (#483),
        /// so <c>DISTINCT</c> folds exactly the elements memory folds.
        /// </summary>
        private SqlExpression DistinctCount(SqlExpression counted, SqlFunctionExpression cardinality)
        {
            var definition = SetTypeRegistry.TryGetByClrType(counted.Type, out var found)
                ? found
                : throw new InvalidOperationException($"No value set type is registered for '{counted.Type}'.");

            var set            = Multiset(counted);
            var alias          = "vr" + _aliases++;
            var elementMapping = definition.ElementTypeMapping
                              ?? typeMappingSource.FindMapping(definition.ElementClrType, definition.ElementStoreType);
            var x              = new ColumnExpression("x", alias, definition.ElementClrType, elementMapping, nullable: true);
            var count          = new SqlFunctionExpression(
                "count", [new DistinctExpression(x)], nullable: false, argumentsPropagateNullability: [false], typeof(long),
                typeMappingSource.FindMapping(typeof(long)));

            var select = new SelectExpression(
                alias: null,
                tables: [new PgUnnestExpression(alias, set, x.Name, withOrdinality: false)],
                predicate: null,
                groupBy: [],
                having: null,
                projections: [new ProjectionExpression(new SqlUnaryExpression(ExpressionType.Convert, count, cardinality.Type, cardinality.TypeMapping), "")],
                distinct: false,
                orderings: [],
                offset: null,
                limit: null,
                sqlAliasManager: new SqlAliasManager());
            var subquery = new ScalarSubqueryExpression(select);

            return MayBeNull(set)
                ? new CaseExpression(
                    [new CaseWhenClause(new SqlUnaryExpression(ExpressionType.Equal, set, typeof(bool), _boolMapping),
                                        new SqlConstantExpression(null, cardinality.Type, cardinality.TypeMapping))],
                    subquery)
                : subquery;
        }

        private static bool MayBeNull(SqlExpression expression) => expression switch
        {
            SqlFunctionExpression function => function.IsNullable,
            ColumnExpression column        => column.IsNullable,
            SqlConstantExpression constant => constant.Value is null,
            _                              => true
        };

        /// <summary><c>set @&gt; ARRAY[x]</c>.</summary>
        private SqlExpression Contains(SqlExpression set, ColumnExpression x, ISetTypeDefinition definition)
            => new PgBinaryExpression(PgExpressionType.Contains, set, Singleton(x, definition), typeof(bool), _boolMapping);

        private static PgNewArrayExpression Singleton(SqlExpression element, ISetTypeDefinition definition)
            => new([element], definition.ElementClrType.MakeArrayType(), definition.SetTypeMapping);
    }

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
            "Compose a server-side text set operation only with Contains, Overlaps, IsSubsetOf/IsSupersetOf, "
          + "IsProperSubsetOf/IsProperSupersetOf, Remove, IsEmpty, Count, == or !=, which don't depend on order or "
          + "repeats, or materialize it first (project it, or switch to AsEnumerable()) and work with the value.";

        public static InvalidOperationException For(Use use) => use switch
        {
            Use.Membership => Create(
                "Membership among value sets (Contains over a sequence of sets)", "array equality",
                "Compare with == against each set, which compares the elements however they are ordered, or with "
              + "IsSubsetOf/IsSupersetOf, or materialize the result first and compare the value."),
            Use.GroupBy      => Create("Grouping by it ('GroupBy')", "GROUP BY", Remedy),
            Use.Distinct     => Create("'Distinct' over it", "DISTINCT", Remedy),
            Use.OrderBy      => Create("Ordering by it ('OrderBy')", "ORDER BY", Remedy),
            Use.SetOperation => Create("A deduplicating set operation over it ('Union', 'Intersect' or 'Except' of two queries)",
                                       "the set operation's row comparison", Remedy),
            _                => Create("This use of it", "the operation", Remedy)
        };

        private static InvalidOperationException Create(string subject, string operation, string remedy)
            => new($"{subject} cannot be translated over a text value set Union, Add, Intersect or Except computed on the "
                 + "server. PostgreSQL sorts text by the database collation rather than ordinally, so the server-side result "
                 + "is left unsorted, and a Union or Add may repeat an element. "
                 + $"{operation} is sensitive to both, so the query would answer differently from the same expression in "
                 + $"memory. {remedy}");

        public static InvalidOperationException Setter(string column) => new(
            $"'ExecuteUpdate' cannot set the value set column '{column}' to a text Union, Add, Intersect or Except computed "
          + "on the server. PostgreSQL sorts text by the database collation rather than ordinally, so the row would store "
          + "the elements unsorted, and a Union or Add may repeat one, after which == against that row is wrong. "
          + "Compute the result in memory and set the value, or load the entities and save them with SaveChanges.");

        // Until 10.1.0 a value that may be NULL on some row got a second reason: jsonb_set returns NULL for
        // a NULL value, so the UPDATE wrote NULL as the whole document. That holds on Npgsql EF 10.0.0–10.0.2
        // only. 10.0.3 writes such a value through jsonb_set_lax (PostgreSQL 16 and later) or coalesces it to
        // JSON null (npgsql/efcore.pg#3855), which sets the property to JSON null as SaveChanges does. The
        // JSON array is refused on every patch, so the message names only that.
        public static InvalidOperationException JsonSetter(string column, string? path) => new(
            $"'ExecuteUpdate' cannot set the value set '{column}{(path is null ? "" : "." + path)}' inside a JSON column. "
          + "EF Core writes it with PostgreSQL's to_jsonb, which stores a JSON array where the document holds the "
          + "set's literal string, so the row would no longer load and queries on the property would fail. "
          + $"Load the entity and save it with SaveChanges, or set the whole JSON property (SetProperty(e => e.{column}, …)) "
          + "or a whole complex property that contains the set.");

        public static InvalidOperationException Collation(string collation) => new(
            $"A string set cannot be compared under the collation '{collation}' (EF.Functions.Collate). A string set "
          + "compares its elements ordinally in memory, and under a case- or accent-insensitive collation Contains, the "
          + "subset operators, == and Remove would answer differently in the database. Use \"C\", or compare in memory.");

        public static InvalidOperationException NullJsonSetter(string column, string? path) => new(
            $"'ExecuteUpdate' cannot set the value set '{column}{(path is null ? "" : "." + path)}' inside a JSON column "
          + "to null. EF Core writes it as to_jsonb(NULL), and PostgreSQL cannot type the untyped NULL (42804). "
          + "Load the entity, set the property to null and save it with SaveChanges, which writes JSON null, or set "
          + $"the whole JSON property (SetProperty(e => e.{column}, …)) or a whole complex property that contains the "
          + "set to a value holding null.");
    }
}

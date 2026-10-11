using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Pre-translation passes over the query: two rewrites and two refusals.
/// </summary>
/// <remarks>
/// <para>
/// The rewrite turns the <see cref="RangeSet{TRange,T}"/> operators (<c>|</c>, <c>&amp;</c>,
/// <c>-</c>) into their <c>Union</c> / <c>Intersect</c> / <c>Except</c> method-call equivalents.
/// EF Core translates user-defined operators as plain SQL binary operators without consulting
/// method call translators; the rewrite routes them through
/// <see cref="ValueRangesMethodCallTranslator"/> instead, producing valid multirange SQL. A second
/// rewrite gives a range constant EF Core inlined its union type — see
/// <see cref="RangeVariantConstantRetyper"/>.
/// </para>
/// <para>
/// The refusals reject comparing a server-computed value set <c>Union</c> for equality — see
/// <see cref="UnionEqualityGuard"/> — and <c>ExecuteUpdate</c> setting a value set inside a JSON
/// column — see <see cref="JsonValueSetUpdateGuard"/>.
/// </para>
/// </remarks>
public sealed class ValueRangesQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        UnionEqualityGuard.Instance.Visit(queryExpression);
        if (eventData.Context?.Model is { } model) new JsonValueSetUpdateGuard(model).Visit(queryExpression);
        return RangeVariantConstantRetyper.Instance.Visit(RangeSetOperatorRewriter.Instance.Visit(queryExpression));
    }

    /// <summary>
    /// Refuses <c>ExecuteUpdate</c> setting a value set that sits inside a JSON-mapped complex type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EF Core translates such a setter to <c>jsonb_set(doc, '{Tags}', to_jsonb(@p))</c>, binding the
    /// set's provider value — a PostgreSQL array. <c>to_jsonb</c> of an array is a JSON array, while
    /// the document holds the set as its literal string, which is what the reader expects and what a
    /// query casts to the array type. The update reports success; afterwards the entity no longer
    /// loads (<c>Cannot get the value of a token type 'StartArray' as a string</c>) and every query
    /// on the property fails. Nothing goes wrong at write time, which is why it is refused here rather
    /// than left to surface on the next read.
    /// </para>
    /// <para>
    /// Ranges and multiranges are not refused: <c>to_jsonb</c> of a range is a JSON string holding
    /// PostgreSQL's own range output, which the strict literal parser reads. Setting the whole JSON
    /// property is not refused either — EF writes that through the plugin's own writer.
    /// </para>
    /// <para>
    /// EF Core's <c>ExecuteUpdate</c> lowers to a private
    /// <c>ExecuteUpdate&lt;TSource&gt;(IQueryable&lt;TSource&gt;, IReadOnlyList&lt;ITuple&gt;)</c> whose setters are
    /// <c>new Tuple&lt;Delegate, object&gt;(propertyLambda, value)</c>. It is matched by name, declaring type
    /// and parameter shape rather than through EF's internal <c>ExecuteUpdateMethodInfo</c>, which
    /// would draw EF1001; <c>ExecuteUpdateShapeTests</c> fails if an EF upgrade changes that shape.
    /// </para>
    /// </remarks>
    private sealed class JsonValueSetUpdateGuard(IModel model) : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method is { Name: "ExecuteUpdate", IsGenericMethod: true } method
                && method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && method.GetParameters() is [_, { ParameterType: var settersType }]
                && settersType == typeof(IReadOnlyList<ITuple>)
                && node.Arguments[1] is NewArrayExpression setters)
            {
                foreach (var setter in setters.Expressions)
                    if (setter is NewExpression { Arguments: [var property, _] } && Unwrap(property) is LambdaExpression lambda)
                        Check(lambda);
            }

            return base.VisitMethodCall(node);
        }

        private void Check(LambdaExpression lambda)
        {
            var path = new List<string>();
            for (var current = Unwrap(lambda.Body); current is MemberExpression member; current = Unwrap(member.Expression))
                path.Insert(0, member.Member.Name);

            if (path.Count < 2 || model.FindEntityType(lambda.Parameters[0].Type) is not { } entityType) return;

            ITypeBase declaring = entityType;
            foreach (var name in path[..^1])
            {
                if (declaring.FindComplexProperty(name) is not { } complex) return;
                declaring = complex.ComplexType;
            }

            var target = Unwrap(lambda.Body)!.Type;
            if (!declaring.IsMappedToJson() || !SetTypeRegistry.TryGetByClrType(target, out _)) return;

            throw new InvalidOperationException(
                $"'ExecuteUpdate' cannot set the value set '{string.Join('.', path)}' inside a JSON column. "
              + "EF Core writes it with PostgreSQL's to_jsonb, which stores a JSON array where the document holds "
              + "the set's literal string, so the row would no longer load and queries on the property would fail. "
              + $"Load the entity and save it with SaveChanges, or set the whole JSON property (SetProperty(e => e.{path[0]}, …)).");
        }

        private static Expression? Unwrap(Expression? expression)
            => expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote } unary
                   ? Unwrap(unary.Operand)
                   : expression;
    }

    /// <summary>
    /// Refuses <c>==</c>, <c>!=</c> and <c>Equals</c> where either side is a value set
    /// <c>Union</c> computed on the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Union</c> translates to <c>array_cat</c>, which concatenates: the result carries
    /// duplicates and keeps each operand's ordering. The membership operators the sets translate
    /// to — <c>@&gt;</c>, <c>&lt;@</c>, <c>&amp;&amp;</c> — ignore both, which is why a union
    /// composes safely with them and with <c>IsProperSubsetOf</c>/<c>IsProperSupersetOf</c>,
    /// written as <c>&lt;@ AND NOT @&gt;</c> for exactly this reason. Array equality ignores
    /// neither.
    /// </para>
    /// <para>
    /// Both halves bite, and the ordering half is the one that surprises: over
    /// <c>text[]</c> the server answers <see langword="false"/> for
    /// <c>{a,c} ∪ {a,b} = {a,b,c}</c> because of the repeated <c>a</c>, and equally
    /// <see langword="false"/> for <c>{a,c} ∪ {b} = {a,b,c}</c>, where nothing repeats and only
    /// the order differs. In memory both are <see langword="true"/>. Nothing throws and the row
    /// simply does not match.
    /// </para>
    /// <para>
    /// PostgreSQL has no array_distinct to canonicalize into, and sorting inside the query would
    /// order <c>text</c> by the database collation rather than ordinally — silently disagreeing
    /// with the client's canonical order, which is the same defect one layer down. So this is
    /// refused rather than translated, matching how <c>Count</c> over a union is refused in
    /// <see cref="ValueSetsMemberTranslator"/>. Materialize first, or compare with
    /// <c>IsSubsetOf</c>/<c>IsSupersetOf</c>, which mean the same thing for canonical sets and
    /// translate correctly.
    /// </para>
    /// </remarks>
    private sealed class UnionEqualityGuard : ExpressionVisitor
    {
        public static readonly UnionEqualityGuard Instance = new();

        /// <summary>
        /// The operators whose lambdas must translate in full. Everything else — a projection above
        /// all — is left alone, because EF falls back to client evaluation there and computes the
        /// comparison correctly against the materialized set.
        /// </summary>
        /// <remarks>
        /// This is what keeps the refusal in step with how <c>Count</c> over a union behaves.
        /// That one returns <see langword="null"/> from a translator, so EF decides: a predicate
        /// fails, a projection degrades to client evaluation. Equality has no translator seam and
        /// is refused a step earlier, so the contexts have to be named rather than inferred —
        /// otherwise the refusal is stricter than the defect and takes a working projection with
        /// it.
        /// </remarks>
        private static readonly HashSet<string> MustTranslate = new(StringComparer.Ordinal)
        {
            "Where", "Any", "All", "Count", "LongCount",
            "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
            "SkipWhile", "TakeWhile",
            "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "GroupBy"
        };

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Only scan inside the lambdas of operators EF must translate server-side. `Queryable`
            // rather than `Enumerable`: past an AsEnumerable the comparison runs in memory and is
            // correct, which is the documented way to ask for it.
            if (node.Method.DeclaringType == typeof(Queryable) && MustTranslate.Contains(node.Method.Name))
            {
                foreach (var argument in node.Arguments.Skip(1))
                    ComparisonScan.Instance.Visit(argument);
            }

            return base.VisitMethodCall(node);
        }
    }

    /// <summary>Finds an equality comparison against a server-computed union.</summary>
    private sealed class ComparisonScan : ExpressionVisitor
    {
        public static readonly ComparisonScan Instance = new();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual)
                Refuse(node.Left, node.Right, node.NodeType == ExpressionType.Equal ? "==" : "!=");

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(object.Equals))
            {
                if (node.Object is { } instance && node.Arguments.Count == 1)
                    Refuse(instance, node.Arguments[0], "Equals");
                else if (node.Object is null && node.Arguments.Count == 2)
                    Refuse(node.Arguments[0], node.Arguments[1], "Equals");
            }

            return base.VisitMethodCall(node);
        }

        private static void Refuse(Expression left, Expression right, string comparison)
        {
            if (!DerivesFromUnion(left) && !DerivesFromUnion(right)) return;

            throw new InvalidOperationException(
                $"'{comparison}' cannot be translated when either side is a value set Union computed "
              + "on the server. Union translates to array_cat, which concatenates rather than "
              + "canonicalizes, so the result carries duplicates and keeps each operand's ordering "
              + "while array equality is sensitive to both — the comparison would answer false for "
              + "sets that are equal in memory. Compare with IsSubsetOf/IsSupersetOf, which mean the "
              + "same thing for canonical sets and ignore order and multiplicity, or materialize the "
              + "union first and compare the value.");
        }

        /// <summary>
        /// Whether the operand is a value set <c>Union</c>, or something built on one that did not
        /// restore canonical form — the LINQ counterpart of the same test
        /// <see cref="ValueSetsMemberTranslator"/> applies to the translated SQL.
        /// </summary>
        private static bool DerivesFromUnion(Expression expression) =>
            expression is MethodCallExpression { Method: { DeclaringType: { } declaringType } method }
         && declaringType == typeof(ValueSetExtensions)
         && method.Name switch
            {
                nameof(ValueSetExtensions.Union) => true,

                // array_remove preserves whatever canonical form its input had, which for a
                // concatenation is none — the same reason the Count refusal looks through it.
                nameof(ValueSetExtensions.Remove) when ((MethodCallExpression) expression).Arguments.Count > 0
                    => DerivesFromUnion(((MethodCallExpression) expression).Arguments[0]),

                _ => false
            };
    }

    /// <summary>
    /// Retypes a range constant that EF Core inlined from the runtime type of its value — the sealed
    /// variant — to the range type itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EF Core evaluates an expression that depends on nothing in the query — a range built inline,
    /// <c>r.Period == DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1))</c>,
    /// <c>Int32Range.Parse("[1,5]", null)</c> — on the client before translation, and inlines the
    /// result as a constant typed by its value: <c>DateRange.Finite</c>, under a conversion back to
    /// <c>DateRange</c>. No type mapping exists for a variant, and EF Core does not carry the column's
    /// mapping across to an operand of another CLR type, so <c>==</c>, <c>!=</c>, <c>Equals</c> and a
    /// conditional failed with "does not have a type mapping assigned". The method translators
    /// (<c>Contains</c>, <c>Overlaps</c>, …) were unaffected — they type their operands themselves —
    /// and so were a captured variable and <c>EF.Constant</c>, which EF types by the declared type.
    /// </para>
    /// <para>
    /// A factory call that depends on a column never reaches here as a constant; the method call
    /// translator turns it into the range constructor function.
    /// </para>
    /// </remarks>
    private sealed class RangeVariantConstantRetyper : ExpressionVisitor
    {
        public static readonly RangeVariantConstantRetyper Instance = new();

        protected override Expression VisitConstant(ConstantExpression node)
            => node.Value is not null
               && node.Type.IsSealed
               && node.Type.BaseType is { IsAbstract: true } union
               && RangeTypeRegistry.TryGetByClrType(union, out _, out var isSet) && !isSet
                   ? Expression.Constant(node.Value, union)
                   : node;
    }

    private sealed class RangeSetOperatorRewriter : ExpressionVisitor
    {
        public static readonly RangeSetOperatorRewriter Instance = new();

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.Method is { DeclaringType: { IsGenericType: true } declaringType } operatorMethod
             && declaringType.GetGenericTypeDefinition() == typeof(RangeSet<,>)
             && OperatorEquivalent(operatorMethod.Name) is { } methodName)
            {
                // Both Union/Intersect/Except overloads (range and set operand) exist;
                // the operator's second parameter type picks the matching one.
                var equivalent = declaringType.GetMethod(
                    methodName, [operatorMethod.GetParameters()[1].ParameterType])!;

                return Expression.Call(Visit(node.Left), equivalent, Visit(node.Right));
            }

            return base.VisitBinary(node);
        }

        private static string? OperatorEquivalent(string operatorMethodName)
            => operatorMethodName switch
               {
                   "op_BitwiseOr"   => nameof(RangeSet<,>.Union),
                   "op_BitwiseAnd"  => nameof(RangeSet<,>.Intersect),
                   "op_Subtraction" => nameof(RangeSet<,>.Except),
                   _                => null
               };
    }
}
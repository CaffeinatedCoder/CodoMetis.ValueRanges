using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Two pre-translation passes over the query: one rewrite and one refusal.
/// </summary>
/// <remarks>
/// <para>
/// The rewrite turns the <see cref="RangeSet{TRange,T}"/> operators (<c>|</c>, <c>&amp;</c>,
/// <c>-</c>) into their <c>Union</c> / <c>Intersect</c> / <c>Except</c> method-call equivalents.
/// EF Core translates user-defined operators as plain SQL binary operators without consulting
/// method call translators; the rewrite routes them through
/// <see cref="ValueRangesMethodCallTranslator"/> instead, producing valid multirange SQL.
/// </para>
/// <para>
/// The refusal rejects comparing a server-computed value set <c>Union</c> for equality — see
/// <see cref="UnionEqualityGuard"/>.
/// </para>
/// </remarks>
public sealed class ValueRangesQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        UnionEqualityGuard.Instance.Visit(queryExpression);
        return RangeSetOperatorRewriter.Instance.Visit(queryExpression);
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
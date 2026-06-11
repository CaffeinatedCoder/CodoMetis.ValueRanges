using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Rewrites the <see cref="RangeSet{TRange,T}"/> operators (<c>|</c>, <c>&amp;</c>, <c>-</c>)
/// into their <c>Union</c> / <c>Intersect</c> / <c>Except</c> method-call equivalents before
/// query translation. EF Core translates user-defined operators as plain SQL binary operators
/// without consulting method call translators; the rewrite routes them through
/// <see cref="ValueRangesMethodCallTranslator"/> instead, producing valid multirange SQL.
/// </summary>
public sealed class ValueRangesQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        => RangeSetOperatorRewriter.Instance.Visit(queryExpression);

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
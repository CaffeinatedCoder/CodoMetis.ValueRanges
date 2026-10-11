using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Pre-translation rewrites of the query.
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
/// The value set refusals — a server-computed <c>Union</c> consumed by an order- or
/// multiplicity-sensitive operation, and <c>ExecuteUpdate</c> writing a value set into a JSON
/// column — are not made here but on the translated SQL, by <see cref="ValueSetSqlGuard"/>: at the
/// LINQ level they can only be recognized by how the query is written, and every pattern list
/// missed a shape that translated to the same SQL.
/// </para>
/// </remarks>
public sealed class ValueRangesQueryExpressionInterceptor : IQueryExpressionInterceptor
{
    /// <inheritdoc />
    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        => RangeVariantConstantRetyper.Instance.Visit(RangeSetOperatorRewriter.Instance.Visit(queryExpression));

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
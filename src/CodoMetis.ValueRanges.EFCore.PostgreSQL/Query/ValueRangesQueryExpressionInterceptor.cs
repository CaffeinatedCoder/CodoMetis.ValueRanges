using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

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
/// <see cref="RangeVariantConstantRetyper"/>. A third sends a captured collection of value sets one
/// parameter per set, because PostgreSQL has no array of arrays to bind it as one — see
/// <see cref="SetCollectionParameterExpander"/>.
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
        => new SetCollectionParameterExpander(DefaultCollectionMode(eventData.Context)).Visit(
            RangeVariantConstantRetyper.Instance.Visit(RangeSetOperatorRewriter.Instance.Visit(queryExpression)));

    private static ParameterTranslationMode DefaultCollectionMode(DbContext? context)
        => context is null
               ? ParameterTranslationMode.Parameter
               : RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).ParameterizedCollectionMode;

    /// <summary>
    /// Binds a captured collection of value sets one parameter per set — <c>IN (@p1, @p2, …)</c> —
    /// where EF Core would bind it as a single array parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A value set is itself a PostgreSQL array, so a collection of them as one parameter is an array
    /// of arrays (<c>text[][]</c>), which PostgreSQL does not have: Npgsql refused to write it, and
    /// <c>wanted.Contains(r.Tags)</c> over a captured list or array threw <c>InvalidCastException</c>
    /// for every family. EF Core's <see cref="ParameterTranslationMode.MultipleParameters"/> mode binds
    /// each set as its own parameter, through the set's own mapping, so <c>Contains</c> becomes
    /// <c>IN</c> — equality against each — and <c>Any</c> or <c>Count</c> over the collection read it
    /// from <c>VALUES</c>. Equality of two canonical arrays is set equality, so the answer is the one
    /// the same expression gives in memory.
    /// </para>
    /// <para>
    /// Only a collection that would have gone out as one array parameter is changed. A context
    /// configured with <see cref="ParameterTranslationMode.Constant"/> or
    /// <see cref="ParameterTranslationMode.MultipleParameters"/>, and a collection wrapped in
    /// <see cref="EF.Constant{T}"/>, already translate, so they keep their mode. A collection wrapped
    /// in <see cref="EF.Parameter{T}"/> asks for the one array parameter that cannot be bound, so it is
    /// refused here, with what to write instead.
    /// </para>
    /// <para>
    /// A <c>Union</c> computed on the server stays refused under <c>IN</c>: that is decided on the SQL
    /// tree, by <see cref="ValueSetSqlGuard"/>.
    /// </para>
    /// </remarks>
    private sealed class SetCollectionParameterExpander(ParameterTranslationMode defaultMode) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
            => node is QueryParameterExpression parameter
               && (parameter.TranslationMode ?? defaultMode) == ParameterTranslationMode.Parameter
               && SetElementType(parameter.Type) is not null
                   ? new QueryParameterExpression(parameter.Name, parameter.Type, ParameterTranslationMode.MultipleParameters)
                   : base.VisitExtension(node);

        protected override Expression VisitMethodCall(MethodCallExpression node)
            => node.Method is { Name: nameof(EF.Parameter), IsGenericMethod: true }
               && node.Method.DeclaringType == typeof(EF)
               && SetElementType(node.Type) is { } set
                   ? throw new InvalidOperationException(
                       $"A collection of {set.Name} cannot be sent as a single parameter ('EF.Parameter'). Each {set.Name} "
                     + "is a PostgreSQL array, and PostgreSQL has no array of arrays, so there is no type to bind the "
                     + "collection as. Remove 'EF.Parameter': without it the collection is sent one parameter per set, "
                     + "and Contains translates to IN (@p1, @p2, …).")
                   : base.VisitMethodCall(node);

        /// <summary>The registered value set type a collection type holds, or <see langword="null"/>.</summary>
        private static Type? SetElementType(Type type)
        {
            var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                 ? type
                                 : type.GetInterfaces().FirstOrDefault(
                                     i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerable?.GetGenericArguments()[0] is { } element && SetTypeRegistry.TryGetByClrType(element, out _)
                       ? element
                       : null;
        }
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
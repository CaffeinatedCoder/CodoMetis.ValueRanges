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
/// <see cref="RangeVariantConstantRetyper"/>. A third sends a captured collection of value sets or ranges
/// one parameter per element, because neither can be bound as one array parameter — see
/// <see cref="CollectionParameterExpander"/>.
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
        => CollectionParameterExpander.Expand(
            RangeVariantConstantRetyper.Instance.Visit(RangeSetOperatorRewriter.Instance.Visit(queryExpression)),
            DefaultCollectionMode(eventData.Context));

    private static ParameterTranslationMode DefaultCollectionMode(DbContext? context)
        => context is null
               ? ParameterTranslationMode.Parameter
               : RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>()).ParameterizedCollectionMode;

    /// <summary>
    /// Binds a captured collection of value sets or ranges one parameter per element —
    /// <c>IN (@p1, @p2, …)</c> — where EF Core would bind it as a single array parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Neither collection can go out as one parameter. A value set is itself a PostgreSQL array, so a
    /// collection of them is an array of arrays (<c>text[][]</c>), which PostgreSQL does not have:
    /// Npgsql refused to write it (<c>InvalidCastException</c>). A collection of ranges reaches Npgsql
    /// as an untyped <c>NpgsqlRange&lt;T&gt;[]</c>, which Npgsql infers as a <em>multirange</em>, so
    /// <c>r.Seats = ANY (@p)</c> failed on the server (<c>42809</c>), and with an
    /// <c>InvalidCastException</c> for the custom <c>timerange</c>, which has no inferred type at all.
    /// Typing that one parameter <c>int4range[]</c> would need Npgsql's internal array mapping, and
    /// still could not name the custom type.
    /// </para>
    /// <para>
    /// EF Core's <see cref="ParameterTranslationMode.MultipleParameters"/> mode binds each element as
    /// its own parameter, through the element's own mapping, which names the store type. So
    /// <c>Contains</c> becomes <c>IN</c> — equality against each — and <c>Any</c> or <c>Count</c> over
    /// the collection read it from <c>VALUES</c>. This is the SQL a constant or inline collection
    /// already produced. Equality of two canonical arrays is set equality, and range equality is the
    /// range <c>==</c> translation, so the answer is the one the same expression gives in memory. A
    /// collection of <see cref="RangeSet{TRange,T}"/> is left alone: <c>NpgsqlRange&lt;T&gt;[][]</c>
    /// infers as a multirange array, which is what it is.
    /// </para>
    /// <para>
    /// Both collections are switched wherever they are used. Until 10.0.0 a range collection was
    /// switched only when a <c>Contains</c> in the query read it, because <c>Contains</c> is what had
    /// failed: on its own, <c>Any</c> or <c>Count</c> over it ran, as <c>unnest</c> of the parameter
    /// Npgsql had typed as a multirange — which merged overlapping and adjacent ranges and dropped
    /// empty ones, so <c>Count(w =&gt; w.Overlaps(c))</c> over <c>[1,5]</c> and <c>[3,8]</c> was 1
    /// where memory says 2, without an error (issue #76). Read from <c>VALUES</c> the list is the
    /// list. An empty collection then reads no rows (<see cref="EmptyCollectionRewriter"/>).
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
    /// A value set <c>Union</c> computed on the server stays refused under <c>IN</c>: that is decided on
    /// the SQL tree, by <see cref="ValueSetSqlGuard"/>.
    /// </para>
    /// </remarks>
    private sealed class CollectionParameterExpander(ParameterTranslationMode defaultMode) : ExpressionVisitor
    {
        public static Expression Expand(Expression query, ParameterTranslationMode defaultMode)
            => new CollectionParameterExpander(defaultMode).Visit(query);

        protected override Expression VisitExtension(Expression node)
            => node is QueryParameterExpression parameter
               && (parameter.TranslationMode ?? defaultMode) == ParameterTranslationMode.Parameter
               && Element(parameter.Type) is not null
                   ? new QueryParameterExpression(parameter.Name, parameter.Type, ParameterTranslationMode.MultipleParameters)
                   : base.VisitExtension(node);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Every use of either collection as one parameter is wrong: a value set collection cannot be
            // bound, and a range collection binds as a multirange, which is not the list.
            if (EFParameterOf(node) is var (type, isSet)) throw EFParameterRefused(type, isSet);

            return base.VisitMethodCall(node);
        }

        private static InvalidOperationException EFParameterRefused(Type type, bool isSet)
            => new($"A collection of {type.Name} cannot be sent as a single parameter ('EF.Parameter'). "
                 + (isSet
                        ? $"Each {type.Name} is a PostgreSQL array, and PostgreSQL has no array of arrays, so there is "
                        + "no type to bind the collection as. "
                        : "Npgsql would send it as an array it reads as a multirange rather than an array of ranges, "
                        + "which merges overlapping and adjacent ranges and drops empty ones. ")
                 + $"Remove 'EF.Parameter': without it the collection is sent one parameter per {(isSet ? "set" : "range")}, "
                 + "Contains translates to IN (@p1, @p2, …), and Any or Count read it from VALUES.");

        /// <summary>The value set or range type of an <c>EF.Parameter(collection)</c> call.</summary>
        private static (Type Type, bool IsSet)? EFParameterOf(Expression? node)
            => node is MethodCallExpression { Method: { Name: nameof(EF.Parameter), IsGenericMethod: true } method } call
               && method.DeclaringType == typeof(EF)
                   ? Element(call.Type)
                   : null;

        /// <summary>
        /// The registered value set or range type a collection type holds, or <see langword="null"/>.
        /// </summary>
        private static (Type Type, bool IsSet)? Element(Type type)
        {
            var enumerable = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                                 ? type
                                 : type.GetInterfaces().FirstOrDefault(
                                     i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (enumerable?.GetGenericArguments()[0] is not { } element) return null;
            if (SetTypeRegistry.TryGetByClrType(element, out _)) return (element, true);

            return RangeTypeRegistry.TryGetByClrType(element, out _, out var isRangeSet) && !isRangeSet
                       ? (element, false)
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
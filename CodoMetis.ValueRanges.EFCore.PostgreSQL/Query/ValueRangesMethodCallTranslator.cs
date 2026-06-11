using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Translates the range algebra to PostgreSQL operators, for every registered range type:
/// <list type="bullet">
///   <item><see cref="RangeExtensions"/> query operations — <c>@&gt;</c>, <c>&lt;@</c>, <c>&amp;&amp;</c>,
///   <c>&lt;&lt;</c>, <c>&gt;&gt;</c>, <c>&amp;&lt;</c>, <c>&amp;&gt;</c>, <c>-|-</c> — and set operations —
///   <c>*</c> for <c>Intersect</c>, and multirange <c>+</c> / <c>-</c> for <c>Union</c> / <c>Except</c>,
///   matching their <see cref="RangeSet{TRange,T}"/> return type.</item>
///   <item><see cref="RangeSet{TRange,T}"/> methods and operators on multirange columns.</item>
///   <item>The static factory methods (<c>CreateFinite</c>, <c>CreateUnboundedStart</c>,
///   <c>CreateUnboundedEnd</c>) as range constructor functions, e.g. <c>daterange(a, b, '[]')</c>.</item>
/// </list>
/// </summary>
internal sealed class ValueRangesMethodCallTranslator(
    NpgsqlSqlExpressionFactory sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource
) : IMethodCallTranslator
{
    // -- RangeExtensions query operations: extension<T>(IRange<T>) --

    private static readonly MethodInfo ContainsValueMethod = GetExtensionMethod(
        nameof(RangeExtensions.Contains), method => method.GetParameters()[1].ParameterType.IsGenericParameter);

    private static readonly MethodInfo ContainsRangeMethod = GetExtensionMethod(
        nameof(RangeExtensions.Contains), method => !method.GetParameters()[1].ParameterType.IsGenericParameter);

    private static readonly MethodInfo IsContainedByMethod        = GetExtensionMethod(nameof(RangeExtensions.IsContainedBy));
    private static readonly MethodInfo OverlapsMethod             = GetExtensionMethod(nameof(RangeExtensions.Overlaps));
    private static readonly MethodInfo IsStrictlyLeftOfMethod     = GetExtensionMethod(nameof(RangeExtensions.IsStrictlyLeftOf));
    private static readonly MethodInfo IsStrictlyRightOfMethod    = GetExtensionMethod(nameof(RangeExtensions.IsStrictlyRightOf));
    private static readonly MethodInfo DoesNotExtendRightOfMethod = GetExtensionMethod(nameof(RangeExtensions.DoesNotExtendRightOf));
    private static readonly MethodInfo DoesNotExtendLeftOfMethod  = GetExtensionMethod(nameof(RangeExtensions.DoesNotExtendLeftOf));

    // -- RangeExtensions set operations: extension<TRange, T>(TRange) --

    private static readonly MethodInfo IsAdjacentToMethod = GetExtensionMethod(nameof(RangeExtensions.IsAdjacentTo));
    private static readonly MethodInfo IntersectMethod    = GetExtensionMethod(nameof(RangeExtensions.Intersect));
    private static readonly MethodInfo UnionMethod        = GetExtensionMethod(nameof(RangeExtensions.Union));
    private static readonly MethodInfo ExceptMethod       = GetExtensionMethod(nameof(RangeExtensions.Except));

    // -- RangeExtensions state methods: extension<T>(IRange<T>) --

    private static readonly MethodInfo IsEmptyMethod          = GetExtensionMethod(nameof(RangeExtensions.IsEmpty));
    private static readonly MethodInfo IsInfinityMethod       = GetExtensionMethod(nameof(RangeExtensions.IsInfinity));
    private static readonly MethodInfo IsFiniteMethod         = GetExtensionMethod(nameof(RangeExtensions.IsFinite));
    private static readonly MethodInfo IsUnboundedStartMethod = GetExtensionMethod(nameof(RangeExtensions.IsUnboundedStart));
    private static readonly MethodInfo IsUnboundedEndMethod   = GetExtensionMethod(nameof(RangeExtensions.IsUnboundedEnd));

    private static MethodInfo GetExtensionMethod(string name, Func<MethodInfo, bool>? filter = null)
        => typeof(RangeExtensions)
          .GetMethods(BindingFlags.Public | BindingFlags.Static)
          .Single(method => method.Name == name && (filter is null || filter(method)));

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression?                             instance,
        MethodInfo                                 method,
        IReadOnlyList<SqlExpression>               arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        var declaringType = method.DeclaringType;

        if (declaringType == typeof(RangeExtensions))
            return TranslateRangeExtensionCall(method, arguments);

        if (declaringType is { IsGenericType: true } && declaringType.GetGenericTypeDefinition() == typeof(RangeSet<,>))
            return TranslateRangeSetCall(instance, declaringType, method, arguments);

        if (declaringType is not null
            && RangeTypeRegistry.TryGetByClrType(declaringType, out var definition, out var isSet)
            && !isSet)
            return TranslateFactoryCall(definition, method, arguments);

        return null;
    }

    // -------------------------------------------------------------------------
    // RangeExtensions
    // -------------------------------------------------------------------------

    private SqlExpression? TranslateRangeExtensionCall(MethodInfo method, IReadOnlyList<SqlExpression> arguments)
    {
        if (!method.IsGenericMethod || !TryResolveDefinition(method, arguments, out var definition))
            return null;

        var generic = method.GetGenericMethodDefinition();

        SqlExpression Range(int index)
            => sqlExpressionFactory.ApplyTypeMapping(Unwrap(arguments[index]), definition.RangeTypeMapping);

        if (generic == ContainsValueMethod)
            return sqlExpressionFactory.Contains(Range(0), ApplyElementMapping(arguments[1], definition));

        if (generic == ContainsRangeMethod)
            return sqlExpressionFactory.Contains(Range(0), Range(1));

        if (generic == IsContainedByMethod)
            return sqlExpressionFactory.ContainedBy(Range(0), Range(1));

        if (generic == OverlapsMethod)
            return sqlExpressionFactory.Overlaps(Range(0), Range(1));

        if (generic == IsStrictlyLeftOfMethod)
            return sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeIsStrictlyLeftOf, Range(0), Range(1));

        if (generic == IsStrictlyRightOfMethod)
            return sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeIsStrictlyRightOf, Range(0), Range(1));

        if (generic == DoesNotExtendRightOfMethod)
            return sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeDoesNotExtendRightOf, Range(0), Range(1));

        if (generic == DoesNotExtendLeftOfMethod)
            return sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeDoesNotExtendLeftOf, Range(0), Range(1));

        if (generic == IsAdjacentToMethod)
            return sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeIsAdjacentTo, Range(0), Range(1));

        if (generic == IntersectMethod)
            return sqlExpressionFactory.MakePostgresBinary(
                PgExpressionType.RangeIntersect, Range(0), Range(1), definition.RangeTypeMapping);

        // Union/Except return a RangeSet, so both operands are lifted to multiranges:
        // range + range errors on disjoint operands, multirange + multirange is total.
        if (generic == UnionMethod)
            return sqlExpressionFactory.MakePostgresBinary(
                PgExpressionType.RangeUnion,
                AsMultirange(Range(0), definition), AsMultirange(Range(1), definition),
                definition.RangeSetTypeMapping);

        if (generic == ExceptMethod)
            return sqlExpressionFactory.MakePostgresBinary(
                PgExpressionType.RangeExcept,
                AsMultirange(Range(0), definition), AsMultirange(Range(1), definition),
                definition.RangeSetTypeMapping);

        if (generic == IsEmptyMethod)
            return BoolFunction("isempty", Range(0));

        if (generic == IsUnboundedStartMethod)
            return BoolFunction("lower_inf", Range(0));

        if (generic == IsUnboundedEndMethod)
            return BoolFunction("upper_inf", Range(0));

        if (generic == IsInfinityMethod)
            return sqlExpressionFactory.AndAlso(
                BoolFunction("lower_inf", Range(0)),
                BoolFunction("upper_inf", Range(0)));

        if (generic == IsFiniteMethod)
            return sqlExpressionFactory.AndAlso(
                sqlExpressionFactory.Not(BoolFunction("lower_inf", Range(0))),
                sqlExpressionFactory.AndAlso(
                    sqlExpressionFactory.Not(BoolFunction("upper_inf", Range(0))),
                    sqlExpressionFactory.Not(BoolFunction("isempty", Range(0)))));

        return null;
    }

    private static bool TryResolveDefinition(
        MethodInfo                              method,
        IReadOnlyList<SqlExpression>            arguments,
        [NotNullWhen(true)] out IRangeTypeDefinition? definition
    )
    {
        foreach (var argument in arguments)
        {
            if (RangeTypeRegistry.TryGetByClrType(argument.Type, out definition, out var isSet) && !isSet)
                return true;
        }

        // Operands statically typed as IRange<T> carry no concrete range type —
        // fall back to the method's element type argument (always the last one).
        return RangeTypeRegistry.TryGetByElementType(method.GetGenericArguments()[^1], out definition);
    }

    // -------------------------------------------------------------------------
    // RangeSet<TRange, T>
    // -------------------------------------------------------------------------

    private SqlExpression? TranslateRangeSetCall(
        SqlExpression?               instance,
        Type                         declaringType,
        MethodInfo                   method,
        IReadOnlyList<SqlExpression> arguments
    )
    {
        if (!RangeTypeRegistry.TryGetByClrType(declaringType, out var definition, out var isSet) || !isSet)
            return null;

        SqlExpression Set(SqlExpression expression)
            => sqlExpressionFactory.ApplyTypeMapping(Unwrap(expression), definition.RangeSetTypeMapping);

        SqlExpression Range(SqlExpression expression)
            => sqlExpressionFactory.ApplyTypeMapping(Unwrap(expression), definition.RangeTypeMapping);

        // Single-range operands of set operations are lifted to one-element multiranges.
        SqlExpression AsSet(SqlExpression expression)
            => Unwrap(expression).Type == definition.RangeSetClrType
                   ? Set(expression)
                   : AsMultirange(Range(expression), definition);

        switch (method.Name)
        {
            case nameof(RangeSet<,>.Contains) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.Contains(
                    Set(instance),
                    arguments[0].Type == definition.ElementClrType
                        ? ApplyElementMapping(arguments[0], definition)
                        : Range(arguments[0]));

            case nameof(RangeSet<,>.Overlaps) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.Overlaps(Set(instance), Range(arguments[0]));

            case nameof(RangeSet<,>.Union) when instance is not null && arguments.Count == 1:
                return SetBinary(PgExpressionType.RangeUnion, Set(instance), AsSet(arguments[0]));

            case nameof(RangeSet<,>.Intersect) when instance is not null && arguments.Count == 1:
                return SetBinary(PgExpressionType.RangeIntersect, Set(instance), AsSet(arguments[0]));

            case nameof(RangeSet<,>.Except) when instance is not null && arguments.Count == 1:
                return SetBinary(PgExpressionType.RangeExcept, Set(instance), AsSet(arguments[0]));

            case nameof(RangeSet<,>.Complement) when instance is not null && arguments.Count == 0:
                return SetBinary(
                    PgExpressionType.RangeExcept,
                    sqlExpressionFactory.Constant(definition.InfiniteRangeSet, definition.RangeSetTypeMapping),
                    Set(instance));

            // User-defined operators arrive as static binary methods.
            case "op_BitwiseOr" when arguments.Count == 2:
                return SetBinary(PgExpressionType.RangeUnion, AsSet(arguments[0]), AsSet(arguments[1]));

            case "op_BitwiseAnd" when arguments.Count == 2:
                return SetBinary(PgExpressionType.RangeIntersect, AsSet(arguments[0]), AsSet(arguments[1]));

            case "op_Subtraction" when arguments.Count == 2:
                return SetBinary(PgExpressionType.RangeExcept, AsSet(arguments[0]), AsSet(arguments[1]));

            default:
                return null;
        }

        SqlExpression SetBinary(PgExpressionType operatorType, SqlExpression left, SqlExpression right)
            => sqlExpressionFactory.MakePostgresBinary(operatorType, left, right, definition.RangeSetTypeMapping);
    }

    // -------------------------------------------------------------------------
    // Factory methods on the concrete range types
    // -------------------------------------------------------------------------

    private SqlExpression? TranslateFactoryCall(
        IRangeTypeDefinition         definition,
        MethodInfo                   method,
        IReadOnlyList<SqlExpression> arguments
    )
    {
        // Bound inclusiveness must be a compile-time constant to pick the bounds literal;
        // in practice it always is, because the flags default at the call site.
        switch (method.Name)
        {
            case "CreateFinite" when arguments.Count == 4
                                     && arguments[2] is SqlConstantExpression { Value: bool startInclusive }
                                     && arguments[3] is SqlConstantExpression { Value: bool endInclusive }:
                return GuardedFiniteRangeConstructor(
                    definition,
                    ApplyElementMapping(arguments[0], definition),
                    ApplyElementMapping(arguments[1], definition),
                    $"{(startInclusive ? '[' : '(')}{(endInclusive ? ']' : ')')}");

            case "CreateUnboundedStart" when arguments.Count == 2
                                             && arguments[1] is SqlConstantExpression { Value: bool endInclusive }:
                return RangeConstructor(
                    definition, NullConstant(definition.ElementClrType), arguments[0],
                    $"({(endInclusive ? ']' : ')')}");

            case "CreateUnboundedEnd" when arguments.Count == 2
                                           && arguments[1] is SqlConstantExpression { Value: bool startInclusive }:
                return RangeConstructor(
                    definition, arguments[0], NullConstant(definition.ElementClrType),
                    $"{(startInclusive ? '[' : '(')})");

            default:
                return null;
        }
    }

    /// <summary>
    /// A range constructor call guarded against inverted bounds: <c>CreateFinite</c> returns
    /// the empty range in that case, while the bare PostgreSQL constructor raises an error.
    /// The constructor's native handling of equal bounds (empty unless both inclusive)
    /// already matches the model semantics.
    /// </summary>
    private SqlExpression GuardedFiniteRangeConstructor(
        IRangeTypeDefinition definition,
        SqlExpression        lower,
        SqlExpression        upper,
        string               bounds
    )
        => sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    sqlExpressionFactory.LessThanOrEqual(lower, upper),
                    RangeConstructor(definition, lower, upper, bounds))
            ],
            sqlExpressionFactory.Constant(definition.EmptyRange, definition.RangeTypeMapping));

    private SqlExpression RangeConstructor(
        IRangeTypeDefinition definition,
        SqlExpression        lower,
        SqlExpression        upper,
        string               bounds
    )
        => sqlExpressionFactory.Function(
            definition.RangeStoreType,
            [ApplyElementMapping(lower, definition), ApplyElementMapping(upper, definition), sqlExpressionFactory.Constant(bounds)],
            nullable: true,
            // A NULL bound makes the range unbounded on that side, it does not null the result.
            argumentsPropagateNullability: [false, false, false],
            definition.RangeClrType,
            definition.RangeTypeMapping);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Strips CLR-only reference conversions (e.g. <c>DateRange</c> → <c>IRange&lt;DateOnly&gt;</c>)
    /// so that type mappings reach the underlying expression.
    /// </summary>
    private static SqlExpression Unwrap(SqlExpression expression)
    {
        while (expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } unary
               && unary.Type.IsAssignableFrom(unary.Operand.Type))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    /// <summary>
    /// Applies the range subtype mapping to a bound or element expression. The provider's
    /// CLR-type default may differ from the range's subtype (e.g. <see cref="DateTime"/>
    /// defaults to <c>timestamptz</c> while the <c>tsrange</c> subtype is <c>timestamp</c>),
    /// so the element store type is resolved explicitly.
    /// </summary>
    private SqlExpression ApplyElementMapping(SqlExpression expression, IRangeTypeDefinition definition)
        => sqlExpressionFactory.ApplyTypeMapping(
            Unwrap(expression),
            typeMappingSource.FindMapping(definition.ElementClrType, definition.ElementStoreType));

    private SqlExpression AsMultirange(SqlExpression range, IRangeTypeDefinition definition)
        => sqlExpressionFactory.Function(
            definition.MultirangeStoreType,
            [range],
            nullable: true,
            argumentsPropagateNullability: [true],
            definition.RangeSetClrType,
            definition.RangeSetTypeMapping);

    private SqlExpression NullConstant(Type elementType)
        => sqlExpressionFactory.Constant(null, typeof(Nullable<>).MakeGenericType(elementType));

    private SqlExpression BoolFunction(string name, SqlExpression argument)
        => sqlExpressionFactory.Function(
            name,
            [argument],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(bool));
}

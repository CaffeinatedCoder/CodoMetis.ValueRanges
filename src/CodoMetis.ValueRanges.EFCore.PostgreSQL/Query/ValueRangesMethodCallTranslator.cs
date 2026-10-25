using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.Core;
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
        nameof(RangeExtensions.Contains), method => !method.GetParameters()[1].ParameterType.IsGenericParameter && !TakesSet(method));

    private static readonly MethodInfo IsContainedByMethod        = GetRangeOperandMethod(nameof(RangeExtensions.IsContainedBy));
    private static readonly MethodInfo OverlapsMethod             = GetRangeOperandMethod(nameof(RangeExtensions.Overlaps));
    private static readonly MethodInfo IsStrictlyLeftOfMethod     = GetRangeOperandMethod(nameof(RangeExtensions.IsStrictlyLeftOf));
    private static readonly MethodInfo IsStrictlyRightOfMethod    = GetRangeOperandMethod(nameof(RangeExtensions.IsStrictlyRightOf));
    private static readonly MethodInfo DoesNotExtendRightOfMethod = GetRangeOperandMethod(nameof(RangeExtensions.DoesNotExtendRightOf));
    private static readonly MethodInfo DoesNotExtendLeftOfMethod  = GetRangeOperandMethod(nameof(RangeExtensions.DoesNotExtendLeftOf));

    // -- RangeExtensions set operations: extension<TRange, T>(TRange) --

    private static readonly MethodInfo IsAdjacentToMethod = GetRangeOperandMethod(nameof(RangeExtensions.IsAdjacentTo));
    private static readonly MethodInfo MergeMethod        = GetExtensionMethod(nameof(RangeExtensions.Merge));
    private static readonly MethodInfo GapMethod          = GetExtensionMethod(nameof(RangeExtensions.Gap));
    private static readonly MethodInfo IntersectMethod    = GetExtensionMethod(nameof(RangeExtensions.Intersect));
    private static readonly MethodInfo UnionMethod        = GetExtensionMethod(nameof(RangeExtensions.Union));
    private static readonly MethodInfo ExceptMethod       = GetExtensionMethod(nameof(RangeExtensions.Except));

    // -- RangeExtensions with a RangeSet operand: the range-receiver half of range × multirange --

    private static readonly HashSet<MethodInfo> SetOperandMethods =
    [
        GetSetOperandMethod(nameof(RangeExtensions.Contains)),
        GetSetOperandMethod(nameof(RangeExtensions.IsContainedBy)),
        GetSetOperandMethod(nameof(RangeExtensions.Overlaps)),
        GetSetOperandMethod(nameof(RangeExtensions.IsStrictlyLeftOf)),
        GetSetOperandMethod(nameof(RangeExtensions.IsStrictlyRightOf)),
        GetSetOperandMethod(nameof(RangeExtensions.DoesNotExtendRightOf)),
        GetSetOperandMethod(nameof(RangeExtensions.DoesNotExtendLeftOf)),
        GetSetOperandMethod(nameof(RangeExtensions.IsAdjacentTo))
    ];

    // -- RangeExtensions state methods: extension<T>(IRange<T>) --

    private static readonly MethodInfo IsEmptyMethod          = GetExtensionMethod(nameof(RangeExtensions.IsEmpty));
    private static readonly MethodInfo IsInfinityMethod       = GetExtensionMethod(nameof(RangeExtensions.IsInfinity));
    private static readonly MethodInfo IsFiniteMethod         = GetExtensionMethod(nameof(RangeExtensions.IsFinite));
    private static readonly MethodInfo IsUnboundedStartMethod = GetExtensionMethod(nameof(RangeExtensions.IsUnboundedStart));
    private static readonly MethodInfo IsUnboundedEndMethod   = GetExtensionMethod(nameof(RangeExtensions.IsUnboundedEnd));

    // -- RangeExtensions bound accessors: extension<T>(IRange<T>) --

    private static readonly MethodInfo LowerBoundMethod          = GetExtensionMethod(nameof(RangeExtensions.LowerBound));
    private static readonly MethodInfo UpperBoundMethod          = GetExtensionMethod(nameof(RangeExtensions.UpperBound));
    private static readonly MethodInfo LowerBoundInclusiveMethod = GetExtensionMethod(nameof(RangeExtensions.LowerBoundInclusive));
    private static readonly MethodInfo UpperBoundInclusiveMethod = GetExtensionMethod(nameof(RangeExtensions.UpperBoundInclusive));

    // -- RangeExtensions.Clamp: extension<T>(IRange<T>), on the types whose bounds read as memory's (#406) --

    private static readonly MethodInfo ClampMethod = GetExtensionMethod(nameof(RangeExtensions.Clamp));

    private static readonly HashSet<Type> ClampsAsSql = [typeof(Int32Range), typeof(Int64Range), typeof(DecimalRange)];

    // -- The calendar factories (Month, Quarter, Year, IsoWeek) over a column, on the types whose every answer
    //    PostgreSQL can hold (#191) --

    private static readonly HashSet<Type> CalendarFactoriesAsSql = [typeof(DateRange)];


    private static MethodInfo GetExtensionMethod(string name, Func<MethodInfo, bool>? filter = null)
        => typeof(RangeExtensions)
          .GetMethods(BindingFlags.Public | BindingFlags.Static)
          .Single(method => method.Name == name && (filter is null || filter(method)));

    private static MethodInfo GetRangeOperandMethod(string name) => GetExtensionMethod(name, method => !TakesSet(method));

    private static MethodInfo GetSetOperandMethod(string name) => GetExtensionMethod(name, TakesSet);

    private static bool TakesSet(MethodInfo method)
        => method.GetParameters() is [_, { ParameterType: { IsGenericType: true } operand }]
        && operand.GetGenericTypeDefinition() == typeof(RangeSet<,>);

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression?                             instance,
        MethodInfo                                 method,
        IReadOnlyList<SqlExpression>               arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        var translation = TranslateCall(instance, method, arguments);

        return translation is not null && translation.Type == typeof(bool)
            ? FalseOverNull(translation, instance is null ? arguments : [instance, .. arguments])
            : translation;
    }

    /// <summary>
    /// A predicate with a NULL range or multirange on either side is <c>false</c>: what EF Core answers
    /// for a predicate over NULL on its own types, and what the in-memory predicates answer for a null
    /// (#350, #355), and so is one with a NULL element: <c>Contains(T)</c> over the optional side of an
    /// outer join or a nullable column read with <c>.Value</c> (#381). Each range, multirange or element
    /// operand becomes <c>x IS NOT NULL AND &lt;predicate&gt;</c>. EF Core's nullability processing drops
    /// the check for a required column and a non-null parameter, and writes <c>x IS NULL OR NOT (…)</c>
    /// under a negation, so a negated predicate keeps the NULL row.
    /// </summary>
    /// <remarks>
    /// <c>CompareTo</c> never reaches this: EF Core turns <c>a.CompareTo(b) op k</c> into a comparison,
    /// which keeps <c>Comparer&lt;T&gt;.Default</c>'s rule for a null (#261). Equality is EF Core's own.
    /// </remarks>
    private SqlExpression FalseOverNull(SqlExpression predicate, IReadOnlyList<SqlExpression> operands)
    {
        var unwrapped = operands.Select(Unwrap).ToList();

        // The element type is the one of the range or multirange the predicate is over.
        IRangeTypeDefinition? ranges = null;
        foreach (var operand in unwrapped)
            if (RangeTypeRegistry.TryGetByClrType(operand.Type, out ranges, out _)) break;

        for (var index = unwrapped.Count - 1; index >= 0; index--)
        {
            var operand = unwrapped[index];

            SqlExpression mapped;
            if (RangeTypeRegistry.TryGetByClrType(operand.Type, out var definition, out var isSet))
                mapped = operand.TypeMapping is null
                    ? sqlExpressionFactory.ApplyTypeMapping(operand, isSet ? definition.RangeSetTypeMapping : definition.RangeTypeMapping)
                    : operand;
            else if (ranges is not null && (Nullable.GetUnderlyingType(operand.Type) ?? operand.Type) == ranges.ElementClrType)
                mapped = operand.TypeMapping is null ? ApplyElementMapping(operand, ranges) : operand;
            else
                continue;

            predicate = sqlExpressionFactory.AndAlso(sqlExpressionFactory.IsNotNull(mapped), predicate);
        }

        return predicate;
    }

    private SqlExpression? TranslateCall(SqlExpression? instance, MethodInfo method, IReadOnlyList<SqlExpression> arguments)
    {
        var declaringType = method.DeclaringType;

        if (declaringType == typeof(RangeExtensions))
            return TranslateRangeExtensionCall(method, arguments);

        if (declaringType is { IsGenericType: true } && declaringType.GetGenericTypeDefinition() == typeof(RangeSet<,>))
            return TranslateRangeSetCall(instance, declaringType, method, method.Name, [.. method.GetParameters().Select(p => p.ParameterType)], arguments);

        // The static form of RangeSet.Contains the interceptor substitutes (RangeSetContains): the set
        // is the first argument, and the method translates as the instance call it stands for.
        if (declaringType == typeof(RangeSetContains) && arguments.Count == 2)
            return TranslateRangeSetCall(arguments[0], method.GetParameters()[0].ParameterType, method, nameof(RangeSet<,>.Contains),
                                         [method.GetParameters()[1].ParameterType], [arguments[1]]);

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

        // The operator with the range on the left, as PostgreSQL declares it: no multirange lift.
        if (SetOperandMethods.Contains(generic))
        {
            var set = sqlExpressionFactory.ApplyTypeMapping(Unwrap(arguments[1]), definition.RangeSetTypeMapping);
            return generic.Name switch
            {
                nameof(RangeExtensions.Contains)             => sqlExpressionFactory.Contains(Range(0), set),
                nameof(RangeExtensions.IsContainedBy)        => sqlExpressionFactory.ContainedBy(Range(0), set),
                nameof(RangeExtensions.Overlaps)             => sqlExpressionFactory.Overlaps(Range(0), set),
                nameof(RangeExtensions.IsStrictlyLeftOf)     => sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeIsStrictlyLeftOf, Range(0), set),
                nameof(RangeExtensions.IsStrictlyRightOf)    => sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeIsStrictlyRightOf, Range(0), set),
                nameof(RangeExtensions.DoesNotExtendRightOf) => sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeDoesNotExtendRightOf, Range(0), set),
                nameof(RangeExtensions.DoesNotExtendLeftOf)  => sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeDoesNotExtendLeftOf, Range(0), set),
                nameof(RangeExtensions.IsAdjacentTo)         => sqlExpressionFactory.MakePostgresBinary(PgExpressionType.RangeIsAdjacentTo, Range(0), set),
                _                                            => throw MissingArm.For(method)
            };
        }

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

        if (generic == MergeMethod)
            return sqlExpressionFactory.Function(
                "range_merge",
                [Range(0), Range(1)],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                definition.RangeClrType,
                definition.RangeTypeMapping);

        if (generic == IntersectMethod)
            return sqlExpressionFactory.MakePostgresBinary(
                PgExpressionType.RangeIntersect, Range(0), Range(1), definition.RangeTypeMapping);

        // PostgreSQL has no gap function, and range - range raises when the result would be two
        // ranges, so the hull and both operands are lifted to multiranges, subtracted, and the
        // single range left (or the empty one) is read back with range_merge.
        if (generic == GapMethod)
        {
            var hull = sqlExpressionFactory.Function(
                "range_merge",
                [Range(0), Range(1)],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                definition.RangeClrType,
                definition.RangeTypeMapping);

            var gaps = sqlExpressionFactory.MakePostgresBinary(
                PgExpressionType.RangeExcept,
                sqlExpressionFactory.MakePostgresBinary(
                    PgExpressionType.RangeExcept,
                    AsMultirange(hull, definition), AsMultirange(Range(0), definition),
                    definition.RangeSetTypeMapping),
                AsMultirange(Range(1), definition),
                definition.RangeSetTypeMapping);

            return sqlExpressionFactory.Function(
                "range_merge",
                [gaps],
                nullable: true,
                argumentsPropagateNullability: [true],
                definition.RangeClrType,
                definition.RangeTypeMapping);
        }

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

        if (generic == LowerBoundMethod)
            return BoundFunction("lower", Range(0), definition);

        if (generic == UpperBoundMethod)
            return UpperBoundFunction(Range(0), definition);

        if (generic == LowerBoundInclusiveMethod)
            return BoolFunction("lower_inc", Range(0));

        if (generic == UpperBoundInclusiveMethod)
            return UpperInclusiveFunction(Range(0), definition);

        // On every other range type Clamp runs in memory (docs/efcore.md#what-runs-where).
        if (generic == ClampMethod)
            return ClampsAsSql.Contains(definition.RangeClrType) ? ClampFunction(Range(0), ApplyElementMapping(arguments[1], definition), definition) : null;

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
        string                       methodName,
        IReadOnlyList<Type>          parameterTypes,
        IReadOnlyList<SqlExpression> arguments
    )
    {
        if (!RangeTypeRegistry.TryGetByClrType(declaringType, out var definition, out var isSet) || !isSet)
            return null;

        SqlExpression Set(SqlExpression expression)
            => sqlExpressionFactory.ApplyTypeMapping(Unwrap(expression), definition.RangeSetTypeMapping);

        SqlExpression Range(SqlExpression expression)
            => sqlExpressionFactory.ApplyTypeMapping(Unwrap(expression), definition.RangeTypeMapping);

        // An operand is mapped as the type its overload declares for it, never as whatever it is not: an
        // overload with any other operand type has no arm here and is refused (#295).
        var rangeInterface = typeof(IRange<>).MakeGenericType(definition.ElementClrType);

        bool IsSet(int index)   => parameterTypes[index] == definition.RangeSetClrType;
        bool IsRange(int index) => parameterTypes[index] == definition.RangeClrType || parameterTypes[index] == rangeInterface;

        // Single-range operands of set operations are lifted to one-element multiranges.
        SqlExpression AsSet(int index)
            => IsSet(index)   ? Set(arguments[index])
             : IsRange(index) ? AsMultirange(Range(arguments[index]), definition)
                              : throw MissingArm.For(method, parameterTypes[index]);

        // Boolean comparisons accept element, range, and multirange operands natively —
        // no lifting required, only the matching type mapping.
        SqlExpression Operand(int index)
            => IsSet(index)                                       ? Set(arguments[index])
             : IsRange(index)                                     ? Range(arguments[index])
             : parameterTypes[index] == definition.ElementClrType ? ApplyElementMapping(arguments[index], definition)
                                                                  : throw MissingArm.For(method, parameterTypes[index]);

        switch (methodName)
        {
            case nameof(RangeSet<,>.Contains) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.Contains(Set(instance), Operand(0));

            case nameof(RangeSet<,>.Overlaps) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.Overlaps(Set(instance), Operand(0));

            case nameof(RangeSet<,>.IsContainedBy) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.ContainedBy(Set(instance), Operand(0));

            case nameof(RangeSet<,>.IsAdjacentTo) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.MakePostgresBinary(
                    PgExpressionType.RangeIsAdjacentTo, Set(instance), Operand(0));

            case nameof(RangeSet<,>.IsStrictlyLeftOf) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.MakePostgresBinary(
                    PgExpressionType.RangeIsStrictlyLeftOf, Set(instance), Operand(0));

            case nameof(RangeSet<,>.IsStrictlyRightOf) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.MakePostgresBinary(
                    PgExpressionType.RangeIsStrictlyRightOf, Set(instance), Operand(0));

            case nameof(RangeSet<,>.DoesNotExtendRightOf) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.MakePostgresBinary(
                    PgExpressionType.RangeDoesNotExtendRightOf, Set(instance), Operand(0));

            case nameof(RangeSet<,>.DoesNotExtendLeftOf) when instance is not null && arguments.Count == 1:
                return sqlExpressionFactory.MakePostgresBinary(
                    PgExpressionType.RangeDoesNotExtendLeftOf, Set(instance), Operand(0));

            case nameof(RangeSet<,>.Merge) when instance is not null && arguments.Count == 0:
                return sqlExpressionFactory.Function(
                    "range_merge",
                    [Set(instance)],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    definition.RangeClrType,
                    definition.RangeTypeMapping);

            case nameof(RangeSet<,>.IsEmpty) when instance is not null && arguments.Count == 0:
                return BoolFunction("isempty", Set(instance));

            case nameof(RangeSet<,>.IsUnboundedStart) when instance is not null && arguments.Count == 0:
                return BoolFunction("lower_inf", Set(instance));

            case nameof(RangeSet<,>.IsUnboundedEnd) when instance is not null && arguments.Count == 0:
                return BoolFunction("upper_inf", Set(instance));

            // Full coverage, not "unbounded at both ends": {(,5],[10,)} satisfies
            // lower_inf AND upper_inf but leaves a hole. PostgreSQL canonicalizes multiranges
            // exactly as the model does, so equality against '{(,)}' is the precise test.
            case nameof(RangeSet<,>.IsInfinity) when instance is not null && arguments.Count == 0:
                return sqlExpressionFactory.Equal(
                    Set(instance),
                    sqlExpressionFactory.Constant(definition.InfiniteRangeSet, definition.RangeSetTypeMapping));

            case nameof(RangeSet<,>.IsFinite) when instance is not null && arguments.Count == 0:
                return sqlExpressionFactory.AndAlso(
                    sqlExpressionFactory.Not(BoolFunction("lower_inf", Set(instance))),
                    sqlExpressionFactory.AndAlso(
                        sqlExpressionFactory.Not(BoolFunction("upper_inf", Set(instance))),
                        sqlExpressionFactory.Not(BoolFunction("isempty", Set(instance)))));

            case nameof(RangeSet<,>.Union) when instance is not null && arguments.Count == 1:
                return SetBinary(PgExpressionType.RangeUnion, Set(instance), AsSet(0));

            case nameof(RangeSet<,>.Intersect) when instance is not null && arguments.Count == 1:
                return SetBinary(PgExpressionType.RangeIntersect, Set(instance), AsSet(0));

            case nameof(RangeSet<,>.Except) when instance is not null && arguments.Count == 1:
                return SetBinary(PgExpressionType.RangeExcept, Set(instance), AsSet(0));

            case nameof(RangeSet<,>.Complement) when instance is not null && arguments.Count == 0:
                return SetBinary(
                    PgExpressionType.RangeExcept,
                    sqlExpressionFactory.Constant(definition.InfiniteRangeSet, definition.RangeSetTypeMapping),
                    Set(instance));

            // The inner complement: the hull of the set minus the set.
            case nameof(RangeSet<,>.Gaps) when instance is not null && arguments.Count == 0:
                return SetBinary(
                    PgExpressionType.RangeExcept,
                    AsMultirange(
                        sqlExpressionFactory.Function(
                            "range_merge",
                            [Set(instance)],
                            nullable: true,
                            argumentsPropagateNullability: [true],
                            definition.RangeClrType,
                            definition.RangeTypeMapping),
                        definition),
                    Set(instance));

            case nameof(RangeSet<,>.Gaps) when instance is not null && arguments.Count == 1:
                return SetBinary(PgExpressionType.RangeExcept, AsSet(0), Set(instance));

            case nameof(RangeSet<,>.LowerBound) when instance is not null && arguments.Count == 0:
                return BoundFunction("lower", Set(instance), definition);

            case nameof(RangeSet<,>.UpperBound) when instance is not null && arguments.Count == 0:
                return UpperBoundFunction(Set(instance), definition);

            case nameof(RangeSet<,>.LowerBoundInclusive) when instance is not null && arguments.Count == 0:
                return BoolFunction("lower_inc", Set(instance));

            case nameof(RangeSet<,>.UpperBoundInclusive) when instance is not null && arguments.Count == 0:
                return UpperInclusiveFunction(Set(instance), definition);

            // User-defined operators arrive as static binary methods.
            case "op_BitwiseOr" when arguments.Count == 2:
                return SetBinary(PgExpressionType.RangeUnion, AsSet(0), AsSet(1));

            case "op_BitwiseAnd" when arguments.Count == 2:
                return SetBinary(PgExpressionType.RangeIntersect, AsSet(0), AsSet(1));

            case "op_Subtraction" when arguments.Count == 2:
                return SetBinary(PgExpressionType.RangeExcept, AsSet(0), AsSet(1));

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
        // On every other range type the calendar factories run in memory (docs/efcore.md#what-runs-where).
        if (method.Name is "Month" or "Quarter" or "Year" or "IsoWeek" && CalendarFactoriesAsSql.Contains(definition.RangeClrType))
            return CalendarFactory(definition, method, arguments);

        if (!definition.SupportsSqlConstruction) return null;

        var element = definition.ElementClrType;

        // Each factory is recognized by its whole signature, and another overload of the same name is
        // refused below rather than translated as one of these (#295). Bound inclusiveness must be a
        // compile-time constant to pick the bounds literal; in practice it always is, because the flags
        // default at the call site.
        switch (method.Name)
        {
            case "CreateFinite" when Takes(method, element, element, typeof(bool), typeof(bool)):
            {
                if (arguments[2] is not SqlConstantExpression { Value: bool startInclusive }
                 || arguments[3] is not SqlConstantExpression { Value: bool endInclusive })
                    return null;

                var lower = ApplyElementMapping(arguments[0], definition);
                var upper = ApplyElementMapping(arguments[1], definition);
                return NullOverNullElement(
                    definition,
                    GuardedFiniteRangeConstructor(definition, lower, upper, $"{(startInclusive ? '[' : '(')}{(endInclusive ? ']' : ')')}"),
                    lower, upper);
            }

            case "CreateUnboundedStart" when Takes(method, element, typeof(bool)):
            {
                if (arguments[1] is not SqlConstantExpression { Value: bool endInclusive })
                    return null;

                var upper = ApplyElementMapping(arguments[0], definition);
                return NullOverNullElement(
                    definition,
                    RangeConstructor(definition, NullConstant(definition.ElementClrType), upper, $"({(endInclusive ? ']' : ')')}"),
                    upper);
            }

            case "CreateUnboundedEnd" when Takes(method, element, typeof(bool)):
            {
                if (arguments[1] is not SqlConstantExpression { Value: bool startInclusive })
                    return null;

                var lower = ApplyElementMapping(arguments[0], definition);
                return NullOverNullElement(
                    definition,
                    RangeConstructor(definition, lower, NullConstant(definition.ElementClrType), $"{(startInclusive ? '[' : '(')})"),
                    lower);
            }

            case "CreateFinite" or "CreateUnboundedStart" or "CreateUnboundedEnd":
                throw MissingArm.For(method);

            default:
                return null;
        }
    }

    private static bool Takes(MethodInfo method, params Type[] parameterTypes)
        => method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameterTypes);

    /// <summary>
    /// A factory over a NULL element is NULL, as EF Core's own value-producing members are over NULL
    /// (#382). Without this, <c>CreateFinite</c> fell into its inverted-bounds guard and gave
    /// <c>empty</c>, and the unbounded factories gave <c>(,)</c>, since PostgreSQL's range constructor
    /// reads a NULL bound as unbounded. An element is NULL on the optional side of an outer join or in
    /// a nullable column read with <c>.Value</c>. EF Core's nullability processing folds the test away
    /// for a required column, a non-null parameter and a constant, so their SQL is unchanged.
    /// </summary>
    private SqlExpression NullOverNullElement(IRangeTypeDefinition definition, SqlExpression range, params SqlExpression[] elements)
        => sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    elements.Select(element => sqlExpressionFactory.IsNull(element)).Aggregate(sqlExpressionFactory.OrElse),
                    sqlExpressionFactory.Constant(null, definition.RangeClrType, definition.RangeTypeMapping))
            ],
            range);

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
    // Calendar factories (#191)
    // -------------------------------------------------------------------------

    /// <summary>
    /// <c>DateRange.Month</c>, <c>Quarter</c>, <c>Year</c> and <c>IsoWeek</c> over a column (#191): the
    /// period's first and last day, <c>daterange(first, last, '[]')</c>, and NULL over a NULL argument
    /// (rule (c)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on PostgreSQL 16 and 18 (#191). The first and the last period of <see cref="DateOnly"/>'s
    /// days are written as constants, taken from memory's factory: the range PostgreSQL builds holds what
    /// Npgsql binds for memory's answer, <c>-infinity</c> for <see cref="DateOnly.MinValue"/> and
    /// <c>infinity</c> for <see cref="DateOnly.MaxValue"/> (or the days themselves, with
    /// <c>Npgsql.DisableDateTimeInfinityConversions</c> set), and PostgreSQL turns a finite <c>]</c> into
    /// <c>)</c>. A date argument at or past those periods takes them too, which covers the <c>-infinity</c>
    /// and <c>infinity</c> a column holds for the extremes: <c>date_trunc</c> keeps an infinity, and the
    /// <c>[)</c> range between two of them is empty. <c>date_trunc</c> takes an explicit <c>timestamp</c>,
    /// since a <c>date</c> resolves to its <c>timestamptz</c> overload, which reads the session time zone.
    /// </para>
    /// <para>
    /// An argument memory refuses raises. <c>make_date</c> raises for a month outside 1 to 12 and for the
    /// year 0. A guard raises for any other year outside 1 to 9999, which <c>make_date</c> accepts, for a week
    /// the week-year does not have, and for the week that ends after 9999-12-31. It casts its message to the
    /// type it stands in for, and PostgreSQL drops it unread when the argument is a constant in range.
    /// <c>to_date(…, 'IYYY-IW')</c> is not used: it rolls week 53 of a 52-week year over to the next year's
    /// week 1.
    /// </para>
    /// </remarks>
    private SqlExpression CalendarFactory(IRangeTypeDefinition definition, MethodInfo method, IReadOnlyList<SqlExpression> arguments)
    {
        var date = definition.ElementClrType;
        var name = $"{definition.RangeClrType.Name}.{method.Name}";

        var operands = method.GetParameters()
                             .Select((parameter, index) => parameter.ParameterType == typeof(int) ? sqlExpressionFactory.ApplyTypeMapping(Unwrap(arguments[index]), IntMapping)
                                                         : parameter.ParameterType == date        ? ApplyElementMapping(arguments[index], definition)
                                                                                                  : throw MissingArm.For(method, parameter.ParameterType))
                             .ToArray();

        SqlExpression Is(int index, int value) => sqlExpressionFactory.Equal(operands[index], Int(value));
        SqlExpression Both(SqlExpression left, SqlExpression right) => sqlExpressionFactory.AndAlso(left, right);

        var range = method.Name switch
        {
            "Month" when Takes(method, date)
                => OfDate(definition, operands[0], "month", Interval(months: 1), DateRange.Month, name),
            "Month" when Takes(method, typeof(int), typeof(int))
                => OfParts(definition, MakeDate(Year(operands[0], name), operands[1], 1), Interval(months: 1),
                           Both(Is(0, 1), Is(1, 1)), Both(Is(0, 9999), Is(1, 12))),
            "Quarter" when Takes(method, date)
                => OfDate(definition, operands[0], "quarter", Interval(months: 3), DateRange.Quarter, name),
            "Quarter" when Takes(method, typeof(int), typeof(int))
                => OfParts(definition, MakeDate(Year(operands[0], name), FirstMonthOf(operands[1]), 1), Interval(months: 3),
                           Both(Is(0, 1), Is(1, 1)), Both(Is(0, 9999), Is(1, 4))),
            "Year" when Takes(method, date)
                => OfDate(definition, operands[0], "year", Interval(years: 1), DateRange.Year, name),
            "Year" when Takes(method, typeof(int))
                => OfParts(definition, MakeDate(Year(operands[0], name), Int(1), 1), Interval(years: 1), Is(0, 1), Is(0, 9999)),
            "IsoWeek" when Takes(method, date)
                => OfDate(definition, operands[0], "week", Interval(weeks: 1), DateRange.IsoWeek, name),
            "IsoWeek" when Takes(method, typeof(int), typeof(int))
                => OfParts(definition, MondayOf(operands[0], operands[1], name, definition), Interval(weeks: 1),
                           Both(Is(0, 1), Is(1, 1)), Both(Is(0, 9999), Is(1, 52)),
                           Raise([$"{name}: week ", Text(operands[1]), " of ", Text(operands[0]), $" ends after {DateOnly.MaxValue:yyyy-MM-dd}"], date, ElementMapping(definition))),
            _ => throw MissingArm.For(method)
        };

        return NullOverNullElement(definition, range, operands);
    }

    /// <summary>
    /// The period holding the date <paramref name="value"/>: <c>date_trunc(unit, value::timestamp)</c> and
    /// that plus <paramref name="length"/> less a day, or memory's first or last period for a date inside or
    /// before or after it. A last period memory refuses (the ISO week past 9999-12-31) raises instead.
    /// </summary>
    private SqlExpression OfDate(
        IRangeTypeDefinition definition, SqlExpression value, string unit, SqlExpression length, Func<DateOnly, DateRange> factory, string name)
    {
        var mapping = ElementMapping(definition);
        var first   = (DateRange.Finite)factory(DateOnly.MinValue);

        // From here on the date is in the last period, or, where memory refuses that period (only the ISO week
        // past 9999-12-31 is refused), in the week starting on the Monday of DateOnly.MaxValue's week.
        var monday =DateOnly.MaxValue.AddDays(-(((int)DateOnly.MaxValue.DayOfWeek + 6) % 7));
        var last   = Refuses(factory, DateOnly.MaxValue) ? null : (DateRange.Finite)factory(DateOnly.MaxValue);
        var from   = sqlExpressionFactory.Constant(last?.Start ?? monday, mapping);

        var start = sqlExpressionFactory.Function(
            "date_trunc", [sqlExpressionFactory.Constant(unit, TextMapping), Timestamp(value)],
            nullable: true, argumentsPropagateNullability: [false, true], typeof(DateTime), TimestampMapping);

        var inFirst = sqlExpressionFactory.LessThanOrEqual(value, sqlExpressionFactory.Constant(first.End, mapping));
        var inLast  = sqlExpressionFactory.GreaterThanOrEqual(value, from);

        var lower = sqlExpressionFactory.Case(
            [
                new CaseWhenClause(inFirst, sqlExpressionFactory.Constant(first.Start, mapping)),
                new CaseWhenClause(inLast, sqlExpressionFactory.Constant(last?.Start ?? monday, mapping))
            ],
            sqlExpressionFactory.Convert(start, definition.ElementClrType, mapping));

        var upper = sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    inLast,
                    last is null
                        ? Raise([$"{name}: the week of ", Text(value), $" ends after {DateOnly.MaxValue:yyyy-MM-dd}"], definition.ElementClrType, mapping)
                        : sqlExpressionFactory.Constant(last.End, mapping)),
                new CaseWhenClause(inFirst, sqlExpressionFactory.Constant(first.End, mapping))
            ],
            LastDay(start, length, definition));

        return CalendarRange(definition, lower, upper);
    }

    /// <summary>
    /// The period starting on <paramref name="first"/>, built from integer arguments, with memory's first
    /// period's start where <paramref name="isFirst"/> and its last period's end where <paramref name="isLast"/>,
    /// or <paramref name="refusal"/> where memory refuses the last period.
    /// </summary>
    private SqlExpression OfParts(
        IRangeTypeDefinition definition, SqlExpression first, SqlExpression length, SqlExpression isFirst, SqlExpression isLast,
        SqlExpression? refusal = null)
    {
        var mapping = ElementMapping(definition);

        var lower = sqlExpressionFactory.Case(
            [new CaseWhenClause(isFirst, sqlExpressionFactory.Constant(DateOnly.MinValue, mapping))],
            first);

        var upper = sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    isLast,
                    refusal ?? sqlExpressionFactory.Constant(DateOnly.MaxValue, mapping))
            ],
            LastDay(Timestamp(first), length, definition));

        return CalendarRange(definition, lower, upper);
    }

    private static bool Refuses(Func<DateOnly, DateRange> factory, DateOnly value)
    {
        try { factory(value); return false; }
        catch (ArgumentOutOfRangeException) { return true; }
    }

    /// <summary>
    /// <c>daterange(first, last, '[]')</c>. Never NULL: PostgreSQL's range constructor reads a NULL bound
    /// as unbounded, and both bounds here are days.
    /// </summary>
    private SqlExpression CalendarRange(IRangeTypeDefinition definition, SqlExpression lower, SqlExpression upper)
        => sqlExpressionFactory.Function(
            definition.RangeStoreType,
            [lower, upper, sqlExpressionFactory.Constant("[]")],
            nullable: false,
            argumentsPropagateNullability: [false, false, false],
            definition.RangeClrType,
            definition.RangeTypeMapping);

    /// <summary><c>(start + length - 1 day)::date</c>, the period's last day.</summary>
    private SqlExpression LastDay(SqlExpression start, SqlExpression length, IRangeTypeDefinition definition)
        => sqlExpressionFactory.Convert(
            sqlExpressionFactory.Add(start, length, TimestampMapping), definition.ElementClrType, ElementMapping(definition));

    /// <summary>
    /// The Monday of ISO week <paramref name="week"/> of <paramref name="year"/>: the Monday on or before
    /// January 4, plus <c>7 * (week - 1)</c> days, raising for a week outside 1 to the year's last,
    /// <c>date_part('week', make_date(year, 12, 28))</c>.
    /// </summary>
    private SqlExpression MondayOf(SqlExpression year, SqlExpression week, string name, IRangeTypeDefinition definition)
    {
        var weeks = sqlExpressionFactory.Convert(
            sqlExpressionFactory.Function(
                "date_part", [sqlExpressionFactory.Constant("week", TextMapping), MakeDate(year, Int(12), 28)],
                nullable: true, argumentsPropagateNullability: [false, true], typeof(double)),
            typeof(int), IntMapping);

        var checkedWeek = sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    sqlExpressionFactory.AndAlso(sqlExpressionFactory.GreaterThanOrEqual(week, Int(1)), sqlExpressionFactory.LessThanOrEqual(week, weeks)),
                    week)
            ],
            Raise([$"{name}: the week-year ", Text(year), " has no week ", Text(week)], typeof(int), IntMapping));

        var firstMonday = sqlExpressionFactory.Convert(
            sqlExpressionFactory.Function(
                "date_trunc", [sqlExpressionFactory.Constant("week", TextMapping), Timestamp(MakeDate(Year(year, name), Int(1), 4))],
                nullable: true, argumentsPropagateNullability: [false, true], typeof(DateTime), TimestampMapping),
            definition.ElementClrType, ElementMapping(definition));

        return sqlExpressionFactory.Add(
            firstMonday,
            sqlExpressionFactory.Multiply(Int(7), sqlExpressionFactory.Subtract(checkedWeek, Int(1))),
            ElementMapping(definition));
    }

    /// <summary>The year, raising for one outside 1 to 9999, which <c>make_date</c> would accept, unless it is 0.</summary>
    private SqlExpression Year(SqlExpression year, string name)
        => sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    sqlExpressionFactory.AndAlso(
                        sqlExpressionFactory.GreaterThanOrEqual(year, Int(DateOnly.MinValue.Year)),
                        sqlExpressionFactory.LessThanOrEqual(year, Int(DateOnly.MaxValue.Year))),
                    year)
            ],
            Raise([$"{name}: the year ", Text(year), $" is outside {DateOnly.MinValue.Year} to {DateOnly.MaxValue.Year}"], typeof(int), IntMapping));

    /// <summary><c>quarter * 3 - 2</c>, a month outside 1 to 12 for every quarter outside 1 to 4, which <c>make_date</c> refuses.</summary>
    private SqlExpression FirstMonthOf(SqlExpression quarter)
        => sqlExpressionFactory.Subtract(sqlExpressionFactory.Multiply(quarter, Int(3)), Int(2));

    private SqlExpression MakeDate(SqlExpression year, SqlExpression month, int day)
        => sqlExpressionFactory.Function(
            "make_date", [year, month, Int(day)],
            nullable: true, argumentsPropagateNullability: [true, true, true], typeof(DateOnly),
            typeMappingSource.FindMapping(typeof(DateOnly), "date"));

    /// <summary><c>'1 month -1 day'::interval</c> and the like: a period's length less the day that ends it.</summary>
    private SqlExpression Interval(int years = 0, int months = 0, int weeks = 0)
    {
        var length = years > 0 ? $"{years} year" : months > 0 ? $"{months} month" : $"{weeks} week";
        return sqlExpressionFactory.Convert(
            sqlExpressionFactory.Constant($"{length} -1 day", TextMapping), typeof(TimeSpan),
            typeMappingSource.FindMapping(typeof(TimeSpan), "interval"));
    }

    /// <summary>
    /// An error raised where the value would be: the message, cast to <paramref name="type"/>, fails as
    /// <c>invalid input syntax</c> (22P02 for <c>integer</c>, 22007 for <c>date</c>) and carries the message.
    /// The message must hold an argument: PostgreSQL folds a constant cast while planning, inside a
    /// <c>CASE</c> branch no row reaches, and raises for every query.
    /// </summary>
    private SqlExpression Raise(IReadOnlyList<object> parts, Type type, RelationalTypeMapping? mapping)
        => sqlExpressionFactory.Convert(
            parts.Select(part => part as SqlExpression ?? sqlExpressionFactory.Constant((string)part, TextMapping))
                 .Aggregate((left, right) => sqlExpressionFactory.Add(left, right, TextMapping)),
            type, mapping);

    private SqlExpression Text(SqlExpression value) => sqlExpressionFactory.Convert(value, typeof(string), TextMapping);

    private SqlExpression Timestamp(SqlExpression value) => sqlExpressionFactory.Convert(value, typeof(DateTime), TimestampMapping);

    private SqlExpression Int(int value) => sqlExpressionFactory.Constant(value, IntMapping);

    private RelationalTypeMapping IntMapping => typeMappingSource.FindMapping(typeof(int))!;

    private RelationalTypeMapping TextMapping => typeMappingSource.FindMapping(typeof(string), "text")!;

    private RelationalTypeMapping TimestampMapping => typeMappingSource.FindMapping(typeof(DateTime), "timestamp without time zone")!;

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
    /// The range subtype mapping. Definition-supplied when the element CLR type is unknown to
    /// the provider; otherwise resolved explicitly by element store type, because the
    /// provider's CLR-type default may differ from the range's subtype (e.g.
    /// <see cref="DateTime"/> defaults to <c>timestamptz</c> while the <c>tsrange</c> subtype
    /// is <c>timestamp</c>).
    /// </summary>
    private RelationalTypeMapping? ElementMapping(IRangeTypeDefinition definition)
        => definition.ElementTypeMapping
        ?? typeMappingSource.FindMapping(definition.ElementClrType, definition.ElementStoreType);

    /// <summary>
    /// Applies the range subtype mapping to a bound or element expression.
    /// </summary>
    private SqlExpression ApplyElementMapping(SqlExpression expression, IRangeTypeDefinition definition)
    {
        var mapped = sqlExpressionFactory.ApplyTypeMapping(Unwrap(expression), ElementMapping(definition));

        return NeedsExplicitCast(mapped, definition)
                   ? sqlExpressionFactory.Convert(mapped, definition.ElementClrType, ElementMapping(definition))
                   : mapped;
    }

    /// <summary>
    /// Whether a constant element operand has to carry an explicit cast to its store type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The range operators are polymorphic — <c>anyrange @&gt; anyelement</c> — and PostgreSQL
    /// resolves those without applying implicit coercions, so the operand's type has to be right
    /// on its own. A bare numeric literal is typed by PostgreSQL as <c>integer</c> (or
    /// <c>numeric</c> if it has a decimal point), which makes
    /// <c>'[1,6)'::int8range @&gt; 25</c> an error — <c>operator does not exist: int8range @&gt;
    /// integer</c> — even though <c>25</c> is a perfectly good <c>bigint</c>. That is what
    /// <c>Int64Range.Contains(25L)</c> used to emit.
    /// </para>
    /// <para>
    /// Only constants need this. A parameter is bound with its own type, and a column carries the
    /// column's. And only a bare numeric literal is at risk: every other element type this package
    /// maps renders self-describing text (<c>DATE '2024-06-15'</c>, <c>TIMESTAMP '…'</c>,
    /// <c>TIME '…'</c>), while <c>integer</c> and <c>numeric</c> literals already arrive as the type
    /// their range's subtype wants. Restricting the cast to the mismatched case keeps the emitted
    /// SQL for every other type unchanged.
    /// </para>
    /// </remarks>
    private bool NeedsExplicitCast(SqlExpression expression, IRangeTypeDefinition definition)
    {
        if (expression is not SqlConstantExpression { Value: not null }) return false;

        // A definition's own element mapping writes a typed literal already ('1.5'::double precision).
        if (definition.ElementTypeMapping is not null) return false;

        var storeType = ElementMapping(definition)?.StoreType;
        if (storeType is null) return false;

        // The store types PostgreSQL assigns to a bare numeric literal, which therefore need no
        // help; anything else numeric does.
        return definition.ElementClrType.IsPrimitive
            && storeType is not ("integer" or "numeric");
    }

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

    /// <summary>
    /// A bound-extraction function (<c>lower</c>/<c>upper</c>) on a range or multirange.
    /// PostgreSQL returns NULL for an empty or correspondingly unbounded operand, hence the
    /// nullable CLR type. That NULL comes from a non-null argument, so the argument must not be
    /// declared as propagating nullability: EF Core would then take the bound of a required column
    /// as never NULL, fold <c>IS NULL</c> to <c>FALSE</c> and drop the <c>COALESCE</c> of a
    /// <c>??</c>, and test a nullable column's bound with the column's own <c>IS NULL</c>.
    /// </summary>
    private SqlExpression BoundFunction(string name, SqlExpression argument, IRangeTypeDefinition definition)
        => sqlExpressionFactory.Function(
            name,
            [argument],
            nullable: true,
            argumentsPropagateNullability: [false],
            typeof(Nullable<>).MakeGenericType(definition.ElementClrType),
            ElementMapping(definition));

    /// <summary>
    /// <c>upper()</c> with discrete-canonicalization compensation. PostgreSQL stores
    /// discrete ranges half-open (<c>[lower, upper)</c>) while the model is closed
    /// (<c>[lower, upper]</c>), so the server's exclusive upper is one step past the
    /// model's inclusive upper: <c>upper(x) - 1</c> restores agreement. NULL propagates
    /// through the subtraction for empty or unbounded operands.
    /// </summary>
    private SqlExpression UpperBoundFunction(SqlExpression argument, IRangeTypeDefinition definition)
    {
        var upper = BoundFunction("upper", argument, definition);
        if (!definition.IsDiscrete) return upper;

        // The step constant must keep its own integer mapping: `date - 1` is date arithmetic
        // in PostgreSQL, and applying the element mapping to the 1 would render it as a date.
        var one = sqlExpressionFactory.Constant(1, typeMappingSource.FindMapping(typeof(int)));
        return sqlExpressionFactory.Subtract(upper, one, ElementMapping(definition));
    }

    /// <summary>
    /// <c>Clamp(value)</c> on <see cref="Int32Range"/>, <see cref="Int64Range"/> and <see cref="DecimalRange"/>
    /// (#406): <c>CASE WHEN x IS NULL OR v IS NULL OR isempty(x) THEN NULL ELSE LEAST(GREATEST(v, lower(x)),
    /// &lt;upper&gt;) END</c>, where <c>&lt;upper&gt;</c> is the bound <see cref="UpperBoundFunction"/> reads:
    /// <c>upper(x) - 1</c> on the discrete types, <c>upper(x)</c> on <c>numrange</c>.
    /// </summary>
    /// <remarks>
    /// Memory's rule: <see langword="null"/> for the empty range, the value itself inside the range, and
    /// otherwise the finite bound it falls outside of, whatever that bound's inclusiveness. An unbounded
    /// side never constrains, and <c>GREATEST</c> and <c>LEAST</c> skip the NULL <c>lower</c> or
    /// <c>upper</c> returns there. They skip a NULL range or value too, so both are tested first, which
    /// makes a NULL operand NULL (rule (c)) rather than the other one. Measured on PostgreSQL 16, the domain
    /// extremes included: <c>upper(x) - 1</c> cannot overflow, since a stored upper bound is at least one
    /// above a value.
    /// </remarks>
    private SqlExpression ClampFunction(SqlExpression range, SqlExpression value, IRangeTypeDefinition definition)
    {
        var type    = typeof(Nullable<>).MakeGenericType(definition.ElementClrType);
        var mapping = ElementMapping(definition);

        SqlExpression Skipping(string name, SqlExpression left, SqlExpression right)
            => sqlExpressionFactory.Function(
                name,
                [left, right],
                nullable: true,
                argumentsPropagateNullability: [false, false],
                type,
                mapping);

        return sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    sqlExpressionFactory.OrElse(
                        sqlExpressionFactory.OrElse(sqlExpressionFactory.IsNull(range), sqlExpressionFactory.IsNull(value)),
                        BoolFunction("isempty", range)),
                    sqlExpressionFactory.Constant(null, type, mapping))
            ],
            Skipping("LEAST", Skipping("GREATEST", value, BoundFunction("lower", range, definition)), UpperBoundFunction(range, definition)));
    }

    /// <summary>
    /// The <c>upper_inc()</c> equivalent. On discrete types the model's canonical form is
    /// closed, so the upper bound is inclusive exactly when a finite upper bound exists —
    /// while PostgreSQL's <c>upper_inc</c> on its half-open canonical form is always false.
    /// </summary>
    private SqlExpression UpperInclusiveFunction(SqlExpression argument, IRangeTypeDefinition definition)
        => definition.IsDiscrete
               ? sqlExpressionFactory.AndAlso(
                   sqlExpressionFactory.Not(BoolFunction("upper_inf", argument)),
                   sqlExpressionFactory.Not(BoolFunction("isempty", argument)))
               : BoolFunction("upper_inc", argument);
}

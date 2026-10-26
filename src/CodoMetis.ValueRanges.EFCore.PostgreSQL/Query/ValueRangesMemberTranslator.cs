using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Translates <c>Length</c> on <see cref="Int32Range"/> and <see cref="Int64Range"/>, the number of
/// integers the range holds, to SQL that answers what memory answers (#405), and <c>Count</c> on a
/// <see cref="RangeSet{TRange,T}"/>, the number of ranges it holds (#484, <see cref="MultirangeCount"/>).
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL stores an integer range as <c>[lower, upper)</c>, so <c>upper(x) - lower(x)</c> is the count
/// (<c>[1,10]</c> is stored as <c>[1,11)</c> and measures 10). The empty range has no bounds and measures 0.
/// An unbounded side gives NULL because <c>upper</c> or <c>lower</c> does, which is memory's
/// <see langword="null"/>, and so does a NULL range (rule (c)).
/// </para>
/// <para>
/// The subtraction is widened first: in <c>bigint</c> for <c>int4range</c>, whose full domain holds
/// 4,294,967,295 integers, and in <c>numeric</c> for <c>int8range</c>, whose count can pass
/// <see cref="long.MaxValue"/>. Memory returns <see langword="null"/> there rather than a wrapped value, so
/// the SQL does too, never an <c>int8</c> overflow error. Measured on PostgreSQL 16 with columns,
/// parameters and inlined constants: the planner drops the arm whose condition folds to false, so a
/// constant too large to count raises nothing either.
/// </para>
/// <para>
/// <c>Length</c> on every other range type runs in memory (<c>docs/efcore.md#what-runs-where</c>). The two
/// members are matched by identity, so a <c>Length</c> on another type is never taken for one of these (#295).
/// </para>
/// </remarks>
internal sealed class ValueRangesMemberTranslator(
    ISqlExpressionFactory        sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource
) : IMemberTranslator
{
    private static readonly PropertyInfo Int32Length = typeof(Int32Range).GetProperty(nameof(Int32Range.Length))!;
    private static readonly PropertyInfo Int64Length = typeof(Int64Range).GetProperty(nameof(Int64Range.Length))!;

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression?                             instance,
        MemberInfo                                 member,
        Type                                       returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (instance is null) return null;

        if (member is PropertyInfo { Name: nameof(RangeSet<,>.Count) } && IsRangeSet(member.DeclaringType))
            return RangeSetCount(instance, member.DeclaringType!);

        var overInt64 = member.HasSameMetadataDefinitionAs(Int64Length);
        if (!overInt64 && !member.HasSameMetadataDefinitionAs(Int32Length)) return null;

        if (!RangeTypeRegistry.TryGetByClrType(overInt64 ? typeof(Int64Range) : typeof(Int32Range), out var definition, out _))
            return null;

        var range   = sqlExpressionFactory.ApplyTypeMapping(Unwrap(instance), definition.RangeTypeMapping);
        var bigint  = typeMappingSource.FindMapping(typeof(long))!;
        var isEmpty = sqlExpressionFactory.Function(
            "isempty",
            [range],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(bool));
        var zero = sqlExpressionFactory.Constant(0L, typeof(long?), bigint);

        if (!overInt64)
        {
            // CASE WHEN isempty(x) THEN 0 ELSE upper(x)::bigint - lower(x)::bigint END
            return sqlExpressionFactory.Case(
                [new CaseWhenClause(isEmpty, zero)],
                sqlExpressionFactory.Subtract(Bound("upper", range, typeof(long?), bigint), Bound("lower", range, typeof(long?), bigint), bigint));
        }

        // CASE WHEN isempty(x) THEN 0
        //      WHEN upper(x)::numeric - lower(x)::numeric <= 9223372036854775807 THEN (upper(x)::numeric - lower(x)::numeric)::bigint
        // END
        var numeric = typeMappingSource.FindMapping(typeof(decimal))!;
        var count   = sqlExpressionFactory.Subtract(Bound("upper", range, typeof(decimal?), numeric), Bound("lower", range, typeof(decimal?), numeric), numeric);

        return sqlExpressionFactory.Case(
            [
                new CaseWhenClause(isEmpty, zero),
                new CaseWhenClause(
                    sqlExpressionFactory.LessThanOrEqual(count, sqlExpressionFactory.Constant((decimal)long.MaxValue, numeric)),
                    sqlExpressionFactory.Convert(count, typeof(long?), bigint))
            ],
            elseResult: null);

        // lower(x) or upper(x), widened. The function is NULL for an empty or unbounded side of a non-null
        // range, so its argument is not declared as propagating nullability (as for LowerBound/UpperBound).
        SqlExpression Bound(string name, SqlExpression argument, Type type, RelationalTypeMapping mapping)
            => sqlExpressionFactory.Convert(
                sqlExpressionFactory.Function(
                    name,
                    [argument],
                    nullable: true,
                    argumentsPropagateNullability: [false],
                    typeof(Nullable<>).MakeGenericType(definition.ElementClrType),
                    typeMappingSource.FindMapping(definition.ElementClrType)),
                type,
                mapping);
    }

    private static bool IsRangeSet(Type? type) => type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(RangeSet<,>);

    /// <summary>
    /// <c>CASE WHEN x IS NULL THEN NULL ELSE &lt;count&gt; END</c>, the count a placeholder that
    /// <see cref="MultirangeCount"/> builds once aliases are final.
    /// </summary>
    /// <remarks>
    /// <c>count(*)</c> over <c>unnest(NULL)</c> is <c>0</c>, while a <c>Count</c> over a NULL column is NULL, as
    /// a value set's <c>cardinality(NULL)</c> is and as EF Core's own members are over NULL. So the placeholder
    /// is reached only with a non-NULL multirange and is declared as never NULL and as propagating nothing,
    /// the shape of <c>NullOverNullOperand</c> (#360). EF Core folds the test away for a required column, a
    /// non-null parameter and a constant.
    /// </remarks>
    private SqlExpression? RangeSetCount(SqlExpression instance, Type rangeSetType)
    {
        if (!RangeTypeRegistry.TryGetByClrType(rangeSetType, out var definition, out var isSet) || !isSet) return null;

        var multirange = sqlExpressionFactory.ApplyTypeMapping(Unwrap(instance), definition.RangeSetTypeMapping);
        var integer    = typeMappingSource.FindMapping(typeof(int))!;

        return sqlExpressionFactory.Case(
            [new CaseWhenClause(sqlExpressionFactory.IsNull(multirange), sqlExpressionFactory.Constant(null, typeof(int?), integer))],
            sqlExpressionFactory.Function(
                MultirangeCount.Placeholder,
                [multirange],
                nullable: false,
                argumentsPropagateNullability: [false],
                typeof(int),
                integer));
    }

    /// <summary>Strips CLR-only reference conversions, so the range mapping reaches the column.</summary>
    private static SqlExpression Unwrap(SqlExpression expression)
    {
        while (expression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } unary
               && unary.Type.IsAssignableFrom(unary.Operand.Type))
        {
            expression = unary.Operand;
        }

        return expression;
    }
}

using System.Reflection;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Translates the value set instance properties to PostgreSQL, for every registered set type:
/// <c>Count</c> → <c>cardinality(column)</c> and <c>IsEmpty</c> →
/// <c>cardinality(column) = 0</c>.
/// </summary>
internal sealed class ValueSetsMemberTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMemberTranslator
{
    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression?                             instance,
        MemberInfo                                 member,
        Type                                       returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (instance is null
            || member.DeclaringType is not { } declaringType
            || !SetTypeRegistry.TryGetByClrType(declaringType, out var definition))
            return null;

        // Union translates to array_cat, whose result is concatenated rather than
        // deduplicated. cardinality would then count shared elements twice — {a,c} unioned
        // with {a,b} is 4 on the server and 3 in memory. PostgreSQL has no array_distinct to
        // wrap it in, so Count over a server-computed union is refused: EF reports the query
        // as untranslatable, which beats returning a number that is quietly too large.
        // IsEmpty stays translatable — a concatenation is empty exactly when both sides are.
        if (member.Name == nameof(StringSet.Count) && DerivesFromUnion(instance))
            return null;

        var set = sqlExpressionFactory.ApplyTypeMapping(instance, definition.SetTypeMapping);

        return member.Name switch
               {
                   nameof(StringSet.Count)   => Cardinality(set),
                   nameof(StringSet.IsEmpty) => sqlExpressionFactory.Equal(Cardinality(set), sqlExpressionFactory.Constant(0)),
                   _                         => null
               };
    }

    /// <summary>
    /// Whether the operand is a union, or something built on one that did not restore canonical
    /// form.
    /// </summary>
    /// <remarks>
    /// Matching only the outermost call let the refusal be side-stepped by anything wrapping the
    /// union. <c>array_remove</c> is the case that exists today: it preserves whatever canonical
    /// form its input had, which for a concatenation is none, so
    /// <c>Tags.Union(Other).Remove(x).Count</c> reached <c>cardinality</c> and counted shared
    /// elements twice — 4 against live PostgreSQL where the in-memory expression is 3.
    /// Canonicality-preserving wrappers therefore have to be looked through rather than treated
    /// as a fresh start.
    /// </remarks>
    private static bool DerivesFromUnion(SqlExpression expression) => expression switch
    {
        SqlFunctionExpression { Name: "array_cat" } => true,

        SqlFunctionExpression { Name: "array_remove", Arguments.Count: > 0 } remove
            => DerivesFromUnion(remove.Arguments[0]),

        _ => false
    };

    private SqlExpression Cardinality(SqlExpression set)
        => sqlExpressionFactory.Function(
            "cardinality",
            [set],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(int));
}

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

        var set = sqlExpressionFactory.ApplyTypeMapping(instance, definition.SetTypeMapping);

        // Recognized by name and type, so a member of the same name answering another type is not taken
        // for one of these (#295).
        return member switch
               {
                   PropertyInfo { Name: nameof(StringSet.Count), PropertyType: var type } when type == typeof(int)    => Cardinality(set),
                   PropertyInfo { Name: nameof(StringSet.IsEmpty), PropertyType: var type } when type == typeof(bool) => sqlExpressionFactory.Equal(Cardinality(set), sqlExpressionFactory.Constant(0)),
                   _                                                                                              => null
               };
    }

    private SqlExpression Cardinality(SqlExpression set)
        => sqlExpressionFactory.Function(
            "cardinality",
            [set],
            nullable: true,
            argumentsPropagateNullability: [true],
            typeof(int));
}

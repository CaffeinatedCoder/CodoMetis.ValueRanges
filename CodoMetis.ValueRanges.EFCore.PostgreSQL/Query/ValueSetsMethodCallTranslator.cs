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
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// Translates the value set algebra to PostgreSQL array operators, for every registered set
/// type:
/// <list type="bullet">
///   <item><c>Contains</c> — containment <c>column @&gt; ARRAY[value]</c>, unconditionally,
///   so a GIN index can always serve it (<c>= ANY</c> cannot).</item>
///   <item><c>Overlaps</c> / <c>IsSubsetOf</c> / <c>IsSupersetOf</c> — <c>&amp;&amp;</c> /
///   <c>&lt;@</c> / <c>@&gt;</c>; all three are order- and multiplicity-insensitive, so they
///   remain correct even against non-canonical rows written by other tools.</item>
///   <item><c>Union</c> — <c>array_cat</c> (the function form of <c>||</c>).</item>
///   <item><c>Count</c> / <c>IsEmpty</c> — <c>cardinality</c>.</item>
/// </list>
/// Set equality (<c>==</c>) needs no translator: EF translates scalar-mapped equality to
/// <c>=</c> itself, which matches set equality exactly when all writers canonicalize.
/// </summary>
internal sealed class ValueSetsMethodCallTranslator(
    NpgsqlSqlExpressionFactory   sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource
) : IMethodCallTranslator
{
    private static readonly FrozenSetLookup KnownMethods = new(
        typeof(ValueSetExtensions)
           .GetMethods(BindingFlags.Public | BindingFlags.Static)
           .Select(method => method.Name)
           .ToHashSet());

    private readonly struct FrozenSetLookup(HashSet<string> names)
    {
        public bool Contains(string name) => names.Contains(name);
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression?                             instance,
        MethodInfo                                 method,
        IReadOnlyList<SqlExpression>               arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        if (method.DeclaringType != typeof(ValueSetExtensions) || !KnownMethods.Contains(method.Name))
            return null;

        if (!TryResolveDefinition(method, arguments, out var definition))
            return null;

        SqlExpression Set(int index)
            => sqlExpressionFactory.ApplyTypeMapping(Unwrap(arguments[index]), definition.SetTypeMapping);

        switch (method.Name)
        {
            case nameof(ValueSetExtensions.Contains) when arguments.Count == 2:
                return sqlExpressionFactory.Contains(Set(0), SingletonArray(arguments[1], definition));

            case nameof(ValueSetExtensions.Overlaps) when arguments.Count == 2:
                return sqlExpressionFactory.Overlaps(Set(0), Set(1));

            case nameof(ValueSetExtensions.IsSubsetOf) when arguments.Count == 2:
                return sqlExpressionFactory.ContainedBy(Set(0), Set(1));

            case nameof(ValueSetExtensions.IsSupersetOf) when arguments.Count == 2:
                return sqlExpressionFactory.Contains(Set(0), Set(1));

            case nameof(ValueSetExtensions.Union) when arguments.Count == 2:
                // The server-side result is unsorted and undeduplicated — fine inside
                // predicates (the operators above ignore both), and a materialized result
                // re-canonicalizes on read.
                return sqlExpressionFactory.Function(
                    "array_cat",
                    [Set(0), Set(1)],
                    nullable: true,
                    argumentsPropagateNullability: [true, true],
                    definition.SetClrType,
                    definition.SetTypeMapping);

            default:
                return null;
        }
    }

    private static bool TryResolveDefinition(
        MethodInfo                                  method,
        IReadOnlyList<SqlExpression>                arguments,
        [NotNullWhen(true)] out ISetTypeDefinition? definition
    )
    {
        foreach (var argument in arguments)
        {
            if (SetTypeRegistry.TryGetByClrType(Unwrap(argument).Type, out definition))
                return true;
        }

        // Operands statically typed as IValueSet<T> carry no concrete set type —
        // fall back to the method's element type argument (always the last one).
        if (method.IsGenericMethod)
            return SetTypeRegistry.TryGetByElementType(method.GetGenericArguments()[^1], out definition);

        definition = null;
        return false;
    }

    /// <summary>
    /// Wraps an element expression as a one-element array — <c>ARRAY[value]</c> — carrying the
    /// set's store type so the containment operand renders as e.g. <c>ARRAY[@p]::text[]</c>.
    /// The element expression takes the definition's element mapping: for wrapper elements the
    /// definition-supplied converting mapping makes a bare element parameter bind as the
    /// primitive store type.
    /// </summary>
    private SqlExpression SingletonArray(SqlExpression element, ISetTypeDefinition definition)
        => new PgNewArrayExpression(
            [ApplyElementMapping(element, definition)],
            definition.ElementClrType.MakeArrayType(),
            definition.SetTypeMapping);

    private SqlExpression ApplyElementMapping(SqlExpression expression, ISetTypeDefinition definition)
        => sqlExpressionFactory.ApplyTypeMapping(Unwrap(expression), ElementMapping(definition));

    private RelationalTypeMapping? ElementMapping(ISetTypeDefinition definition)
        => definition.ElementTypeMapping
        ?? typeMappingSource.FindMapping(definition.ElementClrType, definition.ElementStoreType);

    /// <summary>
    /// Strips CLR-only reference conversions (e.g. <c>StringSet</c> → <c>IValueSet&lt;string&gt;</c>)
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
}

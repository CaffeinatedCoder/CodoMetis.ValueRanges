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
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
// PgNewArrayExpression is Npgsql-internal (EF1001): building an array literal in the SQL tree has
// no public equivalent, and this translator only exists to build one. Acknowledged here, at the
// usage, rather than suppressed repo-wide.
#pragma warning disable EF1001


/// <summary>
/// Translates the value set algebra to PostgreSQL array operators, for every registered set
/// type:
/// <list type="bullet">
///   <item><c>Contains</c> — containment <c>column @&gt; ARRAY[value]</c>, unconditionally,
///   so a GIN index can always serve it (<c>= ANY</c> cannot).</item>
///   <item><c>Overlaps</c> / <c>IsSubsetOf</c> / <c>IsSupersetOf</c> — <c>&amp;&amp;</c> /
///   <c>&lt;@</c> / <c>@&gt;</c>; all three are order- and multiplicity-insensitive, so they
///   remain correct even against non-canonical rows written by other tools.</item>
///   <item><c>IsProperSubsetOf</c> / <c>IsProperSupersetOf</c> — the same operators paired with
///   the negated converse (<c>a &lt;@ b AND NOT a @&gt; b</c>), which keeps them
///   multiplicity-insensitive where <c>&lt;&gt;</c> would not.</item>
///   <item><c>Remove</c> — <c>array_remove</c>, which <em>preserves</em> canonical form rather
///   than establishing it: a sorted, deduplicated array stays sorted and deduplicated once an
///   element is dropped, but a concatenation stays a concatenation. It therefore composes safely
///   with <c>Count</c> and equality only when its operand was already canonical — see the Union
///   note below. <c>Add</c> has no counterpart: PostgreSQL cannot insert at a sorted
///   position.</item>
///   <item><c>Union</c> — <c>array_cat</c> (the function form of <c>||</c>).</item>
///   <item><c>Count</c> / <c>IsEmpty</c> — <c>cardinality</c>.</item>
/// </list>
/// Set equality (<c>==</c>) needs no translator: EF translates scalar-mapped equality to
/// <c>=</c> itself, which matches set equality exactly when all writers canonicalize.
/// <para>
/// <b>Composing on a translated <c>Union</c>.</b> <c>array_cat</c> concatenates rather than
/// canonicalizing, which is invisible to the operators above (all order- and
/// multiplicity-insensitive) and to materialization (reads route through <c>From</c>), but not
/// to everything: equality, <c>Count</c>, grouping, ordering, <c>DISTINCT</c> and an
/// <c>ExecuteUpdate</c> value all see the duplicates and the operand order — including through a
/// wrapping <c>Remove</c>, which preserves the concatenation rather than canonicalizing it. EF
/// emits several of those itself (the <c>=</c> of <c>Tags.Union(x) == y</c>, a <c>GROUP BY</c>
/// key), so no translator can intercept them; <see cref="ValueSetSqlGuard"/> refuses them on the
/// final SQL instead, and <c>ValueSetsMemberTranslator</c> declines <c>Count</c> so that a
/// projection falls back to the client. Canonicalizing server-side is not an option: PostgreSQL
/// has no array-distinct function (verified against 18.4), so it needs a
/// <c>SELECT DISTINCT … ORDER BY</c> subquery, and the ordering could not match CLR canonical
/// order anyway (<c>text</c> orders by database collation, not ordinal; <c>uuid</c> orders
/// byte-wise where <see cref="Guid.CompareTo(Guid)"/> orders field-wise). Materialize the
/// union and compare in memory.
/// </para>
/// </summary>
internal sealed class ValueSetsMethodCallTranslator(
    NpgsqlSqlExpressionFactory   sqlExpressionFactory,
    IRelationalTypeMappingSource typeMappingSource
) : IMethodCallTranslator
{
    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression?                             instance,
        MethodInfo                                 method,
        IReadOnlyList<SqlExpression>               arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger
    )
    {
        // The switch below is the name filter: anything it does not recognize falls through to
        // null, so no separate set of known names is needed.
        if (method.DeclaringType != typeof(ValueSetExtensions))
            return null;

        if (!TryResolveDefinition(arguments, out var definition))
            return null;

        var translation = TranslateCall(method, arguments, definition);

        return translation is not null && translation.Type == typeof(bool)
            ? FalseOverNull(translation, arguments)
            : translation;
    }

    /// <summary>
    /// A predicate with a NULL value set on either side is <c>false</c>, as EF Core answers
    /// <c>!names.Contains(x)</c> over a NULL <c>string[]</c> (#355): each set operand becomes
    /// <c>x IS NOT NULL AND &lt;predicate&gt;</c>, which EF Core drops for a required column and turns
    /// into <c>x IS NULL OR NOT (…)</c> under a negation. In memory the set predicates throw for a null
    /// set, so the database answer is EF Core's convention rather than a copy of a client answer.
    /// </summary>
    private SqlExpression FalseOverNull(SqlExpression predicate, IReadOnlyList<SqlExpression> operands)
    {
        for (var index = operands.Count - 1; index >= 0; index--)
        {
            var operand = Unwrap(operands[index]);
            if (!SetTypeRegistry.TryGetByClrType(operand.Type, out var definition))
                continue;

            var mapped = operand.TypeMapping is null
                ? sqlExpressionFactory.ApplyTypeMapping(operand, definition.SetTypeMapping)
                : operand;
            predicate = sqlExpressionFactory.AndAlso(sqlExpressionFactory.IsNotNull(mapped), predicate);
        }

        return predicate;
    }

    private SqlExpression? TranslateCall(MethodInfo method, IReadOnlyList<SqlExpression> arguments, ISetTypeDefinition definition)
    {
        // An operand is mapped as the type its overload declares for it: the set (as itself, or as the
        // IValueSet<T> or IValueSetFactory<TSet, T> an extension block receives) or the element. An overload
        // with any other operand type has no arm here and is refused (#295).
        var parameters = method.GetParameters();
        Type[] setTypes =
        [
            definition.SetClrType,
            typeof(IValueSet<>).MakeGenericType(definition.ElementClrType),
            typeof(IValueSetFactory<,>).MakeGenericType(definition.SetClrType, definition.ElementClrType)
        ];

        SqlExpression Set(int index)
            => setTypes.Contains(parameters[index].ParameterType)
                   ? sqlExpressionFactory.ApplyTypeMapping(Unwrap(arguments[index]), definition.SetTypeMapping)
                   : throw MissingArm.For(method, parameters[index].ParameterType);

        SqlExpression Element(int index)
            => parameters[index].ParameterType == definition.ElementClrType
                   ? arguments[index]
                   : throw MissingArm.For(method, parameters[index].ParameterType);

        switch (method.Name)
        {
            case nameof(ValueSetExtensions.Contains) when arguments.Count == 2:
                return sqlExpressionFactory.Contains(Set(0), SingletonArray(Element(1), definition));

            case nameof(ValueSetExtensions.Overlaps) when arguments.Count == 2:
                return sqlExpressionFactory.Overlaps(Set(0), Set(1));

            case nameof(ValueSetExtensions.IsSubsetOf) when arguments.Count == 2:
                return sqlExpressionFactory.ContainedBy(Set(0), Set(1));

            case nameof(ValueSetExtensions.IsSupersetOf) when arguments.Count == 2:
                return sqlExpressionFactory.Contains(Set(0), Set(1));

            // Proper containment as `<@ AND NOT @>` rather than `<@ AND <>`: both halves stay
            // order- and multiplicity-insensitive, so these keep working against a row some
            // other writer left non-canonical, which `<>` would not.
            case nameof(ValueSetExtensions.IsProperSubsetOf) when arguments.Count == 2:
                return sqlExpressionFactory.AndAlso(
                    sqlExpressionFactory.ContainedBy(Set(0), Set(1)),
                    sqlExpressionFactory.Not(sqlExpressionFactory.Contains(Set(0), Set(1))));

            case nameof(ValueSetExtensions.IsProperSupersetOf) when arguments.Count == 2:
                return sqlExpressionFactory.AndAlso(
                    sqlExpressionFactory.Contains(Set(0), Set(1)),
                    sqlExpressionFactory.Not(sqlExpressionFactory.ContainedBy(Set(0), Set(1))));

            // array_remove preserves canonical form — a sorted, deduplicated array stays sorted
            // and deduplicated once an element is dropped — so unlike Union this composes
            // safely with Count and equality.
            case nameof(ValueSetExtensions.Remove) when arguments.Count == 2:
                return NullOverNullOperand(definition, "array_remove", Set(0), ApplyElementMapping(Element(1), definition));

            // The server-side result is unsorted and undeduplicated — fine inside predicates (the
            // operators above ignore both), and a materialized result re-canonicalizes on read.
            // Emitted under a placeholder name that only ValueSetSqlGuard renames to array_cat, so
            // the guard cannot be skipped silently.
            case nameof(ValueSetExtensions.Union) when arguments.Count == 2:
                return NullOverNullOperand(definition, ValueSetSqlGuard.UnguardedUnion, Set(0), Set(1));

            default:
                return null;
        }
    }

    /// <summary>
    /// <c>Union</c> and <c>Remove</c> over a NULL operand are NULL, as EF Core's own value-producing
    /// members are over NULL (#360): <c>CASE WHEN a IS NULL OR b IS NULL THEN NULL ELSE f(a, b) END</c>.
    /// Neither function is strict, so the bare call answered with the other operand —
    /// <c>array_cat(NULL, '{a}')</c> and <c>array_remove('{a}', NULL)</c> are both <c>{a}</c> — in a
    /// projection, while a filter's <c>== null</c>, following the declaration, selected the row. An
    /// operand is NULL in a nullable column, on the optional side of an outer join, or as a
    /// <see langword="null"/> parameter. EF Core's nullability processing folds the test away for a
    /// required column, a non-null parameter and a constant, so their SQL is the bare call.
    /// </summary>
    /// <remarks>
    /// The function is reached only with both operands non-NULL, so it is declared as never NULL
    /// and as propagating nothing: a non-strict function declared as propagating would let EF Core
    /// rewrite <c>f(a, b) IS NULL</c> to <c>a IS NULL OR b IS NULL</c>, which is the disagreement
    /// this fixes. <see cref="ValueSetSqlGuard"/> follows a union through the <c>CASE</c>.
    /// </remarks>
    private SqlExpression NullOverNullOperand(ISetTypeDefinition definition, string function, SqlExpression left, SqlExpression right)
        => sqlExpressionFactory.Case(
            [
                new CaseWhenClause(
                    sqlExpressionFactory.OrElse(sqlExpressionFactory.IsNull(left), sqlExpressionFactory.IsNull(right)),
                    sqlExpressionFactory.Constant(null, definition.SetClrType, definition.SetTypeMapping))
            ],
            sqlExpressionFactory.Function(
                function,
                [left, right],
                nullable: false,
                argumentsPropagateNullability: [false, false],
                definition.SetClrType,
                definition.SetTypeMapping));

    /// <summary>
    /// Resolves the set type definition from the operands, by concrete set CLR type only.
    /// </summary>
    /// <remarks>
    /// There is deliberately no fallback to the method's element type argument. Every method this
    /// translator handles is declared on <see cref="ValueSetExtensions"/> and constrained
    /// <c>where TSet : class, IValueSetFactory&lt;TSet, T&gt;, IValueSet&lt;T&gt;</c>, which
    /// <c>IValueSet&lt;T&gt;</c> cannot satisfy with itself as <c>TSet</c> — so <c>TSet</c> always
    /// binds to a concrete set type and the loop below always resolves. An earlier element-type
    /// fallback existed for "operands statically typed as <c>IValueSet&lt;T&gt;</c>", a shape the
    /// constraint makes unreachable; it never ran, and the cast form it described
    /// (<c>((IValueSet&lt;string&gt;)x).Contains(…)</c>) fails in EF before reaching any
    /// translator.
    /// </remarks>
    private static bool TryResolveDefinition(
        IReadOnlyList<SqlExpression>                arguments,
        [NotNullWhen(true)] out ISetTypeDefinition? definition
    )
    {
        foreach (var argument in arguments)
        {
            if (SetTypeRegistry.TryGetByClrType(Unwrap(argument).Type, out definition))
                return true;
        }

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

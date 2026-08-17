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
/// to everything: <c>Count</c> over a union is refused by <c>ValueSetsMemberTranslator</c>
/// rather than counting duplicates — including through a wrapping <c>Remove</c>, which
/// preserves the concatenation rather than canonicalizing it — and <b>equality over a union is
/// wrong and cannot be intercepted</b> — EF emits the <c>=</c> itself, so
/// <c>Tags.Union(x) == y</c> compares a concatenated array against a canonical one and is false
/// even where the in-memory union equals <c>y</c>. The same applies to
/// <c>Tags.Union(x).Remove(e) == y</c>, for the same reason and with the same remedy. Canonicalizing server-side is not an option: PostgreSQL has no
/// array-distinct function (verified against 18.4), so it needs a
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
                return sqlExpressionFactory.Function(
                    "array_remove",
                    [Set(0), ApplyElementMapping(arguments[1], definition)],
                    nullable: true,
                    argumentsPropagateNullability: [true, true],
                    definition.SetClrType,
                    definition.SetTypeMapping);

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

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every public query member (<see cref="QueryMembers"/>), over every discovered range and value set type,
/// handed to EF Core's translators with one operand of each declared parameter type. A translation must
/// map each operand as the type it is declared as, and answer the member's own type (#295).
/// </summary>
/// <remarks>
/// <para>
/// The translators recognise an overload by its name and its argument count, and picked an operand's
/// mapping from what the operand was not: anything that was neither the set nor the element was mapped
/// as a range. That is the receiver-shaped dispatch trap one layer up, and the shape of
/// dotnet/efcore#39329. An overload added later with another operand type, a <c>RangeSet.Contains</c>
/// over a value set, would have been translated as a range operator over a value mapped as a range,
/// without an error, and no translation test calls an overload nobody has written a test for yet.
/// </para>
/// <para>
/// So the sweep starts from the public API rather than from the translators, as the translation-support
/// inventory does, and needs no database: it asks the providers the services of a real context resolve,
/// which is every translator EF Core, Npgsql and the plugins register, in their real order. A member
/// they decline is not judged here (whether it should translate is <c>TranslationSupportProbeTests</c>'
/// question). A translator that throws is a failure, unless it is the refusal the translators throw for
/// an overload they have no arm for, which names the overload; that one still fails the sweep, because
/// an overload without an arm is the change this sweep exists to catch.
/// </para>
/// </remarks>
[TestClass]
public sealed class TranslatorDispatchSweepTests
{
    private sealed class SweepContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseNpgsql(
                "Host=localhost;Database=conventions;Username=postgres",
                npgsql => npgsql.UseValueRangesNodaTime());
    }

    private const string OperandPrefix = "operand";

    [TestMethod]
    public void EveryQueryMember_IsTranslatedOverTheOperandTypesItDeclares()
    {
        using var context = new SweepContext();
        var model      = context.Model;
        var methods    = context.GetService<IMethodCallTranslatorProvider>();
        var members    = context.GetService<IMemberTranslatorProvider>();
        var aggregates = context.GetService<IAggregateMethodCallTranslatorProvider>();
        var factory    = context.GetService<ISqlExpressionFactory>();
        var logger     = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>();

        var failures   = new List<string>();
        var translated = new HashSet<Type>();

        var types = RangeProbes.AllRangeTypes().Select(t => (Value: t.Range, t.Element, RangeSet: (Type?)typeof(RangeSet<,>).MakeGenericType(t.Range, t.Element)))
                               .Concat(SetProbes.AllSetTypes().Select(t => (Value: t.Set, t.Element, RangeSet: (Type?)null)))
                               .ToList();

        foreach (var (value, element, rangeSet) in types)
        {
            foreach (var member in QueryMembers.All)
            {
                if (QueryMembers.Close(member, value, element, rangeSet) is not { } closed) continue;

                var cell = $"{member.Key} on {QueryMembers.Name(value)}";
                try
                {
                    if (Translate(closed) is not { } call) continue;

                    translated.Add(member.Declaring);
                    Judge(cell, call.Translation, call.Operands, call.Returns, failures);
                }
                catch (Exception e)
                {
                    failures.Add($"{cell}: the translators threw {e.GetType().Name}: {e.Message.Split('\n')[0]}");
                }
            }
        }

        // A sweep that reaches no translator passes for the wrong reason: if discovery or the operands stop
        // reaching one of the plugin's translators, this says so instead of quietly going green.
        Type[] expected = [typeof(RangeExtensions), typeof(RangeSet<,>), typeof(ValueSetExtensions), typeof(RangeAggregateExtensions), typeof(DateRange), typeof(StringSet)];
        var unreached = expected.Where(type => !translated.Contains(type)).Select(QueryMembers.Name).ToList();
        Assert.AreEqual(0, unreached.Count,
                        $"No member of {string.Join(", ", unreached)} was translated. Has discovery, the context or the operand construction changed?");

        Assert.AreEqual(0, failures.Count,
                        "A translation must map each operand as the type its overload declares, and answer the member's own type:"
                      + Environment.NewLine + string.Join(Environment.NewLine, failures));

        // One translation of a closed member: the SQL, the declared type of each named operand, and the type
        // the member answers.
        (SqlExpression Translation, IReadOnlyDictionary<string, Type> Operands, Type Returns)? Translate(MemberInfo closed)
        {
            var operands = new Dictionary<string, Type>(StringComparer.Ordinal);

            SqlExpression Operand(Type type)
            {
                // The factories pick their bounds literal from constant inclusiveness flags, as a call site
                // always passes them.
                if (type == typeof(bool)) return factory.Constant(true);

                var name = $"{OperandPrefix}{operands.Count}";
                operands[name] = type;
                return new SqlParameterExpression(name, type, typeMapping: null);
            }

            switch (closed)
            {
                case PropertyInfo { GetMethod: { } getter } property when property.GetIndexParameters().Length == 0:
                {
                    var instance = Operand(property.DeclaringType!);
                    return members.Translate(instance, property, property.PropertyType, logger) is { } sql ? (sql, operands, property.PropertyType) : null;
                }

                case PropertyInfo { GetMethod: { } indexer }:
                    return Translate(indexer);

                case MethodInfo method when IsAggregate(method):
                {
                    var selector = Operand(method.GetParameters()[0].ParameterType.GetGenericArguments()[0]);
                    return aggregates.Translate(model, method, new EnumerableExpression(selector), [], logger) is { } sql ? (sql, operands, method.ReturnType) : null;
                }

                case MethodInfo method:
                {
                    var instance  = method.IsStatic ? null : Operand(method.DeclaringType!);
                    var arguments = method.GetParameters().Select(parameter => Operand(parameter.ParameterType)).ToList();
                    return methods.Translate(model, instance, method, arguments, logger) is { } sql ? (sql, operands, method.ReturnType) : null;
                }

                default:
                    return null;
            }
        }
    }

    /// <summary>An aggregate, which EF Core hands its own translators: a static method over one sequence.</summary>
    private static bool IsAggregate(MethodInfo method) =>
        method is { IsStatic: true } && method.GetParameters() is [{ ParameterType: { IsGenericType: true } input }]
     && input.GetGenericTypeDefinition() == typeof(IEnumerable<>) && method.Name.Contains("Agg", StringComparison.Ordinal);

    private static void Judge(string cell, SqlExpression translation, IReadOnlyDictionary<string, Type> operands, Type returns, List<string> failures)
    {
        foreach (var parameter in Operands.In(translation))
        {
            if (!operands.TryGetValue(parameter.Name, out var declared) || parameter.TypeMapping is not { } mapping) continue;
            if (!Compatible(declared, mapping.ClrType))
                failures.Add($"{cell}: an operand declared {QueryMembers.Name(declared)} is mapped as {mapping.StoreType} ({QueryMembers.Name(mapping.ClrType)})");
        }

        if (!Compatible(returns, translation.Type))
            failures.Add($"{cell}: the translation answers {QueryMembers.Name(translation.Type)} where the member answers {QueryMembers.Name(returns)}");
    }

    /// <summary>The same type, or one assignable to the other, ignoring <see cref="Nullable{T}"/>.</summary>
    private static bool Compatible(Type declared, Type actual)
    {
        declared = Nullable.GetUnderlyingType(declared) ?? declared;
        actual   = Nullable.GetUnderlyingType(actual) ?? actual;
        return declared == actual || declared.IsAssignableFrom(actual) || actual.IsAssignableFrom(declared);
    }

    /// <summary>The sweep's named operands, wherever a translation put them.</summary>
    private sealed class Operands : ExpressionVisitor
    {
        private readonly List<SqlParameterExpression> _found = [];

        public static IReadOnlyList<SqlParameterExpression> In(SqlExpression translation)
        {
            var visitor = new Operands();
            visitor.Visit(translation);
            return visitor._found;
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is SqlParameterExpression parameter && parameter.Name.StartsWith(OperandPrefix, StringComparison.Ordinal))
                _found.Add(parameter);

            return base.VisitExtension(node);
        }
    }
}

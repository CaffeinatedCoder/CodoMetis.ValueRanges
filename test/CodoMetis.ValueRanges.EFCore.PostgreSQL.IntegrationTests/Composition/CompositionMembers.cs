using System.Linq.Expressions;
using System.Reflection;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;

/// <summary>An operand a call can be given: its readable name, the expression, and whether it can be NULL.</summary>
internal sealed record Operand(string Name, Expression Value, bool Nullable);

/// <summary>One call of a library member with its operands spliced in, written as C#.</summary>
internal sealed record Call(string Name, Expression Body)
{
    public bool Predicate => Body.Type == typeof(bool);
}

/// <summary>
/// The members of the library a query can call, discovered rather than listed, closed over one family:
/// <see cref="DateRange"/>, its <see cref="RangeSet{TRange,T}"/> and <see cref="StringSet"/>. The
/// composition sweeps vary how a call is composed (the join, the operands' nullability, the quantifier,
/// the execution, the aggregate), not the type; the type axis is the other sweeps'.
/// </summary>
/// <remarks>
/// Every public method of <see cref="RangeExtensions"/>, of <see cref="RangeSet{TRange,T}"/> (with its
/// operators) and of <see cref="ValueSetExtensions"/>, plus the value set's <c>Count</c> and <c>IsEmpty</c>
/// properties. A member EF Core cannot translate is found by the sweep (it throws "could not be
/// translated") and counted, not judged.
/// </remarks>
internal static class CompositionMembers
{
    public static readonly Type RangeType    = typeof(DateRange);
    public static readonly Type ElementType  = typeof(DateOnly);
    public static readonly Type SetType      = typeof(RangeSet<DateRange, DateOnly>);
    public static readonly Type ValueSetType = typeof(StringSet);

    private static readonly string[] NotQueryable =
        [nameof(Equals), nameof(GetHashCode), nameof(ToString), "GetEnumerator", "CompareTo", "Parse", "TryParse", "Create", "From", "op_Equality", "op_Inequality"];

    /// <summary>The members, closed over the family; a static one takes its receiver first.</summary>
    public static IReadOnlyList<MethodInfo> Methods { get; } = Discover();

    private static List<MethodInfo> Discover()
    {
        var methods = new List<MethodInfo>();

        foreach (var (declaring, one, two) in new[]
                 {
                     (typeof(RangeExtensions), new[] { ElementType }, new[] { RangeType, ElementType }),
                     (typeof(ValueSetExtensions), new[] { typeof(string) }, new[] { ValueSetType, typeof(string) })
                 })
        {
            methods.AddRange(declaring.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                      .Select(method => Close(method, one, two))
                                      .OfType<MethodInfo>()
                                      .Where(method => method.GetParameters().Length is 1 or 2));
        }

        methods.AddRange(SetType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                            .Where(method => !method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal))
                            .Where(method => method.GetParameters().Length <= (method.IsStatic ? 2 : 1)));

        return [.. methods.Where(method => !NotQueryable.Contains(method.Name))
                          .Where(method => method.ReturnType != typeof(void))
                          .OrderBy(method => method.DeclaringType!.Name, StringComparer.Ordinal)
                          .ThenBy(method => method.Name, StringComparer.Ordinal)
                          .ThenBy(method => string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name)), StringComparer.Ordinal)];
    }

    private static MethodInfo? Close(MethodInfo method, Type[] one, Type[] two)
    {
        try
        {
            return method.GetGenericArguments().Length switch
            {
                0 => method,
                1 => method.MakeGenericMethod(one),
                2 => method.MakeGenericMethod(two),
                _ => null
            };
        }
        catch (ArgumentException)
        {
            return null; // a constraint the family does not meet
        }
    }

    /// <summary>
    /// The parameters of <paramref name="method"/> in call order, the receiver first: the instance for
    /// an instance method, the first argument otherwise.
    /// </summary>
    public static IReadOnlyList<Type> Slots(MethodInfo method) =>
        [.. method.IsStatic ? [] : new[] { method.DeclaringType! }, .. method.GetParameters().Select(p => p.ParameterType)];

    /// <summary>
    /// Every call of every member with the operands <paramref name="operands"/> offers per slot, kept
    /// when <paramref name="keep"/> accepts its operands (typically: one of them can be NULL), plus the
    /// value set's <c>Count</c> and <c>IsEmpty</c> on each operand the first slot of a value set takes.
    /// </summary>
    public static List<Call> Calls(Func<Type, int, IEnumerable<Operand>> operands, Func<IReadOnlyList<Operand>, bool> keep)
    {
        var calls = new List<Call>();

        foreach (var method in Methods)
        {
            var slots = Slots(method);
            foreach (var combination in Product(slots.Select((type, index) => operands(type, index).ToList()).ToList()))
            {
                if (!keep(combination)) continue;
                calls.Add(new Call(Name(method, combination), Build(method, combination.Select(o => o.Value).ToList())));
            }
        }

        foreach (var receiver in operands(ValueSetType, 0).Where(o => keep([o])))
        {
            calls.Add(new Call($"{receiver.Name}.Count", Expression.Property(receiver.Value, nameof(StringSet.Count))));
            calls.Add(new Call($"{receiver.Name}.IsEmpty", Expression.Property(receiver.Value, nameof(StringSet.IsEmpty))));
        }

        return calls;
    }

    private static Expression Build(MethodInfo method, IReadOnlyList<Expression> values)
    {
        if (method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal))
        {
            var type = method.Name switch
            {
                "op_BitwiseOr"  => ExpressionType.Or,
                "op_BitwiseAnd" => ExpressionType.And,
                "op_Subtraction" => ExpressionType.Subtract,
                _ => throw new NotSupportedException(method.Name)
            };
            return Expression.MakeBinary(type, values[0], values[1], false, method);
        }

        return method.IsStatic
            ? Expression.Call(method, values)
            : Expression.Call(values[0], method, values.Skip(1));
    }

    private static string Name(MethodInfo method, IReadOnlyList<Operand> operands) =>
        method.Name switch
        {
            "op_BitwiseOr"   => $"{operands[0].Name} | {operands[1].Name}",
            "op_BitwiseAnd"  => $"{operands[0].Name} & {operands[1].Name}",
            "op_Subtraction" => $"{operands[0].Name} - {operands[1].Name}",
            _                => $"{operands[0].Name}.{method.Name}({string.Join(", ", operands.Skip(1).Select(o => o.Name))})"
        };

    private static IEnumerable<IReadOnlyList<Operand>> Product(IReadOnlyList<List<Operand>> slots)
    {
        IEnumerable<IReadOnlyList<Operand>> result = [[]];
        foreach (var slot in slots)
            result = result.SelectMany(prefix => slot.Select(operand => (IReadOnlyList<Operand>)[.. prefix, operand])).ToList();
        return result;
    }

    /// <summary>A captured variable, which EF Core sends as a parameter.</summary>
    public static MemberExpression Captured<T>(T value) =>
        Expression.Field(Expression.Constant(new System.Runtime.CompilerServices.StrongBox<T>(value)), nameof(System.Runtime.CompilerServices.StrongBox<>.Value));

    /// <summary>
    /// Which family a parameter of type <paramref name="parameter"/> takes, if any: a range, a multirange,
    /// a value set, or an element of either.
    /// </summary>
    public static string? Kind(Type parameter) =>
        parameter == ElementType                    ? "element"
      : parameter == typeof(string)                 ? "text"
      : parameter.IsAssignableFrom(RangeType)       ? "range"
      : parameter == SetType                        ? "set"
      : parameter.IsAssignableFrom(ValueSetType)    ? "values"
      :                                               null;
}

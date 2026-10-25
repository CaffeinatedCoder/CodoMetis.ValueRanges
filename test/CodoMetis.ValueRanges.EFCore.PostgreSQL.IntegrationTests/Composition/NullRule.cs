using System.Linq.Expressions;
using System.Reflection;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;

/// <summary>
/// The answer a query should give for an expression over values that can be NULL, computed in memory:
/// the expression rewritten so that a NULL never reaches the library and is answered by rule (c)
/// instead (#350, #355, <c>docs/efcore.md#query-ranges-with-linq</c>).
/// </summary>
/// <remarks>
/// <para>
/// The rewrite is the null propagation EF Core applies to its own members, made explicit as the
/// guard a consumer writes over loaded rows:
/// </para>
/// <list type="bullet">
///   <item>A member read through a value that is <see langword="null"/> (the child of an outer join,
///   a nullable column) is <see langword="null"/>, lifted to <see cref="Nullable{T}"/> for a value type.</item>
///   <item>A <b>predicate</b> of the library with a null on either side is <see langword="false"/>, so
///   its negation is <see langword="true"/>: rule (c). That holds for a value set predicate too, which
///   throws in memory for a null set by design.</item>
///   <item>Any other library member with a null operand, or with an element that is null, has no value:
///   <see langword="null"/>. That is the 11.0.0 half of #350 for <c>Intersect</c>, <c>Gap</c>,
///   <c>Merge</c>, <c>Union</c> and <c>Except</c>, and #360's reading for a value set's <c>Union</c>.
///   The library is never called with a null, so what it does today for one is not judged here.</item>
///   <item>A comparison, a coalesce and a conditional over a lifted value follow C#'s lifted operators:
///   <c>null &gt; 0</c> is <see langword="false"/>, <c>null == null</c> is <see langword="true"/>.</item>
///   <item>Equality keeps its own rule (EF Core's, which is C#'s). <c>CompareTo</c> keeps
///   <see cref="Comparer{T}.Default"/>'s, under which a null sorts first (#220, #253, #256, #261,
///   <c>docs/ranges.md#sort-ranges</c>). The aggregates skip a null as SQL's do (#361).</item>
/// </list>
/// </remarks>
internal sealed class NullRule : ExpressionVisitor
{
    private static readonly Assembly[] Library = [typeof(RangeExtensions).Assembly, typeof(LocalDateRange).Assembly];

    /// <summary>The reference for <paramref name="body"/>, a lambda over <paramref name="row"/>, boxed.</summary>
    public static Func<TRow, object?> Reference<TRow>(Expression body, ParameterExpression row) =>
        Expression.Lambda<Func<TRow, object?>>(Expression.Convert(new NullRule().Visit(body), typeof(object)), row).Compile();

    /// <summary>The reference for <paramref name="body"/>, a lambda over <paramref name="parameter"/>, taking and returning a boxed value.</summary>
    public static Func<object?, object?> Reference(Expression body, ParameterExpression parameter)
    {
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object?, object?>>(
            Expression.Block([parameter],
                             Expression.Assign(parameter, Expression.Convert(value, parameter.Type)),
                             Expression.Convert(new NullRule().Visit(body)!, typeof(object))),
            value).Compile();
    }

    private static bool IsLibrary(MemberInfo member) =>
        member.DeclaringType is { } type && Library.Contains(type.Assembly)
        && type != typeof(RangeAggregateExtensions) && !IsAggregates(type);

    // The satellite's aggregate overloads skip a null as the core's do (#361).
    private static bool IsAggregates(Type type) => type.Name.EndsWith("AggregateExtensions", StringComparison.Ordinal);

    private static bool IsEquality(MethodInfo method) => method.Name is "op_Equality" or "op_Inequality" or nameof(Equals);

    /// <summary>The type a value of <paramref name="type"/> has once it can be missing.</summary>
    internal static Type Lift(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? typeof(Nullable<>).MakeGenericType(type) : type;

    private static Expression NoValue(Type type, bool libraryPredicate) =>
        libraryPredicate ? Expression.Constant(false) : Expression.Constant(null, Lift(type));

    protected override Expression VisitMember(MemberExpression node)
    {
        // A static, a captured variable, or the row itself: present.
        if (node.Expression is null or ConstantExpression or ParameterExpression) return node;

        var target   = Visit(node.Expression)!;
        var original = node.Expression.Type;
        var lifted   = target.Type != original;

        // .Value of a nullable column is the column in SQL, so a missing value stays missing rather than throwing.
        if (node.Member.Name == nameof(Nullable<>.Value) && Nullable.GetUnderlyingType(original) is not null) return target;

        // A member of Nullable<T> itself (HasValue, Value), or of a value that cannot be missing.
        if (!lifted && original.IsValueType) return node.Update(target);

        var temp   = Expression.Variable(target.Type, "value");
        var access = Expression.MakeMemberAccess(lifted ? Expression.Property(temp, nameof(Nullable<>.Value)) : temp, node.Member);
        var isNull = lifted
            ? (Expression)Expression.Not(Expression.Property(temp, nameof(Nullable<>.HasValue)))
            : Expression.ReferenceEqual(temp, Expression.Constant(null, target.Type));

        var predicate = IsLibrary(node.Member) && node.Type == typeof(bool);
        var type      = predicate ? typeof(bool) : Lift(node.Type);
        return Expression.Block(type, [temp],
            Expression.Assign(temp, target),
            Expression.Condition(isNull, NoValue(node.Type, predicate), Expression.Convert(access, type)));
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var instance  = node.Object is null ? null : Visit(node.Object);
        var arguments = node.Arguments.Select(argument => Visit(argument)!).ToList();

        if (node.Method.Name == nameof(IComparable<>.CompareTo) && IsLibrary(node.Method) && instance is not null)
            return Expression.Call(
                Expression.Property(null, typeof(Comparer<>).MakeGenericType(node.Object!.Type), nameof(Comparer<>.Default)),
                nameof(Comparer<>.Compare), null, instance, arguments[0]);

        if (!IsLibrary(node.Method) || IsEquality(node.Method))
        {
            if (instance?.Type != node.Object?.Type || arguments.Where((a, i) => a.Type != node.Arguments[i].Type).Any())
                throw new NotSupportedException($"A null reaches '{node.Method}', which NullRule does not answer for.");
            return node.Update(instance, arguments);
        }

        List<(Expression Value, Type Declared)> operands = [.. arguments.Select((a, i) => (a, node.Arguments[i].Type))];
        if (instance is not null) operands.Insert(0, (instance, node.Object!.Type));

        return Guarded(node.Method, node.Type, operands,
                       values => node.Object is null
                           ? Expression.Call(node.Method, values)
                           : Expression.Call(values[0], node.Method, values.Skip(1)));
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        var left  = Visit(node.Left)!;
        var right = Visit(node.Right)!;

        // A library operator other than equality (RangeSet's |, & and -) is a value-producing member.
        if (node.Method is { } method && IsLibrary(method) && !IsEquality(method))
            return Guarded(method, node.Type, [(left, node.Left.Type), (right, node.Right.Type)],
                           values => Expression.MakeBinary(node.NodeType, values[0], values[1], node.IsLiftedToNull, method));

        if (left.Type == node.Left.Type && right.Type == node.Right.Type) return node.Update(left, node.Conversion, right);

        if (node.NodeType == ExpressionType.Coalesce) return Expression.Coalesce(left, right);

        // C#'s lifted operators: both sides nullable, a comparison answers bool.
        var common = Lift(Nullable.GetUnderlyingType(left.Type) ?? Nullable.GetUnderlyingType(right.Type) ?? left.Type);
        return Expression.MakeBinary(node.NodeType, Expression.Convert(left, common), Expression.Convert(right, common));
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        var operand = Visit(node.Operand)!;
        if (operand.Type == node.Operand.Type) return node.Update(operand);

        return node.NodeType switch
        {
            ExpressionType.Convert when node.Type == operand.Type                             => operand,
            ExpressionType.Convert when node.Type.IsValueType && Nullable.GetUnderlyingType(node.Type) is null
                                                                                               => Expression.Convert(operand, Lift(node.Type)),
            ExpressionType.Convert                                                            => Expression.Convert(operand, node.Type),
            _ => throw new NotSupportedException($"A null reaches '{node}', which NullRule does not answer for.")
        };
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        var test    = Visit(node.Test)!;
        var ifTrue  = Visit(node.IfTrue)!;
        var ifFalse = Visit(node.IfFalse)!;
        if (ifTrue.Type == node.Type && ifFalse.Type == node.Type) return node.Update(test, ifTrue, ifFalse);

        var type = Lift(node.Type);
        return Expression.Condition(test, Expression.Convert(ifTrue, type), Expression.Convert(ifFalse, type));
    }

    /// <summary>
    /// A library member called only when every operand is present: otherwise <see langword="false"/> for
    /// a predicate and <see langword="null"/> for anything else.
    /// </summary>
    private static Expression Guarded(MethodInfo method, Type returnType, IReadOnlyList<(Expression Value, Type Declared)> operands,
                                      Func<IReadOnlyList<Expression>, Expression> call)
    {
        var temps  = operands.Select((o, i) => Expression.Variable(o.Value.Type, $"operand{i}")).ToList();
        var checks = new List<Expression>();
        var values = new List<Expression>();

        foreach (var ((value, declared), temp) in operands.Zip(temps))
        {
            if (value.Type != declared)
            {
                checks.Add(Expression.Not(Expression.Property(temp, nameof(Nullable<>.HasValue))));
                values.Add(Expression.Property(temp, nameof(Nullable<>.Value)));
            }
            else
            {
                if (!declared.IsValueType) checks.Add(Expression.ReferenceEqual(temp, Expression.Constant(null, declared)));
                values.Add(temp);
            }
        }

        var predicate = returnType == typeof(bool);
        var type      = predicate ? typeof(bool) : Lift(returnType);
        var invoked   = Expression.Convert(call(values), type);
        Expression body = checks.Count == 0
            ? invoked
            : Expression.Condition(checks.Aggregate(Expression.OrElse), NoValue(returnType, predicate), invoked);

        var steps = operands.Zip(temps).Select(p => (Expression)Expression.Assign(p.Second, p.First.Value)).Append(body);
        return Expression.Block(type, temps, steps);
    }
}

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// The form a <c>Compare(x, y)</c> over a range, a range set or a value set takes in a query once
/// <see cref="ValueRangesQueryExpressionInterceptor"/> has seen it: a call no translator knows, so it
/// runs in memory or not at all (#385).
/// </summary>
/// <remarks>
/// EF Core reads any <c>Compare(x, y)</c> as the natural order of its operands, so
/// <see cref="RangeLowerBoundComparer{TRange,T}"/> ran in SQL in the range order instead of by lower
/// bound. Under this name EF Core fails to translate the call in a filter or an ordering, and its message
/// shows <c>RangeComparerCall.RunInMemory(…)</c>; in a final projection it runs the call on the rows it
/// loaded, which is the answer the same code gives in memory. The call runs as written: the method the
/// code called, not <see cref="IComparer{T}.Compare"/>, which a class may implement differently, and
/// which a <c>Compare</c> on a class that is no comparer, or a static one, does not have (#464).
/// </remarks>
internal static class RangeComparerCall
{
    /// <summary>The call <paramref name="call"/> stands for, on <paramref name="comparer"/>.</summary>
    public static int RunInMemory<TComparer, T>(TComparer comparer, T x, T y, int call) => ((Func<TComparer, T, T, int>)Calls[call])(comparer, x, y);

    /// <summary>The static call <paramref name="call"/> stands for.</summary>
    public static int RunInMemory<T>(T x, T y, int call) => ((Func<T, T, int>)Calls[call])(x, y);

    private static readonly MethodInfo InstanceMethod =
        typeof(RangeComparerCall).GetMethods().Single(method => method is { Name: nameof(RunInMemory) } && method.GetGenericArguments().Length == 2);

    private static readonly MethodInfo StaticMethod =
        typeof(RangeComparerCall).GetMethods().Single(method => method is { Name: nameof(RunInMemory) } && method.GetGenericArguments().Length == 1);

    // The query holds the call as a number: EF Core refuses a delegate constant in a client projection,
    // since it would be captured by the cached query, and accepts a constant of a type it maps. One
    // delegate per method and receiver type; a race may compile one twice, and the extra is never used.
    private static readonly ConcurrentDictionary<(MethodInfo Method, Type? Receiver), int> Ids = new();

    private static readonly ConcurrentDictionary<int, Delegate> Calls = new();

    private static int _lastId;

    /// <summary>
    /// <paramref name="method"/> called on <paramref name="comparer"/> (<see langword="null"/> for a
    /// static method) with <paramref name="x"/> and <paramref name="y"/>, as a <c>RunInMemory</c> call.
    /// </summary>
    public static MethodCallExpression Of(MethodInfo method, Expression? comparer, Expression x, Expression y)
    {
        var compared = method.GetParameters()[0].ParameterType;
        var call = Expression.Constant(Ids.GetOrAdd((method, comparer?.Type), static key =>
        {
            var id = Interlocked.Increment(ref _lastId);
            Calls[id] = Compile(key.Method, key.Receiver);
            return id;
        }));

        return comparer is null
            ? Expression.Call(StaticMethod.MakeGenericMethod(compared), x, y, call)
            : Expression.Call(InstanceMethod.MakeGenericMethod(comparer.Type, compared), comparer, x, y, call);
    }

    private static Delegate Compile(MethodInfo method, Type? receiverType)
    {
        var (x, y) = (Expression.Parameter(method.GetParameters()[0].ParameterType), Expression.Parameter(method.GetParameters()[1].ParameterType));
        if (receiverType is null)
            return Expression.Lambda(Expression.Call(method, x, y), x, y).Compile();

        var receiver = Expression.Parameter(receiverType);
        return Expression.Lambda(Expression.Call(receiver, method, x, y), receiver, x, y).Compile();
    }
}

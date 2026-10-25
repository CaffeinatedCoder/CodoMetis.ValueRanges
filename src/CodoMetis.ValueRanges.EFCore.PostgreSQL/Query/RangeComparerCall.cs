using System.Reflection;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;

/// <summary>
/// The form an <see cref="IComparer{T}"/>'s or <see cref="System.Collections.IComparer"/>'s <c>Compare</c>
/// over a range, a range set or a value set takes in a query
/// once <see cref="ValueRangesQueryExpressionInterceptor"/> has seen it: a call no translator knows, so
/// it runs in memory or not at all (#385).
/// </summary>
/// <remarks>
/// EF Core reads any <c>Compare(x, y)</c> as the natural order of its operands, so
/// <see cref="RangeLowerBoundComparer{TRange,T}"/> ran in SQL in the range order instead of by lower
/// bound. Under this name EF Core fails to translate the call in a filter or an ordering, and its message
/// shows <c>RangeComparerCall.RunInMemory(…)</c>; in a final projection it calls the comparer on the rows
/// it loaded, which is the answer the same code gives in memory.
/// </remarks>
internal static class RangeComparerCall
{
    /// <summary><paramref name="comparer"/>'s own <see cref="IComparer{T}.Compare"/>.</summary>
    public static int RunInMemory<T>(IComparer<T> comparer, T x, T y) => comparer.Compare(x, y);

    /// <summary><paramref name="comparer"/>'s own <see cref="System.Collections.IComparer.Compare"/>.</summary>
    public static int RunInMemory(System.Collections.IComparer comparer, object? x, object? y) => comparer.Compare(x, y);

    internal static readonly MethodInfo RunInMemoryMethod =
        typeof(RangeComparerCall).GetMethods().Single(method => method is { Name: nameof(RunInMemory), IsGenericMethodDefinition: true });

    internal static readonly MethodInfo RunInMemoryNonGenericMethod =
        typeof(RangeComparerCall).GetMethod(nameof(RunInMemory), [typeof(System.Collections.IComparer), typeof(object), typeof(object)])!;
}

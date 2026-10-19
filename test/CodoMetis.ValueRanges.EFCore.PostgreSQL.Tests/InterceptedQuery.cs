using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// The query tree as EF Core's preprocessor receives it: after every registered
/// <see cref="IQueryExpressionInterceptor"/> has run at <c>QueryCompilationStarting</c>, and before
/// EF Core's own normalization. Linked into the NodaTime translation tests as well.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's <c>QueryableMethodNormalizingExpressionVisitor</c> rewrites a one-argument instance method
/// named <c>Contains</c> on a generic collection type into <c>Queryable.Contains</c> over the
/// collection's element type. EF Core 10.0.0 through 10.0.14 do that for <see cref="ICollection{T}"/>;
/// the patch that carries dotnet/efcore#37081 (10.0.15, dotnet/efcore#39114) and EF Core 11 also do it
/// for <see cref="IReadOnlyCollection{T}"/>, which a <c>RangeSet&lt;TRange, T&gt;</c> is. The
/// interceptor takes every <c>RangeSet.Contains</c> off the collection before that (#288).
/// </para>
/// <para>
/// <c>ToQueryString</c> on the floor cannot tell whether it did: the floor leaves the instance call
/// alone and translates it to the same <c>@&gt;</c>. These helpers read the tree instead, through the
/// same logger call <c>QueryCompilationContext</c> makes, so the interceptor chain that runs is the one
/// the context registered.
/// </para>
/// </remarks>
internal static class InterceptedQuery
{
    /// <summary>The query's expression after the context's query expression interceptors.</summary>
    public static Expression Of(DbContext context, IQueryable query)
        => context.GetService<IDiagnosticsLogger<DbLoggerCategory.Query>>()
                  .QueryCompilationStarting(context, new ExpressionPrinter(), query.Expression)
                  .Query;

    /// <summary>
    /// The calls EF Core's normalization rewrites: a one-argument instance method named
    /// <c>Contains</c> whose declaring type is, or implements, <see cref="ICollection{T}"/> or
    /// <see cref="IReadOnlyCollection{T}"/>.
    /// </summary>
    public static IReadOnlyList<MethodCallExpression> CollectionContainsCalls(Expression tree)
        => Find(tree, call => call is { Object: not null, Arguments.Count: 1 }
                           && call.Method.Name == nameof(ICollection<>.Contains)
                           && IsGenericCollection(call.Method.DeclaringType));

    /// <summary>
    /// The static <c>RangeSetContains</c> calls the interceptor substitutes, named by overload:
    /// <c>Value</c>, <c>Range</c> or <c>Set</c>.
    /// </summary>
    public static IReadOnlyList<MethodCallExpression> StaticRangeSetContainsCalls(Expression tree)
        => Find(tree, call => call.Method is { IsStatic: true, DeclaringType.Name: "RangeSetContains" });

    /// <summary>A call as <c>Declaring.Method</c>, for a failure message.</summary>
    public static string Describe(IEnumerable<MethodCallExpression> calls)
        => string.Join(", ", calls.Select(call => $"{call.Method.DeclaringType?.Name}.{call.Method.Name}"));

    private static bool IsGenericCollection(Type? type)
        => type is { IsGenericType: true }
        && type.GetInterfaces().Prepend(type).Any(
               candidate => candidate.IsGenericType
                         && (candidate.GetGenericTypeDefinition() == typeof(ICollection<>)
                          || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)));

    private static IReadOnlyList<MethodCallExpression> Find(Expression tree, Func<MethodCallExpression, bool> matches)
    {
        var finder = new CallFinder(matches);
        finder.Visit(tree);
        return finder.Found;
    }

    private sealed class CallFinder(Func<MethodCallExpression, bool> matches) : ExpressionVisitor
    {
        public List<MethodCallExpression> Found { get; } = [];

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (matches(node)) Found.Add(node);
            return base.VisitMethodCall(node);
        }
    }
}

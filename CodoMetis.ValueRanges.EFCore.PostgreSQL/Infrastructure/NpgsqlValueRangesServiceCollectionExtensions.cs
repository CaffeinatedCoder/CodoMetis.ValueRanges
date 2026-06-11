using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

// ReSharper disable once CheckNamespace — conventional namespace for service collection extensions.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// CodoMetis.ValueRanges extension method for <see cref="IServiceCollection"/>.
/// </summary>
public static class NpgsqlValueRangesServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services required for CodoMetis.ValueRanges support to an external service
    /// provider used as the EF Core internal service provider. Most applications do not call
    /// this directly — use
    /// <see cref="Microsoft.EntityFrameworkCore.NpgsqlValueRangesDbContextOptionsBuilderExtensions.UseValueRanges"/>
    /// instead.
    /// </summary>
    /// <param name="serviceCollection">The service collection to add services to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddEntityFrameworkNpgsqlValueRanges(this IServiceCollection serviceCollection)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        new EntityFrameworkNpgsqlServicesBuilder(serviceCollection)
           .TryAdd<IRelationalTypeMappingSourcePlugin, ValueRangesTypeMappingSourcePlugin>()
           .TryAdd<IMethodCallTranslatorPlugin, ValueRangesMethodCallTranslatorPlugin>()
           .TryAdd<IInterceptor, ValueRangesQueryExpressionInterceptor>();

        return serviceCollection;
    }
}

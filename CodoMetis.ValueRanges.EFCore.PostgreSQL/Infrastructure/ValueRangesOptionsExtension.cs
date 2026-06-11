using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Infrastructure;

/// <summary>
/// The <see cref="IDbContextOptionsExtension"/> added by
/// <see cref="NpgsqlValueRangesDbContextOptionsBuilderExtensions.UseValueRanges"/>.
/// Registers the type mapping source and translator plugins with the provider's
/// internal service provider.
/// </summary>
public sealed class ValueRangesOptionsExtension : IDbContextOptionsExtension
{
    /// <inheritdoc />
    public DbContextOptionsExtensionInfo Info => field ??= new ExtensionInfo(this);

    /// <inheritdoc />
    public void ApplyServices(IServiceCollection services) => services.AddEntityFrameworkNpgsqlValueRanges();

    /// <inheritdoc />
    public void Validate(IDbContextOptions options)
    {
        var internalServiceProvider = options.FindExtension<CoreOptionsExtension>()?.InternalServiceProvider;
        if (internalServiceProvider is null) return;

        using var scope = internalServiceProvider.CreateScope();
        if (scope.ServiceProvider
                 .GetService<IEnumerable<IRelationalTypeMappingSourcePlugin>>()
                ?.Any(plugin => plugin is ValueRangesTypeMappingSourcePlugin) != true)
        {
            throw new InvalidOperationException(
                $"'{nameof(NpgsqlValueRangesDbContextOptionsBuilderExtensions.UseValueRanges)}' requires "       +
                $"'{nameof(NpgsqlValueRangesServiceCollectionExtensions.AddEntityFrameworkNpgsqlValueRanges)}' " +
                "to be called on the external service provider used.");
        }
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using ValueRanges ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Npgsql:" + nameof(NpgsqlValueRangesDbContextOptionsBuilderExtensions.UseValueRanges)] = "1";
    }
}
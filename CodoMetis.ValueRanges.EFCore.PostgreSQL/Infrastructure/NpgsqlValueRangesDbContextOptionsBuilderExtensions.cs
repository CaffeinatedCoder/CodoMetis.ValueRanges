using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

// ReSharper disable once CheckNamespace — conventional namespace for options builder extensions,
// so UseValueRanges is discoverable without an extra using.
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// CodoMetis.ValueRanges extension method for <see cref="NpgsqlDbContextOptionsBuilder"/>.
/// </summary>
public static class NpgsqlValueRangesDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Enables mapping of the CodoMetis.ValueRanges types to the PostgreSQL range and
    /// multirange types, including LINQ translation of their query and set operations:
    /// <code>
    /// options.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());
    /// </code>
    /// </summary>
    /// <param name="optionsBuilder">The Npgsql options builder.</param>
    /// <returns>The same options builder, for chaining.</returns>
    public static NpgsqlDbContextOptionsBuilder UseValueRanges(this NpgsqlDbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        var coreOptionsBuilder = ((IRelationalDbContextOptionsBuilderInfrastructure)optionsBuilder).OptionsBuilder;

        var extension = coreOptionsBuilder.Options.FindExtension<ValueRangesOptionsExtension>()
                     ?? new ValueRangesOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)coreOptionsBuilder).AddOrUpdateExtension(extension);

        return optionsBuilder;
    }
}

using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;

/// <summary>
/// A database of its own per composition sweep, so its nullable columns and joined tables never change
/// the shared schema and its rows never meet another test's.
/// </summary>
internal static class CompositionDatabase
{
    public static async Task<string> Create(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(ContainerLifecycle.ServerConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString) { Database = name }.ConnectionString;
    }

    public static void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString)
        => optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());
}

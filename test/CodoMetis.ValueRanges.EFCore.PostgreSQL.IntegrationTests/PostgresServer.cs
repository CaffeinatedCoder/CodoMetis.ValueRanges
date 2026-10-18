using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The server each integration project's shared container runs, linked into both so they cannot
/// disagree. The image comes from <see cref="ImageVariable"/> and defaults to the 10.x floor, so a local
/// run tests the oldest supported server; CI runs one leg per supported major (16 and 18). Tests that
/// start servers of their own versions (<c>ComplexIndexesCrossTests</c>,
/// <c>LiteralGrammarParityTests.NestedEmptyArrays_…</c>) are unaffected.
/// </summary>
/// <remarks>
/// A server below the floor fails every test that needs it, Docker or not, instead of letting the
/// sweeps run: on PostgreSQL 14 and 15 <c>pg_input_is_valid</c> does not exist, so the literal oracles
/// compare nothing and only their floor assertions notice (#215).
/// </remarks>
internal static class PostgresServer
{
    /// <summary>The environment variable naming the image, e.g. <c>postgres:18-alpine</c>.</summary>
    internal const string ImageVariable = "VALUERANGES_POSTGRES_IMAGE";

    /// <summary>PostgreSQL 16, the floor of the 10.x line (SECURITY.md).</summary>
    internal const string DefaultImage = "postgres:16-alpine";

    /// <summary>The oldest <c>server_version_num</c> 10.x supports.</summary>
    internal const int FloorVersionNum = 160000;

    internal static string Image =>
        Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } image ? image : DefaultImage;

    /// <summary>
    /// <see langword="null"/> when the server is supported; otherwise why not, naming the server's own
    /// version (<c>server_version_num</c>) rather than the image.
    /// </summary>
    internal static async Task<string?> BelowFloor(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::int", connection);
        var version = (int)(await command.ExecuteScalarAsync())!;

        return version >= FloorVersionNum
            ? null
            : $"The container ({Image}) runs PostgreSQL {version}, below the 10.x floor {FloorVersionNum} (PostgreSQL 16). "
            + $"Set {ImageVariable} to a supported image.";
    }
}

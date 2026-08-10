using Testcontainers.PostgreSql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Starts a single PostgreSQL container for the whole test assembly. When Docker is not
/// available, the container stays <see langword="null"/> and every test reports
/// <c>Inconclusive</c> instead of failing.
/// </summary>
[TestClass]
public sealed class ContainerLifecycle
{
    private static PostgreSqlContainer? _container;

    internal static string? ConnectionString { get; private set; }

    private static string? _unavailableReason;

    [AssemblyInitialize]
    public static async Task StartContainer(TestContext _)
    {
        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();

            await using var context = new IntegrationDbContext();
            await context.Database.EnsureCreatedAsync();
        }
        catch (Exception exception)
        {
            _unavailableReason = exception.Message;
            ConnectionString   = null;
        }
    }

    [AssemblyCleanup]
    public static async Task StopContainer()
    {
        if (_container is not null) await _container.DisposeAsync();
    }

    /// <summary>
    /// Call at the start of every test: skips (Inconclusive) when no database is available.
    /// </summary>
    internal static void RequireDatabase()
    {
        if (ConnectionString is null)
        {
            Assert.Inconclusive(
                $"PostgreSQL test container unavailable ({_unavailableReason ?? "unknown"}). "
                + "Docker is required for the integration tests.");
        }
    }
}

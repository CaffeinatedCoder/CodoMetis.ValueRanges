using Testcontainers.PostgreSql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Starts a single PostgreSQL container for the whole test assembly. When Docker is not
/// available, the container stays <see langword="null"/> and every test reports
/// <c>Inconclusive</c> instead of failing — except in CI (<c>CI</c> environment variable),
/// where the live parity layer is mandatory and a missing database fails the tests: a
/// silent skip would keep the build badge green while the suite's authority layer
/// stopped running.
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
    /// Call at the start of every test: skips (Inconclusive) when no database is available,
    /// or fails outright when running in CI.
    /// </summary>
    internal static void RequireDatabase()
    {
        if (ConnectionString is not null) return;

        var message = $"PostgreSQL test container unavailable ({_unavailableReason ?? "unknown"}). "
                    + "Docker is required for the integration tests.";

        if (IsCi)
            Assert.Fail($"{message} Running in CI, where the live-PostgreSQL layer is mandatory.");

        Assert.Inconclusive(message);
    }

    // GitHub Actions (and most CI systems) set CI=true; treat "1" as truthy as well.
    private static bool IsCi =>
        Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ci
        && (ci.Equals("true", StringComparison.OrdinalIgnoreCase) || ci == "1");
}

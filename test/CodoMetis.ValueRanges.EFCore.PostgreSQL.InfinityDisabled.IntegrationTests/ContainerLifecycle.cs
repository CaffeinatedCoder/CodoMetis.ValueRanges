using Testcontainers.PostgreSql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.InfinityDisabled.IntegrationTests;

/// <summary>
/// One PostgreSQL container for the assembly, as in the other integration project: Inconclusive
/// without Docker, a failure under <c>CI</c>. Not shared with it, because that one's model is built
/// in a process where the switch is off.
/// </summary>
[TestClass]
public sealed class ContainerLifecycle
{
    private static PostgreSqlContainer? _container;

    private static string? _unavailableReason;

    internal static string? ConnectionString { get; private set; }

    [AssemblyInitialize]
    public static async Task StartContainer(TestContext _)
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();

            await using var context = new InfinityDisabledDbContext();
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

    internal static void RequireDatabase()
    {
        if (ConnectionString is not null) return;

        var message = $"PostgreSQL test container unavailable ({_unavailableReason ?? "unknown"}). "
                    + "Docker is required for the integration tests.";

        if (IsCi)
            Assert.Fail($"{message} Running in CI, where the live-PostgreSQL layer is mandatory.");

        Assert.Inconclusive(message);
    }

    private static bool IsCi =>
        Environment.GetEnvironmentVariable("CI") is { Length: > 0 } ci
        && (ci.Equals("true", StringComparison.OrdinalIgnoreCase) || ci == "1");
}

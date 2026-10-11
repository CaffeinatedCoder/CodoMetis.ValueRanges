using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// The final-SQL value set guard runs from a decorator around the provider's parameter-based SQL
/// processor factory. EF Core has no plugin seam at that stage, so a registration that silently
/// failed to decorate would switch every refusal off without any other test noticing the cause.
/// </summary>
[TestClass]
public sealed class SqlGuardRegistrationTests
{
    private static readonly StringSet Wanted = StringSet.From("a", "b");

    [TestMethod]
    public void UseValueRanges_DecoratesTheProvidersProcessorFactory()
    {
        using var context = new TestDbContext();

        Assert.IsInstanceOfType<ValueRangesParameterBasedSqlProcessorFactory>(
            context.GetService<IRelationalParameterBasedSqlProcessorFactory>());
    }

    [TestMethod]
    public void AnExternalProvider_IsDecoratedOnce_WhicheverOrderTheProviderIsAdded()
    {
        var services = new ServiceCollection()
                      .AddEntityFrameworkNpgsqlValueRanges()
                      .AddEntityFrameworkNpgsql()
                      .AddEntityFrameworkNpgsqlValueRanges();

        Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(IRelationalParameterBasedSqlProcessorFactory)));

        using var provider = services.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ExternalProviderContext>()
                     .UseInternalServiceProvider(provider)
                     .UseNpgsql("Host=localhost;Database=valueranges_tests;Username=postgres", npgsql => npgsql.UseValueRanges())
                     .Options;

        using var context = new ExternalProviderContext(options);
        Assert.IsInstanceOfType<ValueRangesParameterBasedSqlProcessorFactory>(
            context.GetService<IRelationalParameterBasedSqlProcessorFactory>());

        // And the guard actually runs through it.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Bookings.Where(b => b.Tags.Union(Wanted) == Wanted).ToQueryString());
    }

    private sealed class ExternalProviderContext(DbContextOptions<ExternalProviderContext> options) : DbContext(options)
    {
        public DbSet<Booking> Bookings => Set<Booking>();
    }
}

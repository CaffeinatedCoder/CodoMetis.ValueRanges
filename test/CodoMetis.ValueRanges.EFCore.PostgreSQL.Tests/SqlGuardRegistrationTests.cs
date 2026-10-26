using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

// NpgsqlParameterBasedSqlProcessorFactory is Npgsql-internal (EF1001): replacing the service with
// the provider's own factory is the realistic way a third-party registration drops the guard.
#pragma warning disable EF1001

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

    /// <summary>
    /// A later <c>ReplaceService</c> (or another extension replacing the same service) drops the
    /// decorator, and with it the guard. Up to the final 9.0.0 review that was silent: the
    /// <c>Union</c> translated to <c>array_cat</c> and every refused query answered as in 8.x. The
    /// translator now emits a placeholder that only the guard renames, so without the guard the SQL
    /// names a function PostgreSQL does not have (executed in <c>UnionCanonicalityTests</c>).
    /// </summary>
    [TestMethod]
    public void AReplacedProcessorFactory_SendsNoArrayCat()
    {
        using var context = new ReplacedFactoryContext();
        Assert.IsInstanceOfType<PlainProcessorFactory>(context.GetService<IRelationalParameterBasedSqlProcessorFactory>());

        var refused  = context.Bookings.Where(b => b.Tags.Union(Wanted) == Wanted).Select(b => b.Id).ToQueryString();
        var harmless = context.Bookings.Where(b => b.Tags.Union(Wanted).Contains("a")).Select(b => b.Id).ToQueryString();

        foreach (var sql in new[] { refused, harmless })
        {
            Assert.IsFalse(sql.Contains("array_cat(", StringComparison.Ordinal), sql);
            StringAssert.Contains(sql, UnguardedUnion + "(");
        }
    }

    /// <summary>
    /// The canonical operations fail closed the same way (#478): each is a placeholder only the guard
    /// builds, so without the guard the SQL names a function PostgreSQL does not have rather than a
    /// concatenation that would answer wrongly under equality.
    /// </summary>
    [TestMethod]
    public void AReplacedProcessorFactory_SendsTheCanonicalPlaceholders()
    {
        using var context = new ReplacedFactoryContext();
        var codes   = Int32Set.From(1, 2);
        var element = 3;

        foreach (var (sql, placeholder) in new[]
                 {
                     (context.Bookings.Where(b => b.Codes.Union(codes) == codes).Select(b => b.Id).ToQueryString(), "valueranges_canonical_union_without_sql_guard("),
                     (context.Bookings.Where(b => b.Codes.Add(element) == codes).Select(b => b.Id).ToQueryString(), "valueranges_canonical_add_without_sql_guard("),
                     (context.Bookings.Where(b => b.Codes.Intersect(codes) == codes).Select(b => b.Id).ToQueryString(), "valueranges_canonical_intersect_without_sql_guard("),
                     (context.Bookings.Where(b => b.Codes.Except(codes) == codes).Select(b => b.Id).ToQueryString(), "valueranges_canonical_except_without_sql_guard(")
                 })
        {
            StringAssert.Contains(sql, placeholder);
            Assert.IsFalse(sql.Contains("array_cat(", StringComparison.Ordinal), sql);
            Assert.IsFalse(sql.Contains("unnest(", StringComparison.Ordinal), sql);
        }
    }

    /// <summary>
    /// <c>RangeSet.Count</c> fails closed the same way (#484): its subquery is built after EF Core's alias pass by
    /// the same decorated processor, so without it the SQL names a function PostgreSQL does not have.
    /// </summary>
    [TestMethod]
    public void AReplacedProcessorFactory_SendsTheCountPlaceholder()
    {
        using var context = new ReplacedFactoryContext();

        var sql = context.Bookings.Where(b => b.BlockedDays.Count > 2).Select(b => b.Id).ToQueryString();

        StringAssert.Contains(sql, "valueranges_multirange_count_without_sql_guard(");
        Assert.IsFalse(sql.Contains("unnest(", StringComparison.Ordinal), sql);
    }

    /// <summary>
    /// The placeholder is renamed by the guard itself, not by detecting the decorator, so another
    /// library decorating <em>around</em> it — rather than replacing it — leaves the guard running and
    /// the SQL unchanged.
    /// </summary>
    [TestMethod]
    public void ADecoratorAroundTheGuard_KeepsItRunning()
    {
        var services = new ServiceCollection().AddEntityFrameworkNpgsql().AddEntityFrameworkNpgsqlValueRanges();
        var guarded  = services.Single(d => d.ServiceType == typeof(IRelationalParameterBasedSqlProcessorFactory));
        services.Remove(guarded);
        services.Add(new ServiceDescriptor(
            typeof(IRelationalParameterBasedSqlProcessorFactory),
            provider => new PassThroughProcessorFactory((IRelationalParameterBasedSqlProcessorFactory) guarded.ImplementationFactory!(provider)),
            guarded.Lifetime));

        using var provider = services.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<ExternalProviderContext>()
                     .UseInternalServiceProvider(provider)
                     .UseNpgsql("Host=localhost;Database=valueranges_tests;Username=postgres", npgsql => npgsql.UseValueRanges())
                     .Options;

        using var context = new ExternalProviderContext(options);
        Assert.IsInstanceOfType<PassThroughProcessorFactory>(context.GetService<IRelationalParameterBasedSqlProcessorFactory>());

        var sql = context.Bookings.Where(b => b.Tags.Union(Wanted).Contains("a")).Select(b => b.Id).ToQueryString();
        StringAssert.Contains(sql, "array_cat(");
        Assert.IsFalse(sql.Contains(UnguardedUnion, StringComparison.Ordinal), sql);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Bookings.Where(b => b.Tags.Union(Wanted) == Wanted).ToQueryString());
    }

    /// <summary>The name PostgreSQL reports when the guard did not run — part of the documented contract.</summary>
    private const string UnguardedUnion = "valueranges_union_without_sql_guard";

    private sealed class ExternalProviderContext(DbContextOptions<ExternalProviderContext> options) : DbContext(options)
    {
        public DbSet<Booking> Bookings => Set<Booking>();
    }

    private sealed class ReplacedFactoryContext : DbContext
    {
        public DbSet<Booking> Bookings => Set<Booking>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=valueranges_tests;Username=postgres", npgsql => npgsql.UseValueRanges())
                             .ReplaceService<IRelationalParameterBasedSqlProcessorFactory, PlainProcessorFactory>();
    }

    /// <summary>What a library replacing the service registers: the provider's own processor, no guard.</summary>
    private sealed class PlainProcessorFactory(RelationalParameterBasedSqlProcessorDependencies dependencies)
        : IRelationalParameterBasedSqlProcessorFactory
    {
        public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
            => new NpgsqlParameterBasedSqlProcessorFactory(dependencies).Create(parameters);
    }

    /// <summary>What a library decorating the service registers: the guarded factory, wrapped.</summary>
    private sealed class PassThroughProcessorFactory(IRelationalParameterBasedSqlProcessorFactory inner)
        : IRelationalParameterBasedSqlProcessorFactory
    {
        public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
            => inner.Create(parameters);
    }
}

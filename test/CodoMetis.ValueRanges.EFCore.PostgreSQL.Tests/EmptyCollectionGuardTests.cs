using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// An empty <c>VALUES</c> that holds no range or value set column keeps EF Core's own behaviour:
/// the plugin rewrites only the collections it put on that path.
/// </summary>
/// <remarks>
/// <c>EmptyCollectionRewriter</c> gives an empty captured collection of ranges or value sets, which
/// the plugin sends one parameter per element, a one-row <c>VALUES</c> of typed NULLs that yields no
/// rows (#137). A context configured for <see cref="ParameterTranslationMode.MultipleParameters"/>
/// sends any collection that way, and for one of plain <see cref="int"/>s the decision is EF Core's,
/// not the plugin's. Nothing held that line (#252): with the guard removed, every empty
/// <c>VALUES</c> was rewritten and every suite still passed. Each case here runs the same query
/// with and without the plugin and requires the same outcome, SQL or exception.
/// </remarks>
[TestClass]
public sealed class EmptyCollectionGuardTests
{
    private static readonly List<int> NoIds = [];

    private static readonly Func<IQueryable<Counter>, IQueryable<object?>>[] Queries =
    [
        // Any with a predicate EF Core cannot turn into IN, so it reads the collection from VALUES.
        counters => counters.Where(c => NoIds.Any(id => id > c.Id)).Select(c => (object?)c.Id),
        counters => counters.Where(c => NoIds.Any(id => id == c.Id)).Select(c => (object?)c.Id),
        counters => counters.Where(c => NoIds.Count(id => id > c.Id) == 0).Select(c => (object?)c.Id),
        counters => counters.Where(c => NoIds.All(id => id != c.Id)).Select(c => (object?)c.Id)
    ];

    [TestMethod]
    public void AnEmptyIntCollection_UnderMultipleParameters_TranslatesAsWithoutThePlugin()
    {
        foreach (var (query, index) in Queries.Select((query, index) => (query, index)))
        {
            using var plain  = new CounterContext(withPlugin: false);
            using var plugin = new CounterContext(withPlugin: true);

            Assert.AreEqual(Outcome(() => query(plain.Counters)), Outcome(() => query(plugin.Counters)), $"query {index}");
        }
    }

    private static string Outcome(Func<IQueryable<object?>> query)
    {
        try { return query().ToQueryString(); }
        catch (Exception exception) { return $"{exception.GetType().Name}: {exception.Message}"; }
    }

    public sealed class Counter
    {
        public int Id { get; set; }
    }

    private sealed class CounterContext(bool withPlugin) : DbContext
    {
        public DbSet<Counter> Counters => Set<Counter>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(
                "Host=localhost;Database=valueranges_tests;Username=postgres",
                npgsql =>
                {
                    npgsql.UseParameterizedCollectionMode(ParameterTranslationMode.MultipleParameters);
                    if (withPlugin) npgsql.UseValueRanges();
                });

        // One model per configuration: the plugin changes the services EF Core caches the model with.
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Counter>();
    }
}

using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A string set column with a byte-order collation answers as memory does (#483), where a nondeterministic one did
/// not: on a case-insensitive column <c>Contains("a")</c> was true for <c>{A}</c> and <c>ExecuteUpdate</c> with
/// <c>Remove("a")</c> stored <c>{}</c>. That collation is now rejected when the model is built.
/// </summary>
[TestClass]
public sealed class StringSetCollationIntegrationTests
{
    [TestMethod]
    public async Task AStringSetColumnCollatedC_ComparesElementsAsMemoryDoes()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await CompositionDatabase.Create("collation_c");
        var stored = StringSet.From("A", "b");
        var a = StringSet.From("a");

        await using (var seed = new CollatedContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Posts.Add(new CollatedPost { Id = 1, Tags = stored });
            await seed.SaveChangesAsync();
        }

        await using (var context = new CollatedContext(connectionString))
        {
            Assert.AreEqual(stored.Contains("a"), await context.Posts.AnyAsync(p => p.Tags.Contains("a")), "Contains(\"a\")");
            Assert.AreEqual(stored.Contains("A"), await context.Posts.AnyAsync(p => p.Tags.Contains("A")), "Contains(\"A\")");
            Assert.AreEqual(stored.Overlaps(a), await context.Posts.AnyAsync(p => p.Tags.Overlaps(a)), "Overlaps({a})");
            Assert.AreEqual(stored.Remove("a"), await context.Posts.Select(p => p.Tags.Remove("a")).SingleAsync(), "Remove(\"a\")");

            await context.Posts.ExecuteUpdateAsync(s => s.SetProperty(p => p.Tags, p => p.Tags.Remove("a")));
        }

        await using (var reread = new CollatedContext(connectionString))
            Assert.AreEqual(stored.Remove("a"), (await reread.Posts.SingleAsync()).Tags, "ExecuteUpdate with Remove(\"a\")");

        await using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    public sealed class CollatedPost
    {
        public int Id { get; set; }

        public StringSet Tags { get; set; } = StringSet.Empty;
    }

    private sealed class CollatedContext(string connectionString) : DbContext
    {
        public DbSet<CollatedPost> Posts => Set<CollatedPost>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CollatedPost>().Property(p => p.Id).ValueGeneratedNever();
            modelBuilder.Entity<CollatedPost>().Property(p => p.Tags).UseCollation("C");
        }
    }
}

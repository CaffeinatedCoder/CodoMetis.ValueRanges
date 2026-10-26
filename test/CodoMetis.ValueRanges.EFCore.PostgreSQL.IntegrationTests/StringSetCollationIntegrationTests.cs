using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

    /// <summary>
    /// PostgreSQL takes an operator's collation from both sides, so an element from a case-insensitive column, or
    /// collated by <c>EF.Functions.Collate</c>, made <c>@&gt;</c> and <c>array_remove</c> compare case-insensitively with
    /// the set column uncollated, as #483's fix leaves it: <c>Contains(owner)</c> was true for <c>{A,b}</c>, and
    /// <c>ExecuteUpdate</c> with <c>Remove(owner)</c> stored <c>{b}</c> (#507). Each throws now, naming the collation.
    /// </summary>
    [TestMethod]
    public async Task AnElementWithACaseInsensitiveCollation_IsRejected_WhereItWouldCompareAsTheCollationDoes()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await CompositionDatabase.Create("collation_element");
        var stored = StringSet.From("A", "b");

        await using (var seed = new MembersContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Groups.Add(new MemberGroup { Id = 1, Members = stored, Owner = "a", Login = "a" });
            await seed.SaveChangesAsync();
        }

        await using (var context = new MembersContext(connectionString))
        {
            async Task Rejected(Func<Task> query, string what)
            {
                var e = await Assert.ThrowsExactlyAsync<InvalidOperationException>(query, what);
                StringAssert.Contains(e.Message, "'case_insensitive'", what);
            }

            await Rejected(() => context.Groups.AnyAsync(g => g.Members.Contains(g.Owner)), "Contains(Owner)");
            await Rejected(() => context.Groups.AnyAsync(g => !g.Members.Contains(g.Owner)), "!Contains(Owner)");
            await Rejected(() => context.Groups.Select(g => g.Members.Remove(g.Owner)).SingleAsync(), "Remove(Owner)");
            await Rejected(() => context.Groups.Select(g => g.Members.Add(g.Owner).Count).SingleAsync(), "Add(Owner).Count");
            await Rejected(() => context.Groups.AnyAsync(g => g.Members.Contains(g.Owner.ToLower())), "Contains(Owner.ToLower())");
            await Rejected(() => context.Groups.AnyAsync(g => g.Members.Contains(EF.Functions.Collate("a", "case_insensitive"))),
                           "Contains(Collate(\"a\"))");
            await Rejected(() => context.Groups.AnyAsync(g => g.Members.Contains(context.Groups.Select(o => o.Owner).First())),
                           "Contains(a subquery's Owner)");
            await Rejected(() => context.Groups.ExecuteUpdateAsync(s => s.SetProperty(g => g.Members, g => g.Members.Remove(g.Owner))),
                           "ExecuteUpdate with Remove(Owner)");

            // A byte-order collation on the element's column, or one EF.Functions.Collate puts on it, which wins over
            // the column's: both answer as memory does. So does a CASE that only tests a case-insensitive column.
            Assert.AreEqual(stored.Contains("a"), await context.Groups.AnyAsync(g => g.Members.Contains(g.Login)), "Contains(Login)");
            Assert.AreEqual(stored.Contains("a"), await context.Groups.AnyAsync(g => g.Members.Contains(EF.Functions.Collate(g.Owner, "C"))),
                            "Contains(Collate(Owner, \"C\"))");
            Assert.AreEqual(stored.Contains("a"), await context.Groups.AnyAsync(g => g.Members.Contains(g.Owner.Length > 0 ? "a" : "b")),
                            "Contains(a CASE whose test reads Owner)");
        }

        await using (var reread = new MembersContext(connectionString))
            Assert.AreEqual(stored, (await reread.Groups.SingleAsync()).Members, "ExecuteUpdate was rejected, so the row is unchanged");

        await using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    /// <summary>
    /// The read-optimized model a query runs on keeps no collation, so the convention names a collated column's on its
    /// property; a compiled model carries that annotation too, or the guard would see no collation at all.
    /// </summary>
    [TestMethod]
    public void AnElementWithACaseInsensitiveCollation_IsRejected_OnACompiledModelToo()
    {
        const string connectionString = "Host=localhost;Database=collation_compiled;Username=postgres";
        IModel compiled;
        using (var design = new MembersContext(connectionString))
            compiled = CompiledModels.CompiledModelJsonTests.CompileModel(design, "CodoMetis.ValueRanges.CompiledCollation");

        using var context = new MembersContext(connectionString, compiled);
        var e = Assert.ThrowsExactly<InvalidOperationException>(() => context.Groups.Where(g => g.Members.Contains(g.Owner)).ToQueryString());
        StringAssert.Contains(e.Message, "'case_insensitive'");
        StringAssert.Contains(context.Groups.Where(g => g.Members.Contains(g.Login)).ToQueryString(), "@> ARRAY[g.\"Login\"]");
    }

    public sealed class MemberGroup
    {
        public int Id { get; set; }

        public StringSet Members { get; set; } = StringSet.Empty;

        /// <summary>Case-insensitive.</summary>
        public string Owner { get; set; } = "";

        /// <summary>Byte order.</summary>
        public string Login { get; set; } = "";
    }

    /// <summary>Public, so that a compiled model of it compiles.</summary>
    public sealed class MembersContext(string connectionString, IModel? model = null) : DbContext
    {
        public DbSet<MemberGroup> Groups => Set<MemberGroup>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            CompositionDatabase.Configure(optionsBuilder, connectionString);
            if (model is not null) optionsBuilder.UseModel(model);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasCollation("case_insensitive", locale: "und-u-ks-level2", provider: "icu", deterministic: false);
            modelBuilder.Entity<MemberGroup>().Property(g => g.Id).ValueGeneratedNever();
            modelBuilder.Entity<MemberGroup>().Property(g => g.Owner).UseCollation("case_insensitive");
            modelBuilder.Entity<MemberGroup>().Property(g => g.Login).UseCollation("C");
        }
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

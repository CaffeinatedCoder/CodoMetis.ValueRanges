using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// A string set column compares its elements as memory does only under a byte-order collation (#483). Any other
/// collation on a <see cref="StringSet"/> or <see cref="StringSet{TElement}"/> column is rejected when the model is
/// built, and <c>EF.Functions.Collate</c> over a string set when the query is. That a nondeterministic collation
/// answers differently is measured in the issue; that "C" answers as memory does is <c>StringSetCollationIntegrationTests</c>'.
/// </summary>
[TestClass]
public sealed class StringSetCollationTests
{
    [TestMethod]
    public void AStringSetColumn_WithACollationThatMayNotCompareBytes_IsRejectedWhenTheModelIsBuilt()
    {
        using var context = new CollationContext(model => model.Entity<Post>().Property(p => p.Tags).UseCollation("case_insensitive"));

        var e = Assert.ThrowsExactly<InvalidOperationException>(() => _ = context.Model);

        StringAssert.Contains(e.Message, "'Post.Tags' is a StringSet column with the collation 'case_insensitive'");
        StringAssert.Contains(e.Message, "Remove the collation, or use \"C\"");
    }

    [TestMethod]
    public void AWrapperStringSetColumn_WithACollation_IsRejectedToo()
    {
        using var context = new CollationContext(model => model.Entity<Post>().Property(p => p.Keys).UseCollation("de-DE-x-icu"));

        var e = Assert.ThrowsExactly<InvalidOperationException>(() => _ = context.Model);

        StringAssert.Contains(e.Message, "'Post.Keys' is a StringSet<TestKey> column with the collation 'de-DE-x-icu'");
    }

    [TestMethod]
    [DataRow("C")]
    [DataRow("POSIX")]
    [DataRow("ucs_basic")]
    [DataRow("pg_c_utf8")]
    [DataRow("default")]
    public void AStringSetColumn_WithAByteOrderCollation_IsMappedWithIt(string collation)
    {
        using var context = new CollationContext(model => model.Entity<Post>().Property(p => p.Tags).UseCollation(collation));

        // Npgsql quotes an identifier only when it needs to: "C", "POSIX" and the keyword "default", but ucs_basic.
        var script = context.Database.GenerateCreateScript();
        Assert.IsTrue(script.Contains($"\"Tags\" text[] COLLATE {collation} NOT NULL", StringComparison.Ordinal)
                   || script.Contains($"\"Tags\" text[] COLLATE \"{collation}\" NOT NULL", StringComparison.Ordinal), script);
    }

    [TestMethod]
    public void AStringSetInsideAJsonDocument_IsNotChecked_SinceTheDocumentHoldsItsLiteral()
    {
        using var context = new CollationContext(model => model.Entity<Post>().ComplexProperty(p => p.Doc, doc =>
        {
            doc.ToJson();
            doc.Property(d => d.Labels).UseCollation("case_insensitive");
        }));

        Assert.IsNotNull(context.Model.FindEntityType(typeof(Post)));
    }

    [TestMethod]
    public void EfFunctionsCollate_OverAStringSet_IsRefusedUnlessItComparesBytes()
    {
        using var context = new CollationContext(_ => { });

        var e = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Posts.Where(p => EF.Functions.Collate(p.Tags, "case_insensitive").Contains("a")).ToQueryString());
        StringAssert.Contains(e.Message, "A string set cannot be compared under the collation 'case_insensitive'");

        var a = StringSet.From("a");
        Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Posts.Where(p => EF.Functions.Collate(p.Tags, "case_insensitive") == a).ToQueryString());
        Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Posts.Select(p => EF.Functions.Collate(p.Tags, "case_insensitive").Remove("a")).ToQueryString());

        StringAssert.Contains(
            context.Posts.Where(p => EF.Functions.Collate(p.Tags, "C").Contains("a")).ToQueryString(),
            "(p.\"Tags\" COLLATE \"C\") @> ARRAY['a']::text[]");
    }

    public sealed class Post
    {
        public int Id { get; set; }

        public StringSet Tags { get; set; } = StringSet.Empty;

        public StringSet<TestKey> Keys { get; set; } = StringSet<TestKey>.Empty;

        public PostDoc Doc { get; set; } = new();
    }

    public sealed class PostDoc
    {
        public StringSet Labels { get; set; } = StringSet.Empty;
    }

    private sealed class CollationContext(Action<ModelBuilder> configure) : DbContext
    {
        public DbSet<Post> Posts => Set<Post>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=valueranges_tests;Username=postgres", npgsql => npgsql.UseValueRanges())
                             // Each test configures the model differently.
                             .ReplaceService<IModelCacheKeyFactory, PerInstanceModelCacheKey>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>().ComplexProperty(p => p.Doc, doc => doc.ToJson());
            configure(modelBuilder);
        }
    }

    private sealed class PerInstanceModelCacheKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context, designTime);
    }
}

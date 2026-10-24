using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// A captured collection of value sets — <c>wanted.Contains(b.Tags)</c> — is sent one parameter per
/// set, because a single parameter would be an array of arrays, which PostgreSQL does not have.
/// </summary>
/// <remarks>
/// Up to 9.0.0 Npgsql bound the collection as one array parameter, <c>b."Tags" = ANY (@wanted)</c>,
/// and threw <c>InvalidCastException</c> writing it (<c>String[][]</c>) for every family. The answers
/// are checked against live PostgreSQL in <c>SetCollectionContainsTests</c>; these pin the SQL.
/// </remarks>
[TestClass]
public sealed class SetCollectionTranslationTests
{
    /// <summary>The WHERE clause only — the projection lists every column and drowns the assertion.</summary>
    private static string Where(Func<TestDbContext, IQueryable<Booking>> query)
    {
        using var context = new TestDbContext();
        return WhereOf(query(context));
    }

    private static string WhereOf(IQueryable<Booking> query)
    {
        var sql   = query.ToQueryString();
        var index = sql.IndexOf("WHERE", StringComparison.Ordinal);

        Assert.IsTrue(index >= 0, $"No WHERE clause in the generated SQL:\n{sql}");
        return sql[index..].ReplaceLineEndings(" ");
    }

    [TestMethod]
    public void Contains_OverACapturedList_IsInOverOneParameterPerSet()
    {
        var wanted = new List<StringSet> { StringSet.From("a", "b"), StringSet.From("c") };

        Assert.AreEqual("WHERE b.\"Tags\" IN (@wanted1, @wanted2)", Where(db => db.Bookings.Where(b => wanted.Contains(b.Tags))));
    }

    [TestMethod]
    public void Contains_OverACapturedArray_IsInOverOneParameterPerSet()
    {
        var wanted = new[] { Int32Set.From(1, 2), Int32Set.From(3) };

        Assert.AreEqual("WHERE b.\"Codes\" IN (@wanted1, @wanted2)", Where(db => db.Bookings.Where(b => wanted.Contains(b.Codes))));
    }

    [TestMethod]
    public void Contains_OverACapturedCollectionOfAWrapperArity_IsInOverOneParameterPerSet()
    {
        IEnumerable<StringSet<TestKey>> wanted =
            [StringSet<TestKey>.From([TestKey.Parse("users.read", null)]), StringSet<TestKey>.Empty];

        Assert.AreEqual("WHERE b.\"Permissions\" IN (@wanted1, @wanted2)", Where(db => db.Bookings.Where(b => wanted.Contains(b.Permissions))));
    }

    [TestMethod]
    public void NotContains_OverACapturedList_IsNotIn()
    {
        var wanted = new List<GuidSet> { GuidSet.Empty, GuidSet.From(Guid.Empty) };

        Assert.AreEqual("WHERE b.\"Uuids\" NOT IN (@wanted1, @wanted2)", Where(db => db.Bookings.Where(b => !wanted.Contains(b.Uuids))));
    }

    [TestMethod]
    public void Any_OverACapturedList_ReadsTheSetsFromValues()
    {
        var wanted = new List<StringSet> { StringSet.From("a"), StringSet.From("b") };

        Assert.AreEqual(
            "WHERE EXISTS (     SELECT 1     FROM (VALUES (@wanted1), (@wanted2)) AS w(\"Value\")     WHERE w.\"Value\" IS NOT NULL AND w.\"Value\" && b.\"Tags\")",
            Where(db => db.Bookings.Where(b => wanted.Any(w => w.Overlaps(b.Tags)))));
    }

    /// <summary>
    /// An empty captured collection expands to a <c>VALUES</c> with no rows (#137). On EF Core 10.0.0
    /// and 10.0.1 the plugin gives it one row of typed <c>NULL</c>s and <c>FALSE</c> on its <c>WHERE</c>
    /// — EF Core folds the predicate away under it. EF Core 10.0.2 and later read no rows for it
    /// themselves — a table-less <c>SELECT</c> of <c>NULL</c>s under <c>WHERE FALSE</c> — and the plugin
    /// casts each <c>NULL</c> to the column's type, since a bare one is <c>text</c> to PostgreSQL and
    /// fails the operator on it (#302). Either way the subquery reads no rows, so <c>Any</c> is
    /// <see langword="false"/> and <c>Count</c> is <c>0</c>. One SQL per shape, chosen by the EF Core
    /// this host resolved (<see cref="EmptyCollectionSql"/>).
    /// </summary>
    [TestMethod]
    public void Any_OverAnEmptyCapturedList_ReadsNoRows()
    {
        var wanted = new List<StringSet>();

        Assert.AreEqual(
            $"WHERE EXISTS (     SELECT 1     {EmptyCollectionSql.FromNoRows("text[]", "w.\"Value\" IS NOT NULL AND w.\"Value\" && b.\"Tags\"")})",
            Where(db => db.Bookings.Where(b => wanted.Any(w => w.Overlaps(b.Tags)))));
    }

    [TestMethod]
    public void Count_OverAnEmptyCapturedList_ReadsNoRows()
    {
        var wanted = new List<StringSet>();

        StringAssert.Contains(
            Where(db => db.Bookings.Where(b => wanted.Count(w => w.IsSubsetOf(b.Tags)) == 0)),
            $"WHERE (     SELECT count(*)::int     {EmptyCollectionSql.FromNoRows("text[]", "w.\"Value\" IS NOT NULL AND w.\"Value\" <@ b.\"Tags\"")}) = 0");
    }

    [TestMethod]
    public void Contains_OverEFParameter_IsRefusedWithWhatToWriteInstead()
    {
        var wanted = new List<StringSet> { StringSet.From("a") };
        using var context = new TestDbContext();

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Bookings.Where(b => EF.Parameter(wanted).Contains(b.Tags)).ToQueryString());

        StringAssert.Contains(refusal.Message, "A collection of StringSet cannot be sent as a single parameter ('EF.Parameter')");
        StringAssert.Contains(refusal.Message, "Remove 'EF.Parameter'");
    }

    [TestMethod]
    public void Contains_OverEFConstant_KeepsItsLiterals()
    {
        var wanted = new List<StringSet> { StringSet.From("a", "b"), StringSet.From("c") };

        Assert.AreEqual(
            "WHERE b.\"Tags\" IN (ARRAY['a','b']::text[], ARRAY['c']::text[])",
            Where(db => db.Bookings.Where(b => EF.Constant(wanted).Contains(b.Tags))));
    }

    [TestMethod]
    public void Contains_UnderAContextConfiguredForConstants_KeepsItsLiterals()
    {
        var wanted = new List<StringSet> { StringSet.From("a", "b"), StringSet.From("c") };
        using var context = new ConstantCollectionsContext();

        Assert.AreEqual(
            "WHERE b.\"Tags\" IN (ARRAY['a','b']::text[], ARRAY['c']::text[])",
            WhereOf(context.Bookings.Where(b => wanted.Contains(b.Tags))));
    }

    [TestMethod]
    public void Contains_OfAServerSideUnion_IsStillRefused()
    {
        var wanted = new List<StringSet> { StringSet.From("a", "b") };
        using var context = new TestDbContext();

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Bookings.Where(b => wanted.Contains(b.Tags.Union(b.Tags))).ToQueryString());

        StringAssert.Contains(refusal.Message, "Contains over a sequence of sets");
    }

    [TestMethod]
    public void Contains_OverACapturedListOfPrimitives_IsUnchanged()
    {
        // The rewrite is keyed to registered value set types: an ordinary collection still binds as
        // one array parameter.
        var wanted = new List<string> { "a", "b" };

        Assert.AreEqual("WHERE b.\"Tag\" = ANY (@wanted)", Where(db => db.Bookings.Where(b => wanted.Contains(b.Tag))));
    }

    private sealed class ConstantCollectionsContext : DbContext
    {
        public DbSet<Booking> Bookings => Set<Booking>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(
                "Host=localhost;Database=valueranges_tests;Username=postgres",
                npgsql => npgsql.UseValueRanges().UseParameterizedCollectionMode(ParameterTranslationMode.Constant));
    }
}

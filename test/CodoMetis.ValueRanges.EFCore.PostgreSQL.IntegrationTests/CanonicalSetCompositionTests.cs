using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The compositions <see cref="UnionCanonicalityTests"/> holds refused for text, run for a family
/// PostgreSQL sorts as memory does (#478): each executes and answers what the same expression answers
/// in memory, where before 10.4.0 it was refused for every family.
/// </summary>
/// <remarks>
/// The data is the text probe's: <c>{1,3} ∪ {1,2}</c> and <c>{2,3} ∪ {1,2}</c> are both <c>{1,2,3}</c>
/// in memory, and would be <c>{1,3,1,2}</c> and <c>{2,3,1,2}</c> concatenated, so every comparison of
/// the two unions tells canonical form from a concatenation. A third row's union is
/// <c>{1,2,4}</c>, which sorts after them.
/// </remarks>
[TestClass]
public sealed class CanonicalSetCompositionTests
{
    private static readonly Int32Set Operand = Int32Set.From(1, 2);
    private static readonly Int32Set Target  = Int32Set.From(1, 2, 3);

    private static readonly Int32Set[] Stored = [Int32Set.From(1, 3), Int32Set.From(2, 3), Int32Set.From(4)];

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static string? _connectionString;

    private static async Task<CompositionContext> Context()
    {
        ContainerLifecycle.RequireServer();

        await SeedLock.WaitAsync();
        try
        {
            if (_connectionString is null)
            {
                var connectionString = await CompositionDatabase.Create("canonical_set_composition");
                await using var seed = new CompositionContext(connectionString);
                await seed.Database.EnsureCreatedAsync();
                seed.Rows.AddRange(Stored.Select((codes, i) => new CompositionRow { Id = i + 1, Codes = codes, MaybeCodes = i == 0 ? null : codes }));
                await seed.SaveChangesAsync();
                _connectionString = connectionString;
            }
        }
        finally
        {
            SeedLock.Release();
        }

        return new CompositionContext(_connectionString);
    }

    private static IQueryable<CompositionRow> Equal(CompositionContext context) => context.Rows.Where(r => r.Id < 3);

    [TestMethod]
    public void TheData_IsWhatTheTestsAssume()
    {
        Assert.AreEqual(Target, Stored[0].Union(Operand));
        Assert.AreEqual(Target, Stored[1].Union(Operand));
        Assert.AreEqual(Int32Set.From(1, 2, 4), Stored[2].Union(Operand));
    }

    [TestMethod]
    public async Task EqualityAndCount_SeeOneSet()
    {
        await using var context = await Context();

        Assert.AreEqual(2, await Equal(context).CountAsync(r => r.Codes.Union(Operand) == Target));
        Assert.AreEqual(0, await Equal(context).CountAsync(r => r.Codes.Union(Operand) != Target));
        CollectionAssert.AreEqual(new[] { true, true }, await Equal(context).OrderBy(r => r.Id).Select(r => r.Codes.Union(Operand) == Target).ToListAsync());
        CollectionAssert.AreEqual(new[] { 3, 3, 3 }, await context.Rows.OrderBy(r => r.Id).Select(r => r.Codes.Union(Operand).Count).ToListAsync());
        Assert.AreEqual(2, await Equal(context).CountAsync(r => r.Codes.Union(Operand).Remove(9) == Target));
    }

    [TestMethod]
    public async Task AConditionalACoalesceAndADerivedTable_CompareCanonicalForm()
    {
        await using var context = await Context();

        Assert.AreEqual(2, await Equal(context).CountAsync(r => (r.Id > 0 ? r.Codes.Union(Operand) : r.Codes) == Target));
        Assert.AreEqual(2, await Equal(context).CountAsync(r => (r.MaybeCodes ?? r.Codes).Union(Operand) == Target));

        var concatenated = context.Rows.Where(r => r.Id == 1).Select(r => new UnionRow { Id = r.Id, U = r.Codes.Union(Operand) })
                                  .Concat(context.Rows.Where(r => r.Id == 2).Select(r => new UnionRow { Id = r.Id, U = r.Codes.Union(Operand) }));
        Assert.AreEqual(2, await concatenated.CountAsync(a => a.U == Target));
        Assert.AreEqual(2, await concatenated.CountAsync(a => a.U.Count == 3));
    }

    [TestMethod]
    public async Task GroupingDistinctAndSetOperations_FormOneSet()
    {
        await using var context = await Context();

        CollectionAssert.AreEqual(new[] { 2 }, await Equal(context).GroupBy(r => r.Codes.Union(Operand)).Select(g => g.Count()).ToListAsync());
        Assert.AreEqual(1, await Equal(context).Select(r => r.Codes.Union(Operand)).Distinct().CountAsync());

        var deduplicated = await context.Rows.Where(r => r.Id == 1).Select(r => r.Codes.Union(Operand))
                                        .Union(context.Rows.Where(r => r.Id == 2).Select(r => r.Codes.Union(Operand)))
                                        .ToListAsync();
        CollectionAssert.AreEqual(new[] { Target }, deduplicated);
    }

    [TestMethod]
    public async Task OrderingAndMembership_CompareCanonicalForm()
    {
        await using var context = await Context();

        CollectionAssert.AreEqual(new[] { 3, 1, 2 },
                                  await context.Rows.OrderByDescending(r => r.Codes.Union(Operand)).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync());

        Assert.AreEqual(1, await context.Rows.Where(r => r.Id == 1)
                                        .CountAsync(r => context.Rows.Where(o => o.Id == 2).Select(o => o.Codes.Union(Operand)).Contains(r.Codes.Union(Operand))));
    }

    [TestMethod]
    public async Task ExecuteUpdate_StoresCanonicalForm()
    {
        await using (var context = await Context())
        {
            await context.Rows.Where(r => r.Id < 3).ExecuteUpdateAsync(s => s.SetProperty(r => r.Updated, r => r.Codes.Union(Operand).Intersect(Target).Add(3)));
        }

        await using var read = await Context();
        Assert.AreEqual(2, await read.Rows.CountAsync(r => r.Id < 3 && r.Updated == Target && r.Updated.Count == 3));
    }

    /// <summary>Member-initialized rather than positional: a constructor call is a client projection.</summary>
    private sealed class UnionRow
    {
        public int Id { get; init; }
        public Int32Set U { get; init; } = Int32Set.Empty;
    }

    public sealed class CompositionRow
    {
        public int Id { get; set; }

        public Int32Set Codes { get; set; } = Int32Set.Empty;

        public Int32Set? MaybeCodes { get; set; }

        public Int32Set Updated { get; set; } = Int32Set.Empty;
    }

    private sealed class CompositionContext(string connectionString) : DbContext
    {
        public DbSet<CompositionRow> Rows => Set<CompositionRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<CompositionRow>().Property(r => r.Id).ValueGeneratedNever();
    }
}

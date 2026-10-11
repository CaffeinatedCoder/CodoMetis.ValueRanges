using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

// NpgsqlParameterBasedSqlProcessorFactory is Npgsql-internal (EF1001): replacing the service with
// the provider's own factory is the realistic way a third-party registration drops the guard.
#pragma warning disable EF1001

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A value set <c>Union</c> computed on the server is <c>array_cat</c> — a concatenation, with
/// duplicates and in operand order. Each test here either executes a composition that is correct
/// for a concatenation and compares the answer with the same expression in memory, or executes one
/// that is not and asserts it is refused — by running the query, never by inspecting its SQL.
/// </summary>
/// <remarks>
/// <para>
/// The data is the probe that found the gaps: <c>{a,c} ∪ {a,b}</c> is <c>{a,c,a,b}</c> on the
/// server and <c>{a,b,c}</c> in memory, and <c>{b,c} ∪ {a,b}</c> is <c>{b,c,a,b}</c> — so the two
/// rows have equal unions in memory and different ones on the server.
/// </para>
/// <para>
/// Up to 9.0.0 the refusal was a LINQ pattern match on <c>==</c>/<c>Equals</c> inside a list of
/// operators, plus <c>Count</c> over a bare union. Every shape in the refused group below translated
/// and answered wrongly; the refusal now runs on the final SQL tree (<c>ValueSetSqlGuard</c>).
/// </para>
/// </remarks>
[TestClass]
public sealed class UnionCanonicalityTests
{
    private const int First  = 12001;
    private const int Second = 12002;

    private static readonly StringSet Operand = StringSet.From("a", "b");
    private static readonly StringSet Target  = StringSet.From("a", "b", "c");

    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static bool _seeded;

    /// <summary>Seeds the two rows once; every test only reads them (or fails to write them).</summary>
    private static async Task<IntegrationDbContext> Context()
    {
        ContainerLifecycle.RequireDatabase();

        await SeedLock.WaitAsync();
        try
        {
            if (!_seeded)
            {
                await using var write = new IntegrationDbContext();
                write.Reservations.AddRange(
                    new Reservation { Id = First,  Tags = StringSet.From("a", "c") },
                    new Reservation { Id = Second, Tags = StringSet.From("b", "c") });
                await write.SaveChangesAsync();
                _seeded = true;
            }
        }
        finally
        {
            SeedLock.Release();
        }

        return new IntegrationDbContext();
    }

    private static IQueryable<Reservation> Rows(IntegrationDbContext context)
        => context.Reservations.Where(r => r.Id == First || r.Id == Second);

    private static IQueryable<Reservation> FirstRow(IntegrationDbContext context)
        => context.Reservations.Where(r => r.Id == First);

    /// <summary>
    /// Both rows' unions as a column of a <c>UNION ALL</c> — a derived table EF cannot inline, so
    /// whatever composes on <c>U</c> reads a column rather than the <c>array_cat</c> itself.
    /// </summary>
    private static IQueryable<UnionRow> Concatenated(IntegrationDbContext context)
        => FirstRow(context).Select(r => new UnionRow { Id = r.Id, U = r.Tags.Union(Operand) })
                            .Concat(context.Reservations.Where(r => r.Id == Second)
                                           .Select(r => new UnionRow { Id = r.Id, U = r.Tags.Union(Operand) }));

    /// <summary>Member-initialized rather than positional: a constructor call is a client projection.</summary>
    private sealed class UnionRow
    {
        public int Id { get; init; }
        public StringSet U { get; init; } = StringSet.Empty;
    }

    private static async Task AssertRefused(Func<Task> query, string mentioning)
    {
        var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(query);
        StringAssert.Contains(refusal.Message, mentioning);
    }

    [TestMethod]
    public void TheData_IsWhatTheTestsAssume()
    {
        Assert.AreEqual(Target, StringSet.From("a", "c").Union(Operand));
        Assert.AreEqual(Target, StringSet.From("b", "c").Union(Operand));
    }

    // -- Refused: each translated, and answered differently from memory, before 9.0.0 --

    [TestMethod]
    public async Task EqualityInAProjection_IsRefused()
    {
        await using var context = await Context();

        // Was: SELECT array_cat(r."Tags", @x) = @target → false, where memory says true.
        await AssertRefused(() => FirstRow(context).Select(r => r.Tags.Union(Operand) == Target).SingleAsync(), "equality");
        await AssertRefused(() => FirstRow(context).Select(r => new { r.Id, Same = r.Tags.Union(Operand).Equals(Target) }).SingleAsync(), "equality");
    }

    [TestMethod]
    public async Task EqualityOnAProjectedUnion_IsRefused()
    {
        await using var context = await Context();

        // Was: 0 rows, where memory finds 1.
        await AssertRefused(
            () => FirstRow(context).Select(r => new { r.Id, U = r.Tags.Union(Operand) }).Where(a => a.U == Target).CountAsync(),
            "equality");

        // The same over a UNION ALL, which EF cannot inline: the predicate reads a column of the
        // derived table, so the refusal has to follow the column to the array_cat that fills it.
        await AssertRefused(
            () => Concatenated(context).Where(a => a.U != Target).CountAsync(),
            "equality");
    }

    [TestMethod]
    public async Task EqualityOnAConditionalOrCoalescedUnion_IsRefused()
    {
        await using var context = await Context();

        // Was: CASE … = @target and COALESCE(…) = @target, 0 rows each.
        await AssertRefused(
            () => FirstRow(context).CountAsync(r => (r.Id > 0 ? r.Tags.Union(Operand) : r.Tags) == Target), "equality");
        await AssertRefused(
            () => FirstRow(context).CountAsync(r => (r.OptionalTags ?? r.Tags.Union(Operand)) == Target), "equality");
    }

    [TestMethod]
    public async Task EqualityOnARemovedUnion_IsRefused()
    {
        await using var context = await Context();

        await AssertRefused(() => FirstRow(context).CountAsync(r => r.Tags.Union(Operand).Remove("z") == Target), "equality");
    }

    [TestMethod]
    public async Task MembershipAmongUnions_IsRefused()
    {
        await using var context = await Context();

        // Membership is equality against each candidate: the first row's union is among the second
        // row's in memory, and {a,c,a,b} is not among {b,c,a,b} on the server.
        Assert.AreEqual(StringSet.From("b", "c").Union(Operand), StringSet.From("a", "c").Union(Operand));
        await AssertRefused(
            () => FirstRow(context).CountAsync(r => context.Reservations.Where(o => o.Id == Second)
                                                           .Select(o => o.Tags.Union(Operand))
                                                           .Contains(r.Tags.Union(Operand))),
            "equality");
    }

    [TestMethod]
    public async Task GroupingByAUnion_IsRefused()
    {
        await using var context = await Context();

        // Was: 2 groups, where memory forms 1 group of 2.
        await AssertRefused(
            () => Rows(context).GroupBy(r => r.Tags.Union(Operand)).Select(g => g.Count()).ToListAsync(), "GROUP BY");
    }

    [TestMethod]
    public async Task DistinctOverAUnion_IsRefused()
    {
        await using var context = await Context();

        // Was: 2, where memory counts 1.
        await AssertRefused(() => Rows(context).Select(r => r.Tags.Union(Operand)).Distinct().CountAsync(), "DISTINCT");
        await AssertRefused(() => Rows(context).Select(r => r.Tags.Union(Operand)).Distinct().ToListAsync(), "DISTINCT");
    }

    [TestMethod]
    public async Task OrderingByAUnion_IsRefused()
    {
        await using var context = await Context();

        await AssertRefused(() => Rows(context).OrderBy(r => r.Tags.Union(Operand)).Select(r => r.Id).ToListAsync(), "ORDER BY");
    }

    [TestMethod]
    public async Task ADeduplicatingSetOperationOverAUnion_IsRefused()
    {
        await using var context = await Context();

        await AssertRefused(
            () => FirstRow(context).Select(r => r.Tags.Union(Operand))
                                   .Union(context.Reservations.Where(r => r.Id == Second).Select(r => r.Tags.Union(Operand)))
                                   .ToListAsync(),
            "set operation");
    }

    [TestMethod]
    public async Task CountOverACoalescedUnion_IsRefused()
    {
        await using var context = await Context();

        // Was: cardinality(COALESCE(…, array_cat(…))) = 3 → 0 rows. The translator now declines it,
        // so EF reports the predicate untranslatable.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => FirstRow(context).CountAsync(r => (r.OptionalTags ?? r.Tags.Union(Operand)).Count == 3));

        // Through a column of a derived table the translator cannot see the union; the SQL check can.
        await AssertRefused(() => Concatenated(context).CountAsync(a => a.U.Count == 3), "'Count'");
    }

    [TestMethod]
    public async Task ExecuteUpdateToAUnion_IsRefused_AndTheRowIsUnchanged()
    {
        // A row of its own, so that a regression corrupts nothing the other tests read.
        const int updated = 12003;
        await using (var write = await Context())
        {
            write.Reservations.Add(new Reservation { Id = updated, Tags = StringSet.From("a", "c") });
            await write.SaveChangesAsync();
        }

        // Was: the row stored {a,c,a,b}, after which server-side equality and Count on it were wrong.
        await using (var context = new IntegrationDbContext())
        {
            await AssertRefused(
                () => context.Reservations.Where(r => r.Id == updated)
                             .ExecuteUpdateAsync(s => s.SetProperty(r => r.Tags, r => r.Tags.Union(Operand))),
                "ExecuteUpdate");
        }

        var original = StringSet.From("a", "c");
        await using var read = new IntegrationDbContext();
        Assert.AreEqual(1, await read.Reservations.CountAsync(r => r.Id == updated && r.Tags == original && r.Tags.Count == 2));
    }

    // -- Still translated, and agreeing with memory --

    [TestMethod]
    public async Task OrderAndMultiplicityInsensitiveOperators_ComposeOnAUnion()
    {
        await using var context = await Context();
        var probe = StringSet.From("a", "b", "c", "d");

        // Each answer is checked against the same expression evaluated in memory over both rows.
        var rows = await Rows(context).Select(r => r.Tags).ToListAsync();
        int Expected(Func<StringSet, bool> predicate) => rows.Count(predicate);

        Assert.AreEqual(Expected(t => t.Union(Operand).Contains("c")), await Rows(context).CountAsync(r => r.Tags.Union(Operand).Contains("c")));
        Assert.AreEqual(Expected(t => t.Union(Operand).IsSubsetOf(Target)), await Rows(context).CountAsync(r => r.Tags.Union(Operand).IsSubsetOf(Target)));
        Assert.AreEqual(Expected(t => t.Union(Operand).IsSupersetOf(Target)), await Rows(context).CountAsync(r => r.Tags.Union(Operand).IsSupersetOf(Target)));
        Assert.AreEqual(Expected(t => t.Union(Operand).Overlaps(Target)), await Rows(context).CountAsync(r => r.Tags.Union(Operand).Overlaps(Target)));
        Assert.AreEqual(Expected(t => t.Union(Operand).IsProperSubsetOf(probe)), await Rows(context).CountAsync(r => r.Tags.Union(Operand).IsProperSubsetOf(probe)));
        Assert.AreEqual(Expected(t => t.Union(Operand).IsProperSubsetOf(Target)), await Rows(context).CountAsync(r => r.Tags.Union(Operand).IsProperSubsetOf(Target)));
        Assert.AreEqual(Expected(t => t.Union(Operand).IsProperSupersetOf(Operand)), await Rows(context).CountAsync(r => r.Tags.Union(Operand).IsProperSupersetOf(Operand)));
        Assert.AreEqual(Expected(t => Target.IsSubsetOf(t.Union(Operand))), await Rows(context).CountAsync(r => Target.IsSubsetOf(r.Tags.Union(Operand))));
        Assert.AreEqual(Expected(t => t.Union(Operand).Remove("c").IsSupersetOf(Operand)),
                        await Rows(context).CountAsync(r => r.Tags.Union(Operand).Remove("c").IsSupersetOf(Operand)));
        Assert.AreEqual(Expected(t => t.Union(Operand).IsEmpty), await Rows(context).CountAsync(r => r.Tags.Union(Operand).IsEmpty));

        // Set equality spelled as mutual containment — the remedy the equality refusal names.
        Assert.AreEqual(2, await Rows(context).CountAsync(r => r.Tags.Union(Operand).IsSubsetOf(Target) && r.Tags.Union(Operand).IsSupersetOf(Target)));
        Assert.AreEqual(2, Expected(t => t.Union(Operand) == Target));
    }

    [TestMethod]
    public async Task AProjectedUnion_IsMaterializedCanonical()
    {
        await using var context = await Context();

        // Reading through the set's From canonicalizes, so the concatenation never reaches the caller.
        var bare = await FirstRow(context).Select(r => r.Tags.Union(Operand)).SingleAsync();
        Assert.AreEqual(Target, bare);
        Assert.AreEqual(3, bare.Count);

        var anonymous = await FirstRow(context).Select(r => new { r.Id, U = r.Tags.Union(Operand) }).SingleAsync();
        Assert.AreEqual(Target, anonymous.U);

        var dto = await FirstRow(context).Select(r => new UnionDto(r.Id, r.Tags.Union(Operand).Remove("z"))).SingleAsync();
        Assert.AreEqual(Target, dto.Union);

        var conditional = await FirstRow(context).Select(r => r.Id > 0 ? r.Tags.Union(Operand) : r.Tags).SingleAsync();
        Assert.AreEqual(Target, conditional);

        // A derived table's column, read by the outer SELECT — and its insensitive compositions.
        var throughDerivedTable = await Concatenated(context).OrderBy(a => a.Id).Select(a => a.U).ToListAsync();
        CollectionAssert.AreEqual(new[] { Target, Target }, throughDerivedTable);
        Assert.AreEqual(2, await Concatenated(context).CountAsync(a => a.U.IsSupersetOf(Target) && !a.U.IsEmpty));

        // A nested collection: its columns are part of the outermost projection too.
        var nested = await FirstRow(context)
                          .Select(r => new
                           {
                               r.Id,
                               Unions = context.Reservations.Where(o => o.Id == First || o.Id == Second)
                                               .OrderBy(o => o.Id)
                                               .Select(o => o.Tags.Union(Operand))
                                               .ToList()
                           })
                          .SingleAsync();
        CollectionAssert.AreEqual(new[] { Target, Target }, nested.Unions);

        // UNION ALL keeps rows rather than comparing them.
        var concatenated = await FirstRow(context).Select(r => r.Tags.Union(Operand))
                                                  .Concat(context.Reservations.Where(r => r.Id == Second).Select(r => r.Tags.Union(Operand)))
                                                  .ToListAsync();
        CollectionAssert.AreEqual(new[] { Target, Target }, concatenated);

        // Count in a projection falls back to the client, against the materialized set.
        Assert.AreEqual(3, await FirstRow(context).Select(r => r.Tags.Union(Operand).Count).SingleAsync());
        Assert.AreEqual(3, await FirstRow(context).Select(r => (r.OptionalTags ?? r.Tags.Union(Operand)).Count).SingleAsync());
    }

    // -- Fail-closed: the guard replaced away --

    /// <summary>
    /// A later <c>ReplaceService</c> of the processor factory drops the decorator the guard runs from.
    /// Before the final 9.0.0 review the refused shapes then ran silently and answered as in 8.x — here,
    /// no rows where memory finds both. Every server-side <c>Union</c> now fails instead, harmless
    /// compositions included: only the guard turns the translator's placeholder into <c>array_cat</c>.
    /// </summary>
    [TestMethod]
    public async Task WithTheProcessorFactoryReplaced_AUnionFailsInsteadOfAnsweringWrongly()
    {
        await (await Context()).DisposeAsync();
        await using var context = new ReplacedProcessorContext();
        Assert.IsInstanceOfType<PlainProcessorFactory>(context.GetService<IRelationalParameterBasedSqlProcessorFactory>());

        var inMemory = new[] { StringSet.From("a", "c"), StringSet.From("b", "c") }.Count(tags => tags.Union(Operand) == Target);
        Assert.AreEqual(2, inMemory);

        foreach (var query in new Func<Task>[]
                 {
                     () => context.Reservations.Where(r => (r.Id == First || r.Id == Second) && r.Tags.Union(Operand) == Target)
                                  .Select(r => r.Id).ToListAsync(),
                     () => context.Reservations.Where(r => r.Id == First && r.Tags.Union(Operand).Contains("a"))
                                  .Select(r => r.Id).ToListAsync()
                 })
        {
            var refusal = await Assert.ThrowsExactlyAsync<PostgresException>(query);
            Assert.AreEqual(PostgresErrorCodes.UndefinedFunction, refusal.SqlState);
            StringAssert.Contains(refusal.MessageText, "valueranges_union_without_sql_guard");
        }
    }

    private sealed record UnionDto(int Id, StringSet Union);

    /// <summary>The integration model's configuration, with the parameter-based processor factory replaced.</summary>
    private sealed class ReplacedProcessorContext : DbContext
    {
        public DbSet<Reservation> Reservations => Set<Reservation>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(
                                  ContainerLifecycle.ConnectionString!,
                                  npgsql => npgsql.UseValueRangesNodaTime()
                                                  .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()))
                             .ReplaceService<IRelationalParameterBasedSqlProcessorFactory, PlainProcessorFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Reservation>().Property(r => r.Id).ValueGeneratedNever();
    }

    /// <summary>What a library replacing the service registers: the provider's own processor, no guard.</summary>
    private sealed class PlainProcessorFactory(RelationalParameterBasedSqlProcessorDependencies dependencies)
        : IRelationalParameterBasedSqlProcessorFactory
    {
        public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
            => new NpgsqlParameterBasedSqlProcessorFactory(dependencies).Create(parameters);
    }
}

using System.Linq.Expressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>Length</c> on <see cref="Int32Range"/> and <see cref="Int64Range"/> runs as SQL and answers what memory
/// answers over the loaded rows (#405), for every condition on the 10.3.0 parity list (#408).
/// </summary>
/// <remarks>
/// <para>
/// <b>Shapes and extremes</b> are the rows: empty, finite, unbounded on either side, <c>(,)</c>, and the
/// widest ranges each type can store. <c>[-2147483648,2147483646]</c> holds 4,294,967,295 integers;
/// <c>[0, long.MaxValue - 1]</c> and <c>[long.MinValue, -2]</c> hold exactly <see cref="long.MaxValue"/>,
/// and one more, as in <c>[-1, long.MaxValue - 1]</c>, is <see langword="null"/> in memory and NULL on the
/// server. <c>[x, int.MaxValue]</c> is not among them: <c>int4range</c> cannot store it
/// (<c>docs/architecture.md#domain-width-and-the-boundary</c>).
/// </para>
/// <para>
/// <b>Nullable operands and composition</b> go through <see cref="CompositionRunner.Judge"/>: a nullable
/// column and the child of an outer join (both NULL on some rows, so rule (c) answers), each value in
/// <c>Where(== null)</c>, <c>Where(!= null)</c> and a final <c>Select</c>, and each predicate built on it
/// (a comparison, a compound one, a coalesce) in <c>Where</c>, <c>Where(!)</c> and <c>Select</c>. The
/// filters and the projection must also agree with each other, which is the declared nullability
/// holding. The composition sweeps (<c>OuterJoinProbeTests</c> and the others) run over the
/// <see cref="DateRange"/> family only, so they do not reach these two members.
/// </para>
/// <para>
/// <b>Ordering</b> is checked on its own: PostgreSQL sorts NULL last ascending and first descending, where
/// memory sorts it first ascending, which <c>docs/ranges.md#sort-ranges</c> documents for every ordering
/// over a value that can be missing (#359). So the reference places NULL where the server does, and the
/// order of everything else must be memory's.
/// </para>
/// </remarks>
[TestClass]
public sealed class IntegerLengthParityTests
{
    private static readonly Int32Range[] Seats =
    [
        Int32Range.Empty,
        Int32Range.CreateFinite(1, 10),
        Int32Range.Infinite,
        Int32Range.CreateUnboundedEnd(1),
        Int32Range.CreateUnboundedStart(10, endInclusive: true),
        Int32Range.CreateFinite(int.MinValue, int.MaxValue - 1),
        Int32Range.CreateFinite(5, 5),
        Int32Range.CreateFinite(-3, 3),
        Int32Range.CreateFinite(int.MinValue, int.MinValue),
        Int32Range.CreateFinite(int.MaxValue - 1, int.MaxValue - 1),
        Int32Range.CreateFinite(0, 1, startInclusive: false, endInclusive: false),
    ];

    private static readonly Int64Range[] Tickets =
    [
        Int64Range.Empty,
        Int64Range.CreateFinite(1, 10),
        Int64Range.Infinite,
        Int64Range.CreateUnboundedEnd(1),
        Int64Range.CreateUnboundedStart(10, endInclusive: true),
        Int64Range.CreateFinite(long.MinValue, long.MaxValue - 1),
        Int64Range.CreateFinite(0, long.MaxValue - 1),
        Int64Range.CreateFinite(-1, long.MaxValue - 1),
        Int64Range.CreateFinite(1, long.MaxValue - 1),
        Int64Range.CreateFinite(long.MinValue, -2),
        Int64Range.CreateFinite(long.MinValue, -1),
    ];

    [TestMethod]
    public async Task IntegerLength_InEveryQueryForm_AnswersWhatMemoryAnswers()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await Seed();

        await using var context = new LengthContext(connectionString);
        var loaded = await context.Rows.Include(r => r.Child).AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.IsTrue(loaded.Any(r => r.MaybeSeats is null) && loaded.Any(r => r.MaybeTickets is null) && loaded.Any(r => r.Child is null),
                      "Some rows must hold a NULL column and some no child, or rule (c) is never reached.");

        var failures = new List<string>();
        foreach (var (name, lambda) in Calls())
        {
            var outcome = await CompositionRunner.Judge(
                "integer length", new Call(name, lambda.Body), lambda.Parameters[0], context.Rows.AsNoTracking(), loaded,
                r => r.Id, id => $"{id} ({Describe(loaded.Single(r => r.Id == id))})", failures);

            if (outcome != Outcome.Compared) failures.Add($"{name}: {outcome}, where it must run as SQL and be compared");
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public async Task IntegerLength_InAnOrdering_SortsAsMemoryDoes_WithNullWherePostgresPutsIt()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await Seed();

        await using var context = new LengthContext(connectionString);
        var loaded = await context.Rows.AsNoTracking().ToListAsync();

        async Task Agree(string label, Expression<Func<LengthRow, long?>> key, bool descending)
        {
            var compiled = key.Compile();
            var server = descending
                ? await context.Rows.OrderByDescending(key).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync()
                : await context.Rows.OrderBy(key).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync();
            var memory = descending
                ? loaded.OrderBy(r => compiled(r) is not null).ThenByDescending(compiled).ThenBy(r => r.Id).Select(r => r.Id).ToList()
                : loaded.OrderBy(r => compiled(r) is null).ThenBy(compiled).ThenBy(r => r.Id).Select(r => r.Id).ToList();

            CollectionAssert.AreEqual(memory, server, $"{label}: server {string.Join(",", server)}, memory {string.Join(",", memory)}");
        }

        await Agree("OrderBy(Seats.Length)", r => r.Seats.Length, descending: false);
        await Agree("OrderByDescending(Seats.Length)", r => r.Seats.Length, descending: true);
        await Agree("OrderBy(Tickets.Length)", r => r.Tickets.Length, descending: false);
        await Agree("OrderByDescending(Tickets.Length)", r => r.Tickets.Length, descending: true);
    }

    private static IEnumerable<(string Name, LambdaExpression Lambda)> Calls()
    {
        (string, LambdaExpression) Value(string name, Expression<Func<LengthRow, long?>> lambda) => (name, lambda);
        (string, LambdaExpression) Predicate(string name, Expression<Func<LengthRow, bool>> lambda) => (name, lambda);

        yield return Value("r.Seats.Length", r => r.Seats.Length);
        yield return Value("r.Tickets.Length", r => r.Tickets.Length);
        yield return Value("r.MaybeSeats.Length", r => r.MaybeSeats!.Length);
        yield return Value("r.MaybeTickets.Length", r => r.MaybeTickets!.Length);
        yield return Value("r.Child.Seats.Length", r => r.Child!.Seats.Length);
        yield return Value("r.Child.Tickets.Length", r => r.Child!.Tickets.Length);
        yield return Value("r.Seats.Intersect(r.MaybeSeats).Length", r => r.Seats.Intersect(r.MaybeSeats!).Length);

        yield return Predicate("r.Seats.Length > 5", r => r.Seats.Length > 5);
        yield return Predicate("r.Seats.Length == 4294967295", r => r.Seats.Length == 4294967295L);
        yield return Predicate("r.Tickets.Length >= long.MaxValue - 1", r => r.Tickets.Length >= long.MaxValue - 1);
        yield return Predicate("r.MaybeSeats.Length == 10", r => r.MaybeSeats!.Length == 10);
        yield return Predicate("r.Child.Tickets.Length > 0", r => r.Child!.Tickets.Length > 0);
        yield return Predicate("r.Seats.Length > 5 && r.Seats.Contains(3)", r => r.Seats.Length > 5 && r.Seats.Contains(3));
        yield return Predicate("r.Tickets.Length == null || r.Tickets.IsEmpty()", r => r.Tickets.Length == null || r.Tickets.IsEmpty());
        yield return Predicate("(r.MaybeTickets.Length ?? -1) > 0", r => (r.MaybeTickets!.Length ?? -1) > 0);
        yield return Predicate("r.Seats.Length < r.Tickets.Length", r => r.Seats.Length < r.Tickets.Length);
    }

    private static string Describe(LengthRow row) =>
        $"Seats {row.Seats}, MaybeSeats {row.MaybeSeats?.ToString() ?? "NULL"}, Tickets {row.Tickets}, MaybeTickets {row.MaybeTickets?.ToString() ?? "NULL"}, "
      + (row.Child is { } child ? $"child {child.Seats} / {child.Tickets}" : "no child");

    /// <summary>A database of its own: one row per extreme, a NULL column every third row, a child every other row.</summary>
    private static async Task<string> Seed()
    {
        var connectionString = await CompositionDatabase.Create("integer_length");
        await using var seed = new LengthContext(connectionString);
        await seed.Database.EnsureCreatedAsync();

        var count = Math.Max(Seats.Length, Tickets.Length);
        for (var i = 0; i < count; i++)
        {
            seed.Rows.Add(new LengthRow
            {
                Id           = i + 1,
                Seats        = Seats[i % Seats.Length],
                MaybeSeats   = i % 3 == 0 ? null : Seats[(i + 1) % Seats.Length],
                Tickets      = Tickets[i % Tickets.Length],
                MaybeTickets = i % 3 == 1 ? null : Tickets[(i + 2) % Tickets.Length],
                Child        = i % 2 == 0 ? new LengthChild { Id = 100 + i, Seats = Seats[(i + 3) % Seats.Length], Tickets = Tickets[(i + 4) % Tickets.Length] } : null
            });
        }

        await seed.SaveChangesAsync();
        return connectionString;
    }

    public sealed class LengthRow
    {
        public int Id { get; set; }

        public Int32Range Seats { get; set; } = Int32Range.Empty;

        public Int32Range? MaybeSeats { get; set; }

        public Int64Range Tickets { get; set; } = Int64Range.Empty;

        public Int64Range? MaybeTickets { get; set; }

        public LengthChild? Child { get; set; }
    }

    public sealed class LengthChild
    {
        public int Id { get; set; }

        public int ParentId { get; set; }

        public Int32Range Seats { get; set; } = Int32Range.Empty;

        public Int64Range Tickets { get; set; } = Int64Range.Empty;
    }

    private sealed class LengthContext(string connectionString) : DbContext
    {
        public DbSet<LengthRow> Rows => Set<LengthRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LengthRow>().Property(r => r.Id).ValueGeneratedNever();
            modelBuilder.Entity<LengthChild>().Property(c => c.Id).ValueGeneratedNever();
            modelBuilder.Entity<LengthRow>().HasOne(r => r.Child).WithOne().HasForeignKey<LengthChild>(c => c.ParentId);
        }
    }
}

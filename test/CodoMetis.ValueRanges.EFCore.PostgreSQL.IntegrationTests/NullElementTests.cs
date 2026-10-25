using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A NULL element against live PostgreSQL: a nullable column read with <c>.Value</c>, which is what the
/// optional side of an outer join gives too. Row 1 has a <c>Day</c> inside every range, row 2 has none.
/// In memory the call cannot be made (<c>.Value</c> throws), so the reference is rule (c), EF Core's own
/// convention: a predicate over NULL is <c>false</c> and its negation <c>true</c> (#350, #381), and a
/// value built from NULL is NULL (#382).
/// </summary>
/// <remarks>
/// On <c>main</c> the composition sweeps (<c>OuterJoinProbeTests</c>, <c>NullableOperandPairProbeTests</c>,
/// <c>QuantifierProbeTests</c>) hold this over every shape; this line has none of them, so the ports
/// (#387, #388) hold the cases the issues name here.
/// </remarks>
[TestClass]
public class NullElementTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    public sealed class Row
    {
        public int Id { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;

        public RangeSet<DateRange, DateOnly> Blocks { get; set; } = RangeSet<DateRange, DateOnly>.Empty;

        public DateOnly Start { get; set; }

        public DateOnly? Day { get; set; }
    }

    /// <summary>A database of its own, so that the nullable element column does not change the shared schema.</summary>
    private sealed class Context(string connectionString) : DbContext
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>().Property(r => r.Id).ValueGeneratedNever();
    }

    private static async Task<Context> Seeded()
    {
        ContainerLifecycle.RequireServer();

        var name = $"null_element_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(ContainerLifecycle.ServerConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        var context = new Context(new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString) { Database = name }.ConnectionString);
        await context.Database.EnsureCreatedAsync();

        var period = DateRange.CreateFinite(March(1), March(10), true, true);
        context.Rows.AddRange(
            new Row { Id = 1, Period = period, Blocks = RangeSet<DateRange, DateOnly>.From([period]), Start = March(3), Day = March(7) },
            new Row { Id = 2, Period = period, Blocks = RangeSet<DateRange, DateOnly>.From([period]), Start = March(3), Day = null });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return context;
    }

    private static async Task<int[]> Ids(IQueryable<Row> rows) => [.. await rows.Select(r => r.Id).OrderBy(id => id).ToListAsync()];

    [TestMethod]
    public async Task Contains_OverANullElement_IsFalse_AndItsNegationTrue()
    {
        await using var db = await Seeded();

        CollectionAssert.AreEqual(new[] { 1 }, await Ids(db.Rows.Where(r => r.Period.Contains(r.Day!.Value))));
        CollectionAssert.AreEqual(new[] { 2 }, await Ids(db.Rows.Where(r => !r.Period.Contains(r.Day!.Value))));
        CollectionAssert.AreEqual(new[] { 1 }, await Ids(db.Rows.Where(r => r.Blocks.Contains(r.Day!.Value))));
        CollectionAssert.AreEqual(new[] { 2 }, await Ids(db.Rows.Where(r => !r.Blocks.Contains(r.Day!.Value))));
    }

    [TestMethod]
    public async Task Contains_OverANullElement_ProjectsFalse()
    {
        await using var db = await Seeded();

        // It threw InvalidOperationException reading the NULL into bool.
        CollectionAssert.AreEqual(new[] { true, false }, await db.Rows.OrderBy(r => r.Id).Select(r => r.Period.Contains(r.Day!.Value)).ToListAsync());
        CollectionAssert.AreEqual(new[] { true, false }, await db.Rows.OrderBy(r => r.Id).Select(r => r.Blocks.Contains(r.Day!.Value)).ToListAsync());
    }

    [TestMethod]
    public async Task All_OverACapturedList_DropsTheNullElement()
    {
        await using var db = await Seeded();
        var windows = new List<DateRange> { DateRange.CreateFinite(March(3), March(8), true, true) };

        // EF Core writes All as NOT EXISTS (… WHERE NOT (x @> e)); a NULL inner NOT made it true.
        CollectionAssert.AreEqual(new[] { 1 }, await Ids(db.Rows.Where(r => windows.All(x => x.Contains(r.Day!.Value)))));
        CollectionAssert.AreEqual(new[] { 2 }, await Ids(db.Rows.Where(r => !windows.All(x => x.Contains(r.Day!.Value)))));
    }

    [TestMethod]
    public async Task RangeFactories_OverANullElement_AreNull()
    {
        await using var db = await Seeded();
        var v = March(9);

        // The unbounded factories gave (,) and CreateFinite gave empty, so these selected row 2.
        CollectionAssert.AreEqual(new[] { 1 }, await Ids(db.Rows.Where(r => DateRange.CreateUnboundedEnd(r.Day!.Value, true).Contains(v))));
        CollectionAssert.AreEqual(new[] { 2 }, await Ids(db.Rows.Where(r => !DateRange.CreateUnboundedEnd(r.Day!.Value, true).Contains(v))));
        CollectionAssert.AreEqual(new[] { 2 }, await Ids(db.Rows.Where(r => DateRange.CreateUnboundedStart(r.Day!.Value, true) == null)));
        // Row 1's bounds are inverted, so it is empty, as in memory; row 2's range is NULL.
        CollectionAssert.AreEqual(new[] { 1 }, await Ids(db.Rows.Where(r => DateRange.CreateFinite(r.Day!.Value, r.Start, true, true).IsEmpty())));

        var built = await db.Rows.OrderBy(r => r.Id).Select(r => DateRange.CreateUnboundedStart(r.Day!.Value, true)).ToListAsync();
        CollectionAssert.AreEqual(new DateRange?[] { DateRange.CreateUnboundedStart(March(7), true), null }, built);
    }

    [TestMethod]
    public async Task RangeFactories_OverANullElement_AreNull_WhereAPredicateConsumesThem()
    {
        await using var db = await Seeded();
        var v = March(9);
        var windows = new List<DateRange> { DateRange.CreateFinite(March(3), March(8), true, true) };

        // Built from a NULL element, (,) contained and overlapped everything, so each of these answered true for row 2.
        CollectionAssert.AreEqual(new[] { true, false }, await db.Rows.OrderBy(r => r.Id).Select(r => DateRange.CreateUnboundedEnd(r.Day!.Value, true).Contains(v)).ToListAsync());
        CollectionAssert.AreEqual(new[] { 1 }, await Ids(db.Rows.Where(r => r.Period.Overlaps(DateRange.CreateUnboundedEnd(r.Day!.Value, true)))));
        CollectionAssert.AreEqual(new[] { 2 }, await Ids(db.Rows.Where(r => !r.Period.Overlaps(DateRange.CreateUnboundedEnd(r.Day!.Value, true)))));
        CollectionAssert.AreEqual(new[] { true, false }, await db.Rows.OrderBy(r => r.Id).Select(r => r.Period.Overlaps(DateRange.CreateUnboundedEnd(r.Day!.Value, true))).ToListAsync());
        CollectionAssert.AreEqual(new[] { 1 }, await Ids(db.Rows.Where(r => windows.All(x => x.Overlaps(DateRange.CreateUnboundedEnd(r.Day!.Value, true))))));
        CollectionAssert.AreEqual(new[] { 2 }, await Ids(db.Rows.Where(r => !windows.All(x => x.Overlaps(DateRange.CreateUnboundedEnd(r.Day!.Value, true))))));
    }
}

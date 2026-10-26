using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.InfinityDisabled.IntegrationTests;

/// <summary>
/// A <c>YearMonthRange</c> upper bound read in a query with the switch set (#506). 9999-12 ends on 9999-12-31, which
/// Npgsql then sends as a date, so the server keeps <c>[…,10000-01-01)</c>: no row with that end loads, but a filter
/// reads the bound a month back, at 9999-12-01, where a month operand binds, without the <c>infinity</c> arm.
/// </summary>
[TestClass]
public sealed class YearMonthUpperBoundTests
{
    [TestMethod]
    public async Task AYearMonthUpperBound_ComparesWithItsMonth_InAFilter()
    {
        ContainerLifecycle.RequireDatabase();

        YearMonth[] months = [new(2026, 1), new(9999, 11), new(9999, 12)];
        await using (var write = new MonthContext())
        {
            await write.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS ym_bounds (\"Id\" integer PRIMARY KEY, \"Period\" daterange NOT NULL)");
            write.Rows.AddRange(months.Select((month, i) => new MonthRow { Id = 506 + i, Period = YearMonthRange.CreateFinite(new YearMonth(2025, 6), month) }));
            await write.SaveChangesAsync();
        }

        await using var context = new MonthContext();
        foreach (var (month, i) in months.Select((month, i) => (month, i)))
        {
            var id = 506 + i;
            Assert.AreEqual(1, await context.Rows.CountAsync(r => r.Id == id && r.Period.UpperBound() == month), $"UpperBound() == {month}");
            Assert.AreEqual(0, await context.Rows.CountAsync(r => r.Id == id && r.Period.UpperBound() > month), $"UpperBound() > {month}");
        }
    }

    public sealed class MonthRow
    {
        public int Id { get; set; }
        public YearMonthRange Period { get; set; } = YearMonthRange.Empty;
    }

    private sealed class MonthContext : DbContext
    {
        public DbSet<MonthRow> Rows => Set<MonthRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(ContainerLifecycle.ConnectionString!, npgsql => npgsql.UseValueRangesNodaTime());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<MonthRow>(row =>
            {
                row.ToTable("ym_bounds");
                row.Property(r => r.Id).ValueGeneratedNever();
            });
    }
}

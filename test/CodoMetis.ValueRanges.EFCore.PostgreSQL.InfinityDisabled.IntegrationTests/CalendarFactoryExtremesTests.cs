using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.InfinityDisabled.IntegrationTests;

/// <summary>
/// <c>DateRange.Month</c>, <c>Quarter</c>, <c>Year</c> and <c>IsoWeek</c> over a column at the extremes with
/// <c>Npgsql.DisableDateTimeInfinityConversions</c> set (#191): the first and last periods hold
/// 0001-01-01 and 9999-12-31 as days, as Npgsql binds memory's answer here, not the infinities they become
/// with the conversions on (<c>CalendarFactoryParityTests</c>). A range ending on 9999-12-31 is stored as
/// <c>[…,10000-01-01)</c>, which neither side can load, so those are compared in a filter.
/// </summary>
[TestClass]
public sealed class CalendarFactoryExtremesTests
{
    [TestMethod]
    public async Task CalendarFactories_AtTheExtremes_AreTheRangesMemoryBinds()
    {
        ContainerLifecycle.RequireDatabase();

        var name = $"calendar_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(ContainerLifecycle.ConnectionString) { Database = name }.ConnectionString;
        (DateOnly Day, int Year, int Month)[] rows = [(DateOnly.MinValue, 1, 1), (DateOnly.MaxValue, 9999, 12), (new DateOnly(1, 1, 5), 1, 12)];

        await using (var seed = new CalendarContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Rows.AddRange(rows.Select((row, index) => new CalendarRow
            {
                Id = index + 1, Day = row.Day, Year = row.Year, Month = row.Month,
                MonthOf = DateRange.Month(row.Day), QuarterOf = DateRange.Quarter(row.Day), YearOf = DateRange.Year(row.Day),
                MonthOfParts = DateRange.Month(row.Year, row.Month)
            }));
            await seed.SaveChangesAsync();
        }

        await using var context = new CalendarContext(connectionString);
        var failures = new List<string>();

        async Task AllRows(string label, System.Linq.Expressions.Expression<Func<CalendarRow, bool>> predicate)
        {
            var selected = await context.Rows.Where(predicate).Select(r => r.Id).OrderBy(id => id).ToListAsync();
            if (selected.Count != rows.Length) failures.Add($"{label}: selects rows {string.Join(",", selected)} of 1 to {rows.Length}");
        }

        await AllRows("Month(r.Day) == r.MonthOf", r => DateRange.Month(r.Day) == r.MonthOf);
        await AllRows("Quarter(r.Day) == r.QuarterOf", r => DateRange.Quarter(r.Day) == r.QuarterOf);
        await AllRows("Year(r.Day) == r.YearOf", r => DateRange.Year(r.Day) == r.YearOf);
        await AllRows("Month(r.Year, r.Month) == r.MonthOfParts", r => DateRange.Month(r.Year, r.Month) == r.MonthOfParts);
        await AllRows("Year(r.Day).Contains(r.Day)", r => DateRange.Year(r.Day).Contains(r.Day));

        var first = await context.Rows.Where(r => r.Id == 1).Select(r => new { Week = DateRange.IsoWeek(r.Day), Month = DateRange.Month(r.Year, r.Month) }).SingleAsync();
        if (first.Week != DateRange.IsoWeek(DateOnly.MinValue)) failures.Add($"IsoWeek(0001-01-01): server {first.Week}, memory {DateRange.IsoWeek(DateOnly.MinValue)}");
        if (first.Month != DateRange.Month(1, 1)) failures.Add($"Month(1, 1): server {first.Month}, memory {DateRange.Month(1, 1)}");

        // Without the conversions the store holds the day: no -infinity in the range PostgreSQL built.
        var text = await context.Rows.Where(r => r.Id == 1).Select(r => DateRange.Month(r.Day) == r.MonthOf && r.MonthOf.LowerBound() == DateOnly.MinValue).SingleAsync();
        if (!text) failures.Add("Month(0001-01-01) does not start on the day 0001-01-01");

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    public sealed class CalendarRow
    {
        public int Id { get; set; }

        public DateOnly Day { get; set; }

        public int Year { get; set; }

        public int Month { get; set; }

        public DateRange MonthOf { get; set; } = DateRange.Empty;

        public DateRange QuarterOf { get; set; } = DateRange.Empty;

        public DateRange YearOf { get; set; } = DateRange.Empty;

        public DateRange MonthOfParts { get; set; } = DateRange.Empty;
    }

    private sealed class CalendarContext(string connectionString) : DbContext
    {
        public DbSet<CalendarRow> Rows => Set<CalendarRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<CalendarRow>().Property(r => r.Id).ValueGeneratedNever();
    }
}

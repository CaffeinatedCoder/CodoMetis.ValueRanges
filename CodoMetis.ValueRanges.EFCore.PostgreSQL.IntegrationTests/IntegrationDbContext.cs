using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// An entity carrying one property per supported range type plus two multirange
/// properties. Ids are assigned by the tests (no identity column) so that parallel
/// tests can insert disjoint rows without coordination.
/// </summary>
public class Reservation
{
    public int Id { get; set; }

    public Int32Range Seats { get; set; } = Int32Range.Empty;

    public Int64Range Tickets { get; set; } = Int64Range.Empty;

    public DecimalRange Price { get; set; } = DecimalRange.Empty;

    public DateRange Period { get; set; } = DateRange.Empty;

    public DateTimeRange LocalTime { get; set; } = DateTimeRange.Empty;

    public DateTimeOffsetRange InstantTime { get; set; } = DateTimeOffsetRange.Empty;

    public RangeSet<DateRange, DateOnly> BlockedDays { get; set; } = RangeSet<DateRange, DateOnly>.Empty;

    public RangeSet<Int32Range, int> SeatBlocks { get; set; } = RangeSet<Int32Range, int>.Empty;

    public int GroupKey { get; set; }
}

public sealed class IntegrationDbContext : DbContext
{
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            ContainerLifecycle.ConnectionString!,
            npgsql => npgsql.UseValueRanges());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Reservation>().Property(r => r.Id).ValueGeneratedNever();
}

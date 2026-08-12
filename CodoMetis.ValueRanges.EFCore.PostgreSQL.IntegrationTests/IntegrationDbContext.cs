using Microsoft.EntityFrameworkCore;
using NodaTime;

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

    // NodaTime range types — same PostgreSQL column types as their BCL counterparts,
    // mapped by the NodaTime satellite package in the same model.
    public LocalDateRange NodaPeriod { get; set; } = LocalDateRange.Empty;

    public LocalDateTimeRange NodaWallClock { get; set; } = LocalDateTimeRange.Empty;

    public InstantRange NodaWindow { get; set; } = InstantRange.Empty;

    public RangeSet<LocalDateRange, LocalDate> NodaBlockedDays { get; set; } = RangeSet<LocalDateRange, LocalDate>.Empty;

    public RangeSet<InstantRange, Instant> NodaWindows { get; set; } = RangeSet<InstantRange, Instant>.Empty;

    public int GroupKey { get; set; }
}

public sealed class IntegrationDbContext : DbContext
{
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            ContainerLifecycle.ConnectionString!,
            // Implies UseNodaTime() and UseValueRanges() — the BCL range types keep working.
            npgsql => npgsql.UseValueRangesNodaTime());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Reservation>().Property(r => r.Id).ValueGeneratedNever();
}

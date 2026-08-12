using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// An entity carrying one property per NodaTime range type, their multirange counterparts,
/// scalar NodaTime columns, and one BCL range property to prove the two families coexist in
/// a single model. No database is required — the tests only build models and generate SQL.
/// </summary>
public class Reservation
{
    public int Id { get; set; }

    public LocalDateRange Period { get; set; } = LocalDateRange.Empty;

    public LocalDateTimeRange WallClockSlot { get; set; } = LocalDateTimeRange.Empty;

    public InstantRange Window { get; set; } = InstantRange.Empty;

    public RangeSet<LocalDateRange, LocalDate> BlockedDays { get; set; } = RangeSet<LocalDateRange, LocalDate>.Empty;

    public RangeSet<InstantRange, Instant> Windows { get; set; } = RangeSet<InstantRange, Instant>.Empty;

    // Month granularity, stored as a month-aligned daterange.
    public YearMonthRange BillingPeriod { get; set; } = YearMonthRange.Empty;

    public RangeSet<YearMonthRange, YearMonth> BillingPeriods { get; set; } = RangeSet<YearMonthRange, YearMonth>.Empty;

    public LocalDate Day { get; set; }

    public LocalDate OtherDay { get; set; }

    public Instant At { get; set; }

    public int CustomerId { get; set; }

    // BCL range in the same model — both plugins are active side by side.
    public DateRange LegacyPeriod { get; set; } = DateRange.Empty;
}

public sealed class NodaTestDbContext : DbContext
{
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            "Host=localhost;Database=valueranges_nodatime_tests;Username=postgres",
            npgsql => npgsql.UseValueRangesNodaTime());
}

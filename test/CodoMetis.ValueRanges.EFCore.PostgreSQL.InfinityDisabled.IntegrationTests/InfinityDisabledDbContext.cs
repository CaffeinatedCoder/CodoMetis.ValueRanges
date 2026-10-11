using CodoMetis.ValueRanges.Conventions.Tests;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.InfinityDisabled.IntegrationTests;

public sealed class ExtremesRow
{
    public int Id { get; set; }
    public ExtremesValues Native { get; set; } = new();
    public ExtremesValues Doc { get; set; } = new();
}

/// <summary>Every temporal type with an extreme Npgsql would otherwise map to infinity.</summary>
public sealed class ExtremesValues
{
    public DateTimeRange                     Wall      { get; set; } = DateTimeRange.Empty;
    public DateTimeOffsetRange               Stz       { get; set; } = DateTimeOffsetRange.Empty;
    public DateRange                         Day       { get; set; } = DateRange.Empty;
    public LocalDateTimeRange                NWall     { get; set; } = LocalDateTimeRange.Empty;
    public InstantRange                      NInst     { get; set; } = InstantRange.Empty;
    public LocalDateRange                    NDay      { get; set; } = LocalDateRange.Empty;
    public RangeSet<DateTimeRange, DateTime> WallMulti { get; set; } = RangeSet<DateTimeRange, DateTime>.Empty;
    public DateTimeSet                       WallSet   { get; set; } = DateTimeSet.Empty;
    public DateTimeOffsetSet                 StzSet    { get; set; } = DateTimeOffsetSet.Empty;
    public DateSet                           DaySet    { get; set; } = DateSet.Empty;
    public InstantSet                        NInstSet  { get; set; } = InstantSet.Empty;
    public LocalDateTimeSet                  NWallSet  { get; set; } = LocalDateTimeSet.Empty;
    public LocalDateSet                      NDaySet   { get; set; } = LocalDateSet.Empty;
    public DateTimeSet<AuditStamp>           Audits    { get; set; } = DateTimeSet<AuditStamp>.Empty;
}

public sealed class InfinityDisabledDbContext : DbContext
{
    public DbSet<ExtremesRow> Rows => Set<ExtremesRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(ContainerLifecycle.ConnectionString!, npgsql => npgsql.UseValueRangesNodaTime());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<ExtremesRow>(row =>
        {
            row.Property(r => r.Id).ValueGeneratedNever();
            row.ComplexProperty(r => r.Native);
            row.ComplexProperty(r => r.Doc, doc => doc.ToJson());
        });
}

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

    // A custom PostgreSQL range type — CREATE TYPE timerange AS RANGE (subtype = time),
    // declared via HasPostgresRange below; PostgreSQL auto-creates timemultirange.
    public TimeRange OpeningHours { get; set; } = TimeRange.Empty;

    public RangeSet<TimeRange, TimeOnly> OpeningWindows { get; set; } = RangeSet<TimeRange, TimeOnly>.Empty;

    // Month granularity, stored as a month-aligned daterange — no custom type needed.
    public YearMonthRange BillingPeriod { get; set; } = YearMonthRange.Empty;

    public RangeSet<YearMonthRange, YearMonth> BillingPeriods { get; set; } = RangeSet<YearMonthRange, YearMonth>.Empty;

    public int GroupKey { get; set; }

    // -- Value set properties: BCL closed types --

    public StringSet Tags { get; set; } = StringSet.Empty;

    /// <summary>Nullable on purpose: NULL column and empty array are distinct states.</summary>
    public StringSet? OptionalTags { get; set; }

    public StringSet<TestKey> Permissions { get; set; } = StringSet<TestKey>.Empty;

    public GuidSet Uuids { get; set; } = GuidSet.Empty;

    public Int16Set SmallCodes { get; set; } = Int16Set.Empty;

    public Int32Set Codes { get; set; } = Int32Set.Empty;

    public Int64Set BigCodes { get; set; } = Int64Set.Empty;

    public DecimalSet Rates { get; set; } = DecimalSet.Empty;

    public DateSet BlackoutDates { get; set; } = DateSet.Empty;

    public TimeSet Slots { get; set; } = TimeSet.Empty;

    public DateTimeSet Marks { get; set; } = DateTimeSet.Empty;

    public DateTimeOffsetSet Stamps { get; set; } = DateTimeOffsetSet.Empty;

    // -- Value set properties: NodaTime satellite --

    public LocalDateSet NodaHolidays { get; set; } = LocalDateSet.Empty;

    public LocalDateTimeSet NodaMarks { get; set; } = LocalDateTimeSet.Empty;

    public InstantSet NodaOccurrences { get; set; } = InstantSet.Empty;

    public LocalTimeSet NodaSlots { get; set; } = LocalTimeSet.Empty;

    // Month granularity, stored as a month-aligned date[].
    public YearMonthSet BillingMonths { get; set; } = YearMonthSet.Empty;
}

/// <summary>
/// A generator-shaped validated wrapper (string-backed) — the consumer shape for
/// <see cref="StringSet{TElement}"/>, exercised end to end against live PostgreSQL.
/// </summary>
public readonly record struct TestKey : IFormattable, IParsable<TestKey>
{
    private readonly string _value;

    private TestKey(string value) => _value = value;

    public static TestKey Parse(string s, IFormatProvider? provider)
        => string.IsNullOrWhiteSpace(s) || !s.Contains('.')
               ? throw new FormatException($"'{s}' is not a valid key.")
               : new TestKey(s.Trim().ToLowerInvariant());

    public static bool TryParse(string? s, IFormatProvider? provider, out TestKey result)
    {
        try
        {
            result = Parse(s!, provider);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public string ToString(string? format, IFormatProvider? formatProvider) => _value;

    public override string ToString() => _value;
}

public sealed class IntegrationDbContext : DbContext
{
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            ContainerLifecycle.ConnectionString!,
            // Implies UseNodaTime() and UseValueRanges() — the BCL range types keep working.
            // EnableUnmappedTypes lets Npgsql put the custom timerange type on the wire;
            // Npgsql resolves it dynamically instead of from its built-in mappings.
            npgsql => npgsql.UseValueRangesNodaTime()
                            .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reservation>().Property(r => r.Id).ValueGeneratedNever();

        // Generates CREATE TYPE timerange AS RANGE (SUBTYPE = time) ahead of the tables.
        modelBuilder.HasPostgresRange("timerange", "time");
    }
}

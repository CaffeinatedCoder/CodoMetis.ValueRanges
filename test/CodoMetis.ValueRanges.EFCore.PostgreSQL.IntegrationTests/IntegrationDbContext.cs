using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// An entity carrying one property per supported range type plus two multirange
/// properties. Ids are assigned by the tests (no identity column) so that parallel
/// tests can insert disjoint rows without coordination.
/// </summary>
public partial class Reservation
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

    // -- The multiranges that had no mapped column until 7.0.0 --
    //
    // Six of the eleven multirange types were exercised end to end; these five had never been
    // mapped to a column, so nothing proved their store type, their literal form or their
    // round trip. Every one is covered by MultirangeCoverageTests.

    public RangeSet<Int64Range, long> TicketBlocks { get; set; } = RangeSet<Int64Range, long>.Empty;

    public RangeSet<DecimalRange, decimal> PriceBands { get; set; } = RangeSet<DecimalRange, decimal>.Empty;

    public RangeSet<DateTimeRange, DateTime> WallClockWindows { get; set; } = RangeSet<DateTimeRange, DateTime>.Empty;

    public RangeSet<DateTimeOffsetRange, DateTimeOffset> InstantWindows { get; set; } =
        RangeSet<DateTimeOffsetRange, DateTimeOffset>.Empty;

    public RangeSet<LocalDateTimeRange, LocalDateTime> NodaWallClocks { get; set; } =
        RangeSet<LocalDateTimeRange, LocalDateTime>.Empty;

    // -- Element-typed scalars, so a range constructor can be built from columns server-side --

    public DateOnly Day { get; set; }

    public DateOnly OtherDay { get; set; }

    public decimal Amount { get; set; }

    public decimal OtherAmount { get; set; }

    public TimeOnly At { get; set; }

    public TimeOnly Until { get; set; }

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

    // -- Value set properties: wrapper arities over lossy-by-default text forms --
    //
    // The temporal and NodaTime arities bridge to the store through a text form, so these are
    // the properties that prove the pinned format actually survives a real round trip rather
    // than only a generated literal.

    public DateTimeSet<AuditStamp> Audits { get; set; } = DateTimeSet<AuditStamp>.Empty;

    public YearMonthSet<BillingMonth> WrappedMonths { get; set; } = YearMonthSet<BillingMonth>.Empty;
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

/// <summary>
/// A generator-shaped validated wrapper (DateTime-backed) — the consumer shape for
/// <see cref="DateTimeSet{TElement}"/>. It forwards the format specifier to the value it
/// wraps, which is what the temporal arities' contract requires and what the generators emit.
/// </summary>
public readonly record struct AuditStamp : IFormattable, IParsable<AuditStamp>, IComparable<AuditStamp>
{
    private readonly DateTime _value;

    public AuditStamp(DateTime value) => _value = value;

    public static AuditStamp Parse(string s, IFormatProvider? provider)
        => new(DateTime.Parse(s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public static bool TryParse(string? s, IFormatProvider? provider, out AuditStamp result)
    {
        var parsed = DateTime.TryParse(
            s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);
        result = parsed ? new AuditStamp(value) : default;
        return parsed;
    }

    public int CompareTo(AuditStamp other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>
/// A generator-shaped validated wrapper (YearMonth-backed) — the consumer shape for
/// <see cref="YearMonthSet{TElement}"/>, whose bridge additionally changes granularity: the
/// element speaks <c>2024-06</c> and the column holds <c>2024-06-01</c>.
/// </summary>
public readonly record struct BillingMonth : IFormattable, IParsable<BillingMonth>, IComparable<BillingMonth>
{
    private readonly YearMonth _value;

    public BillingMonth(YearMonth value) => _value = value;

    public static BillingMonth Parse(string s, IFormatProvider? provider)
        => new(YearMonthPattern.Iso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out BillingMonth result)
    {
        var parsed = s is not null && YearMonthPattern.Iso.Parse(s).Success;
        result = parsed ? new BillingMonth(YearMonthPattern.Iso.Parse(s!).Value) : default;
        return parsed;
    }

    public int CompareTo(BillingMonth other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => YearMonthPattern.Iso.Format(_value);
}

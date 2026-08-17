using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;

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

    // -- NodaTime value set properties --

    public LocalDateSet Holidays { get; set; } = LocalDateSet.Empty;

    public LocalDateTimeSet WallClockMarks { get; set; } = LocalDateTimeSet.Empty;

    public InstantSet Occurrences { get; set; } = InstantSet.Empty;

    public LocalTimeSet Slots { get; set; } = LocalTimeSet.Empty;

    // Month granularity, stored as a month-aligned date[].
    public YearMonthSet BillingMonths { get; set; } = YearMonthSet.Empty;

    // BCL set in the same model — both set families coexist.
    public StringSet Tags { get; set; } = StringSet.Empty;

    // -- NodaTime validated-wrapper arities --
    //
    // These are registered as open generic families through SetTypeRegistry.RegisterFamily from
    // UseValueRangesNodaTime(), not as closed definitions, so they are the properties that prove
    // that seam works at all.

    public LocalDateSet<CalendarDay> WrappedHolidays { get; set; } = LocalDateSet<CalendarDay>.Empty;

    public LocalDateTimeSet<WallClockStamp> WrappedMarks { get; set; } = LocalDateTimeSet<WallClockStamp>.Empty;

    public InstantSet<EventInstant> WrappedOccurrences { get; set; } = InstantSet<EventInstant>.Empty;

    public LocalTimeSet<OpeningTime> WrappedSlots { get; set; } = LocalTimeSet<OpeningTime>.Empty;

    // The one arity whose element and column disagree on granularity: 2024-06 vs 2024-06-01.
    public YearMonthSet<BillingMonth> WrappedMonths { get; set; } = YearMonthSet<BillingMonth>.Empty;
}

public sealed class NodaTestDbContext : DbContext
{
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            "Host=localhost;Database=valueranges_nodatime_tests;Username=postgres",
            npgsql => npgsql.UseValueRangesNodaTime());
}

// Generator-shaped wrapper elements over NodaTime values. Each forwards the format argument, so
// the ISO pattern the family pins reaches the wrapped value — NodaTime's null-format output is
// the culture's form, which is the whole reason those families pin one.

public readonly record struct CalendarDay : IFormattable, IParsable<CalendarDay>, IComparable<CalendarDay>
{
    private readonly LocalDate _value;

    public CalendarDay(LocalDate value) => _value = value;

    public static CalendarDay Parse(string s, IFormatProvider? provider)
        => new(LocalDatePattern.Iso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out CalendarDay result)
    {
        var parsed = s is null ? null : LocalDatePattern.Iso.Parse(s);
        result = parsed is { Success: true } ? new CalendarDay(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(CalendarDay other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalDatePattern.Iso.Format(_value);
}

public readonly record struct WallClockStamp : IFormattable, IParsable<WallClockStamp>, IComparable<WallClockStamp>
{
    private readonly LocalDateTime _value;

    public WallClockStamp(LocalDateTime value) => _value = value;

    public static WallClockStamp Parse(string s, IFormatProvider? provider)
        => new(LocalDateTimePattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out WallClockStamp result)
    {
        var parsed = s is null ? null : LocalDateTimePattern.ExtendedIso.Parse(s);
        result = parsed is { Success: true } ? new WallClockStamp(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(WallClockStamp other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalDateTimePattern.ExtendedIso.Format(_value);
}

public readonly record struct EventInstant : IFormattable, IParsable<EventInstant>, IComparable<EventInstant>
{
    private readonly Instant _value;

    public EventInstant(Instant value) => _value = value;

    public static EventInstant Parse(string s, IFormatProvider? provider)
        => new(InstantPattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out EventInstant result)
    {
        var parsed = s is null ? null : InstantPattern.ExtendedIso.Parse(s);
        result = parsed is { Success: true } ? new EventInstant(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(EventInstant other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => InstantPattern.ExtendedIso.Format(_value);
}

public readonly record struct OpeningTime : IFormattable, IParsable<OpeningTime>, IComparable<OpeningTime>
{
    private readonly LocalTime _value;

    public OpeningTime(LocalTime value) => _value = value;

    public static OpeningTime Parse(string s, IFormatProvider? provider)
        => new(LocalTimePattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out OpeningTime result)
    {
        var parsed = s is null ? null : LocalTimePattern.ExtendedIso.Parse(s);
        result = parsed is { Success: true } ? new OpeningTime(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(OpeningTime other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalTimePattern.ExtendedIso.Format(_value);
}

public readonly record struct BillingMonth : IFormattable, IParsable<BillingMonth>, IComparable<BillingMonth>
{
    private readonly YearMonth _value;

    public BillingMonth(YearMonth value) => _value = value;

    public static BillingMonth Parse(string s, IFormatProvider? provider)
        => new(YearMonthPattern.Iso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out BillingMonth result)
    {
        var parsed = s is null ? null : YearMonthPattern.Iso.Parse(s);
        result = parsed is { Success: true } ? new BillingMonth(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(BillingMonth other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => YearMonthPattern.Iso.Format(_value);
}

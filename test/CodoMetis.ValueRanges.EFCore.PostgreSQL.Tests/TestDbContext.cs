using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// An entity carrying one property per supported range type, plus set and scalar
/// properties used by the translation tests. No database is required — the tests only
/// build models and generate SQL.
/// </summary>
public class Booking
{
    public int Id { get; set; }

    public Int32Range Seats { get; set; } = Int32Range.Empty;

    public Int64Range Tickets { get; set; } = Int64Range.Empty;

    public DecimalRange Price { get; set; } = DecimalRange.Empty;

    public DateRange Period { get; set; } = DateRange.Empty;

    public DateTimeRange LocalTime { get; set; } = DateTimeRange.Empty;

    public DateTimeOffsetRange InstantTime { get; set; } = DateTimeOffsetRange.Empty;

    public TimeRange OpeningHours { get; set; } = TimeRange.Empty;

    public RangeSet<TimeRange, TimeOnly> OpeningWindows { get; set; } = RangeSet<TimeRange, TimeOnly>.Empty;

    public RangeSet<DateRange, DateOnly> BlockedDays { get; set; } = RangeSet<DateRange, DateOnly>.Empty;

    public RangeSet<Int32Range, int> SeatBlocks { get; set; } = RangeSet<Int32Range, int>.Empty;

    public DateOnly Day { get; set; }

    public DateOnly OtherDay { get; set; }

    public decimal Amount { get; set; }

    public TimeOnly At { get; set; }

    // -- Value set properties (one per closed type, plus a wrapper instantiation) --

    public StringSet Tags { get; set; } = StringSet.Empty;

    public StringSet<TestKey> Permissions { get; set; } = StringSet<TestKey>.Empty;

    public GuidSet Uuids { get; set; } = GuidSet.Empty;

    public Int16Set SmallCodes { get; set; } = Int16Set.Empty;

    public Int32Set Codes { get; set; } = Int32Set.Empty;

    public Int64Set BigCodes { get; set; } = Int64Set.Empty;

    public DecimalSet Rates { get; set; } = DecimalSet.Empty;

    public DateSet BlackoutDays { get; set; } = DateSet.Empty;

    public TimeSet Slots { get; set; } = TimeSet.Empty;

    public DateTimeSet WallClocks { get; set; } = DateTimeSet.Empty;

    public DateTimeOffsetSet Instants { get; set; } = DateTimeOffsetSet.Empty;

    public DateTimeSet<TestStamp> Audits { get; set; } = DateTimeSet<TestStamp>.Empty;

    public DecimalSet<TestRate> WrappedRates { get; set; } = DecimalSet<TestRate>.Empty;

    /// <summary>A plain CLR array beside the set types — must keep its native provider mapping.</summary>
    public string[] PlainTags { get; set; } = [];

    public string Tag { get; set; } = "";
}

/// <summary>
/// A generator-shaped validated wrapper (string-backed), the consumer shape for
/// <see cref="StringSet{TElement}"/>.
/// </summary>
public readonly record struct TestKey : IFormattable, IParsable<TestKey>
{
    private readonly string _value;

    private TestKey(string value) => _value = value;

    public static TestKey Parse(string s, IFormatProvider? provider)
        => string.IsNullOrWhiteSpace(s)
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

/// <summary>
/// A generator-shaped validated wrapper (DateTime-backed), the consumer shape for
/// <see cref="DateTimeSet{TElement}"/>. It forwards the format specifier, which is what the
/// temporal arities require: their bridge asks for <c>O</c>, and a wrapper that answered with
/// its own default would hand the store a value truncated to whole seconds.
/// </summary>
public readonly record struct TestStamp : IFormattable, IParsable<TestStamp>, IComparable<TestStamp>
{
    private readonly DateTime _value;

    private TestStamp(DateTime value) => _value = value;

    public static TestStamp Parse(string s, IFormatProvider? provider)
        => new(DateTime.Parse(s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestStamp result)
    {
        var parsed = DateTime.TryParse(
            s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);
        result = parsed ? new TestStamp(value) : default;
        return parsed;
    }

    public int CompareTo(TestStamp other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>
/// A generator-shaped validated wrapper (decimal-backed), the consumer shape for
/// <see cref="DecimalSet{TElement}"/>.
/// </summary>
public readonly record struct TestRate : IFormattable, IParsable<TestRate>, IComparable<TestRate>
{
    private readonly decimal _value;

    private TestRate(decimal value) => _value = value;

    public static TestRate Parse(string s, IFormatProvider? provider)
        => new(decimal.Parse(s, NumberStyles.Number, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestRate result)
    {
        var parsed = decimal.TryParse(s, NumberStyles.Number, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new TestRate(value) : default;
        return parsed;
    }

    public int CompareTo(TestRate other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public sealed class TestDbContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            "Host=localhost;Database=valueranges_tests;Username=postgres",
            npgsql => npgsql.UseValueRanges());
}

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

    // The remaining arities. One property per family, so the model-mapping tests can pin the
    // column type of every one rather than only the three the literal tests exercise.

    public GuidSet<TestUuid> WrappedUuids { get; set; } = GuidSet<TestUuid>.Empty;

    public Int16Set<TestSmallCode> WrappedSmallCodes { get; set; } = Int16Set<TestSmallCode>.Empty;

    public Int32Set<TestCode> WrappedCodes { get; set; } = Int32Set<TestCode>.Empty;

    public Int64Set<TestBigCode> WrappedBigCodes { get; set; } = Int64Set<TestBigCode>.Empty;

    public DateSet<TestDay> WrappedDays { get; set; } = DateSet<TestDay>.Empty;

    public TimeSet<TestSlot> WrappedSlots { get; set; } = TimeSet<TestSlot>.Empty;

    public DateTimeOffsetSet<TestInstant> WrappedInstants { get; set; } = DateTimeOffsetSet<TestInstant>.Empty;

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

// The remaining wrapper element types, one per arity. All are the generator shape: a readonly
// record struct over a private backing field, forwarding the format argument so whatever format
// its family asks for reaches the wrapped value.

public readonly record struct TestUuid : IFormattable, IParsable<TestUuid>, IComparable<TestUuid>
{
    private readonly Guid _value;

    public TestUuid(Guid value) => _value = value;

    public static TestUuid Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestUuid result)
    {
        var ok = Guid.TryParse(s, out var value);
        result = ok ? new TestUuid(value) : default;
        return ok;
    }

    public int CompareTo(TestUuid other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("D", CultureInfo.InvariantCulture);
}

public readonly record struct TestSmallCode : IFormattable, IParsable<TestSmallCode>, IComparable<TestSmallCode>
{
    private readonly short _value;

    public TestSmallCode(short value) => _value = value;

    public static TestSmallCode Parse(string s, IFormatProvider? provider)
        => new(short.Parse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestSmallCode result)
    {
        var ok = short.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value);
        result = ok ? new TestSmallCode(value) : default;
        return ok;
    }

    public int CompareTo(TestSmallCode other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct TestCode : IFormattable, IParsable<TestCode>, IComparable<TestCode>
{
    private readonly int _value;

    public TestCode(int value) => _value = value;

    public static TestCode Parse(string s, IFormatProvider? provider)
        => new(int.Parse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestCode result)
    {
        var ok = int.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value);
        result = ok ? new TestCode(value) : default;
        return ok;
    }

    public int CompareTo(TestCode other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct TestBigCode : IFormattable, IParsable<TestBigCode>, IComparable<TestBigCode>
{
    private readonly long _value;

    public TestBigCode(long value) => _value = value;

    public static TestBigCode Parse(string s, IFormatProvider? provider)
        => new(long.Parse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestBigCode result)
    {
        var ok = long.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value);
        result = ok ? new TestBigCode(value) : default;
        return ok;
    }

    public int CompareTo(TestBigCode other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct TestDay : IFormattable, IParsable<TestDay>, IComparable<TestDay>
{
    private readonly DateOnly _value;

    public TestDay(DateOnly value) => _value = value;

    public static TestDay Parse(string s, IFormatProvider? provider)
        => new(DateOnly.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestDay result)
    {
        var ok = DateOnly.TryParse(s, provider ?? CultureInfo.InvariantCulture, out var value);
        result = ok ? new TestDay(value) : default;
        return ok;
    }

    public int CompareTo(TestDay other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public readonly record struct TestSlot : IFormattable, IParsable<TestSlot>, IComparable<TestSlot>
{
    private readonly TimeOnly _value;

    public TestSlot(TimeOnly value) => _value = value;

    public static TestSlot Parse(string s, IFormatProvider? provider)
        => new(TimeOnly.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestSlot result)
    {
        var ok = TimeOnly.TryParse(s, provider ?? CultureInfo.InvariantCulture, out var value);
        result = ok ? new TestSlot(value) : default;
        return ok;
    }

    public int CompareTo(TestSlot other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);
}

public readonly record struct TestInstant : IFormattable, IParsable<TestInstant>, IComparable<TestInstant>
{
    private readonly DateTimeOffset _value;

    public TestInstant(DateTimeOffset value) => _value = value;

    public static TestInstant Parse(string s, IFormatProvider? provider)
        => new(DateTimeOffset.Parse(s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestInstant result)
    {
        var ok = DateTimeOffset.TryParse(
            s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value);
        result = ok ? new TestInstant(value) : default;
        return ok;
    }

    public int CompareTo(TestInstant other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>
/// A deliberately non-conforming element: it swallows the format argument and answers with the
/// invariant default, which for a <see cref="DateTime"/> drops the fractional seconds. The
/// bridge must refuse it rather than bind a truncated value.
/// </summary>
public readonly record struct TestLossyStamp
    : IFormattable, IParsable<TestLossyStamp>, IComparable<TestLossyStamp>
{
    private readonly DateTime _value;

    public TestLossyStamp(DateTime value) => _value = value;

    public static TestLossyStamp Parse(string s, IFormatProvider? provider)
        => new(DateTime.Parse(s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestLossyStamp result)
    {
        var ok = DateTime.TryParse(
            s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);
        result = ok ? new TestLossyStamp(value) : default;
        return ok;
    }

    public int CompareTo(TestLossyStamp other) => _value.CompareTo(other._value);

    // The defect: `format` is ignored.
    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
}

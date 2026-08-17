using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.ValueRanges.Tests;

// Test doubles for validated-value wrapper elements, emulating the shape that generators
// (Vogen, Metalama aspects, StronglyTypedId) produce: readonly record struct, IEquatable via
// the record, IFormattable exposing the backing text form, IParsable re-running validation.

/// <summary>
/// A string-backed validated key (dot-segmented, lowercased). Its <see cref="IComparable{T}"/>
/// is deliberately culture-sensitive — canonical ordering must never consult it.
/// </summary>
[JsonConverter(typeof(TestPermissionJsonConverter))]
internal readonly record struct TestPermission : IFormattable, IParsable<TestPermission>, IComparable<TestPermission>
{
    private readonly string _value;

    private TestPermission(string value) => _value = value;

    public static TestPermission Parse(string s, IFormatProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(s) || !s.Contains('.'))
            throw new FormatException($"'{s}' is not a valid permission key.");

        return new TestPermission(s.Trim().ToLowerInvariant());
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out TestPermission result)
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

    // Deliberately culture-sensitive, like a generated CompareTo delegating to string.CompareTo.
    public int CompareTo(TestPermission other)
        => string.Compare(_value, other._value, StringComparison.CurrentCulture);
}

internal sealed class TestPermissionJsonConverter : JsonConverter<TestPermission>
{
    public override TestPermission Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => TestPermission.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, TestPermission value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(null, CultureInfo.InvariantCulture));
}

/// <summary>A Guid-backed typed ID whose text form is the backing Guid's "D" form.</summary>
internal readonly record struct TestId : IFormattable, IParsable<TestId>, IComparable<TestId>
{
    private readonly Guid _value;

    private TestId(Guid value) => _value = value;

    public static TestId FromGuid(Guid value) => new(value);

    public static TestId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestId result)
    {
        var ok = Guid.TryParse(s, out var value);
        result = new TestId(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString("D", CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public int CompareTo(TestId other) => _value.CompareTo(other._value);
}

/// <summary>An int-backed typed ID whose text form is the backing int's invariant form.</summary>
internal readonly record struct TestIntId : IFormattable, IParsable<TestIntId>, IComparable<TestIntId>
{
    private readonly int _value;

    public TestIntId(int value) => _value = value;

    public static TestIntId Parse(string s, IFormatProvider? provider)
        => new(int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestIntId result)
    {
        var ok = int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        result = new TestIntId(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public int CompareTo(TestIntId other) => _value.CompareTo(other._value);
}

/// <summary>A long-backed typed ID whose text form is the backing long's invariant form.</summary>
internal readonly record struct TestLongId : IFormattable, IParsable<TestLongId>, IComparable<TestLongId>
{
    private readonly long _value;

    public TestLongId(long value) => _value = value;

    public static TestLongId Parse(string s, IFormatProvider? provider)
        => new(long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestLongId result)
    {
        var ok = long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        result = new TestLongId(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public int CompareTo(TestLongId other) => _value.CompareTo(other._value);
}

// The remaining arities' element types. The temporal ones differ from the integer keys above
// in one load-bearing way: they FORWARD the format argument to the value they wrap. Those
// families ask their elements for a round-trip format precisely because the default one is
// lossy, so a wrapper that answered with its own form would defeat it — see TestLossyStamp for
// what that looks like.

/// <summary>A short-backed typed code whose text form is the backing short's invariant form.</summary>
internal readonly record struct TestSmallId : IFormattable, IParsable<TestSmallId>, IComparable<TestSmallId>
{
    private readonly short _value;

    public TestSmallId(short value) => _value = value;

    public static TestSmallId Parse(string s, IFormatProvider? provider)
        => new(short.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestSmallId result)
    {
        var ok = short.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        result = new TestSmallId(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public int CompareTo(TestSmallId other) => _value.CompareTo(other._value);
}

/// <summary>
/// A decimal-backed money value. Scale is preserved through parse and format, which is what
/// makes it a witness for the decimal element converter writing <c>12.50</c> rather than
/// <c>12.5</c>.
/// </summary>
internal readonly record struct TestMoney : IFormattable, IParsable<TestMoney>, IComparable<TestMoney>
{
    private readonly decimal _value;

    public TestMoney(decimal value) => _value = value;

    public static TestMoney Parse(string s, IFormatProvider? provider)
        => new(decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestMoney result)
    {
        var ok = decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value);
        result = new TestMoney(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public int CompareTo(TestMoney other) => _value.CompareTo(other._value);
}

/// <summary>A DateOnly-backed business date. Forwards the format argument.</summary>
internal readonly record struct TestDay : IFormattable, IParsable<TestDay>, IComparable<TestDay>
{
    private readonly DateOnly _value;

    public TestDay(DateOnly value) => _value = value;

    public static TestDay Parse(string s, IFormatProvider? provider)
        => new(DateOnly.Parse(s, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestDay result)
    {
        var ok = DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var value);
        result = new TestDay(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public int CompareTo(TestDay other) => _value.CompareTo(other._value);
}

/// <summary>A TimeOnly-backed shift slot. Forwards the format argument.</summary>
internal readonly record struct TestSlot : IFormattable, IParsable<TestSlot>, IComparable<TestSlot>
{
    private readonly TimeOnly _value;

    public TestSlot(TimeOnly value) => _value = value;

    public static TestSlot Parse(string s, IFormatProvider? provider)
        => new(TimeOnly.Parse(s, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestSlot result)
    {
        var ok = TimeOnly.TryParse(s, CultureInfo.InvariantCulture, out var value);
        result = new TestSlot(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => ToString("O", CultureInfo.InvariantCulture);

    public int CompareTo(TestSlot other) => _value.CompareTo(other._value);
}

/// <summary>A DateTime-backed audit stamp. Forwards the format argument, and parses with
/// RoundtripKind so a "…Z" payload comes back as UTC instead of being shifted to local.</summary>
internal readonly record struct TestStamp : IFormattable, IParsable<TestStamp>, IComparable<TestStamp>
{
    private readonly DateTime _value;

    public TestStamp(DateTime value) => _value = value;

    public static TestStamp Parse(string s, IFormatProvider? provider)
        => new(DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestStamp result)
    {
        var ok = DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);
        result = new TestStamp(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => ToString("O", CultureInfo.InvariantCulture);

    public int CompareTo(TestStamp other) => _value.CompareTo(other._value);
}

/// <summary>A DateTimeOffset-backed event stamp. Forwards the format argument.</summary>
internal readonly record struct TestOffsetStamp
    : IFormattable, IParsable<TestOffsetStamp>, IComparable<TestOffsetStamp>
{
    private readonly DateTimeOffset _value;

    public TestOffsetStamp(DateTimeOffset value) => _value = value;

    public static TestOffsetStamp Parse(string s, IFormatProvider? provider)
        => new(DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestOffsetStamp result)
    {
        var ok = DateTimeOffset.TryParse(
            s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value);
        result = new TestOffsetStamp(value);
        return ok;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => ToString("O", CultureInfo.InvariantCulture);

    public int CompareTo(TestOffsetStamp other) => _value.CompareTo(other._value);
}

/// <summary>
/// A deliberately non-conforming DateTime wrapper: it swallows the format argument and answers
/// with the invariant default, which drops the fractional seconds. The counterexample the
/// temporal arities' contract exists for — see <c>SetElementBridgeTests</c> for where that is
/// observable and where it is caught.
/// </summary>
internal readonly record struct TestLossyStamp
    : IFormattable, IParsable<TestLossyStamp>, IComparable<TestLossyStamp>
{
    private readonly DateTime _value;

    public TestLossyStamp(DateTime value) => _value = value;

    public static TestLossyStamp Parse(string s, IFormatProvider? provider)
        => new(DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    public static bool TryParse(string? s, IFormatProvider? provider, out TestLossyStamp result)
    {
        var ok = DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);
        result = new TestLossyStamp(value);
        return ok;
    }

    // The defect: `format` is ignored.
    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);

    public int CompareTo(TestLossyStamp other) => _value.CompareTo(other._value);
}

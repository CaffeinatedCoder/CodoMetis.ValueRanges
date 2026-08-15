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

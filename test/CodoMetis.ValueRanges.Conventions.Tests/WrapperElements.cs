using System.Globalization;

namespace CodoMetis.ValueRanges.Conventions.Tests;

// Representative validated wrapper elements — the shape Vogen, Metalama and StronglyTypedId
// generate, and the one the wrapper set arities exist for. Each satisfies the family's
// constraint (struct, IEquatable, IComparable where required, IFormattable, IParsable) and
// upholds the contract constraints cannot express: the invariant text form is exactly the
// backing primitive's text form.
//
// TextKey deliberately normalizes in Parse (trim, lowercase). That is what makes the
// contract tests meaningful for the string-backed family: a probe parsed from "  Admin  "
// must still be found in a set built from it.

public readonly record struct TextKey : IFormattable, IParsable<TextKey>, IComparable<TextKey>
{
    private readonly string? _value;

    private TextKey(string value) => _value = value;

    public static TextKey Parse(string s, IFormatProvider? provider)
        => string.IsNullOrWhiteSpace(s)
               ? throw new FormatException($"'{s}' is not a valid key.")
               : new TextKey(s.Trim().ToLowerInvariant());

    public static bool TryParse(string? s, IFormatProvider? provider, out TextKey result)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            result = default;
            return false;
        }

        result = new TextKey(s.Trim().ToLowerInvariant());
        return true;
    }

    public int CompareTo(TextKey other) => string.CompareOrdinal(_value, other._value);

    public string ToString(string? format, IFormatProvider? formatProvider) => _value ?? "";

    public override string ToString() => _value ?? "";
}

public readonly record struct TenantId : IFormattable, IParsable<TenantId>, IComparable<TenantId>
{
    private readonly Guid _value;

    private TenantId(Guid value) => _value = value;

    public static TenantId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out TenantId result)
    {
        var parsed = Guid.TryParse(s, out var value);
        result = parsed ? new TenantId(value) : default;
        return parsed;
    }

    public int CompareTo(TenantId other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider);

    public override string ToString() => _value.ToString();
}

public readonly record struct SmallCode : IFormattable, IParsable<SmallCode>, IComparable<SmallCode>
{
    private readonly int _value;

    private SmallCode(int value) => _value = value;

    public static SmallCode Parse(string s, IFormatProvider? provider)
        => new(int.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out SmallCode result)
    {
        var parsed = int.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new SmallCode(value) : default;
        return parsed;
    }

    public int CompareTo(SmallCode other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

public readonly record struct LargeCode : IFormattable, IParsable<LargeCode>, IComparable<LargeCode>
{
    private readonly long _value;

    private LargeCode(long value) => _value = value;

    public static LargeCode Parse(string s, IFormatProvider? provider)
        => new(long.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out LargeCode result)
    {
        var parsed = long.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value);
        result = parsed ? new LargeCode(value) : default;
        return parsed;
    }

    public int CompareTo(LargeCode other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

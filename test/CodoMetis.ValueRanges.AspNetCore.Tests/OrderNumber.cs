using System.Globalization;

namespace CodoMetis.ValueRanges.AspNetCore.Tests;

/// <summary>A validated int wrapper, the element shape the wrapper set arities exist for.</summary>
public readonly record struct OrderNumber(int Value) : IFormattable, IParsable<OrderNumber>, IComparable<OrderNumber>
{
    public static OrderNumber Parse(string s, IFormatProvider? provider)
        => new(int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));

    public static bool TryParse(string? s, IFormatProvider? provider, out OrderNumber result)
    {
        var parsed = int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        result = new OrderNumber(value);
        return parsed;
    }

    public int CompareTo(OrderNumber other) => Value.CompareTo(other.Value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => Value.ToString(format, CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, null);
}

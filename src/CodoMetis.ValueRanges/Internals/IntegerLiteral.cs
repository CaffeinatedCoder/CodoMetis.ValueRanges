using System.Globalization;
using System.Numerics;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// An integer bound or element as PostgreSQL 16's <c>int2</c>, <c>int4</c> and <c>int8</c> input reads
/// it: an optional sign, then decimal digits, or <c>0x</c>, <c>0o</c> or <c>0b</c> (either case) and
/// digits in that base, with single underscores between digits (and one straight after the prefix),
/// around which <see cref="LiteralWhiteSpace"/> is skipped. Until 10.0.0 only the decimal form was
/// read, so <c>[0x1F,)</c> and <c>{1_000}</c>, which the server reads, threw here (#127).
/// </summary>
/// <remarks>
/// Measured on PostgreSQL 16 and 18, which agree: <c>0x_1F</c>, <c>0_1</c> and <c>-0x80000000</c>
/// (as <c>int4</c>) are read; <c>0x</c>, <c>0x1__F</c>, <c>0x1F_</c>, <c>_1</c>, <c>00x1</c>,
/// <c>0x-1</c>, <c>0o8</c> and <c>1e3</c> are refused. The magnitude is checked against the type
/// before the sign is applied, so the minimum is reachable in every base. Too large a magnitude
/// throws <see cref="OverflowException"/>, as the BCL integer parsers do. The provider never reaches
/// it. Shared by the integer ranges' and sets' <c>ParseValue</c>, which every literal, JSON element,
/// <c>TypeConverter</c> and EF Core JSON document reaches.
/// </remarks>
internal static class IntegerLiteral
{
    internal static T Parse<T>(ReadOnlySpan<char> s) where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        // PostgreSQL text cannot hold U+0000, and the BCL integer parsers ignore trailing NULs, so
        // "31\0" read as 31 until #233 (the slow path below always refused it).
        if (s.Contains('\0')) throw Invalid(s);

        // The decimal form without separators, which is nearly every literal.
        if (T.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;

        var text     = LiteralWhiteSpace.Trim(s);
        var negative = text.Length > 0 && text[0] == '-';
        if (text.Length > 0 && text[0] is '+' or '-') text = text[1..];

        var radix = text.Length > 1 && text[0] == '0'
            ? text[1] switch { 'x' or 'X' => 16, 'o' or 'O' => 8, 'b' or 'B' => 2, _ => 10 }
            : 10;
        if (radix != 10) text = text[2..];

        // Each underscore needs a digit after it, and a digit before it unless it follows the prefix.
        UInt128 magnitude = 0;
        var digits      = 0;
        var underscore  = false;
        var limit       = negative ? UInt128.CreateTruncating(T.MaxValue) + 1 : UInt128.CreateTruncating(T.MaxValue);
        foreach (var c in text)
        {
            if (c == '_')
            {
                if (underscore || digits == 0 && radix == 10) throw Invalid(s);
                underscore = true;
                continue;
            }

            var digit = DigitValue(c);
            if (digit >= radix) throw Invalid(s);

            underscore = false;
            digits++;
            magnitude = magnitude * (uint)radix + (uint)digit;
            if (magnitude > limit) throw new OverflowException($"Value was either too large or too small for {typeof(T).Name}: '{LiteralExcerpt.Of(s)}'.");
        }

        if (digits == 0 || underscore) throw Invalid(s);

        return negative
            ? T.CreateTruncating(Int128.CreateTruncating(UInt128.Zero - magnitude))
            : T.CreateTruncating(magnitude);
    }

    private static int DigitValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _                 => int.MaxValue
    };

    private static FormatException Invalid(ReadOnlySpan<char> s)
        => new("Expected an integer: an optional sign, then decimal digits, or 0x, 0o or 0b and digits in that base, "
             + $"with single underscores between digits, as PostgreSQL 16 reads it: '{LiteralExcerpt.Of(s)}'.");
}

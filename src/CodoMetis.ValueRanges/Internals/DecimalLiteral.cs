using System.Globalization;
using System.Numerics;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// A decimal bound or element as PostgreSQL's <c>numeric</c> input reads it — sign, digits, point
/// and exponent (<see cref="NumberStyles.Float"/>, invariant) — refused where <see cref="decimal"/>
/// cannot hold the number the text spells. <c>decimal.Parse</c> rounds a 29th significant digit, a
/// scale beyond 28 and <c>1e-30</c> to the nearest representable value without a word, while
/// PostgreSQL keeps every digit, so until 10.0.0 <c>[1.00000000000000000000000000001,2)</c> was a
/// range containing 1 in memory and not on the server, and <c>{1.00000000000000000000000000001,1}</c>
/// had one element here and two there (#115). A magnitude that does not fit still overflows.
/// </summary>
/// <remarks>
/// The check compares the value, not the text: the text's digits and exponent are read exactly
/// (<see cref="BigInteger"/>) and compared with the parsed decimal's mantissa and scale, so
/// <c>1e3</c>, <c>+1.5</c>, <c>007.50</c> and a trailing zero beyond the 28th decimal place — all
/// the same number as what <see cref="decimal"/> holds — pass, and only a text that names a
/// different number fails. Shared by <c>DecimalRange.ParseValue</c> and <c>DecimalSet.ParseValue</c>,
/// which every literal, JSON element, <c>TypeConverter</c> and EF Core JSON document reaches.
/// </remarks>
internal static class DecimalLiteral
{
    internal static decimal Parse(ReadOnlySpan<char> s)
    {
        // PostgreSQL text cannot hold U+0000, and decimal.Parse and BigInteger.Parse ignore trailing
        // NULs, so "31\0" read as 31 until #251.
        if (s.Contains('\0'))
            throw new FormatException($"Expected a number, sign, digits, point and exponent, with no NUL character: '{LiteralExcerpt.Of(s)}'.");

        var value = decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        return Represents(value, s)
                   ? value
                   // The rule first: the literal parsers rebound this as a 64-character excerpt.
                   : throw new FormatException(
                         "More precision than decimal holds (28 significant digits), so it would be rounded, "
                       + $"where PostgreSQL numeric keeps every digit: '{LiteralExcerpt.Of(s)}'.");
    }

    /// <summary>Whether <paramref name="value"/> is exactly the number <paramref name="text"/> spells.</summary>
    private static bool Represents(decimal value, ReadOnlySpan<char> text)
    {
        // The text, already accepted by decimal.Parse: [ws][sign]digits[.digits][(e|E)[sign]digits][ws].
        text = LiteralWhiteSpace.Trim(text);
        if (text.Length > 0 && text[0] is '+' or '-') text = text[1..];

        var exponentAt = text.IndexOfAny('e', 'E');
        var exponent   = 0;
        if (exponentAt >= 0)
        {
            exponent = int.Parse(text[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text     = text[..exponentAt];
        }

        var pointAt  = text.IndexOf('.');
        var fraction = pointAt < 0 ? 0 : text.Length - pointAt - 1;
        Span<char> digits = stackalloc char[text.Length];
        var count = 0;
        foreach (var c in text)
        {
            if (c != '.') digits[count++] = c;
        }

        // The text is spelled / 10^spelledScale; the decimal is mantissa / 10^scale.
        var spelled      = count == 0 ? BigInteger.Zero : BigInteger.Parse(digits[..count], NumberStyles.None, CultureInfo.InvariantCulture);
        var spelledScale = fraction - exponent;
        if (spelledScale < 0)
        {
            spelled     *= BigInteger.Pow(10, -spelledScale);
            spelledScale = 0;
        }

        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var mantissa = new BigInteger((uint)bits[2]) << 64 | new BigInteger((uint)bits[1]) << 32 | new BigInteger((uint)bits[0]);
        var scale    = (bits[3] >> 16) & 0xFF;

        return spelled * BigInteger.Pow(10, scale) == mantissa * BigInteger.Pow(10, spelledScale);
    }
}

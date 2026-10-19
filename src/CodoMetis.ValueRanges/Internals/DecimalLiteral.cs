using System.Globalization;

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
/// The check compares the value, not the text: the text's significant digits and exponent are read
/// exactly and compared with the parsed decimal's mantissa and scale, so
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
    /// <remarks>
    /// Both sides are reduced to <c>significand · 10^exponent</c> with no trailing zero in the
    /// significand, which spells each non-zero number one way only, so the check is two integer
    /// comparisons. The text's significant digits are counted before any are kept, and more than 29 —
    /// the most a decimal mantissa has — are refused, so the work is one pass over the text and a
    /// constant, whatever its length or exponent. Until #276 the digits were buffered on the stack at
    /// the text's length (a long element overflowed it, which kills the process) and the exponent went
    /// into <c>BigInteger.Pow</c> (<c>{0e-10000000}</c> ran for minutes).
    /// </remarks>
    private static bool Represents(decimal value, ReadOnlySpan<char> text)
    {
        // The text, already accepted by decimal.Parse: [ws][sign]digits[.digits][(e|E)[sign]digits][ws].
        text = LiteralWhiteSpace.Trim(text);
        if (text.Length > 0 && text[0] is '+' or '-') text = text[1..];

        var exponentAt = text.IndexOfAny('e', 'E');
        var digits     = exponentAt < 0 ? text : text[..exponentAt];

        // The significant digits, from the first non-zero one to the last; the zeros after the last
        // are only counted, as they may end the text.
        UInt128 significand = 0;
        var count    = 0;
        var zeros    = 0;
        var fraction = 0;
        var point    = false;
        foreach (var c in digits)
        {
            if (c == '.')
            {
                point = true;
                continue;
            }

            if (point) fraction++;
            if (c == '0')
            {
                if (count > 0) zeros++;
                continue;
            }

            count += zeros + 1;
            if (count > 29) return false;
            for (; zeros > 0; zeros--) significand *= 10;
            significand = significand * 10 + (uint)(c - '0');
        }

        // Zero, which decimal.Parse reads as zero whatever the exponent.
        if (count == 0) return true;

        // Beyond Int32, decimal.Parse has overflowed or read zero from non-zero digits.
        var exponent = 0;
        if (exponentAt >= 0
         && !int.TryParse(text[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            return false;

        // The text is significand · 10^spelledExponent; past ±28 decimal cannot hold it.
        var spelledExponent = (long)exponent - fraction + zeros;
        if (spelledExponent is < -28 or > 28) return false;

        // The decimal is mantissa / 10^scale, reduced the same way.
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        var mantissa = (UInt128)(uint)bits[2] << 64 | (UInt128)(uint)bits[1] << 32 | (uint)bits[0];
        var power    = -((bits[3] >> 16) & 0xFF);
        if (mantissa == 0) return false;
        for (; mantissa % 10 == 0; power++) mantissa /= 10;

        return mantissa == significand && power == spelledExponent;
    }
}

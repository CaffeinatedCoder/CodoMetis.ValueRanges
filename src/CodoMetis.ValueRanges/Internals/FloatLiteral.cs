using System.Globalization;
using System.Numerics;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// The text form of a <c>Float64Range</c> / <c>Float32Range</c> bound and a <c>Float64Set</c> /
/// <c>Float32Set</c> element: PostgreSQL's <c>float8in</c> / <c>float4in</c> grammar restricted to
/// finite numbers on the way in, and exactly the text <c>float8out</c> / <c>float4out</c> print on
/// the way out. Also the finite guard every construction path and scalar operand of the two families
/// goes through.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why finite only.</b> .NET orders <c>NaN</c> below every number under <c>CompareTo</c>, and
/// PostgreSQL orders it above <c>Infinity</c>: <c>[5,NaN)</c> is a valid range there that contains
/// <c>1e308</c>, and inverted (empty) here; <c>[0,)</c> contains <c>NaN</c> there and not here. The two
/// infinities agree in order but duplicate an unbounded side (<c>[1,Infinity)</c> is a different range
/// from <c>[1,)</c> on the server). Refusing all three leaves a total order on which <c>CompareTo</c>,
/// the IEEE operators and PostgreSQL agree (#153, measured on PostgreSQL 16, 17 and 18).
/// </para>
/// <para>
/// <b>Parsing.</b> Sign, digits, point and exponent, with the whitespace PostgreSQL strips around a
/// number. The spellings <c>float8in</c> also takes and this library refuses: <c>NaN</c>,
/// <c>Infinity</c>, <c>inf</c> (the guard), and C <c>strtod</c>'s hexadecimal form (<c>0x1p4</c>), which
/// PostgreSQL accepts only because the platform's <c>strtod</c> does. A number that rounds to infinity,
/// or to zero from non-zero digits, is refused there as out of range and is refused here too —
/// <see cref="double.Parse(string)"/> would return infinity or zero without a word. Rounding is
/// otherwise IEEE round-to-nearest on both sides.
/// </para>
/// <para>
/// <b>Formatting.</b> <c>float8out</c> and <c>float4out</c> (at the default <c>extra_float_digits</c>
/// of 1, PostgreSQL 12 and later) print the shortest decimal that reads back as the value, but their
/// Ryu build excludes the ends of the rounding interval, where .NET's shortest form includes the end the
/// value would round to: <c>1e23</c> is <c>1e+23</c> in .NET and <c>9.999999999999999e+22</c> there.
/// The layout differs too — a lowercase exponent of at least two digits, and exponent form from
/// <c>1e+15</c> (<c>float8</c>) or <c>1e+06</c> (<c>float4</c>) up and below <c>0.0001</c>. And .NET's
/// shortest form is wrong for exactly two doubles (2^-25 and 2^-958 print a text that reads back one
/// unit lower, on .NET 10). So the shortest digits are computed here, exactly, against the interval
/// PostgreSQL uses; measured equal to the server's output for 1.3 million values of each type on
/// PostgreSQL 16 and 18.
/// </para>
/// </remarks>
internal static class FloatLiteral
{
    /// <summary><paramref name="value"/>, if finite; otherwise <see cref="ArgumentException"/> naming the type.</summary>
    internal static double RequireFinite(double value, string typeName, string? paramName = null)
        => double.IsFinite(value) ? value : throw NotFinite(value.ToString(CultureInfo.InvariantCulture), typeName, paramName);

    /// <inheritdoc cref="RequireFinite(double, string, string?)"/>
    internal static float RequireFinite(float value, string typeName, string? paramName = null)
        => float.IsFinite(value) ? value : throw NotFinite(value.ToString(CultureInfo.InvariantCulture), typeName, paramName);

    private static ArgumentException NotFinite(string value, string typeName, string? paramName)
        => new($"{typeName} holds finite numbers only; got {value}. PostgreSQL sorts NaN above every number, where "
             + ".NET sorts it below, so no comparison could agree with the server; for an unbounded side use "
             + "CreateUnboundedStart, CreateUnboundedEnd or Infinite rather than an infinity.", paramName);

    /// <summary>A finite <see cref="double"/> as <c>float8in</c> reads <paramref name="s"/>.</summary>
    internal static double ParseDouble(ReadOnlySpan<char> s)
    {
        var text  = Number(s, "double precision");
        var value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsInfinity(value) || value == 0 && HasNonZeroDigit(text) ? throw OutOfRange(text, "double precision") : value;
    }

    /// <summary>A finite <see cref="float"/> as <c>float4in</c> reads <paramref name="s"/>.</summary>
    internal static float ParseSingle(ReadOnlySpan<char> s)
    {
        var text  = Number(s, "real");
        var value = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return float.IsInfinity(value) || value == 0 && HasNonZeroDigit(text) ? throw OutOfRange(text, "real") : value;
    }

    /// <summary>The number with its surrounding whitespace removed, if it is sign, digits, point and exponent.</summary>
    private static ReadOnlySpan<char> Number(ReadOnlySpan<char> s, string storeType)
    {
        var text = LiteralWhiteSpace.Trim(s);
        var i    = 0;
        if (i < text.Length && text[i] is '+' or '-') i++;

        var digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; digits++; }
        }

        if (digits > 0 && i < text.Length && text[i] is 'e' or 'E')
        {
            i++;
            if (i < text.Length && text[i] is '+' or '-') i++;
            var exponentDigits = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i])) { i++; exponentDigits++; }
            if (exponentDigits == 0) digits = 0;
        }

        // The rule first: the literal parsers rebound this as a 64-character excerpt.
        return digits > 0 && i == text.Length
                   ? text
                   : throw new FormatException(
                         $"Not a finite {storeType}: a number is sign, digits, point and exponent, and NaN and Infinity "
                       + $"are refused: '{LiteralExcerpt.Of(s)}'.");
    }

    /// <summary>
    /// PostgreSQL's <c>out of range</c>: a finite text that rounds to infinity, or non-zero digits that
    /// round to zero, where <see cref="double.Parse(string)"/> would return either without a word.
    /// </summary>
    private static FormatException OutOfRange(ReadOnlySpan<char> text, string storeType)
        => new($"Out of range for {storeType}, as PostgreSQL refuses it: '{LiteralExcerpt.Of(text)}'.");

    private static bool HasNonZeroDigit(ReadOnlySpan<char> number)
    {
        foreach (var c in number)
        {
            if (c is 'e' or 'E') return false;
            if (c is >= '1' and <= '9') return true;
        }

        return false;
    }

    /// <summary>The text <c>float8out</c> prints for a finite <paramref name="value"/>.</summary>
    internal static string Format(double value)
    {
        if (value == 0) return double.IsNegative(value) ? "-0" : "0";

        var bits     = BitConverter.DoubleToInt64Bits(RequireFinite(value, "Float64"));
        var exponent = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xF_FFFF_FFFF_FFFF;
        var (mantissa, power) = exponent == 0
                                    ? (new BigInteger(fraction), -1074)
                                    : (new BigInteger(fraction | 1L << 52), exponent - 1075);

        var magnitude = Math.Abs(value);
        return Layout(bits < 0,
                      Shortest(mantissa, power, fraction == 0 && exponent > 1, magnitude, maxDigits: 17),
                      fixedBelow: 15);
    }

    /// <summary>The text <c>float4out</c> prints for a finite <paramref name="value"/>.</summary>
    internal static string Format(float value)
    {
        if (value == 0) return float.IsNegative(value) ? "-0" : "0";

        var bits     = BitConverter.SingleToInt32Bits(RequireFinite(value, "Float32"));
        var exponent = (bits >> 23) & 0xFF;
        var fraction = bits & 0x7F_FFFF;
        var (mantissa, power) = exponent == 0
                                    ? (new BigInteger(fraction), -149)
                                    : (new BigInteger(fraction | 1 << 23), exponent - 150);

        var magnitude = Math.Abs(value);
        return Layout(bits < 0,
                      Shortest(mantissa, power, fraction == 0 && exponent > 1, magnitude, maxDigits: 9),
                      fixedBelow: 6);
    }

    /// <summary>
    /// The fewest significant digits that lie strictly inside the rounding interval of
    /// <c>mantissa · 2^power</c>, the closest of them to it (ties to an even last digit) — Ryu with
    /// PostgreSQL's <c>acceptBounds = false</c> — as the digits without trailing zeros and the decimal
    /// exponent of the first one.
    /// </summary>
    /// <param name="mantissa">The value's integer significand.</param>
    /// <param name="power">The binary exponent: the value is <c>mantissa · 2^power</c>.</param>
    /// <param name="closerBelow">
    /// Whether the next value down is half as far as the next value up — a power of two above the
    /// smallest normal exponent.
    /// </param>
    /// <param name="magnitude">The value's absolute value, whose .NET formats propose the candidates.</param>
    /// <param name="maxDigits">The digits that always suffice: 17 for a double, 9 for a float.</param>
    private static (string Digits, int Exponent) Shortest<TFloat>(
        BigInteger mantissa, int power, bool closerBelow, TFloat magnitude, int maxDigits)
        where TFloat : IFormattable
    {
        // The interval's ends, as numerator · 2^exponent. They are midpoints to the neighbours and are
        // excluded: a text on one reads back as this value only by round-half-even, which PostgreSQL's
        // Ryu does not rely on.
        var (lowNumerator, lowPower) = closerBelow ? (4 * mantissa - 1, power - 2) : (2 * mantissa - 1, power - 1);
        var (highNumerator, highPower) = (2 * mantissa + 1, power - 1);

        // .NET's own shortest form never has more digits than the answer, so it is where the search
        // starts; every length from there is checked exactly, which also corrects the two doubles .NET
        // gets wrong.
        var shortest = magnitude.ToString("R", CultureInfo.InvariantCulture);
        for (var length = SignificantDigits(shortest); length <= maxDigits; length++)
        {
            // The correctly rounded candidate of this length, d.ddd…E±x, and its two neighbours.
            var scientific = magnitude.ToString("E" + (length - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var e          = scientific.IndexOf('E');
            var rounded    = BigInteger.Parse(scientific.AsSpan(0, e).ToString().Replace(".", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);
            var scale      = int.Parse(scientific.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) - (length - 1);

            BigInteger? best     = null;
            var         distance = BigInteger.Zero;
            for (var candidate = rounded - 1; candidate <= rounded + 1; candidate++)
            {
                if (candidate.Sign <= 0
                 || Compare(candidate, scale, lowNumerator, lowPower) <= 0
                 || Compare(candidate, scale, highNumerator, highPower) >= 0) continue;

                var (decimalSide, binarySide) = Scaled(candidate, scale, mantissa, power);
                var gap = BigInteger.Abs(decimalSide - binarySide);
                if (best is null || gap < distance || gap == distance && candidate.IsEven)
                    (best, distance) = (candidate, gap);
            }

            if (best is { } digits)
            {
                var text    = digits.ToString(CultureInfo.InvariantCulture);
                var trimmed = text.TrimEnd('0');
                return (trimmed, scale + text.Length - 1);
            }
        }

        throw new InvalidOperationException($"No {maxDigits}-digit decimal reads back as {shortest}.");
    }

    private static int SignificantDigits(string shortest)
    {
        var e      = shortest.IndexOf('E');
        var digits = (e < 0 ? shortest : shortest[..e]).Replace(".", "", StringComparison.Ordinal).Trim('0');
        return Math.Max(digits.Length, 1);
    }

    /// <summary>Compares <c>digits · 10^scale</c> with <c>numerator · 2^power</c>, exactly.</summary>
    private static int Compare(BigInteger digits, int scale, BigInteger numerator, int power)
    {
        var (decimalSide, binarySide) = Scaled(digits, scale, numerator, power);
        return decimalSide.CompareTo(binarySide);
    }

    /// <summary><c>digits · 10^scale</c> and <c>numerator · 2^power</c>, multiplied to integers by the same factor.</summary>
    private static (BigInteger Decimal, BigInteger Binary) Scaled(BigInteger digits, int scale, BigInteger numerator, int power)
    {
        if (scale >= 0) digits *= BigInteger.Pow(10, scale);
        else numerator *= BigInteger.Pow(10, -scale);

        if (power >= 0) numerator <<= power;
        else digits <<= -power;

        return (digits, numerator);
    }

    /// <summary>
    /// PostgreSQL's <c>to_chars</c>: fixed notation for a decimal exponent from -4 up to
    /// <paramref name="fixedBelow"/>, otherwise <c>d.ddde±xx</c> with at least two exponent digits.
    /// </summary>
    private static string Layout(bool negative, (string Digits, int Exponent) shortest, int fixedBelow)
    {
        var (digits, exponent) = shortest;
        var sign = negative ? "-" : "";

        if (exponent < -4 || exponent >= fixedBelow)
        {
            var fraction = digits.Length > 1 ? "." + digits[1..] : "";
            return $"{sign}{digits[0]}{fraction}e{(exponent < 0 ? '-' : '+')}{Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture)}";
        }

        if (exponent < 0) return $"{sign}0.{new string('0', -exponent - 1)}{digits}";
        if (digits.Length <= exponent + 1) return $"{sign}{digits}{new string('0', exponent + 1 - digits.Length)}";
        return $"{sign}{digits[..(exponent + 1)]}.{digits[(exponent + 1)..]}";
    }
}

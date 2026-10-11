using System.Globalization;
using System.Text;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

internal static class RangeFormat
{
    // `provider` arrives through the IParsable signatures and is deliberately unused: the literal is
    // invariant (see ParseBound). Do not thread it back to the element parser.
    internal static TRange Parse<TRange, T>(ReadOnlySpan<char> s, IFormatProvider? provider)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        s = LiteralWhiteSpace.Trim(s);

        if (s.Equals("empty", StringComparison.OrdinalIgnoreCase))
            return TRange.Empty;

        if (s.Length < 3)
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is not a valid range literal.");

        var startBracket = s[0];
        var endBracket   = s[^1];

        if (startBracket is not '[' and not '(')
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is not a valid range literal.");
        if (endBracket is not ']' and not ')')
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is not a valid range literal.");

        var startInclusive = startBracket == '[';
        var endInclusive   = endBracket   == ']';

        var inner    = s[1..^1];
        var position = 0;

        var startPart = ScanBound(inner, ref position, s);
        if (position == inner.Length || inner[position] != ',')
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is not a valid range literal: missing comma after the lower bound.");

        position++;
        var endPart = ScanBound(inner, ref position, s);
        if (position != inner.Length)
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is not a valid range literal: unexpected '{inner[position]}' in the upper bound.");

        return (startPart, endPart) switch
        {
            (null, null) => TRange.Infinite,
            (null, _)    => TRange.CreateUnboundedStart(ParseBound<TRange, T>(endPart,   s), endInclusive),
            (_,    null) => TRange.CreateUnboundedEnd  (ParseBound<TRange, T>(startPart, s), startInclusive),
            _            => TRange.CreateFinite(
                ParseBound<TRange, T>(startPart, s),
                ParseBound<TRange, T>(endPart,   s),
                startInclusive,
                endInclusive)
        };
    }

    /// <summary>
    /// One bound, read as PostgreSQL's <c>range_in</c> reads it: <see langword="null"/> when the bound
    /// is <em>absent</em> — the next character is the comma or the end — and its text otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only an absent bound is infinite. A quoted <c>""</c>, or a bound of only whitespace, is a
    /// present bound whose text is empty, which the element parser refuses for every type here — as
    /// PostgreSQL does (<c>invalid input syntax for type integer: ""</c>). Until 9.0.0 the bound was
    /// unquoted first and then tested for emptiness, so <c>["",5)</c> read as <c>(,4]</c>, silently.
    /// </para>
    /// <para>
    /// Within a bound, a double quote opens or closes a quoted section anywhere (<c>"1"2</c> is
    /// <c>12</c>), a doubled quote inside one is a literal quote, and a backslash takes the next
    /// character literally, inside quotes or out. An unquoted, unescaped comma, <c>)</c> or <c>]</c>
    /// ends the bound. Whitespace around the unquoted part is dropped: PostgreSQL hands it to the
    /// element's input function, and every input function behind these types ignores it. That is
    /// ASCII whitespace only (<see cref="LiteralWhiteSpace"/>): a no-break space stays in the bound,
    /// where the element parser refuses it as PostgreSQL's does.
    /// </para>
    /// </remarks>
    private static string? ScanBound(ReadOnlySpan<char> inner, ref int position, ReadOnlySpan<char> literal)
    {
        var start = position;
        var sb    = new StringBuilder();

        // The length of the text up to its last character that is not unquoted whitespace.
        var significant = 0;
        var inQuotes    = false;

        while (position < inner.Length && (inQuotes || inner[position] is not (',' or ')' or ']')))
        {
            var c = inner[position++];

            if (c == '\\')
            {
                if (position == inner.Length)
                    throw new FormatException($"The input string '{LiteralExcerpt.Of(literal)}' is not a valid range literal: unterminated escape.");

                sb.Append(inner[position++]);
                significant = sb.Length;
            }
            else if (c == '"')
            {
                if (!inQuotes)
                {
                    inQuotes    = true;
                    significant = sb.Length;
                }
                else if (position < inner.Length && inner[position] == '"')
                {
                    sb.Append('"');
                    significant = sb.Length;
                    position++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else if (inQuotes || !LiteralWhiteSpace.Is(c))
            {
                sb.Append(c);
                significant = sb.Length;
            }
            else if (sb.Length > 0 || significant > 0)
            {
                sb.Append(c); // inner whitespace; dropped below if nothing significant follows
            }
        }

        if (inQuotes)
            throw new FormatException($"The input string '{LiteralExcerpt.Of(literal)}' is not a valid range literal: unterminated quoted bound.");

        if (position == start) return null;

        sb.Length = significant;
        return sb.ToString();
    }

    /// <summary>
    /// A bound through the element parser, with its failure re-thrown as a bounded message. The
    /// element parser is not chained as the inner exception: the BCL's format error embeds the
    /// whole offending text, which is exactly what <see cref="LiteralExcerpt"/> exists to cut.
    /// Its reason survives as an excerpt. Overflow is left alone — the BCL message names the type,
    /// not the input.
    /// </summary>
    /// <remarks>
    /// Always the invariant culture, whatever provider the caller handed <c>Parse</c>: the literal is
    /// a wire format. Before 9.0.0 the provider reached the element parser, so <c>th-TH</c> read
    /// <c>2026-01-01</c> as a Buddhist-era date (Gregorian 1483) and <c>de-DE</c> read <c>1.5</c>
    /// as fifteen — silently.
    /// </remarks>
    private static T ParseBound<TRange, T>(ReadOnlySpan<char> part, ReadOnlySpan<char> literal)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        try
        {
            return TRange.ParseValue(part, CultureInfo.InvariantCulture);
        }
        catch (FormatException ex)
        {
            throw new FormatException(
                $"The bound '{LiteralExcerpt.Of(part)}' in the range literal '{LiteralExcerpt.Of(literal)}' "
              + $"is not a valid {typeof(T).Name}: {LiteralExcerpt.Of(ex.Message)}");
        }
    }

    internal static bool TryParse<TRange, T>(
        ReadOnlySpan<char> s,
        IFormatProvider? provider,
        out TRange result)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        try
        {
            result = Parse<TRange, T>(s, provider);
            return true;
        }
        catch
        {
            result = TRange.Empty;
            return false;
        }
    }

    /// <summary>
    /// Splits a range set literal such as <c>{[1,5],[7,10]}</c> into its component range literal strings.
    /// Returns an empty list for <c>{}</c>.
    /// </summary>
    internal static IReadOnlyList<string> SplitSetLiterals(ReadOnlySpan<char> s)
    {
        s = LiteralWhiteSpace.Trim(s);

        if (s.Length < 2 || s[0] != '{' || s[^1] != '}')
            throw new FormatException($"The input string '{LiteralExcerpt.Of(s)}' is not a valid range set literal.");

        var inner  = LiteralWhiteSpace.Trim(s[1..^1]);
        var result = new List<string>();

        if (inner.IsEmpty)
            return result;

        var depth    = 0;
        var start    = 0;
        var inQuotes = false;

        // Brackets and commas inside a quoted or escaped bound are bound text, not structure.
        for (var i = 0; i < inner.Length; i++)
        {
            switch (inner[i])
            {
                case '\\':
                    i++;
                    break;
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case '[' or '(' when !inQuotes:
                    depth++;
                    break;
                case ']' or ')' when !inQuotes:
                    depth--;
                    break;
                case ',' when depth == 0 && !inQuotes:
                    result.Add(LiteralWhiteSpace.Trim(inner[start..i]).ToString());
                    start = i + 1;
                    break;
            }
        }

        result.Add(LiteralWhiteSpace.Trim(inner[start..]).ToString());
        return result;
    }
}

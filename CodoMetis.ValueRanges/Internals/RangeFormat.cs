using System.Text;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

internal static class RangeFormat
{
    internal static TRange Parse<TRange, T>(ReadOnlySpan<char> s, IFormatProvider? provider)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        s = s.Trim();

        if (s.Equals("empty", StringComparison.OrdinalIgnoreCase))
            return TRange.Empty;

        if (s.Length < 3)
            throw new FormatException($"The input string '{s.ToString()}' is not a valid range literal.");

        var startBracket = s[0];
        var endBracket   = s[^1];

        if (startBracket is not '[' and not '(')
            throw new FormatException($"The input string '{s.ToString()}' is not a valid range literal.");
        if (endBracket is not ']' and not ')')
            throw new FormatException($"The input string '{s.ToString()}' is not a valid range literal.");

        var startInclusive = startBracket == '[';
        var endInclusive   = endBracket   == ']';

        var inner    = s[1..^1];
        var commaIdx = FindComma(inner);

        if (commaIdx < 0)
            throw new FormatException($"The input string '{s.ToString()}' is not a valid range literal.");

        var startPart = UnquoteValue(inner[..commaIdx].Trim());
        var endPart   = UnquoteValue(inner[(commaIdx + 1)..].Trim());

        var hasStart = startPart.Length > 0;
        var hasEnd   = endPart.Length   > 0;

        return (hasStart, hasEnd) switch
        {
            (false, false) => TRange.Infinite,
            (false, true)  => TRange.CreateUnboundedStart(TRange.ParseValue(endPart,   provider), endInclusive),
            (true,  false) => TRange.CreateUnboundedEnd  (TRange.ParseValue(startPart, provider), startInclusive),
            (true,  true)  => TRange.CreateFinite(
                TRange.ParseValue(startPart, provider),
                TRange.ParseValue(endPart,   provider),
                startInclusive,
                endInclusive)
        };
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
        s = s.Trim();

        if (s.Length < 2 || s[0] != '{' || s[^1] != '}')
            throw new FormatException($"The input string '{s.ToString()}' is not a valid range set literal.");

        var inner  = s[1..^1].Trim();
        var result = new List<string>();

        if (inner.IsEmpty)
            return result;

        var depth = 0;
        var start = 0;

        for (var i = 0; i < inner.Length; i++)
        {
            switch (inner[i])
            {
                case '[' or '(':
                    depth++;
                    break;
                case ']' or ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(inner[start..i].Trim().ToString());
                    start = i + 1;
                    break;
            }
        }

        result.Add(inner[start..].Trim().ToString());
        return result;
    }

    private static int FindComma(ReadOnlySpan<char> s)
    {
        var inQuotes = false;
        for (var i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case '\\' when inQuotes:
                    i++; // skip escaped char inside quotes
                    break;
                case ',' when !inQuotes:
                    return i;
            }
        }
        return -1;
    }

    private static ReadOnlySpan<char> UnquoteValue(ReadOnlySpan<char> s)
    {
        if (s.Length < 2 || s[0] != '"' || s[^1] != '"') return s;
        var inner = s[1..^1];
        // No backslash escapes inside the quotes — return a slice of the input directly.
        if (!inner.Contains('\\')) return inner;
        // PostgreSQL quoted bounds escape `\"` as `"` and `\\` as `\`. Unescape them so
        // ParseValue receives the literal value the user intended. This path is rare for
        // the built-in element types (int, decimal, DateOnly, …) but matters for custom
        // range types whose bound stringification can contain quotes or backslashes.
        return UnescapeQuoted(inner);
    }

    private static string UnescapeQuoted(ReadOnlySpan<char> s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char next = s[i + 1];
                if (next == '"' || next == '\\')
                {
                    sb.Append(next);
                    i++; // consume the escaped char
                    continue;
                }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}

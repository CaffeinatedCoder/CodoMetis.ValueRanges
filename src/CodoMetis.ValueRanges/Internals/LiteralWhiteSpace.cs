namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// The whitespace PostgreSQL's literal parsers skip — <c>array_in</c> around elements, <c>range_in</c>
/// around the literal, and the element input functions behind the built-in types around a bound:
/// ASCII space, tab, newline, carriage return, vertical tab and form feed.
/// </summary>
/// <remarks>
/// Not the rest of Unicode's whitespace, which is text there: a leading no-break space is part of a
/// <c>text[]</c> element and makes an <c>integer</c> bound invalid. <see cref="string.Trim()"/> would
/// drop it — silently a different element, or a bound PostgreSQL refuses.
/// </remarks>
internal static class LiteralWhiteSpace
{
    internal static bool Is(char c) => c is ' ' or '\t' or '\n' or '\r' or '\v' or '\f';

    internal static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
    {
        var start = 0;
        var end   = s.Length;
        while (start < end && Is(s[start])) start++;
        while (end > start && Is(s[end - 1])) end--;
        return s[start..end];
    }
}

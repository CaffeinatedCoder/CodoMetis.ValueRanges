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

    /// <summary>
    /// An element as the PostgreSQL input function behind a temporal value set reads it: <c>date_in</c>,
    /// <c>timestamp_in</c>, <c>timestamptz_in</c> and <c>time_in</c> strip these characters from both
    /// ends of the text <c>array_in</c> hands them, quoted or escaped (#70, measured on PostgreSQL 17).
    /// </summary>
    /// <remarks>
    /// Used by the temporal families' <c>ParseValue</c> only. <c>textin</c> keeps the whitespace, so a
    /// string family must not trim; <c>uuid_in</c> rejects it, and so does <see cref="UuidLiteral"/> (#78); the numeric families already
    /// trim exactly these characters through the BCL number parsers. Until 9.1.0 the temporal
    /// families handed it to the strict ISO grammar, so <c>{" 2026-01-01"}</c> threw.
    /// </remarks>
    internal static ReadOnlySpan<char> TrimAsInputFunction(ReadOnlySpan<char> s) => Trim(s);

    internal static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
    {
        var start = 0;
        var end   = s.Length;
        while (start < end && Is(s[start])) start++;
        while (end > start && Is(s[end - 1])) end--;
        return s[start..end];
    }
}

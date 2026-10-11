using System.Globalization;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// Strict ISO 8601 parsing for the BCL temporal elements: exactly the text forms this library writes
/// and the forms PostgreSQL writes in its default ISO <c>DateStyle</c>, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Until 9.0.0 the elements went through the lenient BCL <c>Parse</c>, which reads whatever the
/// invariant culture can make sense of — and the invariant culture reads month first. So
/// <c>[01.02.2027,05.03.2027)</c> was January 2 to May 2, silently, and a <c>DateTimeOffset</c>
/// without an offset took the offset of whichever machine parsed it. The NodaTime satellite was
/// already strict (<c>NodaPatterns</c>); this is the same rule for the BCL types.
/// </para>
/// <para>
/// Accepted: a date <c>yyyy-MM-dd</c>; a time <c>HH:mm</c> or <c>HH:mm:ss</c> with any number of
/// fraction digits, truncated to the 100 ns the BCL types hold (Go and Java write nine, and
/// System.Text.Json truncates them the same way); date and time joined by <c>T</c> (this library) or a
/// space (PostgreSQL); for a <c>DateTime</c> an optional <c>Z</c> (<see cref="DateTimeKind.Utc"/>) or
/// <c>±HH</c>, <c>±HH:mm</c> or <c>±HH:mm:ss</c> (<see cref="DateTimeKind.Local"/>) suffix, with the wall
/// clock kept as written and the offset not applied; for a
/// <c>DateTimeOffset</c> a required <c>Z</c>, <c>±HH</c>, <c>±HH:mm</c> or <c>±HH:mm:ss</c> — the last is
/// how PostgreSQL writes a historical local-mean-time offset (<c>Asia/Kolkata</c> before 1854 is
/// <c>+05:53:28</c>), which <c>DateTimeOffset</c> cannot hold, so that instant is kept at offset zero; and PostgreSQL's
/// <c>infinity</c> / <c>-infinity</c>, read as <c>MaxValue</c> / <c>MinValue</c> — what Npgsql
/// materializes for them. <c>HH:mm</c> is not something either side writes, but it is valid ISO
/// 8601, it cannot be misread, and it is what a hand-written <c>[09:00,17:00)</c> uses.
/// </para>
/// <para>
/// A rejection is a <see cref="FormatException"/> that names the grammar and not the input: the
/// literal parsers wrap it with a bounded excerpt of the offending text.
/// </para>
/// </remarks>
internal static class IsoTemporal
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd"];

    private static readonly string[] TimeFormats = ["HH:mm:ss.FFFFFFF", "HH:mm"];

    // Every form carries an offset, so AssumeUniversal only ever gives the literal Z its meaning.
    private static readonly string[] DateTimeOffsetFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd' 'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzz",  "yyyy-MM-dd' 'HH:mm:ss.FFFFFFFzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd' 'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mmzzz",            "yyyy-MM-dd' 'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mmzz",             "yyyy-MM-dd' 'HH:mmzz",
        "yyyy-MM-dd'T'HH:mm'Z'",            "yyyy-MM-dd' 'HH:mm'Z'"
    ];

    internal static DateOnly ParseDate(ReadOnlySpan<char> s)
    {
        if (TryParseInfinity(s, out var positive)) return positive ? DateOnly.MaxValue : DateOnly.MinValue;

        return DateOnly.TryParseExact(s, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : throw new FormatException("Expected an ISO 8601 date, yyyy-MM-dd, or infinity / -infinity.");
    }

    internal static TimeOnly ParseTime(ReadOnlySpan<char> s)
        => TimeOnly.TryParseExact(TruncateFraction(s), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : throw new FormatException("Expected an ISO 8601 time of day, HH:mm or HH:mm:ss with up to seven fraction digits.");

    /// <remarks>
    /// The offset is not applied. A <c>DateTime</c> range is a <c>tsrange</c>, which stores a wall
    /// clock and no zone, and PostgreSQL's own <c>timestamp</c> input keeps the wall clock and drops
    /// the offset (<c>'2026-01-01 09:30+05:30'::timestamp</c> is 09:30). Until 9.0.0 the offset was
    /// converted into the parsing machine's zone, so one literal stored a different wall clock on
    /// every server. A value with an offset is <see cref="DateTimeKind.Local"/>, so a local value —
    /// which <c>"O"</c> writes with its offset — round-trips with its kind, and no zone rule is ever
    /// consulted, not even for a wall clock the machine's zone skips or repeats.
    /// </remarks>
    internal static DateTime ParseDateTime(ReadOnlySpan<char> s)
    {
        if (TryParseInfinity(s, out var positive)) return positive ? DateTime.MaxValue : DateTime.MinValue;

        var text = TruncateFraction(s);
        var kind = DateTimeKind.Unspecified;
        if (text.Length > 0 && text[^1] == 'Z')
        {
            kind = DateTimeKind.Utc;
            text = text[..^1];
        }
        else if (OffsetLength(text) is > 0 and var length)
        {
            kind = DateTimeKind.Local;
            text = text[..^length];
        }

        return DateTime.TryParseExact(text, WallClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? DateTime.SpecifyKind(value, kind)
            : throw new FormatException(
                "Expected an ISO 8601 date and time, yyyy-MM-dd then T or a space then HH:mm[:ss[.fffffff]], "
              + "optionally followed by Z or ±HH[:mm[:ss]], or infinity / -infinity.");
    }

    /// <summary>
    /// The length of a trailing <c>±HH</c>, <c>±HH:mm</c> or <c>±HH:mm:ss</c> offset of at most 14 hours,
    /// or 0 — only after the time, so the date's own hyphens are never mistaken for one.
    /// </summary>
    private static int OffsetLength(ReadOnlySpan<char> s)
    {
        foreach (var length in (ReadOnlySpan<int>) [3, 6, 9])
        {
            if (s.Length < 16 + length) continue;

            var tail = s[^length..];
            if (tail[0] is not ('+' or '-')
                || !TryTwoDigits(tail[1..3], 14, out _)
                || length >= 6 && (tail[3] != ':' || !TryTwoDigits(tail[4..6], 59, out _))
                || length == 9 && (tail[6] != ':' || !TryTwoDigits(tail[7..9], 59, out _)))
                continue;

            return length;
        }

        return 0;
    }

    private static bool TryTwoDigits(ReadOnlySpan<char> s, int max, out int value)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= max;

    internal static DateTimeOffset ParseDateTimeOffset(ReadOnlySpan<char> s)
    {
        if (TryParseInfinity(s, out var positive)) return positive ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue;

        var trimmed = TruncateFraction(s);
        return DateTimeOffset.TryParseExact(trimmed, DateTimeOffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            || TryParseSecondsOffset(trimmed, out value)
            ? value
            : throw new FormatException(
                "Expected an ISO 8601 date and time with an offset, yyyy-MM-dd then T or a space then "
              + "HH:mm[:ss[.fffffff]] then Z, ±HH, ±HH:mm or ±HH:mm:ss, or infinity / -infinity.");
    }

    // The date-time part of a value whose offset carries seconds, read as a wall clock.
    private static readonly string[] WallClockFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd' 'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm",            "yyyy-MM-dd' 'HH:mm"
    ];

    /// <summary>A trailing <c>±HH:mm:ss</c> offset, applied to the wall clock and kept as offset zero.</summary>
    private static bool TryParseSecondsOffset(ReadOnlySpan<char> s, out DateTimeOffset value)
    {
        value = default;
        if (s.Length < 10) return false;

        var tail = s[^9..];
        if (tail[0] is not ('+' or '-') || tail[3] != ':' || tail[6] != ':'
            || !int.TryParse(tail[1..3], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(tail[4..6], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(tail[7..9], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || hours > 14 || minutes > 59 || seconds > 59
            || !DateTime.TryParseExact(s[..^9], WallClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
            return false;

        var offset = new TimeSpan(hours, minutes, seconds);
        var utc    = tail[0] == '-' ? wall.Ticks + offset.Ticks : wall.Ticks - offset.Ticks;
        if (utc < DateTime.MinValue.Ticks || utc > DateTime.MaxValue.Ticks) return false;

        value = new DateTimeOffset(utc, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// The text with a fraction of more than seven digits cut to seven — truncation, as
    /// System.Text.Json does. A <c>.</c> anywhere else (a dotted date) is left for the formats to refuse.
    /// </summary>
    private static ReadOnlySpan<char> TruncateFraction(ReadOnlySpan<char> s)
    {
        var dot = s.IndexOf('.');
        if (dot < 0) return s;

        var end = dot + 1;
        while (end < s.Length && char.IsAsciiDigit(s[end])) end++;

        return end - dot - 1 <= 7 ? s : string.Concat(s[..(dot + 8)], s[end..]);
    }

    private static bool TryParseInfinity(ReadOnlySpan<char> s, out bool positive)
    {
        positive = s.Equals("infinity", StringComparison.OrdinalIgnoreCase);
        return positive || s.Equals("-infinity", StringComparison.OrdinalIgnoreCase);
    }
}

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
        => !StartsWithHour24(s, 0)
        && TimeOnly.TryParseExact(TruncateFraction(s), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : throw new FormatException("Expected an ISO 8601 time of day, HH:mm or HH:mm:ss with up to seven fraction digits.");

    /// <summary>
    /// Whether the time of day starting at <paramref name="timeStart"/> has the hour 24.
    /// </summary>
    /// <remarks>
    /// ISO 8601's end-of-day form. .NET 11 reads it: <c>24:00:00</c> as <c>00:00:00</c> for a
    /// <see cref="TimeOnly"/>, and <c>2026-01-01T24:00:00</c> as the next day's midnight for a
    /// <see cref="DateTime"/> or <see cref="DateTimeOffset"/>, where .NET 10 refused both. This
    /// <c>net10.0</c> assembly runs on the .NET 11 runtime inside a <c>net11.0</c> application, so
    /// until 10.1.0 the same literal meant one thing there and threw everywhere else (#300; #286 on
    /// the 11 line). The grammar refuses it on every runtime: a <c>time</c> of <c>24:00:00</c> is a
    /// PostgreSQL value <see cref="TimeOnly"/> cannot hold, so reading it as midnight would store a
    /// different value, and a timestamp is never printed that way by PostgreSQL or this library.
    /// Checked before the BCL parse, since afterwards the next day's midnight is indistinguishable
    /// from the literal that names it.
    /// </remarks>
    private static bool StartsWithHour24(ReadOnlySpan<char> s, int timeStart)
        => s.Length >= timeStart + 3 && s[timeStart] == '2' && s[timeStart + 1] == '4' && s[timeStart + 2] == ':';

    /// <summary>The position of the time of day in a date-and-time literal, after <c>yyyy-MM-dd</c> and its separator.</summary>
    private const int TimeStartInDateTime = 11;

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

        return !StartsWithHour24(text, TimeStartInDateTime)
            && DateTime.TryParseExact(text, WallClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? DateTime.SpecifyKind(value, kind)
            : throw new FormatException(
                "Expected an ISO 8601 date and time, yyyy-MM-dd then T or a space then HH:mm[:ss[.fffffff]], "
              + "optionally followed by Z or ±HH[:mm[:ss]], or infinity / -infinity.");
    }

    /// <summary>
    /// The length of a trailing <c>±HH</c>, <c>±HH:mm</c> or <c>±HH:mm:ss</c> offset of at most
    /// <see cref="MaxOffsetHours"/> hours, or 0 — only after the time, so the date's own hyphens are never
    /// mistaken for one.
    /// </summary>
    private static int OffsetLength(ReadOnlySpan<char> s)
    {
        foreach (var length in (ReadOnlySpan<int>) [3, 6, 9])
        {
            if (s.Length < 16 + length) continue;

            var tail = s[^length..];
            if (tail[0] is not ('+' or '-')
                || !TryTwoDigits(tail[1..3], MaxOffsetHours, out _)
                || length >= 6 && (tail[3] != ':' || !TryTwoDigits(tail[4..6], 59, out _))
                || length == 9 && (tail[6] != ':' || !TryTwoDigits(tail[7..9], 59, out _)))
                continue;

            return length;
        }

        return 0;
    }

    /// <summary>
    /// PostgreSQL's <c>timestamp</c> and <c>timestamptz</c> input read an offset up to <c>±15:59:59</c>
    /// (measured on 16 and 18); <c>±16</c> is refused. Until 10.0.0 this grammar stopped at 14 hours,
    /// where <see cref="DateTimeOffset"/> does, so <c>+15:00</c> threw here (#131).
    /// </summary>
    private const int MaxOffsetHours = 15;

    private static bool TryTwoDigits(ReadOnlySpan<char> s, int max, out int value)
        => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= max;

    internal static DateTimeOffset ParseDateTimeOffset(ReadOnlySpan<char> s)
    {
        if (TryParseInfinity(s, out var positive)) return positive ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue;

        var trimmed = TruncateFraction(s);

        // The offset is Z, ±HH, ±HH:mm or ±HH:mm:ss, as for a DateTime; .NET's zzz would also read
        // +0530 and +5:30, which the grammar and the sibling types refuse (#234).
        if (StartsWithHour24(trimmed, TimeStartInDateTime) || !trimmed.EndsWith('Z') && OffsetLength(trimmed) == 0) throw InvalidDateTimeOffset();

        return DateTimeOffset.TryParseExact(trimmed, DateTimeOffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            || TryParseWideOffset(trimmed, out value)
            ? value
            : throw InvalidDateTimeOffset();
    }

    private static FormatException InvalidDateTimeOffset()
        => new("Expected an ISO 8601 date and time with an offset, yyyy-MM-dd then T or a space then "
             + "HH:mm[:ss[.fffffff]] then Z, ±HH, ±HH:mm or ±HH:mm:ss, or infinity / -infinity.");

    // The date-time part of a value whose offset DateTimeOffset cannot hold, read as a wall clock.
    private static readonly string[] WallClockFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd' 'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm",            "yyyy-MM-dd' 'HH:mm"
    ];

    /// <summary>
    /// A trailing offset <see cref="DateTimeOffset"/> cannot hold — one with seconds (<c>±HH:mm:ss</c>), or
    /// beyond <c>±14:00</c> up to <see cref="MaxOffsetHours"/> — applied to the wall clock and kept as
    /// offset zero.
    /// </summary>
    private static bool TryParseWideOffset(ReadOnlySpan<char> s, out DateTimeOffset value)
    {
        value = default;
        var length = OffsetLength(s);
        if (length == 0) return false;

        var tail    = s[^length..];
        var hours   = int.Parse(tail[1..3], NumberStyles.None, CultureInfo.InvariantCulture);
        var minutes = length >= 6 ? int.Parse(tail[4..6], NumberStyles.None, CultureInfo.InvariantCulture) : 0;
        var seconds = length == 9 ? int.Parse(tail[7..9], NumberStyles.None, CultureInfo.InvariantCulture) : 0;
        if (!DateTime.TryParseExact(s[..^length], WallClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
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

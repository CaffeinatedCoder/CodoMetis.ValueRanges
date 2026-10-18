using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// NodaTime text patterns for range bound values. Formatting always produces the canonical
/// ISO 8601 form; parsing additionally accepts the PostgreSQL wire form (space-separated
/// date/time, numeric UTC offsets) so that literals copied from <c>psql</c> output parse
/// directly. All patterns are culture-invariant — ISO 8601 has no culture-sensitive parts.
/// </summary>
/// <remarks>
/// PostgreSQL's <c>infinity</c> / <c>-infinity</c> are read for the three types Npgsql maps them to
/// — <see cref="NodaTime.Instant"/> (<c>MaxValue</c> / <c>MinValue</c>), <see cref="LocalDate"/> and
/// <see cref="LocalDateTime"/> (<c>MaxIsoValue</c> / <c>MinIsoValue</c>) — so the text PostgreSQL writes
/// for such a bound reads back as the value Npgsql would have materialized. Formatting is unchanged.
/// <para>
/// A time of day may also be written <c>HH:mm</c> — on its own, as the time part of a
/// <see cref="LocalDateTime"/>, and before an instant's offset — exactly where the core's
/// <c>IsoTemporal</c> accepts it. It is valid ISO 8601, cannot be misread, and is what a hand-written
/// <c>[09:00,17:00)</c> uses; before 9.0.0 the BCL types read it and these threw. Nothing else is
/// widened: the hour is still two digits, and an instant still needs its offset or <c>Z</c>. A
/// <see cref="LocalDateTime"/> takes a <c>Z</c> or an offset as the core's <c>DateTime</c> does, and drops it.
/// </para>
/// </remarks>
internal static class NodaPatterns
{
    /// <summary>ISO 8601 date: <c>2024-06-01</c>. Also the PostgreSQL wire form.</summary>
    internal static readonly LocalDatePattern Date = LocalDatePattern.Iso;

    /// <summary>ISO 8601 year-month: <c>2024-06</c>.</summary>
    internal static readonly YearMonthPattern YearMonth = YearMonthPattern.Iso;

    /// <summary>ISO 8601 with optional subsecond digits: <c>2024-06-01T12:30:00.123456789</c>.</summary>
    internal static readonly LocalDateTimePattern DateTime = LocalDateTimePattern.ExtendedIso;

    /// <summary>PostgreSQL wire form: <c>2024-06-01 12:30:00.123456</c>.</summary>
    private static readonly LocalDateTimePattern DateTimeSpace =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd' 'HH':'mm':'ss;FFFFFFFFF");

    /// <summary>ISO 8601 time of day with optional subsecond digits: <c>12:30:00.123456789</c>.
    /// Also the PostgreSQL wire form.</summary>
    internal static readonly LocalTimePattern Time = LocalTimePattern.ExtendedIso;

    /// <summary>ISO 8601 time of day without seconds: <c>12:30</c>. Parsing only.</summary>
    private static readonly LocalTimePattern TimeHoursMinutes =
        LocalTimePattern.CreateWithInvariantCulture("HH':'mm");

    /// <summary>Date and time without seconds, <c>T</c>-separated: <c>2024-06-01T12:30</c>. Parsing only.</summary>
    private static readonly LocalDateTimePattern DateTimeHoursMinutes =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm");

    /// <summary>Date and time without seconds, space-separated: <c>2024-06-01 12:30</c>. Parsing only.</summary>
    private static readonly LocalDateTimePattern DateTimeSpaceHoursMinutes =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd' 'HH':'mm");

    /// <summary>ISO 8601 UTC instant: <c>2024-06-01T12:30:00.123456789Z</c>.</summary>
    internal static readonly InstantPattern Instant = InstantPattern.ExtendedIso;

    /// <summary>ISO 8601 with an arbitrary offset: <c>2024-06-01T14:30:00+02:00</c>.</summary>
    private static readonly OffsetDateTimePattern InstantOffsetT =
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm':'ss;FFFFFFFFFo<G>");

    /// <summary>PostgreSQL wire form: <c>2024-06-01 12:30:00.123456+00</c>.</summary>
    private static readonly OffsetDateTimePattern InstantOffsetSpace =
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd' 'HH':'mm':'ss;FFFFFFFFFo<G>");

    /// <summary>Without seconds, with an offset or <c>Z</c>: <c>2024-06-01T14:30+02:00</c>. Parsing only.</summary>
    private static readonly OffsetDateTimePattern InstantOffsetTHoursMinutes =
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mmo<G>");

    /// <summary>Without seconds, space-separated, with an offset or <c>Z</c>: <c>2024-06-01 14:30+02</c>. Parsing only.</summary>
    private static readonly OffsetDateTimePattern InstantOffsetSpaceHoursMinutes =
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd' 'HH':'mmo<G>");

    internal static LocalDate ParseDate(string text)
        => IsInfinity(text, out var positive)
            ? positive ? LocalDate.MaxIsoValue : LocalDate.MinIsoValue
            : Date.Parse(text).GetValueOrThrow();

    internal static YearMonth ParseYearMonth(string text)
        => YearMonth.Parse(text).GetValueOrThrow();

    internal static LocalTime ParseTime(string text)
    {
        var iso = Time.Parse(text);
        if (iso.Success) return iso.Value;

        var hoursMinutes = TimeHoursMinutes.Parse(text);
        return hoursMinutes.Success ? hoursMinutes.Value : iso.GetValueOrThrow();
    }

    internal static LocalDateTime ParseDateTime(string text)
    {
        if (IsInfinity(text, out var positive)) return positive ? LocalDateTime.MaxIsoValue : LocalDateTime.MinIsoValue;

        var iso = DateTime.Parse(text);
        if (iso.Success) return iso.Value;

        foreach (var pattern in (ReadOnlySpan<LocalDateTimePattern>)[DateTimeSpace, DateTimeHoursMinutes, DateTimeSpaceHoursMinutes])
        {
            var result = pattern.Parse(text);
            if (result.Success) return result.Value;
        }

        // A Z or an offset is read and dropped, the wall clock kept: PostgreSQL's timestamp input does
        // this, and so does the core's DateTime. Until 10.0.0 it threw here (#130).
        return TryParseOffsetDateTime(text, out var withOffset) ? withOffset.LocalDateTime : iso.GetValueOrThrow();
    }

    internal static Instant ParseInstant(string text)
    {
        if (IsInfinity(text, out var positive)) return positive ? NodaTime.Instant.MaxValue : NodaTime.Instant.MinValue;

        var iso = Instant.Parse(text);
        if (iso.Success) return iso.Value;

        return TryParseOffsetDateTime(text, out var withOffset) ? withOffset.ToInstant() : iso.GetValueOrThrow();
    }

    /// <summary>
    /// PostgreSQL's <c>timestamp</c> and <c>timestamptz</c> input read an offset up to <c>±15:59:59</c>
    /// (measured on 16 and 18). NodaTime's <see cref="Offset"/> reaches <c>±18:00</c>, so until 10.0.0
    /// an instant at <c>+16:00</c>, which the server refuses, parsed here.
    /// </summary>
    private static readonly Offset MaxOffset = Offset.FromHoursAndMinutes(16, 0);

    /// <summary>A date and time with a <c>Z</c> or an offset within <see cref="MaxOffset"/>, in any of the offset forms.</summary>
    private static bool TryParseOffsetDateTime(string text, out OffsetDateTime value)
    {
        foreach (var pattern in (ReadOnlySpan<OffsetDateTimePattern>)
                 [InstantOffsetT, InstantOffsetSpace, InstantOffsetTHoursMinutes, InstantOffsetSpaceHoursMinutes])
        {
            var result = pattern.Parse(text);
            if (!result.Success) continue;
            if (result.Value.Offset >= MaxOffset || result.Value.Offset <= -MaxOffset)
                throw new FormatException("Expected an offset of at most ±15:59:59, as PostgreSQL reads it.");

            value = result.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static bool IsInfinity(string text, out bool positive)
    {
        positive = text.Equals("infinity", StringComparison.OrdinalIgnoreCase);
        return positive || text.Equals("-infinity", StringComparison.OrdinalIgnoreCase);
    }
}

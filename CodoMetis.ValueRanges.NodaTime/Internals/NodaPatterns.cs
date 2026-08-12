using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// NodaTime text patterns for range bound values. Formatting always produces the canonical
/// ISO 8601 form; parsing additionally accepts the PostgreSQL wire form (space-separated
/// date/time, numeric UTC offsets) so that literals copied from <c>psql</c> output parse
/// directly. All patterns are culture-invariant — ISO 8601 has no culture-sensitive parts.
/// </summary>
internal static class NodaPatterns
{
    /// <summary>ISO 8601 date: <c>2024-06-01</c>. Also the PostgreSQL wire form.</summary>
    internal static readonly LocalDatePattern Date = LocalDatePattern.Iso;

    /// <summary>ISO 8601 with optional subsecond digits: <c>2024-06-01T12:30:00.123456789</c>.</summary>
    internal static readonly LocalDateTimePattern DateTime = LocalDateTimePattern.ExtendedIso;

    /// <summary>PostgreSQL wire form: <c>2024-06-01 12:30:00.123456</c>.</summary>
    private static readonly LocalDateTimePattern DateTimeSpace =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd' 'HH':'mm':'ss;FFFFFFFFF");

    /// <summary>ISO 8601 UTC instant: <c>2024-06-01T12:30:00.123456789Z</c>.</summary>
    internal static readonly InstantPattern Instant = InstantPattern.ExtendedIso;

    /// <summary>ISO 8601 with an arbitrary offset: <c>2024-06-01T14:30:00+02:00</c>.</summary>
    private static readonly OffsetDateTimePattern InstantOffsetT =
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd'T'HH':'mm':'ss;FFFFFFFFFo<G>");

    /// <summary>PostgreSQL wire form: <c>2024-06-01 12:30:00.123456+00</c>.</summary>
    private static readonly OffsetDateTimePattern InstantOffsetSpace =
        OffsetDateTimePattern.CreateWithInvariantCulture("uuuu'-'MM'-'dd' 'HH':'mm':'ss;FFFFFFFFFo<G>");

    internal static LocalDate ParseDate(string text)
        => Date.Parse(text).GetValueOrThrow();

    internal static LocalDateTime ParseDateTime(string text)
    {
        var iso = DateTime.Parse(text);
        if (iso.Success) return iso.Value;

        var space = DateTimeSpace.Parse(text);
        return space.Success ? space.Value : iso.GetValueOrThrow();
    }

    internal static Instant ParseInstant(string text)
    {
        var iso = Instant.Parse(text);
        if (iso.Success) return iso.Value;

        var offsetT = InstantOffsetT.Parse(text);
        if (offsetT.Success) return offsetT.Value.ToInstant();

        var offsetSpace = InstantOffsetSpace.Parse(text);
        return offsetSpace.Success ? offsetSpace.Value.ToInstant() : iso.GetValueOrThrow();
    }
}

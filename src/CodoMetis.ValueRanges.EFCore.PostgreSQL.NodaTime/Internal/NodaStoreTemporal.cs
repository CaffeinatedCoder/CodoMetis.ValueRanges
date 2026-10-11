using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.NodaTime.Internal;

// EF1001 is EF's analyzer reading the core plugin's `EntityFrameworkCore.*.Internal` namespace as EF's
// own internal API; this satellite builds on those types by design (see the options-builder extension).
#pragma warning disable EF1001

/// <summary>
/// The NodaTime temporal values as Npgsql puts them on the wire — the satellite's counterpart of
/// <see cref="StoreTemporal"/>, measured the same way against PostgreSQL 17.
/// </summary>
/// <remarks>
/// NodaTime counts nanoseconds, so the cut to whole microseconds drops three digits more than the
/// BCL types do; Npgsql makes it toward 2000-01-01 here too. The extremes it maps to
/// <c>-infinity</c> / <c>infinity</c> are <see cref="Instant.MinValue"/> / <see cref="Instant.MaxValue"/>
/// and, for <see cref="LocalDate"/> and <see cref="LocalDateTime"/>, <c>MinIsoValue</c> /
/// <c>MaxIsoValue</c>; a time of day has none.
/// </remarks>
internal static class NodaStoreTemporal
{
    private static readonly Instant PostgresEpoch = Instant.FromUtc(2000, 1, 1, 0, 0);

    internal static Instant Truncate(Instant value)
    {
        if (StoreTemporal.InfinityConversions && (value == Instant.MinValue || value == Instant.MaxValue)) return value;

        // BigInteger: the nanoseconds across NodaTime's ±9999 years do not fit a long.
        var nanoseconds = (value - PostgresEpoch).ToBigIntegerNanoseconds();
        return PostgresEpoch + Duration.FromNanoseconds(nanoseconds / 1000 * 1000);
    }

    internal static LocalDateTime Truncate(LocalDateTime value)
        => StoreTemporal.InfinityConversions && (value == LocalDateTime.MinIsoValue || value == LocalDateTime.MaxIsoValue)
               ? value
               : Truncate(value.InUtc().ToInstant()).InUtc().LocalDateTime.WithCalendar(value.Calendar);

    internal static LocalTime Truncate(LocalTime value)
        => LocalTime.FromNanosecondsSinceMidnight(value.NanosecondOfDay / 1000 * 1000);

    internal static string? InfinityText(Instant value)
        => StoreTemporal.Text(value == Instant.MinValue, value == Instant.MaxValue);

    internal static string? InfinityText(LocalDate value)
        => StoreTemporal.Text(value == LocalDate.MinIsoValue, value == LocalDate.MaxIsoValue);

    internal static string? InfinityText(LocalDateTime value)
        => StoreTemporal.Text(value == LocalDateTime.MinIsoValue, value == LocalDateTime.MaxIsoValue);

    /// <summary>The SQL literal form of an element: <c>infinity</c> for an extreme, otherwise ISO.</summary>
    internal static string Literal(Instant value) => InfinityText(value) ?? InstantPattern.ExtendedIso.Format(value);

    internal static string Literal(LocalDate value) => InfinityText(value) ?? LocalDatePattern.Iso.Format(value);

    internal static string Literal(LocalDateTime value) => InfinityText(value) ?? LocalDateTimePattern.ExtendedIso.Format(value);
}

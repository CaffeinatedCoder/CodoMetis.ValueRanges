namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The BCL temporal values as Npgsql puts them on the wire, so that a value the plugin writes as
/// <em>text</em> — inside a JSON column, or as a SQL literal — is the value a parameter would have
/// sent.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL stores timestamps and times in whole microseconds. Npgsql gets there by integer
/// division on ticks counted from the PostgreSQL epoch, 2000-01-01, so the sub-microsecond remainder
/// is cut toward that epoch — rounding down after it and up before it. PostgreSQL's own text input
/// rounds half-to-even instead. Until 9.0.0 the JSON writer and the literals sent all seven fraction
/// digits and let the server round them, so a document or a constant held a different value from
/// the parameter and the native column for the same value: an equality could miss its own row.
/// </para>
/// <para>
/// Npgsql also maps <c>MinValue</c> / <c>MaxValue</c> of <see cref="DateTime"/>,
/// <see cref="DateTimeOffset"/> and <see cref="DateOnly"/> to <c>-infinity</c> / <c>infinity</c>
/// unless the <c>Npgsql.DisableDateTimeInfinityConversions</c> switch is set. The extremes are left
/// untouched by <c>Truncate</c> so a parameter still reaches that mapping, and
/// <c>InfinityText</c> spells them the same way in text. Both rules were measured against
/// PostgreSQL 17 rather than taken from the documentation; <c>TemporalStoreParityTests</c> holds
/// them.
/// </para>
/// </remarks>
internal static class StoreTemporal
{
    private static readonly long PostgresEpochTicks = new DateTime(2000, 1, 1).Ticks;

    /// <summary>Whether Npgsql maps the extremes to infinity — read once, as Npgsql itself does.</summary>
    internal static readonly bool InfinityConversions =
        !(AppContext.TryGetSwitch("Npgsql.DisableDateTimeInfinityConversions", out var disabled) && disabled);

    /// <summary>Ticks cut to whole microseconds toward 2000-01-01 (C# division truncates toward zero).</summary>
    internal static long TruncateTicks(long ticks)
        => PostgresEpochTicks + (ticks - PostgresEpochTicks) / 10 * 10;

    internal static DateTime Truncate(DateTime value)
        => IsInfinity(value == DateTime.MinValue, value == DateTime.MaxValue)
               ? value
               : new DateTime(TruncateTicks(value.Ticks), value.Kind);

    /// <summary>Truncates on the instant and returns it at offset zero — what <c>timestamptz</c> stores.</summary>
    internal static DateTimeOffset Truncate(DateTimeOffset value)
        => IsInfinity(value == DateTimeOffset.MinValue, value == DateTimeOffset.MaxValue)
               ? value.ToUniversalTime()
               : new DateTimeOffset(TruncateTicks(value.UtcTicks), TimeSpan.Zero);

    /// <summary><c>time</c> counts microseconds from midnight; there is nothing to truncate toward but zero.</summary>
    internal static TimeOnly Truncate(TimeOnly value)
        => new(value.Ticks / 10 * 10);

    internal static string? InfinityText(DateTime value)
        => Text(value == DateTime.MinValue, value == DateTime.MaxValue);

    internal static string? InfinityText(DateTimeOffset value)
        => Text(value == DateTimeOffset.MinValue, value == DateTimeOffset.MaxValue);

    internal static string? InfinityText(DateOnly value)
        => Text(value == DateOnly.MinValue, value == DateOnly.MaxValue);

    /// <summary>
    /// <c>-infinity</c> / <c>infinity</c> for the value Npgsql maps to it, otherwise
    /// <see langword="null"/>. Shared with the NodaTime satellite, whose extremes are other values.
    /// </summary>
    internal static string? Text(bool isMin, bool isMax)
        => !InfinityConversions ? null : isMin ? "-infinity" : isMax ? "infinity" : null;

    private static bool IsInfinity(bool isMin, bool isMax)
        => InfinityConversions && (isMin || isMax);
}

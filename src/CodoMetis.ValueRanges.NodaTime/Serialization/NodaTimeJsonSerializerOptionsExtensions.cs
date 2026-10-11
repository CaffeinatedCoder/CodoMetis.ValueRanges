using System.Text.Json;
using NodaTime;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// Extension methods for registering the NodaTime JSON converters.
/// </summary>
public static class NodaTimeRangeJsonSerializerOptionsExtensions
{
    /// <summary>
    /// Registers everything needed to serialize the NodaTime range and value set types:
    /// a <see cref="RangeJsonConverterFactory"/> (as <c>AddRangeConverters()</c> does), plus
    /// ISO 8601 element converters for the five NodaTime types the satellite's value sets are
    /// built over — <see cref="LocalDate"/>, <see cref="LocalDateTime"/>,
    /// <see cref="LocalTime"/>, <see cref="Instant"/> and <see cref="YearMonth"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The element converters are the load-bearing part. Value sets delegate element
    /// serialization to <see cref="System.Text.Json"/>, which has no built-in knowledge of
    /// NodaTime types: without a converter it writes each element as a property dump
    /// (<c>{"Calendar":{…},"Year":2024,…}</c>, or <c>{}</c> for <see cref="Instant"/>) and reads
    /// it back as <see langword="default"/> — silently, with no exception on either leg.
    /// The range types are unaffected either way; they format themselves.
    /// </para>
    /// <para>
    /// The element converters are a fallback. Any other converter on the same options that handles
    /// one of the five types is used instead, wherever it sits in
    /// <see cref="JsonSerializerOptions.Converters"/> — so this composes with
    /// NodaTime.Serialization.SystemTextJson in either registration order, and <c>ConfigureForNodaTime</c>
    /// stays authoritative for the types it covers. Reach for that package when the payload also
    /// carries NodaTime types beyond these five (<c>Duration</c>, <c>Period</c>, <c>ZonedDateTime</c>, …).
    /// </para>
    /// <para>
    /// A bare <see cref="LocalDate"/>, <see cref="LocalDateTime"/> or <see cref="YearMonth"/> in a
    /// calendar other than ISO is refused on write with an <see cref="ArgumentException"/>, as
    /// NodaTime's own converter refuses it: its ISO 8601 text would spell the other calendar's fields
    /// and read back as a different value (a Julian 2024-01-01 is ISO 2024-01-14). Convert it with
    /// <c>WithCalendar(CalendarSystem.Iso)</c> first. Sets and ranges are unaffected — they convert a
    /// non-ISO value to ISO at construction, or reject it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var options = new JsonSerializerOptions().AddNodaTimeRangeConverters();
    ///
    /// JsonSerializer.Serialize(LocalDateSet.From(new LocalDate(2024, 1, 1)), options);
    /// // ["2024-01-01"]
    /// </code>
    /// </example>
    public static JsonSerializerOptions AddNodaTimeRangeConverters(this JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Converters.Any(static c => c is RangeJsonConverterFactory))
            options.Converters.Add(new RangeJsonConverterFactory());

        if (!options.Converters.Any(static c => c is NodaElementJsonConverterFactory))
            options.Converters.Add(new NodaElementJsonConverterFactory());

        return options;
    }
}

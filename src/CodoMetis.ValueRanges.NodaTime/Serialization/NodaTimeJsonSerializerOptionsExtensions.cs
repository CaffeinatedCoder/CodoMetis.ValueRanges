using System.Text.Json;
using CodoMetis.ValueRanges.Core;
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
    /// Composes with NodaTime.Serialization.SystemTextJson in either registration order: an
    /// element type already handled by a registered converter is left alone, so
    /// <c>ConfigureForNodaTime</c> stays authoritative where it is used. Reach for that package
    /// when the payload also carries NodaTime types beyond these five
    /// (<c>Duration</c>, <c>Period</c>, <c>ZonedDateTime</c>, …).
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

        AddElementConverter<LocalDateSet, LocalDate>(options);
        AddElementConverter<LocalDateTimeSet, LocalDateTime>(options);
        AddElementConverter<LocalTimeSet, LocalTime>(options);
        AddElementConverter<InstantSet, Instant>(options);
        AddElementConverter<YearMonthSet, YearMonth>(options);

        return options;
    }

    private static void AddElementConverter<TSet, T>(JsonSerializerOptions options)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        // Whoever claimed the element type first wins — this is what makes the method
        // idempotent and order-independent against ConfigureForNodaTime.
        if (options.Converters.Any(static c => c.CanConvert(typeof(T))))
            return;

        options.Converters.Add(ValueSetTextElementJsonConverter<TSet, T>.Instance);
    }
}

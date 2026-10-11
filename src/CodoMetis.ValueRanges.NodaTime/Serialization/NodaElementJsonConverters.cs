using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using NodaTime;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// The one ISO 8601 element converter per NodaTime element type — what the satellite's sets hand
/// to <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/> and what
/// <c>AddNodaTimeRangeConverters()</c> uses for bare values.
/// </summary>
internal static class NodaElementJson
{
    internal static readonly JsonConverter<LocalDate> LocalDate =
        new IsoCalendarElementJsonConverter<LocalDateSet, LocalDate>(static value => value.Calendar);

    internal static readonly JsonConverter<LocalDateTime> LocalDateTime =
        new IsoCalendarElementJsonConverter<LocalDateTimeSet, LocalDateTime>(static value => value.Calendar);

    internal static readonly JsonConverter<YearMonth> YearMonth =
        new IsoCalendarElementJsonConverter<YearMonthSet, YearMonth>(static value => value.Calendar);

    // No calendar to check: a time of day and an instant are the same in every calendar.
    internal static readonly JsonConverter<LocalTime> LocalTime = ValueSetTextElementJsonConverter<LocalTimeSet, LocalTime>.Instance;

    internal static readonly JsonConverter<Instant> Instant = ValueSetTextElementJsonConverter<InstantSet, Instant>.Instance;
}

/// <summary>
/// The ISO 8601 text form of a calendar-bearing NodaTime value, refusing — on write — a value in
/// any calendar but ISO.
/// </summary>
/// <remarks>
/// <para>
/// The ISO patterns format a value in its own calendar's fields: a Julian <c>LocalDate(2024, 1, 1)</c>,
/// which is ISO 2024-01-14, formats as <c>2024-01-01</c> and reads back as ISO 2024-01-01 — thirteen
/// days off, silently. A Coptic date formats as <c>1740-13-05</c>, which at least fails on the way
/// back in.
/// </para>
/// <para>
/// Refused rather than converted, for two reasons. A converter that converted would hand back a
/// value unequal to the one written (<c>LocalDate</c> equality includes the calendar), which is the
/// silent kind of change this package does not make on a wire boundary. And NodaTime's own
/// converter refuses with the same exception type, so a payload behaves the same whichever of the
/// two converters ends up serving it. Normalizing belongs to construction: the satellite's sets and
/// ranges convert a non-ISO date to ISO there (or reject one, for <c>YearMonth</c>), so what they
/// hand this converter is always ISO.
/// </para>
/// </remarks>
internal sealed class IsoCalendarElementJsonConverter<TSet, T>(Func<T, CalendarSystem> calendarOf) : JsonConverter<T>
    where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ValueSetTextElementJsonConverter<TSet, T>.Instance.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var calendar = calendarOf(value);
        if (calendar != CalendarSystem.Iso)
            throw new ArgumentException(
                $"{typeof(T).Name} values must use the ISO calendar to be serialized; got {value} in the {calendar.Id} "
              + "calendar. Its ISO 8601 text would spell the fields of that calendar and read back as a different "
              + $"{typeof(T).Name}. Convert it first with WithCalendar(CalendarSystem.Iso).",
                nameof(value));

        ValueSetTextElementJsonConverter<TSet, T>.Instance.Write(writer, value, options);
    }
}

/// <summary>
/// Serves the five NodaTime element types for <c>AddNodaTimeRangeConverters()</c>, as a fallback:
/// any other converter on the same options that handles the type is used instead, wherever it sits
/// in <see cref="JsonSerializerOptions.Converters"/>.
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json takes the first converter in <see cref="JsonSerializerOptions.Converters"/> that
/// can handle a type, so registering plain converters made the call order decide: with
/// <c>AddNodaTimeRangeConverters()</c> first, its converters shadowed NodaTime.Serialization's for
/// all five types. A factory is asked for its converter only when the options are first used, by
/// which time every registration has been made — so it can look past itself and hand over the
/// converter that would otherwise have been shadowed.
/// </para>
/// <para>
/// Nothing is built by reflection: the fallbacks are closed instances in <see cref="NodaElementJson"/>,
/// and another factory's converter is obtained through its own <see cref="JsonConverterFactory.CreateConverter"/>.
/// </para>
/// </remarks>
internal sealed class NodaElementJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => Fallback(typeToConvert) is not null;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => RegisteredElsewhere(typeToConvert, options) ?? Fallback(typeToConvert);

    private static JsonConverter? RegisteredElsewhere(Type typeToConvert, JsonSerializerOptions options)
    {
        foreach (var candidate in options.Converters)
        {
            if (candidate is NodaElementJsonConverterFactory || !candidate.CanConvert(typeToConvert))
                continue;

            return candidate is JsonConverterFactory factory
                       ? factory.CreateConverter(typeToConvert, options)
                       : candidate;
        }

        return null;
    }

    private static JsonConverter? Fallback(Type type)
        => type == typeof(LocalDate)     ? NodaElementJson.LocalDate
         : type == typeof(LocalDateTime) ? NodaElementJson.LocalDateTime
         : type == typeof(LocalTime)     ? NodaElementJson.LocalTime
         : type == typeof(Instant)       ? NodaElementJson.Instant
         : type == typeof(YearMonth)     ? NodaElementJson.YearMonth
         : null;
}

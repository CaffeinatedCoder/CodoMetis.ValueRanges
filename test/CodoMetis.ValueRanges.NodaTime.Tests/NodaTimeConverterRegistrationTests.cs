using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// <c>AddNodaTimeRangeConverters()</c> is the single-call setup for the satellite: it registers
/// the core factory plus ISO 8601 element converters for the five NodaTime element types, so
/// value sets round-trip without a dependency on NodaTime.Serialization.SystemTextJson.
/// </summary>
[TestClass]
public class NodaTimeConverterRegistrationTests
{
    private static JsonSerializerOptions Standalone() =>
        new JsonSerializerOptions().AddNodaTimeRangeConverters();

    // -----------------------------------------------------------------------
    // Value sets — the types that need the element converters
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Sets_SerializeAsIsoStringArrays()
    {
        var options = Standalone();

        Assert.AreEqual("[\"2024-01-01\",\"2024-12-24\"]",
            JsonSerializer.Serialize(LocalDateSet.From(new LocalDate(2024, 12, 24), new LocalDate(2024, 1, 1)), options));
        Assert.AreEqual("[\"2024-06-01T08:00:00\"]",
            JsonSerializer.Serialize(LocalDateTimeSet.From(new LocalDateTime(2024, 6, 1, 8, 0, 0)), options));
        Assert.AreEqual("[\"09:00:00\",\"17:30:00\"]",
            JsonSerializer.Serialize(LocalTimeSet.From(new LocalTime(17, 30), new LocalTime(9, 0)), options));
        Assert.AreEqual("[\"2024-06-01T12:30:00Z\"]",
            JsonSerializer.Serialize(InstantSet.From(Instant.FromUtc(2024, 6, 1, 12, 30)), options));
        Assert.AreEqual("[\"2024-01\",\"2024-06\"]",
            JsonSerializer.Serialize(YearMonthSet.From(new YearMonth(2024, 6), new YearMonth(2024, 1)), options));
    }

    [TestMethod]
    public void Sets_RoundTrip_AllFiveTypes()
    {
        var options = Standalone();

        AssertRoundTrips(LocalDateSet.From(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 24)), options);
        AssertRoundTrips(LocalDateTimeSet.From(new LocalDateTime(2024, 6, 1, 8, 0, 0)), options);
        AssertRoundTrips(LocalTimeSet.From(new LocalTime(9, 0), new LocalTime(17, 30)), options);
        AssertRoundTrips(InstantSet.From(Instant.FromUtc(2024, 6, 1, 12, 30)), options);
        AssertRoundTrips(YearMonthSet.From(new YearMonth(2024, 1), new YearMonth(2024, 6)), options);
    }

    [TestMethod]
    public void Sets_SubSecondPrecision_SurvivesRoundTrip()
    {
        var options = Standalone();

        AssertRoundTrips(InstantSet.From(Instant.FromUtc(2024, 6, 1, 12, 30).PlusNanoseconds(123_456_789)), options);
        AssertRoundTrips(LocalDateTimeSet.From(new LocalDateTime(2024, 6, 1, 8, 0, 0).PlusNanoseconds(123_456_789)), options);
    }

    [TestMethod]
    public void Deserialize_UnparsableElement_ThrowsJsonException()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<LocalDateSet>("[\"not-a-date\"]", Standalone()));

    // -----------------------------------------------------------------------
    // Ranges — self-formatting, so they work with or without the element converters
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Ranges_RoundTrip_AllFourTypes()
    {
        var options = Standalone();

        AssertRoundTrips(LocalDateRange.CreateFinite(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31)), options);
        AssertRoundTrips(LocalDateTimeRange.CreateFinite(new LocalDateTime(2025, 1, 1, 8, 0), new LocalDateTime(2025, 1, 1, 17, 0)), options);
        AssertRoundTrips(InstantRange.CreateFinite(Instant.FromUtc(2025, 1, 1, 8, 0), Instant.FromUtc(2025, 1, 1, 17, 0)), options);
        AssertRoundTrips(YearMonthRange.CreateFinite(new YearMonth(2025, 1), new YearMonth(2025, 12)), options);
    }

    [TestMethod]
    public void RangeSet_RoundTrips()
    {
        var options = Standalone();
        var set = RangeSet<YearMonthRange, YearMonth>.From([
            YearMonthRange.CreateFinite(new YearMonth(2025, 1), new YearMonth(2025, 3)),
            YearMonthRange.CreateFinite(new YearMonth(2025, 7), new YearMonth(2025, 9))
        ]);

        AssertRoundTrips(set, options);
    }

    // -----------------------------------------------------------------------
    // Composition with NodaTime.Serialization.SystemTextJson and with itself
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ComposesWithConfigureForNodaTime_InEitherOrder()
    {
        JsonSerializerOptions[] variants =
        [
            new JsonSerializerOptions().AddNodaTimeRangeConverters().ConfigureForNodaTime(DateTimeZoneProviders.Tzdb),
            new JsonSerializerOptions().ConfigureForNodaTime(DateTimeZoneProviders.Tzdb).AddNodaTimeRangeConverters()
        ];

        foreach (var options in variants)
        {
            Assert.AreEqual("[\"2024-01-01\"]",
                JsonSerializer.Serialize(LocalDateSet.From(new LocalDate(2024, 1, 1)), options));
            AssertRoundTrips(InstantSet.From(Instant.FromUtc(2024, 6, 1, 12, 30)), options);
            AssertRoundTrips(LocalDateRange.CreateFinite(new LocalDate(2024, 1, 1), new LocalDate(2024, 3, 1)), options);
        }
    }

    /// <summary>
    /// The registration-order claim, measured where it can fail: before 9.0.0 the element
    /// converters claimed a type by being first in <c>Converters</c>, so with
    /// <c>AddNodaTimeRangeConverters()</c> called first the library's converter shadowed
    /// NodaTime's for all five types. Now the library's converter defers to any other converter
    /// on the same options, wherever it sits.
    /// </summary>
    [TestMethod]
    public void ConfigureForNodaTime_StaysAuthoritative_InEitherOrder()
    {
        JsonSerializerOptions[] variants =
        [
            new JsonSerializerOptions().AddNodaTimeRangeConverters().ConfigureForNodaTime(DateTimeZoneProviders.Tzdb),
            new JsonSerializerOptions().ConfigureForNodaTime(DateTimeZoneProviders.Tzdb).AddNodaTimeRangeConverters()
        ];

        foreach (var options in variants)
        {
            Assert.AreSame(NodaConverters.LocalDateConverter, options.GetConverter(typeof(LocalDate)));
            Assert.AreSame(NodaConverters.LocalDateTimeConverter, options.GetConverter(typeof(LocalDateTime)));
            Assert.AreSame(NodaConverters.LocalTimeConverter, options.GetConverter(typeof(LocalTime)));
            Assert.AreSame(NodaConverters.InstantConverter, options.GetConverter(typeof(Instant)));
            Assert.AreSame(NodaConverters.YearMonthConverter, options.GetConverter(typeof(YearMonth)));

            // NodaTime's own refusal, so the outcome no longer depends on the call order.
            Assert.ThrowsExactly<ArgumentException>(
                () => JsonSerializer.Serialize(new LocalDate(2024, 1, 1, CalendarSystem.Julian), options));
        }
    }

    /// <summary>
    /// Not only NodaTime's converters: the element converters are a fallback, so any converter
    /// registered for the element type wins over them, before or after.
    /// </summary>
    [TestMethod]
    public void ElementConverterRegisteredAfterwards_Wins()
    {
        var options = new JsonSerializerOptions().AddNodaTimeRangeConverters();
        options.Converters.Add(new ShoutyLocalDateConverter());

        Assert.AreEqual("\"DATE:2024-01-01\"", JsonSerializer.Serialize(new LocalDate(2024, 1, 1), options));
        Assert.AreEqual("[\"DATE:2024-01-01\"]",
            JsonSerializer.Serialize(LocalDateSet.From(new LocalDate(2024, 1, 1)), options));
        Assert.AreEqual(new LocalDate(2024, 1, 1), JsonSerializer.Deserialize<LocalDate>("\"DATE:2024-01-01\"", options));
    }

    // -----------------------------------------------------------------------
    // Bare values in a calendar other than ISO — refused, as NodaTime's converter refuses them
    // -----------------------------------------------------------------------

    /// <summary>
    /// A Julian <c>LocalDate(2024, 1, 1)</c> is ISO 2024-01-14. Written in its own calendar's
    /// fields it was <c>"2024-01-01"</c>, which reads back as ISO 2024-01-01 — thirteen days off,
    /// with no error on either leg.
    /// </summary>
    [TestMethod]
    public void BareNonIsoValues_AreRefusedOnWrite()
    {
        var options = Standalone();

        var date = Assert.ThrowsExactly<ArgumentException>(
            () => JsonSerializer.Serialize(new LocalDate(2024, 1, 1, CalendarSystem.Julian), options));
        StringAssert.Contains(date.Message, "Julian");
        StringAssert.Contains(date.Message, "WithCalendar(CalendarSystem.Iso)");

        Assert.ThrowsExactly<ArgumentException>(
            () => JsonSerializer.Serialize(new LocalDateTime(2024, 1, 1, 8, 0, CalendarSystem.Julian), options));
        Assert.ThrowsExactly<ArgumentException>(
            () => JsonSerializer.Serialize(new YearMonth(2024, 1, CalendarSystem.Julian), options));

        // Coptic wrote "1740-13-05", which failed only on the way back in; now it fails on the way out.
        Assert.ThrowsExactly<ArgumentException>(
            () => JsonSerializer.Serialize(new LocalDate(1740, 13, 5, CalendarSystem.Coptic), options));

        // Inside a DTO, as a list element and through object alike.
        Assert.ThrowsExactly<ArgumentException>(
            () => JsonSerializer.Serialize(new Dto(new LocalDate(2024, 1, 1, CalendarSystem.Julian), LocalDateSet.Empty), options));
        Assert.ThrowsExactly<ArgumentException>(
            () => JsonSerializer.Serialize<object>(new LocalDate(2024, 1, 1, CalendarSystem.Julian), options));
    }

    /// <summary>
    /// The refusal is the element converter's, not the set's: a set normalizes a non-ISO date at
    /// construction (and <c>YearMonthSet</c> rejects one), so what reaches the converter from a
    /// set is always ISO — the same date, already in the calendar the wire form speaks.
    /// </summary>
    [TestMethod]
    public void SetsOfNonIsoDates_StillSerialize_BecauseConstructionNormalized()
    {
        var options = Standalone();
        var julian  = new LocalDate(2024, 1, 1, CalendarSystem.Julian);

        Assert.AreEqual("[\"2024-01-14\"]", JsonSerializer.Serialize(LocalDateSet.From(julian), options));
        Assert.AreEqual("[\"2024-01-14\"]", JsonSerializer.Serialize(LocalDateSet.From(julian)));
        Assert.AreEqual("\"2024-01-14\"", JsonSerializer.Serialize(julian.WithCalendar(CalendarSystem.Iso), options));
    }

    [TestMethod]
    public void ComposesWithAddRangeConverters_WithoutDuplicatingTheFactory()
    {
        var options = new JsonSerializerOptions().AddRangeConverters().AddNodaTimeRangeConverters();

        Assert.AreEqual(1, options.Converters.Count(c => c is RangeJsonConverterFactory));
        Assert.AreEqual("[\"2024-01-01\"]",
            JsonSerializer.Serialize(LocalDateSet.From(new LocalDate(2024, 1, 1)), options));
    }

    [TestMethod]
    public void IsIdempotent()
    {
        var options = new JsonSerializerOptions().AddNodaTimeRangeConverters();
        var count   = options.Converters.Count;

        options.AddNodaTimeRangeConverters();

        Assert.AreEqual(count, options.Converters.Count);
        AssertRoundTrips(LocalDateSet.From(new LocalDate(2024, 1, 1)), options);
    }

    // -----------------------------------------------------------------------
    // ElementJsonConverter — the core-side fallback. The satellite's sets define one, so
    // plain AddRangeConverters() is enough for them; nothing silently property-dumps.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Sets_RoundTrip_UnderPlainAddRangeConverters()
    {
        var options = new JsonSerializerOptions().AddRangeConverters();

        Assert.AreEqual("[\"2024-01-01\"]",
            JsonSerializer.Serialize(LocalDateSet.From(new LocalDate(2024, 1, 1)), options));

        AssertRoundTrips(LocalDateSet.From(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 24)), options);
        AssertRoundTrips(LocalDateTimeSet.From(new LocalDateTime(2024, 6, 1, 8, 0, 0)), options);
        AssertRoundTrips(LocalTimeSet.From(new LocalTime(9, 0), new LocalTime(17, 30)), options);
        AssertRoundTrips(InstantSet.From(Instant.FromUtc(2024, 6, 1, 12, 30)), options);
        AssertRoundTrips(YearMonthSet.From(new YearMonth(2024, 1), new YearMonth(2024, 6)), options);
    }

    [TestMethod]
    public void Sets_RoundTrip_UnderBareConverterFactory()
    {
        var options = new JsonSerializerOptions { Converters = { new RangeJsonConverterFactory() } };

        AssertRoundTrips(InstantSet.From(Instant.FromUtc(2024, 6, 1, 12, 30)), options);
        AssertRoundTrips(YearMonthSet.From(new YearMonth(2024, 1)), options);
    }

    [TestMethod]
    public void Sets_ProduceIdenticalJson_UnderEitherRegistration()
    {
        var withHook      = new JsonSerializerOptions().AddRangeConverters();
        var withConverters = Standalone();
        var set            = LocalDateSet.From(new LocalDate(2024, 12, 24), new LocalDate(2024, 1, 1));

        Assert.AreEqual(JsonSerializer.Serialize(set, withConverters), JsonSerializer.Serialize(set, withHook));
    }

    [TestMethod]
    public void RegisteredElementConverter_TakesPrecedenceOverTheHook()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new RangeJsonConverterFactory(), new ShoutyLocalDateConverter() }
        };
        var set = LocalDateSet.From(new LocalDate(2024, 1, 1));

        Assert.AreEqual("[\"DATE:2024-01-01\"]", JsonSerializer.Serialize(set, options));
        AssertRoundTrips(set, options);
    }

    /// <summary>
    /// The hook covers set <em>elements</em>. A bare NodaTime property alongside a set is still
    /// System.Text.Json's business — which is what <c>AddNodaTimeRangeConverters()</c> is for.
    /// </summary>
    [TestMethod]
    public void BareElementProperty_NeedsTheExtensionMethod()
    {
        var dto = new Dto(new LocalDate(2024, 1, 1), LocalDateSet.From(new LocalDate(2024, 1, 1)));

        Assert.AreEqual("{\"Day\":\"2024-01-01\",\"Days\":[\"2024-01-01\"]}",
            JsonSerializer.Serialize(dto, Standalone()));

        // Without it the set still round-trips, but the bare property does not reach a converter.
        var hookOnly = JsonSerializer.Serialize(dto, new JsonSerializerOptions().AddRangeConverters());
        Assert.IsTrue(hookOnly.Contains("\"Days\":[\"2024-01-01\"]"), hookOnly);
        Assert.IsFalse(hookOnly.Contains("\"Day\":\"2024-01-01\""), hookOnly);
    }

    private sealed record Dto(LocalDate Day, LocalDateSet Days);

    private sealed class ShoutyLocalDateConverter : JsonConverter<LocalDate>
    {
        public override LocalDate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => LocalDatePattern.Iso.Parse(reader.GetString()!["DATE:".Length..]).Value;

        public override void Write(Utf8JsonWriter writer, LocalDate value, JsonSerializerOptions options)
            => writer.WriteStringValue($"DATE:{LocalDatePattern.Iso.Format(value)}");
    }

    private static void AssertRoundTrips<T>(T value, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(value, options);
        Assert.AreEqual(value, JsonSerializer.Deserialize<T>(json, options), $"Round trip failed for: {json}");
    }
}

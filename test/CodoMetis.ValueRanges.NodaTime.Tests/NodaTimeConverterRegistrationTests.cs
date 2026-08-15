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

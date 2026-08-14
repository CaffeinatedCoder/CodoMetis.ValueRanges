using System.Text.Json;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// Value set JSON support delegates element serialization to System.Text.Json — with
/// NodaTime.Serialization.SystemTextJson configured, NodaTime elements serialize as
/// ISO 8601 strings.
/// </summary>
[TestClass]
public class NodaTimeSetJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions().AddRangeConverters().ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    [TestMethod]
    public void Serialize_LocalDateSet_AsIsoStringArray()
    {
        var set = LocalDateSet.From(new LocalDate(2024, 12, 24), new LocalDate(2024, 1, 1));

        Assert.AreEqual("[\"2024-01-01\",\"2024-12-24\"]", JsonSerializer.Serialize(set, Options));
    }

    [TestMethod]
    public void RoundTrip_AllFiveTypes()
    {
        var dates      = LocalDateSet.From(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 24));
        var wallClocks = LocalDateTimeSet.From(new LocalDateTime(2024, 6, 1, 8, 0, 0));
        var instants   = InstantSet.From(Instant.FromUtc(2024, 6, 1, 12, 30));
        var times      = LocalTimeSet.From(new LocalTime(9, 0), new LocalTime(17, 30));
        var months     = YearMonthSet.From(new YearMonth(2024, 1), new YearMonth(2024, 6));

        Assert.AreEqual(dates, JsonSerializer.Deserialize<LocalDateSet>(JsonSerializer.Serialize(dates, Options), Options));
        Assert.AreEqual(wallClocks, JsonSerializer.Deserialize<LocalDateTimeSet>(JsonSerializer.Serialize(wallClocks, Options), Options));
        Assert.AreEqual(instants, JsonSerializer.Deserialize<InstantSet>(JsonSerializer.Serialize(instants, Options), Options));
        Assert.AreEqual(times, JsonSerializer.Deserialize<LocalTimeSet>(JsonSerializer.Serialize(times, Options), Options));
        Assert.AreEqual(months, JsonSerializer.Deserialize<YearMonthSet>(JsonSerializer.Serialize(months, Options), Options));
    }

    [TestMethod]
    public void Deserialize_Unsorted_Normalizes()
    {
        var set = JsonSerializer.Deserialize<LocalDateSet>("[\"2024-12-24\",\"2024-01-01\",\"2024-12-24\"]", Options);

        Assert.AreEqual(2, set!.Count);
        Assert.AreEqual(new LocalDate(2024, 1, 1), set.Values[0]);
    }

    [TestMethod]
    public void Deserialize_NullElement_Throws()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<LocalDateSet>("[\"2024-01-01\",null]", Options));
}

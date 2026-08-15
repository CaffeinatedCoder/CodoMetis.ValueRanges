using System.Text.Json;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

using LocalDateRangeSet = RangeSet<LocalDateRange, LocalDate>;

[TestClass]
public class NodaTimeJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions().AddRangeConverters();

    [TestMethod]
    public void Serialize_LocalDateRange()
    {
        var range = LocalDateRange.CreateFinite(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31));
        Assert.AreEqual("\"[2025-01-01,2025-12-31]\"", JsonSerializer.Serialize(range, Options));
    }

    [TestMethod]
    public void RoundTrip_AllThreeTypes()
    {
        var date = LocalDateRange.CreateFinite(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31));
        var wall = LocalDateTimeRange.CreateFinite(new LocalDateTime(2025, 1, 1, 8, 0), new LocalDateTime(2025, 1, 1, 17, 0));
        var inst = InstantRange.CreateFinite(Instant.FromUtc(2025, 1, 1, 8, 0), Instant.FromUtc(2025, 1, 1, 17, 0));

        Assert.AreEqual(date, JsonSerializer.Deserialize<LocalDateRange>(JsonSerializer.Serialize(date, Options), Options));
        Assert.AreEqual(wall, JsonSerializer.Deserialize<LocalDateTimeRange>(JsonSerializer.Serialize(wall, Options), Options));
        Assert.AreEqual(inst, JsonSerializer.Deserialize<InstantRange>(JsonSerializer.Serialize(inst, Options), Options));
    }

    [TestMethod]
    public void RoundTrip_SpecialShapes()
    {
        Assert.AreEqual("\"empty\"", JsonSerializer.Serialize(InstantRange.Empty, Options));
        Assert.AreEqual("\"(,)\"",   JsonSerializer.Serialize(LocalDateTimeRange.Infinite, Options));

        Assert.AreEqual(LocalDateRange.Empty,  JsonSerializer.Deserialize<LocalDateRange>("\"empty\"", Options));
        Assert.AreEqual(InstantRange.Infinite, JsonSerializer.Deserialize<InstantRange>("\"(,)\"", Options));
    }

    [TestMethod]
    public void RoundTrip_YearMonthRange()
    {
        var billing = YearMonthRange.CreateFinite(new YearMonth(2025, 1), new YearMonth(2025, 12));

        var json = JsonSerializer.Serialize(billing, Options);

        Assert.AreEqual("\"[2025-01,2025-12]\"", json);
        Assert.AreEqual(billing, JsonSerializer.Deserialize<YearMonthRange>(json, Options));
    }

    /// <summary>
    /// The satellite's unions get variant handling from the core factory: a value reached
    /// through <see langword="object"/> resolves to its sealed variant, not the union.
    /// </summary>
    [TestMethod]
    public void Serialize_ObjectTyped_UsesRangeLiteral()
    {
        Assert.AreEqual("\"[2025-01-01,2025-12-31]\"", JsonSerializer.Serialize<object>(
            LocalDateRange.CreateFinite(new LocalDate(2025, 1, 1), new LocalDate(2025, 12, 31)), Options));
        Assert.AreEqual("\"empty\"", JsonSerializer.Serialize<object>(InstantRange.Empty, Options));
        Assert.AreEqual("\"(,)\"",   JsonSerializer.Serialize<object>(LocalDateTimeRange.Infinite, Options));
        Assert.AreEqual("\"[2025-01,2025-03]\"", JsonSerializer.Serialize<object>(
            YearMonthRange.CreateFinite(new YearMonth(2025, 1), new YearMonth(2025, 3)), Options));
    }

    [TestMethod]
    public void RoundTrip_RangeSet()
    {
        var set = LocalDateRangeSet.From([
            LocalDateRange.CreateFinite(new LocalDate(2025, 1, 1), new LocalDate(2025, 1, 31)),
            LocalDateRange.CreateFinite(new LocalDate(2025, 6, 1), new LocalDate(2025, 6, 30))
        ]);

        var json = JsonSerializer.Serialize(set, Options);
        Assert.AreEqual("\"{[2025-01-01,2025-01-31],[2025-06-01,2025-06-30]}\"", json);
        Assert.AreEqual(set, JsonSerializer.Deserialize<LocalDateRangeSet>(json, Options));
    }
}

using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

[TestClass]
public class NodaTimeParseFormatTests
{
    // -----------------------------------------------------------------------
    // Formatting
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LocalDateRange_ToString_ProducesIsoLiteral()
    {
        var range = LocalDateRange.CreateFinite(new LocalDate(2025, 1, 1), new LocalDate(2025, 3, 31));
        Assert.AreEqual("[2025-01-01,2025-03-31]", range.ToString());
    }

    [TestMethod]
    public void LocalDateTimeRange_ToString_ProducesIsoLiteral()
    {
        var range = LocalDateTimeRange.CreateFinite(
            new LocalDateTime(2024, 6, 1, 0, 0),
            new LocalDateTime(2024, 7, 1, 12, 30));
        Assert.AreEqual("[2024-06-01T00:00:00,2024-07-01T12:30:00)", range.ToString());
    }

    [TestMethod]
    public void InstantRange_ToString_ProducesIsoLiteralWithZ()
    {
        var range = InstantRange.CreateFinite(
            Instant.FromUtc(2024, 6, 1, 0, 0),
            Instant.FromUtc(2024, 7, 1, 0, 0));
        Assert.AreEqual("[2024-06-01T00:00:00Z,2024-07-01T00:00:00Z)", range.ToString());
    }

    [TestMethod]
    public void ToString_SpecialShapes()
    {
        Assert.AreEqual("empty", LocalDateRange.Empty.ToString());
        Assert.AreEqual("(,)",   InstantRange.Infinite.ToString());
        Assert.AreEqual("(,2025-01-01]", LocalDateRange.CreateUnboundedStart(new LocalDate(2025, 1, 1), true).ToString());
        Assert.AreEqual("[2025-01-01,)", LocalDateRange.CreateUnboundedEnd(new LocalDate(2025, 1, 1)).ToString());
    }

    [TestMethod]
    public void Format_SubsecondDigits_OnlyWhenPresent()
    {
        var precise = InstantRange.CreateFinite(
            Instant.FromUtc(2024, 6, 1, 0, 0).PlusNanoseconds(123456789),
            Instant.FromUtc(2024, 7, 1, 0, 0));
        Assert.AreEqual("[2024-06-01T00:00:00.123456789Z,2024-07-01T00:00:00Z)", precise.ToString());
    }

    // -----------------------------------------------------------------------
    // Parsing — canonical (round-trip) form
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Parse_RoundTrips_AllShapes_LocalDate()
    {
        string[] literals = ["[2025-01-01,2025-03-31]", "(,2025-01-01]", "[2025-01-01,)", "(,)", "empty"];
        foreach (var literal in literals)
        {
            var range = LocalDateRange.Parse(literal, null);
            Assert.AreEqual(literal, range.ToString(), $"Round-trip failed for '{literal}'");
        }
    }

    [TestMethod]
    public void Parse_RoundTrips_AllShapes_Instant()
    {
        string[] literals =
        [
            "[2024-06-01T00:00:00Z,2024-07-01T00:00:00Z)",
            "(,2024-06-01T00:00:00Z)",
            "[2024-06-01T00:00:00.5Z,)",
            "(,)",
            "empty"
        ];
        foreach (var literal in literals)
        {
            var range = InstantRange.Parse(literal, null);
            Assert.AreEqual(literal, range.ToString(), $"Round-trip failed for '{literal}'");
        }
    }

    [TestMethod]
    public void Parse_DiscreteCanonicalization_HalfOpenBecomesClosed()
    {
        var range = LocalDateRange.Parse("[2025-01-01,2025-01-10)", null);
        Assert.AreEqual("[2025-01-01,2025-01-09]", range.ToString());
    }

    // -----------------------------------------------------------------------
    // Parsing — PostgreSQL wire form
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Parse_PostgresWireForm_LocalDateTime_SpaceSeparator()
    {
        // As produced by psql: ["2024-06-01 00:00:00","2024-07-01 12:30:00")
        var range = LocalDateTimeRange.Parse("[\"2024-06-01 00:00:00\",\"2024-07-01 12:30:00\")", null);
        Assert.AreEqual("[2024-06-01T00:00:00,2024-07-01T12:30:00)", range.ToString());
    }

    [TestMethod]
    public void Parse_PostgresWireForm_Instant_NumericOffset()
    {
        // As produced by psql with timezone = 'UTC': ["2024-06-01 00:00:00+00","2024-07-01 00:00:00+00")
        var range = InstantRange.Parse("[\"2024-06-01 00:00:00+00\",\"2024-07-01 00:00:00+00\")", null);
        Assert.AreEqual("[2024-06-01T00:00:00Z,2024-07-01T00:00:00Z)", range.ToString());
    }

    /// <summary>
    /// PostgreSQL writes <c>infinity</c> / <c>-infinity</c> for the values Npgsql maps from the
    /// extremes of <c>Instant</c>, <c>LocalDate</c> and <c>LocalDateTime</c>; they read back as those
    /// extremes, so text the server or <c>to_jsonb</c> produced parses to what Npgsql would have
    /// materialized.
    /// </summary>
    [TestMethod]
    public void Parse_PostgresInfinity_ReadsAsTheExtremeValues()
    {
        Assert.AreEqual(
            InstantRange.CreateFinite(Instant.MinValue, Instant.MaxValue),
            InstantRange.Parse("[-infinity,infinity)", null));
        Assert.AreEqual(
            LocalDateRange.CreateFinite(LocalDate.MinIsoValue, LocalDate.MaxIsoValue),
            LocalDateRange.Parse("[-infinity,infinity]", null));
        Assert.AreEqual(
            LocalDateTimeRange.CreateFinite(LocalDateTime.MinIsoValue, LocalDateTime.MaxIsoValue),
            LocalDateTimeRange.Parse("[-infinity,INFINITY)", null));
        Assert.AreEqual(InstantSet.From(Instant.MaxValue), InstantSet.Parse("{infinity}", null));
    }

    /// <summary>A time of day has no infinity, in PostgreSQL or in NodaTime.</summary>
    [TestMethod]
    public void Parse_Infinity_IsRefusedForATimeOfDay()
        => Assert.ThrowsExactly<FormatException>(() => LocalTimeSet.Parse("{infinity}", null));

    [TestMethod]
    public void Parse_Instant_NonUtcOffset_ConvertsToInstant()
    {
        // 14:00+02:00 is 12:00Z
        var range = InstantRange.Parse("[2024-06-01T14:00:00+02:00,2024-06-02T00:00:00Z)", null);
        Assert.AreEqual("[2024-06-01T12:00:00Z,2024-06-02T00:00:00Z)", range.ToString());
    }

    // -----------------------------------------------------------------------
    // TryParse failure paths
    // -----------------------------------------------------------------------

    [TestMethod]
    public void TryParse_Garbage_ReturnsFalseAndEmpty()
    {
        Assert.IsFalse(LocalDateRange.TryParse("not a range", null, out var r1));
        Assert.IsInstanceOfType<LocalDateRange.EmptyRange>(r1);

        Assert.IsFalse(InstantRange.TryParse("[banana,apple)", null, out var r2));
        Assert.IsInstanceOfType<InstantRange.EmptyRange>(r2);

        Assert.IsFalse(LocalDateTimeRange.TryParse(null, null, out _));
    }

    [TestMethod]
    public void TryParse_ValidLiteral_ReturnsTrue()
    {
        Assert.IsTrue(LocalDateRange.TryParse("[2025-01-01,2025-01-31]", null, out var range));
        Assert.AreEqual("[2025-01-01,2025-01-31]", range.ToString());
    }
}

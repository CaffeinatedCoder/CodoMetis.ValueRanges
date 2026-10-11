using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// A closed temporal set reads its JSON elements with the grammar its literal uses — unless a converter
/// is registered for the element type, which still wins.
/// </summary>
/// <remarks>
/// The elements used to go through System.Text.Json's own converters, which are ISO 8601 too, with one
/// silent exception: a <see cref="DateTimeOffset"/> without an offset, or a bare date, was read in the
/// parsing machine's offset, so the same payload held different instants on different servers while
/// the set's literal refused the text. The write side is System.Text.Json's, unchanged.
/// </remarks>
[TestClass]
public class TemporalSetJsonTests
{
    [TestMethod]
    [DataRow("2027-02-01T09:30:00")]
    [DataRow("2027-02-01")]
    public void ADateTimeOffsetWithoutAnOffset_IsRefused(string element)
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<DateTimeOffsetSet>($"[\"{element}\"]"));

    [TestMethod]
    public void ADateOnlyDateTime_AndAnUnpaddedTime_AreRefused()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DateTimeSet>("[\"2027-02-01\"]"));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<TimeSet>("[\"9:30\"]"));
    }

    [TestMethod]
    public void ANonStringElement_IsRefused()
        => Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffsetSet>("[20270201]"));

    /// <summary>What Go and Java write: nine fraction digits, truncated to 100 ns as System.Text.Json did.</summary>
    [TestMethod]
    public void NineFractionDigits_AreRead_Truncated()
        => Assert.AreEqual(
            DateTimeOffsetSet.From(new DateTimeOffset(2027, 2, 1, 9, 30, 0, TimeSpan.Zero).AddTicks(1_234_567)),
            JsonSerializer.Deserialize<DateTimeOffsetSet>("[\"2027-02-01T09:30:00.123456789Z\"]"));

    [TestMethod]
    public void TheWrittenForm_IsSystemTextJsons_AndReadsBack()
    {
        var set = DateTimeOffsetSet.From(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.FromHours(1)));

        var json = JsonSerializer.Serialize(set);

        Assert.AreEqual("[\"2026-01-01T09:30:00+01:00\"]", json);
        Assert.AreEqual(set, JsonSerializer.Deserialize<DateTimeOffsetSet>(json));
    }

    /// <summary>A converter registered for the element type is authoritative, as for every element type.</summary>
    [TestMethod]
    public void ARegisteredElementConverter_StillReads()
    {
        var options = new JsonSerializerOptions { Converters = { new OffsetlessIsUtcConverter() } };

        Assert.AreEqual(
            DateTimeOffsetSet.From(new DateTimeOffset(2027, 2, 1, 9, 30, 0, TimeSpan.Zero)),
            JsonSerializer.Deserialize<DateTimeOffsetSet>("[\"2027-02-01T09:30:00\"]", options));
    }

    /// <summary>A source-generated context hands over System.Text.Json's own converter: still strict.</summary>
    [TestMethod]
    public void ASourceGeneratedContext_IsStrictToo()
    {
        Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize("[\"2027-02-01T09:30:00\"]", TemporalSetJsonContext.Default.DateTimeOffsetSet));
        Assert.AreEqual(
            DateTimeOffsetSet.From(new DateTimeOffset(2027, 2, 1, 9, 30, 0, TimeSpan.FromHours(2))),
            JsonSerializer.Deserialize("[\"2027-02-01T09:30:00+02:00\"]", TemporalSetJsonContext.Default.DateTimeOffsetSet));
    }

    private sealed class OffsetlessIsUtcConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }
}

[JsonSerializable(typeof(DateTimeOffsetSet))]
[JsonSerializable(typeof(DateTimeOffset))]
internal sealed partial class TemporalSetJsonContext : JsonSerializerContext;

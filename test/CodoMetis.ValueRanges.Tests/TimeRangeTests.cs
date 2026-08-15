using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;
using TimeRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.TimeRange, System.TimeOnly>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Covers what is new with <see cref="TimeRange"/> — TimeOnly parsing/formatting, the
/// continuous half-open default, and the midnight-wrap multirange pattern. The range
/// engines themselves are exhaustively covered by the per-operation test files.
/// </summary>
[TestClass]
public class TimeRangeTests
{
    private static readonly TimeOnly Nine      = new(9, 0);
    private static readonly TimeOnly Noon      = new(12, 0);
    private static readonly TimeOnly Five      = new(17, 0);
    private static readonly TimeOnly TenTwenty = new(22, 0);

    // -----------------------------------------------------------------------
    // Construction and normalization
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CreateFinite_DefaultsToHalfOpen()
    {
        var finite = Assert.IsInstanceOfType<TimeRange.Finite>(TimeRange.CreateFinite(Nine, Five));
        Assert.IsTrue(finite.StartInclusive);
        Assert.IsFalse(finite.EndInclusive);
    }

    [TestMethod]
    public void CreateFinite_InvertedBounds_ReturnsEmpty()
    {
        Assert.IsInstanceOfType<TimeRange.EmptyRange>(TimeRange.CreateFinite(Five, Nine));
    }

    [TestMethod]
    public void CreateFinite_EqualBounds_BothInclusive_IsSingleton()
    {
        var range = TimeRange.CreateFinite(Noon, Noon, startInclusive: true, endInclusive: true);
        Assert.IsInstanceOfType<TimeRange.Finite>(range);
        Assert.IsTrue(range.Contains(Noon));
    }

    [TestMethod]
    public void CreateFinite_EqualBounds_HalfOpen_ReturnsEmpty()
    {
        Assert.IsInstanceOfType<TimeRange.EmptyRange>(TimeRange.CreateFinite(Noon, Noon));
    }

    [TestMethod]
    public void UnboundedFactories_PreserveInclusiveness()
    {
        var start = Assert.IsInstanceOfType<TimeRange.UnboundedStart>(
            TimeRange.CreateUnboundedStart(Noon, endInclusive: true));
        Assert.IsTrue(start.EndInclusive);

        var end = Assert.IsInstanceOfType<TimeRange.UnboundedEnd>(
            TimeRange.CreateUnboundedEnd(Noon, startInclusive: false));
        Assert.IsFalse(end.StartInclusive);
    }

    // -----------------------------------------------------------------------
    // Representative algebra — engines are generic, so spot checks suffice
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Contains_HalfOpen_ExcludesUpperBound()
    {
        var shift = TimeRange.CreateFinite(Nine, Five);
        Assert.IsTrue(shift.Contains(Nine));
        Assert.IsTrue(shift.Contains(new TimeOnly(16, 59, 59)));
        Assert.IsFalse(shift.Contains(Five));
    }

    [TestMethod]
    public void HalfOpenNeighbors_AreAdjacentNotOverlapping()
    {
        var morning   = TimeRange.CreateFinite(Nine, Noon);
        var afternoon = TimeRange.CreateFinite(Noon, Five);

        Assert.IsFalse(morning.Overlaps(afternoon));
        Assert.IsTrue(morning.IsAdjacentTo(afternoon));
    }

    [TestMethod]
    public void Intersect_OverlappingShifts()
    {
        var early = TimeRange.CreateFinite(Nine, new TimeOnly(14, 0));
        var late  = TimeRange.CreateFinite(Noon, Five);

        var overlap = Assert.IsInstanceOfType<TimeRange.Finite>(early.Intersect(late));
        Assert.AreEqual(Noon, overlap.Start);
        Assert.AreEqual(new TimeOnly(14, 0), overlap.End);
    }

    [TestMethod]
    public void Union_AdjacentShifts_MergeToSingleRange()
    {
        var result = TimeRange.CreateFinite(Nine, Noon).Union(TimeRange.CreateFinite(Noon, Five));

        Assert.AreEqual(1, result.Count);
        var merged = Assert.IsInstanceOfType<TimeRange.Finite>(result[0]);
        Assert.AreEqual(Nine, merged.Start);
        Assert.AreEqual(Five, merged.End);
    }

    [TestMethod]
    public void Except_LunchBreak_SplitsShift()
    {
        var day   = TimeRange.CreateFinite(Nine, Five);
        var lunch = TimeRange.CreateFinite(Noon, new TimeOnly(13, 0));

        var result = day.Except(lunch);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Contains(Nine));
        Assert.IsFalse(result.Contains(new TimeOnly(12, 30)));
        Assert.IsTrue(result.Contains(new TimeOnly(13, 0)));
    }

    // -----------------------------------------------------------------------
    // Midnight wrap — the reason a single range cannot model 22:00–06:00
    // -----------------------------------------------------------------------

    [TestMethod]
    public void OvernightWindow_IsTwoElementSet()
    {
        var nightShift = TimeRangeSet.From(
        [
            TimeRange.CreateUnboundedStart(new TimeOnly(6, 0)),
            TimeRange.CreateUnboundedEnd(TenTwenty)
        ]);

        Assert.AreEqual(2, nightShift.Count);
        Assert.IsTrue(nightShift.Contains(new TimeOnly(23, 30)));
        Assert.IsTrue(nightShift.Contains(new TimeOnly(3, 0)));
        Assert.IsFalse(nightShift.Contains(Noon));
    }

    // -----------------------------------------------------------------------
    // Parse / format
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToString_UsesRoundTripFormat()
    {
        var range = TimeRange.CreateFinite(Nine, Five);
        Assert.AreEqual("[09:00:00.0000000,17:00:00.0000000)", range.ToString());
    }

    [TestMethod]
    public void ToString_EmptyAndInfinity_UseRangeLiterals()
    {
        Assert.AreEqual("empty", TimeRange.Empty.ToString());
        Assert.AreEqual("(,)", TimeRange.Infinite.ToString());
    }

    [TestMethod]
    public void Parse_PostgresWireForm_WithoutFractionalSeconds()
    {
        var result = TimeRange.Parse("[09:00:00,17:00:00)", CultureInfo.InvariantCulture);
        Assert.AreEqual(TimeRange.CreateFinite(Nine, Five), result);
    }

    [TestMethod]
    public void Parse_PostgresWireForm_WithMicroseconds()
    {
        var result = TimeRange.Parse("[09:00:00.123456,17:00:00)", CultureInfo.InvariantCulture);
        var finite = Assert.IsInstanceOfType<TimeRange.Finite>(result);
        Assert.AreEqual(new TimeOnly(9, 0, 0).Add(TimeSpan.FromTicks(1234560)), finite.Start);
    }

    [TestMethod]
    public void Roundtrip_AllShapes()
    {
        TimeRange[] cases =
        [
            TimeRange.Empty,
            TimeRange.Infinite,
            TimeRange.CreateFinite(Nine, Five),
            TimeRange.CreateFinite(Nine, Five, startInclusive: false, endInclusive: true),
            TimeRange.CreateUnboundedStart(Noon, endInclusive: true),
            TimeRange.CreateUnboundedEnd(TenTwenty, startInclusive: false)
        ];

        foreach (var original in cases)
        {
            var s      = original.ToString();
            var parsed = TimeRange.Parse(s, CultureInfo.InvariantCulture);
            Assert.AreEqual(original, parsed, $"Roundtrip failed for: {s}");
        }
    }

    // -----------------------------------------------------------------------
    // JSON and aggregates
    // -----------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions().AddRangeConverters();

    [TestMethod]
    public void Json_Roundtrip_RangeAndSet()
    {
        var range = TimeRange.CreateFinite(Nine, Five);
        var json  = JsonSerializer.Serialize(range, JsonOptions);
        Assert.AreEqual("\"[09:00:00.0000000,17:00:00.0000000)\"", json);
        Assert.AreEqual(range, JsonSerializer.Deserialize<TimeRange>(json, JsonOptions));

        var set     = TimeRangeSet.From([range, TimeRange.CreateFinite(TenTwenty, new TimeOnly(23, 0))]);
        var setJson = JsonSerializer.Serialize(set, JsonOptions);
        Assert.AreEqual(set, JsonSerializer.Deserialize<TimeRangeSet>(setJson, JsonOptions));
    }

    [TestMethod]
    public void RangeAgg_MergesAdjacentShifts()
    {
        TimeRange[] shifts =
        [
            TimeRange.CreateFinite(Nine, Noon),
            TimeRange.CreateFinite(Noon, Five),
            TimeRange.CreateFinite(TenTwenty, new TimeOnly(23, 0))
        ];

        var set = shifts.RangeAgg();

        Assert.AreEqual(2, set.Count);
        Assert.IsTrue(set.Contains(new TimeOnly(10, 0)));
        Assert.IsTrue(set.Contains(TenTwenty));
        Assert.IsFalse(set.Contains(new TimeOnly(20, 0)));
    }

    [TestMethod]
    public void RangeIntersectAgg_FindsCommonWindow()
    {
        TimeRange[] availabilities =
        [
            TimeRange.CreateFinite(Nine, Five),
            TimeRange.CreateFinite(new TimeOnly(10, 0), new TimeOnly(18, 0)),
            TimeRange.CreateFinite(new TimeOnly(8, 0), Noon)
        ];

        var common = Assert.IsInstanceOfType<TimeRange.Finite>(availabilities.RangeIntersectAgg());
        Assert.AreEqual(new TimeOnly(10, 0), common.Start);
        Assert.AreEqual(Noon, common.End);

        Assert.IsNull(Array.Empty<TimeRange>().RangeIntersectAgg());
    }
}

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The measure of a range, per element type. Two conventions carry the whole surface: a discrete
/// domain counts its values (both bounds included), a continuous one measures the span between
/// the bounds, and every unbounded shape measures <see langword="null"/> because there is no
/// number to give.
/// </summary>
[TestClass]
public class RangeLengthTests
{
    // -------------------------------------------------------------------------
    // Discrete domains count, inclusive of both ends
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Int32_CountsValuesInclusiveOfBothBounds()
    {
        Assert.AreEqual(10L, Int32Range.CreateFinite(1, 10).Length);
        Assert.AreEqual(1L, Int32Range.CreateFinite(5, 5).Length);
    }

    /// <summary>
    /// The half-open spelling canonicalizes to closed before the measure is taken, so both
    /// spellings of the same set measure the same.
    /// </summary>
    [TestMethod]
    public void Int32_HalfOpenSpelling_MeasuresTheSameAsItsClosedForm()
    {
        var halfOpen = Int32Range.CreateFinite(1, 11, true, false);  // [1,11) ≡ [1,10]
        var closed   = Int32Range.CreateFinite(1, 10);

        Assert.AreEqual(closed, halfOpen);
        Assert.AreEqual(closed.Length, halfOpen.Length);
        Assert.AreEqual(10L, halfOpen.Length);
    }

    [TestMethod]
    public void Date_CountsDaysInclusive()
    {
        var january = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));

        Assert.AreEqual(31, january.Length, "a calendar month measures its own number of days");
        Assert.AreEqual(1, DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1)).Length);
    }

    [TestMethod]
    public void Date_LeapDayIsCounted()
        => Assert.AreEqual(29, DateRange.CreateFinite(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29)).Length);

    [TestMethod]
    public void Int64_CountsValuesInclusiveOfBothBounds()
        => Assert.AreEqual(10L, Int64Range.CreateFinite(1L, 10L).Length);

    /// <summary>
    /// The one case where the count does not fit the type that reports it: the near-full domain
    /// holds more than <see cref="long.MaxValue"/> values. Computed in decimal and refused,
    /// rather than wrapping to a plausible negative.
    /// </summary>
    [TestMethod]
    public void Int64_CountExceedingLongMaxValue_IsNullRatherThanWrapped()
    {
        var whole = Int64Range.CreateFinite(long.MinValue, long.MaxValue);

        Assert.IsNull(whole.Length);

        // Just inside the representable count, to show the refusal is not blanket.
        var representable = Int64Range.CreateFinite(0L, long.MaxValue - 1);
        Assert.AreEqual(long.MaxValue, representable.Length);
    }

    // -------------------------------------------------------------------------
    // Continuous domains measure the span
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Decimal_MeasuresTheSpanNotACount()
        => Assert.AreEqual(4m, DecimalRange.CreateFinite(1m, 5m, true, true).Length);

    /// <summary>
    /// A continuous span does not depend on which side claims the boundary point — unlike a
    /// discrete count, where inclusiveness moves the canonical bound.
    /// </summary>
    [TestMethod]
    public void Decimal_InclusivenessDoesNotChangeTheSpan()
    {
        Assert.AreEqual(4m, DecimalRange.CreateFinite(1m, 5m, true, true).Length);
        Assert.AreEqual(4m, DecimalRange.CreateFinite(1m, 5m, true, false).Length);
        Assert.AreEqual(4m, DecimalRange.CreateFinite(1m, 5m, false, true).Length);
    }

    [TestMethod]
    public void DateTime_MeasuresElapsedTime()
    {
        var shift = DateTimeRange.CreateFinite(
            new DateTime(2024, 6, 15, 9, 0, 0), new DateTime(2024, 6, 15, 17, 30, 0));

        Assert.AreEqual(TimeSpan.FromHours(8.5), shift.Length);
    }

    [TestMethod]
    public void DateTimeOffset_MeasuresRealElapsedTimeAcrossOffsets()
    {
        // 09:00+02:00 to 09:00+00:00 is two hours of real time, not zero.
        var window = DateTimeOffsetRange.CreateFinite(
            new DateTimeOffset(2024, 6, 15, 9, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2024, 6, 15, 9, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(TimeSpan.FromHours(2), window.Length);
    }

    [TestMethod]
    public void Time_MeasuresElapsedTime()
        => Assert.AreEqual(
            TimeSpan.FromHours(8),
            TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0), true, true).Length);

    // -------------------------------------------------------------------------
    // The shapes with no measure
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Empty_MeasuresZero()
    {
        Assert.AreEqual(0L, Int32Range.Empty.Length);
        Assert.AreEqual(0L, Int64Range.Empty.Length);
        Assert.AreEqual(0, DateRange.Empty.Length);
        Assert.AreEqual(0m, DecimalRange.Empty.Length);
        Assert.AreEqual(TimeSpan.Zero, DateTimeRange.Empty.Length);
        Assert.AreEqual(TimeSpan.Zero, DateTimeOffsetRange.Empty.Length);
        Assert.AreEqual(TimeSpan.Zero, TimeRange.Empty.Length);
    }

    [TestMethod]
    public void UnboundedAndInfinite_MeasureNull()
    {
        Assert.IsNull(Int32Range.CreateUnboundedStart(10, true).Length);
        Assert.IsNull(Int32Range.CreateUnboundedEnd(10).Length);
        Assert.IsNull(Int32Range.Infinite.Length);

        Assert.IsNull(DateRange.CreateUnboundedEnd(new DateOnly(2024, 1, 1)).Length);
        Assert.IsNull(DecimalRange.Infinite.Length);
        Assert.IsNull(DateTimeRange.CreateUnboundedStart(DateTime.UnixEpoch, true).Length);
    }

    /// <summary>
    /// Zero and null mean different things and must not be conflated: the empty range contains
    /// nothing (a measure of zero), an unbounded one contains too much to measure.
    /// </summary>
    [TestMethod]
    public void EmptyAndUnbounded_AreDistinguishable()
    {
        Assert.AreEqual(0L, Int32Range.Empty.Length);
        Assert.IsNull(Int32Range.Infinite.Length);
        Assert.AreNotEqual(Int32Range.Empty.Length, Int32Range.Infinite.Length);
    }
}

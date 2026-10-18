namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Covers <c>Shift</c> and <c>Expand</c> with the worked examples <c>docs/ranges.md</c> shows.
/// <c>RangeArithmeticOracleTests</c> holds every shape, inclusivity and domain edge of every range
/// type.
/// </summary>
[TestClass]
public class RangeShiftExpandTests
{
    private static DateOnly June(int day) => new(2026, 6, day);

    [TestMethod]
    public void Shift_DateRange_MovesBothBounds()
    {
        var week = DateRange.CreateFinite(June(1), June(7));

        Assert.AreEqual("[2026-06-08,2026-06-14]", week.Shift(7).ToString());
        Assert.AreEqual("[2026-05-25,2026-05-31]", week.Shift(-7).ToString());
        Assert.AreEqual(week, week.Shift(7).Shift(-7));
    }

    [TestMethod]
    public void Shift_KeepsInclusivenessAndUnboundedSides()
    {
        var slot = DecimalRange.CreateFinite(1m, 2m, false, true);  // (1,2]

        Assert.AreEqual(DecimalRange.CreateFinite(11m, 12m, false, true), slot.Shift(10m));
        Assert.AreEqual(Int32Range.CreateUnboundedEnd(15), Int32Range.CreateUnboundedEnd(5).Shift(10));
        Assert.AreSame(Int32Range.Empty, Int32Range.Empty.Shift(10));
        Assert.AreSame(Int32Range.Infinite, Int32Range.Infinite.Expand(10));
    }

    [TestMethod]
    public void Expand_BookingWithABuffer()
    {
        var meeting = DateTimeRange.CreateFinite(new DateTime(2026, 6, 1, 9, 0, 0), new DateTime(2026, 6, 1, 10, 0, 0));

        Assert.AreEqual("[2026-06-01T08:45:00.0000000,2026-06-01T10:15:00.0000000)",
                        meeting.Expand(TimeSpan.FromMinutes(15)).ToString());
        Assert.IsTrue(meeting.Expand(TimeSpan.FromMinutes(15)).Contains(meeting));
    }

    [TestMethod]
    public void Expand_Negative_ShrinksAndEmptiesPastItsWidth()
    {
        var range = Int32Range.CreateFinite(1, 10);

        Assert.AreEqual(Int32Range.CreateFinite(3, 8), range.Expand(-2));
        Assert.AreEqual(Int32Range.CreateFinite(5, 6), range.Expand(-4));
        Assert.AreEqual(Int32Range.Empty, range.Expand(-5));
    }

    [TestMethod]
    public void Shift_PastTheDomain_Throws()
    {
        Assert.ThrowsExactly<OverflowException>(() => Int32Range.CreateFinite(1, int.MaxValue).Shift(1));
        Assert.ThrowsExactly<OverflowException>(() => DateRange.CreateFinite(June(1), DateOnly.MaxValue.AddDays(-2)).Shift(3));
        Assert.ThrowsExactly<OverflowException>(() => DateRange.CreateFinite(June(1), June(2)).Shift(int.MaxValue));
        Assert.ThrowsExactly<OverflowException>(() => DateTimeRange.CreateUnboundedStart(DateTime.MaxValue.AddTicks(-1), true).Shift(TimeSpan.FromTicks(2)));
        Assert.ThrowsExactly<OverflowException>(() => DateRange.CreateFinite(June(1), June(2)).Expand(int.MinValue));

        // The unbounded side has nothing to move, so only the bounded one can overflow.
        Assert.AreEqual(Int32Range.CreateUnboundedStart(1, true), Int32Range.CreateUnboundedStart(int.MaxValue, true).Shift(1 - int.MaxValue));
    }

    [TestMethod]
    public void Shift_TimeRange_DoesNotWrapAtMidnight()
    {
        var evening = TimeRange.CreateFinite(new TimeOnly(22, 0), new TimeOnly(23, 30));

        Assert.AreEqual(TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(10, 30)), evening.Shift(TimeSpan.FromHours(-13)));

        var error = Assert.ThrowsExactly<OverflowException>(() => evening.Shift(TimeSpan.FromHours(1)));
        StringAssert.Contains(error.Message, "cannot cross midnight");
    }

    [TestMethod]
    public void Shift_DateTimeOffset_KeepsTheOffset()
    {
        var slot = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(2026, 3, 28, 9, 0, 0, TimeSpan.FromHours(1)),
                                                    new DateTimeOffset(2026, 3, 28, 10, 0, 0, TimeSpan.FromHours(1)));

        var next = (DateTimeOffsetRange.Finite)slot.Shift(TimeSpan.FromDays(1));
        Assert.AreEqual(new DateTimeOffset(2026, 3, 29, 9, 0, 0, TimeSpan.FromHours(1)), next.Start);
        Assert.AreEqual(TimeSpan.FromHours(1), next.Start.Offset, "an exact 24 hours, at the offset it had");
    }

    /// <summary>
    /// Each bound keeps its own <see cref="DateTimeKind"/> through <c>Shift</c>, <c>Expand</c> and every
    /// <c>Split</c> piece. A <c>tsrange</c> holds no kind, so the kind lives only in memory, and the EF Core
    /// write leg reads it (E09): a bound that came back <c>Unspecified</c> from a <c>Utc</c> one would be
    /// a different instant on the way to the column, with every comparison in memory still agreeing.
    /// The bounds carry different kinds on purpose, so a rebuild that took one bound's kind for both
    /// fails too.
    /// </summary>
    [TestMethod]
    [DataRow(DateTimeKind.Unspecified, DateTimeKind.Utc)]
    [DataRow(DateTimeKind.Local, DateTimeKind.Unspecified)]
    [DataRow(DateTimeKind.Utc, DateTimeKind.Local)]
    public void DateTimeRange_ShiftExpandSplit_KeepEachBoundsKind(DateTimeKind startKind, DateTimeKind endKind)
    {
        var start = new DateTime(2026, 3, 28, 9, 0, 0, startKind);
        var end   = new DateTime(2026, 3, 28, 12, 0, 0, endKind);
        var hour  = TimeSpan.FromHours(1);

        foreach (var (operation, range) in new (string, DateTimeRange)[]
                 {
                     ("Shift", DateTimeRange.CreateFinite(start, end).Shift(hour)),
                     ("Shift back", DateTimeRange.CreateFinite(start, end).Shift(-hour)),
                     ("Expand", DateTimeRange.CreateFinite(start, end).Expand(hour)),
                     ("Expand inward", DateTimeRange.CreateFinite(start, end).Expand(-hour)),
                 })
        {
            var finite = (DateTimeRange.Finite)range;
            Assert.AreEqual(startKind, finite.Start.Kind, $"{operation}: Start.Kind");
            Assert.AreEqual(endKind, finite.End.Kind, $"{operation}: End.Kind");
        }

        Assert.AreEqual(startKind, ((DateTimeRange.UnboundedEnd)DateTimeRange.CreateUnboundedEnd(start).Shift(hour)).Start.Kind, "UnboundedEnd.Shift");
        Assert.AreEqual(endKind, ((DateTimeRange.UnboundedStart)DateTimeRange.CreateUnboundedStart(end).Expand(hour)).End.Kind, "UnboundedStart.Expand");

        // Every piece starts on a boundary counted from Start, so it has Start's kind; every piece but
        // the last ends on one too, and the last ends on the range's own End.
        var pieces = DateTimeRange.CreateFinite(start, end).Split(TimeSpan.FromMinutes(45)).Cast<DateTimeRange.Finite>().ToList();
        Assert.AreEqual(4, pieces.Count);
        for (var i = 0; i < pieces.Count; i++)
        {
            Assert.AreEqual(startKind, pieces[i].Start.Kind, $"Split piece {i}: Start.Kind");
            Assert.AreEqual(i == pieces.Count - 1 ? endKind : startKind, pieces[i].End.Kind, $"Split piece {i}: End.Kind");
        }
    }

    /// <summary>
    /// The offset of each bound survives the same three operations on a <see cref="DateTimeOffsetRange"/>.
    /// <c>DateTimeOffset</c> equality ignores the offset, so it is read off each bound, not compared.
    /// </summary>
    [TestMethod]
    public void DateTimeOffsetRange_ShiftExpandSplit_KeepEachBoundsOffset()
    {
        var start = new DateTimeOffset(2026, 3, 28, 9, 0, 0, TimeSpan.FromHours(1));
        var end   = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.FromHours(-5));
        var range = DateTimeOffsetRange.CreateFinite(start, end);
        var hour  = TimeSpan.FromHours(1);

        foreach (var (operation, moved) in new (string, DateTimeOffsetRange)[] { ("Shift", range.Shift(hour)), ("Expand", range.Expand(hour)) })
        {
            var finite = (DateTimeOffsetRange.Finite)moved;
            Assert.AreEqual(start.Offset, finite.Start.Offset, $"{operation}: Start.Offset");
            Assert.AreEqual(end.Offset, finite.End.Offset, $"{operation}: End.Offset");
        }

        var pieces = range.Split(TimeSpan.FromHours(3)).Cast<DateTimeOffsetRange.Finite>().ToList();
        Assert.IsTrue(pieces.Count > 1);
        Assert.IsTrue(pieces.All(piece => piece.Start.Offset == start.Offset), "every piece starts at Start's offset");
        Assert.AreEqual(end.Offset, pieces[^1].End.Offset, "the last piece ends on End itself");
    }

    /// <summary>
    /// A bound at a temporal extreme stands for PostgreSQL's <c>infinity</c> or <c>-infinity</c> and
    /// stays where it is, as <c>infinity ± d</c> does there (#224). On a closed date range that
    /// includes the end the day before the maximum, whose exclusive end is the maximum: a column
    /// holding <c>[x,infinity)</c> reads as <c>[x,9999-12-30]</c>. The integer extremes are ordinary
    /// values and still overflow.
    /// </summary>
    [TestMethod]
    public void Shift_ABoundThatStandsForInfinity_Stays()
    {
        var openEnded = DateRange.Parse("[2026-01-01,infinity)", null);
        Assert.AreEqual("[2025-12-31,9999-12-30]", openEnded.Shift(-1).ToString());
        Assert.AreEqual(openEnded, openEnded.Shift(-1).Shift(1));
        Assert.AreEqual("[2025-12-31,9999-12-31]", DateRange.CreateFinite(June(1), DateOnly.MaxValue).Shift(-152).ToString());

        var fromMinusInfinity = DateRange.CreateFinite(DateOnly.MinValue, June(1));
        Assert.AreEqual(DateRange.CreateFinite(DateOnly.MinValue, June(2)), fromMinusInfinity.Shift(1));

        var timestamps = DateTimeRange.CreateFinite(new DateTime(2026, 1, 1), DateTime.MaxValue);
        Assert.AreEqual(DateTimeRange.CreateFinite(new DateTime(2025, 12, 31), DateTime.MaxValue), timestamps.Shift(TimeSpan.FromDays(-1)));
        Assert.AreEqual(DateTimeRange.CreateFinite(new DateTime(2025, 12, 31), DateTime.MaxValue), timestamps.Expand(TimeSpan.FromDays(1)));

        Assert.AreEqual(DateTimeOffsetRange.CreateFinite(DateTimeOffset.MinValue, DateTimeOffset.MaxValue),
                        DateTimeOffsetRange.CreateFinite(DateTimeOffset.MinValue, DateTimeOffset.MaxValue).Expand(TimeSpan.FromHours(1)));

        // A finite end moved up to the day that stands for infinity) is allowed; past the maximum throws.
        Assert.AreEqual("[2026-06-02,9999-12-30]", DateRange.CreateFinite(June(1), DateOnly.MaxValue.AddDays(-2)).Shift(1).ToString());
    }
}

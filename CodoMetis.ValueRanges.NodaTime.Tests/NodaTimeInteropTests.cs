using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.Core;

[TestClass]
public class NodaTimeInteropTests
{
    private static Instant I(int year, int month, int day) => Instant.FromUtc(year, month, day, 0, 0);

    // -----------------------------------------------------------------------
    // Interval → InstantRange (total)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToInstantRange_BoundedInterval_BecomesHalfOpenFinite()
    {
        var range = new Interval(I(2025, 1, 1), I(2025, 2, 1)).ToInstantRange();

        var finite = (IFiniteRange<Instant>)range;
        Assert.AreEqual(I(2025, 1, 1), finite.Start);
        Assert.AreEqual(I(2025, 2, 1), finite.End);
        Assert.IsTrue(finite.StartInclusive);
        Assert.IsFalse(finite.EndInclusive);
    }

    [TestMethod]
    public void ToInstantRange_AbsentEnds_MapToUnboundedShapes()
    {
        Assert.IsInstanceOfType<InstantRange.UnboundedEnd>(new Interval(I(2025, 1, 1), null).ToInstantRange());
        Assert.IsInstanceOfType<InstantRange.UnboundedStart>(new Interval(null, I(2025, 1, 1)).ToInstantRange());
        Assert.IsInstanceOfType<InstantRange.Infinity>(new Interval(null, null).ToInstantRange());
    }

    [TestMethod]
    public void ToInstantRange_DegenerateInterval_BecomesEmpty()
    {
        // [t, t) contains nothing
        var at = I(2025, 1, 1);
        Assert.IsInstanceOfType<InstantRange.EmptyRange>(new Interval(at, at).ToInstantRange());
    }

    // -----------------------------------------------------------------------
    // InstantRange → Interval (partial — only [start, end) shapes)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToInterval_DefaultShapes_ConvertExactly()
    {
        var finite = InstantRange.CreateFinite(I(2025, 1, 1), I(2025, 2, 1)).ToInterval();
        Assert.AreEqual(I(2025, 1, 1), finite.Start);
        Assert.AreEqual(I(2025, 2, 1), finite.End);

        var head = InstantRange.CreateUnboundedStart(I(2025, 1, 1)).ToInterval();
        Assert.IsFalse(head.HasStart);
        Assert.AreEqual(I(2025, 1, 1), head.End);

        var tail = InstantRange.CreateUnboundedEnd(I(2025, 1, 1)).ToInterval();
        Assert.IsFalse(tail.HasEnd);

        var everything = InstantRange.Infinite.ToInterval();
        Assert.IsFalse(everything.HasStart);
        Assert.IsFalse(everything.HasEnd);
    }

    [TestMethod]
    public void ToInterval_RoundTrip_PreservesValue()
    {
        var original = new Interval(I(2025, 1, 1), I(2025, 2, 1));
        Assert.AreEqual(original, original.ToInstantRange().ToInterval());
    }

    [TestMethod]
    public void ToInterval_EmptyRange_Throws()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => InstantRange.Empty.ToInterval());
    }

    [TestMethod]
    public void ToInterval_NonHalfOpenInclusiveness_Throws()
    {
        var closedEnd = InstantRange.CreateFinite(I(2025, 1, 1), I(2025, 2, 1), true, true);
        Assert.ThrowsExactly<InvalidOperationException>(() => closedEnd.ToInterval());

        var inclusiveHead = InstantRange.CreateUnboundedStart(I(2025, 1, 1), endInclusive: true);
        Assert.ThrowsExactly<InvalidOperationException>(() => inclusiveHead.ToInterval());

        var exclusiveTail = InstantRange.CreateUnboundedEnd(I(2025, 1, 1), startInclusive: false);
        Assert.ThrowsExactly<InvalidOperationException>(() => exclusiveTail.ToInterval());
    }

    // -----------------------------------------------------------------------
    // DateInterval ↔ LocalDateRange
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToLocalDateRange_IsFiniteClosed()
    {
        var interval = new DateInterval(new LocalDate(2025, 1, 1), new LocalDate(2025, 1, 31));
        var range    = interval.ToLocalDateRange();

        var finite = (IFiniteRange<LocalDate>)range;
        Assert.AreEqual(interval.Start, finite.Start);
        Assert.AreEqual(interval.End,   finite.End);
        Assert.IsTrue(finite.StartInclusive);
        Assert.IsTrue(finite.EndInclusive);
    }

    [TestMethod]
    public void ToDateInterval_OnFiniteVariant_RoundTrips()
    {
        var interval = new DateInterval(new LocalDate(2025, 1, 1), new LocalDate(2025, 1, 31));
        var back     = ((LocalDateRange.Finite)interval.ToLocalDateRange()).ToDateInterval();

        Assert.AreEqual(interval, back);
    }

    [TestMethod]
    public void ToLocalDateRange_NonIsoCalendar_NormalizesToIso()
    {
        var copticInterval = new DateInterval(
            new LocalDate(2025, 1, 1).WithCalendar(CalendarSystem.Coptic),
            new LocalDate(2025, 1, 31).WithCalendar(CalendarSystem.Coptic));

        var finite = (IFiniteRange<LocalDate>)copticInterval.ToLocalDateRange();
        Assert.AreEqual(CalendarSystem.Iso, finite.Start.Calendar);
        Assert.AreEqual(new LocalDate(2025, 1, 1), finite.Start);
    }
}

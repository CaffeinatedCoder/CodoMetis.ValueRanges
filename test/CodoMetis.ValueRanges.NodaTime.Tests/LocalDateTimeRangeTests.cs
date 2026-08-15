using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.Core;

[TestClass]
public class LocalDateTimeRangeTests
{
    private static LocalDateTime T(int year, int month, int day, int hour = 0, int minute = 0)
        => new(year, month, day, hour, minute);

    [TestMethod]
    public void CreateFinite_DefaultsToHalfOpenInterval()
    {
        var range  = LocalDateTimeRange.CreateFinite(T(2025, 1, 1), T(2025, 2, 1));
        var finite = (IFiniteRange<LocalDateTime>)range;

        Assert.IsTrue(finite.StartInclusive);
        Assert.IsFalse(finite.EndInclusive);
    }

    [TestMethod]
    public void CreateFinite_EqualBounds_BothInclusive_IsSingleton()
    {
        var at    = T(2025, 1, 1, 12, 0);
        var range = LocalDateTimeRange.CreateFinite(at, at, true, true);

        Assert.IsInstanceOfType<LocalDateTimeRange.Finite>(range);
        Assert.IsTrue(range.Contains(at));
    }

    [TestMethod]
    public void CreateFinite_EqualBounds_HalfOpen_IsEmpty()
    {
        var at = T(2025, 1, 1, 12, 0);
        Assert.IsInstanceOfType<LocalDateTimeRange.EmptyRange>(LocalDateTimeRange.CreateFinite(at, at));
    }

    [TestMethod]
    public void CreateFinite_InvertedBounds_IsEmpty()
    {
        var range = LocalDateTimeRange.CreateFinite(T(2025, 2, 1), T(2025, 1, 1));
        Assert.IsInstanceOfType<LocalDateTimeRange.EmptyRange>(range);
    }

    [TestMethod]
    public void CreateFinite_NonIsoCalendar_NormalizesBoundsToIso()
    {
        var copticStart = T(2025, 1, 1, 8, 30).WithCalendar(CalendarSystem.Coptic);
        var copticEnd   = T(2025, 1, 2, 8, 30).WithCalendar(CalendarSystem.Coptic);

        var finite = (IFiniteRange<LocalDateTime>)LocalDateTimeRange.CreateFinite(copticStart, copticEnd);

        Assert.AreEqual(CalendarSystem.Iso, finite.Start.Calendar);
        Assert.AreEqual(T(2025, 1, 1, 8, 30), finite.Start);
        // Time-of-day survives the calendar conversion
        Assert.AreEqual(8, finite.Start.Hour);
    }

    [TestMethod]
    public void VariantConstructors_NonIsoCalendar_NormalizeToIso()
    {
        var coptic = T(2025, 6, 15, 9, 0).WithCalendar(CalendarSystem.Coptic);

        Assert.AreEqual(CalendarSystem.Iso, new LocalDateTimeRange.UnboundedStart(coptic, true).End.Calendar);
        Assert.AreEqual(CalendarSystem.Iso, new LocalDateTimeRange.UnboundedEnd(coptic, true).Start.Calendar);
    }

    [TestMethod]
    public void MixedCalendarOperands_DoNotThrow()
    {
        var iso    = LocalDateTimeRange.CreateFinite(T(2025, 1, 1), T(2025, 6, 30));
        var coptic = LocalDateTimeRange.CreateFinite(
            T(2025, 3, 1).WithCalendar(CalendarSystem.Coptic),
            T(2025, 4, 1).WithCalendar(CalendarSystem.Coptic));

        Assert.IsTrue(iso.Overlaps(coptic));
    }

    [TestMethod]
    public void HalfOpenRanges_ShareBoundary_AreAdjacentNotOverlapping()
    {
        var morning   = LocalDateTimeRange.CreateFinite(T(2025, 1, 1, 8, 0),  T(2025, 1, 1, 12, 0));
        var afternoon = LocalDateTimeRange.CreateFinite(T(2025, 1, 1, 12, 0), T(2025, 1, 1, 18, 0));

        Assert.IsFalse(morning.Overlaps(afternoon));
        Assert.IsTrue(morning.IsAdjacentTo(afternoon));

        var union = morning.Union(afternoon);
        Assert.AreEqual(1, union.Count);
    }

    [TestMethod]
    public void Contains_RespectsHalfOpenEnd()
    {
        var slot = LocalDateTimeRange.CreateFinite(T(2025, 1, 1, 8, 0), T(2025, 1, 1, 12, 0));

        Assert.IsTrue(slot.Contains(T(2025, 1, 1, 8, 0)));
        Assert.IsTrue(slot.Contains(T(2025, 1, 1, 11, 59)));
        Assert.IsFalse(slot.Contains(T(2025, 1, 1, 12, 0)));
    }

    [TestMethod]
    public void Intersect_UnboundedStartWithUnboundedEnd()
    {
        var upToJune  = LocalDateTimeRange.CreateUnboundedStart(T(2025, 6, 1));
        var fromMarch = LocalDateTimeRange.CreateUnboundedEnd(T(2025, 3, 1));

        var overlap = (IFiniteRange<LocalDateTime>)upToJune.Intersect(fromMarch);
        Assert.AreEqual(T(2025, 3, 1), overlap.Start);
        Assert.AreEqual(T(2025, 6, 1), overlap.End);
        Assert.IsFalse(overlap.EndInclusive);
    }

    [TestMethod]
    public void Union_UnboundedStartAndUnboundedEnd_Overlapping_YieldsInfinity()
    {
        var upToJune  = LocalDateTimeRange.CreateUnboundedStart(T(2025, 6, 1));
        var fromMarch = LocalDateTimeRange.CreateUnboundedEnd(T(2025, 3, 1));

        var union = upToJune.Union(fromMarch);
        Assert.AreEqual(1, union.Count);
        Assert.IsInstanceOfType<LocalDateTimeRange.Infinity>(union[0]);
    }

    [TestMethod]
    public void SubsecondPrecision_NanosecondsAreDistinct()
    {
        var start = T(2025, 1, 1).PlusNanoseconds(1);
        var end   = T(2025, 1, 1).PlusNanoseconds(2);

        var range = LocalDateTimeRange.CreateFinite(start, end);
        Assert.IsInstanceOfType<LocalDateTimeRange.Finite>(range);
        Assert.IsTrue(range.Contains(start));
        Assert.IsFalse(range.Contains(end));
    }
}

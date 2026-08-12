using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.Core;

[TestClass]
public class InstantRangeTests
{
    private static Instant I(int year, int month, int day, int hour = 0, int minute = 0)
        => Instant.FromUtc(year, month, day, hour, minute);

    [TestMethod]
    public void CreateFinite_DefaultsToHalfOpenInterval()
    {
        var range  = InstantRange.CreateFinite(I(2025, 1, 1), I(2025, 2, 1));
        var finite = (IFiniteRange<Instant>)range;

        Assert.IsTrue(finite.StartInclusive);
        Assert.IsFalse(finite.EndInclusive);
    }

    [TestMethod]
    public void CreateFinite_EqualBounds_HalfOpen_IsEmpty()
    {
        var at = I(2025, 1, 1, 12, 0);
        Assert.IsInstanceOfType<InstantRange.EmptyRange>(InstantRange.CreateFinite(at, at));
    }

    [TestMethod]
    public void CreateFinite_InvertedBounds_IsEmpty()
    {
        Assert.IsInstanceOfType<InstantRange.EmptyRange>(
            InstantRange.CreateFinite(I(2025, 2, 1), I(2025, 1, 1)));
    }

    [TestMethod]
    public void OffsetInputs_DenoteTheSameInstant()
    {
        // 14:00+02:00 and 12:00Z are the same instant — no normalization caveat exists,
        // because the offset never enters the model in the first place.
        var fromOffset = OffsetDateTime.FromDateTimeOffset(
            new DateTimeOffset(2025, 6, 1, 14, 0, 0, TimeSpan.FromHours(2))).ToInstant();
        var utc = I(2025, 6, 1, 12, 0);

        Assert.AreEqual(utc, fromOffset);

        var range = InstantRange.CreateFinite(fromOffset, I(2025, 6, 1, 18, 0));
        Assert.IsTrue(range.Contains(utc));
    }

    [TestMethod]
    public void Contains_RespectsHalfOpenEnd()
    {
        var window = InstantRange.CreateFinite(I(2025, 1, 1, 8, 0), I(2025, 1, 1, 12, 0));

        Assert.IsTrue(window.Contains(I(2025, 1, 1, 8, 0)));
        Assert.IsFalse(window.Contains(I(2025, 1, 1, 12, 0)));
    }

    [TestMethod]
    public void HalfOpenRanges_ShareBoundary_AreAdjacentNotOverlapping()
    {
        var first  = InstantRange.CreateFinite(I(2025, 1, 1, 0, 0), I(2025, 1, 1, 12, 0));
        var second = InstantRange.CreateFinite(I(2025, 1, 1, 12, 0), I(2025, 1, 2, 0, 0));

        Assert.IsFalse(first.Overlaps(second));
        Assert.IsTrue(first.IsAdjacentTo(second));
        Assert.AreEqual(1, first.Union(second).Count);
    }

    [TestMethod]
    public void Intersect_AcrossShapes()
    {
        var finite = InstantRange.CreateFinite(I(2025, 1, 1), I(2025, 12, 31));

        Assert.AreEqual(finite, finite.Intersect(InstantRange.Infinite));
        Assert.IsInstanceOfType<InstantRange.EmptyRange>(finite.Intersect(InstantRange.Empty));

        var tail = (IFiniteRange<Instant>)finite.Intersect(InstantRange.CreateUnboundedEnd(I(2025, 7, 1)));
        Assert.AreEqual(I(2025, 7, 1), tail.Start);
    }

    [TestMethod]
    public void Merge_UnboundedShapes_SpanTheDomain()
    {
        var head = InstantRange.CreateUnboundedStart(I(2025, 3, 1), true);
        var tail = InstantRange.CreateUnboundedEnd(I(2025, 10, 1));

        Assert.IsInstanceOfType<InstantRange.Infinity>(head.Merge(tail));
    }

    [TestMethod]
    public void NanosecondPrecision_IsPreserved()
    {
        var start = I(2025, 1, 1).PlusNanoseconds(1);
        var end   = I(2025, 1, 1).PlusNanoseconds(3);

        var range = InstantRange.CreateFinite(start, end);
        Assert.IsTrue(range.Contains(I(2025, 1, 1).PlusNanoseconds(2)));
        Assert.IsFalse(range.Contains(I(2025, 1, 1)));
    }

    [TestMethod]
    public void BoundAccessors_MatchPostgresSemantics()
    {
        var window = InstantRange.CreateFinite(I(2025, 1, 1), I(2025, 2, 1));

        Assert.AreEqual(I(2025, 1, 1), window.LowerBound());
        Assert.AreEqual(I(2025, 2, 1), window.UpperBound());
        Assert.IsTrue(window.LowerBoundInclusive());
        Assert.IsFalse(window.UpperBoundInclusive());

        Assert.IsNull(InstantRange.Empty.LowerBound());
        Assert.IsNull(InstantRange.CreateUnboundedEnd(I(2025, 1, 1)).UpperBound());
    }
}

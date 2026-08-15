using System.Diagnostics;
using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.Core;

[TestClass]
public class LocalDateRangeTests
{
    private static LocalDate D(int year, int month, int day) => new(year, month, day);

    // -----------------------------------------------------------------------
    // Factories and discrete canonicalization
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CreateFinite_DefaultsToClosedInterval()
    {
        var range = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 31));

        var finite = (IFiniteRange<LocalDate>)range;
        Assert.IsTrue(finite.StartInclusive);
        Assert.IsTrue(finite.EndInclusive);
        Assert.AreEqual(D(2025, 1, 1),  finite.Start);
        Assert.AreEqual(D(2025, 1, 31), finite.End);
    }

    [TestMethod]
    public void CreateFinite_ExclusiveBounds_CanonicalizeByStepping()
    {
        // (2025-01-01, 2025-01-31) → [2025-01-02, 2025-01-30]
        var range = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 31), false, false);

        var finite = (IFiniteRange<LocalDate>)range;
        Assert.AreEqual(D(2025, 1, 2),  finite.Start);
        Assert.AreEqual(D(2025, 1, 30), finite.End);
    }

    [TestMethod]
    public void CreateFinite_ExclusiveNeighbors_IsEmpty()
    {
        // (2025-01-01, 2025-01-02) contains no date
        var range = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 2), false, false);
        Assert.IsInstanceOfType<LocalDateRange.EmptyRange>(range);
    }

    [TestMethod]
    public void CreateFinite_InvertedBounds_IsEmpty()
    {
        var range = LocalDateRange.CreateFinite(D(2025, 2, 1), D(2025, 1, 1));
        Assert.IsInstanceOfType<LocalDateRange.EmptyRange>(range);
    }

    [TestMethod]
    public void CreateUnboundedStart_ExclusiveEnd_CanonicalizesToInclusivePredecessor()
    {
        // (-∞, 2025-01-31) ≡ (-∞, 2025-01-30]
        var range = LocalDateRange.CreateUnboundedStart(D(2025, 1, 31));

        var unbounded = (IUnboundedStartRange<LocalDate>)range;
        Assert.AreEqual(D(2025, 1, 30), unbounded.End);
        Assert.IsTrue(unbounded.EndInclusive);
    }

    // -----------------------------------------------------------------------
    // Discrete stepping at the domain edges
    // -----------------------------------------------------------------------

    [TestMethod]
    public void NextValueAfter_MaxIsoValue_ReturnsNull()
    {
        Assert.IsNull(LocalDateRange.NextValueAfter(LocalDate.MaxIsoValue));
        Assert.AreEqual(D(2025, 1, 2), LocalDateRange.NextValueAfter(D(2025, 1, 1)));
    }

    [TestMethod]
    public void PreviousValueBefore_MinIsoValue_ReturnsNull()
    {
        Assert.IsNull(LocalDateRange.PreviousValueBefore(LocalDate.MinIsoValue));
        Assert.AreEqual(D(2024, 12, 31), LocalDateRange.PreviousValueBefore(D(2025, 1, 1)));
    }

    [TestMethod]
    public void CreateUnboundedStart_ExclusiveMinIsoValue_IsEmpty()
    {
        // (-∞, min) contains nothing
        var range = LocalDateRange.CreateUnboundedStart(LocalDate.MinIsoValue);
        Assert.IsInstanceOfType<LocalDateRange.EmptyRange>(range);
    }

    // -----------------------------------------------------------------------
    // Calendar normalization
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CreateFinite_NonIsoCalendar_NormalizesBoundsToIso()
    {
        var copticStart = D(2025, 1, 1).WithCalendar(CalendarSystem.Coptic);
        var copticEnd   = D(2025, 1, 31).WithCalendar(CalendarSystem.Coptic);

        var range  = LocalDateRange.CreateFinite(copticStart, copticEnd);
        var finite = (IFiniteRange<LocalDate>)range;

        Assert.AreEqual(CalendarSystem.Iso, finite.Start.Calendar);
        Assert.AreEqual(CalendarSystem.Iso, finite.End.Calendar);
        // Same day on the timeline, ISO representation
        Assert.AreEqual(D(2025, 1, 1),  finite.Start);
        Assert.AreEqual(D(2025, 1, 31), finite.End);
    }

    [TestMethod]
    public void VariantConstructors_NonIsoCalendar_NormalizeToIso()
    {
        var coptic = D(2025, 6, 15).WithCalendar(CalendarSystem.Coptic);

        Assert.AreEqual(CalendarSystem.Iso, new LocalDateRange.UnboundedStart(coptic).End.Calendar);
        Assert.AreEqual(CalendarSystem.Iso, new LocalDateRange.UnboundedEnd(coptic).Start.Calendar);
    }

    [TestMethod]
    public void MixedCalendarOperands_DoNotThrow()
    {
        // Without normalization LocalDate.CompareTo would throw ArgumentException here.
        var iso    = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 6, 30));
        var coptic = LocalDateRange.CreateFinite(
            D(2025, 3, 1).WithCalendar(CalendarSystem.Coptic),
            D(2025, 4, 1).WithCalendar(CalendarSystem.Coptic));

        Assert.IsTrue(iso.Overlaps(coptic));
        Assert.IsTrue(iso.Contains(coptic));
    }

    // -----------------------------------------------------------------------
    // Operations across shapes (wiring sanity — engines are covered by core tests)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Contains_PointAndRange()
    {
        var sprint = LocalDateRange.CreateFinite(D(2025, 1, 6), D(2025, 1, 17));

        Assert.IsTrue(sprint.Contains(D(2025, 1, 10)));
        Assert.IsFalse(sprint.Contains(D(2025, 1, 20)));
        Assert.IsTrue(sprint.Contains(LocalDateRange.CreateFinite(D(2025, 1, 8), D(2025, 1, 14))));
    }

    [TestMethod]
    public void IsAdjacentTo_OneDayApart_IsAdjacent()
    {
        var january  = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 31));
        var february = LocalDateRange.CreateFinite(D(2025, 2, 1), D(2025, 2, 28));
        var march    = LocalDateRange.CreateFinite(D(2025, 3, 1), D(2025, 3, 31));

        Assert.IsTrue(january.IsAdjacentTo(february));
        Assert.IsTrue(february.IsAdjacentTo(january));
        Assert.IsFalse(january.IsAdjacentTo(march));
    }

    [TestMethod]
    public void Intersect_OverlappingRanges()
    {
        var a = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 20));
        var b = LocalDateRange.CreateFinite(D(2025, 1, 10), D(2025, 1, 31));

        var intersection = (IFiniteRange<LocalDate>)a.Intersect(b);
        Assert.AreEqual(D(2025, 1, 10), intersection.Start);
        Assert.AreEqual(D(2025, 1, 20), intersection.End);
    }

    [TestMethod]
    public void Intersect_WithUnboundedShapes()
    {
        var finite = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 12, 31));

        var fromJuly = (IFiniteRange<LocalDate>)finite.Intersect(LocalDateRange.CreateUnboundedEnd(D(2025, 7, 1)));
        Assert.AreEqual(D(2025, 7, 1), fromJuly.Start);

        Assert.AreEqual(finite, finite.Intersect(LocalDateRange.Infinite));
        Assert.IsInstanceOfType<LocalDateRange.EmptyRange>(finite.Intersect(LocalDateRange.Empty));
    }

    [TestMethod]
    public void Union_AdjacentRanges_MergeIntoOne()
    {
        var january  = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 31));
        var february = LocalDateRange.CreateFinite(D(2025, 2, 1), D(2025, 2, 28));

        var union = january.Union(february);
        Assert.AreEqual(1, union.Count);

        var merged = (IFiniteRange<LocalDate>)union[0];
        Assert.AreEqual(D(2025, 1, 1),  merged.Start);
        Assert.AreEqual(D(2025, 2, 28), merged.End);
    }

    [TestMethod]
    public void Union_DisjointRanges_KeepsBoth()
    {
        var a = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 10));
        var b = LocalDateRange.CreateFinite(D(2025, 3, 1), D(2025, 3, 10));

        Assert.AreEqual(2, a.Union(b).Count);
    }

    [TestMethod]
    public void Except_InteriorRange_SplitsInTwo()
    {
        var year   = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 12, 31));
        var summer = LocalDateRange.CreateFinite(D(2025, 6, 1), D(2025, 8, 31));

        var result = year.Except(summer);
        Assert.AreEqual(2, result.Count);

        var before = (IFiniteRange<LocalDate>)result[0];
        var after  = (IFiniteRange<LocalDate>)result[1];
        Assert.AreEqual(D(2025, 5, 31), before.End);
        Assert.AreEqual(D(2025, 9, 1),  after.Start);
    }

    [TestMethod]
    public void Merge_DisjointRanges_CoversGap()
    {
        var a = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 10));
        var b = LocalDateRange.CreateFinite(D(2025, 3, 1), D(2025, 3, 10));

        var merged = (IFiniteRange<LocalDate>)a.Merge(b);
        Assert.AreEqual(D(2025, 1, 1),  merged.Start);
        Assert.AreEqual(D(2025, 3, 10), merged.End);
    }

    [TestMethod]
    public void BoundAccessors_MatchPostgresSemantics()
    {
        var finite = LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 31));

        Assert.AreEqual(D(2025, 1, 1),  finite.LowerBound());
        Assert.AreEqual(D(2025, 1, 31), finite.UpperBound());
        Assert.IsTrue(finite.LowerBoundInclusive());
        Assert.IsTrue(finite.UpperBoundInclusive());

        Assert.IsNull(LocalDateRange.Empty.LowerBound());
        Assert.IsNull(LocalDateRange.Infinite.UpperBound());
        Assert.IsNull(LocalDateRange.CreateUnboundedStart(D(2025, 1, 1), true).LowerBound());
    }

    [TestMethod]
    public void PatternMatching_CoversAllVariants()
    {
        static string Describe(LocalDateRange range) => range switch
        {
            LocalDateRange.EmptyRange       => "empty",
            LocalDateRange.Finite           => "finite",
            LocalDateRange.UnboundedStart   => "unbounded-start",
            LocalDateRange.UnboundedEnd     => "unbounded-end",
            LocalDateRange.Infinity         => "infinity",
            _                               => throw new UnreachableException()
        };

        Assert.AreEqual("empty",           Describe(LocalDateRange.Empty));
        Assert.AreEqual("finite",          Describe(LocalDateRange.CreateFinite(D(2025, 1, 1), D(2025, 1, 2))));
        Assert.AreEqual("unbounded-start", Describe(LocalDateRange.CreateUnboundedStart(D(2025, 1, 1), true)));
        Assert.AreEqual("unbounded-end",   Describe(LocalDateRange.CreateUnboundedEnd(D(2025, 1, 1))));
        Assert.AreEqual("infinity",        Describe(LocalDateRange.Infinite));
    }
}

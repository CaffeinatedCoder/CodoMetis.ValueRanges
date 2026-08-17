using System.Globalization;
using NodaTime;
using YearMonthRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.YearMonthRange, NodaTime.YearMonth>;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;
using CodoMetis.ValueRanges.Core;

/// <summary>
/// Covers what is new with <see cref="YearMonthRange"/> — the one-month discrete step,
/// ISO calendar enforcement, ISO year-month parsing/formatting, and the month-aligned
/// <see cref="LocalDateRange"/>/<see cref="DateInterval"/> interop. The range engines
/// themselves are exhaustively covered by the core tests.
/// </summary>
[TestClass]
public class YearMonthRangeTests
{
    private static YearMonth Ym(int year, int month) => new(year, month);

    // -----------------------------------------------------------------------
    // Discrete canonicalization — one-month step
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CreateFinite_DefaultsToClosedInterval()
    {
        var range  = YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 12));
        var finite = (IFiniteRange<YearMonth>)range;

        Assert.IsTrue(finite.StartInclusive);
        Assert.IsTrue(finite.EndInclusive);
    }

    [TestMethod]
    public void CreateFinite_HalfOpen_CanonicalizesToClosed()
    {
        // [2024-01, 2025-01) is January through December 2024.
        var result = YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2025, 1), startInclusive: true, endInclusive: false);

        var finite = Assert.IsInstanceOfType<YearMonthRange.Finite>(result);
        Assert.AreEqual(Ym(2024, 1), finite.Start);
        Assert.AreEqual(Ym(2024, 12), finite.End);
    }

    [TestMethod]
    public void CreateFinite_InvertedBounds_IsEmpty()
    {
        Assert.IsInstanceOfType<YearMonthRange.EmptyRange>(
            YearMonthRange.CreateFinite(Ym(2024, 6), Ym(2024, 1)));
    }

    [TestMethod]
    public void OneMonthApart_IsAdjacent()
    {
        var q1 = YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 3));
        var q2 = YearMonthRange.CreateFinite(Ym(2024, 4), Ym(2024, 6));

        Assert.IsTrue(q1.IsAdjacentTo(q2));
        Assert.IsFalse(q1.Overlaps(q2));

        var q3 = YearMonthRange.CreateFinite(Ym(2024, 7), Ym(2024, 9));
        Assert.IsFalse(q1.IsAdjacentTo(q3));
    }

    [TestMethod]
    public void Union_AdjacentQuarters_MergeToSingleRange()
    {
        var result = YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 3))
                                   .Union(YearMonthRange.CreateFinite(Ym(2024, 4), Ym(2024, 6)));

        Assert.AreEqual(1, result.Count);
        var merged = Assert.IsInstanceOfType<YearMonthRange.Finite>(result[0]);
        Assert.AreEqual(Ym(2024, 1), merged.Start);
        Assert.AreEqual(Ym(2024, 6), merged.End);
    }

    [TestMethod]
    public void Contains_BillingPeriod()
    {
        var subscription = YearMonthRange.CreateFinite(Ym(2024, 3), Ym(2025, 2));

        Assert.IsTrue(subscription.Contains(Ym(2024, 3)));
        Assert.IsTrue(subscription.Contains(Ym(2025, 2)));
        Assert.IsFalse(subscription.Contains(Ym(2025, 3)));
    }

    [TestMethod]
    public void UnboundedFactories_ExclusiveBoundsStepByOneMonth()
    {
        var start = Assert.IsInstanceOfType<YearMonthRange.UnboundedStart>(
            YearMonthRange.CreateUnboundedStart(Ym(2024, 6), endInclusive: false));
        Assert.AreEqual(Ym(2024, 5), start.End);

        var end = Assert.IsInstanceOfType<YearMonthRange.UnboundedEnd>(
            YearMonthRange.CreateUnboundedEnd(Ym(2024, 6), startInclusive: false));
        Assert.AreEqual(Ym(2024, 7), end.Start);
    }

    // -----------------------------------------------------------------------
    // ISO calendar enforcement — no lossless conversion exists for year-months
    // -----------------------------------------------------------------------

    [TestMethod]
    public void NonIsoCalendar_ThrowsAtConstruction()
    {
        var coptic = new YearMonth(1740, 5, CalendarSystem.Coptic);

        Assert.ThrowsExactly<ArgumentException>(
            () => YearMonthRange.CreateFinite(coptic, coptic.PlusMonths(2)));
        Assert.ThrowsExactly<ArgumentException>(
            () => YearMonthRange.CreateUnboundedStart(coptic));
        Assert.ThrowsExactly<ArgumentException>(
            () => new YearMonthRange.UnboundedEnd(coptic));
    }

    // -----------------------------------------------------------------------
    // Parse / format — ISO uuuu-MM bounds
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToString_UsesIsoYearMonth()
    {
        var range = YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 12));
        Assert.AreEqual("[2024-01,2024-12]", range.ToString());
    }

    [TestMethod]
    public void Parse_HalfOpenLiteral_Canonicalizes()
    {
        var result = YearMonthRange.Parse("[2024-01,2025-01)", CultureInfo.InvariantCulture);
        Assert.AreEqual(YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 12)), result);
    }

    [TestMethod]
    public void Roundtrip_AllShapes()
    {
        YearMonthRange[] cases =
        [
            YearMonthRange.Empty,
            YearMonthRange.Infinite,
            YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 12)),
            YearMonthRange.CreateUnboundedStart(Ym(2024, 6), endInclusive: true),
            YearMonthRange.CreateUnboundedEnd(Ym(2024, 6))
        ];

        foreach (var original in cases)
        {
            var s      = original.ToString();
            var parsed = YearMonthRange.Parse(s, CultureInfo.InvariantCulture);
            Assert.AreEqual(original, parsed, $"Roundtrip failed for: {s}");
        }
    }

    // -----------------------------------------------------------------------
    // Interop — month-aligned LocalDateRange and DateInterval
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ToLocalDateRange_ExpandsMonthsToDays()
    {
        var range = YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 2)).ToLocalDateRange();

        var finite = Assert.IsInstanceOfType<LocalDateRange.Finite>(range);
        Assert.AreEqual(new LocalDate(2024, 1, 1), finite.Start);
        Assert.AreEqual(new LocalDate(2024, 2, 29), finite.End); // leap year
    }

    [TestMethod]
    public void ToLocalDateRange_IsTotalOverAllShapes()
    {
        Assert.IsInstanceOfType<LocalDateRange.EmptyRange>(YearMonthRange.Empty.ToLocalDateRange());
        Assert.IsInstanceOfType<LocalDateRange.Infinity>(YearMonthRange.Infinite.ToLocalDateRange());

        var unboundedStart = Assert.IsInstanceOfType<LocalDateRange.UnboundedStart>(
            YearMonthRange.CreateUnboundedStart(Ym(2024, 6), endInclusive: true).ToLocalDateRange());
        Assert.AreEqual(new LocalDate(2024, 6, 30), unboundedStart.End);

        var unboundedEnd = Assert.IsInstanceOfType<LocalDateRange.UnboundedEnd>(
            YearMonthRange.CreateUnboundedEnd(Ym(2024, 6)).ToLocalDateRange());
        Assert.AreEqual(new LocalDate(2024, 6, 1), unboundedEnd.Start);
    }

    [TestMethod]
    public void ToYearMonthRange_InvertsToLocalDateRange()
    {
        YearMonthRange[] cases =
        [
            YearMonthRange.Empty,
            YearMonthRange.Infinite,
            YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 12)),
            YearMonthRange.CreateUnboundedStart(Ym(2024, 6), endInclusive: true),
            YearMonthRange.CreateUnboundedEnd(Ym(2024, 6))
        ];

        foreach (var original in cases)
            Assert.AreEqual(original, original.ToLocalDateRange().ToYearMonthRange());
    }

    /// <summary>
    /// The month→day expansion asks the calendar for each month's last day, so the value that has
    /// to survive is the one a fixed-length assumption would get wrong: February, in both a leap
    /// and a common year, and a span crossing a year boundary.
    /// </summary>
    /// <remarks>
    /// The shape sweep above cannot catch this — every shape converts and inverts cleanly whether
    /// February ends on the 28th or the 29th, because the same assumption is applied on both legs.
    /// Only pinning the expanded dates does.
    /// </remarks>
    [TestMethod]
    public void ToLocalDateRange_MonthLengths_FollowTheCalendar()
    {
        Assert.AreEqual(
            "[2024-02-01,2024-02-29]",
            YearMonthRange.CreateFinite(Ym(2024, 2), Ym(2024, 2)).ToLocalDateRange().ToString());

        Assert.AreEqual(
            "[2023-02-01,2023-02-28]",
            YearMonthRange.CreateFinite(Ym(2023, 2), Ym(2023, 2)).ToLocalDateRange().ToString());

        Assert.AreEqual(
            "[2024-12-01,2025-01-31]",
            YearMonthRange.CreateFinite(Ym(2024, 12), Ym(2025, 1)).ToLocalDateRange().ToString());

        // …and each one still inverts, so the expansion is exact rather than merely consistent.
        foreach (var months in new[]
                 {
                     YearMonthRange.CreateFinite(Ym(2024, 2), Ym(2024, 2)),
                     YearMonthRange.CreateFinite(Ym(2023, 2), Ym(2023, 2)),
                     YearMonthRange.CreateFinite(Ym(2024, 12), Ym(2025, 1))
                 })
        {
            Assert.AreEqual(months, months.ToLocalDateRange().ToYearMonthRange());
        }
    }

    /// <summary>
    /// Month enumeration at the top of the ISO calendar, where a step past the maximum would wrap
    /// rather than stop.
    /// </summary>
    [TestMethod]
    public void Values_AtTheDomainMaximum_Terminates()
    {
        var last  = LocalDate.MaxIsoValue.ToYearMonth();
        var range = YearMonthRange.CreateFinite(last.PlusMonths(-2), last);

        CollectionAssert.AreEqual(
            new[] { last.PlusMonths(-2), last.PlusMonths(-1), last },
            range.Values().ToArray());
    }

    [TestMethod]
    public void ToYearMonthRange_PartialMonth_Throws()
    {
        var midMonthStart = LocalDateRange.CreateFinite(new LocalDate(2024, 1, 15), new LocalDate(2024, 3, 31));
        Assert.ThrowsExactly<InvalidOperationException>(() => midMonthStart.ToYearMonthRange());

        var midMonthEnd = LocalDateRange.CreateFinite(new LocalDate(2024, 1, 1), new LocalDate(2024, 3, 15));
        Assert.ThrowsExactly<InvalidOperationException>(() => midMonthEnd.ToYearMonthRange());
    }

    [TestMethod]
    public void ToDateInterval_CoversWholeMonths()
    {
        var finite   = (YearMonthRange.Finite)YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 3));
        var interval = finite.ToDateInterval();

        Assert.AreEqual(new LocalDate(2024, 1, 1), interval.Start);
        Assert.AreEqual(new LocalDate(2024, 3, 31), interval.End);
    }

    // -----------------------------------------------------------------------
    // Aggregates
    // -----------------------------------------------------------------------

    [TestMethod]
    public void RangeAgg_MergesAdjacentPeriods()
    {
        YearMonthRange[] periods =
        [
            YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 3)),
            YearMonthRange.CreateFinite(Ym(2024, 4), Ym(2024, 6)),
            YearMonthRange.CreateFinite(Ym(2024, 10), Ym(2024, 12))
        ];

        var set = periods.RangeAgg();

        Assert.AreEqual(2, set.Count);
        Assert.IsTrue(set.Contains(Ym(2024, 5)));
        Assert.IsFalse(set.Contains(Ym(2024, 8)));
    }

    [TestMethod]
    public void RangeIntersectAgg_FindsCommonPeriod()
    {
        YearMonthRange[] contracts =
        [
            YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 12)),
            YearMonthRange.CreateFinite(Ym(2024, 6), Ym(2025, 5))
        ];

        var common = Assert.IsInstanceOfType<YearMonthRange.Finite>(contracts.RangeIntersectAgg());
        Assert.AreEqual(Ym(2024, 6), common.Start);
        Assert.AreEqual(Ym(2024, 12), common.End);

        Assert.IsNull(Array.Empty<YearMonthRange>().RangeIntersectAgg());
    }

    [TestMethod]
    public void RangeSet_NormalizesOnConstruction()
    {
        var set = YearMonthRangeSet.From(
        [
            YearMonthRange.CreateFinite(Ym(2024, 4), Ym(2024, 6)),
            YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 5)),
            YearMonthRange.Empty
        ]);

        Assert.AreEqual(1, set.Count);
        Assert.AreEqual(YearMonthRange.CreateFinite(Ym(2024, 1), Ym(2024, 6)), set[0]);
    }
}

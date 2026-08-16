using NodaTime;
using LocalDateRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.LocalDateRange, NodaTime.LocalDate>;
using YearMonthRangeSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.YearMonthRange, NodaTime.YearMonth>;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

/// <summary>
/// The NodaTime side of the measure, the enumeration and the value-set bridge. The conventions
/// match the BCL types — discrete domains count inclusively, continuous ones measure a span —
/// but the measure's <em>type</em> differs, because NodaTime distinguishes a calendar period
/// from an elapsed duration and the BCL does not.
/// </summary>
[TestClass]
public class NodaTimeLengthAndBridgeTests
{
    // -------------------------------------------------------------------------
    // Length
    // -------------------------------------------------------------------------

    [TestMethod]
    public void LocalDate_CountsDaysInclusive()
        => Assert.AreEqual(
            31,
            LocalDateRange.CreateFinite(new LocalDate(2024, 1, 1), new LocalDate(2024, 1, 31)).Length);

    [TestMethod]
    public void LocalDate_CountsTheLeapDay()
        => Assert.AreEqual(
            29,
            LocalDateRange.CreateFinite(new LocalDate(2024, 2, 1), new LocalDate(2024, 2, 29)).Length);

    [TestMethod]
    public void YearMonth_CountsMonthsInclusive()
    {
        Assert.AreEqual(
            3, YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 3)).Length);

        Assert.AreEqual(
            12, YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 12)).Length);

        // Across a year boundary, where the naive month subtraction would go negative.
        Assert.AreEqual(
            4, YearMonthRange.CreateFinite(new YearMonth(2023, 11), new YearMonth(2024, 2)).Length);
    }

    /// <summary>
    /// An instant range measures a <see cref="Duration"/> — exact elapsed time — because both
    /// bounds sit on the time line.
    /// </summary>
    [TestMethod]
    public void Instant_MeasuresADuration()
    {
        var range = InstantRange.CreateFinite(
            Instant.FromUtc(2024, 6, 15, 9, 0), Instant.FromUtc(2024, 6, 15, 17, 0));

        Assert.AreEqual(Duration.FromHours(8), range.Length);
    }

    /// <summary>
    /// A wall-clock range measures a <see cref="Period"/> instead, which is a calendar quantity:
    /// one month is not a fixed number of hours.
    /// </summary>
    [TestMethod]
    public void LocalDateTime_MeasuresAPeriod()
    {
        var range = LocalDateTimeRange.CreateFinite(
            new LocalDateTime(2024, 1, 1, 0, 0), new LocalDateTime(2024, 3, 1, 0, 0));

        Assert.AreEqual(Period.Between(new LocalDateTime(2024, 1, 1, 0, 0), new LocalDateTime(2024, 3, 1, 0, 0)),
                        range.Length);
        Assert.AreEqual(2, range.Length!.Months);
    }

    [TestMethod]
    public void Empty_MeasuresZero()
    {
        Assert.AreEqual(0, LocalDateRange.Empty.Length);
        Assert.AreEqual(0, YearMonthRange.Empty.Length);
        Assert.AreEqual(Duration.Zero, InstantRange.Empty.Length);
        Assert.AreEqual(Period.Zero, LocalDateTimeRange.Empty.Length);
    }

    [TestMethod]
    public void Unbounded_MeasuresNull()
    {
        Assert.IsNull(LocalDateRange.Infinite.Length);
        Assert.IsNull(YearMonthRange.CreateUnboundedEnd(new YearMonth(2024, 1)).Length);
        Assert.IsNull(InstantRange.Infinite.Length);
        Assert.IsNull(LocalDateTimeRange.Infinite.Length);
    }

    // -------------------------------------------------------------------------
    // Values()
    // -------------------------------------------------------------------------

    [TestMethod]
    public void LocalDate_Values_WalksDayByDayAcrossTheLeapDay()
        => CollectionAssert.AreEqual(
            new[] { new LocalDate(2024, 2, 28), new LocalDate(2024, 2, 29), new LocalDate(2024, 3, 1) },
            LocalDateRange.CreateFinite(new LocalDate(2024, 2, 28), new LocalDate(2024, 3, 1)).Values().ToList());

    [TestMethod]
    public void YearMonth_Values_WalksMonthByMonthAcrossTheYearBoundary()
        => CollectionAssert.AreEqual(
            new[]
            {
                new YearMonth(2023, 11), new YearMonth(2023, 12),
                new YearMonth(2024, 1), new YearMonth(2024, 2)
            },
            YearMonthRange.CreateFinite(new YearMonth(2023, 11), new YearMonth(2024, 2)).Values().ToList());

    [TestMethod]
    public void Values_IsDeclaredByDiscreteTypesOnly()
    {
        Assert.IsNotNull(typeof(LocalDateRange).GetMethod("Values"));
        Assert.IsNotNull(typeof(YearMonthRange).GetMethod("Values"));

        Assert.IsNull(typeof(InstantRange).GetMethod("Values"), "Instant is a continuous domain");
        Assert.IsNull(typeof(LocalDateTimeRange).GetMethod("Values"), "LocalDateTime is continuous");
    }

    [TestMethod]
    public void Values_Unbounded_Throws()
        => Assert.ThrowsExactly<NotSupportedException>(
            () => LocalDateRange.CreateUnboundedEnd(new LocalDate(2024, 1, 1)).Values());

    // -------------------------------------------------------------------------
    // Value-set bridge
    // -------------------------------------------------------------------------

    [TestMethod]
    public void LocalDateSet_ToRangeSet_CollapsesConsecutiveDates()
    {
        var set = LocalDateSet.From(
            new LocalDate(2024, 5, 3), new LocalDate(2024, 5, 4),
            new LocalDate(2024, 5, 5), new LocalDate(2024, 8, 1));

        var ranges = set.ToRangeSet();

        Assert.AreEqual(2, ranges.Count);
        Assert.AreEqual(3, ranges[0].Length);
        Assert.AreEqual(1, ranges[1].Length);
    }

    [TestMethod]
    public void YearMonthSet_ToRangeSet_CollapsesAWholeYear()
    {
        var months = YearMonthSet.From(Enumerable.Range(1, 12).Select(m => new YearMonth(2024, m)));

        var ranges = months.ToRangeSet();

        Assert.AreEqual(1, ranges.Count);
        Assert.AreEqual(YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 12)), ranges[0]);
    }

    [TestMethod]
    public void RoundTrip_PreservesTheSet()
    {
        var dates = LocalDateSet.From(
            new LocalDate(2024, 5, 3), new LocalDate(2024, 5, 4), new LocalDate(2024, 8, 1));
        Assert.AreEqual(dates, dates.ToRangeSet().ToLocalDateSet());

        var months = YearMonthSet.From(new YearMonth(2024, 1), new YearMonth(2024, 2), new YearMonth(2024, 7));
        Assert.AreEqual(months, months.ToRangeSet().ToYearMonthSet());
    }

    [TestMethod]
    public void ToLocalDateSet_Unbounded_Throws()
        => Assert.ThrowsExactly<NotSupportedException>(
            () => LocalDateRangeSet.From([LocalDateRange.CreateUnboundedEnd(new LocalDate(2024, 1, 1))])
                                   .ToLocalDateSet());

    [TestMethod]
    public void YearMonthRangeSet_ExpandsInclusiveOfBothBounds()
    {
        var ranges = YearMonthRangeSet.From([
            YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 3))
        ]);

        Assert.AreEqual(
            YearMonthSet.From(new YearMonth(2024, 1), new YearMonth(2024, 2), new YearMonth(2024, 3)),
            ranges.ToYearMonthSet());
    }

    // -------------------------------------------------------------------------
    // Clamp and the set indexer
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Clamp_PullsIntoTheRange()
    {
        var year = LocalDateRange.CreateFinite(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31));

        Assert.AreEqual(new LocalDate(2024, 1, 1), year.Clamp(new LocalDate(2020, 3, 3)));
        Assert.AreEqual(new LocalDate(2024, 12, 31), year.Clamp(new LocalDate(2030, 3, 3)));
        Assert.AreEqual(new LocalDate(2024, 6, 15), year.Clamp(new LocalDate(2024, 6, 15)));
        Assert.IsNull(LocalDateRange.Empty.Clamp(new LocalDate(2024, 6, 15)));
    }

    [TestMethod]
    public void Indexer_ReturnsCanonicalOrder()
    {
        var set = LocalDateSet.From(new LocalDate(2024, 8, 1), new LocalDate(2024, 5, 3));

        Assert.AreEqual(new LocalDate(2024, 5, 3), set[0]);
        Assert.AreEqual(new LocalDate(2024, 8, 1), set[1]);
    }
}

using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// Covers <c>Deconstruct</c> on the NodaTime variants that are not positional records: every
/// <c>Finite</c>, and the unbounded variants of the three calendar-validated types. Mostly a
/// compile check of the syntax; <c>VariantDeconstructContractTests</c> (conventions) holds every
/// variant to its constructor.
/// </summary>
[TestClass]
public class NodaTimeDeconstructTests
{
    [TestMethod]
    public void LocalDateRange_Finite_DeconstructsIntoClosedBounds()
    {
        var range = LocalDateRange.CreateFinite(new LocalDate(2026, 1, 1), new LocalDate(2026, 7, 1), endInclusive: false);

        var (start, end) = (LocalDateRange.Finite)range;

        Assert.AreEqual(new LocalDate(2026, 1, 1), start);
        Assert.AreEqual(new LocalDate(2026, 6, 30), end);
    }

    [TestMethod]
    public void InstantRange_Finite_DeconstructsIntoBoundsAndInclusiveness()
    {
        var (from, to) = (Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2026, 1, 2, 0, 0));
        var range      = InstantRange.CreateFinite(from, to);

        var (start, end, startInclusive, endInclusive) = (InstantRange.Finite)range;

        Assert.AreEqual(range, InstantRange.CreateFinite(start, end, startInclusive, endInclusive));
    }

    [TestMethod]
    public void YearMonthRange_UnboundedVariants_MatchPositionally()
    {
        var upTo   = YearMonthRange.CreateUnboundedStart(new YearMonth(2026, 6), endInclusive: true);
        var onward = YearMonthRange.CreateUnboundedEnd(new YearMonth(2026, 1));

        Assert.IsTrue(upTo is YearMonthRange.UnboundedStart(var end) && end == new YearMonth(2026, 6));
        Assert.IsTrue(onward is YearMonthRange.UnboundedEnd(var start) && start == new YearMonth(2026, 1));
    }

    [TestMethod]
    public void LocalDateTimeRange_UnboundedStart_MatchesPositionallyWithInclusiveness()
    {
        var range = LocalDateTimeRange.CreateUnboundedStart(new LocalDateTime(2026, 1, 1, 9, 30), endInclusive: false);

        Assert.IsTrue(range is LocalDateTimeRange.UnboundedStart(_, false));
    }
}

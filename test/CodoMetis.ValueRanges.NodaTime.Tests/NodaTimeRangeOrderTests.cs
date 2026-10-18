using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// The NodaTime range types carry the same <c>CompareTo</c> as the core ones: PostgreSQL's range
/// order, whose 0 is exactly <c>Equals</c>. <c>RangeOrderParityTests</c> (integration) asks
/// PostgreSQL for every shape pair of every one of them.
/// </summary>
[TestClass]
public class NodaTimeRangeOrderTests
{
    [TestMethod]
    public void LocalDateRange_SortsEmptyFirst_ThenByLowerThenUpperBound()
    {
        var jan = new LocalDate(2026, 1, 1);
        var feb = new LocalDate(2026, 2, 1);

        LocalDateRange[] expected =
        [
            LocalDateRange.Empty,
            LocalDateRange.CreateUnboundedStart(feb),
            LocalDateRange.Infinite,
            LocalDateRange.CreateFinite(jan, jan),
            LocalDateRange.CreateFinite(jan, feb),
            LocalDateRange.CreateUnboundedEnd(jan)
        ];

        CollectionAssert.AreEqual(expected, Enumerable.Reverse(expected).Order().ToArray());
    }

    [TestMethod]
    public void LocalDateTimeRange_BreaksATieOnInclusivity()
    {
        var a = new LocalDateTime(2026, 1, 1, 9, 0);
        var b = new LocalDateTime(2026, 1, 1, 17, 0);

        Assert.IsLessThan(0, LocalDateTimeRange.CreateFinite(a, b, true, false).CompareTo(LocalDateTimeRange.CreateFinite(a, b, true, true)));
        Assert.IsLessThan(0, LocalDateTimeRange.CreateFinite(a, b, true, true).CompareTo(LocalDateTimeRange.CreateFinite(a, b, false, true)));
    }

    [TestMethod]
    public void InstantRange_AndItsMultirange_CompareToZeroExactlyWhenEqual()
    {
        var start = Instant.FromUtc(2026, 1, 1, 9, 0);
        var end   = Instant.FromUtc(2026, 1, 1, 17, 0);

        InstantRange[] ranges =
        [
            InstantRange.Empty, InstantRange.Infinite, InstantRange.CreateFinite(start, end),
            InstantRange.CreateFinite(start, end, false, true), InstantRange.CreateUnboundedEnd(start),
            InstantRange.CreateUnboundedStart(end)
        ];

        foreach (var x in ranges)
        foreach (var y in ranges)
        {
            Assert.AreEqual(x.Equals(y), x.CompareTo(y) == 0, $"{x} vs {y}");
            Assert.AreEqual(Math.Sign(x.CompareTo(y)), -Math.Sign(y.CompareTo(x)), $"{x} vs {y}");

            var (setX, setY) = (RangeSet<InstantRange, Instant>.From([x]), RangeSet<InstantRange, Instant>.From([y]));
            Assert.AreEqual(Math.Sign(x.CompareTo(y)), Math.Sign(setX.CompareTo(setY)), $"{{{x}}} vs {{{y}}}");
        }
    }

    [TestMethod]
    public void YearMonthRange_SortsByMonth()
    {
        var jan = new YearMonth(2026, 1);
        var jun = new YearMonth(2026, 6);

        Assert.IsLessThan(0, YearMonthRange.CreateFinite(jan, jan).CompareTo(YearMonthRange.CreateFinite(jan, jun)));
        Assert.IsGreaterThan(0, YearMonthRange.CreateUnboundedEnd(jan).CompareTo(YearMonthRange.CreateFinite(jan, jun)));
        Assert.IsGreaterThan(0, YearMonthRange.Empty.CompareTo(null));
    }
}

using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

[TestClass]
public sealed class ProviderConversionTests
{
    private static TRange RoundTrip<TRange, T>(TRange range)
        where TRange : class, Core.IRangeFactory<TRange, T>, Core.IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => RangeProviderConversion.FromProvider<TRange, T>(RangeProviderConversion.ToProvider(range, null));

    // -------------------------------------------------------------------------
    // Shape round-trips
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RoundTrip_Empty() =>
        Assert.AreEqual(DateRange.Empty, RoundTrip<DateRange, DateOnly>(DateRange.Empty));

    [TestMethod]
    public void RoundTrip_Infinity() =>
        Assert.AreEqual(DateRange.Infinite, RoundTrip<DateRange, DateOnly>(DateRange.Infinite));

    [TestMethod]
    public void RoundTrip_Finite_Discrete()
    {
        var range = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31));
        Assert.AreEqual(range, RoundTrip<DateRange, DateOnly>(range));
    }

    [TestMethod]
    public void RoundTrip_UnboundedStart()
    {
        var range = DateRange.CreateUnboundedStart(new DateOnly(2024, 3, 31), endInclusive: true);
        Assert.AreEqual(range, RoundTrip<DateRange, DateOnly>(range));
    }

    [TestMethod]
    public void RoundTrip_UnboundedEnd()
    {
        var range = DateRange.CreateUnboundedEnd(new DateOnly(2024, 1, 1));
        Assert.AreEqual(range, RoundTrip<DateRange, DateOnly>(range));
    }

    [TestMethod]
    public void RoundTrip_Continuous_HalfOpen()
    {
        var range = DecimalRange.CreateFinite(1.5m, 9.75m);
        Assert.AreEqual(range, RoundTrip<DecimalRange, decimal>(range));
    }

    [TestMethod]
    public void RoundTrip_Continuous_ExclusiveStart_InclusiveEnd()
    {
        var range = DecimalRange.CreateFinite(1.5m, 9.75m, startInclusive: false, endInclusive: true);
        Assert.AreEqual(range, RoundTrip<DecimalRange, decimal>(range));
    }

    [TestMethod]
    public void RoundTrip_Int32_Finite()
    {
        var range = Int32Range.CreateFinite(1, 10);
        Assert.AreEqual(range, RoundTrip<Int32Range, int>(range));
    }

    [TestMethod]
    public void RoundTrip_Int64_Finite()
    {
        var range = Int64Range.CreateFinite(1L, 10_000_000_000L);
        Assert.AreEqual(range, RoundTrip<Int64Range, long>(range));
    }

    // -------------------------------------------------------------------------
    // Provider shape details
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ToProvider_Empty_MapsToNpgsqlEmpty()
    {
        var provider = RangeProviderConversion.ToProvider<DateOnly>(DateRange.Empty, null);
        Assert.IsTrue(provider.IsEmpty);
    }

    [TestMethod]
    public void ToProvider_Infinity_MapsToDoublyInfinite()
    {
        var provider = RangeProviderConversion.ToProvider<DateOnly>(DateRange.Infinite, null);
        Assert.IsTrue(provider.LowerBoundInfinite);
        Assert.IsTrue(provider.UpperBoundInfinite);
    }

    [TestMethod]
    public void ToProvider_DiscreteFinite_IsFullyClosed()
    {
        var provider = RangeProviderConversion.ToProvider<DateOnly>(
            DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31)), null);
        Assert.IsTrue(provider.LowerBoundIsInclusive);
        Assert.IsTrue(provider.UpperBoundIsInclusive);
    }

    [TestMethod]
    public void FromProvider_HalfOpenDiscrete_Canonicalizes()
    {
        // PostgreSQL returns discrete ranges canonicalized to [lower, upper); the model
        // type re-canonicalizes to its closed form.
        var provider = new NpgsqlRange<DateOnly>(
            new DateOnly(2024, 1, 1), true, false, new DateOnly(2024, 4, 1), false, false);

        var expected = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31));
        Assert.AreEqual(expected, RangeProviderConversion.FromProvider<DateRange, DateOnly>(provider));
    }

    // -------------------------------------------------------------------------
    // Element normalization
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ToProvider_NormalizesDateTimeKind()
    {
        var range = DateTimeRange.CreateFinite(
            new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local));

        var provider = RangeProviderConversion.ToProvider<DateTime>(
            range, value => DateTime.SpecifyKind(value, DateTimeKind.Unspecified));

        Assert.AreEqual(DateTimeKind.Unspecified, provider.LowerBound.Kind);
        Assert.AreEqual(DateTimeKind.Unspecified, provider.UpperBound.Kind);
    }

    [TestMethod]
    public void ToProvider_NormalizesDateTimeOffsetToUtc()
    {
        var range = DateTimeOffsetRange.CreateFinite(
            new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.FromHours(2)));

        var provider = RangeProviderConversion.ToProvider<DateTimeOffset>(range, value => value.ToUniversalTime());

        Assert.AreEqual(TimeSpan.Zero, provider.LowerBound.Offset);
        Assert.AreEqual(new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero), provider.LowerBound);
    }

    // -------------------------------------------------------------------------
    // RangeSet (multirange) round-trips
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RoundTrip_RangeSet()
    {
        var set = RangeSet<DateRange, DateOnly>.From(
        [
            DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)),
            DateRange.CreateFinite(new DateOnly(2024, 3, 1), new DateOnly(2024, 3, 31))
        ]);

        var provider = RangeProviderConversion.ToProvider(set, null);
        Assert.HasCount(2, provider);

        Assert.AreEqual(set, RangeProviderConversion.SetFromProvider<DateRange, DateOnly>(provider));
    }

    [TestMethod]
    public void RoundTrip_EmptyRangeSet()
    {
        var provider = RangeProviderConversion.ToProvider(RangeSet<Int32Range, int>.Empty, null);
        Assert.IsEmpty(provider);
        Assert.AreEqual(RangeSet<Int32Range, int>.Empty, RangeProviderConversion.SetFromProvider<Int32Range, int>(provider));
    }

    [TestMethod]
    public void SetFromProvider_Normalizes()
    {
        // Adjacent and overlapping provider elements are merged by RangeSet.From on the way in.
        NpgsqlRange<int>[] provider =
        [
            new(1, true, false, 5, true, false),
            new(6, true, false, 10, true, false)
        ];

        var set = RangeProviderConversion.SetFromProvider<Int32Range, int>(provider);
        Assert.HasCount(1, set);
        Assert.AreEqual(Int32Range.CreateFinite(1, 10), set[0]);
    }
}

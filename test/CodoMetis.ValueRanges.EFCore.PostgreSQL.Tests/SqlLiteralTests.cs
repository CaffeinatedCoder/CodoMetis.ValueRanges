using System.Globalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

[TestClass]
public sealed class SqlLiteralTests
{
    private static string LiteralOf<TValue>(TValue value) where TValue : notnull
    {
        using var context = new TestDbContext();
        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(TValue))!;
        return mapping.GenerateSqlLiteral(value);
    }

    [TestMethod]
    public void FiniteDateRange() =>
        Assert.AreEqual(
            "'[2024-01-01,2024-03-31]'::daterange",
            LiteralOf(DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31))));

    [TestMethod]
    public void EmptyRange() =>
        Assert.AreEqual("'empty'::daterange", LiteralOf(DateRange.Empty));

    [TestMethod]
    public void InfiniteRange() =>
        Assert.AreEqual("'(,)'::int4range", LiteralOf(Int32Range.Infinite));

    [TestMethod]
    public void HalfOpenDecimalRange() =>
        Assert.AreEqual("'[1.5,9.75)'::numrange", LiteralOf(DecimalRange.CreateFinite(1.5m, 9.75m)));

    [TestMethod]
    public void UnboundedEndRange() =>
        Assert.AreEqual(
            "'[2024-01-01,)'::daterange",
            LiteralOf(DateRange.CreateUnboundedEnd(new DateOnly(2024, 1, 1))));

    [TestMethod]
    public void RangeSet_Multirange() =>
        Assert.AreEqual(
            "'{[1,5],[8,10]}'::int4multirange",
            LiteralOf(RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(8, 10), Int32Range.CreateFinite(1, 5)])));

    [TestMethod]
    public void EmptyRangeSet() =>
        Assert.AreEqual("'{}'::datemultirange", LiteralOf(RangeSet<DateRange, DateOnly>.Empty));

    // -- Validated wrapper arities: what the element-to-primitive bridge actually produces --
    //
    // The literal is the only place the bridge's output is observable without a database, and
    // the sub-second digits are the whole point: a bridge that asked its elements for their
    // default text form instead of the round-trip one would render 10:30:00.0000000 here and
    // store a value silently truncated to the second. The seventh digit is 0 since 9.0.0: the
    // literal carries the value at the store's microsecond, truncated exactly as Npgsql truncates
    // the parameter, so a constant and a parameter for the same value agree.

    [TestMethod]
    public void WrapperTimestampSet_KeepsSubSecondPrecision() =>
        Assert.AreEqual(
            "ARRAY['2024-06-15T10:30:00.1234560']::timestamp without time zone[]",
            LiteralOf(DateTimeSet<TestStamp>.From(
                TestStamp.Parse("2024-06-15T10:30:00.1234567", CultureInfo.InvariantCulture))));

    /// <summary>
    /// A UTC-kinded element normalizes to wall-clock time, exactly as the closed
    /// <see cref="DateTimeSet"/> does: PostgreSQL <c>timestamp</c> carries no time zone, and
    /// Npgsql rejects a UTC-kinded value for it.
    /// </summary>
    [TestMethod]
    public void WrapperTimestampSet_NormalizesKindToUnspecified() =>
        Assert.AreEqual(
            "ARRAY['2024-06-15T10:30:00.1234560']::timestamp without time zone[]",
            LiteralOf(DateTimeSet<TestStamp>.From(
                TestStamp.Parse("2024-06-15T10:30:00.1234567Z", CultureInfo.InvariantCulture))));

    /// <summary>
    /// A local element keeps its wall clock, even one this machine's zone skips: the bridge drops
    /// the offset its <c>"O"</c> text carries instead of converting it back through the zone, which
    /// would move a skipped wall clock by the length of the gap. Inconclusive in a zone without one.
    /// </summary>
    [TestMethod]
    public void WrapperTimestampSet_LocalElement_KeepsAWallClockTheZoneSkips()
    {
        var skipped = Enumerable.Range(0, 366 * 24)
            .Select(hour => new DateTime(2026, 1, 1, 0, 30, 0, DateTimeKind.Local).AddHours(hour))
            .FirstOrDefault(TimeZoneInfo.Local.IsInvalidTime);
        if (skipped == default) Assert.Inconclusive($"{TimeZoneInfo.Local.Id} skips no wall clock in 2026.");

        Assert.AreEqual(
            $"ARRAY['{skipped:yyyy-MM-dd'T'HH:mm:ss}.0000000']::timestamp without time zone[]",
            LiteralOf(DateTimeSet<TestStamp>.From(TestStamp.From(skipped))));
    }

    [TestMethod]
    public void WrapperDecimalSet_KeepsScale() =>
        Assert.AreEqual(
            "ARRAY['12.50','13']::numeric[]",
            LiteralOf(DecimalSet<TestRate>.From(
                TestRate.Parse("12.50", CultureInfo.InvariantCulture),
                TestRate.Parse("13", CultureInfo.InvariantCulture))));

    /// <summary>
    /// The bridge reads an element's text with the family's grammar. An element formatting in the
    /// current culture writes <c>1,5</c> under <c>de-DE</c>; <c>NumberStyles.Number</c> read that
    /// as fifteen and bound it, silently. Refused instead, with the same message as any other
    /// element that does not format as its primitive.
    /// </summary>
    [TestMethod]
    public void WrapperDecimalSet_ElementLeakingTheCulture_IsRejected()
    {
        var leaky    = DecimalSet<TestCultureLeakingRate>.From(new TestCultureLeakingRate(1.5m));
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var error = Assert.ThrowsExactly<InvalidOperationException>(() => LiteralOf(leaky));
            StringAssert.Contains(error.Message, nameof(TestCultureLeakingRate));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// The other half of the temporal contract: an element that swallows the format argument
    /// answers with a form the bridge refuses, rather than one it would quietly truncate. The
    /// message has to name the type and the contract, because the consumer's next question is
    /// which of their wrappers is wrong.
    /// </summary>
    [TestMethod]
    public void WrapperTimestampSet_ElementIgnoringTheFormat_IsRejected()
    {
        var lossy = DateTimeSet<TestLossyStamp>.From(
            new TestLossyStamp(new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified)));

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => LiteralOf(lossy));

        StringAssert.Contains(error.Message, nameof(TestLossyStamp));
        StringAssert.Contains(error.Message, "must format to exactly the backing primitive's text form");
    }
}

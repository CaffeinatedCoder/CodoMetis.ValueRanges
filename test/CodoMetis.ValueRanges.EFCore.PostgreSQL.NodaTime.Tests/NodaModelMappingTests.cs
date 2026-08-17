using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

[TestClass]
public sealed class NodaModelMappingTests
{
    private static string ColumnTypeOf(string propertyName)
    {
        using var context = new NodaTestDbContext();
        var property = context.Model.FindEntityType(typeof(Reservation))!.FindProperty(propertyName)!;
        return property.GetColumnType();
    }

    [TestMethod]
    public void LocalDateRange_MapsTo_DateRange() => Assert.AreEqual("daterange", ColumnTypeOf(nameof(Reservation.Period)));

    [TestMethod]
    public void LocalDateTimeRange_MapsTo_TsRange() => Assert.AreEqual("tsrange", ColumnTypeOf(nameof(Reservation.WallClockSlot)));

    [TestMethod]
    public void InstantRange_MapsTo_TstzRange() => Assert.AreEqual("tstzrange", ColumnTypeOf(nameof(Reservation.Window)));

    [TestMethod]
    public void LocalDateRangeSet_MapsTo_DateMultirange() =>
        Assert.AreEqual("datemultirange", ColumnTypeOf(nameof(Reservation.BlockedDays)));

    [TestMethod]
    public void InstantRangeSet_MapsTo_TstzMultirange() =>
        Assert.AreEqual("tstzmultirange", ColumnTypeOf(nameof(Reservation.Windows)));

    [TestMethod]
    public void YearMonthRange_MapsTo_DateRange() =>
        Assert.AreEqual("daterange", ColumnTypeOf(nameof(Reservation.BillingPeriod)));

    [TestMethod]
    public void YearMonthRangeSet_MapsTo_DateMultirange() =>
        Assert.AreEqual("datemultirange", ColumnTypeOf(nameof(Reservation.BillingPeriods)));

    [TestMethod]
    public void NodaTimeScalars_MapViaNpgsqlNodaTimePlugin()
    {
        Assert.AreEqual("date", ColumnTypeOf(nameof(Reservation.Day)));
        Assert.AreEqual("timestamp with time zone", ColumnTypeOf(nameof(Reservation.At)));
    }

    // -- Validated wrapper arities --
    //
    // The satellite registers these as open generic families via SetTypeRegistry.RegisterFamily,
    // a different seam from the closed definitions above. A family missing from that call
    // resolves to no mapping at all, which is not a compile error — these assertions are what
    // turns it into a test failure rather than a consumer's broken model.

    [TestMethod]
    public void WrapperLocalDateSet_MapsTo_DateArray() =>
        Assert.AreEqual("date[]", ColumnTypeOf(nameof(Reservation.WrappedHolidays)));

    [TestMethod]
    public void WrapperLocalDateTimeSet_MapsTo_TimestampArray() =>
        Assert.AreEqual("timestamp without time zone[]", ColumnTypeOf(nameof(Reservation.WrappedMarks)));

    [TestMethod]
    public void WrapperInstantSet_MapsTo_TimestamptzArray() =>
        Assert.AreEqual("timestamp with time zone[]", ColumnTypeOf(nameof(Reservation.WrappedOccurrences)));

    [TestMethod]
    public void WrapperLocalTimeSet_MapsTo_TimeArray() =>
        Assert.AreEqual("time without time zone[]", ColumnTypeOf(nameof(Reservation.WrappedSlots)));

    /// <summary>
    /// <see cref="YearMonth"/> has no PostgreSQL representation, so the arity stores a
    /// month-aligned <c>date[]</c> — the same storage shape as the closed
    /// <see cref="YearMonthSet"/>, reached through a different definition.
    /// </summary>
    [TestMethod]
    public void WrapperYearMonthSet_MapsTo_DateArray() =>
        Assert.AreEqual("date[]", ColumnTypeOf(nameof(Reservation.WrappedMonths)));

    [TestMethod]
    public void EveryNodaTimeWrapperArity_MapsToTheSameColumnTypeAsItsClosedSibling()
    {
        (string Closed, string Wrapper)[] pairs =
        [
            (nameof(Reservation.Holidays),       nameof(Reservation.WrappedHolidays)),
            (nameof(Reservation.WallClockMarks), nameof(Reservation.WrappedMarks)),
            (nameof(Reservation.Occurrences),    nameof(Reservation.WrappedOccurrences)),
            (nameof(Reservation.Slots),          nameof(Reservation.WrappedSlots)),
            (nameof(Reservation.BillingMonths),  nameof(Reservation.WrappedMonths))
        ];

        foreach (var (closed, wrapper) in pairs)
        {
            Assert.AreEqual(
                ColumnTypeOf(closed), ColumnTypeOf(wrapper),
                $"{wrapper} does not map to the same column type as {closed}.");
        }
    }

    [TestMethod]
    public void BclRange_CoexistsInSameModel() =>
        Assert.AreEqual("daterange", ColumnTypeOf(nameof(Reservation.LegacyPeriod)));

    [TestMethod]
    public void ClrTypeLookups_ResolveBothFamilies()
    {
        // Both range families share PostgreSQL store type names, so CLR-type resolution is
        // the authoritative path — each type gets its own mapping onto the same column type.
        // (Store-name-only lookups like FindMapping("daterange") are claimed by the Npgsql
        // NodaTime plugin itself once UseNodaTime is active, and resolve to DateInterval.)
        using var context = new NodaTestDbContext();
        var mappingSource = context.GetService<IRelationalTypeMappingSource>();

        Assert.AreEqual("daterange", mappingSource.FindMapping(typeof(DateRange))?.StoreType);
        Assert.AreEqual("daterange", mappingSource.FindMapping(typeof(LocalDateRange))?.StoreType);
        Assert.AreEqual(
            "tstzmultirange",
            mappingSource.FindMapping(typeof(RangeSet<InstantRange, Instant>))?.StoreType);
    }
}

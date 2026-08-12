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

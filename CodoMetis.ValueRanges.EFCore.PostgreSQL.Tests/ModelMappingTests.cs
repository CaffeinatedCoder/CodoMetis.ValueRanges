using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

[TestClass]
public sealed class ModelMappingTests
{
    private static string ColumnTypeOf(string propertyName)
    {
        using var context = new TestDbContext();
        var property = context.Model.FindEntityType(typeof(Booking))!.FindProperty(propertyName)!;
        return property.GetColumnType();
    }

    [TestMethod]
    public void Int32Range_MapsTo_Int4Range() => Assert.AreEqual("int4range", ColumnTypeOf(nameof(Booking.Seats)));

    [TestMethod]
    public void Int64Range_MapsTo_Int8Range() => Assert.AreEqual("int8range", ColumnTypeOf(nameof(Booking.Tickets)));

    [TestMethod]
    public void DecimalRange_MapsTo_NumRange() => Assert.AreEqual("numrange", ColumnTypeOf(nameof(Booking.Price)));

    [TestMethod]
    public void DateRange_MapsTo_DateRange() => Assert.AreEqual("daterange", ColumnTypeOf(nameof(Booking.Period)));

    [TestMethod]
    public void DateTimeRange_MapsTo_TsRange() => Assert.AreEqual("tsrange", ColumnTypeOf(nameof(Booking.LocalTime)));

    [TestMethod]
    public void DateTimeOffsetRange_MapsTo_TstzRange() =>
        Assert.AreEqual("tstzrange", ColumnTypeOf(nameof(Booking.InstantTime)));

    [TestMethod]
    public void DateRangeSet_MapsTo_DateMultirange() =>
        Assert.AreEqual("datemultirange", ColumnTypeOf(nameof(Booking.BlockedDays)));

    [TestMethod]
    public void Int32RangeSet_MapsTo_Int4Multirange() =>
        Assert.AreEqual("int4multirange", ColumnTypeOf(nameof(Booking.SeatBlocks)));

    [TestMethod]
    public void StoreTypeName_ResolvesMapping()
    {
        using var context = new TestDbContext();
        var mappingSource = context.GetService<IRelationalTypeMappingSource>();

        Assert.AreEqual(typeof(DateRange), mappingSource.FindMapping("daterange")?.ClrType);
        Assert.AreEqual(typeof(RangeSet<DateRange, DateOnly>), mappingSource.FindMapping("datemultirange")?.ClrType);
        Assert.AreEqual(typeof(Int64Range), mappingSource.FindMapping("int8range")?.ClrType);
    }
}

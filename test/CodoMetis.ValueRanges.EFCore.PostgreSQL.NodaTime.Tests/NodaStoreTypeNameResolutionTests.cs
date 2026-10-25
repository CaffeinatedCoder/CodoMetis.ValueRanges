using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// With <c>UseValueRangesNodaTime</c>, which turns on Npgsql's NodaTime plugin, that plugin takes the
/// <c>daterange</c>, <c>tsrange</c> and <c>tstzrange</c> names first, and the other names, the multiranges
/// included, still resolve to the core types (<c>docs/efcore.md</c>, #56). A NodaTime range property is
/// mapped by its CLR type, so this concerns the name alone.
/// </summary>
[TestClass]
public sealed class NodaStoreTypeNameResolutionTests
{
    public static IEnumerable<object[]> Names =>
    [
        ["daterange", typeof(DateInterval)], ["tsrange", typeof(NpgsqlRange<LocalDateTime>)], ["tstzrange", typeof(Interval)],
        ["int4range", typeof(Int32Range)], ["timerange", typeof(TimeRange)],
        ["datemultirange", typeof(RangeSet<DateRange, DateOnly>)], ["tsmultirange", typeof(RangeSet<DateTimeRange, DateTime>)],
        ["tstzmultirange", typeof(RangeSet<DateTimeOffsetRange, DateTimeOffset>)]
    ];

    [TestMethod]
    [DynamicData(nameof(Names))]
    public void StoreTypeName_WithNodaTime_ResolvesAsDocumented(string storeType, Type expected)
    {
        using var context = new NodaTestDbContext();

        Assert.AreEqual(expected, context.GetService<IRelationalTypeMappingSource>().FindMapping(storeType)?.ClrType, storeType);
    }

    [TestMethod]
    public void NodaTimeRangeProperty_MapsByItsClrType()
    {
        var source = new NodaTestDbContext().GetService<IRelationalTypeMappingSource>();

        Assert.AreEqual("daterange", source.FindMapping(typeof(LocalDateRange))?.StoreType);
        Assert.AreEqual("tsrange", source.FindMapping(typeof(LocalDateTimeRange))?.StoreType);
        Assert.AreEqual("tstzrange", source.FindMapping(typeof(InstantRange))?.StoreType);
    }
}

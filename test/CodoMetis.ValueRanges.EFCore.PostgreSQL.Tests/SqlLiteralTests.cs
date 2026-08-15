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
}

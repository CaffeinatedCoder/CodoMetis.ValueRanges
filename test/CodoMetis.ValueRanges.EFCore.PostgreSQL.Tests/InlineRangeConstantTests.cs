using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// A range built inline in a query from values that depend on nothing in it. EF Core evaluates such a
/// call on the client and inlines the result as a constant typed by its value — the sealed variant,
/// <c>Int32Range.Finite</c> — for which no type mapping exists; until 9.0.0 <c>==</c>, <c>!=</c>,
/// <c>Equals</c> and a conditional over one failed with "does not have a type mapping assigned". The
/// interceptor now retypes the constant to its range type. <c>InlineRangeConstantExecutionTests</c>
/// (integration) runs one per range type against a live server.
/// </summary>
[TestClass]
public sealed class InlineRangeConstantTests
{
    private static string Where(Func<TestDbContext, IQueryable<Booking>> query)
    {
        using var db = new TestDbContext();
        var sql = query(db).ToQueryString();
        return sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..].Trim();
    }

    [TestMethod]
    public void Equality_WithAFactoryCall()
        => Assert.AreEqual(
            "WHERE b.\"Seats\" = '[1,5]'::int4range",
            Where(db => db.Bookings.Where(b => b.Seats == Int32Range.CreateFinite(1, 5))));

    [TestMethod]
    public void Inequality_AndEquals()
    {
        Assert.AreEqual(
            "WHERE b.\"Seats\" <> '[1,5]'::int4range",
            Where(db => db.Bookings.Where(b => b.Seats != Int32Range.CreateFinite(1, 5))));
        Assert.AreEqual(
            "WHERE b.\"Seats\" = '[1,5]'::int4range",
            Where(db => db.Bookings.Where(b => b.Seats.Equals(Int32Range.CreateFinite(1, 5)))));
    }

    [TestMethod]
    public void EveryShape_AndParse()
    {
        Assert.AreEqual(
            "WHERE b.\"Seats\" = '[3,)'::int4range",
            Where(db => db.Bookings.Where(b => b.Seats == Int32Range.CreateUnboundedEnd(3))));
        Assert.AreEqual(
            "WHERE b.\"Seats\" = '(,3]'::int4range",
            Where(db => db.Bookings.Where(b => b.Seats == Int32Range.CreateUnboundedStart(3, endInclusive: true))));
        Assert.AreEqual(
            "WHERE b.\"Seats\" = '[1,5]'::int4range",
            Where(db => db.Bookings.Where(b => b.Seats == Int32Range.Parse("[1,5]", null))));
    }

    [TestMethod]
    public void ADateRange_FromLiteralDates()
        => Assert.AreEqual(
            "WHERE b.\"Period\" = '[2026-01-01,2026-01-31]'::daterange",
            Where(db => db.Bookings.Where(b => b.Period == DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)))));

    /// <summary>The inlined constant goes through the store text, like any other temporal constant.</summary>
    [TestMethod]
    public void ATemporalRange_IsWrittenAtTheStoresResolution()
        => Assert.AreEqual(
            "WHERE b.\"LocalTime\" = '[2026-03-15T10:00:00.0000000,2026-03-15T11:00:00.0000000)'::tsrange",
            Where(db => db.Bookings.Where(b => b.LocalTime == DateTimeRange.CreateFinite(
                new DateTime(2026, 3, 15, 10, 0, 0).AddTicks(5), new DateTime(2026, 3, 15, 11, 0, 0)))));

    [TestMethod]
    public void InsideAConditional()
        => StringAssert.Contains(
            Where(db => db.Bookings.Where(b => (b.Seats == Int32Range.CreateFinite(1, 5) ? 1 : 0) == 1)),
            "b.\"Seats\" = '[1,5]'::int4range");
}

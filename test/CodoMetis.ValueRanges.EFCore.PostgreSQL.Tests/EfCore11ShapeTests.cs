using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// Two shapes EF Core 11 gives a query before the plugin sees it, held here without a database so a
/// revert of either fix fails on <c>ToQueryString</c> alone. <c>EfCore11RetargetTests</c> (integration)
/// runs the same against a live server.
/// </summary>
[TestClass]
public sealed class EfCore11ShapeTests
{
    private static readonly DateOnly Day = new(2024, 6, 15);

    private static string Sql(Func<TestDbContext, IQueryable<Booking>> query)
    {
        using var db = new TestDbContext();
        return query(db).ToQueryString();
    }

    /// <summary>
    /// EF Core 11 rewrites every <c>Contains</c> on an <see cref="IReadOnlyCollection{T}"/> into
    /// <c>Queryable.Contains</c> over the element type, which for a <see cref="RangeSet{TRange,T}"/> is
    /// the range; the operand of <c>Contains(T)</c> and <c>Contains(RangeSet)</c> is not one, and the
    /// rewrite threw <see cref="ArgumentException"/> inside EF Core (#287). The interceptor hands the
    /// three overloads over as static calls, which translate to <c>@&gt;</c> as before.
    /// </summary>
    [TestMethod]
    public void RangeSetContains_EveryOverload_TranslatesToContainment_NotEfCoresCollectionContains()
    {
        var other = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(Day, Day.AddDays(2))]);

        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.BlockedDays.Contains(Day))),      "b.\"BlockedDays\" @> DATE '2024-06-15'");
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.BlockedDays.Contains(b.Period))), "b.\"BlockedDays\" @> b.\"Period\"");
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.BlockedDays.Contains(other))),    "b.\"BlockedDays\" @> @other");
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => other.Contains(b.Period))),         "@other @> b.\"Period\"");
    }

    /// <summary>
    /// EF Core 11 writes <c>a == b ? null : a</c> as <c>NULLIF(a, b)</c>, which compares the two. Over a server-side
    /// text <c>Union</c> the guard compares by mutual containment instead, as it does <c>==</c> (#485): the
    /// guard's <c>CASE</c> arm doesn't see a <c>NULLIF</c>, so it has an arm of its own (#103).
    /// </summary>
    [TestMethod]
    public void NullIf_OverAServerSideUnion_ComparesByMutualContainment()
    {
        var operand = StringSet.From("a", "b");
        var target  = StringSet.From("a", "b", "c");

        using var db = new TestDbContext();
        StringAssert.Contains(db.Bookings.Select(b => b.Id == 5 ? (int?)null : b.Id).ToQueryString(), "NULLIF(");

        var sql = db.Bookings.Select(b => b.Tags.Union(operand) == target ? null : b.Tags.Union(operand)).ToQueryString();

        Assert.DoesNotContain("NULLIF(", sql);
        StringAssert.Contains(sql, "WHEN array_cat(b.\"Tags\", @operand) @> @target AND @target @> array_cat(b.\"Tags\", @operand) THEN NULL");
    }
}

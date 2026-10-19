using CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// <c>RangeSetContainsShapeTests</c> for the NodaTime range sets: the interceptor's rewrite keys off
/// <c>RangeSet&lt;,&gt;</c> itself, so the satellite's sets — the month-aligned
/// <c>RangeSet&lt;YearMonthRange, YearMonth&gt;</c> included — must leave no instance <c>Contains</c>
/// for the EF Core 10 patch that carries dotnet/efcore#37081 to rewrite (#288).
/// </summary>
[TestClass]
public sealed class NodaRangeSetContainsShapeTests
{
    private static readonly LocalDate Day   = new(2024, 6, 15);
    private static readonly Instant   At    = Instant.FromUtc(2024, 6, 15, 12, 0);
    private static readonly YearMonth Month = new(2024, 6);

    [TestMethod]
    public void LocalDateSet_Contains_Value()
        => AssertStaticForm(db => db.Reservations.Where(r => r.BlockedDays.Contains(Day)), "Value", "r.\"BlockedDays\" @> DATE '2024-06-15'");

    [TestMethod]
    public void LocalDateSet_Contains_RangeColumn()
        => AssertStaticForm(db => db.Reservations.Where(r => r.BlockedDays.Contains(r.Period)), "Range", "r.\"BlockedDays\" @> r.\"Period\"");

    [TestMethod]
    public void LocalDateSet_Contains_CapturedSet()
    {
        var other = RangeSet<LocalDateRange, LocalDate>.From([LocalDateRange.CreateFinite(Day, Day.PlusDays(2))]);

        AssertStaticForm(db => db.Reservations.Where(r => r.BlockedDays.Contains(other)), "Set", "r.\"BlockedDays\" @> @other");
    }

    [TestMethod]
    public void InstantSet_Contains_Value()
        => AssertStaticForm(db => db.Reservations.Where(r => r.Windows.Contains(At)), "Value", "r.\"Windows\" @> TIMESTAMPTZ '2024-06-15T12:00:00Z'");

    [TestMethod]
    public void InstantSet_Contains_RangeColumn()
        => AssertStaticForm(db => db.Reservations.Where(r => r.Windows.Contains(r.Window)), "Range", "r.\"Windows\" @> r.\"Window\"");

    [TestMethod]
    public void YearMonthSet_Contains_Value()
        => AssertStaticForm(db => db.Reservations.Where(r => r.BillingPeriods.Contains(Month)), "Value", "r.\"BillingPeriods\" @> DATE '2024-06-01'");

    [TestMethod]
    public void YearMonthSet_Contains_RangeColumn()
        => AssertStaticForm(db => db.Reservations.Where(r => r.BillingPeriods.Contains(r.BillingPeriod)), "Range", "r.\"BillingPeriods\" @> r.\"BillingPeriod\"");

    [TestMethod]
    public void YearMonthSet_Contains_CapturedSet()
    {
        var other = RangeSet<YearMonthRange, YearMonth>.From([YearMonthRange.CreateFinite(Month, Month.PlusMonths(2))]);

        AssertStaticForm(db => db.Reservations.Where(r => r.BillingPeriods.Contains(other)), "Set", "r.\"BillingPeriods\" @> @other");
    }

    private static void AssertStaticForm(Func<NodaTestDbContext, IQueryable<Reservation>> query, string overload, string sql)
    {
        using var db = new NodaTestDbContext();
        var queryable = query(db);

        StringAssert.Contains(queryable.ToQueryString(), sql);

        var tree     = InterceptedQuery.Of(db, queryable);
        var leftover = InterceptedQuery.CollectionContainsCalls(tree);
        Assert.IsEmpty(
            leftover,
            $"An instance Contains on a collection type survived interception, which EF Core 10.0.15 rewrites "
          + $"into Queryable.Contains over the range type: {InterceptedQuery.Describe(leftover)}");

        var statics = InterceptedQuery.StaticRangeSetContainsCalls(tree);
        Assert.HasCount(1, statics, $"Expected one RangeSetContains call, found: {InterceptedQuery.Describe(statics)}");
        Assert.AreEqual(overload, statics[0].Method.Name);
    }
}

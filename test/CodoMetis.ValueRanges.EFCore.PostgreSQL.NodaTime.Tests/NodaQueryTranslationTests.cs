using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// Verifies LINQ-to-SQL translation of the NodaTime range types end to end through the EF
/// query pipeline using <see cref="EntityFrameworkQueryableExtensions.ToQueryString"/> —
/// no database required. The operation matrix itself is covered by the base package's
/// translation tests; these tests prove the NodaTime definitions participate in every
/// translation path: operators, functions, factories, aggregates, and multiranges.
/// </summary>
[TestClass]
public sealed class NodaQueryTranslationTests
{
    private static readonly LocalDate Day = new(2024, 6, 15);

    private static readonly LocalDateRange Range =
        LocalDateRange.CreateFinite(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 31));

    private static readonly InstantRange Window =
        InstantRange.CreateFinite(Instant.FromUtc(2024, 6, 1, 0, 0), Instant.FromUtc(2024, 7, 1, 0, 0));

    private const string RangeLiteral  = "'[2024-01-01,2024-12-31]'::daterange";
    private const string WindowLiteral = "'[2024-06-01T00:00:00Z,2024-07-01T00:00:00Z)'::tstzrange";

    private static string Sql(Func<NodaTestDbContext, IQueryable<object?>> query)
    {
        using var context = new NodaTestDbContext();
        return query(context).ToQueryString();
    }

    // -------------------------------------------------------------------------
    // Query operations
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Contains_Value()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Period.Contains(Day)));
        StringAssert.Contains(sql, "r.\"Period\" @> DATE '2024-06-15'");
    }

    [TestMethod]
    public void Contains_ColumnValue()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Period.Contains(r.Day)));
        StringAssert.Contains(sql, "r.\"Period\" @> r.\"Day\"");
    }

    [TestMethod]
    public void Contains_Range()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Period.Contains(Range)));
        StringAssert.Contains(sql, $"r.\"Period\" @> {RangeLiteral}");
    }

    [TestMethod]
    public void Overlaps_InstantRange()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Window.Overlaps(Window)));
        StringAssert.Contains(sql, $"r.\"Window\" && {WindowLiteral}");
    }

    [TestMethod]
    public void Contains_Instant_Value()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Window.Contains(r.At)));
        StringAssert.Contains(sql, "r.\"Window\" @> r.\"At\"");
    }

    [TestMethod]
    public void IsStrictlyLeftOf()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Period.IsStrictlyLeftOf(Range)));
        StringAssert.Contains(sql, $"r.\"Period\" << {RangeLiteral}");
    }

    [TestMethod]
    public void IsAdjacentTo()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.Period.IsAdjacentTo(Range)));
        StringAssert.Contains(sql, $"r.\"Period\" -|- {RangeLiteral}");
    }

    // -------------------------------------------------------------------------
    // State checks and bound accessors
    // -------------------------------------------------------------------------

    [TestMethod]
    public void IsEmpty_And_IsUnboundedStart()
    {
        var sql = Sql(db => db.Reservations.Where(r => !r.Window.IsEmpty() && r.Window.IsUnboundedStart()));
        StringAssert.Contains(sql, "isempty(r.\"Window\")");
        StringAssert.Contains(sql, "lower_inf(r.\"Window\")");
    }

    [TestMethod]
    public void LowerBound_OrderBy()
    {
        var sql = Sql(db => db.Reservations.OrderBy(r => r.Period.LowerBound()).Select(r => (object?)r.Id));
        StringAssert.Contains(sql, "ORDER BY lower(r.\"Period\")");
    }

    [TestMethod]
    public void UpperBound_Discrete_CompensatesCanonicalForm()
    {
        // LocalDateRange is discrete: the model's inclusive upper is upper(x) - 1.
        var sql = Sql(db => db.Reservations.Select(r => (object?)r.Period.UpperBound()));
        StringAssert.Contains(sql, "upper(r.\"Period\") - 1");
    }

    [TestMethod]
    public void UpperBound_Continuous_IsBareUpper()
    {
        var sql = Sql(db => db.Reservations.Select(r => (object?)r.Window.UpperBound()));
        StringAssert.Contains(sql, "upper(r.\"Window\")");
        Assert.IsFalse(sql.Contains("upper(r.\"Window\") - 1"));
    }

    // -------------------------------------------------------------------------
    // Set operations
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Intersect()
    {
        var sql = Sql(db => db.Reservations.Select(r => (object?)r.Period.Intersect(Range)));
        StringAssert.Contains(sql, $"r.\"Period\" * {RangeLiteral}");
    }

    [TestMethod]
    public void Union_LiftsToMultirange()
    {
        var sql = Sql(db => db.Reservations.Select(r => (object?)r.Period.Union(Range)));
        StringAssert.Contains(sql, "datemultirange(r.\"Period\") + datemultirange(");
    }

    [TestMethod]
    public void Merge_TranslatesToRangeMerge()
    {
        var sql = Sql(db => db.Reservations.Select(r => (object?)r.Window.Merge(Window)));
        StringAssert.Contains(sql, $"range_merge(r.\"Window\", {WindowLiteral})");
    }

    // -------------------------------------------------------------------------
    // Factories
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CreateFinite_TranslatesToGuardedConstructor()
    {
        var sql = Sql(db => db.Reservations.Where(r => LocalDateRange.CreateFinite(r.Day, r.OtherDay).Contains(Day)));
        StringAssert.Contains(sql, "daterange(r.\"Day\", r.\"OtherDay\", '[]')");
        StringAssert.Contains(sql, "WHEN r.\"Day\" <= r.\"OtherDay\"");
    }

    [TestMethod]
    public void CreateUnboundedEnd_TranslatesToConstructor()
    {
        var sql = Sql(db => db.Reservations.Where(r => LocalDateRange.CreateUnboundedEnd(r.Day).Contains(Day)));
        StringAssert.Contains(sql, "daterange(r.\"Day\", NULL, '[)')");
    }

    // -------------------------------------------------------------------------
    // Aggregates — satellite overloads recognized via RegisterAggregateExtensions
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeAgg_TranslatesToRangeAgg()
    {
        var sql = Sql(db => db.Reservations
                             .GroupBy(r => r.CustomerId)
                             .Select(g => (object?)g.Select(r => r.Period).RangeAgg()));
        StringAssert.Contains(sql, "range_agg(r.\"Period\")");
    }

    [TestMethod]
    public void RangeIntersectAgg_TranslatesToRangeIntersectAgg()
    {
        var sql = Sql(db => db.Reservations
                             .GroupBy(r => r.CustomerId)
                             .Select(g => (object?)g.Select(r => r.Window).RangeIntersectAgg()));
        StringAssert.Contains(sql, "range_intersect_agg(r.\"Window\")");
    }

    // -------------------------------------------------------------------------
    // RangeSet (multirange) operations
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeSet_Contains_Value()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.BlockedDays.Contains(Day)));
        StringAssert.Contains(sql, "r.\"BlockedDays\" @> DATE '2024-06-15'");
    }

    [TestMethod]
    public void RangeSet_UnionOperator_ViaInterceptorRewrite()
    {
        var sql = Sql(db => db.Reservations.Select(r => (object?)(r.BlockedDays | Range)));
        StringAssert.Contains(sql, "r.\"BlockedDays\" + datemultirange(");
    }

    [TestMethod]
    public void RangeSet_Complement()
    {
        var sql = Sql(db => db.Reservations.Select(r => (object?)r.Windows.Complement()));
        StringAssert.Contains(sql, "- r.\"Windows\"");
        StringAssert.Contains(sql, "'{(,)}'::tstzmultirange");
    }

    [TestMethod]
    public void RangeSet_Equality_TranslatesToEquals()
    {
        var set = RangeSet<LocalDateRange, LocalDate>.From([Range]);
        var sql = Sql(db => db.Reservations.Where(r => r.BlockedDays == set));
        StringAssert.Contains(sql, "r.\"BlockedDays\" = @");
    }

    // -------------------------------------------------------------------------
    // Coexistence with the BCL types
    // -------------------------------------------------------------------------

    private static readonly DateRange BclRange =
        DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

    [TestMethod]
    public void BclAndNodaTimeRanges_TranslateInOneQuery()
    {
        var sql = Sql(db => db.Reservations.Where(r =>
            r.Period.Contains(Day) && r.LegacyPeriod.Overlaps(BclRange)));

        StringAssert.Contains(sql, "r.\"Period\" @> DATE '2024-06-15'");
        StringAssert.Contains(sql, "r.\"LegacyPeriod\" && '[2024-01-01,2024-12-31]'::daterange");
    }

    // -------------------------------------------------------------------------
    // YearMonthRange — month granularity over a month-aligned daterange
    // -------------------------------------------------------------------------

    private static readonly YearMonth Month = new(2024, 6);

    private static readonly YearMonthRange BillingYear =
        YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 12));

    [TestMethod]
    public void YearMonthRange_Contains_Month_ConvertsToFirstOfMonthDate()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.BillingPeriod.Contains(Month)));
        StringAssert.Contains(sql, "r.\"BillingPeriod\" @> DATE '2024-06-01'");
    }

    [TestMethod]
    public void YearMonthRange_Overlaps_ConstantRendersDateRangeLiteral()
    {
        // The month bounds expand to the days they cover: 2024-01 through 2024-12
        // is [2024-01-01,2024-12-31], canonicalized by the server.
        var sql = Sql(db => db.Reservations.Where(r => r.BillingPeriod.Overlaps(BillingYear)));
        StringAssert.Contains(sql, "r.\"BillingPeriod\" && '[2024-01-01,2024-12-31]'::daterange");
    }

    [TestMethod]
    public void YearMonthRange_UpperBound_Discrete_CompensatesCanonicalForm()
    {
        // upper() of the month-aligned daterange is the first day of the month after the
        // end month; - 1 lands on the last day of the end month, which reads back as it.
        var sql = Sql(db => db.Reservations.Select(r => (object?)r.BillingPeriod.UpperBound()));
        StringAssert.Contains(sql, "upper(r.\"BillingPeriod\") - 1");
    }

    [TestMethod]
    public void YearMonthRange_ConstantFactory_EvaluatesClientSideToDateRangeLiteral()
    {
        // Fully constant factories evaluate client-side; the resulting range renders in
        // date form — January through June 2024 covers [2024-01-01,2024-06-30].
        var sql = Sql(db => db.Reservations.Where(r =>
            r.BillingPeriod.Overlaps(YearMonthRange.CreateFinite(new YearMonth(2024, 1), new YearMonth(2024, 6), true, true))));
        StringAssert.Contains(sql, "r.\"BillingPeriod\" && '[2024-01-01,2024-06-30]'::daterange");
    }

    [TestMethod]
    public void YearMonthRange_ColumnFactory_DoesNotTranslate()
    {
        // Months are coarser than the date subtype, so building a YearMonthRange from
        // column values in SQL is unsupported — it must fail loudly, not shift bounds.
        using var context = new NodaTestDbContext();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            context.Reservations
                   .Where(r => YearMonthRange.CreateUnboundedEnd(r.Day.ToYearMonth(), true).Contains(Month))
                   .ToQueryString());
    }

    [TestMethod]
    public void YearMonthRange_RangeAgg_TranslatesToRangeAggAggregate()
    {
        var sql = Sql(db => db.Reservations
            .GroupBy(r => r.CustomerId)
            .Select(g => g.Select(r => r.BillingPeriod).RangeAgg()));

        StringAssert.Contains(sql, "range_agg(r.\"BillingPeriod\")");
    }

    [TestMethod]
    public void YearMonthRangeSet_Contains_TranslatesOnMultirange()
    {
        var sql = Sql(db => db.Reservations.Where(r => r.BillingPeriods.Contains(Month)));
        StringAssert.Contains(sql, "r.\"BillingPeriods\" @> DATE '2024-06-01'");
    }
}

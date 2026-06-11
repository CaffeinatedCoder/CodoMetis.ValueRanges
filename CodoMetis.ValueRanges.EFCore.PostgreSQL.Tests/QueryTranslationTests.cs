using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// Verifies LINQ-to-SQL translation end to end through the EF query pipeline using
/// <see cref="EntityFrameworkQueryableExtensions.ToQueryString"/>, which generates SQL
/// without connecting to a database. Static range operands are inlined by EF as constants
/// and rendered as range literals; captured locals become parameters.
/// </summary>
[TestClass]
public sealed class QueryTranslationTests
{
    private static readonly DateOnly Day = new(2024, 6, 15);

    private static readonly DateRange Range =
        DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

    private const string RangeLiteral = "'[2024-01-01,2024-12-31]'::daterange";

    private static string Sql(Func<TestDbContext, IQueryable<object?>> query)
    {
        using var context = new TestDbContext();
        return query(context).ToQueryString();
    }

    // -------------------------------------------------------------------------
    // Query operations on ranges
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Contains_Value()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.Contains(Day)));
        StringAssert.Contains(sql, "b.\"Period\" @> DATE '2024-06-15'");
    }

    [TestMethod]
    public void Contains_ParameterValue()
    {
        var day = new DateOnly(2024, 6, 15);
        var sql = Sql(db => db.Bookings.Where(b => b.Period.Contains(day)));
        StringAssert.Contains(sql, "b.\"Period\" @> @");
    }

    [TestMethod]
    public void Contains_ColumnValue()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.Contains(b.Day)));
        StringAssert.Contains(sql, "b.\"Period\" @> b.\"Day\"");
    }

    [TestMethod]
    public void Contains_Range()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.Contains(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" @> {RangeLiteral}");
    }

    [TestMethod]
    public void Contains_ParameterRange()
    {
        var range = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var sql = Sql(db => db.Bookings.Where(b => b.Period.Contains(range)));
        StringAssert.Contains(sql, "b.\"Period\" @> @");
    }

    [TestMethod]
    public void IsContainedBy()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.IsContainedBy(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" <@ {RangeLiteral}");
    }

    [TestMethod]
    public void Overlaps()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.Overlaps(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" && {RangeLiteral}");
    }

    [TestMethod]
    public void IsStrictlyLeftOf()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.IsStrictlyLeftOf(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" << {RangeLiteral}");
    }

    [TestMethod]
    public void IsStrictlyRightOf()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.IsStrictlyRightOf(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" >> {RangeLiteral}");
    }

    [TestMethod]
    public void DoesNotExtendRightOf()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.DoesNotExtendRightOf(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" &< {RangeLiteral}");
    }

    [TestMethod]
    public void DoesNotExtendLeftOf()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.DoesNotExtendLeftOf(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" &> {RangeLiteral}");
    }

    [TestMethod]
    public void IsAdjacentTo()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.IsAdjacentTo(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" -|- {RangeLiteral}");
    }

    [TestMethod]
    public void Works_ForAllRangeTypes()
    {
        var sql = Sql(db => db.Bookings.Where(b =>
            b.Seats.Contains(7)
            && b.Tickets.Contains(7L)
            && b.Price.Contains(9.5m)
            && b.LocalTime.Contains(new DateTime(2024, 6, 15, 12, 0, 0))
            && b.InstantTime.Contains(new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero))));

        StringAssert.Contains(sql, "b.\"Seats\" @> 7");
        StringAssert.Contains(sql, "b.\"Tickets\" @> ");
        StringAssert.Contains(sql, "b.\"Price\" @> 9.5");
        StringAssert.Contains(sql, "b.\"LocalTime\" @> TIMESTAMP");
        StringAssert.Contains(sql, "b.\"InstantTime\" @> TIMESTAMPTZ");
    }

    // -------------------------------------------------------------------------
    // Set operations on ranges
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Intersect_TranslatesToRangeIntersection()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.Period.Intersect(Range)));
        StringAssert.Contains(sql, $"b.\"Period\" * {RangeLiteral}");
    }

    [TestMethod]
    public void Union_LiftsToMultiranges()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.Period.Union(Range)));
        StringAssert.Contains(sql, $"datemultirange(b.\"Period\") + datemultirange({RangeLiteral})");
    }

    [TestMethod]
    public void Except_LiftsToMultiranges()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.Period.Except(Range)));
        StringAssert.Contains(sql, $"datemultirange(b.\"Period\") - datemultirange({RangeLiteral})");
    }

    // -------------------------------------------------------------------------
    // RangeSet (multirange) operations
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeSet_Contains_Value()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.BlockedDays.Contains(Day)));
        StringAssert.Contains(sql, "b.\"BlockedDays\" @> DATE '2024-06-15'");
    }

    [TestMethod]
    public void RangeSet_Contains_RangeColumn()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.BlockedDays.Contains(b.Period)));
        StringAssert.Contains(sql, "b.\"BlockedDays\" @> b.\"Period\"");
    }

    [TestMethod]
    public void RangeSet_Overlaps()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.BlockedDays.Overlaps(b.Period)));
        StringAssert.Contains(sql, "b.\"BlockedDays\" && b.\"Period\"");
    }

    [TestMethod]
    public void RangeSet_Union_Range()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.BlockedDays.Union(b.Period)));
        StringAssert.Contains(sql, "b.\"BlockedDays\" + datemultirange(b.\"Period\")");
    }

    [TestMethod]
    public void RangeSet_Except_Range()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.BlockedDays.Except(b.Period)));
        StringAssert.Contains(sql, "b.\"BlockedDays\" - datemultirange(b.\"Period\")");
    }

    [TestMethod]
    public void RangeSet_Intersect_Set()
    {
        var set = RangeSet<DateRange, DateOnly>.From([Range]);
        var sql = Sql(db => db.Bookings.Select(b => b.BlockedDays.Intersect(set)));
        StringAssert.Contains(sql, "b.\"BlockedDays\" * @");
    }

    [TestMethod]
    public void RangeSet_Complement()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.BlockedDays.Complement()));
        StringAssert.Contains(sql, "'{(,)}'::datemultirange - b.\"BlockedDays\"");
    }

    [TestMethod]
    public void RangeSet_UnionOperator()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.BlockedDays | b.Period));
        StringAssert.Contains(sql, "b.\"BlockedDays\" + datemultirange(b.\"Period\")");
    }

    [TestMethod]
    public void RangeSet_IntersectOperator()
    {
        var set = RangeSet<DateRange, DateOnly>.From([Range]);
        var sql = Sql(db => db.Bookings.Select(b => b.BlockedDays & set));
        StringAssert.Contains(sql, "b.\"BlockedDays\" * @");
    }

    [TestMethod]
    public void RangeSet_ExceptOperator()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.BlockedDays - b.Period));
        StringAssert.Contains(sql, "b.\"BlockedDays\" - datemultirange(b.\"Period\")");
    }

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    [TestMethod]
    public void CreateFinite_TranslatesToGuardedRangeConstructor()
    {
        // The CASE guard preserves the model semantics: inverted bounds yield the empty
        // range, where the bare PostgreSQL constructor would raise an error.
        var sql = Sql(db => db.Bookings.Where(b => DateRange.CreateFinite(b.Day, b.OtherDay, true, true).Contains(Day)));
        StringAssert.Contains(sql, "WHEN b.\"Day\" <= b.\"OtherDay\" THEN daterange(b.\"Day\", b.\"OtherDay\", '[]')");
        StringAssert.Contains(sql, "ELSE 'empty'::daterange");
        StringAssert.Contains(sql, "END @> DATE '2024-06-15'");
    }

    [TestMethod]
    public void CreateFinite_DefaultInclusivity_UsesHalfOpenForContinuous()
    {
        var sql = Sql(db => db.Bookings.Where(b => DecimalRange.CreateFinite(b.Amount, 9m).Overlaps(b.Price)));
        StringAssert.Contains(sql, "THEN numrange(b.\"Amount\", 9.0, '[)')");
        StringAssert.Contains(sql, "ELSE 'empty'::numrange");
    }

    [TestMethod]
    public void CreateFinite_FullyConstant_IsParameterized()
    {
        // A factory call with no column references is client-evaluated into a single
        // range parameter rather than a server-side constructor call.
        var sql = Sql(db => db.Bookings.Where(b => DecimalRange.CreateFinite(1m, 9m).Overlaps(b.Price)));
        StringAssert.Contains(sql, "&& b.\"Price\"");
    }

    [TestMethod]
    public void CreateUnboundedEnd_TranslatesToRangeConstructorWithNullUpperBound()
    {
        var sql = Sql(db => db.Bookings.Where(b => DateRange.CreateUnboundedEnd(b.Day, true).Overlaps(b.Period)));
        StringAssert.Contains(sql, "daterange(b.\"Day\", NULL, '[)') && b.\"Period\"");
    }

    [TestMethod]
    public void CreateUnboundedStart_TranslatesToRangeConstructorWithNullLowerBound()
    {
        var sql = Sql(db => db.Bookings.Where(b => DateRange.CreateUnboundedStart(b.Day, true).Overlaps(b.Period)));
        StringAssert.Contains(sql, "daterange(NULL, b.\"Day\", '(]') && b.\"Period\"");
    }

    // -------------------------------------------------------------------------
    // State checks via equality
    // -------------------------------------------------------------------------

    [TestMethod]
    public void EqualityWithEmpty_Translates()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period == DateRange.Empty));
        StringAssert.Contains(sql, "b.\"Period\" = @");
    }

    [TestMethod]
    public void EqualityWithInfinite_Translates()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period == DateRange.Infinite));
        StringAssert.Contains(sql, "b.\"Period\" = @");
    }
}

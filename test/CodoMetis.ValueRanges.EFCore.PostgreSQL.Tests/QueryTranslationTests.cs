using CodoMetis.ValueRanges.Core;
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
    // Bound accessors
    // -------------------------------------------------------------------------

    [TestMethod]
    public void LowerBound_TranslatesToLower()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.LowerBound() == Day));
        StringAssert.Contains(sql, "lower(b.\"Period\")");
    }

    [TestMethod]
    public void LowerBound_InOrderBy()
    {
        var sql = Sql(db => db.Bookings.OrderBy(b => b.Period.LowerBound()));
        StringAssert.Contains(sql, "ORDER BY lower(b.\"Period\")");
    }

    [TestMethod]
    public void UpperBound_Discrete_CompensatesHalfOpenCanonicalization()
    {
        // PostgreSQL stores daterange half-open; the model is closed — upper(x) - 1 aligns them.
        var sql = Sql(db => db.Bookings.Select(b => (object?)b.Period.UpperBound()));
        StringAssert.Contains(sql, "upper(b.\"Period\") - 1");
    }

    [TestMethod]
    public void UpperBound_Continuous_TranslatesToPlainUpper()
    {
        var sql = Sql(db => db.Bookings.Select(b => (object?)b.Price.UpperBound()));
        StringAssert.Contains(sql, "upper(b.\"Price\")");
        Assert.IsFalse(sql.Contains("upper(b.\"Price\") - 1"));
    }

    [TestMethod]
    public void BoundInclusive_TranslatesToLowerIncUpperInc()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.LowerBoundInclusive() && b.Price.UpperBoundInclusive()));
        StringAssert.Contains(sql, "lower_inc(b.\"Period\")");
        StringAssert.Contains(sql, "upper_inc(b.\"Price\")");
    }

    [TestMethod]
    public void UpperBoundInclusive_Discrete_ExpandsToBoundednessCheck()
    {
        // On the closed discrete model, the upper bound is inclusive iff it exists;
        // PostgreSQL's upper_inc on its half-open form would always be false.
        var sql = Sql(db => db.Bookings.Where(b => b.Period.UpperBoundInclusive()));
        StringAssert.Contains(sql, "NOT (upper_inf(b.\"Period\"))");
        StringAssert.Contains(sql, "NOT (isempty(b.\"Period\"))");
    }

    [TestMethod]
    public void Range_StateChecks_TranslateToRangeFunctions()
    {
        var sql = Sql(db => db.Bookings.Where(b =>
            b.Period.IsEmpty()
            || b.Period.IsUnboundedStart()
            || b.Period.IsUnboundedEnd()));

        StringAssert.Contains(sql, "isempty(b.\"Period\")");
        StringAssert.Contains(sql, "lower_inf(b.\"Period\")");
        StringAssert.Contains(sql, "upper_inf(b.\"Period\")");
    }

    /// <summary>
    /// PostgreSQL has no <c>isinfinity</c>, so the two derived predicates compose the primitives:
    /// unbounded on both sides, and bounded on both sides while non-empty.
    /// </summary>
    [TestMethod]
    public void Range_IsInfinity_ComposesBothUnboundedChecks()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.IsInfinity()));

        StringAssert.Contains(sql, "lower_inf(b.\"Period\")");
        StringAssert.Contains(sql, "upper_inf(b.\"Period\")");
        StringAssert.Contains(sql, "AND");
    }

    [TestMethod]
    public void Range_IsFinite_ComposesNegatedUnboundedAndEmptyChecks()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Period.IsFinite()));

        StringAssert.Contains(sql, "NOT (lower_inf(b.\"Period\"))");
        StringAssert.Contains(sql, "NOT (upper_inf(b.\"Period\"))");
        StringAssert.Contains(sql, "NOT (isempty(b.\"Period\"))");
    }

    [TestMethod]
    public void RangeSet_BoundAccessors_TranslateToMultirangeFunctions()
    {
        var sql = Sql(db => db.Bookings
            .Where(b => b.BlockedDays.LowerBoundInclusive())
            .OrderBy(b => b.BlockedDays.LowerBound())
            .Select(b => (object?)b.BlockedDays.UpperBound()));

        StringAssert.Contains(sql, "lower_inc(b.\"BlockedDays\")");
        StringAssert.Contains(sql, "ORDER BY lower(b.\"BlockedDays\")");
        StringAssert.Contains(sql, "upper(b.\"BlockedDays\")");
    }

    // -------------------------------------------------------------------------
    // Set operations on ranges
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Merge_TranslatesToRangeMergeFunction()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.Period.Merge(Range)));
        StringAssert.Contains(sql, $"range_merge(b.\"Period\", {RangeLiteral})");
    }

    [TestMethod]
    public void RangeSet_Merge_TranslatesToRangeMergeFunction()
    {
        var sql = Sql(db => db.Bookings.Select(b => b.BlockedDays.Merge()));
        StringAssert.Contains(sql, "range_merge(b.\"BlockedDays\")");
    }

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
    // Aggregates
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeAgg_TranslatesToRangeAggAggregate()
    {
        var sql = Sql(db => db.Bookings
            .GroupBy(b => b.Day)
            .Select(g => g.Select(b => b.Period).RangeAgg()));

        StringAssert.Contains(sql, "range_agg(b.\"Period\")");
        StringAssert.Contains(sql, "GROUP BY b.\"Day\"");
    }

    [TestMethod]
    public void RangeIntersectAgg_TranslatesToRangeIntersectAggAggregate()
    {
        var sql = Sql(db => db.Bookings
            .GroupBy(b => b.Day)
            .Select(g => g.Select(b => b.Period).RangeIntersectAgg()));

        StringAssert.Contains(sql, "range_intersect_agg(b.\"Period\")");
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
    public void RangeSet_Contains_Set()
    {
        // A static property operand is funcletized into a parameter, not inlined.
        var sql = Sql(db => db.Bookings.Where(b => b.SeatBlocks.Contains(RangeSet<Int32Range, int>.Empty)));
        StringAssert.Contains(sql, "b.\"SeatBlocks\" @> @");
    }

    [TestMethod]
    public void RangeSet_Overlaps_Set()
    {
        var blocked = RangeSet<DateRange, DateOnly>.From([Range]);
        var sql = Sql(db => db.Bookings.Where(b => b.BlockedDays.Overlaps(blocked)));
        StringAssert.Contains(sql, "b.\"BlockedDays\" && @");
    }

    [TestMethod]
    public void RangeSet_IsAdjacentTo_Range()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.BlockedDays.IsAdjacentTo(b.Period)));
        StringAssert.Contains(sql, "b.\"BlockedDays\" -|- b.\"Period\"");
    }

    [TestMethod]
    public void RangeSet_PositionalComparisons_TranslateToRangeOperators()
    {
        var sql = Sql(db => db.Bookings.Where(b =>
            b.BlockedDays.IsStrictlyLeftOf(b.Period)
            && b.BlockedDays.IsStrictlyRightOf(b.Period)
            && b.BlockedDays.DoesNotExtendRightOf(b.Period)
            && b.BlockedDays.DoesNotExtendLeftOf(b.Period)));

        StringAssert.Contains(sql, "b.\"BlockedDays\" << b.\"Period\"");
        StringAssert.Contains(sql, "b.\"BlockedDays\" >> b.\"Period\"");
        StringAssert.Contains(sql, "b.\"BlockedDays\" &< b.\"Period\"");
        StringAssert.Contains(sql, "b.\"BlockedDays\" &> b.\"Period\"");
    }

    [TestMethod]
    public void RangeSet_StateChecks_TranslateToMultirangeFunctions()
    {
        var sql = Sql(db => db.Bookings.Where(b =>
            b.BlockedDays.IsEmpty()
            || b.BlockedDays.IsUnboundedStart()
            || b.BlockedDays.IsUnboundedEnd()));

        StringAssert.Contains(sql, "isempty(b.\"BlockedDays\")");
        StringAssert.Contains(sql, "lower_inf(b.\"BlockedDays\")");
        StringAssert.Contains(sql, "upper_inf(b.\"BlockedDays\")");
    }

    [TestMethod]
    public void RangeSet_IsFinite_ComposesNegatedUnboundedAndEmptyChecks()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.BlockedDays.IsFinite()));

        StringAssert.Contains(sql, "NOT (lower_inf(b.\"BlockedDays\"))");
        StringAssert.Contains(sql, "NOT (upper_inf(b.\"BlockedDays\"))");
        StringAssert.Contains(sql, "NOT (isempty(b.\"BlockedDays\"))");
    }

    /// <summary>
    /// Deliberately not <c>lower_inf AND upper_inf</c>, which is the translation the single-range
    /// <c>IsInfinity</c> uses: a multirange can satisfy both and still have a gap. PostgreSQL
    /// canonicalizes multiranges the same way the model does, so equality against the infinite
    /// multirange literal is exact.
    /// </summary>
    [TestMethod]
    public void RangeSet_IsInfinity_ComparesAgainstTheInfiniteMultirange()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.BlockedDays.IsInfinity()));

        StringAssert.Contains(sql, "b.\"BlockedDays\" = '{(,)}'::datemultirange");
        Assert.IsFalse(sql.Contains("lower_inf"), $"IsInfinity must not weaken to lower_inf:\n{sql}");
    }

    [TestMethod]
    public void RangeSet_EqualityOperator_TranslatesToSqlEquals()
    {
        var blocked = RangeSet<DateRange, DateOnly>.From([Range]);
        var sql = Sql(db => db.Bookings.Where(b => b.BlockedDays == blocked));
        StringAssert.Contains(sql, "b.\"BlockedDays\" = @");
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
    // Operands statically typed as IRange<T>
    // -------------------------------------------------------------------------

    /// <summary>
    /// The query operations are declared as <c>extension&lt;T&gt;(IRange&lt;T&gt; range)</c> — one
    /// type parameter, receiver typed as the interface — so a receiver whose static type really is
    /// <c>IRange&lt;T&gt;</c> carries no concrete range type for the translator to resolve from.
    /// <c>ValueRangesMethodCallTranslator.TryResolveDefinition</c> falls back to the method's
    /// element type argument for exactly this case, and nothing else exercised it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fallback is not dead code, though its counterpart on the value set side was: those
    /// methods are constrained <c>where TSet : IValueSetFactory&lt;TSet, T&gt;</c>, which forces a
    /// concrete type, and the range set operations (<c>Intersect</c>, <c>Merge</c>, <c>Union</c>,
    /// <c>Except</c>, <c>IsAdjacentTo</c>) are constrained the same way. The query operations are
    /// not, so this shape reaches the translator and resolves through the element type.
    /// </para>
    /// <para>
    /// It is reachable only when no operand carries a concrete range type: a captured local
    /// declared as the interface, with an element-typed argument. Where any operand is a range
    /// column, the CLR-type loop resolves first — which is why every other test here passes
    /// without the fallback. Deleting it makes this query untranslatable.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void InterfaceTypedReceiver_ResolvesThroughTheElementType()
    {
        IRange<DateOnly> declaredAsInterface = Range;

        var sql = Sql(db => db.Bookings.Where(b => declaredAsInterface.Contains(b.Day)));

        StringAssert.Contains(sql, " @> b.\"Day\"");
    }

    /// <summary>
    /// The same receiver against an <see cref="Int32Range"/> element, confirming the fallback
    /// resolves the definition from <c>T</c> rather than defaulting to one registered type.
    /// </summary>
    [TestMethod]
    public void InterfaceTypedReceiver_ResolvesPerElementType()
    {
        IRange<int> seats = Int32Range.CreateFinite(1, 5);

        var sql = Sql(db => db.Bookings.Where(b => seats.Contains(b.Id)));

        StringAssert.Contains(sql, " @> b.\"Id\"");
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

    // -------------------------------------------------------------------------
    // TimeRange — a custom range type (CREATE TYPE timerange AS RANGE),
    // translated through the same paths as the built-ins
    // -------------------------------------------------------------------------

    private static readonly TimeRange Shift =
        TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0));

    private const string ShiftLiteral = "'[09:00:00.0000000,17:00:00.0000000)'::timerange";

    [TestMethod]
    public void TimeRange_Contains_ColumnValue()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.OpeningHours.Contains(b.At)));
        StringAssert.Contains(sql, "b.\"OpeningHours\" @> b.\"At\"");
    }

    [TestMethod]
    public void TimeRange_Overlaps_ConstantRendersTimerangeLiteral()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.OpeningHours.Overlaps(Shift)));
        StringAssert.Contains(sql, $"b.\"OpeningHours\" && {ShiftLiteral}");
    }

    [TestMethod]
    public void TimeRange_UpperBound_Continuous_IsBareUpper()
    {
        var sql = Sql(db => db.Bookings.Select(b => (object?)b.OpeningHours.UpperBound()));
        StringAssert.Contains(sql, "upper(b.\"OpeningHours\")");
        Assert.IsFalse(sql.Contains("upper(b.\"OpeningHours\") - 1"));
    }

    [TestMethod]
    public void TimeRange_CreateFinite_TranslatesToTimerangeConstructor()
    {
        var sql = Sql(db => db.Bookings.Where(b => TimeRange.CreateFinite(b.At, new TimeOnly(17, 0), true, false).Contains(b.At)));
        StringAssert.Contains(sql, "timerange(b.\"At\",");
    }

    [TestMethod]
    public void TimeRange_Union_LiftsToTimeMultirange()
    {
        var sql = Sql(db => db.Bookings.Select(b => (object?)b.OpeningHours.Union(Shift)));
        StringAssert.Contains(sql, "timemultirange(b.\"OpeningHours\") + timemultirange(");
    }

    [TestMethod]
    public void TimeRange_RangeAgg_TranslatesToRangeAggAggregate()
    {
        var sql = Sql(db => db.Bookings
            .GroupBy(b => b.Day)
            .Select(g => g.Select(b => b.OpeningHours).RangeAgg()));

        StringAssert.Contains(sql, "range_agg(b.\"OpeningHours\")");
    }
}

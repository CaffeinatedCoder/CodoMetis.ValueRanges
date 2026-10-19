using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// The form a <c>RangeSet.Contains</c> call has when EF Core's preprocessor receives it, held without
/// a database so that a revert of the interceptor's rewrite fails here on the EF Core 10.0.0 floor.
/// </summary>
/// <remarks>
/// The EF Core 10 patch that carries dotnet/efcore#37081 (10.0.15) rewrites every one-argument
/// <c>Contains</c> on an <see cref="IReadOnlyCollection{T}"/> into <c>Queryable.Contains</c> over the
/// element type, which for a <see cref="RangeSet{TRange,T}"/> is the range; the operand of
/// <c>Contains(T)</c> and <c>Contains(RangeSet)</c> is not one, and the rewrite threw
/// <see cref="ArgumentException"/> inside EF Core (#288). The interceptor hands the three overloads
/// over as static <c>RangeSetContains</c> calls, which translate to <c>@&gt;</c> as before. On the floor
/// the instance call translated as it was, so the SQL assertions pass with the rewrite stripped; the
/// tree assertions are what fails, which is why both are here.
/// </remarks>
[TestClass]
public sealed class RangeSetContainsShapeTests
{
    private static readonly DateOnly Day = new(2024, 6, 15);

    [TestMethod]
    public void Contains_Value_IsHandedOverAsAStaticCall()
        => AssertStaticForm(db => db.Bookings.Where(b => b.BlockedDays.Contains(Day)),
                            nameof(RangeSetContains.Value), "b.\"BlockedDays\" @> DATE '2024-06-15'");

    [TestMethod]
    public void Contains_RangeColumn_IsHandedOverAsAStaticCall()
        => AssertStaticForm(db => db.Bookings.Where(b => b.BlockedDays.Contains(b.Period)),
                            nameof(RangeSetContains.Range), "b.\"BlockedDays\" @> b.\"Period\"");

    [TestMethod]
    public void Contains_CapturedSet_IsHandedOverAsAStaticCall()
    {
        var other = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(Day, Day.AddDays(2))]);

        AssertStaticForm(db => db.Bookings.Where(b => b.BlockedDays.Contains(other)),
                         nameof(RangeSetContains.Set), "b.\"BlockedDays\" @> @other");
    }

    [TestMethod]
    public void Contains_OnACapturedSet_IsHandedOverAsAStaticCall()
    {
        var other = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(Day, Day.AddDays(2))]);

        AssertStaticForm(db => db.Bookings.Where(b => other.Contains(b.Period)),
                         nameof(RangeSetContains.Range), "@other @> b.\"Period\"");
    }

    [TestMethod]
    public void Contains_OnAnIntegerSet_IsHandedOverAsAStaticCall()
        => AssertStaticForm(db => db.Bookings.Where(b => b.SeatBlocks.Contains(5)),
                            nameof(RangeSetContains.Value), "b.\"SeatBlocks\" @> 5");

    /// <summary>
    /// A query EF Core evaluates on the client runs the static form as a delegate, so each must answer
    /// as the instance method it stands for — and <c>Equivalent</c> must pick the overload by the
    /// operand's type, not by position.
    /// </summary>
    [TestMethod]
    public void StaticForms_AnswerAsTheInstanceMethods()
    {
        var set    = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(Day, Day.AddDays(10))]);
        var inside = DateRange.CreateFinite(Day.AddDays(1), Day.AddDays(3));
        var subset = RangeSet<DateRange, DateOnly>.From([inside]);
        var setType = typeof(RangeSet<DateRange, DateOnly>);

        var value = RangeSetContains.Equivalent(setType.GetMethod(nameof(RangeSet<,>.Contains), [typeof(DateOnly)])!)!;
        var range = RangeSetContains.Equivalent(setType.GetMethod(nameof(RangeSet<,>.Contains), [typeof(IRange<DateOnly>)])!)!;
        var other = RangeSetContains.Equivalent(setType.GetMethod(nameof(RangeSet<,>.Contains), [setType])!)!;

        Assert.AreEqual(nameof(RangeSetContains.Value), value.Name);
        Assert.AreEqual(nameof(RangeSetContains.Range), range.Name);
        Assert.AreEqual(nameof(RangeSetContains.Set),   other.Name);

        Assert.AreEqual(set.Contains(Day),            value.Invoke(null, [set, Day]));
        Assert.AreEqual(set.Contains(Day.AddDays(20)), value.Invoke(null, [set, Day.AddDays(20)]));
        Assert.AreEqual(set.Contains(inside),         range.Invoke(null, [set, inside]));
        Assert.AreEqual(set.Contains(subset),         other.Invoke(null, [set, subset]));
        Assert.AreEqual(subset.Contains(set),         other.Invoke(null, [subset, set]));

        Assert.IsNull(RangeSetContains.Equivalent(setType.GetMethod(nameof(RangeSet<,>.Overlaps), [typeof(IRange<DateOnly>)])!));
        Assert.IsNull(RangeSetContains.Equivalent(typeof(List<int>).GetMethod(nameof(List<int>.Contains))!));
    }

    private static void AssertStaticForm(Func<TestDbContext, IQueryable<Booking>> query, string overload, string sql)
    {
        using var db = new TestDbContext();
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
        Assert.AreSame(typeof(RangeSetContains), statics[0].Method.DeclaringType);
    }
}

using System.Linq.Expressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A date range with an exclusive infinite bound, <c>[x,infinity)</c> or <c>(-infinity,x)</c>, is read in a query as
/// memory reads the loaded row (#489): <c>LowerBound()</c>, <c>UpperBound()</c>, both inclusiveness accessors and
/// <c>Clamp</c> at the extremes, on <see cref="DateRange"/>, <see cref="LocalDateRange"/> and their multiranges.
/// </summary>
/// <remarks>
/// <para>
/// The plugin never writes such a bound, but another tool can, and the server's own algebra computes one:
/// <c>(,) - (,-infinity]</c> is <c>(-infinity,)</c>. Memory loads it one day inside the extreme
/// (<c>docs/architecture.md#domain-width-and-the-boundary</c>). Before #489 a query read <c>upper(x) - 1</c>, which is
/// still <c>infinity</c>, and <c>lower(x)</c>, which is <c>-infinity</c>: 9999-12-31 where memory answered 9999-12-30,
/// and 0001-01-01 where memory answered 0001-01-02. <c>lower_inc</c> was <c>false</c> where memory's closed form
/// starts inclusive.
/// </para>
/// <para>
/// Each reader runs as a predicate, <c>reader(r) == memory</c>, which can't fall back to the client as a final
/// projection could. Its projection is shown when the predicate misses.
/// </para>
/// </remarks>
[TestClass]
public sealed class ExclusiveInfinityBoundParityTests
{
    /// <summary>
    /// The infinite forms a <c>daterange</c> holds. The first six have an exclusive infinity; the last two, and the
    /// unbounded sides, agreed before #489 and must still.
    /// </summary>
    private static readonly string[] Forms =
    [
        "daterange('2026-01-01', 'infinity')",
        "daterange('-infinity', '2026-01-01', '()')",
        "daterange('-infinity', 'infinity', '()')",
        "daterange('-infinity', 'infinity')",
        "daterange(NULL, 'infinity')",
        "daterange('-infinity', NULL, '()')",
        "daterange('2026-01-01', 'infinity', '[]')",
        "daterange('-infinity', '2026-01-01', '[]')"
    ];

    /// <summary>The day after <see cref="LocalDate.MinIsoValue"/>, which memory starts <c>(-infinity</c> on.</summary>
    private static readonly LocalDate AfterMinIso = LocalDate.MinIsoValue.PlusDays(1);

    [TestMethod]
    public Task DateRange_ReadsAnExclusiveInfinityAsMemoryLoadsIt() => Run(4900, "Period", form => form,
    [
        ("LowerBound()",          (Expression<Func<Reservation, DateOnly?>>)(r => r.Period.LowerBound())),
        ("UpperBound()",          (Expression<Func<Reservation, DateOnly?>>)(r => r.Period.UpperBound())),
        ("LowerBoundInclusive()", (Expression<Func<Reservation, bool>>)(r => r.Period.LowerBoundInclusive())),
        ("UpperBoundInclusive()", (Expression<Func<Reservation, bool>>)(r => r.Period.UpperBoundInclusive())),
        ("Clamp(MinValue)",       (Expression<Func<Reservation, DateOnly?>>)(r => r.Period.Clamp(DateOnly.MinValue))),
        ("Clamp(MaxValue)",       (Expression<Func<Reservation, DateOnly?>>)(r => r.Period.Clamp(DateOnly.MaxValue))),
        ("Clamp(2026-01-01)",     (Expression<Func<Reservation, DateOnly?>>)(r => r.Period.Clamp(new DateOnly(2026, 1, 1))))
    ]);

    [TestMethod]
    public Task DateRangeSet_ReadsAnExclusiveInfinityAsMemoryLoadsIt() => Run(4910, "BlockedDays", form => $"datemultirange({form})",
    [
        ("LowerBound()",          (Expression<Func<Reservation, DateOnly?>>)(r => r.BlockedDays.LowerBound())),
        ("UpperBound()",          (Expression<Func<Reservation, DateOnly?>>)(r => r.BlockedDays.UpperBound())),
        ("LowerBoundInclusive()", (Expression<Func<Reservation, bool>>)(r => r.BlockedDays.LowerBoundInclusive())),
        ("UpperBoundInclusive()", (Expression<Func<Reservation, bool>>)(r => r.BlockedDays.UpperBoundInclusive()))
    ]);

    /// <summary>
    /// The upper side as <see cref="DateRange"/>'s. On the lower side memory starts <c>(-infinity</c> on -9998-01-02,
    /// which PostgreSQL's dates can't hold, so there a query keeps <see cref="LocalDate.MinIsoValue"/>, as documented
    /// in <c>docs/efcore.md#rows-written-by-other-tools</c>.
    /// </summary>
    [TestMethod]
    public Task LocalDateRange_ReadsAnExclusiveInfinityAsMemoryLoadsIt_ExceptBelowPostgresDates() => Run(4920, "NodaPeriod", form => form,
    [
        ("LowerBound()",          (Expression<Func<Reservation, LocalDate?>>)(r => r.NodaPeriod.LowerBound())),
        ("UpperBound()",          (Expression<Func<Reservation, LocalDate?>>)(r => r.NodaPeriod.UpperBound())),
        ("LowerBoundInclusive()", (Expression<Func<Reservation, bool>>)(r => r.NodaPeriod.LowerBoundInclusive())),
        ("UpperBoundInclusive()", (Expression<Func<Reservation, bool>>)(r => r.NodaPeriod.UpperBoundInclusive())),
        ("Clamp(MinIsoValue)",    (Expression<Func<Reservation, LocalDate?>>)(r => r.NodaPeriod.Clamp(LocalDate.MinIsoValue))),
        ("Clamp(MaxIsoValue)",    (Expression<Func<Reservation, LocalDate?>>)(r => r.NodaPeriod.Clamp(LocalDate.MaxIsoValue))),
        ("Clamp(2026-01-01)",     (Expression<Func<Reservation, LocalDate?>>)(r => r.NodaPeriod.Clamp(new LocalDate(2026, 1, 1))))
    ], KeepsTheIsoMinimum);

    [TestMethod]
    public Task LocalDateRangeSet_ReadsAnExclusiveInfinityAsMemoryLoadsIt_ExceptBelowPostgresDates() => Run(4930, "NodaBlockedDays", form => $"datemultirange({form})",
    [
        ("LowerBound()",          (Expression<Func<Reservation, LocalDate?>>)(r => r.NodaBlockedDays.LowerBound())),
        ("UpperBound()",          (Expression<Func<Reservation, LocalDate?>>)(r => r.NodaBlockedDays.UpperBound())),
        ("LowerBoundInclusive()", (Expression<Func<Reservation, bool>>)(r => r.NodaBlockedDays.LowerBoundInclusive())),
        ("UpperBoundInclusive()", (Expression<Func<Reservation, bool>>)(r => r.NodaBlockedDays.UpperBoundInclusive()))
    ], KeepsTheIsoMinimum);

    /// <summary>
    /// No other tool needed: subtracting a range bounded at an extreme leaves the server an exclusive infinity, where
    /// memory's result stops one day inside. <c>(,) - (,-infinity]</c> is <c>(-infinity,)</c>, and
    /// <c>(,) - [infinity,)</c> is <c>(,infinity)</c>.
    /// </summary>
    [TestMethod]
    public Task DateRange_AnExclusiveInfinityTheServerComputes_IsReadAsMemoryComputesIt() => Run(4940, "Period", form => form,
    [
        ("Except((,MinValue]).LowerBound()",
            (Expression<Func<Reservation, DateOnly?>>)(r => r.Period.Except(DateRange.CreateUnboundedStart(DateOnly.MinValue, true)).LowerBound())),
        ("Except((,MinValue]).LowerBoundInclusive()",
            (Expression<Func<Reservation, bool>>)(r => r.Period.Except(DateRange.CreateUnboundedStart(DateOnly.MinValue, true)).LowerBoundInclusive())),
        ("Except([MaxValue,)).UpperBound()",
            (Expression<Func<Reservation, DateOnly?>>)(r => r.Period.Except(DateRange.CreateUnboundedEnd(DateOnly.MaxValue, true)).UpperBound())),
        ("Except([MaxValue,)).UpperBoundInclusive()",
            (Expression<Func<Reservation, bool>>)(r => r.Period.Except(DateRange.CreateUnboundedEnd(DateOnly.MaxValue, true)).UpperBoundInclusive()))
    ], forms: ["'(,)'::daterange"]);

    /// <summary>
    /// A month range has no day inside an exclusive infinity to answer: memory refuses the row, which covers a partial
    /// month, so <see cref="YearMonthRange"/> keeps <c>lower(x)</c> and <c>upper(x) - 1</c>.
    /// </summary>
    [TestMethod]
    [DataRow(4950, "daterange('2026-01-01', 'infinity')")]
    [DataRow(4951, "daterange('-infinity', '2026-01-01', '()')")]
    public async Task YearMonthRange_AnExclusiveInfinity_DoesNotLoad(int id, string form)
    {
        ContainerLifecycle.RequireDatabase();
        await Seed(id, "BillingPeriod", form);

        await using var context = new IntegrationDbContext();
        var refused = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => context.Reservations.AsNoTracking().SingleAsync(r => r.Id == id));
        StringAssert.Contains(refused.Message, "covers a partial month");
    }

    private static object? KeepsTheIsoMinimum(object? memory) => memory is LocalDate day && day == AfterMinIso ? LocalDate.MinIsoValue : memory;

    /// <summary>
    /// Writes each form into <paramref name="column"/> of a row of its own, loads the row, and runs every reader on
    /// the server as <c>reader(r) == answer</c>, where the answer is memory's over the loaded row, or what
    /// <paramref name="expected"/> makes of it.
    /// </summary>
    private static async Task Run(
        int                                           firstId,
        string                                        column,
        Func<string, string>                          wrap,
        IReadOnlyList<(string Name, LambdaExpression Reader)> readers,
        Func<object?, object?>?                       expected = null,
        IReadOnlyList<string>?                        forms    = null)
    {
        ContainerLifecycle.RequireDatabase();

        var failures = new List<string>();
        var compared = 0;
        foreach (var (form, index) in (forms ?? Forms).Select((form, index) => (form, index)))
        {
            var id = firstId + index;
            await Seed(id, column, wrap(form));

            await using var context = new IntegrationDbContext();
            var loaded = await context.Reservations.AsNoTracking().SingleAsync(r => r.Id == id);

            foreach (var (name, reader) in readers)
            {
                var memory = reader.Compile().DynamicInvoke(loaded);
                var answer = expected is null ? memory : expected(memory);
                var row    = Expression.Parameter(typeof(Reservation), "r");
                var body   = new Rebind(reader.Parameters[0], row).Visit(reader.Body);

                var matches = Expression.Lambda<Func<Reservation, bool>>(Expression.Equal(body, Expression.Constant(answer, reader.ReturnType)), row);
                var hits    = await context.Reservations.Where(r => r.Id == id).Where(matches).CountAsync();
                compared++;
                if (hits == 1) continue;

                var projected = await context.Reservations.Where(r => r.Id == id)
                                             .Select(Expression.Lambda<Func<Reservation, object?>>(Expression.Convert(body, typeof(object)), row))
                                             .SingleAsync();
                failures.Add($"{form}, loaded as {CompositionRunner.Show(ColumnValue(loaded, column))}: {name} is "
                           + $"{CompositionRunner.Show(projected)} in the query, {CompositionRunner.Show(answer)} expected "
                           + $"(memory: {CompositionRunner.Show(memory)})");
            }
        }

        Assert.AreEqual((forms ?? Forms).Count * readers.Count, compared);
        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    /// <summary>A row of its own, whose <paramref name="column"/> another tool set to <paramref name="value"/>.</summary>
    private static async Task Seed(int id, string column, string value)
    {
        await using var seed = new IntegrationDbContext();
        seed.Reservations.Add(new Reservation { Id = id });
        await seed.SaveChangesAsync();

        // The column and the value are this test's own constants, not input.
#pragma warning disable EF1002
        await seed.Database.ExecuteSqlRawAsync($"UPDATE \"Reservations\" SET \"{column}\" = {value} WHERE \"Id\" = {id}");
#pragma warning restore EF1002
    }

    private static object? ColumnValue(Reservation row, string column) => typeof(Reservation).GetProperty(column)!.GetValue(row);

    private sealed class Rebind(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The bound readers of a <c>YearMonthRange</c> and its range set, compared with a month in a filter, against memory
/// over the loaded rows (#506).
/// </summary>
/// <remarks>
/// A projection can't show this: the server's date loads as its month whatever day it is, so a bound read as the
/// month's last day projects as memory's answer. A filter compares the date with the month as a parameter binds it,
/// the first day. <c>ClampDomainParityTests</c> holds <c>Clamp</c> the same way.
/// </remarks>
[TestClass]
public sealed class YearMonthBoundParityTests
{
    private static YearMonth Ym(int year, int month) => new(year, month);

    /// <summary>
    /// <c>LowerBound()</c> and <c>UpperBound()</c> of a range and of its set compared with a month in a filter, where
    /// the bound meets the month as it binds, the first day, rather than as it loads. <c>upper(x) - 1</c> was the last
    /// day of the last month: it loaded as that month and compared unequal to it, and greater (#506). The months
    /// include 9999-12, whose last day Npgsql writes as <c>infinity</c>.
    /// </summary>
    [TestMethod]
    public async Task YearMonthRange_BoundReadersInAFilter_CompareAsInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        YearMonth[] months = [Ym(1, 1), Ym(2026, 1), Ym(2026, 3), Ym(9999, 11), Ym(9999, 12)];
        var ranges = new List<YearMonthRange> { YearMonthRange.Empty, YearMonthRange.Infinite };
        foreach (var start in months)
        {
            ranges.Add(YearMonthRange.CreateUnboundedStart(start));
            ranges.Add(YearMonthRange.CreateUnboundedEnd(start));
            ranges.AddRange(months.Where(end => end >= start).Select(end => YearMonthRange.CreateFinite(start, end)));
        }

        var rows = ranges.Select((range, index) => new Reservation
        {
            Id = 7301 + index, GroupKey = 7300, BillingPeriod = range,
            BillingPeriods = RangeSet<YearMonthRange, YearMonth>.From(range, YearMonthRange.CreateFinite(Ym(2, 1), Ym(2, 2)))
        }).ToArray();
        await using (var seed = new IntegrationDbContext())
        {
            seed.Reservations.AddRange(rows);
            await seed.SaveChangesAsync();
        }

        await using var context = new IntegrationDbContext();
        var loaded = await context.Reservations.AsNoTracking().Where(r => r.GroupKey == 7300).ToListAsync();
        Assert.AreEqual(rows.Length, loaded.Count);

        Expression<Func<Reservation, YearMonth?>>[] readers =
        [
            r => r.BillingPeriod.LowerBound(),
            r => r.BillingPeriod.UpperBound(),
            r => r.BillingPeriods.LowerBound(),
            r => r.BillingPeriods.UpperBound()
        ];

        var failures = new List<string>();
        foreach (var reader in readers)
        foreach (var month in months)
        foreach (var (op, compare) in new (string, Func<Expression, Expression, BinaryExpression>)[]
                 { ("==", Expression.Equal), ("<", Expression.LessThan), (">", Expression.GreaterThan) })
        {
            Expression<Func<YearMonth?>> operand = () => month;
            var predicate = Expression.Lambda<Func<Reservation, bool>>(compare(reader.Body, operand.Body), reader.Parameters);

            var memory = loaded.Where(predicate.Compile()).Select(r => r.Id).Order().ToList();
            var server = await context.Reservations.Where(r => r.GroupKey == 7300).Where(predicate).Select(r => r.Id).Order().ToListAsync();
            if (!memory.SequenceEqual(server))
            {
                var differ = memory.Except(server).Concat(server.Except(memory)).Select(id => loaded.Single(r => r.Id == id).BillingPeriod);
                failures.Add($"{reader.Body} {op} {month}: server and memory differ on {string.Join(", ", differ)}");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }
}

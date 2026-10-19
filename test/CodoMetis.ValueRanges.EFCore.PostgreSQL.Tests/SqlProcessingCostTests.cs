using System.Diagnostics;
using System.Linq.Expressions;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// The plugin's pass over the final SQL tree visits each node once, so it costs time linear in the
/// tree's size on every query, <c>ExecuteUpdate</c> and <c>ExecuteDelete</c>, whether or not the query
/// touches a range or set column.
/// </summary>
/// <remarks>
/// <c>EmptyCollectionRewriter</c> visited the children of every node that was not a <c>SELECT</c>
/// twice, once to look and once to return, so a tree of depth <i>d</i> took 2<sup>d</sup> visits: a
/// filter of 22 OR'd terms took 1.4 s to translate, against 1 ms without the plugin, and 30 terms
/// effectively never compiled (#249). The count below does not depend on timing; the query shows the
/// symptom the way a consumer meets it.
/// </remarks>
[TestClass]
public sealed class SqlProcessingCostTests
{
    [TestMethod]
    public void EmptyCollectionRewriter_VisitsEachNodeOnce()
    {
        using var context = new TestDbContext();
        var factory  = context.GetService<ISqlExpressionFactory>();
        var mappings = context.GetService<IRelationalTypeMappingSource>();

        const int depth = 12;
        var counter = new VisitCounter();
        Expression tree = Expression.Constant(0);
        for (var level = 0; level < depth; level++) tree = new CountingNode(tree, counter);

        EmptyCollectionRewriter.Rewrite(tree, factory, mappings);

        Assert.AreEqual(depth, counter.Visits, $"A chain of {depth} nodes was visited {counter.Visits} times.");
    }

    [TestMethod]
    public async Task AFilterOfFortyOredTerms_Translates_InAboutAMillisecondPerTerm()
    {
        var booking = Expression.Parameter(typeof(Booking), "b");
        var id      = Expression.Property(booking, nameof(Booking.Id));
        var amount  = Expression.Property(booking, nameof(Booking.Amount));

        // b.Amount > (decimal)(b.Id + k) for k = 0 … 39: nothing EF Core can fold.
        var filter = Enumerable.Range(0, 40)
                               .Select(k => (Expression)Expression.GreaterThan(
                                    amount, Expression.Convert(Expression.Add(id, Expression.Constant(k)), typeof(decimal))))
                               .Aggregate(Expression.OrElse);
        var predicate = Expression.Lambda<Func<Booking, bool>>(filter, booking);

        var clock = Stopwatch.StartNew();
        var translation = Task.Run(() =>
        {
            using var context = new TestDbContext();
            return context.Bookings.Where(predicate).Select(b => b.Id).ToQueryString();
        });

        // Generous: the fixed pass takes a few milliseconds. With the double visit, 40 terms would not
        // finish in the lifetime of the test run, so the wait is bounded rather than the test hanging.
        var finished = await Task.WhenAny(translation, Task.Delay(TimeSpan.FromSeconds(20))) == translation;

        Assert.IsTrue(finished, $"Translating 40 OR'd terms had not finished after {clock.Elapsed.TotalSeconds:0} s.");
        StringAssert.Contains(await translation, "+ 39");
    }

    private sealed class VisitCounter
    {
        public int Visits;
    }

    /// <summary>A SQL-tree node that counts how often a visitor walks into it.</summary>
    private sealed class CountingNode(Expression child, VisitCounter counter) : Expression
    {
        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type => child.Type;

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            counter.Visits++;
            var visited = visitor.Visit(child);
            return visited == child ? this : new CountingNode(visited, counter);
        }
    }
}

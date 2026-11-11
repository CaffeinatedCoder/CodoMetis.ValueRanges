using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using static CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition.CompositionMembers;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>RangeAgg</c> and <c>RangeIntersectAgg</c> over groups whose inputs a filter can remove, and the
/// aggregate then consumed: by every discovered member on its result, <c>??</c>, a null test, a group
/// filter (<c>HAVING</c>) and an ordering, against memory over the rows as loaded (#379).
/// </summary>
/// <remarks>
/// <para>
/// <c>RangeAggregateParityTests</c> compares the aggregates per group, projected. Here a group can
/// exist while its filter (<c>g.Where(…)</c>, a <c>FILTER</c> clause) removes every input, hold only
/// NULLs, hold an <c>empty</c> range, or be absent (a key left-joined to no rows); and
/// the result is consumed rather than projected. The groups, by key: 1 two finite ranges; 2 two ranges
/// the filter removes; 3 two NULLs; 4 <c>empty</c> and a finite range; 5 <c>(,)</c> and a range the
/// filter removes; 6 a NULL and a finite range; 7 only <c>empty</c>; 8 no rows.
/// </para>
/// <para>
/// The reference is the in-memory aggregate over the same inputs, which skips a NULL as SQL does
/// (#361), with the documented difference mapped rather than reported: over zero inputs, or only NULLs,
/// SQL answers NULL where the in-memory <c>RangeAgg()</c> answers the empty set (claim E07,
/// <c>docs/efcore.md#read-bounds-merge-ranges-and-aggregate</c>), so the expected server answer is the
/// consumption of NULL there. An ordering by a bound puts the groups without it where PostgreSQL puts
/// NULLs (claim E72).
/// </para>
/// </remarks>
[TestClass]
public sealed class FilteredAggregateProbeTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    private static DateRange Days(int from, int to) => DateRange.CreateFinite(March(from), March(to), true, true);

    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "two finite", [2] = "all filtered away", [3] = "only NULLs", [4] = "empty and finite", [5] = "(,) and filtered",
        [6] = "NULL and finite", [7] = "only empty", [8] = "no rows"
    };

    [TestMethod]
    public async Task EveryConsumption_OfAFilteredAggregate_AnswersAsMemory()
    {
        ContainerLifecycle.RequireServer();

        (int Key, DateRange? Period, int Weight)[] inputs =
        [
            (1, Days(1, 10), 1), (1, Days(5, 15), 1),
            (2, Days(1, 10), 0), (2, Days(20, 25), -1),
            (3, null, 1), (3, null, 1),
            (4, DateRange.Empty, 1), (4, Days(3, 8), 1),
            (5, DateRange.Infinite, 1), (5, Days(5, 8), 0),
            (6, null, 1), (6, Days(3, 8), 1),
            (7, DateRange.Empty, 1)
        ];

        var connectionString = await CompositionDatabase.Create("filtered_aggregate");
        await using (var seed = new AggregateContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Keys.AddRange(Names.Keys.Select(key => new AggregateKey { Id = key }));
            seed.Rows.AddRange(inputs.Select((input, index) => new AggregateRow
            {
                Id = index + 1, GroupKey = input.Key, Weight = input.Weight, Period = input.Period,
                Blocks = input.Period is null ? null : RangeSet<DateRange, DateOnly>.From(input.Period)
            }));
            await seed.SaveChangesAsync();
        }

        await using var context = new AggregateContext(connectionString);
        var loaded = await context.Rows.AsNoTracking().ToListAsync();

        var row     = Expression.Parameter(typeof(AggregateRow), "r");
        var weighed = Expression.Lambda<Func<AggregateRow, bool>>(Expression.GreaterThan(Expression.Property(row, nameof(AggregateRow.Weight)), Expression.Constant(0)), row);

        var failures     = new List<string>();
        var untranslated = new HashSet<string>();
        var judged       = 0;

        foreach (var aggregate in Aggregates())
        foreach (var (filterName, filter) in new (string, Expression<Func<AggregateRow, bool>>?)[] { ("", null), (".Where(r => r.Weight > 0)", weighed) })
        {
            var label = $"g{filterName}.Select(r => r.{aggregate.Column}).{aggregate.Method.Name}()";

            // The aggregate per key in memory, with E07 mapped: zero non-NULL inputs is NULL.
            var kept   = filter?.Compile() ?? (_ => true);
            var memory = Names.Keys.ToDictionary(key => key, key =>
            {
                var group = loaded.Where(r => r.GroupKey == key && kept(r)).Select(aggregate.Read).ToList();
                return group.All(value => value is null) ? null : aggregate.Memory(group);
            });

            foreach (var consumption in Consumptions(aggregate.ResultType))
            {
                var name      = $"{label}{consumption.Name}";
                var reference = NullRule.Reference(consumption.Body, consumption.Parameter);
                var expected  = Names.Keys.ToDictionary(key => key, key => Answer(reference, memory[key]));

                try
                {
                    // The consumption as a group filter, HAVING, both ways: a predicate and its negation, a
                    // value tested against null. First, so a member EF Core would evaluate on the client in
                    // the projection is found untranslated here instead.
                    var predicate = consumption.Body.Type == typeof(bool);
                    var groups    = (await context.Rows.Select(r => r.GroupKey).Distinct().ToListAsync()).Order().ToList();
                    foreach (var (form, negate) in predicate ? new[] { ("HAVING", false), ("HAVING NOT", true) } : [("HAVING (== null)", false), ("HAVING (!= null)", true)])
                    {
                        var having = await context.Rows.GroupBy(r => r.GroupKey).Where(Having(aggregate, filter, consumption, negate)).Select(g => g.Key).ToListAsync();
                        foreach (var key in groups.Where(key => expected[key].Known))
                        {
                            var keeps = (predicate ? expected[key].Value is true : expected[key].Value is null) != negate;
                            if (having.Contains(key) != keeps)
                                failures.Add($"{name} [GroupBy] {form}, group {Names[key]}: server {(having.Contains(key) ? "keeps" : "drops")} it, memory {(keeps ? "keeps" : "drops")} it");
                        }
                    }

                    // GroupBy, then the consumption projected: the groups that exist.
                    var grouped = await context.Rows.GroupBy(r => r.GroupKey)
                                               .Select(Projection<IGrouping<int, AggregateRow>>(g => g.Key, aggregate, filter, consumption))
                                               .ToListAsync();
                    foreach (var item in grouped.OrderBy(p => p.Id).Where(p => !Equals(p.Value, expected[p.Id].Value) && expected[p.Id].Known))
                        failures.Add($"{name} [GroupBy] Select, group {Names[item.Id]}: server {CompositionRunner.Show(item.Value)}, memory {CompositionRunner.Show(expected[item.Id].Value)}");

                    // Every key left-joined to its rows and grouped, which reaches the key without rows too:
                    // its group holds the one NULL row the join makes.
                    var joined = await context.Keys.LeftJoin(context.Rows, k => k.Id, r => r.GroupKey, (k, r) => new KeyedRow { Key = k.Id, Row = r! })
                                              .GroupBy(x => x.Key, x => x.Row)
                                              .Select(Projection<IGrouping<int, AggregateRow>>(g => g.Key, aggregate, filter, consumption))
                                              .ToListAsync();
                    foreach (var item in joined.OrderBy(p => p.Id).Where(p => !Equals(p.Value, expected[p.Id].Value) && expected[p.Id].Known))
                        failures.Add($"{name} [LeftJoin GroupBy] Select, key {Names[item.Id]}: server {CompositionRunner.Show(item.Value)}, memory {CompositionRunner.Show(expected[item.Id].Value)}");

                    if (consumption.Name is ".LowerBound()" or ".UpperBound()")
                        await Order(context, name, aggregate, filter, consumption, expected, grouped.Select(p => p.Id).ToList(), failures);

                    judged++;
                }
                catch (InvalidOperationException e) when (e.Message.Contains("could not be translated", StringComparison.Ordinal))
                {
                    untranslated.Add(name);
                }
                catch (Exception e) when (e is InvalidOperationException or DbException)
                {
                    failures.Add($"{name}: server threw {e.GetType().Name}: {CompositionRunner.FirstLine(e)}");
                }
            }
        }


        // Some 300 consumptions are judged today; the floor catches a discovery that stopped matching.
        Assert.IsGreaterThanOrEqualTo(200, judged, $"Only {judged} consumptions were judged; untranslated: {string.Join("; ", untranslated.Order())}");

        Candidates.Verdict(nameof(FilteredAggregateProbeTests), failures,
                           "A filtered aggregate, consumed: PostgreSQL and memory (with E07 mapped) disagree",
                           method: nameof(EveryConsumption_OfAFilteredAggregate_AnswersAsMemory));
    }

    /// <summary>
    /// <c>OrderBy</c> and <c>OrderByDescending</c> of the groups by the bound, then the key: memory's
    /// order with the groups whose bound is NULL moved where PostgreSQL puts NULLs (E72).
    /// </summary>
    private static async Task Order(AggregateContext context, string name, Aggregate aggregate, Expression<Func<AggregateRow, bool>>? filter,
                                    Consumption consumption, Dictionary<int, (bool Known, object? Value)> expected, List<int> groups, List<string> failures)
    {
        var g   = Expression.Parameter(typeof(IGrouping<int, AggregateRow>), "g");
        var key = Expression.Lambda<Func<IGrouping<int, AggregateRow>, DateOnly?>>(consumption.Splice(aggregate.Over(g, filter)), g);

        foreach (var descending in new[] { false, true })
        {
            var ordered = context.Rows.GroupBy(r => r.GroupKey);
            var server = await (descending ? ordered.OrderByDescending(key) : ordered.OrderBy(key)).ThenBy(x => x.Key).Select(x => x.Key).ToListAsync();

            var present = groups.Where(id => expected[id].Value is not null).OrderBy(id => (DateOnly?)expected[id].Value).ThenBy(id => id).ToList();
            if (descending) present = [.. groups.Where(id => expected[id].Value is not null).OrderByDescending(id => (DateOnly?)expected[id].Value).ThenBy(id => id)];
            var missing = groups.Where(id => expected[id].Value is null).Order().ToList();
            List<int> documented = descending ? [.. missing, .. present] : [.. present, .. missing];

            if (!server.SequenceEqual(documented))
                failures.Add($"{name} [GroupBy] {(descending ? "OrderByDescending" : "OrderBy")}: server [{string.Join(",", server)}], documented [{string.Join(",", documented)}]");
        }
    }

    private static (bool Known, object? Value) Answer(Func<object?, object?> reference, object? aggregate)
    {
        try { return (true, reference(aggregate)); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return (false, null); }
    }

    /// <summary><c>new Projected { Id = key, Value = consume(aggregate) }</c> over a group.</summary>
    private static Expression<Func<TGroup, Projected>> Projection<TGroup>(Expression<Func<TGroup, int>> key, Aggregate aggregate,
                                                                          Expression<Func<AggregateRow, bool>>? filter, Consumption consumption)
    {
        var g = key.Parameters[0];
        return Expression.Lambda<Func<TGroup, Projected>>(Projected(key.Body, consumption.Splice(aggregate.Over(g, filter))), g);
    }

    /// <summary>The consumption as a group filter: a predicate itself, a value tested against null; negated when <paramref name="negate"/>.</summary>
    private static Expression<Func<IGrouping<int, AggregateRow>, bool>> Having(Aggregate aggregate, Expression<Func<AggregateRow, bool>>? filter,
                                                                              Consumption consumption, bool negate)
    {
        var g     = Expression.Parameter(typeof(IGrouping<int, AggregateRow>), "g");
        var value = consumption.Splice(aggregate.Over(g, filter));
        var body  = value.Type == typeof(bool) ? value : CompositionRunner.IsNull(value);
        return Expression.Lambda<Func<IGrouping<int, AggregateRow>, bool>>(negate ? Expression.Not(body) : body, g);
    }

    private static MemberInitExpression Projected(Expression id, Expression value) =>
        Expression.MemberInit(Expression.New(typeof(Projected)),
                              Expression.Bind(typeof(Projected).GetProperty(nameof(Composition.Projected.Id))!, id),
                              Expression.Bind(typeof(Projected).GetProperty(nameof(Composition.Projected.Value))!,
                                              Expression.Convert(Expression.Convert(value, NullRule.Lift(value.Type)), typeof(object))));

    /// <summary>One aggregate: the column it reads, the method, its result type, and the same aggregate in memory.</summary>
    private sealed record Aggregate(string Column, MethodInfo Method, Type ResultType, Func<AggregateRow, object?> Read, Func<List<object?>, object?> Memory)
    {
        /// <summary><c>source[.Where(filter)].Select(r =&gt; r.Column).Aggregate()</c>, over a group or a query.</summary>
        public Expression Over(Expression source, Expression<Func<AggregateRow, bool>>? filter)
        {
            var queryable = typeof(IQueryable<AggregateRow>).IsAssignableFrom(source.Type);
            var linq      = queryable ? typeof(Queryable) : typeof(Enumerable);
            Expression Lambda(LambdaExpression lambda) => queryable ? Expression.Quote(lambda) : lambda;

            if (filter is not null) source = Expression.Call(linq, nameof(Enumerable.Where), [typeof(AggregateRow)], source, Lambda(filter));
            var r      = Expression.Parameter(typeof(AggregateRow), "r");
            var column = Expression.Property(r, Column);
            var select = Expression.Call(linq, nameof(Enumerable.Select), [typeof(AggregateRow), column.Type], source, Lambda(Expression.Lambda(column, r)));
            return Expression.Call(Method, select);
        }
    }

    private static IEnumerable<Aggregate> Aggregates()
    {
        var aggregates = typeof(RangeAggregateExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static);
        MethodInfo Over(string name, Type input) => aggregates.Single(m => m.Name == name && !m.IsGenericMethodDefinition
                                                                        && m.GetParameters()[0].ParameterType == typeof(IEnumerable<>).MakeGenericType(input));
        MethodInfo OverSets(string name) => aggregates.Single(m => m.Name == name && m.IsGenericMethodDefinition).MakeGenericMethod(RangeType, ElementType);

        yield return new(nameof(AggregateRow.Period), Over(nameof(RangeAggregateExtensions.RangeAgg), RangeType), SetType,
                         r => r.Period, values => values.Cast<DateRange>().RangeAgg());
        yield return new(nameof(AggregateRow.Period), Over(nameof(RangeAggregateExtensions.RangeIntersectAgg), RangeType), RangeType,
                         r => r.Period, values => values.Cast<DateRange>().RangeIntersectAgg());
        yield return new(nameof(AggregateRow.Blocks), OverSets(nameof(RangeAggregateExtensions.RangeAgg)), SetType,
                         r => r.Blocks, values => values.Cast<RangeSet<DateRange, DateOnly>>().RangeAgg());
        yield return new(nameof(AggregateRow.Blocks), OverSets(nameof(RangeAggregateExtensions.RangeIntersectAgg)), SetType,
                         r => r.Blocks, values => values.Cast<RangeSet<DateRange, DateOnly>>().RangeIntersectAgg());
    }

    /// <summary>
    /// What a query does with an aggregate's result: the value itself, a null test, <c>?? (,)</c>, and
    /// every discovered member with the result as its receiver and a captured operand.
    /// </summary>
    private sealed record Consumption(string Name, ParameterExpression Parameter, Expression Body)
    {
        public Expression Splice(Expression aggregate) => new Rebind(Parameter, aggregate).Visit(Body)!;

        private sealed class Rebind(ParameterExpression from, Expression to) : ExpressionVisitor
        {
            protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
        }
    }

    private static IEnumerable<Consumption> Consumptions(Type result)
    {
        var a        = Expression.Parameter(result, "a");
        var infinite = result == RangeType ? (Expression)Captured(DateRange.Infinite) : Captured(RangeSet<DateRange, DateOnly>.Infinite);

        yield return new("", a, a);
        yield return new(" == null", a, Expression.Equal(a, Expression.Constant(null, result)));
        yield return new(" ?? @(,)", a, Expression.Coalesce(a, infinite));

        Operand[] Slot(Type type, int position) => position == 0
            ? type.IsAssignableFrom(result) && Kind(type) is "range" or "set" ? [new("", a, true)] : []
            : Kind(type) switch
            {
                "range"   => [new("@[3,8]", Captured(Days(3, 8)), false)],
                "set"     => [new("@{[3,8]}", Captured(RangeSet<DateRange, DateOnly>.From(Days(3, 8))), false)],
                "element" => [new("@2026-03-07", Captured(March(7)), false)],
                _         => []
            };

        foreach (var call in Calls(Slot, operands => operands[0].Value == a && operands.Count <= 2))
            yield return new(call.Name, a, call.Body);
    }

    public sealed class AggregateKey
    {
        public int Id { get; set; }
    }

    /// <summary>A key and one of its rows, or no row when the key has none.</summary>
    public sealed class KeyedRow
    {
        public int Key { get; set; }

        public AggregateRow Row { get; set; } = null!;
    }

    public sealed class AggregateRow
    {
        public int Id { get; set; }

        public int GroupKey { get; set; }

        public int Weight { get; set; }

        public DateRange? Period { get; set; }

        public RangeSet<DateRange, DateOnly>? Blocks { get; set; }
    }

    private sealed class AggregateContext(string connectionString) : DbContext
    {
        public DbSet<AggregateKey> Keys => Set<AggregateKey>();

        public DbSet<AggregateRow> Rows => Set<AggregateRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AggregateKey>().Property(k => k.Id).ValueGeneratedNever();
            modelBuilder.Entity<AggregateRow>().Property(r => r.Id).ValueGeneratedNever();
        }
    }
}

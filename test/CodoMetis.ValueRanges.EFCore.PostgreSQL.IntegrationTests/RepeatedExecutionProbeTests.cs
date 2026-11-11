using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using static CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition.CompositionMembers;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The same query run again and again while its parameter changes, as a captured variable and as the
/// argument of <c>EF.CompileQuery</c> and <c>EF.CompileAsyncQuery</c>, each execution against
/// <see cref="NullRule"/> over the rows as loaded (#378).
/// </summary>
/// <remarks>
/// <para>
/// Every other sweep runs a query once. EF Core caches a query by its expression's shape and builds the
/// SQL again only for a new shape of its parameters: whether each one is NULL, and how many elements a
/// collection has. A translation that reads a parameter's value while it translates (a NULL operand, an
/// empty collection) is then reused for values it was not built for. So each operand is held in one
/// captured variable that walks null → finite → empty → <c>(,)</c> → null and back, and each captured
/// list walks empty → one element → a duplicate → a NULL element → empty and back.
/// </para>
/// <para>
/// The calls are discovered (<see cref="CompositionMembers"/>): every binary member with the nullable
/// column on one side and the captured variable on the other, in a filter, a negated filter and a
/// projection; and <c>Any</c>, <c>All</c>, <c>Count</c> and <c>Contains</c> over the captured list. The
/// compiled forms run every predicate with the value as their argument, filtered and negated.
/// </para>
/// </remarks>
[TestClass]
public sealed class RepeatedExecutionProbeTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    private static DateRange Days(int from, int to) => DateRange.CreateFinite(March(from), March(to), true, true);

    private static RangeSet<DateRange, DateOnly> Blocks(params DateRange[] ranges) => RangeSet<DateRange, DateOnly>.From(ranges);

    private static readonly ParameterExpression Row = Expression.Parameter(typeof(RepeatRow), "r");

    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "null", [2] = "[1,10]", [3] = "empty", [4] = "(,)", [5] = "[20,)", [6] = "(,10]", [7] = "[5,8]"
    };

    // The walk each captured value takes, forward and back: null first, last and in the middle.
    private static (string Name, object? Value)[] Walk(params (string Name, object? Value)[] values) =>
        [.. values, .. values.Reverse().Skip(1)];

    private static readonly Dictionary<string, (string Name, object? Value)[]> Values = new()
    {
        ["range"]  = Walk(("null", null), ("[3,8]", Days(3, 8)), ("empty", DateRange.Empty), ("(,)", DateRange.Infinite), ("null", null)),
        ["set"]    = Walk(("null", null), ("{[3,8]}", Blocks(Days(3, 8))), ("{}", Blocks()), ("{(,)}", Blocks(DateRange.Infinite)), ("null", null)),
        ["values"] = Walk(("null", null), ("{a}", StringSet.From(["a"])), ("{}", StringSet.Empty), ("{a,b}", StringSet.From(["a", "b"])), ("null", null))
    };

    private static readonly Dictionary<string, (string Name, object List)[]> Lists = new()
    {
        ["range"] = Walk(("[]", new List<DateRange>()), ("[[3,8]]", new List<DateRange> { Days(3, 8) }), ("[[3,8],[3,8]]", new List<DateRange> { Days(3, 8), Days(3, 8) }),
                         ("[null,(,)]", new List<DateRange> { null!, DateRange.Infinite }), ("[]", new List<DateRange>()))
                    .Select(v => (v.Name, v.Value!)).ToArray(),
        ["set"] = Walk(("[]", new List<RangeSet<DateRange, DateOnly>>()), ("[{[3,8]}]", new List<RangeSet<DateRange, DateOnly>> { Blocks(Days(3, 8)) }),
                       ("[{[3,8]},{[3,8]}]", new List<RangeSet<DateRange, DateOnly>> { Blocks(Days(3, 8)), Blocks(Days(3, 8)) }),
                       ("[null,{}]", new List<RangeSet<DateRange, DateOnly>> { null!, Blocks() }), ("[]", new List<RangeSet<DateRange, DateOnly>>()))
                  .Select(v => (v.Name, v.Value!)).ToArray(),
        ["values"] = Walk(("[]", new List<StringSet>()), ("[{a}]", new List<StringSet> { StringSet.From(["a"]) }),
                          ("[{a},{a}]", new List<StringSet> { StringSet.From(["a"]), StringSet.From(["a"]) }),
                          ("[null,{}]", new List<StringSet> { null!, StringSet.Empty }), ("[]", new List<StringSet>()))
                     .Select(v => (v.Name, v.Value!)).ToArray()
    };

    private static async Task<(RepeatContext Context, List<RepeatRow> Loaded)> Database()
    {
        var connectionString = await CompositionDatabase.Create("repeated");
        await using (var seed = new RepeatContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Rows.AddRange(
                new RepeatRow { Id = 1 },
                new RepeatRow { Id = 2, Period = Days(1, 10), Blocks = Blocks(Days(1, 10)), Tags = StringSet.From(["a"]) },
                new RepeatRow { Id = 3, Period = DateRange.Empty, Blocks = Blocks(), Tags = StringSet.Empty },
                new RepeatRow { Id = 4, Period = DateRange.Infinite, Blocks = Blocks(DateRange.Infinite), Tags = StringSet.From(["a", "b"]) },
                new RepeatRow { Id = 5, Period = DateRange.CreateUnboundedEnd(March(20), true), Blocks = Blocks(Days(20, 25), DateRange.CreateUnboundedEnd(March(27), true)),
                                Tags = StringSet.From(["b"]) },
                new RepeatRow { Id = 6, Period = DateRange.CreateUnboundedStart(March(10), true), Blocks = Blocks(DateRange.CreateUnboundedStart(March(10), true)),
                                Tags = StringSet.From(["a"]) },
                new RepeatRow { Id = 7, Period = Days(5, 8), Blocks = Blocks(Days(3, 8)), Tags = StringSet.From(["c"]) });
            await seed.SaveChangesAsync();
        }

        var context = new RepeatContext(connectionString);
        return (context, await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync());
    }

    private static Expression Column(string name) => Expression.Property(Row, name);

    private static Operand? ColumnFor(string? kind) => kind switch
    {
        "range"  => new("r.Period", Column(nameof(RepeatRow.Period)), true),
        "set"    => new("r.Blocks", Column(nameof(RepeatRow.Blocks)), true),
        "values" => new("r.Tags", Column(nameof(RepeatRow.Tags)), true),
        _        => null
    };

    /// <summary>The static type a captured operand of each kind is declared as.</summary>
    private static Type Declared(string kind) => kind switch
    {
        "range" => RangeType,
        "set"   => SetType,
        _       => ValueSetType
    };

    /// <summary>
    /// Every binary member with the nullable column on one side and one captured variable on the other,
    /// run once per value of the walk, in a filter, a negated filter and a projection.
    /// </summary>
    [TestMethod]
    public async Task CapturedOperand_WalkedThroughNullFiniteEmptyAndInfinite_AnswersAsRuleCEveryTime()
    {
        ContainerLifecycle.RequireServer();

        var (context, loaded) = await Database();
        await using var _ = context;

        var failures     = new List<string>();
        var untranslated = new HashSet<string>();
        var unanswered   = new HashSet<string>();
        var judged       = 0;

        foreach (var (name, kind, body, holder) in Bodies())
        {
            foreach (var (step, (valueName, value)) in Values[kind].Index())
            {
                holder.Value = value;
                var call = new Call(name, body);
                switch (await CompositionRunner.Judge($"execution {step + 1}, @p = {valueName}", call, Row, context.Rows.AsNoTracking(), loaded,
                                                      r => r.Id, id => Names[id], failures))
                {
                    case Outcome.Compared:     judged++; break;
                    case Outcome.Untranslated: untranslated.Add(name); break;
                    case Outcome.Unanswered:   unanswered.Add(name); break;
                }
            }
        }


        // Some 1000 executions are judged today; the floor catches a discovery that stopped matching.
        Assert.IsGreaterThanOrEqualTo(700, judged, $"Only {judged} executions were judged; untranslated: {string.Join("; ", untranslated.Order())}; unanswered: {string.Join("; ", unanswered.Order())}");

        Candidates.Verdict(nameof(RepeatedExecutionProbeTests), failures,
                           "A query run again with its captured operand changed: PostgreSQL and rule (c) disagree on a later execution",
                           method: nameof(CapturedOperand_WalkedThroughNullFiniteEmptyAndInfinite_AnswersAsRuleCEveryTime));
    }

    /// <summary>
    /// <c>Any</c>, <c>All</c> and <c>Count</c> of every binary member over one captured list, and
    /// <c>Contains</c> of the column, run once per list of the walk.
    /// </summary>
    [TestMethod]
    public async Task CapturedList_WalkedThroughEmptyOneDuplicateAndNull_AnswersAsRuleCEveryTime()
    {
        ContainerLifecycle.RequireServer();

        var (context, loaded) = await Database();
        await using var _ = context;

        var calls = new List<(Call Call, string Kind, IStrongBox Holder)>();
        foreach (var kind in Lists.Keys)
        {
            var listType = typeof(List<>).MakeGenericType(Declared(kind));
            var holder   = (IStrongBox)Activator.CreateInstance(typeof(StrongBox<>).MakeGenericType(listType))!;
            var list     = Expression.Field(Expression.Constant(holder), nameof(StrongBox<>.Value));
            var x        = Expression.Parameter(Declared(kind), "x");

            foreach (var (name, body) in PredicatesOver(kind, x))
            {
                foreach (var quantifier in new[] { nameof(Enumerable.Any), nameof(Enumerable.All) })
                    calls.Add((new Call($"@list.{quantifier}(x => {name})", Expression.Call(typeof(Enumerable), quantifier, [x.Type], list, Expression.Lambda(body, x))), kind, holder));
                calls.Add((new Call($"@list.Count(x => {name}) == 0",
                                    Expression.Equal(Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [x.Type], list, Expression.Lambda(body, x)), Expression.Constant(0))),
                           kind, holder));
            }

            calls.Add((new Call($"@list.Contains({ColumnFor(kind)!.Name})",
                                Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [x.Type], list, ColumnFor(kind)!.Value)), kind, holder));
        }

        var failures     = new List<string>();
        var untranslated = new HashSet<string>();
        var unanswered   = new HashSet<string>();
        var judged       = 0;
        foreach (var (call, kind, holder) in calls)
        {
            foreach (var (step, (listName, list)) in Lists[kind].Index())
            {
                holder.Value = list;
                switch (await CompositionRunner.Judge($"execution {step + 1}, @list = {listName}", call, Row, context.Rows.AsNoTracking(), loaded,
                                                      r => r.Id, id => Names[id], failures))
                {
                    case Outcome.Compared:     judged++; break;
                    case Outcome.Untranslated: untranslated.Add(call.Name); break;
                    case Outcome.Unanswered:   unanswered.Add(call.Name); break;
                }
            }
        }


        Assert.IsGreaterThanOrEqualTo(500, judged, $"Only {judged} executions were judged; untranslated: {string.Join("; ", untranslated.Order())}; unanswered: {string.Join("; ", unanswered.Order())}");

        Candidates.Verdict(nameof(RepeatedExecutionProbeTests), failures,
                           "A query run again with its captured list changed: PostgreSQL and rule (c) disagree on a later execution",
                           method: nameof(CapturedList_WalkedThroughEmptyOneDuplicateAndNull_AnswersAsRuleCEveryTime));
    }

    /// <summary>
    /// Every binary predicate of the first test as an <c>EF.CompileQuery</c> and an
    /// <c>EF.CompileAsyncQuery</c> delegate taking the operand as its argument, filtered and negated, run
    /// once per value of the walk.
    /// </summary>
    [TestMethod]
    public async Task CompiledQuery_CalledWithNullFiniteEmptyAndInfinite_AnswersAsRuleCEveryTime()
    {
        ContainerLifecycle.RequireServer();

        var (context, loaded) = await Database();
        await using var _ = context;

        var failures = new List<string>();
        var compiled = 0;
        var calls    = 0;

        foreach (var (name, kind, body, holder) in Bodies().Where(b => b.Body.Type == typeof(bool)))
        {
            var argument = Expression.Parameter(Declared(kind), "p");
            var over     = new Substitute(holder, argument).Visit(body)!;

            foreach (var (form, filter) in new[] { ("Where", over), ("Where(!)", Expression.Not(over)) })
            {
                var c     = Expression.Parameter(typeof(RepeatContext), "c");
                var query = Expression.Lambda(
                    typeof(Func<,,>).MakeGenericType(typeof(RepeatContext), argument.Type, typeof(IQueryable<int>)),
                    Expression.Call(typeof(Queryable), nameof(Queryable.Select), [typeof(RepeatRow), typeof(int)],
                                    Expression.Call(typeof(Queryable), nameof(Queryable.Where), [typeof(RepeatRow)],
                                                    Expression.Property(c, nameof(RepeatContext.Rows)), Expression.Quote(Expression.Lambda(filter, Row))),
                                    Expression.Quote((Expression<Func<RepeatRow, int>>)(r => r.Id))),
                    c, argument);

                var syncQuery  = Compile(nameof(EF.CompileQuery), argument.Type, query);
                var asyncQuery = Compile(nameof(EF.CompileAsyncQuery), argument.Type, query);
                var reference  = NullRule.Reference<RepeatRow>(form == "Where" ? body : Expression.Not(body), Row);
                var translated = true;

                foreach (var (variant, run) in new (string, Func<object?, Task<List<int>>>)[]
                         {
                             ("EF.CompileQuery", value => Task.FromResult(((IEnumerable<int>)syncQuery.DynamicInvoke(context, value)!).ToList())),
                             ("EF.CompileAsyncQuery", async value =>
                             {
                                 var ids = new List<int>();
                                 await foreach (var id in (IAsyncEnumerable<int>)asyncQuery.DynamicInvoke(context, value)!) ids.Add(id);
                                 return ids;
                             })
                         })
                {
                    foreach (var (step, (valueName, value)) in Values[kind].Index())
                    {
                        if (!translated) break;
                        holder.Value = value;
                        var expected = loaded.Where(r => reference(r) is true).Select(r => r.Id).ToHashSet();
                        try
                        {
                            var server = (await run(value)).ToHashSet();
                            compiled++;
                            foreach (var id in loaded.Select(r => r.Id).Where(id => server.Contains(id) != expected.Contains(id)))
                                failures.Add($"{name} [{variant}, call {step + 1}, p = {valueName}] {form}, row {Names[id]}: server "
                                           + $"{(server.Contains(id) ? "selects" : "drops")} it, rule (c) {(expected.Contains(id) ? "selects" : "drops")} it");
                        }
                        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException { Message: var message }
                                                                 && message.Contains("could not be translated", StringComparison.Ordinal))
                        {
                            translated = false;
                        }
                        catch (Exception e) when (e is InvalidOperationException or DbException or TargetInvocationException)
                        {
                            var inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
                            failures.Add($"{name} [{variant}, call {step + 1}, p = {valueName}] {form}: server threw {inner.GetType().Name}: {CompositionRunner.FirstLine(inner)}");
                        }
                    }
                }

                if (translated) calls++;
            }
        }


        Assert.IsGreaterThanOrEqualTo(1000, compiled, $"Only {compiled} compiled executions ran, over {calls} compiled queries");

        Candidates.Verdict(nameof(RepeatedExecutionProbeTests), failures,
                           "A compiled query called with a changed argument: PostgreSQL and rule (c) disagree",
                           method: nameof(CompiledQuery_CalledWithNullFiniteEmptyAndInfinite_AnswersAsRuleCEveryTime));
    }

    /// <summary>
    /// Every binary member with the nullable column on one side and a captured variable of its own on
    /// the other, the variable's kind, and the variable.
    /// </summary>
    private static List<(string Name, string Kind, Expression Body, IStrongBox Holder)> Bodies()
    {
        var bodies = new List<(string, string, Expression, IStrongBox)>();
        foreach (var method in Methods.Where(m => Slots(m).Count == 2))
        {
            var slots = Slots(method);
            for (var at = 0; at < 2; at++)
            {
                var column = ColumnFor(Kind(slots[1 - at]));
                var kind   = Kind(slots[at]);
                if (column is null || kind is not ("range" or "set" or "values")) continue;

                var holder   = (IStrongBox)Activator.CreateInstance(typeof(StrongBox<>).MakeGenericType(Declared(kind)))!;
                var captured = Expression.Field(Expression.Constant(holder), nameof(StrongBox<>.Value));
                Expression[] operands = at == 0 ? [captured, column.Value] : [column.Value, captured];
                Expression body = method.IsSpecialName
                    ? Expression.MakeBinary(method.Name switch { "op_BitwiseOr" => ExpressionType.Or, "op_BitwiseAnd" => ExpressionType.And, _ => ExpressionType.Subtract },
                                            operands[0], operands[1], false, method)
                    : method.IsStatic ? Expression.Call(method, operands) : Expression.Call(operands[0], method, operands[1]);
                var name = method.IsSpecialName
                    ? at == 0 ? $"@p {method.Name} {column.Name}" : $"{column.Name} {method.Name} @p"
                    : at == 0 ? $"@p.{method.Name}({column.Name})" : $"{column.Name}.{method.Name}(@p)";
                bodies.Add((name, kind, body, holder));
            }
        }

        return bodies;
    }

    /// <summary>Every binary predicate with the nullable column of <paramref name="kind"/>'s family on one side and <paramref name="x"/> on the other.</summary>
    private static IEnumerable<(string Name, Expression Body)> PredicatesOver(string kind, ParameterExpression x)
    {
        foreach (var method in Methods.Where(m => m.ReturnType == typeof(bool) && Slots(m).Count == 2))
        {
            var slots = Slots(method);
            for (var at = 0; at < 2; at++)
            {
                if (Kind(slots[at]) != kind || ColumnFor(Kind(slots[1 - at])) is not { } column) continue;
                Expression[] operands = at == 0 ? [x, column.Value] : [column.Value, x];
                yield return (at == 0 ? $"x.{method.Name}({column.Name})" : $"{column.Name}.{method.Name}(x)",
                              method.IsStatic ? Expression.Call(method, operands) : Expression.Call(operands[0], method, operands[1]));
            }
        }
    }

    private static Delegate Compile(string compiler, Type argument, LambdaExpression query) =>
        (Delegate)typeof(EF).GetMethods()
                            .Single(m => m.Name == compiler && m.GetGenericArguments().Length == 3
                                      && m.GetParameters()[0].ParameterType.GetGenericArguments()[0].GetGenericArguments()[^1].IsGenericType
                                      && m.GetParameters()[0].ParameterType.GetGenericArguments()[0].GetGenericArguments()[^1].GetGenericTypeDefinition() == typeof(IQueryable<>))
                            .MakeGenericMethod(typeof(RepeatContext), argument, typeof(int))
                            .Invoke(null, [query])!;

    /// <summary>
    /// Replaces the read of a captured variable with the compiled query's argument. EF Core evaluates a
    /// compiled query's closures once, when it first runs, so a captured variable left in it would keep
    /// its first value.
    /// </summary>
    private sealed class Substitute(IStrongBox holder, Expression argument) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node) =>
            node.Expression is ConstantExpression { Value: var value } && ReferenceEquals(value, holder) ? argument : base.VisitMember(node);
    }

    public sealed class RepeatRow
    {
        public int Id { get; set; }

        public DateRange? Period { get; set; }

        public RangeSet<DateRange, DateOnly>? Blocks { get; set; }

        public StringSet? Tags { get; set; }
    }

    private sealed class RepeatContext(string connectionString) : DbContext
    {
        public DbSet<RepeatRow> Rows => Set<RepeatRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<RepeatRow>().Property(r => r.Id).ValueGeneratedNever();
    }
}

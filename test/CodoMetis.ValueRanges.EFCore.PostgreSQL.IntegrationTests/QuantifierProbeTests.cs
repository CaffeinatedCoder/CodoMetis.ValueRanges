using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using static CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition.CompositionMembers;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Quantifiers over captured lists, compound Boolean predicates and predicates over a value-producing
/// member, over nullable range, <see cref="RangeSet{TRange,T}"/>, value set and element columns, held
/// against <see cref="NullRule"/> over the rows as loaded and by metamorphic relations inside the
/// database that need no memory answer (#377).
/// </summary>
/// <remarks>
/// <para>
/// <c>CapturedCollectionProbeTests</c> sends a list through <c>Contains</c>, <c>Any</c> and
/// <c>Count</c> over required columns. Here the column can be NULL, the list can hold a NULL, and the
/// quantifier can be <c>All</c>, which EF Core writes as <c>NOT EXISTS (… WHERE NOT …)</c>: a
/// three-valued <c>NOT</c> inside it turns a NULL predicate into a row that <c>All</c> keeps. The
/// relations: <c>All(P)</c> selects what <c>!Any(x =&gt; !P)</c> selects, <c>Count(P) == 0</c> what
/// <c>!Any(P)</c> selects, <c>!(P &amp;&amp; Q)</c> what <c>!P || !Q</c> selects and <c>!(P || Q)</c> what
/// <c>!P &amp;&amp; !Q</c> selects; and for every predicate, <c>Where(P)</c> and <c>Where(!P)</c> partition the
/// rows and <c>Select(P)</c> is <see langword="true"/> exactly on <c>Where(P)</c>'s.
/// </para>
/// </remarks>
[TestClass]
public sealed class QuantifierProbeTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    private static DateRange Days(int from, int to) => DateRange.CreateFinite(March(from), March(to), true, true);

    private static RangeSet<DateRange, DateOnly> Blocks(params DateRange[] ranges) => RangeSet<DateRange, DateOnly>.From(ranges);

    private static readonly ParameterExpression Row = Expression.Parameter(typeof(QuantRow), "r");

    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "null", [2] = "[1,10]", [3] = "empty", [4] = "(,)", [5] = "[20,)", [6] = "(,10]", [7] = "[5,8]"
    };

    private static async Task<(QuantContext Context, List<QuantRow> Loaded)> Database()
    {
        var connectionString = await CompositionDatabase.Create("quantifier");
        await using (var seed = new QuantContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Rows.AddRange(
                new QuantRow { Id = 1 },
                new QuantRow { Id = 2, Period = Days(1, 10), Blocks = Blocks(Days(1, 10)), Tags = StringSet.From(["a"]), Day = March(7), Label = "a" },
                new QuantRow { Id = 3, Period = DateRange.Empty, Blocks = Blocks(), Tags = StringSet.Empty, Day = March(3), Label = "b" },
                new QuantRow { Id = 4, Period = DateRange.Infinite, Blocks = Blocks(DateRange.Infinite), Tags = StringSet.From(["a", "b"]), Day = March(20) },
                new QuantRow { Id = 5, Period = DateRange.CreateUnboundedEnd(March(20), true), Blocks = Blocks(Days(20, 25), DateRange.CreateUnboundedEnd(March(27), true)),
                               Tags = StringSet.From(["b"]), Label = "a" },
                new QuantRow { Id = 6, Period = DateRange.CreateUnboundedStart(March(10), true), Blocks = Blocks(DateRange.CreateUnboundedStart(March(10), true)),
                               Tags = StringSet.From(["a"]), Day = March(31), Label = "c" },
                new QuantRow { Id = 7, Period = Days(5, 8), Blocks = Blocks(Days(3, 8)), Tags = StringSet.From(["c"]), Day = March(7), Label = "a" });
            await seed.SaveChangesAsync();
        }

        var context = new QuantContext(connectionString);
        return (context, await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync());
    }

    private static Expression Column(string name) => Expression.Property(Row, name);

    // The nullable column a parameter of each kind takes.
    private static Operand? ColumnFor(string? kind) => kind switch
    {
        "range"   => new("r.Period", Column(nameof(QuantRow.Period)), true),
        "set"     => new("r.Blocks", Column(nameof(QuantRow.Blocks)), true),
        "values"  => new("r.Tags", Column(nameof(QuantRow.Tags)), true),
        "element" => new("r.Day.Value", Expression.Property(Column(nameof(QuantRow.Day)), nameof(Nullable<>.Value)), true),
        "text"    => new("r.Label", Column(nameof(QuantRow.Label)), true),
        _         => null
    };

    /// <summary>
    /// <c>list.Any(x =&gt; P)</c>, <c>list.All(x =&gt; P)</c> and <c>list.Count(x =&gt; P) == 0</c> for every
    /// binary member with a nullable column on one side and the list's element on the other, over lists
    /// that are empty, hold one value, a duplicate, a NULL, or one of each shape; and
    /// <c>list.Contains(r.Column)</c> over the same lists.
    /// </summary>
    [TestMethod]
    public async Task Quantifiers_OverCapturedLists_AnswerAsRuleCAndAgreeWithEachOther()
    {
        ContainerLifecycle.RequireServer();

        var (context, loaded) = await Database();
        await using var _ = context;

        var elements = new Dictionary<string, ParameterExpression>
        {
            ["range"] = Expression.Parameter(RangeType, "x"), ["set"] = Expression.Parameter(SetType, "x"), ["values"] = Expression.Parameter(ValueSetType, "x"),
            ["element"] = Expression.Parameter(ElementType, "x"), ["text"] = Expression.Parameter(typeof(string), "x")
        };
        var lists = new Dictionary<string, (string Name, object List)[]>
        {
            ["range"] = [("[]", new List<DateRange>()), ("[[3,8]]", new List<DateRange> { Days(3, 8) }), ("[[3,8],[3,8]]", new List<DateRange> { Days(3, 8), Days(3, 8) }),
                         ("[null,[3,8]]", new List<DateRange> { null!, Days(3, 8) }),
                         ("[empty,(,),[3,8],[20,)]", new List<DateRange> { DateRange.Empty, DateRange.Infinite, Days(3, 8), DateRange.CreateUnboundedEnd(March(20), true) })],
            ["set"] = [("[]", new List<RangeSet<DateRange, DateOnly>>()), ("[{[3,8]}]", new List<RangeSet<DateRange, DateOnly>> { Blocks(Days(3, 8)) }),
                       ("[{[3,8]},{[3,8]}]", new List<RangeSet<DateRange, DateOnly>> { Blocks(Days(3, 8)), Blocks(Days(3, 8)) }),
                       ("[null,{[3,8]}]", new List<RangeSet<DateRange, DateOnly>> { null!, Blocks(Days(3, 8)) }),
                       ("[{},{(,)}]", new List<RangeSet<DateRange, DateOnly>> { Blocks(), Blocks(DateRange.Infinite) })],
            ["values"] = [("[]", new List<StringSet>()), ("[{a}]", new List<StringSet> { StringSet.From(["a"]) }),
                          ("[{a},{a}]", new List<StringSet> { StringSet.From(["a"]), StringSet.From(["a"]) }),
                          ("[null,{a}]", new List<StringSet> { null!, StringSet.From(["a"]) }),
                          ("[{},{a,b}]", new List<StringSet> { StringSet.Empty, StringSet.From(["a", "b"]) })],
            ["element"] = [("[]", new List<DateOnly>()), ("[7]", new List<DateOnly> { March(7) }), ("[7,7]", new List<DateOnly> { March(7), March(7) }),
                           ("[3,20]", new List<DateOnly> { March(3), March(20) })],
            ["text"] = [("[]", new List<string>()), ("[a]", new List<string> { "a" }), ("[a,a]", new List<string> { "a", "a" }), ("[null,a]", new List<string> { null!, "a" })]
        };

        // The bodies: every binary member with the column on one side and x on the other.
        var bodies = new List<(string Name, string Kind, Expression Body)>();
        foreach (var method in Methods)
        {
            var slots = Slots(method);
            if (slots.Count != 2 || method.ReturnType != typeof(bool)) continue;
            for (var at = 0; at < 2; at++)
            {
                var columnKind = Kind(slots[1 - at]);
                var elementKind = Kind(slots[at]);
                if (ColumnFor(columnKind) is not { } column || elementKind is null) continue;

                var x = elements[elementKind];
                var operands = at == 0 ? new[] { (Expression)x, column.Value } : [column.Value, x];
                var name = at == 0 ? $"x.{method.Name}({column.Name})" : $"{column.Name}.{method.Name}(x)";
                bodies.Add((name, elementKind, Build(method, operands)));
            }
        }

        var calls = new List<Call>();
        var relations = new List<(string Name, Call Left, Call Right)>();
        foreach (var (name, kind, body) in bodies)
        {
            var x = elements[kind];
            foreach (var (listName, list) in lists[kind])
            {
                var captured = Expression.Field(Expression.Constant(Holder(list)), "Value");
                var any      = Quantify(nameof(Enumerable.Any), captured, x, body);
                var all      = Quantify(nameof(Enumerable.All), captured, x, body);
                var notAny   = Expression.Not(Quantify(nameof(Enumerable.Any), captured, x, Expression.Not(body)));
                var none     = Expression.Equal(Quantify(nameof(Enumerable.Count), captured, x, body), Expression.Constant(0));

                var anyCall  = new Call($"@{listName}.Any(x => {name})", any);
                var allCall  = new Call($"@{listName}.All(x => {name})", all);
                var noneCall = new Call($"@{listName}.Count(x => {name}) == 0", none);
                calls.AddRange([anyCall, allCall, noneCall]);
                relations.Add(("All(P) == !Any(!P)", allCall, new Call($"!@{listName}.Any(x => !{name})", notAny)));
                relations.Add(("Count(P) == 0 == !Any(P)", noneCall, new Call($"!@{listName}.Any(x => {name})", Expression.Not(any))));
            }
        }

        // list.Contains(column): EF Core's own operator, over a nullable column and a list that may hold NULL.
        foreach (var kind in new[] { "range", "set", "values", "text" })
        {
            var column = ColumnFor(kind)!;
            foreach (var (listName, list) in lists[kind])
            {
                var captured = Expression.Field(Expression.Constant(Holder(list)), "Value");
                var contains = typeof(Enumerable).GetMethods().Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
                                                 .MakeGenericMethod(column.Value.Type);
                calls.Add(new Call($"@{listName}.Contains({column.Name})", Expression.Call(contains, captured, column.Value)));
            }
        }

        var failures     = new List<string>();
        var untranslated = new HashSet<string>();
        var unanswered   = new HashSet<string>();
        var judged       = 0;
        foreach (var call in calls)
        {
            switch (await CompositionRunner.Judge("quantifier", call, Row, context.Rows.AsNoTracking(), loaded, r => r.Id, id => Names[id], failures))
            {
                case Outcome.Compared:     judged++; break;
                case Outcome.Untranslated: untranslated.Add(call.Name); break;
                case Outcome.Unanswered:   unanswered.Add(call.Name); break;
            }
        }

        foreach (var (relation, left, right) in relations.Where(r => !untranslated.Contains(r.Left.Name)))
            await Relate(context, relation, left, right, failures);


        Assert.IsGreaterThanOrEqualTo(300, judged, $"Only {judged} quantifiers were judged; untranslated: {string.Join("; ", untranslated.Order())}; unanswered: {string.Join("; ", unanswered.Order())}");

        Candidates.Verdict(nameof(QuantifierProbeTests), failures,
                           "A quantifier over a captured list and a nullable column: PostgreSQL disagrees with rule (c) or with itself",
                           method: nameof(Quantifiers_OverCapturedLists_AnswerAsRuleCAndAgreeWithEachOther));
    }

    /// <summary>
    /// <c>P &amp;&amp; Q</c> and <c>P || Q</c> over every ordered pair of predicates drawn across the nullable
    /// range, multirange, value set and bound, negated, and against De Morgan's forms.
    /// </summary>
    [TestMethod]
    public async Task CompoundPredicates_OverNullableColumns_AnswerAsRuleCAndKeepDeMorgan()
    {
        ContainerLifecycle.RequireServer();

        var (context, loaded) = await Database();
        await using var _ = context;

        var period = Column(nameof(QuantRow.Period));
        var blocks = Column(nameof(QuantRow.Blocks));
        var tags   = Column(nameof(QuantRow.Tags));
        (string Name, Expression Body)[] predicates =
        [
            ("r.Period.Overlaps(@[3,8])", Range1(nameof(RangeExtensions.Overlaps), period, Captured(Days(3, 8)))),
            ("r.Period.IsEmpty()", Expression.Call(typeof(RangeExtensions), nameof(RangeExtensions.IsEmpty), [ElementType], period)),
            ("r.Period.IsStrictlyLeftOf(@[20,25])", Range1(nameof(RangeExtensions.IsStrictlyLeftOf), period, Captured(Days(20, 25)))),
            ("r.Period.LowerBound() == null", Expression.Equal(Expression.Call(typeof(RangeExtensions), nameof(RangeExtensions.LowerBound), [ElementType], period),
                                                               Expression.Constant(null, typeof(DateOnly?)))),
            ("r.Blocks.Contains(@2026-03-07)", Expression.Call(blocks, SetType.GetMethod(nameof(RangeSet<,>.Contains), [ElementType])!, Captured(March(7)))),
            ("r.Tags.Contains(@a)", Expression.Call(Methods.Single(m => m.DeclaringType == typeof(ValueSetExtensions) && m.Name == nameof(ValueSetExtensions.Contains)), tags, Captured("a"))),
            ("r.Tags.IsSubsetOf(@{a,b})", Expression.Call(Methods.Single(m => m.DeclaringType == typeof(ValueSetExtensions) && m.Name == nameof(ValueSetExtensions.IsSubsetOf)),
                                                         tags, Captured(StringSet.From(["a", "b"])))),
            ("r.Tags.Count > 0", Expression.GreaterThan(Expression.Property(tags, nameof(StringSet.Count)), Expression.Constant(0)))
        ];

        var calls     = new List<Call>();
        var relations = new List<(string Name, Call Left, Call Right)>();
        foreach (var (p, pBody) in predicates)
        foreach (var (q, qBody) in predicates.Where(q => q.Name != p))
        {
            var and = new Call($"{p} && {q}", Expression.AndAlso(pBody, qBody));
            var or  = new Call($"{p} || {q}", Expression.OrElse(pBody, qBody));
            calls.AddRange([and, or]);
            relations.Add(("!(P && Q) == !P || !Q", new Call($"!({p} && {q})", Expression.Not(and.Body)),
                           new Call($"!{p} || !{q}", Expression.OrElse(Expression.Not(pBody), Expression.Not(qBody)))));
            relations.Add(("!(P || Q) == !P && !Q", new Call($"!({p} || {q})", Expression.Not(or.Body)),
                           new Call($"!{p} && !{q}", Expression.AndAlso(Expression.Not(pBody), Expression.Not(qBody)))));
        }

        var failures = new List<string>();
        var judged   = 0;
        foreach (var call in calls)
        {
            if (await CompositionRunner.Judge("compound", call, Row, context.Rows.AsNoTracking(), loaded, r => r.Id, id => Names[id], failures) == Outcome.Compared) judged++;
        }

        foreach (var (relation, left, right) in relations)
            await Relate(context, relation, left, right, failures);


        Assert.AreEqual(calls.Count, judged, "every compound predicate translates");

        Candidates.Verdict(nameof(QuantifierProbeTests), failures,
                           "A compound predicate over nullable columns: PostgreSQL disagrees with rule (c) or with De Morgan",
                           method: nameof(CompoundPredicates_OverNullableColumns_AnswerAsRuleCAndKeepDeMorgan));
    }

    /// <summary>
    /// A predicate over the result of a value-producing member: <c>IsEmpty</c> and an overlap or
    /// containment test on every discovered member that returns a range, a multirange or a value set,
    /// over a nullable column and a captured operand.
    /// </summary>
    [TestMethod]
    public async Task NestedPredicates_OverAValueProducingMember_AnswerAsRuleC()
    {
        ContainerLifecycle.RequireServer();

        var (context, loaded) = await Database();
        await using var _ = context;

        Operand[] Slot(Type type, int position) => position == 0
            ? ColumnFor(Kind(type)) is { } column ? [column] : []
            : Kind(type) switch
            {
                "range"   => [new("@[3,8]", Captured(Days(3, 8)), false), new("@empty", Captured(DateRange.Empty), false)],
                "set"     => [new("@{[3,8]}", Captured(Blocks(Days(3, 8))), false)],
                "values"  => [new("@{a}", Captured(StringSet.From(["a"])), false)],
                "element" => [new("@2026-03-07", Captured(March(7)), false)],
                "text"    => [new("@a", Captured("a"), false)],
                _         => []
            };

        var calls = new List<Call>();
        foreach (var inner in Calls(Slot, _ => true).Where(c => Kind(c.Body.Type) is "range" or "set" or "values"))
        {
            switch (Kind(inner.Body.Type))
            {
                case "range":
                    calls.Add(new Call($"{inner.Name}.IsEmpty()", Expression.Call(typeof(RangeExtensions), nameof(RangeExtensions.IsEmpty), [ElementType], inner.Body)));
                    calls.Add(new Call($"{inner.Name}.Overlaps(@[3,8])", Range1(nameof(RangeExtensions.Overlaps), inner.Body, Captured(Days(3, 8)))));
                    break;
                case "set":
                    calls.Add(new Call($"{inner.Name}.IsEmpty()", Expression.Call(inner.Body, SetType.GetMethod(nameof(RangeSet<,>.IsEmpty))!)));
                    calls.Add(new Call($"{inner.Name}.Contains(@2026-03-07)", Expression.Call(inner.Body, SetType.GetMethod(nameof(RangeSet<,>.Contains), [ElementType])!, Captured(March(7)))));
                    break;
                default:
                    calls.Add(new Call($"{inner.Name}.IsEmpty", Expression.Property(inner.Body, nameof(StringSet.IsEmpty))));
                    calls.Add(new Call($"{inner.Name}.Contains(@a)",
                                       Expression.Call(Methods.Single(m => m.DeclaringType == typeof(ValueSetExtensions) && m.Name == nameof(ValueSetExtensions.Contains)),
                                                       inner.Body, Captured("a"))));
                    break;
            }
        }

        var failures     = new List<string>();
        var untranslated = new HashSet<string>();
        var unanswered   = new HashSet<string>();
        var judged       = 0;
        foreach (var call in calls)
        {
            switch (await CompositionRunner.Judge("nested", call, Row, context.Rows.AsNoTracking(), loaded, r => r.Id, id => Names[id], failures))
            {
                case Outcome.Compared:     judged++; break;
                case Outcome.Untranslated: untranslated.Add(call.Name); break;
                case Outcome.Unanswered:   unanswered.Add(call.Name); break;
            }
        }


        Assert.IsGreaterThanOrEqualTo(40, judged, $"Only {judged} nested predicates were judged; untranslated: {string.Join("; ", untranslated.Order())}; unanswered: {string.Join("; ", unanswered.Order())}");

        Candidates.Verdict(nameof(QuantifierProbeTests), failures,
                           "A predicate over a value-producing member of a nullable column: PostgreSQL and rule (c) disagree",
                           method: nameof(NestedPredicates_OverAValueProducingMember_AnswerAsRuleC));
    }

    /// <summary>Two filters that must select the same rows; one line per row they split on.</summary>
    private static async Task Relate(QuantContext context, string relation, Call left, Call right, List<string> failures)
    {
        try
        {
            var a = await Ids(context, left.Body);
            var b = await Ids(context, right.Body);
            foreach (var id in a.Union(b).Order().Where(id => a.Contains(id) != b.Contains(id)))
                failures.Add($"{left.Name} [{relation}], row {Names[id]}: {(a.Contains(id) ? "selected" : "dropped")} by the left form, {(b.Contains(id) ? "selected" : "dropped")} by {right.Name}");
        }
        catch (InvalidOperationException e) when (e.Message.Contains("could not be translated", StringComparison.Ordinal))
        {
            // Counted by Judge; a right-hand form EF Core cannot translate has nothing to relate.
        }
        catch (Exception e) when (e is InvalidOperationException or System.Data.Common.DbException)
        {
            failures.Add($"{left.Name} [{relation}]: server threw {e.GetType().Name}: {CompositionRunner.FirstLine(e)}");
        }
    }

    private static async Task<HashSet<int>> Ids(QuantContext context, Expression filter) =>
        (await context.Rows.Where(Expression.Lambda<Func<QuantRow, bool>>(filter, Row)).Select(r => r.Id).ToListAsync()).ToHashSet();

    private static Expression Quantify(string quantifier, Expression list, ParameterExpression x, Expression body) =>
        Expression.Call(typeof(Enumerable), quantifier, [x.Type], list, Expression.Lambda(body, x));

    // A captured list, which EF Core sends as a parameter; typed by its runtime type.
    private static object Holder(object list) =>
        Activator.CreateInstance(typeof(System.Runtime.CompilerServices.StrongBox<>).MakeGenericType(list.GetType()), list)!;

    private static Expression Range1(string name, Expression range, Expression operand) =>
        Expression.Call(typeof(RangeExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                                               .Single(m => m.Name == name && m.GetParameters()[1].ParameterType.IsGenericType
                                                         && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(IRange<>))
                                               .MakeGenericMethod(ElementType),
                        range, operand);

    private static Expression Build(MethodInfo method, IReadOnlyList<Expression> operands) =>
        method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal)
            ? throw new NotSupportedException(method.Name)
            : method.IsStatic ? Expression.Call(method, operands) : Expression.Call(operands[0], method, operands.Skip(1));

    public sealed class QuantRow
    {
        public int Id { get; set; }

        public DateRange? Period { get; set; }

        public RangeSet<DateRange, DateOnly>? Blocks { get; set; }

        public StringSet? Tags { get; set; }

        public DateOnly? Day { get; set; }

        public string? Label { get; set; }
    }

    private sealed class QuantContext(string connectionString) : DbContext
    {
        public DbSet<QuantRow> Rows => Set<QuantRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<QuantRow>().Property(r => r.Id).ValueGeneratedNever();
    }
}

using System.Linq.Expressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using static CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition.CompositionMembers;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The NULL an outer join produces from a <b>required</b> column: every range, <see cref="RangeSet{TRange,T}"/>
/// and value set member over the child of a parent that has none, reached through
/// <c>GroupJoin</c>/<c>DefaultIfEmpty</c>, <c>LeftJoin</c> and an optional navigation, against
/// <see cref="NullRule"/> over the rows as loaded (#375).
/// </summary>
/// <remarks>
/// <para>
/// The nullable-column sweeps put the NULL in a column the model declares nullable. Here the model says
/// the child's columns cannot be NULL, and only the join makes them so; EF Core reads that from the
/// join, not from the column, so a translation can be right over a nullable column and wrong here.
/// </para>
/// <para>
/// The calls are discovered (<see cref="CompositionMembers"/>): the child's range, multirange, value set
/// and element columns as the receiver or the operand, the parent's required columns and captured values
/// on the other side, and the child's range coalesced with <c>(,)</c>. On top of them, the range factories
/// over the child's element columns and the bounds through <c>??</c>. The reference guards the missing
/// child explicitly: a predicate over it is <see langword="false"/> and its negation <see langword="true"/>
/// (rule (c)), and a bound, a value-producing member or a factory over it has no value.
/// </para>
/// </remarks>
[TestClass]
public sealed class OuterJoinProbeTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    private static DateRange Days(int from, int to) => DateRange.CreateFinite(March(from), March(to), true, true);

    private static RangeSet<DateRange, DateOnly> Blocks(params DateRange[] ranges) => RangeSet<DateRange, DateOnly>.From(ranges);

    [TestMethod]
    public async Task EveryMember_OverAMissingChild_AnswersAsRuleC()
    {
        ContainerLifecycle.RequireServer();

        var connectionString = await CompositionDatabase.Create("outer_join");
        await using (var seed = new OuterJoinContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Parents.AddRange(
                new JoinParent { Id = 1, Window = Days(5, 22), WindowBlocks = Blocks(Days(3, 8)), Labels = StringSet.From(["a"]) },
                new JoinParent { Id = 2, Window = Days(5, 22), WindowBlocks = Blocks(Days(3, 8)), Labels = StringSet.From(["a"]) },
                new JoinParent { Id = 3, Window = DateRange.Infinite, WindowBlocks = Blocks(DateRange.Infinite), Labels = StringSet.Empty },
                new JoinParent { Id = 4, Window = Days(5, 22), WindowBlocks = Blocks(Days(3, 8)), Labels = StringSet.From(["b"]) },
                new JoinParent { Id = 5, Window = DateRange.Empty, WindowBlocks = Blocks(), Labels = StringSet.From(["a"]) },
                new JoinParent { Id = 6, Window = Days(5, 22), WindowBlocks = Blocks(Days(3, 8)), Labels = StringSet.From(["a"]) },
                new JoinParent { Id = 7, Window = DateRange.Infinite, WindowBlocks = Blocks(DateRange.Infinite), Labels = StringSet.Empty });
            seed.Children.AddRange(
                new JoinChild { Id = 11, ParentId = 1, Period = Days(1, 10), Blocks = Blocks(Days(1, 10)), Tags = StringSet.From(["a"]), From = March(1), To = March(10), Name = "a" },
                new JoinChild { Id = 12, ParentId = 2, Period = DateRange.Empty, Blocks = Blocks(), Tags = StringSet.Empty, From = March(10), To = March(1), Name = "b" },
                new JoinChild { Id = 13, ParentId = 3, Period = DateRange.Infinite, Blocks = Blocks(DateRange.Infinite), Tags = StringSet.From(["a", "b"]), From = March(1), To = March(31), Name = "a" },
                new JoinChild { Id = 14, ParentId = 4, Period = DateRange.CreateUnboundedEnd(March(20), true), Blocks = Blocks(Days(20, 25), DateRange.CreateUnboundedEnd(March(27), true)),
                                Tags = StringSet.From(["b"]), From = March(20), To = March(25), Name = "c" },
                new JoinChild { Id = 15, ParentId = 5, Period = DateRange.CreateUnboundedStart(March(10), true), Blocks = Blocks(DateRange.CreateUnboundedStart(March(10), true)),
                                Tags = StringSet.From(["a"]), From = March(7), To = March(7), Name = "a" });
            await seed.SaveChangesAsync();
        }

        // Parents 6 and 7 have no child. The names say which shape the child holds.
        var names = new Dictionary<int, string>
        {
            [1] = "child [1,10]", [2] = "child empty", [3] = "child (,)", [4] = "child [20,)", [5] = "child (,10]",
            [6] = "no child", [7] = "no child, window (,)"
        };

        var row   = Expression.Parameter(typeof(Joined), "j");
        var child = Expression.Property(row, nameof(Joined.Child));
        var parent = Expression.Property(row, nameof(Joined.Parent));
        Expression Of(Expression on, string property) => Expression.Property(on, property);

        Operand[] Slot(Type type, int position) => Kind(type) switch
        {
            "range" => position == 0
                ? [new("c.Period", Of(child, nameof(JoinChild.Period)), true),
                   new("(c.Period ?? @(,))", Expression.Coalesce(Of(child, nameof(JoinChild.Period)), Captured(DateRange.Infinite)), true),
                   new("p.Window", Of(parent, nameof(JoinParent.Window)), false)]
                : [new("c.Period", Of(child, nameof(JoinChild.Period)), true),
                   new("p.Window", Of(parent, nameof(JoinParent.Window)), false),
                   new("@[3,8]", Captured(Days(3, 8)), false)],
            "set" => position == 0
                ? [new("c.Blocks", Of(child, nameof(JoinChild.Blocks)), true), new("p.WindowBlocks", Of(parent, nameof(JoinParent.WindowBlocks)), false)]
                : [new("c.Blocks", Of(child, nameof(JoinChild.Blocks)), true), new("p.WindowBlocks", Of(parent, nameof(JoinParent.WindowBlocks)), false),
                   new("@{[3,8]}", Captured(Blocks(Days(3, 8))), false)],
            "values" => position == 0
                ? [new("c.Tags", Of(child, nameof(JoinChild.Tags)), true), new("p.Labels", Of(parent, nameof(JoinParent.Labels)), false)]
                : [new("c.Tags", Of(child, nameof(JoinChild.Tags)), true), new("p.Labels", Of(parent, nameof(JoinParent.Labels)), false),
                   new("@{a}", Captured(StringSet.From(["a"])), false)],
            "element" => [new("c.From", Of(child, nameof(JoinChild.From)), true), new("@2026-03-07", Captured(March(7)), false)],
            "text"    => [new("c.Name", Of(child, nameof(JoinChild.Name)), true), new("@a", Captured("a"), false)],
            _         => []
        };

        // A call is this sweep's when the child reaches it, and a binary one only once with the child on
        // both sides; the (c.Period ?? @(,)) receiver is for the members of one operand.
        var calls = Calls(Slot, operands =>
            operands.Any(o => o.Nullable)
            && (operands.Count == 1 || !operands[0].Name.StartsWith('(')));

        calls.AddRange(Factories(child));

        var failures     = new List<string>();
        var untranslated = new HashSet<string>();
        var unanswered   = new HashSet<string>();
        var judged       = 0;

        await using var context = new OuterJoinContext(connectionString);
        var parents  = await context.Parents.AsNoTracking().ToListAsync();
        var children = await context.Children.AsNoTracking().ToListAsync();
        var loaded   = parents.Select(p => new Joined { Id = p.Id, Parent = p, Child = children.SingleOrDefault(c => c.ParentId == p.Id) })
                              .OrderBy(j => j.Id).ToList();
        Assert.AreEqual(2, loaded.Count(j => j.Child is null), "the parents without a child are what this sweep is about");

        foreach (var (form, source) in JoinForms(context))
        {
            foreach (var call in calls)
            {
                switch (await CompositionRunner.Judge(form, call, row, source, loaded, j => j.Id, id => names[id], failures))
                {
                    case Outcome.Compared:     judged++; break;
                    case Outcome.Untranslated: untranslated.Add(call.Name); break;
                    case Outcome.Unanswered:   unanswered.Add(call.Name); break;
                }
            }
        }

        // Some 280 calls translate per join form today; the floor catches a discovery or a join form that stopped working.
        Assert.IsGreaterThanOrEqualTo(3 * 250, judged,
            $"Only {judged} calls were judged; untranslated: {string.Join("; ", untranslated.Order())}; unanswered: {string.Join("; ", unanswered.Order())}");

        Candidates.Verdict(nameof(OuterJoinProbeTests), failures,
                           "A member over the child of an outer join: PostgreSQL and rule (c) disagree for a parent without a child",
                           method: nameof(EveryMember_OverAMissingChild_AnswersAsRuleC));
    }

    /// <summary>
    /// The three ways a consumer reaches an optional child: an optional reference navigation, a
    /// <c>GroupJoin</c> flattened with <c>DefaultIfEmpty</c>, and .NET 10's <c>LeftJoin</c>.
    /// </summary>
    private static (string Form, IQueryable<Joined> Source)[] JoinForms(OuterJoinContext context) =>
    [
        ("navigation", context.Parents.Select(p => new Joined { Id = p.Id, Parent = p, Child = p.Child })),
        ("DefaultIfEmpty", context.Parents.GroupJoin(context.Children, p => p.Id, c => c.ParentId, (p, cs) => new { p, cs })
                                  .SelectMany(x => x.cs.DefaultIfEmpty(), (x, c) => new Joined { Id = x.p.Id, Parent = x.p, Child = c })),
        ("LeftJoin", context.Parents.LeftJoin(context.Children, p => p.Id, c => c.ParentId,
                                              (p, c) => new Joined { Id = p.Id, Parent = p, Child = c }))
    ];

    /// <summary>
    /// The range factories over the child's element columns, as values and under a predicate, and the
    /// bounds of the child's range through <c>??</c>: the calls that read the child's columns through
    /// something other than a library member's receiver.
    /// </summary>
    private static IEnumerable<Call> Factories(Expression child)
    {
        var from = Expression.Property(child, nameof(JoinChild.From));
        var to   = Expression.Property(child, nameof(JoinChild.To));
        var v    = Captured(March(7));

        var finite = Expression.Call(typeof(DateRange), nameof(DateRange.CreateFinite), null, from, to, Expression.Constant(true), Expression.Constant(true));
        var start  = Expression.Call(typeof(DateRange), nameof(DateRange.CreateUnboundedStart), null, to, Expression.Constant(true));
        var end    = Expression.Call(typeof(DateRange), nameof(DateRange.CreateUnboundedEnd), null, from, Expression.Constant(true));

        foreach (var (name, factory) in new[]
                 {
                     ("DateRange.CreateFinite(c.From, c.To)", (Expression)finite),
                     ("DateRange.CreateUnboundedStart(c.To)", start),
                     ("DateRange.CreateUnboundedEnd(c.From)", end)
                 })
        {
            yield return new Call(name, factory);
            yield return new Call($"{name}.IsEmpty()", Expression.Call(typeof(RangeExtensions), nameof(RangeExtensions.IsEmpty), [typeof(DateOnly)], factory));
            yield return new Call($"{name}.Contains(@2026-03-07)",
                                  Expression.Call(typeof(RangeExtensions).GetMethods().Single(m => m.Name == nameof(RangeExtensions.Contains)
                                                      && m.GetParameters()[1].ParameterType.IsGenericParameter).MakeGenericMethod(typeof(DateOnly)),
                                                  factory, v));
        }

        var period = Expression.Property(child, nameof(JoinChild.Period));
        var lower  = Expression.Call(typeof(RangeExtensions), nameof(RangeExtensions.LowerBound), [typeof(DateOnly)], period);
        var upper  = Expression.Call(typeof(RangeExtensions), nameof(RangeExtensions.UpperBound), [typeof(DateOnly)], period);
        yield return new Call("(c.Period.LowerBound() ?? @0001-01-01) <= @2026-03-07",
                              Expression.LessThanOrEqual(Expression.Coalesce(lower, Captured(DateOnly.MinValue)), Expression.Convert(v, typeof(DateOnly))));
        yield return new Call("(c.Period.UpperBound() ?? @9999-12-31) >= @2026-03-07",
                              Expression.GreaterThanOrEqual(Expression.Coalesce(upper, Captured(DateOnly.MaxValue)), Expression.Convert(v, typeof(DateOnly))));
        yield return new Call("c.Period.LowerBound() ?? @0001-01-01", Expression.Coalesce(lower, Captured(DateOnly.MinValue)));
        yield return new Call("c.From <= c.To", Expression.LessThanOrEqual(from, to));
    }

    public sealed class JoinParent
    {
        public int Id { get; set; }

        public DateRange Window { get; set; } = DateRange.Empty;

        public RangeSet<DateRange, DateOnly> WindowBlocks { get; set; } = RangeSet<DateRange, DateOnly>.Empty;

        public StringSet Labels { get; set; } = StringSet.Empty;

        public JoinChild? Child { get; set; }
    }

    /// <summary>The optional child: every column required, so only the join can make it NULL.</summary>
    public sealed class JoinChild
    {
        public int Id { get; set; }

        public int ParentId { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;

        public RangeSet<DateRange, DateOnly> Blocks { get; set; } = RangeSet<DateRange, DateOnly>.Empty;

        public StringSet Tags { get; set; } = StringSet.Empty;

        public DateOnly From { get; set; }

        public DateOnly To { get; set; }

        public string Name { get; set; } = "";
    }

    /// <summary>A parent and its child, if any, as each join form returns them.</summary>
    public sealed class Joined
    {
        public int Id { get; set; }

        public JoinParent Parent { get; set; } = null!;

        public JoinChild? Child { get; set; }
    }

    private sealed class OuterJoinContext(string connectionString) : DbContext
    {
        public DbSet<JoinParent> Parents => Set<JoinParent>();

        public DbSet<JoinChild> Children => Set<JoinChild>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<JoinParent>().Property(p => p.Id).ValueGeneratedNever();
            modelBuilder.Entity<JoinChild>().Property(c => c.Id).ValueGeneratedNever();
            modelBuilder.Entity<JoinParent>().HasOne(p => p.Child).WithOne().HasForeignKey<JoinChild>(c => c.ParentId);
        }
    }
}

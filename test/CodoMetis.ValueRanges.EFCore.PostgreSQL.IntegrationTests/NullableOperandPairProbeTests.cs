using System.Linq.Expressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using static CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition.CompositionMembers;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Every binary range, <see cref="RangeSet{TRange,T}"/> and value set member with <b>both</b> operands
/// able to be NULL: two nullable columns, a conditional over them (<c>r.Flag ? r.A : r.B</c>, a
/// <c>CASE</c>) and a coalesce of them (<c>r.A ?? r.B</c>, a <c>COALESCE</c>), over every pair of
/// (null, null), (null, value), (value, null) and (value, value), against <see cref="NullRule"/> over the
/// rows as loaded (#376).
/// </summary>
/// <remarks>
/// <para>
/// The nullable-column sweep (#355) pairs one nullable column with a non-null captured operand. Here EF
/// Core has to compensate for two operands, and for operands whose nullability it infers from an
/// expression rather than reads from a column. The values are a finite range, <c>empty</c>, <c>(,)</c>
/// and an unbounded one, each in a row with every other and with NULL; the element operand is a nullable
/// <c>DateOnly</c> column read through <c>.Value</c>.
/// </para>
/// <para>
/// Equality and <c>CompareTo</c> are swept beside the members, each with its own documented rule: EF
/// Core's equality, under which NULL equals NULL, and <see cref="Comparer{T}.Default"/>'s order, under
/// which a NULL sorts first (#261, <c>docs/ranges.md#sort-ranges</c>).
/// </para>
/// </remarks>
[TestClass]
public sealed class NullableOperandPairProbeTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    private static DateRange Days(int from, int to) => DateRange.CreateFinite(March(from), March(to), true, true);

    private static RangeSet<DateRange, DateOnly> Blocks(params DateRange[] ranges) => RangeSet<DateRange, DateOnly>.From(ranges);

    [TestMethod]
    public async Task EveryBinaryMember_OverTwoNullableOperands_AnswersAsRuleC()
    {
        ContainerLifecycle.RequireServer();

        (string Name, DateRange? Range, RangeSet<DateRange, DateOnly>? Set, StringSet? Tags)[] values =
        [
            ("null", null, null, null),
            ("[1,10]", Days(1, 10), Blocks(Days(1, 10)), StringSet.From(["a"])),
            ("empty", DateRange.Empty, Blocks(), StringSet.Empty),
            ("(,)", DateRange.Infinite, Blocks(DateRange.Infinite), StringSet.From(["a", "b"])),
            ("[20,)", DateRange.CreateUnboundedEnd(March(20), true), Blocks(Days(20, 25), DateRange.CreateUnboundedEnd(March(27), true)), StringSet.From(["b"]))
        ];
        DateOnly?[] days   = [null, March(3), March(7), March(20), March(31)];
        string?[]   labels = [null, "a", "b", "c", "a"];

        var connectionString = await CompositionDatabase.Create("operand_pair");
        var names = new Dictionary<int, string>();
        await using (var seed = new PairContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            for (var a = 0; a < values.Length; a++)
            for (var b = 0; b < values.Length; b++)
            {
                var id = a * values.Length + b + 1;
                names[id] = $"({values[a].Name}, {values[b].Name})";
                seed.Rows.Add(new PairRow
                {
                    Id = id, Flag = (a + b) % 2 == 0, Day = days[(a + 2 * b) % days.Length], Label = labels[(2 * a + b) % labels.Length],
                    A = values[a].Range, B = values[b].Range, SetA = values[a].Set, SetB = values[b].Set, TagsA = values[a].Tags, TagsB = values[b].Tags
                });
            }

            await seed.SaveChangesAsync();
        }

        var row = Expression.Parameter(typeof(PairRow), "r");
        Expression Column(string name) => Expression.Property(row, name);
        var flag = Column(nameof(PairRow.Flag));

        // Per family the two columns, the conditional between them and their coalesce, on either side.
        Operand[] Forms(string a, string b) =>
        [
            new($"r.{a}", Column(a), true),
            new($"(r.Flag ? r.{a} : r.{b})", Expression.Condition(flag, Column(a), Column(b)), true),
            new($"(r.{a} ?? r.{b})", Expression.Coalesce(Column(a), Column(b)), true)
        ];

        Operand[] Slot(Type type, int position)
        {
            Operand[] Of(string left, string right) => position == 0 ? Forms(left, right) : Forms(right, left);
            return Kind(type) switch
            {
                "range"   => Of(nameof(PairRow.A), nameof(PairRow.B)),
                "set"     => Of(nameof(PairRow.SetA), nameof(PairRow.SetB)),
                "values"  => Of(nameof(PairRow.TagsA), nameof(PairRow.TagsB)),
                "element" => [new("r.Day.Value", Expression.Property(Column(nameof(PairRow.Day)), nameof(Nullable<>.Value)), true)],
                "text"    => [new("r.Label", Column(nameof(PairRow.Label)), true)],
                _         => []
            };
        }

        // The members of one operand are #355's and the nullable-column sweep's; here only the forms it
        // does not reach, the conditional and the coalesce, are kept for them.
        var calls = Calls(Slot, operands => operands.Count > 1 || operands[0].Name.StartsWith('('));
        calls.AddRange(EqualityAndOrder(Column));

        var failures     = new List<string>();
        var untranslated = new HashSet<string>();
        var unanswered   = new HashSet<string>();
        var judged       = 0;

        await using var context = new PairContext(connectionString);
        var loaded = await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        foreach (var call in calls)
        {
            switch (await CompositionRunner.Judge("pair", call, row, context.Rows.AsNoTracking(), loaded, r => r.Id, id => names[id], failures))
            {
                case Outcome.Compared:     judged++; break;
                case Outcome.Untranslated: untranslated.Add(call.Name); break;
                case Outcome.Unanswered:   unanswered.Add(call.Name); break;
            }
        }


        Assert.IsGreaterThanOrEqualTo(500, judged, $"Only {judged} calls were judged; untranslated: {string.Join("; ", untranslated.Order())}; unanswered: {string.Join("; ", unanswered.Order())}");

        Candidates.Verdict(nameof(NullableOperandPairProbeTests), failures,
                           "A binary member over two nullable operands: PostgreSQL and rule (c) disagree",
                           method: nameof(EveryBinaryMember_OverTwoNullableOperands_AnswersAsRuleC));
    }

    /// <summary>
    /// <c>==</c>, <c>!=</c> and <c>CompareTo</c> against 0 for the two range, multirange and value set
    /// columns, each under its own documented rule (<see cref="NullRule"/>).
    /// </summary>
    private static IEnumerable<Call> EqualityAndOrder(Func<string, Expression> column)
    {
        foreach (var (a, b) in new[] { (nameof(PairRow.A), nameof(PairRow.B)), (nameof(PairRow.SetA), nameof(PairRow.SetB)), (nameof(PairRow.TagsA), nameof(PairRow.TagsB)) })
        {
            yield return new Call($"r.{a} == r.{b}", Expression.Equal(column(a), column(b)));
            yield return new Call($"r.{a} != r.{b}", Expression.NotEqual(column(a), column(b)));
        }

        foreach (var (a, b) in new[] { (nameof(PairRow.A), nameof(PairRow.B)), (nameof(PairRow.SetA), nameof(PairRow.SetB)) })
        {
            var compare = Expression.Call(column(a), column(a).Type.GetMethod(nameof(IComparable<>.CompareTo), [column(b).Type])!, column(b));
            foreach (var (name, op) in new[] { ("<", ExpressionType.LessThan), ("<=", ExpressionType.LessThanOrEqual), ("==", ExpressionType.Equal),
                                               (">=", ExpressionType.GreaterThanOrEqual), (">", ExpressionType.GreaterThan) })
                yield return new Call($"r.{a}.CompareTo(r.{b}) {name} 0", Expression.MakeBinary(op, compare, Expression.Constant(0)));
        }
    }

    public sealed class PairRow
    {
        public int Id { get; set; }

        public bool Flag { get; set; }

        public DateOnly? Day { get; set; }

        public string? Label { get; set; }

        public DateRange? A { get; set; }

        public DateRange? B { get; set; }

        public RangeSet<DateRange, DateOnly>? SetA { get; set; }

        public RangeSet<DateRange, DateOnly>? SetB { get; set; }

        public StringSet? TagsA { get; set; }

        public StringSet? TagsB { get; set; }
    }

    private sealed class PairContext(string connectionString) : DbContext
    {
        public DbSet<PairRow> Rows => Set<PairRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<PairRow>().Property(r => r.Id).ValueGeneratedNever();
    }
}

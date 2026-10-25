using System.Linq.Expressions;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <see cref="RangeLowerBoundComparer{TRange,T}"/> in a query runs in memory: a filter or an ordering fails
/// translation, and a final projection answers what the comparer answers over the loaded rows (#385).
/// </summary>
/// <remarks>
/// EF Core reads any <c>Compare(x, y)</c> as the natural order (dotnet/efcore#39402), so the comparer ran in
/// SQL in the range order: with <c>p = [2026-03-01,2026-03-10]</c>, <c>[2026-03-01,)</c> and
/// <c>[2026-03-01,2026-03-03]</c> compared as 1 and -1 where memory compares them as 0, and
/// <c>Where(… == 0)</c> selected one row of the three. The comparer is reached directly, through
/// <see cref="IComparer{T}"/>, and through <see cref="RangeSet{TRange,T}.LowerBoundComparer"/>, which a query
/// cannot tell apart once it is a parameter; <see cref="Comparer{T}.Default"/> goes the same way. A NULL
/// column sorts first, as the comparer says. The operands decide, not the type <c>Compare</c> declares: a
/// comparer over <see cref="IRange{T}"/>, the non-generic <see cref="System.Collections.IComparer"/> and a
/// comparer of value sets each still compared in the range order (#452). By count, <c>{z}</c> is equal to
/// rows 1 and 2, where SQL's <c>=</c> selected none.
/// </remarks>
[TestClass]
public sealed class LowerBoundComparerQueryTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    private static readonly DateRange P = DateRange.CreateFinite(March(1), March(10), true, true);

    private static readonly DateRange?[] Periods =
    [
        P,
        DateRange.CreateUnboundedEnd(March(1), true),
        DateRange.CreateFinite(March(1), March(3), true, true),
        DateRange.CreateFinite(March(5), March(6), true, true),
        null
    ];

    private static readonly StringSet?[] Tags = [StringSet.From(["a"]), StringSet.From(["b"]), StringSet.From(["a", "b"]), StringSet.Empty, null];

    private static readonly IComparer<IRange<DateOnly>> OverInterface =
        Comparer<IRange<DateOnly>>.Create((x, y) => RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare((DateRange?)x, (DateRange?)y));

    private static readonly System.Collections.IComparer Created = Comparer<DateRange>.Create(RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare);

    private static readonly System.Collections.IComparer OwnNonGeneric = new LowerBoundObjectComparer();

    private static readonly IComparer<StringSet> ByCount = Comparer<StringSet>.Create((x, y) => (x?.Count ?? -1).CompareTo(y?.Count ?? -1));

    private static readonly StringSet OneTag = StringSet.From(["z"]);

    [TestMethod]
    public async Task Compare_InAProjection_AnswersAsTheComparerDoes()
    {
        ContainerLifecycle.RequireServer();
        await using var context = new ComparerContext(await Seed());
        var loaded = await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        IComparer<DateRange> viaInterface = RangeLowerBoundComparer<DateRange, DateOnly>.Instance;
        var p = P;

        async Task Agree(string label, Expression<Func<ComparerRow, int>> compare)
        {
            var server = await context.Rows.OrderBy(r => r.Id).Select(compare).ToListAsync();
            var memory = loaded.Select(compare.Compile()).ToList();
            CollectionAssert.AreEqual(memory, server, $"{label}: server {string.Join(",", server)}, memory {string.Join(",", memory)}");
        }

        // The issue's four rows by lower bound: 0, 0, 0, 1, where the range order is 0, 1, -1, 1.
        var issueRows = await context.Rows.Where(r => r.Period != null).OrderBy(r => r.Id)
                                     .Select(r => RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare(r.Period, p)).ToListAsync();
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 1 }, issueRows, $"server {string.Join(",", issueRows)}");

        // Every row, the NULL one first in the comparer's order (-1).
        await Agree("RangeLowerBoundComparer.Instance.Compare", r => RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare(r.Period, p));
        await Agree("IComparer<DateRange>.Compare", r => viaInterface.Compare(r.Period, p));
        await Agree("RangeSet.LowerBoundComparer.Compare", r => RangeSet<DateRange, DateOnly>.LowerBoundComparer.Compare(p, r.Period));
        await Agree("Comparer<DateRange>.Default.Compare", r => Comparer<DateRange>.Default.Compare(r.Period, p));

        // As locals, so each comparer is a query parameter, as in the filter test.
        var (overInterface, created, ownNonGeneric, byCount, oneTag) = (OverInterface, Created, OwnNonGeneric, ByCount, OneTag);
        await Agree("IComparer<IRange<DateOnly>>.Compare", r => overInterface.Compare(r.Period, p));
        await Agree("Comparer<DateRange>.Create as IComparer", r => created.Compare(r.Period, p));
        await Agree("own IComparer", r => ownNonGeneric.Compare(r.Period, p));
        await Agree("IComparer<StringSet>.Compare", r => byCount.Compare(r.Tags, oneTag));
    }

    [TestMethod]
    public async Task Compare_InAFilterOrAnOrdering_FailsTranslation()
    {
        ContainerLifecycle.RequireServer();
        await using var context = new ComparerContext(await Seed());

        IComparer<DateRange> viaInterface = RangeLowerBoundComparer<DateRange, DateOnly>.Instance;
        var p = P;

        // As locals, so each comparer is a query parameter. EF Core leaves a Compare on a constant comparer
        // (a static field) untranslated in any case, and the range order needs a parameter (#452).
        var (overInterface, created, ownNonGeneric, byCount, oneTag) = (OverInterface, Created, OwnNonGeneric, ByCount, OneTag);

        IQueryable<ComparerRow>[] queries =
        [
            context.Rows.Where(r => RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare(r.Period, p) == 0),
            context.Rows.Where(r => viaInterface.Compare(r.Period, p) == 0),
            context.Rows.Where(r => RangeSet<DateRange, DateOnly>.LowerBoundComparer.Compare(r.Period, p) <= 0),
            context.Rows.OrderBy(r => RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare(r.Period, p)),
            context.Rows.Where(r => overInterface.Compare(r.Period, p) == 0),
            context.Rows.OrderBy(r => overInterface.Compare(r.Period, p)),
            context.Rows.Where(r => created.Compare(r.Period, p) == 0),
            context.Rows.OrderBy(r => created.Compare(r.Period, p)),
            context.Rows.Where(r => ownNonGeneric.Compare(r.Period, p) == 0),
            context.Rows.OrderBy(r => ownNonGeneric.Compare(r.Period, p)),
            context.Rows.Where(r => byCount.Compare(r.Tags, oneTag) == 0),
            context.Rows.OrderBy(r => byCount.Compare(r.Tags, oneTag)),
        ];

        foreach (var query in queries)
        {
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => query.ToListAsync(), query.Expression.ToString());
            StringAssert.Contains(error.Message, "could not be translated");
            StringAssert.Contains(error.Message, "RangeComparerCall.RunInMemory");
        }
    }

    private static async Task<string> Seed()
    {
        var connectionString = await CompositionDatabase.Create("lower_bound_comparer");
        await using var seed = new ComparerContext(connectionString);
        await seed.Database.EnsureCreatedAsync();
        seed.Rows.AddRange(Periods.Select((period, index) => new ComparerRow { Id = index + 1, Period = period, Tags = Tags[index] }));
        await seed.SaveChangesAsync();
        return connectionString;
    }

    public sealed class ComparerRow
    {
        public int Id { get; set; }

        public DateRange? Period { get; set; }

        public StringSet? Tags { get; set; }
    }

    private sealed class LowerBoundObjectComparer : System.Collections.IComparer
    {
        public int Compare(object? x, object? y) => RangeLowerBoundComparer<DateRange, DateOnly>.Instance.Compare((DateRange?)x, (DateRange?)y);
    }

    private sealed class ComparerContext(string connectionString) : DbContext
    {
        public DbSet<ComparerRow> Rows => Set<ComparerRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ComparerRow>().Property(r => r.Id).ValueGeneratedNever();
    }
}

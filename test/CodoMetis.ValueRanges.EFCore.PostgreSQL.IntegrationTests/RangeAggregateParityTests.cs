using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Sweeps <c>RangeAgg</c> and <c>RangeIntersectAgg</c> against <c>range_agg</c> and
/// <c>range_intersect_agg</c> over every subset of a range universe that includes the empty range
/// and the infinite one, and records what the two aggregates do with those inputs.
/// </summary>
/// <remarks>
/// <para>
/// The aggregates were example-tested on both sides and swept on neither: three integration tests
/// aggregate three or four hand-picked finite ranges per range type, and the unit tests check the
/// in-memory implementations on their own. Nothing fed either aggregate an empty range, and nothing
/// fed either an unbounded one — the two inputs whose handling is a choice rather than a
/// consequence, and the two a caller is most likely to hit by accident, since <c>Empty</c> is what
/// an inverted-bounds factory returns and <c>Infinite</c> is what an all-null row materializes as.
/// </para>
/// <para>
/// Both aggregates are folds, so a subset sweep is the natural exhaustive form: each subset of the
/// universe is one group, the group's rows are its ranges, and the server's fold has to equal the
/// client's. Both element domains are swept because <c>range_agg</c> merges what
/// <see cref="RangeSet{TRange,T}"/> merges — which over a discrete domain includes the pairs that
/// are adjacent only after canonicalization, and over a continuous one the pairs whose
/// inclusivities happen to meet.
/// </para>
/// <para>
/// <see cref="Aggregates_OverEmptyAndInfiniteInputs_HaveTheseSemantics"/> states the empty and
/// infinity results as literals rather than as parity. The sweep proves the two implementations
/// agree; only a written-down expectation proves they agree on the right answer, and these are
/// exactly the cases where "whatever the other one does" is not a specification.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeAggregateParityTests
{
    /// <summary>Eight input ranges: 255 non-empty subsets, each one group.</summary>
    private const int UniverseSize = 8;

    private const int Subsets = 1 << UniverseSize;

    private sealed class AggregateRow<TRange, T>
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        public int Key { get; set; }

        public RangeSet<TRange, T> Aggregated { get; set; } = RangeSet<TRange, T>.Empty;

        public TRange? Intersected { get; set; }
    }

    /// <summary>
    /// A discrete domain, where <c>range_agg</c> merges neighbours the model only sees as adjacent
    /// after canonicalizing to closed bounds — <c>[1,5]</c> and <c>[6,10]</c> have no value between
    /// them, and both implementations have to notice independently.
    /// </summary>
    [TestMethod]
    public async Task Int32Aggregates_EverySubsetOfInputs_AgreeWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        (string Name, Int32Range Range)[] universe =
        [
            ("empty",   Int32Range.Empty),
            ("[1,5]",   Int32Range.CreateFinite(1, 5)),
            ("[6,10]",  Int32Range.CreateFinite(6, 10)),   // discretely adjacent to [1,5]
            ("[3,8]",   Int32Range.CreateFinite(3, 8)),    // overlaps both
            ("[20,30]", Int32Range.CreateFinite(20, 30)),  // disjoint from everything finite above
            ("(,0)",    Int32Range.CreateUnboundedStart(0)),   // exclusive by default: canonically (,-1]
            ("[6,)",    Int32Range.CreateUnboundedEnd(6)),
            ("(,)",     Int32Range.Infinite)
        ];

        await SweepEverySubsetOfInputs<Int32Range, int>(
            "Int32Range", 30_000, universe,
            static (row, range) => row.Seats = range,
            rows => rows.GroupBy(r => r.GroupKey)
                        .Select(g => new AggregateRow<Int32Range, int>
                        {
                            Key         = g.Key,
                            Aggregated  = g.Select(r => r.Seats).RangeAgg(),
                            Intersected = g.Select(r => r.Seats).RangeIntersectAgg()
                        }),
            static ranges => ranges.RangeAgg(),
            static ranges => ranges.RangeIntersectAgg());
    }

    /// <summary>
    /// A continuous domain, where nothing is adjacent by a step and a merge is decided by the two
    /// inclusivities meeting — <c>[1,5)</c> with <c>[5,9)</c> merges, <c>(1,5)</c> with
    /// <c>(5,9)</c> does not, and 5 is a bound of all four.
    /// </summary>
    [TestMethod]
    public async Task DecimalAggregates_EverySubsetOfInputs_AgreeWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        (string Name, DecimalRange Range)[] universe =
        [
            ("empty", DecimalRange.Empty),
            ("[1,5)", DecimalRange.CreateFinite(1m, 5m)),
            ("[5,9)", DecimalRange.CreateFinite(5m, 9m)),          // merges with [1,5)
            ("(5,9)", DecimalRange.CreateFinite(5m, 9m, false, false)), // does not merge with [1,5)
            ("[3,7)", DecimalRange.CreateFinite(3m, 7m)),
            ("(,5)",  DecimalRange.CreateUnboundedStart(5m, false)),
            ("[5,)",  DecimalRange.CreateUnboundedEnd(5m)),
            ("(,)",   DecimalRange.Infinite)
        ];

        await SweepEverySubsetOfInputs<DecimalRange, decimal>(
            "DecimalRange", 40_000, universe,
            static (row, range) => row.Price = range,
            rows => rows.GroupBy(r => r.GroupKey)
                        .Select(g => new AggregateRow<DecimalRange, decimal>
                        {
                            Key         = g.Key,
                            Aggregated  = g.Select(r => r.Price).RangeAgg(),
                            Intersected = g.Select(r => r.Price).RangeIntersectAgg()
                        }),
            static ranges => ranges.RangeAgg(),
            static ranges => ranges.RangeIntersectAgg());
    }

    // -------------------------------------------------------------------------
    // The two input shapes nothing had fed either aggregate
    // -------------------------------------------------------------------------

    /// <summary>
    /// What the aggregates do with empty and unbounded inputs, written down rather than compared:
    /// <c>range_agg</c> drops empties and absorbs everything into <c>(,)</c>, and
    /// <c>range_intersect_agg</c> collapses to empty as soon as one input is empty and treats the
    /// infinite range as the identity. The in-memory counterparts agree on all four, which is not a
    /// coincidence — <c>RangeAgg</c> is <c>RangeSet.From</c>, which drops empties by the set's
    /// invariant, and <c>RangeIntersectAgg</c> is a fold of <c>Intersect</c>.
    /// </summary>
    [TestMethod]
    public async Task Aggregates_OverEmptyAndInfiniteInputs_HaveTheseSemantics()
    {
        ContainerLifecycle.RequireDatabase();

        var finite = Int32Range.CreateFinite(1, 5);

        await Seed(
            (30_900, Int32Range.Empty), (30_900, Int32Range.Empty),
            (30_901, Int32Range.Empty), (30_901, finite),
            (30_902, Int32Range.Infinite), (30_902, finite),
            (30_903, Int32Range.Infinite), (30_903, Int32Range.Infinite));

        await using var context = new IntegrationDbContext();

        var byGroup = await context.Reservations
            .Where(r => r.GroupKey >= 30_900 && r.GroupKey <= 30_903)
            .GroupBy(r => r.GroupKey)
            .Select(g => new AggregateRow<Int32Range, int>
            {
                Key         = g.Key,
                Aggregated  = g.Select(r => r.Seats).RangeAgg(),
                Intersected = g.Select(r => r.Seats).RangeIntersectAgg()
            })
            .ToDictionaryAsync(row => row.Key);

        var empties     = byGroup[30_900];
        var emptyPlus   = byGroup[30_901];
        var infinite    = byGroup[30_902];
        var allInfinite = byGroup[30_903];

        // range_agg drops empty inputs: a group of nothing but empties aggregates to the empty
        // multirange, not to a multirange holding an empty element.
        Assert.AreEqual(RangeSet<Int32Range, int>.Empty, empties.Aggregated);
        Assert.AreEqual(RangeSet<Int32Range, int>.Empty, new[] { Int32Range.Empty, Int32Range.Empty }.RangeAgg());

        Assert.AreEqual(RangeSet<Int32Range, int>.From([finite]), emptyPlus.Aggregated);
        Assert.AreEqual(RangeSet<Int32Range, int>.From([finite]), new[] { Int32Range.Empty, finite }.RangeAgg());

        // …while range_intersect_agg does not drop them: one empty input empties the fold.
        Assert.AreEqual(Int32Range.Empty, empties.Intersected);
        Assert.AreEqual(Int32Range.Empty, emptyPlus.Intersected);
        Assert.AreEqual(Int32Range.Empty, new[] { Int32Range.Empty, finite }.RangeIntersectAgg());

        // The infinite range absorbs under range_agg and is the identity under the intersection.
        Assert.AreEqual(RangeSet<Int32Range, int>.Infinite, infinite.Aggregated);
        Assert.AreEqual(finite, infinite.Intersected);
        Assert.AreEqual(RangeSet<Int32Range, int>.Infinite, allInfinite.Aggregated);
        Assert.AreEqual(Int32Range.Infinite, allInfinite.Intersected);

        static async Task Seed(params (int GroupKey, Int32Range Range)[] rows)
        {
            await using var context = new IntegrationDbContext();
            var             id      = 30_900_000;

            foreach (var (groupKey, range) in rows)
                context.Reservations.Add(new Reservation { Id = id++, GroupKey = groupKey, Seats = range });

            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// The one divergence, executed: a PostgreSQL aggregate over zero rows is <c>NULL</c>, where
    /// the in-memory <c>RangeAgg</c> answers the empty set. <c>RangeIntersectAgg</c> has no such
    /// gap — it answers <see langword="null"/> in both. The README documents this; nothing ran it.
    /// </summary>
    /// <remarks>
    /// The server half is raw SQL because the divergence cannot be reached through LINQ at all:
    /// <c>GroupBy</c> produces no group for zero rows, so there is nothing to project, and a
    /// <c>RangeAgg</c> outside a grouping binds to the <see cref="IEnumerable{T}"/> overload and is
    /// evaluated client-side. That unreachability is the reason it is only a documentation note and
    /// not a defect — and the reason it needs its own test to stay true.
    /// </remarks>
    [TestMethod]
    public async Task AggregatesOverZeroRows_AreNullOnTheServerAndEmptyInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT range_agg("Seats") IS NULL, range_intersect_agg("Seats") IS NULL
            FROM "Reservations" WHERE false
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.IsTrue(reader.GetBoolean(0), "range_agg over zero rows is NULL");
        Assert.IsTrue(reader.GetBoolean(1), "range_intersect_agg over zero rows is NULL");

        Assert.AreEqual(RangeSet<Int32Range, int>.Empty, Array.Empty<Int32Range>().RangeAgg(),
                        "the in-memory aggregate answers the empty set where the server answers NULL");
        Assert.IsNull(Array.Empty<Int32Range>().RangeIntersectAgg(),
                      "the intersection aggregate agrees with the server on zero rows");
    }

    // -------------------------------------------------------------------------
    // The sweep
    // -------------------------------------------------------------------------

    /// <summary>
    /// Seeds one group per non-empty subset of <paramref name="universe"/> — one row per input
    /// range — and requires the server's fold over each group to equal the in-memory one.
    /// </summary>
    /// <remarks>
    /// The expected values come from <paramref name="inMemoryAgg"/> and
    /// <paramref name="inMemoryIntersect"/> — the shipped aggregates, handed in from the concrete
    /// call site because the overloads are per range type and a generic engine cannot name them.
    /// Re-deriving the fold here instead would compare PostgreSQL against a second implementation
    /// written in the test, which is a different claim and a weaker one: the first draft did that,
    /// and a seeded defect in <c>RangeIntersectAgg</c> walked through this sweep untouched.
    /// </remarks>
    private static async Task SweepEverySubsetOfInputs<TRange, T>(
        string                                                              family,
        int                                                                 block,
        (string Name, TRange Range)[]                                       universe,
        Action<Reservation, TRange>                                         assign,
        Func<IQueryable<Reservation>, IQueryable<AggregateRow<TRange, T>>>  project,
        Func<IEnumerable<TRange>, RangeSet<TRange, T>>                      inMemoryAgg,
        Func<IEnumerable<TRange>, TRange?>                                  inMemoryIntersect
    )
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        Assert.AreEqual(UniverseSize, universe.Length, $"{family}: the input universe is fixed at eight ranges");

        await using (var seed = new IntegrationDbContext())
        {
            var id = block * 100;

            for (var mask = 1; mask < Subsets; mask++)
            {
                foreach (var index in Indices(mask))
                {
                    var row = new Reservation { Id = id++, GroupKey = block + mask };
                    assign(row, universe[index].Range);
                    seed.Reservations.Add(row);
                }
            }

            await seed.SaveChangesAsync();
        }

        await using var context = new IntegrationDbContext();

        var query = project(context.Reservations.Where(r => r.GroupKey > block && r.GroupKey < block + Subsets));
        var sql   = query.ToQueryString();

        // Both aggregates must be in the SQL: a projection EF cannot translate is evaluated
        // client-side, where this sweep would compare the in-memory fold with itself.
        StringAssert.Contains(sql, "range_agg(", $"{family}: RangeAgg did not translate:\n{sql}");
        StringAssert.Contains(sql, "range_intersect_agg(", $"{family}: RangeIntersectAgg did not translate:\n{sql}");

        var server = await query.ToDictionaryAsync(row => row.Key);

        Assert.AreEqual(Subsets - 1, server.Count, $"{family}: the sweep read {server.Count} of {Subsets - 1} groups");

        var disagreements = new List<string>();

        for (var mask = 1; mask < Subsets; mask++)
        {
            var inputs = Indices(mask).Select(index => universe[index].Range).ToArray();
            var row    = server[block + mask];

            var expectedAgg       = inMemoryAgg(inputs);
            var expectedIntersect = inMemoryIntersect(inputs);

            if (!row.Aggregated.Equals(expectedAgg))
            {
                disagreements.Add(
                    $"  range_agg{Name(mask)} = {row.Aggregated}, in memory {expectedAgg}");
            }

            if (!Equals(row.Intersected, expectedIntersect))
            {
                disagreements.Add(
                    $"  range_intersect_agg{Name(mask)} = {Show(row.Intersected)}, "
                  + $"in memory {Show(expectedIntersect)}");
            }
        }

        Assert.AreEqual(
            0, disagreements.Count,
            $"{disagreements.Count} of {(Subsets - 1) * 2} {family} aggregate results disagree with PostgreSQL:\n"
          + string.Join("\n", disagreements.Take(20))
          + (disagreements.Count > 20 ? $"\n  … and {disagreements.Count - 20} more" : ""));

        string Name(int mask) => $"({string.Join(", ", Indices(mask).Select(index => universe[index].Name))})";

        static string Show(TRange? range) => range?.ToString() ?? "NULL";

        static IEnumerable<int> Indices(int mask) =>
            Enumerable.Range(0, UniverseSize).Where(index => (mask & (1 << index)) != 0);
    }
}

using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Executes the translated SQL against live PostgreSQL and asserts that the server's
/// answers agree with the in-memory implementations — the core promise of the library.
/// Each test seeds its own rows (unique Ids / GroupKeys) so tests can run in parallel.
/// </summary>
[TestClass]
public class ExecutedQueryTests
{
    private static async Task Seed(params Reservation[] rows)
    {
        await using var context = new IntegrationDbContext();
        context.Reservations.AddRange(rows);
        await context.SaveChangesAsync();
    }

    [TestMethod]
    public async Task ContainmentAndOverlap_ReturnExpectedRows()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(
            new Reservation { Id = 2001, Period = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30)) },
            new Reservation { Id = 2002, Period = DateRange.CreateFinite(new DateOnly(2024, 7, 1), new DateOnly(2024, 12, 31)) },
            new Reservation { Id = 2003, Period = DateRange.Empty });

        var day = new DateOnly(2024, 3, 15);
        var mid = DateRange.CreateFinite(new DateOnly(2024, 6, 1), new DateOnly(2024, 7, 31));

        await using var context = new IntegrationDbContext();

        var containsDay = await context.Reservations
            .Where(r => r.Id >= 2001 && r.Id <= 2003 && r.Period.Contains(day))
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 2001 }, containsDay);

        var overlapsMid = await context.Reservations
            .Where(r => r.Id >= 2001 && r.Id <= 2003 && r.Period.Overlaps(mid))
            .Select(r => r.Id)
            .OrderBy(id => id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 2001, 2002 }, overlapsMid);
    }

    [TestMethod]
    public async Task OrderByLowerBound_SortsByRangeStart()
    {
        ContainerLifecycle.RequireDatabase();

        await Seed(
            new Reservation { Id = 2011, Seats = Int32Range.CreateFinite(50, 60) },
            new Reservation { Id = 2012, Seats = Int32Range.CreateFinite(10, 20) },
            new Reservation { Id = 2013, Seats = Int32Range.CreateFinite(30, 40) });

        await using var context = new IntegrationDbContext();

        var ordered = await context.Reservations
            .Where(r => r.Id >= 2011 && r.Id <= 2013)
            .OrderBy(r => r.Seats.LowerBound())
            .Select(r => r.Id)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 2012, 2013, 2011 }, ordered);
    }

    [TestMethod]
    public async Task MergeAndBoundAccessors_MatchInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var period  = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31));
        var operand = DateRange.CreateFinite(new DateOnly(2024, 9, 1), new DateOnly(2024, 9, 30));
        await Seed(new Reservation { Id = 2021, Period = period });

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 2021)
            .Select(r => new
            {
                Merged   = r.Period.Merge(operand),
                Lower    = r.Period.LowerBound(),
                Upper    = r.Period.UpperBound(),
                LowerInc = r.Period.LowerBoundInclusive(),
                UpperInc = r.Period.UpperBoundInclusive()
            })
            .SingleAsync();

        Assert.AreEqual(period.Merge(operand), server.Merged);
        Assert.AreEqual(period.LowerBound(), server.Lower);
        Assert.AreEqual(period.UpperBound(), server.Upper);
        Assert.AreEqual(period.LowerBoundInclusive(), server.LowerInc);
        Assert.AreEqual(period.UpperBoundInclusive(), server.UpperInc);
    }

    [TestMethod]
    public async Task SetOperations_MatchInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var period  = Int32Range.CreateFinite(1, 10);
        var operand = Int32Range.CreateFinite(5, 15);
        await Seed(new Reservation { Id = 2031, Seats = period });

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 2031)
            .Select(r => new
            {
                Intersection = r.Seats.Intersect(operand),
                Union        = r.Seats.Union(operand),
                Difference   = r.Seats.Except(operand)
            })
            .SingleAsync();

        Assert.AreEqual(period.Intersect(operand), server.Intersection);
        Assert.AreEqual(period.Union(operand), server.Union);
        Assert.AreEqual(period.Except(operand), server.Difference);
    }

    /// <summary>
    /// Adjacency against the server that defines it, across the shape pairs involving an
    /// unbounded operand. The model answered <see langword="false"/> for every pair whose
    /// *receiver* was unbounded while PostgreSQL answered <see langword="true"/>; this pins the
    /// agreement in both directions.
    /// </summary>
    [TestMethod]
    public async Task RangeAdjacency_UnboundedShapes_MatchPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        var openStart = Int32Range.CreateUnboundedStart(0, true);  // (-∞, 0]
        var openEnd   = Int32Range.CreateUnboundedEnd(4);          // [4, +∞)
        var meeting   = Int32Range.CreateUnboundedEnd(1);          // [1, +∞)
        var finite    = Int32Range.CreateFinite(1, 3);             // [1, 3]

        await Seed(
            new Reservation { Id = 2091, Seats = openStart },
            new Reservation { Id = 2092, Seats = openEnd },
            new Reservation { Id = 2093, Seats = finite });

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id >= 2091 && r.Id <= 2093)
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.Seats,
                ToFinite  = r.Seats.IsAdjacentTo(finite),
                ToOpenEnd = r.Seats.IsAdjacentTo(openEnd),
                ToMeeting = r.Seats.IsAdjacentTo(meeting)
            })
            .ToListAsync();

        foreach (var row in server)
        {
            Assert.AreEqual(row.Seats.IsAdjacentTo(finite), row.ToFinite, $"'{row.Seats}' -|- '{finite}'");
            Assert.AreEqual(row.Seats.IsAdjacentTo(openEnd), row.ToOpenEnd, $"'{row.Seats}' -|- '{openEnd}'");
            Assert.AreEqual(row.Seats.IsAdjacentTo(meeting), row.ToMeeting, $"'{row.Seats}' -|- '{meeting}'");
        }

        // The three that used to disagree, pinned explicitly against the server's answers.
        var fromOpenStart = server.Single(row => row.Id == 2091);
        Assert.IsTrue(fromOpenStart.ToFinite, "(-∞,0] -|- [1,3]");
        Assert.IsTrue(fromOpenStart.ToMeeting, "(-∞,0] -|- [1,+∞)");
        Assert.IsFalse(fromOpenStart.ToOpenEnd, "(-∞,0] and [4,+∞) leave a gap");

        var fromOpenEnd = server.Single(row => row.Id == 2092);
        Assert.IsTrue(fromOpenEnd.ToFinite, "[4,+∞) -|- [1,3]");
    }

    /// <summary>
    /// The <c>&lt;&lt;</c> / <c>&gt;&gt;</c> counterpart of the adjacency test above, for the same
    /// reason: these are receiver-shaped predicates, and the unbounded shapes are where a
    /// receiver-vs-operand asymmetry hides.
    /// </summary>
    /// <remarks>
    /// <c>&lt;&lt;</c> compares the receiver's upper bound with the operand's lower bound, so an
    /// <c>UnboundedStart</c> receiver is decided by its finite upper bound. Until 7.0.0 the
    /// in-memory implementation answered <see langword="false"/> for every such receiver while the
    /// server answered <see langword="true"/> — a disagreement invisible to the translation tests,
    /// which only assert that <c>&lt;&lt;</c> is emitted.
    /// </remarks>
    [TestMethod]
    public async Task UnboundedStrictlyLeftRightOf_MatchesPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        var openStart = Int32Range.CreateUnboundedStart(5);        // (-∞, 5]
        var openEnd   = Int32Range.CreateUnboundedEnd(10);         // [10, +∞)
        var finiteLow = Int32Range.CreateFinite(1, 5);             // [1, 5]
        var probe     = Int32Range.CreateFinite(10, 20);           // [10, 20]

        await Seed(
            new Reservation { Id = 2101, Seats = openStart },
            new Reservation { Id = 2102, Seats = openEnd },
            new Reservation { Id = 2103, Seats = finiteLow },
            new Reservation { Id = 2104, Seats = Int32Range.Infinite },
            new Reservation { Id = 2105, Seats = Int32Range.Empty });

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id >= 2101 && r.Id <= 2105)
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                r.Seats,
                LeftOfProbe   = r.Seats.IsStrictlyLeftOf(probe),
                RightOfProbe  = r.Seats.IsStrictlyRightOf(probe),
                LeftOfOpenEnd = r.Seats.IsStrictlyLeftOf(openEnd),
                ProbeLeftOfIt = probe.IsStrictlyLeftOf(r.Seats)
            })
            .ToListAsync();

        foreach (var row in server)
        {
            Assert.AreEqual(row.Seats.IsStrictlyLeftOf(probe), row.LeftOfProbe, $"'{row.Seats}' << '{probe}'");
            Assert.AreEqual(row.Seats.IsStrictlyRightOf(probe), row.RightOfProbe, $"'{row.Seats}' >> '{probe}'");
            Assert.AreEqual(row.Seats.IsStrictlyLeftOf(openEnd), row.LeftOfOpenEnd, $"'{row.Seats}' << '{openEnd}'");
            Assert.AreEqual(probe.IsStrictlyLeftOf(row.Seats), row.ProbeLeftOfIt, $"'{probe}' << '{row.Seats}'");
        }

        // The cases that used to disagree, pinned against the server's answers rather than
        // against the loop, which would agree with a wrong implementation on both sides.
        var fromOpenStart = server.Single(row => row.Id == 2101);
        Assert.IsTrue(fromOpenStart.LeftOfProbe, "(-∞,5] << [10,20]");
        Assert.IsTrue(fromOpenStart.LeftOfOpenEnd, "(-∞,5] << [10,+∞)");
        Assert.IsFalse(fromOpenStart.ProbeLeftOfIt, "nothing is strictly left of an unbounded start");

        // …and the ones that must stay false: no upper bound on the receiver.
        Assert.IsFalse(server.Single(row => row.Id == 2102).LeftOfProbe, "[10,+∞) << [10,20]");
        Assert.IsFalse(server.Single(row => row.Id == 2104).LeftOfProbe, "(,) << [10,20]");
        Assert.IsFalse(server.Single(row => row.Id == 2105).LeftOfProbe, "empty << [10,20]");

        // The mirror: [1,5] is strictly right of nothing here, but [10,20] is right of it.
        Assert.IsTrue(server.Single(row => row.Id == 2103).ProbeLeftOfIt is false, "[10,20] is not << [1,5]");
        Assert.IsTrue(server.Single(row => row.Id == 2103).LeftOfProbe, "[1,5] << [10,20]");
    }

    [TestMethod]
    public async Task MultirangeComparisons_MatchInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        // Three elements so that adjacency against the interior gap is observable:
        // PostgreSQL (and the in-memory implementation) consider outermost elements only.
        var seatBlocks = RangeSet<Int32Range, int>.From([
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(7, 9),
            Int32Range.CreateFinite(20, 22)
        ]);
        await Seed(new Reservation { Id = 2041, SeatBlocks = seatBlocks });

        var interiorAdjacent = Int32Range.CreateFinite(10, 12); // touches only the interior [7,9]
        var outerAdjacent    = Int32Range.CreateFinite(23, 25); // attaches after the last element
        var subset           = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(2, 3), Int32Range.CreateFinite(20, 21)]);
        var disjoint         = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(30, 40)]);

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 2041)
            .Select(r => new
            {
                InteriorAdjacent = r.SeatBlocks.IsAdjacentTo(interiorAdjacent),
                OuterAdjacent    = r.SeatBlocks.IsAdjacentTo(outerAdjacent),
                ContainsSubset   = r.SeatBlocks.Contains(subset),
                OverlapsDisjoint = r.SeatBlocks.Overlaps(disjoint),
                StrictlyLeft     = r.SeatBlocks.IsStrictlyLeftOf(Int32Range.CreateFinite(30, 40)),
                NotExtendRight   = r.SeatBlocks.DoesNotExtendRightOf(Int32Range.CreateFinite(1, 22)),
                Empty            = r.SeatBlocks.IsEmpty(),
                LowerInf         = r.SeatBlocks.IsUnboundedStart(),
                Complement       = r.SeatBlocks.Complement()
            })
            .SingleAsync();

        Assert.AreEqual(seatBlocks.IsAdjacentTo(interiorAdjacent), server.InteriorAdjacent);
        Assert.IsFalse(server.InteriorAdjacent); // the interesting case, pinned explicitly
        Assert.AreEqual(seatBlocks.IsAdjacentTo(outerAdjacent), server.OuterAdjacent);
        Assert.IsTrue(server.OuterAdjacent);
        Assert.AreEqual(seatBlocks.Contains(subset), server.ContainsSubset);
        Assert.AreEqual(seatBlocks.Overlaps(disjoint), server.OverlapsDisjoint);
        Assert.AreEqual(seatBlocks.IsStrictlyLeftOf(Int32Range.CreateFinite(30, 40)), server.StrictlyLeft);
        Assert.AreEqual(seatBlocks.DoesNotExtendRightOf(Int32Range.CreateFinite(1, 22)), server.NotExtendRight);
        Assert.AreEqual(seatBlocks.IsEmpty(), server.Empty);
        Assert.AreEqual(seatBlocks.IsUnboundedStart(), server.LowerInf);
        Assert.AreEqual(seatBlocks.Complement(), server.Complement);

        var byEquality = await context.Reservations
            .Where(r => r.Id == 2041 && r.SeatBlocks == seatBlocks)
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 2041 }, byEquality);
    }

    /// <summary>
    /// The shape predicates over a multirange column, against the server that defines them.
    /// The row that matters is 2072: unbounded at both ends with a gap in the middle, so
    /// <c>lower_inf AND upper_inf</c> — the translation that is correct for a single range —
    /// answers true while the set does not cover the domain.
    /// </summary>
    [TestMethod]
    public async Task MultirangeShapePredicates_MatchInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var infinite = RangeSet<Int32Range, int>.Infinite;

        var gapped = RangeSet<Int32Range, int>.From([
            Int32Range.CreateUnboundedStart(5, true),
            Int32Range.CreateUnboundedEnd(10)
        ]);

        var bounded = RangeSet<Int32Range, int>.From([
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(20, 22)
        ]);

        var empty = RangeSet<Int32Range, int>.Empty;

        await Seed(
            new Reservation { Id = 2071, SeatBlocks = infinite },
            new Reservation { Id = 2072, SeatBlocks = gapped },
            new Reservation { Id = 2073, SeatBlocks = bounded },
            new Reservation { Id = 2074, SeatBlocks = empty });

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id >= 2071 && r.Id <= 2074)
            .OrderBy(r => r.Id)
            .Select(r => new
            {
                r.Id,
                Infinity        = r.SeatBlocks.IsInfinity(),
                Finite          = r.SeatBlocks.IsFinite(),
                UnboundedStart  = r.SeatBlocks.IsUnboundedStart(),
                UnboundedEnd    = r.SeatBlocks.IsUnboundedEnd()
            })
            .ToListAsync();

        var expected = new[] { infinite, gapped, bounded, empty };

        for (var i = 0; i < expected.Length; i++)
        {
            var set = expected[i];
            var row = server[i];

            Assert.AreEqual(set.IsInfinity(), row.Infinity, $"IsInfinity on row {row.Id} ('{set}')");
            Assert.AreEqual(set.IsFinite(), row.Finite, $"IsFinite on row {row.Id} ('{set}')");
            Assert.AreEqual(set.IsUnboundedStart(), row.UnboundedStart, $"IsUnboundedStart on row {row.Id}");
            Assert.AreEqual(set.IsUnboundedEnd(), row.UnboundedEnd, $"IsUnboundedEnd on row {row.Id}");
        }

        // Pinned explicitly: the case that separates full coverage from open at both ends.
        var gappedRow = server.Single(row => row.Id == 2072);
        Assert.IsTrue(gappedRow.UnboundedStart);
        Assert.IsTrue(gappedRow.UnboundedEnd);
        Assert.IsFalse(gappedRow.Infinity, "PostgreSQL agrees the gapped set is not the whole domain");

        var onlyInfinite = await context.Reservations
            .Where(r => r.Id >= 2071 && r.Id <= 2074 && r.SeatBlocks.IsInfinity())
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 2071 }, onlyInfinite);

        var onlyBounded = await context.Reservations
            .Where(r => r.Id >= 2071 && r.Id <= 2074 && r.SeatBlocks.IsFinite())
            .Select(r => r.Id)
            .ToListAsync();
        CollectionAssert.AreEqual(new[] { 2073 }, onlyBounded);
    }

    /// <summary>
    /// <c>IsInfinity</c> composes on a server-computed union, unlike the value sets' <c>Count</c>:
    /// the multirange <c>+</c> operator returns a normalized multirange, where <c>array_cat</c>
    /// merely concatenates, so equality against <c>'{(,)}'</c> stays meaningful through the
    /// composition.
    /// </summary>
    /// <remarks>
    /// The union with the set's own complement is the sharp case: it covers the domain only if
    /// both sides normalize the unbounded halves together. It is also the case that failed while
    /// <c>IsAdjacentTo</c> answered <see langword="false"/> for an unbounded receiver — the server
    /// said <c>{(,)}</c> and the model said <c>{(,0],[1,)}</c>.
    /// </remarks>
    [TestMethod]
    public async Task MultirangeIsInfinity_ComposesOnAServerComputedUnion()
    {
        ContainerLifecycle.RequireDatabase();

        var blocks = RangeSet<Int32Range, int>.From([
            Int32Range.CreateFinite(1, 3),
            Int32Range.CreateFinite(20, 22)
        ]);
        var bridging = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(4, 19)]);

        await Seed(new Reservation { Id = 2081, SeatBlocks = blocks });

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 2081)
            .Select(r => new
            {
                WithComplement = r.SeatBlocks.Union(r.SeatBlocks.Complement()).IsInfinity(),
                WithBridging   = r.SeatBlocks.Union(bridging).IsInfinity(),
                WithItself     = r.SeatBlocks.Union(r.SeatBlocks).IsInfinity(),
                Bridged        = r.SeatBlocks.Union(bridging).Merge()
            })
            .SingleAsync();

        Assert.AreEqual(blocks.Union(blocks.Complement()).IsInfinity(), server.WithComplement);
        Assert.IsTrue(server.WithComplement, "a set unioned with its complement covers the domain");

        // The union collapses three elements into one [1,22] — a real normalization on the
        // server — and IsInfinity still answers over the composed expression.
        Assert.AreEqual(blocks.Union(bridging).IsInfinity(), server.WithBridging);
        Assert.IsFalse(server.WithBridging);
        Assert.AreEqual(blocks.Union(bridging).Merge(), server.Bridged);
        Assert.AreEqual(Int32Range.CreateFinite(1, 22), server.Bridged);

        Assert.AreEqual(blocks.Union(blocks).IsInfinity(), server.WithItself);
        Assert.IsFalse(server.WithItself);
    }

    [TestMethod]
    public async Task UnboundedDoesNotExtend_MatchesPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        // Full &< / &> parity for infinite bounds: +∞ ≤ +∞ and -∞ ≥ -∞ are true.
        var seats = Int32Range.CreateUnboundedEnd(5);        // [5, +∞)
        var price = DecimalRange.CreateUnboundedStart(100m); // (-∞, 100)
        await Seed(new Reservation { Id = 2061, Seats = seats, Price = price });

        var unboundedEnd   = Int32Range.CreateUnboundedEnd(100);
        var infinite       = Int32Range.Infinite;
        var finiteUpper    = Int32Range.CreateFinite(1, 1000);
        var unboundedStart = DecimalRange.CreateUnboundedStart(0m);
        var finiteLower    = DecimalRange.CreateFinite(0m, 1m);

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 2061)
            .Select(r => new
            {
                UpperInfVsInf    = r.Seats.DoesNotExtendRightOf(unboundedEnd),
                UpperInfVsWhole  = r.Seats.DoesNotExtendRightOf(infinite),
                UpperInfVsFinite = r.Seats.DoesNotExtendRightOf(finiteUpper),
                LowerInfVsInf    = r.Price.DoesNotExtendLeftOf(unboundedStart),
                LowerInfVsFinite = r.Price.DoesNotExtendLeftOf(finiteLower)
            })
            .SingleAsync();

        Assert.AreEqual(seats.DoesNotExtendRightOf(unboundedEnd), server.UpperInfVsInf);
        Assert.IsTrue(server.UpperInfVsInf); // the parity case: previously false in-memory
        Assert.AreEqual(seats.DoesNotExtendRightOf(infinite), server.UpperInfVsWhole);
        Assert.AreEqual(seats.DoesNotExtendRightOf(finiteUpper), server.UpperInfVsFinite);
        Assert.IsFalse(server.UpperInfVsFinite);
        Assert.AreEqual(price.DoesNotExtendLeftOf(unboundedStart), server.LowerInfVsInf);
        Assert.IsTrue(server.LowerInfVsInf);
        Assert.AreEqual(price.DoesNotExtendLeftOf(finiteLower), server.LowerInfVsFinite);
        Assert.IsFalse(server.LowerInfVsFinite);
    }

    [TestMethod]
    public async Task Aggregates_MatchInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var periods = new[]
        {
            DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31)),
            DateRange.CreateFinite(new DateOnly(2024, 3, 1), new DateOnly(2024, 5, 31)),
            DateRange.CreateFinite(new DateOnly(2024, 9, 1), new DateOnly(2024, 9, 30))
        };
        await Seed(
            new Reservation { Id = 2051, GroupKey = 42, Period = periods[0] },
            new Reservation { Id = 2052, GroupKey = 42, Period = periods[1] },
            new Reservation { Id = 2053, GroupKey = 42, Period = periods[2] });

        await using var context = new IntegrationDbContext();

        var serverAgg = await context.Reservations
            .Where(r => r.GroupKey == 42)
            .GroupBy(r => r.GroupKey)
            .Select(g => g.Select(r => r.Period).RangeAgg())
            .SingleAsync();
        Assert.AreEqual(periods.RangeAgg(), serverAgg);

        var serverIntersectAgg = await context.Reservations
            .Where(r => r.GroupKey == 42 && r.Id != 2053)
            .GroupBy(r => r.GroupKey)
            .Select(g => g.Select(r => r.Period).RangeIntersectAgg())
            .SingleAsync();
        Assert.AreEqual(periods.Take(2).RangeIntersectAgg(), serverIntersectAgg);
    }
}

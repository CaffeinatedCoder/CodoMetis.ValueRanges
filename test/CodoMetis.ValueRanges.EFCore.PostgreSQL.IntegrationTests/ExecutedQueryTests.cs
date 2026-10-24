using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Executes the translated SQL against live PostgreSQL and asserts that the server's
/// answers agree with the in-memory implementations — the core promise of the library.
/// Each test seeds its own rows (unique Ids / GroupKeys) so tests can run in parallel.
/// The two sweeps at the end collect their disagreements and end in <see cref="Candidates.Verdict"/>.
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
    public async Task GapAndGaps_MatchInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        DateOnly June(int day) => new(2025, 6, day);

        var period  = DateRange.CreateFinite(June(1), June(8));
        var blocked = RangeSet<DateRange, DateOnly>.From(
            [DateRange.CreateFinite(June(3), June(5)), DateRange.CreateFinite(June(20), June(25))]);
        await Seed(new Reservation { Id = 2025, Period = period, BlockedDays = blocked });

        var later    = DateRange.CreateFinite(June(12), June(15));
        var touching = DateRange.CreateFinite(June(9), June(10));
        var window   = DateRange.CreateFinite(June(1), June(30));

        await using var context = new IntegrationDbContext();

        var server = await context.Reservations
            .Where(r => r.Id == 2025)
            .Select(r => new
            {
                Gap          = r.Period.Gap(later),
                Reversed     = later.Gap(r.Period),
                Adjacent     = r.Period.Gap(touching),
                Gaps         = r.BlockedDays.Gaps(),
                InWindow     = r.BlockedDays.Gaps(window),
                FreeInPeriod = r.BlockedDays.Gaps(r.Period),
                HasGapLater  = r.Period.Gap(later).IsEmpty()
            })
            .SingleAsync();

        Assert.AreEqual(period.Gap(later), server.Gap);
        Assert.AreEqual(DateRange.CreateFinite(June(9), June(11)), server.Gap);
        Assert.AreEqual(later.Gap(period), server.Reversed);
        Assert.AreEqual(DateRange.Empty, server.Adjacent);
        Assert.AreEqual(blocked.Gaps(), server.Gaps);
        Assert.AreEqual(blocked.Gaps(window), server.InWindow);
        Assert.AreEqual(blocked.Gaps(period), server.FreeInPeriod);
        Assert.IsFalse(server.HasGapLater);

        // As a predicate too: the rows with a free stretch between the stay and a later one.
        var withGap = await context.Reservations
            .Where(r => r.Id == 2025 && !r.Period.Gap(later).IsEmpty())
            .Select(r => r.Id)
            .ToListAsync();

        CollectionAssert.AreEqual(new[] { 2025 }, withGap);
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
    /// The shape predicates over a range column, for every shape, against the server that defines
    /// <c>lower_inf</c> and <c>upper_inf</c>. The row that matters is the infinite one: until 10.0.0
    /// <c>IsUnboundedStart()</c> and <c>IsUnboundedEnd()</c> tested the variant and answered
    /// <see langword="false"/> for <c>(,)</c>, while the translation, <c>lower_inf</c>, answered
    /// <see langword="true"/> (#109). The memory answer is asserted against the server's row by row,
    /// and the infinite row is pinned to <see langword="true"/> so the two sides cannot agree by both
    /// being wrong.
    /// </summary>
    [TestMethod]
    public async Task RangeShapePredicates_MatchInMemory()
    {
        ContainerLifecycle.RequireDatabase();

        var shapes = new Dictionary<int, Int32Range>
        {
            [2111] = Int32Range.Empty,
            [2112] = Int32Range.Infinite,
            [2113] = Int32Range.CreateFinite(1, 5),
            [2114] = Int32Range.CreateUnboundedStart(5),
            [2115] = Int32Range.CreateUnboundedEnd(10)
        };

        await Seed([.. shapes.Select(pair => new Reservation { Id = pair.Key, Seats = pair.Value })]);

        await using var context = new IntegrationDbContext();

        var rows = await context.Reservations
            .Where(r => r.Id >= 2111 && r.Id <= 2115)
            .Select(r => new
            {
                r.Id,
                Empty          = r.Seats.IsEmpty(),
                UnboundedStart = r.Seats.IsUnboundedStart(),
                UnboundedEnd   = r.Seats.IsUnboundedEnd(),
                Infinity       = r.Seats.IsInfinity(),
                Finite         = r.Seats.IsFinite()
            })
            .ToListAsync();

        Assert.AreEqual(shapes.Count, rows.Count);
        foreach (var row in rows)
        {
            var range = shapes[row.Id];
            Assert.AreEqual(range.IsEmpty(),          row.Empty,          $"IsEmpty on {range}");
            Assert.AreEqual(range.IsUnboundedStart(), row.UnboundedStart, $"IsUnboundedStart on {range}");
            Assert.AreEqual(range.IsUnboundedEnd(),   row.UnboundedEnd,   $"IsUnboundedEnd on {range}");
            Assert.AreEqual(range.IsInfinity(),       row.Infinity,       $"IsInfinity on {range}");
            Assert.AreEqual(range.IsFinite(),         row.Finite,         $"IsFinite on {range}");
        }

        var infinite = rows.Single(row => row.Id == 2112);
        Assert.IsTrue(infinite.UnboundedStart, "lower_inf('(,)') is true");
        Assert.IsTrue(infinite.UnboundedEnd, "upper_inf('(,)') is true");

        var unboundedStartRows = await context.Reservations
            .Where(r => r.Id >= 2111 && r.Id <= 2115 && r.Seats.IsUnboundedStart())
            .Select(r => r.Id)
            .OrderBy(id => id)
            .ToListAsync();
        CollectionAssert.AreEqual(
            shapes.Where(pair => pair.Value.IsUnboundedStart()).Select(pair => pair.Key).Order().ToList(),
            unboundedStartRows,
            "the rows a Where(IsUnboundedStart) returns are the rows memory selects");
        CollectionAssert.AreEqual(new[] { 2112, 2114 }, unboundedStartRows);
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

    /// <summary>
    /// <c>LowerBound()</c> and <c>UpperBound()</c> compared with <see langword="null"/>, read through
    /// <c>HasValue</c>, coalesced with <c>??</c> in a filter and a projection, and filtered after being
    /// projected, for a range column and a multirange column of every family, against the same
    /// expression over the rows as loaded. The bounds are NULL for the empty range and for the unbounded
    /// side of a non-null one, so these forms only agree while EF Core knows <c>lower</c> and
    /// <c>upper</c> can be NULL for a non-null argument (#349). Each failure line starts with the
    /// operation, then the column. Ids run from 2400 to 2659, twenty per family.
    /// </summary>
    [TestMethod]
    public async Task BoundsComparedWithNull_SelectTheRowsMemorySelects()
    {
        ContainerLifecycle.RequireDatabase();

        var at = new DateTime(2026, 3, 1, 8, 0, 0);
        var utc = new DateTimeOffset(at, TimeSpan.Zero);
        LocalDate Day(int day) => new(2026, 3, day);

        var failures = new List<string>();
        failures.AddRange(await BoundNullSweep(2400, r => r.Seats, r => r.SeatBlocks, 1, 10, 20, int.MinValue, int.MaxValue));
        failures.AddRange(await BoundNullSweep(2420, r => r.Tickets, r => r.TicketBlocks, 1L, 10L, 20L, long.MinValue, long.MaxValue));
        failures.AddRange(await BoundNullSweep(2440, r => r.Price, r => r.PriceBands, 1.5m, 10m, 20.25m, decimal.MinValue, decimal.MaxValue));
        failures.AddRange(await BoundNullSweep(2460, r => r.Period, r => r.BlockedDays, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15),
                                               new DateOnly(2026, 3, 31), DateOnly.MinValue, DateOnly.MaxValue));
        failures.AddRange(await BoundNullSweep(2480, r => r.LocalTime, r => r.WallClockWindows, at, at.AddDays(1), at.AddDays(2),
                                               DateTime.MinValue, DateTime.MaxValue));
        failures.AddRange(await BoundNullSweep(2500, r => r.InstantTime, r => r.InstantWindows, utc, utc.AddDays(1), utc.AddDays(2),
                                               DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
        failures.AddRange(await BoundNullSweep(2520, r => r.NodaPeriod, r => r.NodaBlockedDays, Day(1), Day(15), Day(31),
                                               LocalDate.MinIsoValue, LocalDate.MaxIsoValue));
        failures.AddRange(await BoundNullSweep(2540, r => r.NodaWallClock, r => r.NodaWallClocks, LocalDateTime.FromDateTime(at),
                                               LocalDateTime.FromDateTime(at.AddDays(1)), LocalDateTime.FromDateTime(at.AddDays(2)),
                                               LocalDateTime.MinIsoValue, LocalDateTime.MaxIsoValue));
        failures.AddRange(await BoundNullSweep(2560, r => r.NodaWindow, r => r.NodaWindows, Instant.FromDateTimeOffset(utc),
                                               Instant.FromDateTimeOffset(utc.AddDays(1)), Instant.FromDateTimeOffset(utc.AddDays(2)),
                                               Instant.MinValue, Instant.MaxValue));
        // time is microsecond-precise, so TimeOnly.MaxValue would round to 24:00; any fallback the column holds will do.
        failures.AddRange(await BoundNullSweep(2580, r => r.OpeningHours, r => r.OpeningWindows, new TimeOnly(9, 0), new TimeOnly(12, 0),
                                               new TimeOnly(17, 0), TimeOnly.MinValue, new TimeOnly(23, 59, 59)));
        failures.AddRange(await BoundNullSweep(2600, r => r.Score, r => r.ScoreBands, 1.5, 10.0, 20.25, double.MinValue, double.MaxValue));
        failures.AddRange(await BoundNullSweep(2620, r => r.Load, r => r.LoadBands, 1.5f, 10f, 20.25f, float.MinValue, float.MaxValue));
        failures.AddRange(await BoundNullSweep(2640, r => r.BillingPeriod, r => r.BillingPeriods, new YearMonth(2026, 1), new YearMonth(2026, 6),
                                               new YearMonth(2026, 12), new YearMonth(1, 1), new YearMonth(9999, 12)));

        Candidates.Verdict(nameof(ExecutedQueryTests), failures,
                           "LowerBound()/UpperBound() against null in a query: PostgreSQL and memory select different rows",
                           method: nameof(BoundsComparedWithNull_SelectTheRowsMemorySelects));
    }

    /// <summary>
    /// Seeds one row per shape into <paramref name="column"/>, and the same shape as a one-element
    /// multirange into <paramref name="set"/> — finite across, below and above <paramref name="v"/>,
    /// unbounded on either side of it, infinite and empty — and runs every bound operation on both
    /// columns, on the server and over the rows as loaded. <paramref name="min"/> and
    /// <paramref name="max"/> are the <c>??</c> fallbacks; they must round-trip through the column's
    /// element type, or the projection disagrees for that reason alone.
    /// </summary>
    private static async Task<List<string>> BoundNullSweep<TRange, T>(
        int firstId, Expression<Func<Reservation, TRange>> column, Expression<Func<Reservation, RangeSet<TRange, T>>> set,
        T a, T v, T b, T min, T max)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        (string Name, TRange Range)[] shapes =
        [
            ("[a,b]", TRange.CreateFinite(a, b, true, true)),
            ("[a,a]", TRange.CreateFinite(a, a, true, true)),
            ("[b,b]", TRange.CreateFinite(b, b, true, true)),
            ("(,a]",  TRange.CreateUnboundedStart(a, true)),
            ("(,b]",  TRange.CreateUnboundedStart(b, true)),
            ("[a,)",  TRange.CreateUnboundedEnd(a, true)),
            ("[b,)",  TRange.CreateUnboundedEnd(b, true)),
            ("(,)",   TRange.Infinite),
            ("empty", TRange.Empty)
        ];

        var property    = (PropertyInfo)((MemberExpression)column.Body).Member;
        var setProperty = (PropertyInfo)((MemberExpression)set.Body).Member;
        var names       = new Dictionary<int, string>();
        var rows        = new List<Reservation>();
        foreach (var (shape, index) in shapes.Select((shape, index) => (shape, index)))
        {
            var row = new Reservation { Id = firstId + index };
            property.SetValue(row, shape.Range);
            setProperty.SetValue(row, RangeSet<TRange, T>.From(shape.Range));
            rows.Add(row);
            names[row.Id] = shape.Name;
        }

        await Seed([.. rows]);

        var row0      = column.Parameters[0];
        var setColumn = Expression.Property(row0, setProperty);
        var lastId    = firstId + shapes.Length - 1;

        await using var context = new IntegrationDbContext();
        var mine     = context.Reservations.Where(r => r.Id >= firstId && r.Id <= lastId);
        var loaded   = await mine.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        var failures = new List<string>();

        // The range's bounds are the IRange<T> extensions and the set's its own methods, each spliced
        // onto its column, so the call is the one a consumer's r.Column.LowerBound() makes.
        await Sweep($"{property.Name} ({typeof(TRange).Name})",
                    Bound<IRange<T>>(column.Body, (_, x) => x.LowerBound()), Bound<IRange<T>>(column.Body, (_, x) => x.UpperBound()));
        await Sweep($"{setProperty.Name} (RangeSet<{typeof(TRange).Name}>)",
                    Bound<RangeSet<TRange, T>>(setColumn, (_, x) => x.LowerBound()), Bound<RangeSet<TRange, T>>(setColumn, (_, x) => x.UpperBound()));

        return failures;

        Expression<Func<Reservation, T?>> Bound<TColumn>(Expression on, Expression<Func<Reservation, TColumn, T?>> bound) =>
            Expression.Lambda<Func<Reservation, T?>>(FuzzEngine.Splice(bound, row0, on), row0);

        // The operation written over the bound, spliced onto it.
        Expression<Func<Reservation, TOut>> Over<TOut>(Expression<Func<Reservation, T?>> bound, Expression<Func<Reservation, T?, TOut>> operation) =>
            Expression.Lambda<Func<Reservation, TOut>>(FuzzEngine.Splice(operation, row0, bound.Body), row0);

        // (bound ?? fallback) op v, with both values captured, so EF Core sends them as parameters.
        Expression<Func<Reservation, bool>> Coalesced(ExpressionType comparison, Expression<Func<Reservation, T?>> bound, T fallback) =>
            Expression.Lambda<Func<Reservation, bool>>(
                Expression.MakeBinary(comparison, Expression.Coalesce(bound.Body, Captured(fallback)), Captured(v)), row0);

        async Task Sweep(string label, Expression<Func<Reservation, T?>> lower, Expression<Func<Reservation, T?>> upper)
        {
            (string Name, Expression<Func<Reservation, bool>> Filter)[] filters =
            [
                ("LowerBound() == null",       Over(lower, (_, x) => x == null)),
                ("LowerBound() != null",       Over(lower, (_, x) => x != null)),
                ("LowerBound().HasValue",      Over(lower, (_, x) => x.HasValue)),
                ("UpperBound() == null",       Over(upper, (_, x) => x == null)),
                ("UpperBound() != null",       Over(upper, (_, x) => x != null)),
                ("(UpperBound() ?? max) >= v", Coalesced(ExpressionType.GreaterThanOrEqual, upper, max)),
                ("(LowerBound() ?? min) <= v", Coalesced(ExpressionType.LessThanOrEqual, lower, min))
            ];

            foreach (var (name, filter) in filters)
            {
                var memory = loaded.Where(filter.Compile()).Select(r => r.Id).ToList();
                await Agree(label, name, () => mine.Where(filter).Select(r => r.Id).OrderBy(id => id).ToListAsync(), memory);
            }

            var projection = Expression.Lambda<Func<Reservation, T>>(Expression.Coalesce(upper.Body, Captured(max)), row0);
            var projected  = loaded.Select(projection.Compile()).ToList();
            try
            {
                var server = await mine.OrderBy(r => r.Id).Select(projection).ToListAsync();
                if (!server.SequenceEqual(projected))
                    failures.Add($"Select(UpperBound() ?? max) on {label}: server [{string.Join(", ", server)}], memory [{string.Join(", ", projected)}]");
            }
            catch (Exception e) when (e is InvalidOperationException or DbException)
            {
                failures.Add($"Select(UpperBound() ?? max) on {label}: server threw {e.GetType().Name}: {e.Message}");
            }

            // The filter a grid's "blank" header filter sends: the bound projected first, compared after.
            var withUpper = Over(upper, (r, x) => new { r.Id, To = x });
            await Agree(label, "Select(new { Id, To = UpperBound() }).Where(To == null)",
                        () => mine.Select(withUpper).Where(t => t.To == null).Select(t => t.Id).OrderBy(id => id).ToListAsync(),
                        [.. loaded.Select(withUpper.Compile()).Where(t => t.To == null).Select(t => t.Id)]);
        }

        async Task Agree(string label, string operation, Func<Task<List<int>>> server, List<int> memory)
        {
            try
            {
                var rowsServer = await server();
                if (!rowsServer.SequenceEqual(memory))
                    failures.Add($"{operation} on {label}: server rows {Named(rowsServer)}, memory rows {Named(memory)}");
            }
            catch (Exception e) when (e is InvalidOperationException or DbException)
            {
                failures.Add($"{operation} on {label}: server threw {e.GetType().Name}: {e.Message}");
            }
        }

        string Named(IEnumerable<int> ids) => $"{{{string.Join(" ", ids.Select(id => names[id]))}}}";
    }

    /// <summary>
    /// Range predicates over a nullable column holding NULL and every shape, against the same
    /// expression over the rows as loaded. On a NULL column every call is NULL in SQL; in memory some
    /// extensions answer a value on a null receiver, so their negated and vacuous forms keep or drop the
    /// NULL row where SQL does the opposite (#350). A row whose in-memory evaluation throws has no answer
    /// to compare and is left out on both sides. The column lives in a database of its own, so the
    /// shared schema keeps its required columns.
    /// </summary>
    [TestMethod]
    public async Task PredicatesOverANullableColumn_SelectTheRowsMemorySelects()
    {
        ContainerLifecycle.RequireServer();

        var connectionString = await NullableRangeContext.CreateDatabase();

        DateOnly March(int day) => new(2026, 3, day);
        (string Name, DateRange? Range)[] shapes =
        [
            ("null",    null),
            ("empty",   DateRange.Empty),
            ("(,)",     DateRange.Infinite),
            ("[1,10]",  DateRange.CreateFinite(March(1), March(10), true, true)),
            ("[20,25]", DateRange.CreateFinite(March(20), March(25), true, true)),
            ("(,10]",   DateRange.CreateUnboundedStart(March(10), true)),
            ("[20,)",   DateRange.CreateUnboundedEnd(March(20), true))
        ];
        var names = shapes.Select((shape, index) => (Id: index + 1, shape.Name)).ToDictionary(s => s.Id, s => s.Name);

        await using (var seed = new NullableRangeContext(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Rows.AddRange(shapes.Select((shape, index) => new NullableRangeRow { Id = index + 1, Period = shape.Range }));
            await seed.SaveChangesAsync();
        }

        var p = DateRange.CreateFinite(March(5), March(22), true, true);
        var v = March(7);

        (string Name, Expression<Func<NullableRangeRow, bool>> Filter)[] filters =
        [
            ("!IsEmpty()",                  r => !r.Period!.IsEmpty()),
            ("!IsFinite()",                 r => !r.Period!.IsFinite()),
            ("!IsUnboundedEnd()",           r => !r.Period!.IsUnboundedEnd()),
            ("!UpperBoundInclusive()",      r => !r.Period!.UpperBoundInclusive()),
            ("!Overlaps(p)",                r => !r.Period!.Overlaps(p)),
            ("Contains(Empty)",             r => r.Period!.Contains(DateRange.Empty)),
            ("IsContainedBy(Infinite)",     r => r.Period!.IsContainedBy(DateRange.Infinite)),
            ("Intersect(p) != null",        r => r.Period!.Intersect(p) != null),
            ("Intersect(p) == Empty",       r => r.Period!.Intersect(p) == DateRange.Empty),
            ("Gap(Empty).IsEmpty()",        r => r.Period!.Gap(DateRange.Empty).IsEmpty()),
            ("Union(Infinite).Contains(v)", r => r.Period!.Union(DateRange.Infinite).Contains(v)),
            ("Merge(Infinite) == x",        r => r.Period!.Merge(DateRange.Infinite) == r.Period)
        ];

        await using var context = new NullableRangeContext(connectionString);
        var loaded   = await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        var failures = new List<string>();

        foreach (var (name, filter) in filters)
        {
            var test   = filter.Compile();
            var judged = new HashSet<int>();
            var memory = new List<int>();
            foreach (var row in loaded)
            {
                bool holds;
                try { holds = test(row); }
                catch (Exception e) when (e is NullReferenceException or InvalidOperationException or ArgumentException) { continue; }

                judged.Add(row.Id);
                if (holds) memory.Add(row.Id);
            }

            // One line per row, so a candidate names the row it quarantines and every other row stays judged.
            try
            {
                var server = (await context.Rows.Where(filter).Select(r => r.Id).ToListAsync()).ToHashSet();
                foreach (var id in judged.Order().Where(id => server.Contains(id) != memory.Contains(id)))
                    failures.Add($"{name} on DateRange? {names[id]}: server {Selects(server.Contains(id))} the row, memory {Selects(memory.Contains(id))} it");
            }
            catch (Exception e) when (e is InvalidOperationException or DbException)
            {
                failures.Add($"{name} on DateRange?: server threw {e.GetType().Name}: {e.Message}");
            }
        }

        Candidates.Verdict(nameof(ExecutedQueryTests), failures,
                           "Range predicates over a nullable column: PostgreSQL and memory select different rows",
                           method: nameof(PredicatesOverANullableColumn_SelectTheRowsMemorySelects));

        static string Selects(bool selected) => selected ? "selects" : "drops";
    }

    /// <summary>A captured variable, which EF Core sends as a parameter.</summary>
    private static MemberExpression Captured<T>(T value) =>
        Expression.Field(Expression.Constant(new StrongBox<T>(value)), nameof(StrongBox<>.Value));

    public sealed class NullableRangeRow
    {
        public int Id { get; set; }

        public DateRange? Period { get; set; }
    }

    /// <summary>A database of its own, so that a nullable range column does not change the shared schema.</summary>
    private sealed class NullableRangeContext(string connectionString) : DbContext
    {
        public DbSet<NullableRangeRow> Rows => Set<NullableRangeRow>();

        public static async Task<string> CreateDatabase()
        {
            var name = $"nullable_range_{Guid.NewGuid():N}";
            await using (var connection = new NpgsqlConnection(ContainerLifecycle.ServerConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
                await command.ExecuteNonQueryAsync();
            }

            return new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString) { Database = name }.ConnectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<NullableRangeRow>().Property(r => r.Id).ValueGeneratedNever();
    }
}

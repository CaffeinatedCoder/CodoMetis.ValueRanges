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

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Draws random operands for every range, multirange and value set type the packages define and
/// requires three answers to agree on every operation EF Core translates: the in-memory model, the
/// EF translation executed against PostgreSQL, and PostgreSQL's own operator applied to the literals
/// EF writes.
/// </summary>
/// <remarks>
/// <para>
/// The other parity tests sweep shapes and hand-picked edge values. Every value-level defect between
/// memory and the database so far — <c>IsAdjacentTo</c> (6.2.1), <c>IsStrictlyLeftOf</c> and
/// <c>Except</c> (7.0.0), <c>RangeSet.Except</c> with an infinite operand (8.0.0) — hid behind correct
/// SQL and was found only once someone picked the row that showed it. This sweep picks the rows: bounds
/// drawn near the element type's extremes and near each other (equal, adjacent, one unit apart,
/// touching), every shape, empties, multiranges of up to five members, and value sets with
/// duplicates, whitespace, culture-folding and array-literal-hostile strings and extreme temporals.
/// </para>
/// <para>
/// The generator is seeded (<see cref="FuzzRun"/>): CI draws the same rows every run, every failure
/// line carries the seed and row, and <c>VALUERANGES_FUZZ_ROWS</c> / <c>VALUERANGES_FUZZ_SEED</c> widen
/// or move the search locally. A disagreement is a finding: it goes to <c>test/oracles/candidates.json</c>
/// through <see cref="Candidates.Verdict"/> with its issue, keyed on the type, the operation and the
/// shape pair.
/// </para>
/// <para>
/// Two kinds of row are not drawn, because each has its own authority and would otherwise fail on
/// every other line: a temporal value below the store's microsecond (the store rule,
/// <c>TemporalStoreParityTests</c>), and a value PostgreSQL refuses outright at the edge of its domain
/// (<see cref="IsDomainBoundary"/>) — such a row is set aside before the sweep and counted, not
/// compared.
/// </para>
/// </remarks>
[TestClass]
public sealed class DifferentialFuzzParityTests
{
    public TestContext TestContext { get; set; } = null!;

    private const int RangeProbes   = 4;
    private const int ElementProbes = 8;

    [ClassInitialize]
    public static async Task CreateDatabase(TestContext _)
    {
        if (ContainerLifecycle.ConnectionString is null) return;

        await using (var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {FuzzDbContext.DatabaseName}", connection);
            await command.ExecuteNonQueryAsync();
        }

        await using var context = new FuzzDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    private static IRelationalTypeMappingSource Mappings { get; } = new FuzzDbContext().GetService<IRelationalTypeMappingSource>();

    /// <summary>The SQL literal EF Core writes for <paramref name="value"/> as a <typeparamref name="T"/>.</summary>
    private static string Lit<T>(T value) =>
        (Mappings.FindMapping(typeof(T)) ?? throw new InvalidOperationException($"No mapping for {typeof(T)}."))
       .GenerateSqlLiteral(value);

    [TestMethod]
    public async Task Ranges_AgreeWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();
        Assert.IsGreaterThanOrEqualTo(11, FuzzTypes.Ranges.Count, "range type discovery found too few types");

        var method = typeof(DifferentialFuzzParityTests).GetMethod(nameof(SweepRange), BindingFlags.NonPublic | BindingFlags.Static)!;
        var lines  = await RunAll(FuzzTypes.Ranges.Select(t => (Func<Task<List<string>>>)(
                                      () => (Task<List<string>>)method.MakeGenericMethod(t.Range, t.Element).Invoke(null, [TestContext])!)));

        Candidates.Verdict(nameof(DifferentialFuzzParityTests), lines,
            $"Ranges and multiranges disagree with PostgreSQL (seed {FuzzRun.Seed}, {FuzzRun.Rows} rows per type)",
            method: nameof(Ranges_AgreeWithPostgres), requireEveryCandidate: FuzzRun.IsDefaultSeed);
    }

    [TestMethod]
    public async Task ValueSets_AgreeWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();
        Assert.IsGreaterThanOrEqualTo(30, FuzzTypes.Sets.Count, "value set type discovery found too few types");

        var method = typeof(DifferentialFuzzParityTests).GetMethod(nameof(SweepSet), BindingFlags.NonPublic | BindingFlags.Static)!;
        var lines  = await RunAll(FuzzTypes.Sets.Select(t => (Func<Task<List<string>>>)(
                                      () => (Task<List<string>>)method.MakeGenericMethod(t.Set, t.Element).Invoke(null, [TestContext, t.Primitive])!)));

        Candidates.Verdict(nameof(DifferentialFuzzParityTests), lines,
            $"Value sets disagree with PostgreSQL (seed {FuzzRun.Seed}, {FuzzRun.Rows} rows per type)",
            method: nameof(ValueSets_AgreeWithPostgres), requireEveryCandidate: FuzzRun.IsDefaultSeed);
    }

    private static async Task<List<string>> RunAll(IEnumerable<Func<Task<List<string>>>> sweeps)
    {
        var lines = new ConcurrentBag<List<string>>();
        await Parallel.ForEachAsync(sweeps, new ParallelOptions { MaxDegreeOfParallelism = 4 },
                                    async (sweep, _) => lines.Add(await sweep()));
        return [.. lines.SelectMany(l => l).Order(StringComparer.Ordinal)];
    }

    // =========================================================================
    // Ranges and multiranges
    // =========================================================================

    private static string Shape<T>(IRange<T> range) where T : struct, IComparable<T>, IEquatable<T> =>
        range switch
        {
            IEmptyRange<T>          => "Empty",
            IInfinityRange<T>       => "Infinite",
            IFiniteRange<T>         => "Finite",
            IUnboundedStartRange<T> => "UnboundedStart",
            IUnboundedEndRange<T>   => "UnboundedEnd",
            _                       => throw new InvalidOperationException(range.GetType().Name)
        };

    private static string Shape<TRange, T>(RangeSet<TRange, T> set)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => $"Set{set.Count()}";

    private static TRange DrawRange<TRange, T>(Random random, FuzzDomain<T> domain, T? anchor)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var start = anchor is { } a ? domain.Near(random, a) : domain.Draw(random);
        var shape = random.Next(12);
        switch (shape)
        {
            case 0:     return TRange.Empty;
            case 1:     return TRange.Infinite;
            case 2 or 3: return TRange.CreateUnboundedStart(start, random.Next(2) == 0);
            case 4 or 5: return TRange.CreateUnboundedEnd(start, random.Next(2) == 0);
        }

        var end = random.Next(4) == 0 ? domain.Draw(random) : domain.AtOrAbove(random, start);
        if (end.CompareTo(start) < 0) (start, end) = (end, start);
        try
        {
            return TRange.CreateFinite(start, end, random.Next(2) == 0, random.Next(2) == 0);
        }
        catch (ArgumentException)
        {
            return TRange.Empty;
        }
    }

    private static T? AnyBound<T>(Random random, IRange<T> range) where T : struct, IComparable<T>, IEquatable<T> =>
        random.Next(2) == 0 ? range.LowerBound() ?? range.UpperBound() : range.UpperBound() ?? range.LowerBound();

    private static RangeSet<TRange, T> DrawSet<TRange, T>(Random random, FuzzDomain<T> domain, T? anchor)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var members = new List<TRange>();
        var count   = random.Next(6);
        for (var index = 0; index < count; index++)
        {
            var member = DrawRange<TRange, T>(random, domain, anchor);
            members.Add(member);
            if (random.Next(3) > 0) anchor = AnyBound(random, member) ?? anchor;
        }

        return RangeSet<TRange, T>.From(members);
    }

    private static async Task<List<string>> SweepRange<TRange, T>(TestContext test)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var subject = typeof(TRange).Name;
        var random  = FuzzRun.For(subject);
        var domain  = FuzzDomains.For<T>();

        // Draw: the right operand near a bound of the left, the multirange near the range.
        var rows = new List<FuzzRangeRow<TRange, T>>();
        for (var id = 1; id <= FuzzRun.Rows; id++)
        {
            var left     = DrawRange<TRange, T>(random, domain, null);
            var right    = DrawRange<TRange, T>(random, domain, random.Next(10) < 7 ? AnyBound(random, left) : null);
            var leftSet  = DrawSet<TRange, T>(random, domain, AnyBound(random, left));
            var rightSet = DrawSet<TRange, T>(random, domain, random.Next(10) < 7 ? leftSet.LowerBound() ?? leftSet.UpperBound() : null);
            rows.Add(new FuzzRangeRow<TRange, T> { Id = id, Left = left, Right = right, LeftSet = leftSet, RightSet = rightSet });
        }

        var rangeProbes   = Enumerable.Range(0, RangeProbes).Select(_ => DrawRange<TRange, T>(random, domain, AnyBound(random, rows[random.Next(rows.Count)].Left))).ToList();
        var setProbes     = Enumerable.Range(0, RangeProbes).Select(_ => DrawSet<TRange, T>(random, domain, AnyBound(random, rows[random.Next(rows.Count)].Left))).ToList();
        var elementProbes = Enumerable.Range(0, ElementProbes).Select(_ => random.Next(4) == 0
                                                                                 ? domain.Draw(random)
                                                                                 : domain.Near(random, AnyBound(random, rows[random.Next(rows.Count)].Left) ?? domain.Draw(random))).ToList();

        // Set aside what PostgreSQL cannot represent at all (the documented domain boundary).
        var refused = await Refused(rows.SelectMany(r => new[] { Lit(r.Left), Lit(r.Right), Lit(r.LeftSet), Lit(r.RightSet) })
                                        .Concat(rangeProbes.Select(Lit)).Concat(setProbes.Select(Lit))
                                        .Concat(elementProbes.Select(e => ElementSql<TRange, T>(e))));
        var failures = refused.Where(r => !IsDomainBoundary(r.SqlState))
                              .Select(r => $"{subject} literal refused: {r.Literal} → {r.SqlState} {r.Message}").ToList();

        bool Accepted(string literal) => !refused.Any(r => r.Literal == literal);
        var kept = rows.Where(r => Accepted(Lit(r.Left)) && Accepted(Lit(r.Right)) && Accepted(Lit(r.LeftSet)) && Accepted(Lit(r.RightSet))).ToList();
        rangeProbes   = [.. rangeProbes.Where(p => Accepted(Lit(p)))];
        setProbes     = [.. setProbes.Where(p => Accepted(Lit(p)))];
        elementProbes = [.. elementProbes.Where(p => Accepted(ElementSql<TRange, T>(p)))];

        test.WriteLine($"{subject}: {kept.Count} of {rows.Count} rows kept; {rows.Count - kept.Count} set aside at the domain boundary.");

        await using (var write = new FuzzDbContext())
        {
            write.AddRange(kept);
            await write.SaveChangesAsync();
        }

        bool Edge(IRange<T> range) => range.LowerBound() is { } l && domain.IsExtreme(l) || range.UpperBound() is { } u && domain.IsExtreme(u);
        bool SetEdge(RangeSet<TRange, T> set) => set.Any(Edge);

        var multirange = Mappings.FindMapping(typeof(RangeSet<TRange, T>))!.StoreType;
        string Lift(string range) => $"{multirange}({range})";

        var r2r = RangeOps<TRange, T>(Lift);
        var s2r = SetRangeOps<TRange, T>(Lift);
        var s2s = SetSetOps<TRange, T>();

        var sweeps = new List<Task<List<string>>>
        {
            FuzzEngine.RunAsync(new FuzzSweep<FuzzRangeRow<TRange, T>, TRange, TRange>(
                subject, "Left", "Right",
                [.. kept.Select(r => new FuzzCase<TRange, TRange>(r.Id, r.Left, r.Right, Shape(r.Left), Shape(r.Right)))],
                [.. rangeProbes.Select(p => (p, Shape(p)))], Lit, Lit, r2r, Edge, Edge), () => new FuzzDbContext()),

            FuzzEngine.RunAsync(new FuzzSweep<FuzzRangeRow<TRange, T>, TRange, T>(
                subject, "Left", null,
                [.. kept.Select(r => new FuzzCase<TRange, T>(r.Id, r.Left, default, Shape(r.Left), ""))],
                [.. elementProbes.Select(p => (p, "Element"))], Lit, ElementSql<TRange, T>,
                [Op<TRange, T>.Bool("Contains(element)", (l, e) => l.Contains(e), (a, e) => $"{a} @> {e}")],
                Edge, domain.IsExtreme), () => new FuzzDbContext()),

            FuzzEngine.RunAsync(new FuzzSweep<FuzzRangeRow<TRange, T>, RangeSet<TRange, T>, TRange>(
                $"RangeSet<{subject}>", "LeftSet", "Right",
                [.. kept.Select(r => new FuzzCase<RangeSet<TRange, T>, TRange>(r.Id, r.LeftSet, r.Right, Shape(r.LeftSet), Shape(r.Right)))],
                [.. rangeProbes.Select(p => (p, Shape(p)))], Lit, Lit, s2r, SetEdge, Edge), () => new FuzzDbContext()),

            FuzzEngine.RunAsync(new FuzzSweep<FuzzRangeRow<TRange, T>, RangeSet<TRange, T>, RangeSet<TRange, T>>(
                $"RangeSet<{subject}>", "LeftSet", "RightSet",
                [.. kept.Select(r => new FuzzCase<RangeSet<TRange, T>, RangeSet<TRange, T>>(r.Id, r.LeftSet, r.RightSet, Shape(r.LeftSet), Shape(r.RightSet)))],
                [.. setProbes.Select(p => (p, Shape(p)))], Lit, Lit, s2s, SetEdge, SetEdge), () => new FuzzDbContext()),

            FuzzEngine.RunAsync(new FuzzSweep<FuzzRangeRow<TRange, T>, RangeSet<TRange, T>, T>(
                $"RangeSet<{subject}>", "LeftSet", null,
                [.. kept.Select(r => new FuzzCase<RangeSet<TRange, T>, T>(r.Id, r.LeftSet, default, Shape(r.LeftSet), ""))],
                [.. elementProbes.Select(p => (p, "Element"))], Lit, ElementSql<TRange, T>,
                [Op<RangeSet<TRange, T>, T>.Bool("Contains(element)", (l, e) => l.Contains(e), (a, e) => $"{a} @> {e}")],
                SetEdge, domain.IsExtreme), () => new FuzzDbContext())
        };

        foreach (var lines in await Task.WhenAll(sweeps)) failures.AddRange(lines);

        // A discrete date whose extremes Npgsql writes as ±infinity is one step from its neighbour in
        // memory and adjacent to nothing on the server: no finite date is next to -infinity
        // (docs/architecture.md#domain-width-and-the-boundary, DomainBoundaryDivergenceTests). Those
        // rows are set aside only in the shape the document states — memory true, both database legs
        // false — and counted; an adjacency row at the edge that disagrees any other way still fails.
        if (TRange.IsDiscrete && domain.Extremes.Any(e => Lit(TRange.CreateUnboundedEnd(e, true)).Contains("infinity", StringComparison.Ordinal)))
        {
            var setAside = failures.RemoveAll(IsAdjacencyAcrossAnInfinityExtreme);
            test.WriteLine($"{subject}: {setAside} adjacency rows at the date extremes set aside (documented divergence, docs/architecture.md#domain-width-and-the-boundary).");
        }

        return failures;
    }

    private static bool IsAdjacencyAcrossAnInfinityExtreme(string line)
        => line.Contains(" IsAdjacentTo", StringComparison.Ordinal)
        && line.Contains(" at the domain edge ", StringComparison.Ordinal)
        && line.Contains("memory=true, EF=false, PostgreSQL=false", StringComparison.Ordinal);

    /// <summary>
    /// An element as SQL, typed by the range: the lower bound of <c>[e,)</c>. Typed this way it needs
    /// no element mapping of its own (a <see cref="NodaTime.YearMonth"/> is a date on the server), and
    /// <c>[e,)</c> exists for every e, where <c>[e,e]</c> overflows at a discrete maximum.
    /// </summary>
    private static string ElementSql<TRange, T>(T element)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => $"lower({Lit(TRange.CreateUnboundedEnd(element, true))})";

    /// <summary>
    /// The refusals this sweep expects: a data exception, raised loudly, for a value the model holds
    /// and the server cannot — canonicalizing <c>[x, int.MaxValue]</c> needs <c>max + 1</c> (22003,
    /// <c>DomainBoundaryDivergenceTests</c>), a year below 4713 BC (22008/22009, the domain width in
    /// <c>docs/architecture.md</c>). Any other refusal means EF Core wrote a literal PostgreSQL cannot
    /// read, which is a finding.
    /// </summary>
    private static bool IsDomainBoundary(string sqlState) => sqlState is "22003" or "22008" or "22009";

    /// <summary>Each literal PostgreSQL refuses to read, with its SQLSTATE.</summary>
    private static async Task<List<(string Literal, string SqlState, string Message)>> Refused(IEnumerable<string> literals)
    {
        var distinct = literals.Distinct(StringComparer.Ordinal).ToList();
        var answers  = await FuzzEngine.EvaluateAll(() => new FuzzDbContext(), [.. distinct.Select(l => new FuzzQuestion(l, "true", null, null))]);
        var refused  = new List<(string, string, string)>();
        for (var index = 0; index < distinct.Count; index++)
        {
            var text = answers[index].Text;
            if (!text.StartsWith("<error ", StringComparison.Ordinal)) continue;
            var state = text.Substring(7, 5);
            refused.Add((distinct[index], state, text[14..^1]));
        }

        return refused;
    }

    /// <summary>Range against range: the eight relations, equality, and the four value-producing operations.</summary>
    private static List<FuzzOp<TRange, TRange>> RangeOps<TRange, T>(Func<string, string> lift)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => [
            Op<TRange, TRange>.Bool("Contains(range)",      (l, r) => l.Contains(r),             (a, b) => $"{a} @> {b}"),
            Op<TRange, TRange>.Bool("IsContainedBy",        (l, r) => l.IsContainedBy(r),        (a, b) => $"{a} <@ {b}"),
            Op<TRange, TRange>.Bool("Overlaps",             (l, r) => l.Overlaps(r),             (a, b) => $"{a} && {b}"),
            Op<TRange, TRange>.Bool("IsStrictlyLeftOf",     (l, r) => l.IsStrictlyLeftOf(r),     (a, b) => $"{a} << {b}"),
            Op<TRange, TRange>.Bool("IsStrictlyRightOf",    (l, r) => l.IsStrictlyRightOf(r),    (a, b) => $"{a} >> {b}"),
            Op<TRange, TRange>.Bool("DoesNotExtendRightOf", (l, r) => l.DoesNotExtendRightOf(r), (a, b) => $"{a} &< {b}"),
            Op<TRange, TRange>.Bool("DoesNotExtendLeftOf",  (l, r) => l.DoesNotExtendLeftOf(r),  (a, b) => $"{a} &> {b}"),
            Op<TRange, TRange>.Bool("IsAdjacentTo",         (l, r) => l.IsAdjacentTo(r),         (a, b) => $"{a} -|- {b}"),
            Op<TRange, TRange>.Bool("==",                   Equal<TRange>(),                     (a, b) => $"{a} = {b}"),
            Op<TRange, TRange>.Value("Merge",     (l, r) => l.Merge(r),     (a, b) => $"range_merge({a}, {b})"),
            Op<TRange, TRange>.Value("Intersect", (l, r) => l.Intersect(r), (a, b) => $"{a} * {b}"),
            Op<TRange, TRange>.Value("Union",     (l, r) => l.Union(r),     (a, b) => $"{lift(a)} + {lift(b)}"),
            Op<TRange, TRange>.Value("Except",    (l, r) => l.Except(r),    (a, b) => $"{lift(a)} - {lift(b)}"),
            .. BoundOps<TRange, TRange, TRange, T>(
                l => l.LowerBound(), l => l.UpperBound(), l => l.LowerBoundInclusive(), l => l.UpperBoundInclusive(),
                l => l.IsEmpty(), l => l.IsUnboundedStart(), l => l.IsUnboundedEnd(), l => l.IsInfinity(), l => l.IsFinite(),
                a => $"({a} = {Lit(TRange.Infinite)})")
        ];

    /// <summary>Multirange against range.</summary>
    private static List<FuzzOp<RangeSet<TRange, T>, TRange>> SetRangeOps<TRange, T>(Func<string, string> lift)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => [
            Op<RangeSet<TRange, T>, TRange>.Bool("Contains(range)",      (l, r) => l.Contains(r),             (a, b) => $"{a} @> {b}"),
            Op<RangeSet<TRange, T>, TRange>.Bool("Overlaps(range)",      (l, r) => l.Overlaps(r),             (a, b) => $"{a} && {b}"),
            Op<RangeSet<TRange, T>, TRange>.Bool("IsAdjacentTo(range)",  (l, r) => l.IsAdjacentTo(r),         (a, b) => $"{a} -|- {b}"),
            Op<RangeSet<TRange, T>, TRange>.Bool("IsStrictlyLeftOf(range)",     (l, r) => l.IsStrictlyLeftOf(r),     (a, b) => $"{a} << {b}"),
            Op<RangeSet<TRange, T>, TRange>.Bool("IsStrictlyRightOf(range)",    (l, r) => l.IsStrictlyRightOf(r),    (a, b) => $"{a} >> {b}"),
            Op<RangeSet<TRange, T>, TRange>.Bool("DoesNotExtendRightOf(range)", (l, r) => l.DoesNotExtendRightOf(r), (a, b) => $"{a} &< {b}"),
            Op<RangeSet<TRange, T>, TRange>.Bool("DoesNotExtendLeftOf(range)",  (l, r) => l.DoesNotExtendLeftOf(r),  (a, b) => $"{a} &> {b}"),
            Op<RangeSet<TRange, T>, TRange>.Value("Union(range)",     (l, r) => l.Union(r),     (a, b) => $"{a} + {lift(b)}"),
            Op<RangeSet<TRange, T>, TRange>.Value("Intersect(range)", (l, r) => l.Intersect(r), (a, b) => $"{a} * {lift(b)}"),
            Op<RangeSet<TRange, T>, TRange>.Value("Except(range)",    (l, r) => l.Except(r),    (a, b) => $"{a} - {lift(b)}")
        ];

    /// <summary>Multirange against multirange, the operators, and the multirange's own bounds and shape.</summary>
    private static List<FuzzOp<RangeSet<TRange, T>, RangeSet<TRange, T>>> SetSetOps<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var infinite = Lit(RangeSet<TRange, T>.Infinite);
        return
        [
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Bool("Contains(set)",      (l, r) => l.Contains(r),             (a, b) => $"{a} @> {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Bool("Overlaps(set)",      (l, r) => l.Overlaps(r),             (a, b) => $"{a} && {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Bool("IsAdjacentTo(set)",  (l, r) => l.IsAdjacentTo(r),         (a, b) => $"{a} -|- {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Bool("IsStrictlyLeftOf(set)",     (l, r) => l.IsStrictlyLeftOf(r),     (a, b) => $"{a} << {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Bool("IsStrictlyRightOf(set)",    (l, r) => l.IsStrictlyRightOf(r),    (a, b) => $"{a} >> {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Bool("DoesNotExtendRightOf(set)", (l, r) => l.DoesNotExtendRightOf(r), (a, b) => $"{a} &< {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Bool("DoesNotExtendLeftOf(set)",  (l, r) => l.DoesNotExtendLeftOf(r),  (a, b) => $"{a} &> {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Bool("==",                 Equal<RangeSet<TRange, T>>(),        (a, b) => $"{a} = {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Value("Union(set)",     (l, r) => l.Union(r),     (a, b) => $"{a} + {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Value("Intersect(set)", (l, r) => l.Intersect(r), (a, b) => $"{a} * {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Value("Except(set)",    (l, r) => l.Except(r),    (a, b) => $"{a} - {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Value("operator |",     (l, r) => l | r,          (a, b) => $"{a} + {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Value("operator &",     (l, r) => l & r,          (a, b) => $"{a} * {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Value("operator -",     (l, r) => l - r,          (a, b) => $"{a} - {b}"),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Value("Merge",      (l, _) => l.Merge(),      (a, _) => $"range_merge({a})", unary: true),
            Op<RangeSet<TRange, T>, RangeSet<TRange, T>>.Value("Complement", (l, _) => l.Complement(), (a, _) => $"{infinite} - {a}", unary: true),
            .. BoundOps<RangeSet<TRange, T>, RangeSet<TRange, T>, TRange, T>(
                l => l.LowerBound(), l => l.UpperBound(), l => l.LowerBoundInclusive(), l => l.UpperBoundInclusive(),
                l => l.IsEmpty(), l => l.IsUnboundedStart(), l => l.IsUnboundedEnd(), l => l.IsInfinity(), l => l.IsFinite(),
                a => $"({a} = {infinite})")
        ];
    }

    /// <summary>
    /// Bounds, inclusivity and shape. A bound is compared on the server as a bound of a range the
    /// model built from the expected value — <c>lower([v,))</c>, <c>upper((,v]))</c> — so the
    /// discrete step PostgreSQL adds to an upper bound is added to both sides, and an element type
    /// with no mapping of its own is compared all the same.
    /// </summary>
    private static List<FuzzOp<TL, TR>> BoundOps<TL, TR, TRange, T>(
        Expression<Func<TL, T?>> lower, Expression<Func<TL, T?>> upper,
        Expression<Func<TL, bool>> lowerInclusive, Expression<Func<TL, bool>> upperInclusive,
        Expression<Func<TL, bool>> isEmpty, Expression<Func<TL, bool>> unboundedStart, Expression<Func<TL, bool>> unboundedEnd,
        Expression<Func<TL, bool>> isInfinity, Expression<Func<TL, bool>> isFinite, Func<string, string> infinitySql)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var discrete = TRange.IsDiscrete;

        return
        [
            new("LowerBound", Unary(lower), (a, _) => $"lower({a})", BoundAgreement<TRange, T>(lower: true), unary: true),
            new("UpperBound", Unary(upper), (a, _) => $"upper({a})", BoundAgreement<TRange, T>(lower: false), unary: true),
            Op<TL, TR>.Bool("LowerBoundInclusive", Unary(lowerInclusive), (a, _) => $"lower_inc({a})", unary: true),
            // The model's discrete canonical form is closed; PostgreSQL's half-open upper_inc is always false.
            Op<TL, TR>.Bool("UpperBoundInclusive", Unary(upperInclusive),
                            (a, _) => discrete ? $"(NOT upper_inf({a}) AND NOT isempty({a}))" : $"upper_inc({a})", unary: true),
            Op<TL, TR>.Bool("IsEmpty",          Unary(isEmpty),        (a, _) => $"isempty({a})",   unary: true),
            Op<TL, TR>.Bool("IsUnboundedStart", Unary(unboundedStart), (a, _) => $"lower_inf({a})", unary: true),
            Op<TL, TR>.Bool("IsUnboundedEnd",   Unary(unboundedEnd),   (a, _) => $"upper_inf({a})", unary: true),
            Op<TL, TR>.Bool("IsInfinity",       Unary(isInfinity),     (a, _) => infinitySql(a),    unary: true),
            Op<TL, TR>.Bool("IsFinite",         Unary(isFinite),
                            (a, _) => $"(NOT lower_inf({a}) AND NOT upper_inf({a}) AND NOT isempty({a}))", unary: true)
        ];

        static LambdaExpression Unary<TOut>(Expression<Func<TL, TOut>> body) =>
            Expression.Lambda(body.Body, body.Parameters[0], Expression.Parameter(typeof(TR), "_"));
    }

    private static Func<object?, string, string> BoundAgreement<TRange, T>(bool lower)
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => (expected, actual) => expected is not T value
                                     ? $"({actual}) IS NULL"
                                     : lower
                                         ? $"({actual}) = lower({Lit(TRange.CreateUnboundedEnd(value, true))})"
                                         : $"({actual}) = upper({Lit(TRange.CreateUnboundedStart(value, true))})";

    private static LambdaExpression Equal<TValue>()
    {
        var (l, r) = (Expression.Parameter(typeof(TValue), "l"), Expression.Parameter(typeof(TValue), "r"));
        return Expression.Lambda(Expression.Equal(l, r), l, r);
    }

    // =========================================================================
    // Value sets
    // =========================================================================

    private static async Task<List<string>> SweepSet<TSet, T>(TestContext test, Type primitive)
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var subject = FuzzTypes.Name(typeof(TSet));
        var random  = FuzzRun.For(subject);
        var draw    = Element<TSet, T>(primitive);

        // Each row draws from a small pool, so the two sets share elements and repeat them.
        TSet DrawFrom(List<T> pool)
        {
            var count = random.Next(7);
            return TSet.From(Enumerable.Range(0, count).Select(_ => pool[random.Next(pool.Count)]));
        }

        var pools = new List<List<T>>();
        var rows  = new List<FuzzSetRow<TSet, T>>();
        for (var id = 1; id <= FuzzRun.Rows; id++)
        {
            var pool = Enumerable.Range(0, random.Next(1, 7)).Select(_ => draw(random)).ToList();
            pools.Add(pool);
            rows.Add(new FuzzSetRow<TSet, T> { Id = id, Left = DrawFrom(pool), Right = DrawFrom(pool) });
        }

        var setProbes     = Enumerable.Range(0, RangeProbes).Select(_ => DrawFrom(pools[random.Next(pools.Count)])).ToList();
        var elementProbes = Enumerable.Range(0, ElementProbes)
                                      .Select(_ => random.Next(3) == 0 ? draw(random) : pools[random.Next(pools.Count)][0]).ToList();

        var refused = await Refused(rows.SelectMany(r => new[] { Lit(r.Left), Lit(r.Right) })
                                        .Concat(setProbes.Select(Lit)).Concat(elementProbes.Select(e => Lit(TSet.From(e)))));
        var failures = refused.Select(r => $"{subject} literal refused: {r.Literal} → {r.SqlState} {r.Message}").ToList();
        if (failures.Count > 0) return failures;

        await using (var write = new FuzzDbContext())
        {
            write.AddRange(rows);
            await write.SaveChangesAsync();
        }

        test.WriteLine($"{subject}: {rows.Count} rows.");

        string ElementSql(T element) => $"({Lit(TSet.From(element))})[1]";

        var sweeps = new List<Task<List<string>>>
        {
            FuzzEngine.RunAsync(new FuzzSweep<FuzzSetRow<TSet, T>, TSet, TSet>(
                subject, "Left", "Right",
                [.. rows.Select(r => new FuzzCase<TSet, TSet>(r.Id, r.Left, r.Right, $"N{r.Left.Values.Length}", $"N{r.Right.Values.Length}"))],
                [.. setProbes.Select(p => (p, $"N{p.Values.Length}"))], Lit, Lit, ValueSetOps<TSet, T>()), () => new FuzzDbContext()),

            FuzzEngine.RunAsync(new FuzzSweep<FuzzSetRow<TSet, T>, TSet, T>(
                subject, "Left", null,
                [.. rows.Select(r => new FuzzCase<TSet, T>(r.Id, r.Left, default!, $"N{r.Left.Values.Length}", ""))],
                [.. elementProbes.Select(p => (p, "Element"))], Lit, ElementSql,
                [
                    Op<TSet, T>.Bool("Contains(element)", (l, e) => l.Contains(e), (a, e) => $"{a} @> ARRAY[{e}]"),
                    Op<TSet, T>.Value("Remove(element)",  (l, e) => l.Remove(e),   (a, e) => $"array_remove({a}, {e})")
                ]), () => new FuzzDbContext())
        };

        foreach (var lines in await Task.WhenAll(sweeps)) failures.AddRange(lines);

        return failures;
    }

    /// <summary>
    /// The value set operations EF Core translates. <c>Union</c> is <c>array_cat</c>, which keeps
    /// duplicates and order, so it is asked only where the SQL guard allows it — projected (the
    /// read canonicalizes) and under the order- and multiplicity-insensitive operators — and compared
    /// on the server as a set, not as an array.
    /// </summary>
    private static List<FuzzOp<TSet, TSet>> ValueSetOps<TSet, T>()
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
        => [
            Op<TSet, TSet>.Bool("Overlaps",           (l, r) => l.Overlaps(r),           (a, b) => $"{a} && {b}"),
            Op<TSet, TSet>.Bool("IsSubsetOf",         (l, r) => l.IsSubsetOf(r),         (a, b) => $"{a} <@ {b}"),
            Op<TSet, TSet>.Bool("IsSupersetOf",       (l, r) => l.IsSupersetOf(r),       (a, b) => $"{a} @> {b}"),
            Op<TSet, TSet>.Bool("IsProperSubsetOf",   (l, r) => l.IsProperSubsetOf(r),   (a, b) => $"({a} <@ {b} AND NOT {a} @> {b})"),
            Op<TSet, TSet>.Bool("IsProperSupersetOf", (l, r) => l.IsProperSupersetOf(r), (a, b) => $"({a} @> {b} AND NOT {a} <@ {b})"),
            Op<TSet, TSet>.Bool("==",                 Equal<TSet>(),                     (a, b) => $"{a} = {b}"),
            // Read back, array_cat is canonicalized by the set's From — what materializing a projected Union does.
            Op<TSet, TSet>.Value("Union", (l, r) => l.Union(r), (a, b) => $"array_cat({a}, {b})"),
            Op<TSet, TSet>.Bool("Union.IsSubsetOf(right)", (l, r) => l.Union(r).IsSubsetOf(r), (a, b) => $"array_cat({a}, {b}) <@ {b}"),
            Op<TSet, TSet>.Bool("Union.Overlaps(left)",    (l, r) => l.Union(r).Overlaps(l),   (a, b) => $"array_cat({a}, {b}) && {a}"),
            Op<TSet, TSet>.Bool("Union.IsEmpty",           Member<TSet>(nameof(StringSet.IsEmpty), (Expression<Func<TSet, TSet, TSet>>)((l, r) => l.Union(r))),
                                (a, b) => $"cardinality(array_cat({a}, {b})) = 0"),
            new("Count", Member<TSet>(nameof(StringSet.Count)), (a, _) => $"cardinality({a})",
                (expected, actual) => $"({actual}) = {expected}", unary: true),
            Op<TSet, TSet>.Bool("IsEmpty", Member<TSet>(nameof(StringSet.IsEmpty)), (a, _) => $"cardinality({a}) = 0", unary: true)
        ];

    /// <summary>
    /// <c>Count</c> and <c>IsEmpty</c> are instance properties of each set type, not of an interface,
    /// so they are reached by name — on the left operand, or on the result of <paramref name="of"/>.
    /// </summary>
    private static LambdaExpression Member<TSet>(string name, LambdaExpression? of = null)
    {
        var (l, r) = (Expression.Parameter(typeof(TSet), "l"), Expression.Parameter(typeof(TSet), "r"));
        var target = of is null ? l : FuzzEngine.Splice(of, l, r);
        return Expression.Lambda(Expression.Property(target, name), l, r);
    }

    /// <summary>
    /// A drawer of elements: the primitive's domain for a closed set, and for a wrapper arity the
    /// primitive's round-trip text parsed by the wrapper — refusals (a key that may not be blank) are
    /// drawn again. An element the set itself refuses is drawn again too.
    /// </summary>
    private static Func<Random, T> Element<TSet, T>(Type primitive)
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var drawPrimitive = FuzzDomains.SetElement(primitive);
        Func<object, T> convert = typeof(T) == primitive
                                      ? value => (T)value
                                      : value => (T)typeof(T).GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(IFormatProvider)])!
                                                             .Invoke(null, [FuzzDomains.RoundTripText(value), System.Globalization.CultureInfo.InvariantCulture])!;
        return random =>
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var element = convert(drawPrimitive(random));
                    _ = TSet.From(element);
                    return element;
                }
                catch (Exception) when (attempt < 50)
                {
                }
            }
        };
    }

    // =========================================================================
    // Operation builders
    // =========================================================================

    private static class Op<TL, TR>
    {
        public static FuzzOp<TL, TR> Bool(string name, Expression<Func<TL, TR, bool>> expression, Func<string, string, string> sql, bool unary = false)
            => Bool(name, (LambdaExpression)expression, sql, unary);

        public static FuzzOp<TL, TR> Bool(string name, LambdaExpression expression, Func<string, string, string> sql, bool unary = false)
            => new(name, expression, sql, (expected, actual) => $"({actual}) IS NOT DISTINCT FROM {((bool)expected! ? "true" : "false")}", unary);

        public static FuzzOp<TL, TR> Value<TOut>(string name, Expression<Func<TL, TR, TOut>> expression, Func<string, string, string> sql, bool unary = false)
            => new(name, expression, sql, null, unary);
    }
}

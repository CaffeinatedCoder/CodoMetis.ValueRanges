using System.Linq.Expressions;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>RangeSet.Count</c> runs as SQL and answers what memory answers over the loaded rows (#484), on every range
/// type, for a stored multirange and for one the server computes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rows</b> hold multiranges built from the ranges each type's factories make from a handful of points, the
/// extremes among them: empty, every range alone, and pairs of ranges that overlap, touch or stand apart, as
/// <c>A</c> and <c>B</c>, with a nullable <c>MaybeA</c> NULL on every third row.
/// </para>
/// <para>
/// <b>Calls</b> count the column, the nullable column, and the server's <c>Union</c>, <c>Except</c>,
/// <c>Intersect</c>, <c>Complement()</c> and <c>Gaps()</c>, each through <see cref="CompositionRunner.Judge"/>
/// (tested against <see langword="null"/> both ways and projected), plus two predicates and an ordering.
/// </para>
/// <para>
/// <b>The domain boundary.</b> A stored multirange is counted as memory counts it on every row, the extremes
/// included. One the server computes from an operand with a bound at the element type's extreme is set aside
/// and counted: there PostgreSQL keeps a range memory reads as empty, <c>(,-2147483648)</c> below
/// <c>int.MinValue</c> or <c>(infinity,)</c> past the last date, and keeps a date at the extreme apart from
/// its neighbour (<c>docs/architecture.md#domain-width-and-the-boundary</c>, <c>docs/efcore.md#what-runs-where</c>).
/// <c>IsEmpty()</c> over the same result disagrees the same way; <c>Count</c> only makes it visible.
/// </para>
/// <para>
/// <b>Not in parallel.</b> Each test seeds a database of its own; run beside the rest of the suite, their pools
/// would add a dozen databases' connections to the shared server's limit at once.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class RangeSetCountParityTests
{
    [TestMethod]
    public Task Int32Range_CountsAsMemoryDoes() => Run<Int32Range, int>("int4", [int.MinValue, -1, 0, 1, 5, int.MaxValue - 1], int.MinValue, int.MaxValue);

    [TestMethod]
    public Task Int64Range_CountsAsMemoryDoes() => Run<Int64Range, long>("int8", [long.MinValue, 0, 1, 5, long.MaxValue - 1], long.MinValue, long.MaxValue);

    [TestMethod]
    public Task DecimalRange_CountsAsMemoryDoes() => Run<DecimalRange, decimal>("numeric", [-1.5m, 0m, 0.5m, 2m, 2.000m]);

    [TestMethod]
    public Task Float64Range_CountsAsMemoryDoes() => Run<Float64Range, double>("float8", [double.MinValue, -1.5, -0.0, 0.0, 2.25, double.MaxValue]);

    [TestMethod]
    public Task Float32Range_CountsAsMemoryDoes() => Run<Float32Range, float>("float4", [float.MinValue, -1.5f, 0.0f, 2.25f, float.MaxValue]);

    [TestMethod]
    public Task DateRange_CountsAsMemoryDoes() => Run<DateRange, DateOnly>(
        "date", [DateOnly.MinValue, new(1, 1, 2), new(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 15), new(9999, 12, 30), DateOnly.MaxValue],
        DateOnly.MinValue, DateOnly.MaxValue);

    [TestMethod]
    public Task DateTimeRange_CountsAsMemoryDoes() => Run<DateTimeRange, DateTime>(
        "timestamp", [DateTime.MinValue, new(2026, 1, 1), new(2026, 1, 1, 12, 30, 0), new(2026, 1, 2), DateTime.MaxValue]);

    [TestMethod]
    public Task DateTimeOffsetRange_CountsAsMemoryDoes() => Run<DateTimeOffsetRange, DateTimeOffset>(
        "timestamptz",
        [DateTimeOffset.MinValue, new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 1, 1, 12, 30, 0, TimeSpan.Zero), DateTimeOffset.MaxValue]);

    [TestMethod]
    public Task TimeRange_CountsAsMemoryDoes() => Run<TimeRange, TimeOnly>(
        "time", [TimeOnly.MinValue, new(8, 0), new(12, 30), new(17, 0), new(23, 59, 59, 999, 999)]);

    [TestMethod]
    public Task LocalDateRange_CountsAsMemoryDoes() => Run<LocalDateRange, LocalDate>(
        "localdate", [new(1, 1, 1), new(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 15), new(9999, 12, 30), LocalDate.MaxIsoValue],
        LocalDate.MinIsoValue, LocalDate.MaxIsoValue);

    [TestMethod]
    public Task LocalDateTimeRange_CountsAsMemoryDoes() => Run<LocalDateTimeRange, LocalDateTime>(
        "localdatetime", [LocalDateTime.MinIsoValue, new(2026, 1, 1, 0, 0), new(2026, 1, 1, 12, 30), LocalDateTime.MaxIsoValue]);

    [TestMethod]
    public Task InstantRange_CountsAsMemoryDoes() => Run<InstantRange, Instant>(
        "instant", [Instant.MinValue, Instant.FromUtc(2026, 1, 1, 0, 0), Instant.FromUtc(2026, 1, 1, 12, 30), Instant.MaxValue]);

    [TestMethod]
    public Task YearMonthRange_CountsAsMemoryDoes() => Run<YearMonthRange, YearMonth>(
        "yearmonth", [new(1, 1), new(2026, 1), new(2026, 2), new(2026, 7), new(9999, 11), new(9999, 12)],
        max: new YearMonth(9999, 12));

    /// <summary>Every range type is measured here, so a range type added later is counted only with its parity shown.</summary>
    [TestMethod]
    public void EveryRangeType_HasACountParityTest()
    {
        var rangeTypes = new[] { typeof(Int32Range).Assembly, typeof(LocalDateRange).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsAbstract && type.GetInterfaces().Any(i => i.IsGenericType
                                                                         && i.GetGenericTypeDefinition() == typeof(IRangeFactory<,>)
                                                                         && i.GenericTypeArguments[0] == type))
            .ToList();
        Assert.IsTrue(rangeTypes.Count >= 13, $"Discovered only {rangeTypes.Count} range types: the discovery is broken.");

        var missing = rangeTypes.Where(type => typeof(RangeSetCountParityTests).GetMethod($"{type.Name}_CountsAsMemoryDoes") is null)
                                .Select(type => type.Name)
                                .ToList();
        Assert.AreEqual(0, missing.Count, $"RangeSet.Count runs as SQL on every range type, but these have no parity test here: {string.Join(", ", missing)}");
    }

    private static async Task Run<TRange, T>(string name, T[] points, T? min = null, T? max = null)
        where TRange : class, IRange<T>, IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await CompositionDatabase.Create($"count_{name}");
        var sets = Sets<TRange, T>(points);

        await using (var seed = new CountContext<TRange, T>(connectionString))
        {
            await seed.Database.EnsureCreatedAsync();
            for (var i = 0; i < sets.Count; i++)
            {
                seed.Rows.Add(new CountOf<TRange, T>
                {
                    Id     = i + 1,
                    A      = sets[i],
                    B      = sets[(i * 7 + 3) % sets.Count],
                    MaybeA = i % 3 == 0 ? null : sets[(i + 5) % sets.Count]
                });
            }

            await seed.SaveChangesAsync();
        }

        await using var context = new CountContext<TRange, T>(connectionString);
        var loaded = await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.AreEqual(sets.Count, loaded.Count);
        string Describe(int id) => loaded.Single(r => r.Id == id) is var r ? $"{id} (A {r.A}, B {r.B}, MaybeA {r.MaybeA?.ToString() ?? "NULL"})" : "";

        // Operands with a bound at the element type's extreme: their computed multirange is the documented boundary.
        bool AtExtreme(RangeSet<TRange, T> set) => set.Any(range => range.LowerBound() is { } l && (l.Equals(min) || l.Equals(max))
                                                                 || range.UpperBound() is { } u && (u.Equals(min) || u.Equals(max)));
        var inside = loaded.Where(r => !AtExtreme(r.A) && !AtExtreme(r.B)).ToList();
        var insideIds = inside.Select(r => r.Id).ToList();
        Assert.IsTrue(inside.Count * 3 > loaded.Count, $"Only {inside.Count} of {loaded.Count} rows stay away from the extremes");

        var failures = new List<string>();
        foreach (var (call, lambda, computed) in Calls<TRange, T>())
        {
            var (source, rows) = computed
                ? (context.Rows.AsNoTracking().Where(r => insideIds.Contains(r.Id)), inside)
                : (context.Rows.AsNoTracking(), loaded);
            var outcome = await CompositionRunner.Judge(
                $"count {name}", new Call(call, lambda.Body), lambda.Parameters[0], source, rows, r => r.Id, Describe, failures);
            if (outcome != Outcome.Compared) failures.Add($"{call} [count {name}]: {outcome}, where it must run as SQL and be compared");
        }

        Console.WriteLine($"count {name}: {loaded.Count} rows, {loaded.Count - inside.Count} set aside for the computed calls at the domain boundary");

        foreach (var descending in new[] { false, true })
        {
            var sorted = descending
                ? await context.Rows.OrderByDescending(r => r.A.Count).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync()
                : await context.Rows.OrderBy(r => r.A.Count).ThenBy(r => r.Id).Select(r => r.Id).ToListAsync();
            var expected = descending
                ? loaded.OrderByDescending(r => r.A.Count).ThenBy(r => r.Id).Select(r => r.Id).ToList()
                : loaded.OrderBy(r => r.A.Count).ThenBy(r => r.Id).Select(r => r.Id).ToList();
            if (!sorted.SequenceEqual(expected))
                failures.Add($"OrderBy{(descending ? "Descending" : "")}(r.A.Count) [count {name}]: server {string.Join(",", sorted)}, memory {string.Join(",", expected)}");
        }

        // Each test has a database of its own, so its idle connections would otherwise stay open beside every other's.
        await using (var connection = new NpgsqlConnection(connectionString)) NpgsqlConnection.ClearPool(connection);
        Assert.AreEqual(0, failures.Count, $"{failures.Count} disagreements:{Environment.NewLine}" + string.Join(Environment.NewLine, failures.Take(80)));
    }

    /// <summary>Each call, and whether it counts a multirange the server computes rather than a stored one.</summary>
    private static IEnumerable<(string Name, LambdaExpression Lambda, bool Computed)> Calls<TRange, T>()
        where TRange : class, IRange<T>, IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        (string, LambdaExpression, bool) Count(string name, Expression<Func<CountOf<TRange, T>, int>> lambda, bool computed = true) => (name, lambda, computed);
        (string, LambdaExpression, bool) Predicate(string name, Expression<Func<CountOf<TRange, T>, bool>> lambda, bool computed = true) => (name, lambda, computed);

        yield return Count("r.A.Count", r => r.A.Count, computed: false);
        yield return Count("r.MaybeA.Count", r => r.MaybeA!.Count, computed: false);
        yield return Predicate("r.A.Count > 1", r => r.A.Count > 1, computed: false);
        yield return Count("r.A.Union(r.B).Count", r => r.A.Union(r.B).Count);
        yield return Count("r.A.Except(r.B).Count", r => r.A.Except(r.B).Count);
        yield return Count("r.A.Intersect(r.B).Count", r => r.A.Intersect(r.B).Count);
        yield return Count("r.A.Complement().Count", r => r.A.Complement().Count);
        yield return Count("r.A.Gaps().Count", r => r.A.Gaps().Count);
        yield return Predicate("r.A.Union(r.B).Count < r.A.Count + r.B.Count", r => r.A.Union(r.B).Count < r.A.Count + r.B.Count);
    }

    /// <summary>
    /// The empty set, every range the factories build from <paramref name="points"/> alone, and each range with
    /// two others: the next one built (which often touches or overlaps it) and one further on.
    /// </summary>
    private static List<RangeSet<TRange, T>> Sets<TRange, T>(T[] points)
        where TRange : class, IRange<T>, IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var ranges = new List<TRange>();
        foreach (var point in points)
        {
            foreach (var inclusive in new[] { true, false })
            {
                ranges.AddRange(Build(() => TRange.CreateUnboundedStart(point, inclusive)));
                ranges.AddRange(Build(() => TRange.CreateUnboundedEnd(point, inclusive)));
            }
        }

        for (var i = 0; i < points.Length; i++)
        {
            for (var j = i; j < points.Length; j++)
            {
                foreach (var (startInclusive, endInclusive) in new[] { (true, true), (true, false), (false, true), (false, false) })
                    ranges.AddRange(Build(() => TRange.CreateFinite(points[i], points[j], startInclusive, endInclusive)));
            }
        }

        ranges = [.. ranges.Where(range => !range.IsEmpty()).Distinct()];
        var sets = new List<RangeSet<TRange, T>> { RangeSet<TRange, T>.Empty };
        for (var i = 0; i < ranges.Count; i++)
        {
            sets.Add(RangeSet<TRange, T>.From([ranges[i]]));
            sets.Add(RangeSet<TRange, T>.From([ranges[i], ranges[(i + 1) % ranges.Count]]));
            sets.Add(RangeSet<TRange, T>.From([ranges[i], ranges[(i + 1) % ranges.Count], ranges[(i + ranges.Count / 2) % ranges.Count]]));
        }

        return sets;

        static IEnumerable<TRange> Build(Func<TRange> factory)
        {
            TRange? built = null;
            try { built = factory(); }
            catch (ArgumentException) { }
            if (built is not null) yield return built;
        }
    }

    public sealed class CountOf<TRange, T>
        where TRange : class, IRange<T>, IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        public int Id { get; set; }

        public RangeSet<TRange, T> A { get; set; } = RangeSet<TRange, T>.Empty;

        public RangeSet<TRange, T> B { get; set; } = RangeSet<TRange, T>.Empty;

        public RangeSet<TRange, T>? MaybeA { get; set; }
    }

    private sealed class CountContext<TRange, T>(string connectionString) : DbContext
        where TRange : class, IRange<T>, IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        public DbSet<CountOf<TRange, T>> Rows => Set<CountOf<TRange, T>>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(
                connectionString,
                npgsql => npgsql.UseValueRangesNodaTime().ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresRange("timerange", "time");
            modelBuilder.HasPostgresRange("float8range", "double precision");
            modelBuilder.HasPostgresRange("float4range", "real");
            modelBuilder.Entity<CountOf<TRange, T>>(row =>
            {
                row.ToTable("count_rows");
                row.Property(r => r.Id).ValueGeneratedNever();
            });
        }
    }
}

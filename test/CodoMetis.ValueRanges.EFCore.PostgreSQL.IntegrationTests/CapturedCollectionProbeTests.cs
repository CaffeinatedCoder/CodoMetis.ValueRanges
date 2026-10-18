using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.Conventions.Tests;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A captured collection of ranges or value sets read by a query — <c>wanted.Contains(r.Value)</c>,
/// <c>wanted.Any(…)</c>, <c>wanted.Count(…)</c> — for every discovered range and value set type,
/// executed and compared with the same expression in memory.
/// </summary>
/// <remarks>
/// <para>
/// The <em>captured collection</em> value of the EF Core operand-position axis (<c>test/oracles/axes.md</c>).
/// The other sweeps bind an operand as a column, an inlined constant or a single parameter; none sends
/// a collection, which EF Core binds as one array parameter of the element type. Three defects came
/// through that gap: a collection of value sets is an array of arrays, which Npgsql could not write
/// (#46); a collection of ranges went out untyped, Npgsql inferred a multirange, and <c>= ANY</c> failed
/// on the server (#71); and <c>Any</c>/<c>Count</c> over that multirange read its ranges merged and its
/// empties dropped, without an error (#76).
/// </para>
/// <para>
/// The product, per type: four collection forms (array, <c>List</c>, <c>HashSet</c>, <c>IEnumerable</c>)
/// × six lists chosen to tell a list from its multirange (empty, overlapping, adjacent, holding an empty
/// value, a duplicate, every shape) × two query shapes. <em>Alone</em> projects only <c>Any</c> and
/// <c>Count</c> forms; <em>beside Contains</em> projects a <c>Contains</c> reading the same collection
/// first, because whether one does decides how the plugin binds a range collection. The oracle is the
/// projection itself, compiled and run over the rows as loaded. A disagreement goes through
/// <see cref="Candidates.Verdict"/> with its issue.
/// </para>
/// </remarks>
[TestClass]
public sealed class CapturedCollectionProbeTests
{
    private const string Alone         = "alone";
    private const string BesideContains = "beside Contains";

    [ClassInitialize]
    public static async Task CreateDatabase(TestContext _)
    {
        if (ContainerLifecycle.ConnectionString is null) return;

        await using (var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {CollectionProbeDbContext.DatabaseName}", connection);
            await command.ExecuteNonQueryAsync();
        }

        await using var context = new CollectionProbeDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    [TestMethod]
    public async Task Ranges_AnswerAsInMemory()
    {
        ContainerLifecycle.RequireDatabase();
        Assert.IsGreaterThanOrEqualTo(11, FuzzTypes.Ranges.Count, "range type discovery found too few types");

        var method = typeof(CapturedCollectionProbeTests).GetMethod(nameof(SweepRange), BindingFlags.NonPublic | BindingFlags.Static)!;
        var lines  = await RunAll(FuzzTypes.Ranges.Select(t => (Func<Task<List<string>>>)(
                                      () => (Task<List<string>>)method.MakeGenericMethod(t.Range, t.Element).Invoke(null, [])!)));

        Candidates.Verdict(nameof(CapturedCollectionProbeTests), lines,
            "A captured collection of ranges answers differently in the database and in memory",
            method: nameof(Ranges_AnswerAsInMemory));
    }

    [TestMethod]
    public async Task ValueSets_AnswerAsInMemory()
    {
        ContainerLifecycle.RequireDatabase();
        Assert.IsGreaterThanOrEqualTo(30, FuzzTypes.Sets.Count, "value set type discovery found too few types");

        var method = typeof(CapturedCollectionProbeTests).GetMethod(nameof(SweepSet), BindingFlags.NonPublic | BindingFlags.Static)!;
        var lines  = await RunAll(FuzzTypes.Sets.Select(t => (Func<Task<List<string>>>)(
                                      () => (Task<List<string>>)method.MakeGenericMethod(t.Set, t.Element).Invoke(null, [])!)));

        Candidates.Verdict(nameof(CapturedCollectionProbeTests), lines,
            "A captured collection of value sets answers differently in the database and in memory",
            method: nameof(ValueSets_AnswerAsInMemory));
    }

    private static async Task<List<string>> RunAll(IEnumerable<Func<Task<List<string>>>> sweeps)
    {
        var lines = new ConcurrentBag<List<string>>();
        await Parallel.ForEachAsync(sweeps, new ParallelOptions { MaxDegreeOfParallelism = 4 },
                                    async (sweep, _) => lines.Add(await sweep()));
        return [.. lines.SelectMany(l => l).Order(StringComparer.Ordinal)];
    }

    // =========================================================================
    // The products
    // =========================================================================

    private static Task<List<string>> SweepRange<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (low, high) = ((T)RangeProbes.Bounds[typeof(T)].Low, (T)RangeProbes.Bounds[typeof(T)].High);

        // The rows, and the lists as indexes into them.
        const int empty = 0, infinite = 1, finite = 2, onward = 3, before = 4, until = 5;

        return Sweep(
            typeof(TRange).Name,
            [
                TRange.Empty,
                TRange.Infinite,
                TRange.CreateFinite(low, high, startInclusive: true, endInclusive: false),
                TRange.CreateUnboundedEnd(low, startInclusive: true),     // overlaps finite
                TRange.CreateUnboundedStart(low, endInclusive: false),    // adjacent to finite
                TRange.CreateUnboundedStart(high, endInclusive: false)    // contains finite
            ],
            [
                ("empty",               []),
                ("overlapping",         [finite, onward]),
                ("adjacent",            [before, finite]),
                ("with an empty range", [empty, finite]),
                ("duplicate",           [finite, finite]),
                ("every shape",         [infinite, empty, finite, onward, before, until])
            ],
            [
                ("Any(==)",             Aggregate<TRange>(nameof(Enumerable.Any),   Equal<TRange>())),
                ("Any(Overlaps)",       Aggregate<TRange>(nameof(Enumerable.Any),   Pred<TRange>((w, c) => w.Overlaps(c)))),
                ("Any(IsEmpty)",        Aggregate<TRange>(nameof(Enumerable.Any),   Pred<TRange>((w, _) => w.IsEmpty()))),
                ("Count(Overlaps)",     Aggregate<TRange>(nameof(Enumerable.Count), Pred<TRange>((w, c) => w.Overlaps(c)))),
                ("Count(IsAdjacentTo)", Aggregate<TRange>(nameof(Enumerable.Count), Pred<TRange>((w, c) => w.IsAdjacentTo(c)))),
                ("Count()",             Aggregate<TRange>(nameof(Enumerable.Count), null))
            ]);
    }

    private static Task<List<string>> SweepSet<TSet, T>()
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        if (!SetProbes.HasProbes(typeof(T))) throw new InvalidOperationException($"SetProbes has no elements for {typeof(T)}.");
        var probes = SetProbes.For<T>();

        const int empty = 0, first = 1, all = 2, last = 3;

        return Sweep(
            FuzzTypes.Name(typeof(TSet)),
            [TSet.Empty, TSet.From([probes[0]]), TSet.From(probes), TSet.From([probes[^1]])],
            [
                ("empty",             []),
                ("overlapping",       [all, first]),
                ("with an empty set", [empty, first]),
                ("duplicate",         [first, first]),
                ("every value",       [empty, first, all, last])
            ],
            [
                ("Any(==)",           Aggregate<TSet>(nameof(Enumerable.Any),   Equal<TSet>())),
                ("Any(Overlaps)",     Aggregate<TSet>(nameof(Enumerable.Any),   Pred<TSet>((w, c) => w.Overlaps(c)))),
                ("Any(IsEmpty)",      Aggregate<TSet>(nameof(Enumerable.Any),   IsEmpty<TSet>())),
                ("Count(IsSubsetOf)", Aggregate<TSet>(nameof(Enumerable.Count), Pred<TSet>((w, c) => w.IsSubsetOf(c)))),
                ("Count(Overlaps)",   Aggregate<TSet>(nameof(Enumerable.Count), Pred<TSet>((w, c) => w.Overlaps(c)))),
                ("Count()",           Aggregate<TSet>(nameof(Enumerable.Count), null))
            ]);
    }

    // =========================================================================
    // The engine
    // =========================================================================

    /// <summary>The four ways a consumer holds the collection, as fields, so EF Core parameterizes it as a captured local.</summary>
    private sealed class Forms<TItem>(TItem[] items)
    {
        public readonly TItem[]            Array      = items;
        public readonly List<TItem>        List       = [.. items];
        public readonly HashSet<TItem>     HashSet    = [.. items];
        public readonly IEnumerable<TItem> Enumerable = items.ToList();
    }

    private static readonly string[] FormNames =
        [nameof(Forms<int>.Array), nameof(Forms<int>.List), nameof(Forms<int>.HashSet), nameof(Forms<int>.Enumerable)];

    private static async Task<List<string>> Sweep<TItem>(
        string subject, TItem[] values, (string Name, int[] Rows)[] lists, (string Name, LambdaExpression Op)[] ops)
        where TItem : class
    {
        await using (var write = new CollectionProbeDbContext())
        {
            write.AddRange(values.Select((value, index) => new CollectionRow<TItem> { Id = index + 1, Value = value }));
            await write.SaveChangesAsync();
        }

        List<CollectionRow<TItem>> rows;
        await using (var read = new CollectionProbeDbContext())
            rows = await read.Set<CollectionRow<TItem>>().AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        var failures = new List<string>();
        // A list holds the values as loaded, not as built: a temporal finer than the store's microsecond
        // is truncated on the way in (the store rule, TemporalStoreParityTests), and a consumer who
        // captures values read them back first.
        foreach (var (listName, indexes) in lists)
        {
            var forms = Expression.Constant(new Forms<TItem>([.. indexes.Select(index => rows[index].Value)]));
            foreach (var form in FormNames)
            {
                var collection = Expression.Field(forms, form);
                var contains   = ("Contains", ContainsOf<TItem>(collection));

                foreach (var (shape, companion) in new[] { (Alone, ((string, LambdaExpression)?)null), (BesideContains, contains) })
                {
                    var label = $"{subject} over {listName} as {form}, {shape}";
                    failures.AddRange(await Compare(label, rows, collection, companion, ops));
                }
            }
        }

        return failures;
    }

    /// <summary>
    /// Runs the operations as one projection, after the companion when there is one, and compares each
    /// row with memory. A query that fails is run again once per operation — still beside the companion,
    /// since whether one reads the collection decides how it is bound — so the failure names the
    /// operation that spoiled it.
    /// </summary>
    private static async Task<List<string>> Compare<TItem>(
        string label, List<CollectionRow<TItem>> rows, Expression collection,
        (string Name, LambdaExpression Op)? companion, (string Name, LambdaExpression Op)[] ops)
        where TItem : class
    {
        (string Name, LambdaExpression Op)[] asked = companion is { } first ? [first, .. ops] : ops;
        var projection = Projection<TItem>(collection, asked);
        var memory     = projection.Compile();

        try
        {
            return Differences(label, rows, memory, await Database(projection), [.. asked.Select(a => a.Name)]);
        }
        catch (Exception)
        {
            // One operation spoiled the query; ask each alone below.
        }

        var lines = new List<string>();
        foreach (var single in asked)
        {
            (string Name, LambdaExpression Op)[] pair = companion is { } with && with.Name != single.Name ? [with, single] : [single];
            var one = Projection<TItem>(collection, pair);
            try
            {
                lines.AddRange(Differences(label, rows, one.Compile(), await Database(one), [.. pair.Select(p => p.Name)])
                                  .Where(line => line.Contains($": {single.Name} for ", StringComparison.Ordinal)));
            }
            catch (Exception exception)
            {
                var inner = exception.InnerException ?? exception;
                lines.Add($"{label}: {single.Name} threw {inner.GetType().Name}: {inner.Message.Split('\n')[0].Trim()}");
            }
        }

        return lines;
    }

    private static async Task<Dictionary<int, object?[]>> Database<TItem>(Expression<Func<CollectionRow<TItem>, FuzzOut>> projection)
        where TItem : class
    {
        await using var db = new CollectionProbeDbContext();
        var results = await db.Set<CollectionRow<TItem>>().AsNoTracking().Select(projection).ToListAsync();
        return results.ToDictionary(r => r.Id, r => r.Values);
    }

    private static List<string> Differences<TItem>(
        string label, List<CollectionRow<TItem>> rows, Func<CollectionRow<TItem>, FuzzOut> memory,
        Dictionary<int, object?[]> database, string[] names)
        where TItem : class
    {
        var lines = new List<string>();
        foreach (var row in rows)
        {
            var expected = memory(row).Values;
            if (!database.TryGetValue(row.Id, out var actual))
            {
                lines.Add($"{label}: row {row.Value} not returned");
                continue;
            }

            for (var index = 0; index < names.Length; index++)
            {
                if (Equals(expected[index], actual[index])) continue;
                lines.Add($"{label}: {names[index]} for {row.Value} → memory={expected[index]}, database={actual[index]}");
            }
        }

        return lines;
    }

    /// <summary><c>r => new FuzzOut { Id = r.Id, Values = [op1(collection, r.Value), …] }</c>.</summary>
    private static Expression<Func<CollectionRow<TItem>, FuzzOut>> Projection<TItem>(
        Expression collection, (string Name, LambdaExpression Op)[] asked)
        where TItem : class
    {
        var row    = Expression.Parameter(typeof(CollectionRow<TItem>), "r");
        var column = Expression.Property(row, nameof(CollectionRow<TItem>.Value));
        var body   = Expression.MemberInit(
            Expression.New(typeof(FuzzOut)),
            Expression.Bind(typeof(FuzzOut).GetProperty(nameof(FuzzOut.Id))!, Expression.Property(row, nameof(CollectionRow<TItem>.Id))),
            Expression.Bind(
                typeof(FuzzOut).GetProperty(nameof(FuzzOut.Values))!,
                Expression.NewArrayInit(
                    typeof(object),
                    asked.Select(a => Expression.Convert(FuzzEngine.Splice(a.Op, collection, column), typeof(object))))));
        return Expression.Lambda<Func<CollectionRow<TItem>, FuzzOut>>(body, row);
    }

    /// <summary>
    /// <c>collection.Contains(c)</c> as a consumer writes it: the instance method where the form has one
    /// (<c>List</c>, <c>HashSet</c>), <c>Enumerable.Contains</c> otherwise.
    /// </summary>
    private static LambdaExpression ContainsOf<TItem>(MemberExpression collection)
    {
        var list     = Expression.Parameter(collection.Type, "list");
        var c        = Expression.Parameter(typeof(TItem), "c");
        var instance = collection.Type.GetMethod(nameof(Enumerable.Contains), [typeof(TItem)]);
        Expression call = instance is not null && !collection.Type.IsArray
                              ? Expression.Call(list, instance, c)
                              : Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(TItem)], list, c);
        return Expression.Lambda(call, list, c);
    }

    /// <summary><c>(list, c) => list.Any(w => pred(w, c))</c>, or <c>list.Count()</c> without a predicate.</summary>
    private static LambdaExpression Aggregate<TItem>(string method, LambdaExpression? pred)
    {
        var list = Expression.Parameter(typeof(IEnumerable<TItem>), "list");
        var c    = Expression.Parameter(typeof(TItem), "c");
        var w    = Expression.Parameter(typeof(TItem), "w");
        Expression call = pred is null
                              ? Expression.Call(typeof(Enumerable), method, [typeof(TItem)], list)
                              : Expression.Call(typeof(Enumerable), method, [typeof(TItem)], list,
                                                Expression.Lambda(FuzzEngine.Splice(pred, w, c), w));
        return Expression.Lambda(call, list, c);
    }

    private static LambdaExpression Pred<TItem>(Expression<Func<TItem, TItem, bool>> pred) => pred;

    /// <summary><c>==</c> through the type's own operator, which a generic lambda cannot name.</summary>
    private static LambdaExpression Equal<TItem>()
    {
        var (w, c) = (Expression.Parameter(typeof(TItem), "w"), Expression.Parameter(typeof(TItem), "c"));
        return Expression.Lambda(Expression.Equal(w, c), w, c);
    }

    /// <summary><c>IsEmpty</c> is an instance property of each set type, not of an interface, so it is reached by name.</summary>
    private static LambdaExpression IsEmpty<TSet>()
    {
        var (w, c) = (Expression.Parameter(typeof(TSet), "w"), Expression.Parameter(typeof(TSet), "c"));
        return Expression.Lambda(Expression.Property(w, nameof(StringSet.IsEmpty)), w, c);
    }
}

/// <summary>One value of a range or value set type, in the table of that type.</summary>
public sealed class CollectionRow<TValue> where TValue : class
{
    public int Id { get; set; }

    public TValue Value { get; set; } = null!;
}

/// <summary>
/// The tables of <see cref="CapturedCollectionProbeTests"/>, one per discovered type, in a database of
/// their own so the rows never meet another test's.
/// </summary>
public sealed class CollectionProbeDbContext : DbContext
{
    internal const string DatabaseName = "valueranges_collections";

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            new NpgsqlConnectionStringBuilder(ContainerLifecycle.ConnectionString) { Database = DatabaseName }.ConnectionString,
            npgsql => npgsql.UseValueRangesNodaTime()
                            .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var type in FuzzTypes.Ranges.Select(t => t.Range).Concat(FuzzTypes.Sets.Select(t => t.Set)))
        {
            var row = modelBuilder.Entity(typeof(CollectionRow<>).MakeGenericType(type));
            row.ToTable($"collection_{FuzzTypes.Name(type)}");
            row.Property(nameof(CollectionRow<StringSet>.Id)).ValueGeneratedNever();
        }

        modelBuilder.HasPostgresRange("timerange", "time");
        modelBuilder.HasPostgresRange("float8range", "double precision");
        modelBuilder.HasPostgresRange("float4range", "real");
    }
}

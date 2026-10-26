using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.Conventions.Tests;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>Union</c>, <c>Add</c>, <c>Intersect</c> and <c>Except</c> over every value set family PostgreSQL
/// sorts as memory does, executed and compared with the same expression in memory (#478).
/// </summary>
/// <remarks>
/// <para>
/// The SQL guard builds each operation as a canonicalizing subquery where the consumer sees order or
/// duplicates, and as a cheaper multiset where it cannot. So each is asked in both kinds of place: under
/// <c>==</c> and <c>Count</c>, which see the canonical form, as an <c>ExecuteUpdate</c> value, which stores
/// it, and in a projection and under the containment operators and <c>IsEmpty</c>, which get the multiset.
/// The stored value is then compared on the server with the expected set (<c>=</c> sees order and
/// duplicates) and loaded.
/// </para>
/// <para>
/// Every pair of subsets of three probes is stored three times: canonical, as EF Core writes it; out of
/// order and twice, as another tool might; and with a NULL element on each side, which the canonical form
/// drops because a set never holds one. A NULL-bearing array cannot be loaded and does not take part in
/// containment as memory would (<c>docs/efcore.md#rows-written-by-other-tools</c>), so those rows are asked
/// only where the canonical form decides the answer.
/// </para>
/// <para>
/// Whether the first copy of each value is the one kept, as <c>ValueSetCore</c> keeps it, shows only in a
/// stored value's text, and only for the families whose equal values can be written differently:
/// <see cref="TheFirstCopyOfEachValue_IsKept_AsInMemory"/>.
/// </para>
/// </remarks>
[TestClass]
public sealed class CanonicalSetOperationParityTests
{
    /// <summary>The same pairs, stored out of order and twice.</summary>
    private const int Foreign = 1000;

    /// <summary>The same pairs, with a NULL element on each side.</summary>
    private const int WithNull = 2000;

    /// <summary>Every discovered family but text, which PostgreSQL sorts by the database collation (#479).</summary>
    private static readonly IReadOnlyList<Type> Families =
        [.. FuzzTypes.Sets.Where(t => t.Primitive != typeof(string)).Select(t => t.Set)];

    [TestMethod]
    public async Task EveryFamilyButText_AnswersAsInMemory()
    {
        ContainerLifecycle.RequireServer();
        Assert.IsGreaterThanOrEqualTo(25, Families.Count, "value set type discovery found too few types");

        var connectionString = await Database();
        var lines = await ForEachFamily(nameof(SweepFamily), Families, connectionString);

        Candidates.Verdict(nameof(CanonicalSetOperationParityTests), lines,
            "A value set Union, Add, Intersect or Except answers differently in the database and in memory",
            method: nameof(EveryFamilyButText_AnswersAsInMemory));
    }

    /// <summary>
    /// <c>DecimalSet</c> keeps whichever of <c>1.0</c> and <c>1.00</c> came first, and the float sets
    /// whichever of <c>-0</c> and <c>0</c>. Thirty values a side, so that a <c>DISTINCT</c> that keeps any
    /// copy is caught by PostgreSQL's unstable sort rather than passing by luck.
    /// </summary>
    [TestMethod]
    public async Task TheFirstCopyOfEachValue_IsKept_AsInMemory()
    {
        ContainerLifecycle.RequireServer();
        var families = FuzzTypes.Sets.Where(t => t.Primitive == typeof(decimal) || t.Primitive == typeof(double) || t.Primitive == typeof(float))
                                     .Select(t => t.Set).ToList();
        Assert.AreEqual(6, families.Count, "DecimalSet, Float64Set and Float32Set, and their arities");

        var connectionString = await Database();
        var lines = await ForEachFamily(nameof(SweepRepresentations), families, connectionString);

        Candidates.Verdict(nameof(CanonicalSetOperationParityTests), lines,
            "A value set operation keeps another copy of a value than memory keeps",
            method: nameof(TheFirstCopyOfEachValue_IsKept_AsInMemory));
    }

    private static async Task<string> Database()
    {
        var connectionString = await CompositionDatabase.Create("canonical_set_operations");
        await using var create = new CanonicalContext(connectionString);
        await create.Database.EnsureCreatedAsync();
        return connectionString;
    }

    /// <summary>
    /// Two families at a time, and the database's pool emptied afterwards: the suite runs its methods in
    /// parallel against one server, whose 100 connections the database-per-test sweeps share.
    /// </summary>
    private static async Task<List<string>> ForEachFamily(string sweep, IEnumerable<Type> families, string connectionString)
    {
        var method = typeof(CanonicalSetOperationParityTests).GetMethod(sweep, BindingFlags.NonPublic | BindingFlags.Static)!;
        var lines  = new ConcurrentBag<string>();
        try
        {
            await Parallel.ForEachAsync(families, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (set, _) =>
            {
                var element = set.GetInterfaces().Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueSet<>)).GetGenericArguments()[0];
                foreach (var line in await (Task<List<string>>)method.MakeGenericMethod(set, element).Invoke(null, [connectionString])!)
                    lines.Add(line);
            });
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(connection);
        }

        return [.. lines.Order(StringComparer.Ordinal)];
    }

    // =========================================================================
    // Every family
    // =========================================================================

    private static async Task<List<string>> SweepFamily<TSet, T>(string connectionString)
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var name    = FuzzTypes.Name(typeof(TSet));
        var probes  = SetProbes.For<T>().Take(3).ToArray();
        var subsets = Enumerable.Range(0, 1 << probes.Length)
                                .Select(mask => TSet.From(probes.Where((_, i) => (mask & 1 << i) != 0)))
                                .ToArray();
        var pairs   = (from l in subsets from r in subsets select (l, r)).ToArray();

        await using (var write = new CanonicalContext(connectionString))
        {
            for (var i = 0; i < pairs.Length; i++)
            foreach (var offset in new[] { 0, Foreign, WithNull })
            {
                write.Add(new CanonicalRow<TSet>
                {
                    Id = offset + i, Left = pairs[i].l, Right = pairs[i].r,
                    Union = TSet.Empty, Added = TSet.Empty, Intersect = TSet.Empty, Except = TSet.Empty, Stored = TSet.Empty
                });
            }

            await write.SaveChangesAsync();
        }

        // The expectations come from the pairs as stored: the column keeps a temporal value to the
        // microsecond and a timestamptz in UTC (docs/efcore.md#store-timestamp-ranges), and memory has
        // to compute over what the database holds. The added element is the first probe as stored too.
        Dictionary<int, CanonicalRow<TSet>> expected;
        T element;
        await using (var update = new CanonicalContext(connectionString))
        {
            var stored = await update.Set<CanonicalRow<TSet>>().OrderBy(r => r.Id).ToListAsync();
            element = stored.Select(r => r.Left).First(l => l.Values.Length == 1).Values[0];
            foreach (var r in stored)
            {
                (r.Union, r.Intersect, r.Except, r.Added) = (r.Left.Union(r.Right), r.Left.Intersect(r.Right), r.Left.Except(r.Right), r.Left.Add(element));
            }

            await update.SaveChangesAsync();
            expected = stored.ToDictionary(r => r.Id);
        }

        // What another tool might store: the same sets out of order and twice, and with a NULL element.
        await using (var write = new CanonicalContext(connectionString))
        {
            var table = Table<TSet>();
            var foreignRows = $"""
                UPDATE "{table}" SET
                    "Left"  = ARRAY(SELECT x FROM unnest("Left") WITH ORDINALITY AS t(x, n) ORDER BY n DESC) || "Left",
                    "Right" = ARRAY(SELECT x FROM unnest("Right") WITH ORDINALITY AS t(x, n) ORDER BY n DESC) || "Right"
                WHERE "Id" >= {Foreign} AND "Id" < {WithNull}
                """;
            var nullRows = $"""
                UPDATE "{table}" SET "Left" = array_append("Left", NULL), "Right" = array_prepend(NULL, "Right")
                WHERE "Id" >= {WithNull}
                """;
            await write.Database.ExecuteSqlRawAsync(foreignRows);
            await write.Database.ExecuteSqlRawAsync(nullRows);
        }

        string Row(int id) => $"{name} row {id} ({(id >= WithNull ? "with NULL" : id >= Foreign ? "out of order, twice" : "canonical")}: "
                            + $"{expected[id].Left} with {expected[id].Right})";

        // The four operations, over the stored pair.
        Expression<Func<CanonicalRow<TSet>, TSet>> union     = r => r.Left.Union(r.Right);
        Expression<Func<CanonicalRow<TSet>, TSet>> add       = r => r.Left.Add(element);
        Expression<Func<CanonicalRow<TSet>, TSet>> intersect = r => r.Left.Intersect(r.Right);
        Expression<Func<CanonicalRow<TSet>, TSet>> except    = r => r.Left.Except(r.Right);
        var operations = new (string Name, Expression<Func<CanonicalRow<TSet>, TSet>> Server, Func<CanonicalRow<TSet>, TSet> Memory, Expression<Func<CanonicalRow<TSet>, TSet>> Column)[]
        {
            ("Union",     union,     e => e.Union,     r => r.Union),
            ("Add",       add,       e => e.Added,     r => r.Added),
            ("Intersect", intersect, e => e.Intersect, r => r.Intersect),
            ("Except",    except,    e => e.Except,    r => r.Except)
        };

        var row      = Expression.Parameter(typeof(CanonicalRow<TSet>), "r");
        var failures = new List<string>();

        // Where the canonical form decides: == against the expected set, and Count. Every row.
        var canonical = new List<(string Name, Expression Server, Func<CanonicalRow<TSet>, object?> Memory)>();
        foreach (var (op, server, memory, column) in operations)
        {
            var value = Splice(server, row);
            canonical.Add(($"{op} == expected", Expression.Equal(value, Splice(column, row)), _ => true));
            canonical.Add(($"{op}.Count", Expression.Property(value, nameof(StringSet.Count)), e => memory(e).Values.Length));
        }

        await Ask(connectionString, row, canonical, int.MaxValue, "unnest(", expected, Row, failures);

        // Where the multiset suffices: the projection, containment and IsEmpty. Not over a NULL element.
        var multiset = new List<(string Name, Expression Server, Func<CanonicalRow<TSet>, object?> Memory)>();
        foreach (var (op, server, memory, _) in operations)
        {
            var value = Splice(server, row);
            multiset.Add((op, value, e => memory(e)));
            multiset.Add(($"{op}.IsEmpty", Expression.Property(value, nameof(StringSet.IsEmpty)), e => memory(e).Values.IsEmpty));
            multiset.Add(($"{op}.IsSupersetOf(Left)", Call(nameof(ValueSetExtensions.IsSupersetOf), value, Expression.Property(row, nameof(CanonicalRow<TSet>.Left))),
                          e => memory(e).IsSupersetOf(e.Left)));
            multiset.Add(($"{op}.Overlaps(Right)", Call(nameof(ValueSetExtensions.Overlaps), value, Expression.Property(row, nameof(CanonicalRow<TSet>.Right))),
                          e => memory(e).Overlaps(e.Right)));
        }

        await Ask(connectionString, row, multiset, WithNull, "array_cat(", expected, Row, failures);

        // Stored: compared on the server as an array, then loaded. Every row.
        foreach (var (op, server, memory, column) in operations)
        {
            await using (var update = new CanonicalContext(connectionString))
                await update.Set<CanonicalRow<TSet>>().ExecuteUpdateAsync(s => s.SetProperty(r => r.Stored, server));

            var stored = Expression.Property(row, nameof(CanonicalRow<TSet>.Stored));
            await Ask<TSet>(connectionString, row,
                [
                    ($"{op} stored == expected", Expression.Equal(stored, Splice(column, row)), _ => true),
                    ($"{op} stored, Count", Expression.Property(stored, nameof(StringSet.Count)), e => memory(e).Values.Length),
                    ($"{op} stored, loaded", stored, e => memory(e))
                ],
                int.MaxValue, "\"Stored\"", expected, Row, failures);
        }

        return failures;
    }

    /// <summary>Projects every question for every row below <paramref name="below"/>, and compares each answer with memory.</summary>
    private static async Task Ask<TSet>(
        string connectionString, ParameterExpression row, IReadOnlyList<(string Name, Expression Server, Func<CanonicalRow<TSet>, object?> Memory)> questions,
        int below, string mustRunAs, Dictionary<int, CanonicalRow<TSet>> expected, Func<int, string> describe, List<string> failures)
        where TSet : class
    {
        var projection = Expression.Lambda<Func<CanonicalRow<TSet>, FuzzOut>>(
            Expression.MemberInit(
                Expression.New(typeof(FuzzOut)),
                Expression.Bind(typeof(FuzzOut).GetProperty(nameof(FuzzOut.Id))!, Expression.Property(row, nameof(CanonicalRow<TSet>.Id))),
                Expression.Bind(typeof(FuzzOut).GetProperty(nameof(FuzzOut.Values))!,
                                Expression.NewArrayInit(typeof(object), questions.Select(q => Expression.Convert(q.Server, typeof(object)))))),
            row);

        List<FuzzOut> answers;
        try
        {
            await using var read = new CanonicalContext(connectionString);
            var query = read.Set<CanonicalRow<TSet>>().AsNoTracking().Where(r => r.Id < below).Select(projection);

            // EF Core evaluates a projection it cannot translate on the client, where memory would only
            // agree with itself: the SQL has to carry the form under test.
            var sql = query.ToQueryString();
            if (!sql.Contains(mustRunAs, StringComparison.Ordinal))
                failures.Add($"{typeof(TSet).Name} [{string.Join(", ", questions.Select(q => q.Name))}]: the SQL does not contain {mustRunAs}:\n{sql}");

            answers = await query.ToListAsync();
        }
        catch (Exception exception)
        {
            failures.Add($"{typeof(TSet).Name} [{string.Join(", ", questions.Select(q => q.Name))}]: {exception.GetType().Name}: {exception.Message.Split('\n')[0]}");
            return;
        }

        var asked = expected.Keys.Count(id => id < below);
        if (answers.Count != asked)
            failures.Add($"{typeof(TSet).Name} [{string.Join(", ", questions.Select(q => q.Name))}]: {answers.Count} rows answered of {asked}");

        foreach (var answer in answers)
        for (var index = 0; index < questions.Count; index++)
        {
            var memory = questions[index].Memory(expected[answer.Id]);
            if (!Equals(memory, answer.Values[index]))
                failures.Add($"{describe(answer.Id)} {questions[index].Name}: memory {memory}, database {answer.Values[index] ?? "NULL"}");
        }
    }

    // =========================================================================
    // The first copy of each value
    // =========================================================================

    private static async Task<List<string>> SweepRepresentations<TSet, T>(string connectionString)
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        static TSet Parse(IEnumerable<string> texts) => TSet.From(texts.Select(text => TSet.ParseValue(text, CultureInfo.InvariantCulture)));

        var name   = FuzzTypes.Name(typeof(TSet));
        var values = Enumerable.Range(1, 30).ToArray();

        // Decimals by scale; floats by the sign of zero, the one value a float can write two ways.
        var (one, other) = FuzzTypes.Sets.Single(t => t.Set == typeof(TSet)).Primitive == typeof(decimal)
                               ? (Parse(values.Select(v => $"{v}.0")), Parse(values.Select(v => $"{v}.00").Append("31")))
                               : (Parse(values.Select(v => $"{v}").Prepend("-0")), Parse(values.Select(v => $"{v}").Prepend("0").Append("31")));

        var rows = new[] { (Id: 1, Left: one, Right: other), (Id: 2, Left: other, Right: one) }
                  .Select(p => new CanonicalRow<TSet>
                   {
                       Id = p.Id, Left = p.Left, Right = p.Right,
                       Union = TSet.Empty, Added = TSet.Empty, Intersect = TSet.Empty, Except = TSet.Empty, Stored = TSet.Empty
                   })
                  .ToList();

        await using (var write = new CanonicalContext(connectionString))
        {
            write.AddRange(rows);
            await write.SaveChangesAsync();
        }

        var element = other.Values[0];
        var failures = new List<string>();
        foreach (var (op, server, memory) in new (string, Expression<Func<CanonicalRow<TSet>, TSet>>, Func<CanonicalRow<TSet>, TSet>)[]
                 {
                     ("Union",     r => r.Left.Union(r.Right),     e => e.Left.Union(e.Right)),
                     ("Add",       r => r.Left.Add(element),       e => e.Left.Add(element)),
                     ("Intersect", r => r.Left.Intersect(r.Right), e => e.Left.Intersect(e.Right))
                 })
        {
            await using (var update = new CanonicalContext(connectionString))
                await update.Set<CanonicalRow<TSet>>().ExecuteUpdateAsync(s => s.SetProperty(r => r.Stored, server));

            await using var read = new CanonicalContext(connectionString);
            foreach (var stored in await read.Set<CanonicalRow<TSet>>().AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Stored }).ToListAsync())
            {
                var source = rows.Single(r => r.Id == stored.Id);
                var wanted = memory(source).ToString();
                if (stored.Stored.ToString() != wanted)
                    failures.Add($"{name} row {stored.Id} {op}: memory {wanted}, database {stored.Stored}");
            }
        }

        return failures;
    }

    // =========================================================================
    // The model and the expression helpers
    // =========================================================================

    private static string Table<TSet>() => $"canonical_{FuzzTypes.Name(typeof(TSet))}";

    private static Expression Splice<TSet>(Expression<Func<CanonicalRow<TSet>, TSet>> lambda, ParameterExpression row) where TSet : class
        => new Replace(lambda.Parameters[0], row).Visit(lambda.Body);

    /// <summary>A predicate of <see cref="ValueSetExtensions"/> over two sets of one family.</summary>
    private static Expression Call(string predicate, Expression set, Expression other)
    {
        var element = set.Type.GetInterfaces().Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueSet<>)).GetGenericArguments()[0];
        var method  = typeof(ValueSetExtensions).GetMethods().Single(m => m.Name == predicate && m.GetParameters().Length == 2)
                                                .MakeGenericMethod(set.Type, element);
        return Expression.Call(method, set, other);
    }

    private sealed class Replace(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }

    /// <summary>The tables, one per family, in a database of their own.</summary>
    private sealed class CanonicalContext(string connectionString) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseValueRangesNodaTime());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var set in Families)
            {
                var entity = modelBuilder.Entity(typeof(CanonicalRow<>).MakeGenericType(set));
                entity.ToTable($"canonical_{FuzzTypes.Name(set)}");
                entity.Property(nameof(CanonicalRow<StringSet>.Id)).ValueGeneratedNever();
            }
        }
    }
}

/// <summary>A stored pair of one family, the expected results of each operation, and a column to store one in.</summary>
public sealed class CanonicalRow<TSet> where TSet : class
{
    public int Id { get; set; }

    public TSet Left { get; set; } = null!;

    public TSet Right { get; set; } = null!;

    public TSet Union { get; set; } = null!;

    public TSet Added { get; set; } = null!;

    public TSet Intersect { get; set; } = null!;

    public TSet Except { get; set; } = null!;

    public TSet Stored { get; set; } = null!;
}

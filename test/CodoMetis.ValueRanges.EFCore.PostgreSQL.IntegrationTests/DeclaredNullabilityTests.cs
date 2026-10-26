using System.Collections.Concurrent;
using System.Linq.Expressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition.CompositionMembers;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The nullability each translated function declares to EF Core, against what PostgreSQL computes:
/// per function, and for the complete translated expression (#380, the static half of #362).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SqlFunctionExpression"/> tells EF Core when its result can be NULL. With an argument
/// declared as propagating nullability, EF Core takes the result to be NULL exactly when a propagating
/// argument is NULL: it rewrites <c>f(x) IS NULL</c> to <c>x IS NULL</c>, folds it to <c>FALSE</c> for
/// a required column, and drops the <c>COALESCE</c> of a <c>??</c>. #349 was <c>lower</c> declared that
/// way, though <c>lower('empty')</c> is NULL; #360 is <c>array_cat</c>, though
/// <c>array_cat(NULL, '{a}')</c> is <c>{a}</c>.
/// </para>
/// <para>
/// The first test records every function the translators emit for every discovered call (through a
/// recording decorator over the plugin's own SQL processor, so the plugin's chain stays as it is) and
/// checks each declaration against <c>pg_proc.proisstrict</c> and against the server, over typed NULLs
/// and samples that include <c>empty</c>, <c>(,)</c> and <c>{}</c>. The second checks the complete
/// expression a call becomes, <c>CASE</c> wrappers and arithmetic included: EF Core's own null test,
/// which follows the declarations, selects exactly the rows whose projected value is NULL.
/// </para>
/// </remarks>
[TestClass]
public sealed class DeclaredNullabilityTests
{
    private static DateOnly March(int day) => new(2026, 3, day);

    private static DateRange Days(int from, int to) => DateRange.CreateFinite(March(from), March(to), true, true);

    private static RangeSet<DateRange, DateOnly> Blocks(params DateRange[] ranges) => RangeSet<DateRange, DateOnly>.From(ranges);

    private static readonly ParameterExpression Row = Expression.Parameter(typeof(DeclaredRow), "r");

    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "null", [2] = "[1,10]", [3] = "empty", [4] = "(,)", [5] = "[20,)", [6] = "(,10]"
    };

    /// <summary>Non-NULL samples per store type, chosen to reach the NULLs a function returns for non-NULL input.</summary>
    private static readonly Dictionary<string, string[]> Samples = new()
    {
        ["daterange"]      = ["'[2026-03-01,2026-03-10]'::daterange", "'empty'::daterange", "'(,)'::daterange", "'[2026-03-20,)'::daterange", "'(,2026-03-10]'::daterange"],
        ["datemultirange"] = ["'{}'::datemultirange", "'{[2026-03-01,2026-03-10]}'::datemultirange", "'{(,)}'::datemultirange", "'{[2026-03-20,2026-03-25],[2026-03-27,)}'::datemultirange"],
        ["date"]           = ["DATE '2026-03-07'"],
        ["timestamp without time zone"] = ["TIMESTAMP '2026-03-07 00:00'"],
        ["text[]"]         = ["'{}'::text[]", "'{a}'::text[]", "'{a,b}'::text[]"],
        ["text"]           = ["'a'::text"],
        ["integer"]        = ["1"]
    };

    private static async Task<string> Database()
    {
        var connectionString = await CompositionDatabase.Create("declared_nullability");
        await using var seed = new DeclaredContext(connectionString, null);
        await seed.Database.EnsureCreatedAsync();
        (DateRange? Range, RangeSet<DateRange, DateOnly>? Set, StringSet? Tags, DateOnly? Day, string? Label)[] values =
        [
            (null, null, null, null, null),
            (Days(1, 10), Blocks(Days(1, 10)), StringSet.From(["a"]), March(7), "a"),
            (DateRange.Empty, Blocks(), StringSet.Empty, March(3), "b"),
            (DateRange.Infinite, Blocks(DateRange.Infinite), StringSet.From(["a", "b"]), null, "a"),
            (DateRange.CreateUnboundedEnd(March(20), true), Blocks(Days(20, 25), DateRange.CreateUnboundedEnd(March(27), true)), StringSet.From(["b"]), March(20), null),
            (DateRange.CreateUnboundedStart(March(10), true), Blocks(DateRange.CreateUnboundedStart(March(10), true)), StringSet.From(["a"]), March(31), "c")
        ];
        seed.Rows.AddRange(values.Select((v, index) => new DeclaredRow
        {
            Id = index + 1, Period = v.Range, Required = v.Range ?? DateRange.Empty, Blocks = v.Set, RequiredBlocks = v.Set ?? Blocks(),
            Tags = v.Tags, RequiredTags = v.Tags ?? StringSet.Empty, Day = v.Day, Label = v.Label
        }));
        await seed.SaveChangesAsync();
        return connectionString;
    }

    private static Expression Column(string name) => Expression.Property(Row, name);

    /// <summary>
    /// The receiver is a nullable or a required column; the operand a captured value, a captured
    /// <see langword="null"/>, or for an element a nullable column read through <c>.Value</c>.
    /// </summary>
    private static Operand[] Slot(Type type, int position) => Kind(type) switch
    {
        "range" => position == 0
            ? [new("r.Period", Column(nameof(DeclaredRow.Period)), true), new("r.Required", Column(nameof(DeclaredRow.Required)), false)]
            : [new("@[3,8]", Captured(Days(3, 8)), false), new("@null", Captured<DateRange>(null!), true)],
        "set" => position == 0
            ? [new("r.Blocks", Column(nameof(DeclaredRow.Blocks)), true), new("r.RequiredBlocks", Column(nameof(DeclaredRow.RequiredBlocks)), false)]
            : [new("@{[3,8]}", Captured(Blocks(Days(3, 8))), false), new("@null", Captured<RangeSet<DateRange, DateOnly>>(null!), true)],
        "values" => position == 0
            ? [new("r.Tags", Column(nameof(DeclaredRow.Tags)), true), new("r.RequiredTags", Column(nameof(DeclaredRow.RequiredTags)), false)]
            : [new("@{a}", Captured(StringSet.From(["a"])), false), new("@null", Captured<StringSet>(null!), true)],
        "element" => [new("r.Day.Value", Expression.Property(Column(nameof(DeclaredRow.Day)), nameof(Nullable<>.Value)), true), new("@2026-03-07", Captured(March(7)), false)],
        "text"    => [new("r.Label", Column(nameof(DeclaredRow.Label)), true), new("@a", Captured("a"), false)],
        _         => []
    };

    /// <summary>
    /// Every discovered call, the range and calendar factories over the nullable element column, the
    /// calendar factories' integer forms, and the two aggregates.
    /// </summary>
    private static List<Call> Discovered()
    {
        var calls = Calls(Slot, _ => true);
        var day   = Expression.Property(Column(nameof(DeclaredRow.Day)), nameof(Nullable<>.Value));
        calls.Add(new Call("DateRange.CreateFinite(@2026-03-01, r.Day.Value)",
                           Expression.Call(typeof(DateRange), nameof(DateRange.CreateFinite), null, Captured(March(1)), day, Expression.Constant(true), Expression.Constant(true))));
        calls.Add(new Call("DateRange.CreateUnboundedStart(r.Day.Value)",
                           Expression.Call(typeof(DateRange), nameof(DateRange.CreateUnboundedStart), null, day, Expression.Constant(true))));
        calls.Add(new Call("DateRange.CreateUnboundedEnd(r.Day.Value)",
                           Expression.Call(typeof(DateRange), nameof(DateRange.CreateUnboundedEnd), null, day, Expression.Constant(true))));

        // The calendar factories over the nullable element column, and the integer forms over the id (#191).
        var id = Column(nameof(DeclaredRow.Id));
        foreach (var name in new[] { nameof(DateRange.Month), nameof(DateRange.Quarter), nameof(DateRange.Year), nameof(DateRange.IsoWeek) })
        {
            calls.Add(new Call($"DateRange.{name}(r.Day.Value)", Expression.Call(typeof(DateRange), name, null, day)));
            calls.Add(name == nameof(DateRange.Year)
                          ? new Call("DateRange.Year(r.Id)", Expression.Call(typeof(DateRange), name, null, id))
                          : new Call($"DateRange.{name}(r.Id, 1)", Expression.Call(typeof(DateRange), name, null, id, Expression.Constant(1))));
        }

        return calls;
    }

    /// <summary>
    /// Every function the translators emit, declared as PostgreSQL evaluates it: an argument declared as
    /// propagating nullability belongs to a strict function, and the result is NULL exactly when a
    /// propagating argument is NULL.
    /// </summary>
    [TestMethod]
    public async Task EveryEmittedFunction_IsDeclaredAsPostgresEvaluatesIt()
    {
        ContainerLifecycle.RequireServer();

        var connectionString = await Database();
        var recorder         = new ConcurrentQueue<Expression>();
        await using var context = new DeclaredContext(connectionString, recorder);

        // Translate every call as a projection and the aggregates per group, and record the SQL trees.
        // ToQueryString runs the processor without executing, so a member EF Core evaluates on the
        // client never runs over a NULL.
        foreach (var call in Discovered())
        {
            var projection = Expression.Lambda<Func<DeclaredRow, object>>(Expression.Convert(call.Body, typeof(object)), Row);
            try { _ = context.Rows.Select(projection).ToQueryString(); }
            catch (InvalidOperationException) { }
        }

        _ = context.Rows.GroupBy(r => r.Id % 2).Select(g => new { A = g.Select(r => r.Period!).RangeAgg(), B = g.Select(r => r.Period!).RangeIntersectAgg(),
                                                                  C = g.Select(r => r.Blocks!).RangeAgg(), D = g.Select(r => r.Blocks!).RangeIntersectAgg() })
                       .ToQueryString();

        var functions = new Dictionary<string, Declared>();
        foreach (var tree in recorder)
            new FunctionCollector(functions).Visit(tree);

        var failures = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var function in functions.Values.OrderBy(f => f.Signature, StringComparer.Ordinal))
        {
            var catalogue = await Strictness(connection, function);
            if (catalogue is null)
            {
                failures.Add($"{function.Signature}: no pg_proc entry takes these argument types");
                continue;
            }

            if (function.Propagating.Count == 0 || catalogue.Value.Aggregate) continue;

            if (!catalogue.Value.Strict)
                failures.Add($"{function.Signature}: declared NULL for a NULL argument {string.Join(" or ", function.Propagating)}, but pg_proc.proisstrict is false");

            // Every argument NULL or a sample, constants kept: NULL exactly when a propagating one is NULL.
            foreach (var arguments in Product(function.Arguments.Select((a, i) => a.Constant is { } literal ? [literal] : (string[])["NULL", .. Samples[a.StoreType]]).ToList()))
            {
                var expectNull = function.Propagating.Any(i => arguments[i] == "NULL");
                var sql        = $"{function.Name}({string.Join(", ", arguments.Select((a, i) => a == "NULL" ? $"NULL::{function.Arguments[i].StoreType}" : a))})";
                await using var command = new NpgsqlCommand($"SELECT ({sql})::text", connection);
                var result = await command.ExecuteScalarAsync();
                if ((result is null or DBNull) != expectNull)
                    failures.Add($"{function.Signature}: declared {(expectNull ? "NULL" : "not NULL")} for {sql}, which is {(result is string text ? $"'{text}'" : "NULL")}");
            }
        }

        // Seventeen today: isempty, lower_inf, upper_inf, lower_inc, lower, upper, range_merge, cardinality,
        // array_remove, array_cat, the range and multirange constructors, the two aggregates, and the
        // calendar factories' date_trunc, make_date and date_part (#191).
        Assert.IsGreaterThanOrEqualTo(15, functions.Select(f => f.Value.Name).Distinct().Count(),
                                      $"Only {string.Join(", ", functions.Values.Select(f => f.Name).Distinct())} were recorded");

        Candidates.Verdict(nameof(DeclaredNullabilityTests), failures,
                           "A translated function's declared nullability disagrees with PostgreSQL",
                           method: nameof(EveryEmittedFunction_IsDeclaredAsPostgresEvaluatesIt));
    }

    /// <summary>
    /// For every value a discovered call produces, over a nullable and a required column and a captured
    /// value or <see langword="null"/>: <c>Where(v == null)</c> and <c>Where(v != null)</c>, which EF Core
    /// rewrites from the declarations, select exactly the rows <c>Select(v)</c> reads as NULL and not NULL.
    /// </summary>
    [TestMethod]
    public async Task EveryTranslatedValue_IsNullInAFilterExactlyWhereItIsNullInAProjection()
    {
        ContainerLifecycle.RequireServer();

        var connectionString = await Database();
        await using var context = new DeclaredContext(connectionString, null);
        var loaded = await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync();

        var failures = new List<string>();
        var judged   = 0;
        foreach (var call in Discovered().Where(c => !c.Predicate))
        {
            if (await CompositionRunner.Judge("declared", call, Row, context.Rows.AsNoTracking(), loaded, r => r.Id, id => Names[id], failures,
                                              reference: false) == Outcome.Compared)
                judged++;
        }


        Assert.IsGreaterThanOrEqualTo(80, judged, $"Only {judged} values were judged");

        Candidates.Verdict(nameof(DeclaredNullabilityTests), failures,
                           "A translated value is NULL in a filter and not in a projection, or the other way",
                           method: nameof(EveryTranslatedValue_IsNullInAFilterExactlyWhereItIsNullInAProjection));
    }

    private static async Task<(bool Strict, bool Aggregate)?> Strictness(NpgsqlConnection connection, Declared function)
    {
        // ARRAY((SELECT …)) is the array constructor, not a pg_proc function: never NULL, {} over no rows.
        // The SQL guard builds it for a set operation (#478, #485).
        if (function.Name is "ARRAY") return (Strict: false, Aggregate: false);
        // GREATEST and LEAST are conditional expressions, not pg_proc functions: they skip a NULL argument and
        // are NULL only when every argument is (PostgreSQL's "Conditional Expressions"). Clamp emits them (#486).
        if (function.Name is "GREATEST" or "LEAST") return (Strict: false, Aggregate: false);

        // The overloads of that name and arity, an exact argument type preferred over a polymorphic one.
        await using var command = new NpgsqlCommand(
            """
            SELECT p.proisstrict, p.prokind = 'a', p.proargtypes::regtype[]::text[]
            FROM pg_proc p
            WHERE p.proname = $1 AND p.pronargs = $2 AND p.provariadic = 0
            """, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = function.Name });
        command.Parameters.Add(new NpgsqlParameter { Value = (short)function.Arguments.Count });

        var candidates = new List<(bool Strict, bool Aggregate, int Exact)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var types = reader.GetFieldValue<string[]>(2);
            var exact = 0;
            var fits  = true;
            for (var i = 0; i < types.Length; i++)
            {
                if (types[i] == function.Arguments[i].StoreType) exact++;
                else if (!types[i].StartsWith("any", StringComparison.Ordinal)) fits = false;
            }

            if (fits) candidates.Add((reader.GetBoolean(0), reader.GetBoolean(1), exact));
        }

        return candidates.Count == 0 ? null : candidates.OrderByDescending(c => c.Exact).Select(c => ((bool, bool)?)(c.Strict, c.Aggregate)).First();
    }

    private static IEnumerable<string[]> Product(IReadOnlyList<string[]> slots)
    {
        IEnumerable<string[]> result = [[]];
        foreach (var slot in slots) result = result.SelectMany(prefix => slot.Select(value => (string[])[.. prefix, value])).ToList();
        return result;
    }

    /// <summary>One argument of a recorded function: its store type, and its literal when it is a constant.</summary>
    private sealed record Argument(string StoreType, string? Constant);

    /// <summary>A function as the translators declare it.</summary>
    private sealed record Declared(string Name, IReadOnlyList<Argument> Arguments, IReadOnlyList<int> Propagating, bool Nullable)
    {
        public string Signature => $"{Name}({string.Join(", ", Arguments.Select(a => a.Constant ?? a.StoreType))})";
    }

    /// <summary>Every <see cref="SqlFunctionExpression"/> in a SQL tree, by signature and declaration.</summary>
    private sealed class FunctionCollector(Dictionary<string, Declared> functions) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is SqlFunctionExpression { IsBuiltIn: true, Arguments: { } arguments } function
                && function.Name != "COALESCE"
                && arguments.All(a => a.TypeMapping is not null))
            {
                var declared = new Declared(
                    function.Name,
                    [.. arguments.Select(a => new Argument(a.TypeMapping!.StoreType,
                                                           a is SqlConstantExpression { Value: { } value } ? a.TypeMapping.GenerateSqlLiteral(value) : null))],
                    [.. (function.ArgumentsPropagateNullability ?? []).Select((p, i) => (p, i)).Where(x => x.p).Select(x => x.i)],
                    function.IsNullable);
                functions.TryAdd($"{declared.Signature} [{string.Join(",", declared.Propagating)}]", declared);
            }

            return base.VisitExtension(node);
        }
    }

    public sealed class DeclaredRow
    {
        public int Id { get; set; }

        public DateRange? Period { get; set; }

        public DateRange Required { get; set; } = DateRange.Empty;

        public RangeSet<DateRange, DateOnly>? Blocks { get; set; }

        public RangeSet<DateRange, DateOnly> RequiredBlocks { get; set; } = RangeSet<DateRange, DateOnly>.Empty;

        public StringSet? Tags { get; set; }

        public StringSet RequiredTags { get; set; } = StringSet.Empty;

        public DateOnly? Day { get; set; }

        public string? Label { get; set; }
    }

    /// <summary>
    /// With a recorder, a context whose internal service provider wraps the plugin's SQL processor in one
    /// that records each final SQL tree; the plugin's own decorator stays in the chain, so the guard and
    /// the empty-collection rewrite run as in any other context.
    /// </summary>
    private sealed class DeclaredContext(string connectionString, ConcurrentQueue<Expression>? recorder) : DbContext
    {
        private static readonly ConcurrentDictionary<ConcurrentQueue<Expression>, IServiceProvider> Providers = new();

        public DbSet<DeclaredRow> Rows => Set<DeclaredRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (recorder is not null) optionsBuilder.UseInternalServiceProvider(Providers.GetOrAdd(recorder, Recording));
            CompositionDatabase.Configure(optionsBuilder, connectionString);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<DeclaredRow>().Property(r => r.Id).ValueGeneratedNever();

        private static IServiceProvider Recording(ConcurrentQueue<Expression> recorder)
        {
            var services = new ServiceCollection().AddEntityFrameworkNpgsql().AddEntityFrameworkNpgsqlValueRanges();
            var plugin   = services.Last(d => d.ServiceType == typeof(IRelationalParameterBasedSqlProcessorFactory));
            services.Remove(plugin);
            services.Add(new ServiceDescriptor(
                typeof(IRelationalParameterBasedSqlProcessorFactory),
                provider => new RecordingFactory((IRelationalParameterBasedSqlProcessorFactory)plugin.ImplementationFactory!(provider),
                                                 provider.GetRequiredService<RelationalParameterBasedSqlProcessorDependencies>(), recorder),
                plugin.Lifetime));
            return services.BuildServiceProvider();
        }
    }

    private sealed class RecordingFactory(
        IRelationalParameterBasedSqlProcessorFactory inner, RelationalParameterBasedSqlProcessorDependencies dependencies, ConcurrentQueue<Expression> recorder)
        : IRelationalParameterBasedSqlProcessorFactory
    {
        public RelationalParameterBasedSqlProcessor Create(RelationalParameterBasedSqlProcessorParameters parameters)
            => new Recording(inner.Create(parameters), dependencies, parameters, recorder);

        private sealed class Recording(
            RelationalParameterBasedSqlProcessor inner, RelationalParameterBasedSqlProcessorDependencies dependencies,
            RelationalParameterBasedSqlProcessorParameters parameters, ConcurrentQueue<Expression> recorder)
            : RelationalParameterBasedSqlProcessor(dependencies, parameters)
        {
            public override Expression Process(Expression queryExpression, ParametersCacheDecorator parametersDecorator)
            {
                var processed = inner.Process(queryExpression, parametersDecorator);
                recorder.Enqueue(processed);
                return processed;
            }
        }
    }
}

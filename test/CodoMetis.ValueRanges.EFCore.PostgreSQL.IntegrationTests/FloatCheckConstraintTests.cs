using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>docs/efcore.md#keep-nan-and-infinity-out-of-a-float-column</c>, against a live server (#232). The
/// finite-only rule covers what the application creates, binds and loads; a row another tool writes is
/// answered in PostgreSQL's order by a query that runs in the database, and only loading it throws. The
/// documented CHECK constraints keep such rows out of each column kind.
/// </summary>
/// <remarks>
/// <para>
/// The constraint expressions are read out of the guide's code block, not copied here, so the SQL a
/// reader pastes is the SQL that ran. They are applied through <c>HasCheckConstraint</c> and created by
/// <c>EnsureCreated</c>, as the guide does, for the float8 and the float4 column of each kind.
/// </para>
/// <para>
/// The multirange expression is the one a bounds check cannot replace: a multirange's <c>lower</c> and
/// <c>upper</c> are its outer bounds, so <c>{(,-Infinity],[1,2)}</c> and <c>{[1,2),[Infinity,)}</c> pass
/// the range expression (measured on PostgreSQL 16 and 18). Both are in the corpus.
/// </para>
/// </remarks>
[TestClass]
public sealed class FloatCheckConstraintTests
{
    private const string Section = "### Keep NaN and infinity out of a float column";

    private static readonly string[] Finite = ["[1,2)", "(,)", "empty", "(,5]", "[-1.7976931348623157e308,1.7976931348623157e308]"];

    private static readonly (string Column, string Kind, string[] Refused, string[] Accepted)[] Corpus =
    [
        ("Score", "range",
            ["[5,NaN)", "[NaN,NaN]", "[1,Infinity)", "[-Infinity,1)", "(,Infinity]", "[-Infinity,)", "(,NaN)"],
            Finite),
        ("Bands", "multirange",
            ["{[5,NaN)}", "{[1,2),[NaN,NaN]}", "{[1,2),[3,Infinity)}", "{[-Infinity,-Infinity],[1,2)}",
             "{(,-Infinity],[1,2)}", "{(,-Infinity),[1,2)}", "{[1,2),[Infinity,)}", "{[1,2),(Infinity,)}", "{(,0),[1,Infinity),[NaN,)}"],
            ["{[1,2),[3,4)}", "{}", "{(,)}", "{(,1),[2,)}", "{(,1),[1e+308,)}"]),
        ("Weights", "array",
            ["{1,NaN}", "{Infinity}", "{-Infinity,1}", "{NULL,NaN}"],
            ["{1,2}", "{}", "{-1.7976931348623157e308,5e-324}", "{1,NULL}"])
    ];

    [TestMethod]
    public async Task DocumentedConstraints_RefuseNaNAndInfinity_InEveryColumnKind()
    {
        ContainerLifecycle.RequireServer();

        var expressions = DocumentedConstraints();
        Assert.HasCount(3, expressions, "The guide's code block no longer holds three HasCheckConstraint calls.");

        var connectionString = new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString)
        {
            Database = $"float_check_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using (var context = new ReadingContext(connectionString, expressions))
            await context.Database.EnsureCreatedAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var failures = new List<string>();
        var id       = 0;
        foreach (var (column, kind, refused, accepted) in Corpus)
        {
            foreach (var single in new[] { false, true })
            {
                var name = single ? column + "4" : column;
                var type = (kind, single) switch
                {
                    ("range", false)      => "float8range",
                    ("range", true)       => "float4range",
                    ("multirange", false) => "float8multirange",
                    ("multirange", true)  => "float4multirange",
                    ("array", false)      => "double precision[]",
                    _                     => "real[]"
                };

                foreach (var literal in refused)
                {
                    var state = await Insert(name, type, literal);
                    if (state != PostgresErrorCodes.CheckViolation)
                        failures.Add($"  {type} {literal}: {(state is null ? "stored" : $"failed with {state}")}, expected a check violation");
                }

                // float4 rounds the float8 extremes to infinity on input; its own extremes are tested by the range rows.
                foreach (var literal in accepted.Where(l => !single || !l.Contains("308", StringComparison.Ordinal)))
                {
                    var state = await Insert(name, type, literal);
                    if (state is not null) failures.Add($"  {type} {literal}: refused ({state}), expected it stored");
                }
            }
        }

        Assert.IsEmpty(failures, $"The documented constraints, on PostgreSQL:\n{string.Join("\n", failures)}");

        async Task<string?> Insert(string column, string type, string literal)
        {
            await using var insert = new NpgsqlCommand(
                $"INSERT INTO \"Readings\" (\"Id\", \"{column}\") VALUES (@id, @literal::{type})", connection);
            insert.Parameters.AddWithValue("id", ++id);
            insert.Parameters.AddWithValue("literal", literal);
            try
            {
                await insert.ExecuteNonQueryAsync();
                return null;
            }
            catch (PostgresException exception)
            {
                return exception.SqlState;
            }
        }
    }

    /// <summary>
    /// The boundary the guide states: without the constraints, a query that runs in the database answers
    /// over a stored NaN in PostgreSQL's order, and only loading the entity throws.
    /// </summary>
    [TestMethod]
    public async Task WithoutTheConstraints_AQueryAnswersInPostgresOrder_AndOnlyLoadingThrows()
    {
        ContainerLifecycle.RequireServer();

        var connectionString = new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString)
        {
            Database = $"float_unchecked_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using (var context = new FloatDomainContext(connectionString))
            await context.Database.EnsureCreatedAsync();

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO float_rows ("Id", "Range", "Ratio") VALUES (1, '[0,NaN)', 'NaN');
                INSERT INTO float_rows ("Id", "Set") VALUES (2, '{1,NaN}');
                """, connection);
            await insert.ExecuteNonQueryAsync();
        }

        await using var query = new FloatDomainContext(connectionString);
        CollectionAssert.AreEqual(new[] { 1 }, await query.Rows.Where(r => r.Range!.Contains(1e308)).Select(r => r.Id).ToArrayAsync());
        CollectionAssert.AreEqual(new[] { 1 }, await query.Rows.Where(r => Float64Range.CreateUnboundedEnd(0).Contains(r.Ratio!.Value)).Select(r => r.Id).ToArrayAsync());
        CollectionAssert.AreEqual(new[] { 2 }, await query.Rows.Where(r => r.Id == 2).Select(r => r.Set!.Count).ToArrayAsync());

        var error = await Assert.ThrowsAsync<Exception>(() => query.Rows.AsNoTracking().SingleAsync(r => r.Id == 1));
        Assert.IsTrue(Chain(error).Any(ex => ex is ArgumentException && ex.Message.Contains("finite numbers only")), error.ToString());
    }

    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException) yield return exception;
    }

    /// <summary>The <c>HasCheckConstraint</c> calls in the guide's code block, as column → SQL.</summary>
    private static Dictionary<string, string> DocumentedConstraints()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot().FullName, "docs", "efcore.md"));
        var start = guide.IndexOf(Section, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, $"docs/efcore.md has no '{Section}'.");
        var block = guide[start..guide.IndexOf("\n## ", start, StringComparison.Ordinal)];

        return Regex.Matches(block, @"HasCheckConstraint\(""CK_Reading_(\w+)_Finite"",\s*""""""(.+?)""""""\)")
                    .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CodoMetis.ValueRanges.slnx"))) return directory;
        throw new InvalidOperationException("No CodoMetis.ValueRanges.slnx above the test assembly.");
    }

    public sealed class Reading
    {
        public int Id { get; set; }

        public Float64Range? Score { get; set; }

        public RangeSet<Float64Range, double>? Bands { get; set; }

        public Float64Set? Weights { get; set; }

        public Float32Range? Score4 { get; set; }

        public RangeSet<Float32Range, float>? Bands4 { get; set; }

        public Float32Set? Weights4 { get; set; }
    }

    /// <summary>The guide's model: each documented expression on its float8 column, and on its float4 twin.</summary>
    private sealed class ReadingContext(string connectionString, IReadOnlyDictionary<string, string> expressions) : DbContext
    {
        public DbSet<Reading> Readings => Set<Reading>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(
                connectionString,
                npgsql => npgsql.UseValueRanges().ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Reading>().Property(r => r.Id).ValueGeneratedNever();
            modelBuilder.Entity<Reading>().ToTable("Readings", table =>
            {
                foreach (var (column, sql) in expressions)
                {
                    table.HasCheckConstraint($"CK_Reading_{column}_Finite", sql);
                    table.HasCheckConstraint($"CK_Reading_{column}4_Finite", sql.Replace($"\"{column}\"", $"\"{column}4\"", StringComparison.Ordinal));
                }
            });
            modelBuilder.HasPostgresRange("float8range", "double precision");
            modelBuilder.HasPostgresRange("float4range", "real");
        }
    }
}

using System.Globalization;
using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The float families against the server they mirror (#153): <see cref="Float64Range"/> over the custom
/// <c>float8range</c> and <see cref="Float64Set"/> over <c>double precision[]</c>. Three questions, each
/// asked of PostgreSQL rather than of a table copied from it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Text.</b> The family writes the text <c>float8out</c> prints. Random doubles go to the server as
/// binary parameters (exact) and come back as its text; the family's text must be the same string. .NET's
/// own shortest form differs in layout for most exponents, in digits for about one double in three hundred
/// (PostgreSQL's Ryu excludes the ends of the rounding interval), and does not read back at all for 2^-25
/// and 2^-958.
/// </para>
/// <para>
/// <b>Literals and the store.</b> The same literal goes to <c>range_in</c> / <c>array_in</c> and to the
/// family's parser, and the server's value is loaded through EF Core: all three agree, or the model
/// refuses a value PostgreSQL holds with <c>NaN</c> or an infinity in it — the one documented way it is
/// stricter. A refusal must be loud on every path; a value the model reads differently is a failure.
/// <c>[5,NaN)</c> is the row that matters: under <c>CompareTo</c> it is inverted, so a model without the
/// guard reads it as the empty range, while PostgreSQL holds a range containing <c>1e308</c>.
/// </para>
/// <para>
/// <b>Operands.</b> A <c>NaN</c> parameter is refused at query time, and an inlined constant is the exact
/// double — including the two .NET formats wrong.
/// </para>
/// </remarks>
[TestClass]
public sealed class FloatDomainParityTests
{
    private const int TextSamples = 20_000;

    private static readonly double[] TextEdges =
    [
        0, -0.0, 1, -1, 0.1, 0.3, 1e14, 1e15, 99999999999999.99, 0.0001, 0.00001, 1e21, 1e22, 1e23, 1e100,
        double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue, 2.2250738585072014e-308,
        2.225073858507201e-308, 9007199254740993, 9007199254740992, 18014398509482500, 56125240608817456,
        2.9802322387695312e-08, 4.1045368012983762e-289, Math.BitIncrement(1.0)
    ];

    [TestMethod]
    public async Task Text_IsWhatFloat8OutPrints()
    {
        ContainerLifecycle.RequireDatabase();

        var random = new Random(153);
        var values = new List<double>(TextEdges);
        while (values.Count < TextSamples)
        {
            var value = random.Next(4) switch
            {
                0 => BitConverter.Int64BitsToDouble(random.NextInt64() ^ (random.Next(2) == 0 ? long.MinValue : 0)),
                1 => Math.Round(random.NextDouble() * Math.Pow(10, random.Next(-8, 25)), random.Next(0, 8)),
                2 => Math.ScaleB(1.0, random.Next(-1074, 1024)) * (random.Next(2) == 0 ? 1 : -1),
                _ => Math.BitDecrement(Math.ScaleB(1.0, random.Next(-1022, 1024)))
            };
            if (double.IsFinite(value)) values.Add(value);
        }

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT v::text, float8range(v, v, '[]')::text FROM unnest(@values) AS v", connection);
        command.Parameters.Add(new NpgsqlParameter<double[]>("values", values.ToArray()));

        var failures = new List<string>();
        var index    = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var value    = values[index++];
            var expected = reader.GetString(0);
            var element  = Float64Set.FormatValue(value, null, CultureInfo.InvariantCulture);
            var range    = Float64Range.CreateFinite(value, value, true, true).ToString();

            if (element != expected || range != reader.GetString(1))
                failures.Add($"  {value.ToString("G17", CultureInfo.InvariantCulture)} (bits {BitConverter.DoubleToInt64Bits(value):X16}): PostgreSQL '{expected}' / '{reader.GetString(1)}', "
                           + $"family '{element}' / '{range}'");
        }

        Assert.AreEqual(values.Count, index, "PostgreSQL returned a different number of rows.");
        Assert.IsEmpty(failures,
            $"{failures.Count} of {values.Count} doubles are written differently from float8out:\n" + string.Join("\n", failures.Take(25)));
    }

    /// <summary>Range literals: grammar, the guard, the extremes, continuity and <c>-0</c>.</summary>
    private static readonly string[] RangeLiterals =
    [
        "[1.5,2.5)", "[ 1.5 , 2.5 )", "[\" 1.5 \",2)", "(1,1.0000000000000002)", "[0.0,-0.0)", "[-0,0]",
        "[1e-300,1.7976931348623157e308]", "[-1.7976931348623157e308,5e-324)", "[2.9802322387695312e-08,1)",
        "[1e+5,1E6)", "[+.5,5.)", "[1,)", "(,1]", "(,)", "empty", "[5,1)", "[1.5,2.5)x", "[\"\",5)", "[1,,2)",
        "[5,NaN)", "[NaN,5)", "[NaN,NaN]", "[1,Infinity)", "[-Infinity,1)", "(,inf)", "[-inf,)", "[0x10,20)",
        "[1e309,2)", "[1,1e309)", "[1e-400,1)", "[1,1_000)"
    ];

    /// <summary>Array literals for the set: the same grammar per element, and the guard per element.</summary>
    private static readonly string[] ArrayLiterals =
    [
        "{1.5,2.5}", "{ 1.5 , 2.5 }", "{\" 1.5 \"}", "{-0,0}", "{2.9802322387695312e-08,1e23}", "{}", "{NULL}",
        "{1.5,NaN}", "{Infinity}", "{-Infinity,1}", "{0x10}", "{1e309}", "{1e-400}", "{{1,2}}"
    ];

    private static readonly string[] MultirangeLiterals = ["{[1,2),[3,4)}", "{[5,NaN)}", "{[1,2),[3,Infinity)}", "{}"];

    [TestMethod]
    public async Task Literals_AndTheStore_AgreeWithPostgres_OrRefuseANonFiniteValue()
    {
        ContainerLifecycle.RequireServer();

        var connectionString = new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString)
        {
            Database = $"float_domain_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using (var context = new FloatDomainContext(connectionString))
            await context.Database.EnsureCreatedAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var failures = new List<string>();
        var refused  = 0;
        var id       = 0;

        foreach (var literal in RangeLiterals)
            refused += await Compare(literal, "float8range", "Range",
                                     text => Float64Range.TryParse(text, CultureInfo.InvariantCulture, out var r) ? r.ToString() : null,
                                     row => row.Range?.ToString());

        foreach (var literal in ArrayLiterals)
            refused += await Compare(literal, "double precision[]", "Set",
                                     text => Float64Set.TryParse(text, CultureInfo.InvariantCulture, out var s) ? s.ToString() : null,
                                     row => row.Set?.ToString());

        foreach (var literal in MultirangeLiterals)
            refused += await Compare(literal, "float8multirange", "Bands",
                                     text => RangeSet<Float64Range, double>.TryParse(text, CultureInfo.InvariantCulture, out var b) ? b.ToString() : null,
                                     row => row.Bands?.ToString());

        Assert.IsEmpty(failures, "The float families disagree with PostgreSQL:\n" + string.Join("\n", failures));
        Assert.IsGreaterThanOrEqualTo(10, refused, "Fewer non-finite values were refused than the corpus holds — is the guard reached?");

        // Returns 1 when the row is the documented refusal, 0 when the three answers agree.
        async Task<int> Compare(string literal, string storeType, string column, Func<string, string?> parse, Func<FloatRow, string?> read)
        {
            var client = parse(literal);

            string? server;
            bool    nonFinite;
            try
            {
                await using var command = new NpgsqlCommand(
                    $"""
                     SELECT v::text,
                            v::text ~* '(nan|infinity)'
                     FROM (SELECT @literal::{storeType} AS v) AS literal
                     """, connection);
                command.Parameters.AddWithValue("literal", literal);
                await using var reader = await command.ExecuteReaderAsync();
                await reader.ReadAsync();
                (server, nonFinite) = (reader.GetString(0), reader.GetBoolean(1));
            }
            catch (PostgresException)
            {
                (server, nonFinite) = (null, false);
            }

            // What PostgreSQL's value is when it reaches the model: its text through the parser, and
            // the stored value through EF Core's read leg (Npgsql's binary range or array, then the
            // family's factories).
            string? loaded = null;
            Exception? loadError = null;
            if (server is not null)
            {
                var rowId = ++id;
                await using (var insert = new NpgsqlCommand(
                                 $"INSERT INTO float_rows (\"Id\", \"{column}\") VALUES (@id, @literal::{storeType})", connection))
                {
                    insert.Parameters.AddWithValue("id", rowId);
                    insert.Parameters.AddWithValue("literal", literal);
                    await insert.ExecuteNonQueryAsync();
                }

                try
                {
                    await using var context = new FloatDomainContext(connectionString);
                    loaded = read(await context.Rows.AsNoTracking().SingleAsync(row => row.Id == rowId));
                }
                catch (Exception ex)
                {
                    loadError = ex;
                }
            }

            if (server is null)
            {
                if (client is not null) failures.Add($"  {storeType} {literal}: PostgreSQL refuses it, the family reads {client}");
                return 0;
            }

            if (nonFinite)
            {
                // The documented stricter row: the model holds finite numbers only, so it must refuse
                // the literal, the server's own text, and the stored value — each loudly.
                if (client is not null) failures.Add($"  {storeType} {literal}: PostgreSQL holds {server}, the family reads the literal as {client}");
                if (parse(server) is { } fromText) failures.Add($"  {storeType} {literal}: the family reads PostgreSQL's text {server} as {fromText}");
                if (loadError is null) failures.Add($"  {storeType} {literal}: PostgreSQL holds {server}, EF Core loads it as {loaded ?? "null"} instead of refusing it");
                else if (!Chain(loadError).Any(ex => ex is ArgumentException && ex.Message.Contains("finite numbers only")))
                    failures.Add($"  {storeType} {literal}: loading {server} failed, but not with the finite guard: {loadError.GetType().Name}: {loadError.Message}");
                return 1;
            }

            // Literals the family refuses although the server reads a finite value, each for a reason
            // documented elsewhere: C strtod's hexadecimal form, which PostgreSQL accepts because the
            // platform's strtod does, and — for every value set — a NULL element and a second dimension.
            if (client is null && (literal.Contains("0x", StringComparison.Ordinal)
                                || literal.Contains("NULL", StringComparison.Ordinal)
                                || literal.StartsWith("{{", StringComparison.Ordinal)))
                return 0;

            var expected = parse(server);
            if (client is null)
                failures.Add($"  {storeType} {literal}: PostgreSQL reads {server}, the family refuses it");
            if (client is not null && client != expected)
                failures.Add($"  {storeType} {literal}: PostgreSQL reads {server} ({expected}), the family reads {client}");
            if (loadError is not null)
                failures.Add($"  {storeType} {literal}: EF Core cannot load {server}: {loadError.GetType().Name}: {loadError.Message}");
            else if (loaded != expected)
                failures.Add($"  {storeType} {literal}: PostgreSQL holds {server} ({expected}), EF Core loads {loaded ?? "null"}");
            return 0;
        }
    }

    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException) yield return exception;
    }

    [TestMethod]
    public async Task NonFiniteOperand_IsRefusedAtQueryTime()
    {
        ContainerLifecycle.RequireDatabase();

        await using var context = new IntegrationDbContext();
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var error = await Assert.ThrowsAsync<Exception>(() => context.Reservations.Where(r => r.Score.Contains(value)).ToListAsync());
            Assert.IsTrue(Chain(error).Any(ex => ex is ArgumentException && ex.Message.Contains("finite numbers only")),
                          $"Score.Contains({value}) failed, but not with the finite guard: {error}");

            error = await Assert.ThrowsAsync<Exception>(() => context.Reservations.Where(r => r.Weights.Contains(value)).ToListAsync());
            Assert.IsTrue(Chain(error).Any(ex => ex is ArgumentException && ex.Message.Contains("finite numbers only")),
                          $"Weights.Contains({value}) failed, but not with the finite guard: {error}");
        }
    }

    /// <summary>
    /// A constant operand is inlined as its literal, typed (<c>'…'::double precision</c>) because the range
    /// operators are polymorphic. It must be the exact double: .NET's shortest text for 2^-25 is the
    /// double below it, which the stored bound would not equal.
    /// </summary>
    [TestMethod]
    public async Task InlinedConstant_IsTheExactDouble()
    {
        ContainerLifecycle.RequireDatabase();

        const double twoToMinus25 = 2.9802322387695312e-08;
        await using (var seed = new IntegrationDbContext())
        {
            seed.Reservations.Add(new Reservation
            {
                Id      = 15301,
                Score   = Float64Range.CreateFinite(twoToMinus25, twoToMinus25, true, true),
                Weights = Float64Set.From(twoToMinus25),
                Ratio   = twoToMinus25
            });
            await seed.SaveChangesAsync();
        }

        await using var context = new IntegrationDbContext();
        Assert.AreEqual(1, await context.Reservations.CountAsync(r => r.Id == 15301 && r.Score.Contains(2.9802322387695312e-08)));
        Assert.AreEqual(1, await context.Reservations.CountAsync(r => r.Id == 15301 && r.Weights.Contains(2.9802322387695312e-08)));
        Assert.AreEqual(1, await context.Reservations.CountAsync(r => r.Id == 15301 && r.Score.Contains(r.Ratio)));

        var loaded = await context.Reservations.AsNoTracking().SingleAsync(r => r.Id == 15301);
        Assert.AreEqual(twoToMinus25, ((IFiniteRange<double>)loaded.Score).Start);
        Assert.AreEqual(twoToMinus25, loaded.Weights[0]);
    }

    /// <summary>
    /// Why the documented <c>HasPostgresRange</c> call names no <c>subtype_diff</c>: with
    /// <c>float8mi</c>, a GiST index over extreme values fails to build (<c>22003</c>), and without it the
    /// index builds and answers.
    /// </summary>
    [TestMethod]
    public async Task GistIndex_OverExtremeValues_BuildsWithoutSubtypeDiff()
    {
        ContainerLifecycle.RequireDatabase();

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        const string extremes =
            """
            SELECT r(CASE WHEN g % 2 = 0 THEN -1.7976931348623157e308 ELSE 1e308 END,
                     CASE WHEN g % 2 = 0 THEN -1e308 ELSE 1.7976931348623157e308 END)
            FROM generate_series(1, 5000) AS g
            """;

        await using (var plain = new NpgsqlCommand(
                         $"""
                          CREATE TEMP TABLE gist_plain (v float8range);
                          INSERT INTO gist_plain {extremes.Replace("r(", "float8range(", StringComparison.Ordinal)};
                          INSERT INTO gist_plain VALUES ('[-1.7976931348623157e308,1.7976931348623157e308]');
                          CREATE INDEX ON gist_plain USING gist (v);
                          SET enable_seqscan = off;
                          """, connection))
            await plain.ExecuteNonQueryAsync();

        await using (var query = new NpgsqlCommand("SELECT count(*) FROM gist_plain WHERE v @> 1.2e308::float8", connection))
            Assert.AreEqual(2501L, await query.ExecuteScalarAsync());

        await using var withDiff = new NpgsqlCommand(
            $"""
             CREATE TYPE pg_temp.float8range_with_diff AS RANGE (subtype = float8, subtype_diff = float8mi);
             CREATE TEMP TABLE gist_diff (v pg_temp.float8range_with_diff);
             INSERT INTO gist_diff {extremes.Replace("r(", "pg_temp.float8range_with_diff(", StringComparison.Ordinal)};
             CREATE INDEX ON gist_diff USING gist (v);
             """, connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => withDiff.ExecuteNonQueryAsync());
        Assert.AreEqual(PostgresErrorCodes.NumericValueOutOfRange, error.SqlState);
    }
}

/// <summary>One nullable column per float shape, so a row can hold whatever literal the test inserts.</summary>
public sealed class FloatRow
{
    public int Id { get; set; }

    public Float64Range? Range { get; set; }

    public Float64Set? Set { get; set; }

    public RangeSet<Float64Range, double>? Bands { get; set; }
}

/// <summary>A database of its own for <see cref="FloatDomainParityTests"/>, created by <c>EnsureCreated</c>.</summary>
public sealed class FloatDomainContext(string connectionString) : DbContext
{
    public DbSet<FloatRow> Rows => Set<FloatRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.UseValueRanges().ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FloatRow>().ToTable("float_rows").Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.HasPostgresRange("float8range", "double precision");
    }
}

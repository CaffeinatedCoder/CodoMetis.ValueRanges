using System.Collections.Concurrent;
using System.Globalization;
using CodoMetis.ValueRanges.Oracles;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Hands the same literal text to PostgreSQL's <c>array_in</c> / <c>range_in</c> and to this library's
/// parsers, and requires the same answer: both refuse it, or both read the same value.
/// </summary>
/// <remarks>
/// <para>
/// The literal grammars are where memory and the database can disagree without either looking
/// wrong. Until 9.0.0 <c>StringSet.Parse("{a\,b}")</c> was two elements where PostgreSQL reads one,
/// <c>{{a,b}}</c> was a one-dimensional set of <c>"{a"</c> and <c>"b}"</c>, and
/// <c>Int32Range.Parse("[\"\",5)")</c> was <c>(,4]</c> where PostgreSQL refuses an empty integer.
/// The unit tests pin each answer; this asks the server they were taken from.
/// </para>
/// <para>
/// A literal PostgreSQL reads into something a value set cannot hold — a multi-dimensional array, a
/// NULL element — counts as one the set must refuse.
/// </para>
/// </remarks>
[TestClass]
public sealed class LiteralGrammarParityTests
{
    private static readonly string[] ArrayLiterals =
    [
        "{a,b}", "{}", "{  }", "{ a b }", "{\"a\" , \"b\"}", "{\"\"}", "{\"NULL\"}",
        @"{a\,b}", @"{a\\b}", @"{""a\""b""}", @"{\""a}", @"{a\ }", @"{\ a}", @"{a b\ }", @"{N\ULL}", @"{a\}b}", @"{\{a}",
        "{ a}", "{a }", "{\ta\n}",
        "{NULL}", "{null}", "{a,NULL}",
        "{{a,b}}", "{{a},{b}}", "{a}b}", "{a{b}", "{a\"b}", "{a\"}", "{a\"b\"c}", "{\"a\"b}", "{\"x\" y}",
        @"{a\}", "{a,}", "{,a}", "{a,,b}", "{\"a\",}", "{\"a", "a,b", "{a,b"
    ];

    private static readonly string[] Int4RangeLiterals =
    [
        "[1,5)", "(,5)", "[1,)", "(,)", "empty", " [1,5) ", "[ 1 , 5 )", "[\"1\",\"5\")", @"[\1,5)", "[\"1\"2,20)",
        "[1\"\",5)", "[\" 1 \",5)", "[\"1\",\"\"\"\")",
        "[\"\",5)", "[1,\"\")", "(\"\",\"\")", "[ ,5)", "[1, )", "( , )",
        "[1,5]x", "[1,5,6)", "[1),5)", "[1(,5)", "[\"1,5)", @"[1,5\)", "[ 1,5)", " [1,5)",
        "[5,1)", "(5,1]", "[2,1)", "[5,5)", "(5,5]", "(1,2)", "[5,5]"
    ];

    private static readonly string[] DateRangeLiterals =
    [
        "[2024-01-01,2024-02-01)", "[\"2024-01-01\",\"2024-02-01\")", "[\"\",2024-02-01)", "[ ,2024-02-01)",
        @"[2024\-01\-01,2024-02-01)", "(,infinity)", "[-infinity,2024-01-01)",
        "[ 2024-01-01 , 2024-02-01 )", " [2024-01-01,2024-02-01) ", "[\" 2024-01-01\",2024-02-01)",
        "[2024-06-30,2024-01-01]", "[infinity,2024-01-01)", "(2024-01-01,-infinity]", "[2024-01-01,2024-01-01)"
    ];

    /// <summary>
    /// Each range type with a lower and upper bound text its element parser and PostgreSQL's both
    /// read. The server's output goes back through the same parser, so the comparison is by value.
    /// </summary>
    private static readonly (string Type, string Low, string High, Func<string, string> Client)[] BoundWhitespaceTypes =
    [
        ("int4range", "1", "5", l => Int32Range.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("int8range", "1", "5", l => Int64Range.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("numrange", "1.5", "5", l => DecimalRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("float8range", "1.5", "1e+300", l => Float64Range.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("float4range", "0.1", "1e+06", l => Float32Range.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("daterange", "2026-01-01", "2026-02-01", l => DateRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("daterange", "-infinity", "2026-02-01", l => DateRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("tsrange", "2026-01-01 10:00:00", "2026-02-01 00:00:00", l => DateTimeRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("tstzrange", "2026-01-01 10:00:00+00", "2026-02-01 00:00:00+00", l => DateTimeOffsetRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("timerange", "09:00:00", "17:00:00", l => TimeRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("daterange", "2026-01-01", "2026-02-01", l => LocalDateRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("tsrange", "2026-01-01 10:00:00", "2026-02-01 00:00:00", l => LocalDateTimeRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused"),
        ("tstzrange", "2026-01-01 10:00:00+00", "2026-02-01 00:00:00+00", l => InstantRange.TryParse(l, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused")
    ];

    /// <summary>ASCII whitespace PostgreSQL strips, and two Unicode spaces it does not.</summary>
    private static readonly string[] BoundWhiteSpace = [" ", "\t", "\n", "\r", "\v", "\f", "\u00A0", "\u2003"];

    private static readonly string[] Int4MultirangeLiterals =
    [
        "{[1,5),[7,9)}", "{}", "{ [1,5) , [7,9) }", "{[\"\",5)}", "{[1,5),[\"\",9)}", "{[\"1\",\"5\")}", "{ [1,5)}",
        "{[5,1),[7,9]}", "{[7,9],(5,1]}"
    ];

    [TestMethod]
    public async Task ArrayLiterals_ReadAsPostgresReadsThem()
    {
        ContainerLifecycle.RequireDatabase();
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        var failures = new List<string>();

        foreach (var literal in ArrayLiterals)
        {
            string server;
            try
            {
                var ndims = await ScalarAsync<int?>(connection, "SELECT array_ndims($1::text[])", literal);
                var elements = new List<string?>();
                await using (var command = Command(connection, "SELECT e FROM unnest($1::text[]) AS e", literal))
                await using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        elements.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
                }

                server = ndims > 1 || elements.Contains(null)
                    ? "refused"
                    : StringSet.From(elements!).ToString();
            }
            catch (PostgresException)
            {
                server = "refused";
            }

            var client = StringSet.TryParse(literal, CultureInfo.InvariantCulture, out var parsed) ? parsed.ToString() : "refused";

            if (client != server)
                failures.Add($"  {Show(literal)}: StringSet {client}, PostgreSQL {server}");
        }

        Assert.IsEmpty(failures, "text[] literals read differently:\n" + string.Join("\n", failures));
    }

    [TestMethod]
    public async Task RangeLiterals_ReadAsPostgresReadsThem()
    {
        ContainerLifecycle.RequireDatabase();
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        var failures = new List<string>();

        await Compare("int4range", Int4RangeLiterals,
            literal => Int32Range.TryParse(literal, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused");
        await Compare("daterange", DateRangeLiterals,
            literal => DateRange.TryParse(literal, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused");
        await Compare("int4multirange", Int4MultirangeLiterals,
            literal => RangeSet<Int32Range, int>.TryParse(literal, CultureInfo.InvariantCulture, out var r) ? r.ToString() : "refused");

        Assert.IsEmpty(failures, "Range literals read differently:\n" + string.Join("\n", failures));

        Task Compare(string type, string[] literals, Func<string, string> client)
            => CompareRangeLiterals(connection, type, literals, client, failures);
    }

    /// <summary>
    /// Whitespace at either end of a quoted or escaped bound, and a bound of only whitespace, for
    /// every range type: PostgreSQL's <c>range_in</c> keeps it in the bound and the element input
    /// function decides (#45). Until 9.1.0 the library refused <c>[" 2026-01-01",2026-02-01)</c>,
    /// which PostgreSQL reads as <c>[2026-01-01,2026-02-01)</c>.
    /// </summary>
    [TestMethod]
    public async Task BoundWhitespace_ReadsAsPostgresReadsIt()
    {
        ContainerLifecycle.RequireDatabase();
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        var failures = new List<string>();

        foreach (var (type, lo, hi, client) in BoundWhitespaceTypes)
        {
            var literals = BoundWhiteSpace.SelectMany(ws => new[]
            {
                $"[\"{ws}{lo}\",{hi})", $"[\"{lo}{ws}\",{hi})", $"[\"{ws}{lo}{ws}\",\"{ws}{hi}{ws}\")",
                $"[\\{ws}{lo},{hi})", $"[{lo}\\{ws},{hi})", $"[ \"{ws}{lo}\" ,{hi})",
                $"[\"{ws}\",{hi})", $"[{lo},\"{ws}{ws}\")", $"[\\{ws},{hi})"
            }).ToArray();

            await CompareRangeLiterals(connection, type, literals, client, failures);
        }

        Assert.IsEmpty(failures, "Bound whitespace read differently:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// Each array type with a set family that reads it and an element text both read. The server's
    /// output goes back through the same parser, so the comparison is by value.
    /// </summary>
    private static readonly (string Type, string Family, string Element, Func<string, string> Client)[] ElementWhitespaceTypes =
    [
        ("int2[]", "Int16Set", "1", l => Int16Set.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("int4[]", "Int32Set", "1", l => Int32Set.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("int8[]", "Int64Set", "1", l => Int64Set.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("numeric[]", "DecimalSet", "1.5", l => DecimalSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("float8[]", "Float64Set", "1.5", l => Float64Set.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("float4[]", "Float32Set", "0.1", l => Float32Set.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("text[]", "StringSet", "ab", l => StringSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("uuid[]", "GuidSet", "6f9619ff-8b86-d011-b42d-00c04fc964ff", l => GuidSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("date[]", "DateSet", "2026-01-01", l => DateSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("date[]", "DateSet", "infinity", l => DateSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("timestamp[]", "DateTimeSet", "2026-01-01 10:00:00", l => DateTimeSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("timestamptz[]", "DateTimeOffsetSet", "2026-01-01 10:00:00+00", l => DateTimeOffsetSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("time[]", "TimeSet", "09:00:00", l => TimeSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("date[]", "LocalDateSet", "2026-01-01", l => LocalDateSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("timestamp[]", "LocalDateTimeSet", "2026-01-01 10:00:00", l => LocalDateTimeSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("timestamptz[]", "InstantSet", "2026-01-01 10:00:00+00", l => InstantSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused"),
        ("time[]", "LocalTimeSet", "09:00:00", l => LocalTimeSet.TryParse(l, CultureInfo.InvariantCulture, out var s) ? s.ToString() : "refused")
    ];

    /// <summary>
    /// Whitespace at either end of a quoted or escaped array element, and an element of only
    /// whitespace, for every set family: PostgreSQL's <c>array_in</c> hands it to the element's input
    /// function, which strips it (numeric, temporal), keeps it (<c>text</c>) or rejects it
    /// (<c>uuid</c>) (#70). Until 9.1.0 the temporal sets rejected <c>{" 2026-01-01"}</c>, which
    /// PostgreSQL reads as <c>{2026-01-01}</c>. <c>GuidSet</c> accepting what <c>uuid_in</c> rejects
    /// is a known candidate (#78).
    /// </summary>
    [TestMethod]
    public async Task ElementWhitespace_ReadsAsPostgresReadsIt()
    {
        ContainerLifecycle.RequireDatabase();
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        var failures = new List<string>();

        foreach (var (type, family, element, client) in ElementWhitespaceTypes)
        {
            foreach (var ws in BoundWhiteSpace)
            {
                foreach (var literal in new[]
                         {
                             $"{{\"{ws}{element}\"}}", $"{{\"{element}{ws}\"}}", $"{{\"{ws}{element}{ws}\"}}",
                             $"{{\\{ws}{element}}}", $"{{{element}\\{ws}}}", $"{{ \"{ws}{element}\" }}",
                             $"{{\"{ws}\"}}", $"{{\"{ws}{ws}\"}}", $"{{\\{ws}}}"
                         })
                {
                    string server;
                    try
                    {
                        // Text is read element by element: reparsing the server's array output with
                        // the parser under test would hide a space it dropped on both sides.
                        server = type == "text[]"
                            ? StringSet.From(await ElementsAsync(connection, literal)).ToString()
                            : client((await ScalarAsync<string>(connection, $"SELECT $1::{type}::text", literal))!);
                    }
                    catch (PostgresException)
                    {
                        server = "refused";
                    }

                    var mine = client(literal);
                    if (mine != server)
                        failures.Add($"  {type} {family} {Show(literal)}: library {mine}, PostgreSQL {server}");
                }
            }
        }

        Candidates.Verdict(nameof(LiteralGrammarParityTests), failures, "Array element whitespace read differently",
                           method: nameof(ElementWhitespace_ReadsAsPostgresReadsIt));
    }

    private static async Task CompareRangeLiterals(
        NpgsqlConnection connection, string type, string[] literals, Func<string, string> client, List<string> failures)
    {
        foreach (var literal in literals)
        {
            string server;
            try
            {
                // Compared as values, not text: the library writes a discrete range closed
                // ([1,4] for PostgreSQL's [1,5)) and an infinite date as MaxValue, so the
                // server's canonical output goes through the same parser.
                server = client((await ScalarAsync<string>(connection, $"SELECT $1::{type}::text", literal))!);
            }
            catch (PostgresException)
            {
                server = "refused";
            }

            var mine = client(literal);
            if (mine != server)
                failures.Add($"  {type} {Show(literal)}: library {mine}, PostgreSQL {server}");
        }
    }

    private static async Task<List<string>> ElementsAsync(NpgsqlConnection connection, string literal)
    {
        var elements = new List<string>();
        await using var command = Command(connection, "SELECT e FROM unnest($1::text[]) AS e", literal);
        await using var reader  = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            elements.Add(reader.GetString(0));
        return elements;
    }

    /// <summary>
    /// Empty arrays nested to one depth are where PostgreSQL versions disagree: 17 and 18 read
    /// <c>{{}}</c> as <c>{}</c>, and 16 rejects it. The sets follow 17 and later (#128) and
    /// <c>docs/value-sets.md</c> names the split, so this asks each version, in containers of this test's
    /// own (the suite's shared one runs whichever major <c>VALUERANGES_POSTGRES_IMAGE</c> names), and fails
    /// when either side of the split moves, before the sentence goes stale. Each server's major is read
    /// from <c>server_version_num</c>, not from the image. A nested array holding an element is
    /// two-dimensional on every version, and rejected here.
    /// </summary>
    [TestMethod]
    public async Task NestedEmptyArrays_ReadAsPostgres17AndLaterReadThem_AndPostgres16Rejects()
    {
        ContainerLifecycle.RequireDatabase();

        string[] empties = ["{{}}", "{{},{}}", "{ {} }", "{{{}},{{}}}", "{{{},{}}}", "{{{},{}},{{},{}}}", "{{{{}}},{{{}}}}", "{{{{{{}}}}}}"];
        string[] nested  = ["{{1}}", "{{1},{2}}"];
        // Mixed depths, a nest holding an element, and ragged nests whose siblings differ in count (#231).
        string[] mixed   = ["{{},{{}}}", "{{},{1}}", "{{},}", "{{{},{}},{{}}}", "{{{}},{{},{}}}", "{{{{}},{{}}},{{{}}}}",
                            "{{{{{{{}}}}}}}"];

        var failures = new List<string>();
        var majors = new List<int>();
        foreach (var image in (string[])["postgres:16-alpine", "postgres:17-alpine", "postgres:18-alpine"])
        {
            await using var connection = new NpgsqlConnection(await VersionServer(image));
            await connection.OpenAsync();
            await using (var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::int / 10000", connection))
                majors.Add((int)(await command.ExecuteScalarAsync())!);
            var version = majors[^1];

            foreach (var literal in empties)
            {
                var valid    = await ScalarAsync<bool>(connection, "SELECT pg_input_is_valid($1, 'int4[]')", literal);
                var expected = version >= 17;
                if (valid != expected)
                    failures.Add($"  PostgreSQL {version} {(valid ? "reads" : "rejects")} {literal}, which the documented split says it {(expected ? "reads" : "rejects")}");
                else if (valid && await ScalarAsync<string>(connection, "SELECT $1::int4[]::text", literal) is var text && text != "{}")
                    failures.Add($"  PostgreSQL {version} reads {literal} as {text}, not {{}}");
            }

            foreach (var literal in nested)
                if (await ScalarAsync<int?>(connection, "SELECT array_ndims($1::int4[])", literal) != 2)
                    failures.Add($"  PostgreSQL {version} no longer reads {literal} as two-dimensional");

            foreach (var literal in mixed)
                if (await ScalarAsync<bool>(connection, "SELECT pg_input_is_valid($1, 'int4[]')", literal))
                    failures.Add($"  PostgreSQL {version} reads {literal}, which it rejected");
        }

        foreach (var literal in empties)
            if (!Int32Set.TryParse(literal, CultureInfo.InvariantCulture, out var set) || !set.IsEmpty)
                failures.Add($"  Int32Set does not read {literal} as {{}}");
        foreach (var literal in nested.Concat(mixed))
            if (Int32Set.TryParse(literal, CultureInfo.InvariantCulture, out _))
                failures.Add($"  Int32Set reads {literal}, which no version reads as a one-dimensional array");

        CollectionAssert.AreEqual(new[] { 16, 17, 18 }, majors, "the three servers are not the three majors the split is stated for");
        Assert.IsEmpty(failures, "Nested empty arrays:\n" + string.Join("\n", failures));
    }

    private static readonly ConcurrentDictionary<string, Lazy<Task<PostgreSqlContainer>>> VersionServers = new();

    private static async Task<string> VersionServer(string image)
        => (await VersionServers.GetOrAdd(image, key => new Lazy<Task<PostgreSqlContainer>>(async () =>
        {
            var container = new PostgreSqlBuilder(key).Build();
            await container.StartAsync();
            return container;
        })).Value).GetConnectionString();

    [ClassCleanup]
    public static async Task StopVersionServers()
    {
        foreach (var server in VersionServers.Values.Where(server => server.IsValueCreated))
        {
            try { await (await server.Value).DisposeAsync(); }
            catch { /* a server that never started has nothing to stop */ }
        }
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, string sql, string literal)
    {
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = literal, NpgsqlDbType = NpgsqlDbType.Text });
        return command;
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection connection, string sql, string literal)
    {
        await using var command = Command(connection, sql, literal);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private static string Show(string literal)
        => string.Concat(literal.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString()));
}

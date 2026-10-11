using System.Globalization;
using Npgsql;
using NpgsqlTypes;

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
        "[1,5]x", "[1,5,6)", "[1),5)", "[1(,5)", "[\"1,5)", @"[1,5\)", "[ 1,5)", " [1,5)"
    ];

    private static readonly string[] DateRangeLiterals =
    [
        "[2024-01-01,2024-02-01)", "[\"2024-01-01\",\"2024-02-01\")", "[\"\",2024-02-01)", "[ ,2024-02-01)",
        @"[2024\-01\-01,2024-02-01)", "(,infinity)", "[-infinity,2024-01-01)",
        // Whitespace around an unquoted bound is the range grammar's to skip. Inside quotes it reaches
        // the element, where PostgreSQL's date_in trims it and the strict ISO grammar refuses it —
        // deliberately, and loudly (TemporalLiteralGrammarTests.Date_Rejects), so it is not listed.
        "[ 2024-01-01 , 2024-02-01 )", " [2024-01-01,2024-02-01) "
    ];

    private static readonly string[] Int4MultirangeLiterals =
    [
        "{[1,5),[7,9)}", "{}", "{ [1,5) , [7,9) }", "{[\"\",5)}", "{[1,5),[\"\",9)}", "{[\"1\",\"5\")}", "{ [1,5)}"
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

        async Task Compare(string type, string[] literals, Func<string, string> client)
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

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using Npgsql;
using NpgsqlTypes;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Route B, step 3 (#105, #123, <c>test/oracles/axes.md</c>): literal corpus × entry point × type,
/// judged against PostgreSQL's own text input on the same text. For every literal of the corpus the
/// server is asked <c>pg_input_is_valid($1, type)</c> and <c>$1::type::text</c>, and every entry point
/// of the library must give the server's answer: both refuse, or both read the same value.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LiteralGrammarParityTests"/> asks the server about quoted, escaped and whitespace bounds
/// through <c>TryParse</c>. The rest of what PostgreSQL's <c>range_in</c>, <c>multirange_in</c>,
/// <c>array_in</c> and the element input functions distinguish was never asked, and #114 (an inverted
/// literal read as empty) and #115 (decimal text beyond 28 significant digits rounded) were each one
/// cell of this product, found once and not kept. So the corpus is generated per element kind: the
/// structural families of each grammar (bounds, equal and inverted bounds, <c>empty</c> in every case,
/// quoting, escapes, trailing junk, Unicode whitespace, dimensions, multirange canonicalization) over
/// four ascending element texts, and per element kind the texts its input function reads differently
/// (sign, leading zeros, exponents, <c>NaN</c>, <c>infinity</c> spellings, <c>24:00:00</c>, more than
/// 28 digits, PostgreSQL's special inputs <c>epoch</c>/<c>now</c>/<c>today</c>, non-ISO dates …),
/// each placed in a bound or an array element.
/// </para>
/// <para>
/// Per literal the library answers through <c>Parse(string)</c>, <c>Parse(span)</c>,
/// <c>TryParse(string)</c>, <c>TryParse(span)</c>, the JSON converter (a range or multirange as its
/// literal; a value set as a one-element JSON array where the element is expressible as JSON), the
/// <c>TypeConverter</c> and the EF Core JSON document reader. All of them must agree. Against the
/// server, a value the library reads is written back through EF Core twice — as the SQL constant and
/// as the JSON document text — and the server must read each as the value it read from the original
/// literal: compared on the server as typed values (<c>=</c> for ranges, containment both ways for
/// arrays), so a precision the parser dropped shows up even though re-parsing the server's output
/// through the same parser would hide it. A wrapper arity is compared through its own parser instead,
/// since its element type validates and may normalize (<c>TextKey</c> trims); the EF legs are its
/// closed sibling's.
/// </para>
/// <para>
/// Two kinds of refusal are not disagreements. <b>Domain</b>: the server reads a value the CLR type
/// cannot hold (<c>NaN</c>, <c>numeric</c> infinity, <c>24:00:00</c> as a <c>TimeOnly</c>, a year
/// beyond 9999, a NULL element, a two-dimensional array); the library refuses both the literal and the
/// server's own output (<c>docs/architecture.md#domain-width-and-the-boundary</c>). <b>Documented</b>:
/// a family <c>docs/serialization.md</c> says the library refuses although PostgreSQL reads it — the
/// strict ISO 8601 temporal grammar (claim J07) and the <c>numeric</c> grammar of sign, digits, point
/// and exponent (J06). Both are counted; acceptance of such a literal is still compared.
/// </para>
/// <para>
/// One <c>[DynamicData]</c> row per type, so a candidate is scoped by the type's label. A failure line
/// is <c>{type} {family} {literal}: …</c>, and a candidate's row is <c>{type} {family} </c>: one
/// family is one defect class. The server round trips are batched (three to four statements per type
/// over one connection), so the sweep adds seconds, not minutes, to the integration run.
/// </para>
/// </remarks>
[TestClass]
public sealed class LiteralCorpusProbeTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static IRelationalTypeMappingSource Mappings { get; } = new IntegrationDbContext().GetService<IRelationalTypeMappingSource>();

    /// <summary>One row per discovered type; the label is the prefix every failure line and candidate row starts with.</summary>
    public sealed record ProbeRow(string Label, Func<NpgsqlConnection, List<string>, Task<Tally>> Sweep)
    {
        public override string ToString() => Label;
    }

    /// <summary>What one row ran: literals, the cells (literal × entry point), and how the refusals were judged.</summary>
    public sealed record Tally(int Literals, int Cells, int BothRefuse, int Domain, int Documented, int Compared);

    public static IEnumerable<object[]> Rows
    {
        get
        {
            foreach (var (range, element) in FuzzTypes.Ranges)
            {
                if (KindOf(element) is not { } kind) continue;
                var multirange = typeof(RangeSet<,>).MakeGenericType(range, element);
                yield return [Row(range.Name, range, kind, Grammar.Range, closedSibling: null)];
                yield return [Row($"RangeSet<{range.Name}>", multirange, kind, Grammar.Multirange, closedSibling: null)];
            }

            foreach (var (set, _, primitive) in FuzzTypes.Sets)
            {
                if (KindOf(primitive) is not { } kind) continue;
                var label = set.IsGenericType ? $"{set.Name.Split('`')[0]}<{set.GetGenericArguments()[0].Name}>" : set.Name;
                Type? sibling = set.IsGenericType
                    ? set.Assembly.GetExportedTypes().Single(t => t.Name == set.Name.Split('`')[0] && !t.IsGenericType)
                    : null;
                yield return [Row(label, set, kind, Grammar.Array, sibling)];
            }
        }
    }

    private static ProbeRow Row(string label, Type type, Kind kind, Grammar grammar, Type? closedSibling)
    {
        var method = typeof(LiteralCorpusProbeTests).GetMethod(nameof(Sweep), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                                                     .MakeGenericMethod(type);
        return new ProbeRow(label, (connection, failures) =>
            (Task<Tally>)method.Invoke(null, [connection, failures, label, kind, grammar, closedSibling])!);
    }

    /// <summary>The floor that keeps the row list from passing by finding nothing.</summary>
    [TestMethod]
    public void Discovery_FindsEveryRow()
    {
        // 10 ranges and their 10 multiranges (YearMonthRange's literal is not a daterange literal),
        // 28 set types (14 families × closed, wrapper; YearMonthSet likewise).
        const int knownRows = 48;
        var rows = Rows.Count();
        Assert.IsGreaterThanOrEqualTo(knownRows, rows, $"Found {rows} rows, fewer than {knownRows}.");
    }

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task EveryEntryPoint_ReadsTheLiteralAsPostgresReadsIt(ProbeRow row)
    {
        ContainerLifecycle.RequireDatabase();
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        var failures = new List<string>();
        var tally    = await row.Sweep(connection, failures);

        Console.WriteLine($"{row}: {tally}");
        Assert.IsGreaterThanOrEqualTo(60, tally.Literals, $"{row}: only {tally.Literals} literals ran.");
        Assert.IsGreaterThanOrEqualTo(10, tally.BothRefuse, $"{row}: only {tally.BothRefuse} literals were refused by both sides.");
        Assert.IsGreaterThanOrEqualTo(10, tally.Compared, $"{row}: only {tally.Compared} literals were read by both sides.");

        Candidates.Verdict(nameof(LiteralCorpusProbeTests), failures,
            $"{row}: of {tally.Literals} literals, these are read differently from PostgreSQL", shown: 40, rowPrefix: row.Label + " ");
    }

    // =========================================================================
    // The sweep
    // =========================================================================

    private enum Grammar { Range, Multirange, Array }

    private enum Kind { Int2, Int4, Int8, Numeric, Date, Timestamp, Timestamptz, Time, Uuid, Text }

    /// <summary>One entry point's answer: a value and its literal, a refusal, or an exception no refusal should be.</summary>
    private sealed record Answer(bool Accepted, object? Value, string Text, bool Threw = false)
    {
        public static Answer Refused(Exception e) => new(false, null, $"refused ({e.GetType().Name})");

        public static Answer Unexpected(Exception e) => new(false, null, $"threw {e.GetType().Name}: {e.Message}", Threw: true);

        public bool SameAs(Answer other) =>
            Accepted == other.Accepted && (!Accepted || Equals(Value, other.Value) && Text == other.Text);
    }

    private static async Task<Tally> Sweep<TSelf>(
        NpgsqlConnection connection, List<string> failures, string label, Kind kind, Grammar grammar, Type? closedSibling)
        where TSelf : ISpanParsable<TSelf>
    {
        var corpus    = Corpus(kind, grammar).ToList();
        var mapping   = Mappings.FindMapping(typeof(TSelf)) ?? throw new InvalidOperationException($"No mapping for {typeof(TSelf)}.");
        var storeType = mapping.StoreType;
        var isWrapper = closedSibling is not null;

        var server = await ReadOnServer(connection, storeType, grammar, [.. corpus.Select(c => c.Literal)]);

        int cells = 0, bothRefuse = 0, domain = 0, documented = 0, compared = 0;
        var writeBack = new List<(int Index, string Leg, string Text)>();
        var library   = new Answer[corpus.Count];

        for (var i = 0; i < corpus.Count; i++)
        {
            var (family, literal, element) = corpus[i];
            var head = $"{label} {family} {Show(literal)}:";

            // --- every entry point, against Parse(string) -----------------------------------------
            var legs = EntryPoints<TSelf>(literal, element, grammar, mapping).ToList();
            cells += legs.Count;
            var parse = legs[0].Answer;
            library[i] = parse;
            foreach (var (leg, answer) in legs)
            {
                if (answer.Threw)
                    failures.Add($"{head} {leg} {answer.Text}");
                else if (!answer.SameAs(parse))
                    failures.Add($"{head} {leg} {answer.Text}, Parse(string) {parse.Text}");
            }

            // --- against the server ---------------------------------------------------------------
            var pg = server[i];
            if (!pg.Valid)
            {
                if (parse.Accepted) failures.Add($"{head} library {parse.Text}, PostgreSQL refuses");
                else bothRefuse++;
                continue;
            }

            // The server's canonical text can leave the CLR domain while the value does not — the
            // discrete (,9999-12-31] prints as (,10000-01-01) — so for a closed type an unreadable
            // canonical text is judged by the write-back below, and only a refusal of both is domain.
            var readsServerText = StringOverloads.TryParse<TSelf>(pg.Text, Invariant, out var fromServer);
            if (!pg.Representable || !readsServerText && (isWrapper || !parse.Accepted))
            {
                // The server reads a value the CLR type cannot hold: a refusal is the only right answer.
                if (parse.Accepted)
                    failures.Add($"{head} library {parse.Text}, PostgreSQL reads {Show(pg.Text!)}, which {label} cannot hold");
                else domain++;
                continue;
            }

            if (!parse.Accepted)
            {
                if (DocumentedRefusal(family, kind) is not null) documented++;
                else failures.Add($"{head} library {parse.Text}, PostgreSQL reads {Show(pg.Text!)}");
                continue;
            }

            compared++;
            if (readsServerText && !Equals(parse.Value, fromServer))
                failures.Add($"{head} library {parse.Text}, PostgreSQL reads {Show(pg.Text!)} = {fromServer}");

            if (isWrapper) continue;
            writeBack.Add((i, "SQL constant", mapping.GenerateSqlLiteral(parse.Value)));
            writeBack.Add((i, "JSON document", JsonSerializer.Deserialize<string>(mapping.JsonValueReaderWriter!.ToJsonString(parse.Value!))!));
        }

        // --- the value written back through EF Core, read by the server as the original literal ----
        if (writeBack.Count > 0)
        {
            var constants = writeBack.Where(w => w.Leg == "SQL constant").ToList();
            var evaluated = await EvaluateOnServer(connection, [.. constants.Select(c => c.Text)]);
            var texts     = writeBack.Select(w => w.Leg == "SQL constant" ? evaluated[constants.IndexOf(w)] : w.Text).ToArray();
            var verdicts  = await CompareOnServer(connection, storeType, grammar,
                                                  [.. writeBack.Select(w => corpus[w.Index].Literal)], texts);

            for (var k = 0; k < writeBack.Count; k++)
            {
                var (index, leg, text) = writeBack[k];
                var (family, literal, _) = corpus[index];
                var head = $"{label} {family} {Show(literal)}:";
                switch (verdicts[k])
                {
                    // docs/architecture.md#domain-width-and-the-boundary (#117): the date extremes stand
                    // for the infinities, so [x,infinity) is [x,9999-12-30] here, as a column reads it, and
                    // is stored as [x,9999-12-31), where the server's range also holds its real last date.
                    // DomainBoundaryDivergenceTests holds the column and the text to the same value. A
                    // LocalDate one day above the minimum is a year PostgreSQL can't hold, which is loud.
                    case var _ when family == "infinity bound" && kind == Kind.Date:
                        documented++;
                        break;
                    case (null, _):
                        failures.Add($"{head} {leg} {Show(text)} of library {library[index].Text} is refused by PostgreSQL ({Show(texts[k] ?? "error")})");
                        break;
                    // docs/efcore.md#json-columns: the store spells MinValue / MaxValue -infinity / infinity,
                    // as Npgsql sends them, so a literal naming an extreme is stored as an infinity.
                    case (false, var read) when IsTemporal(kind) && read!.Contains("infinity") && !server[index].Text!.Contains("infinity"):
                        documented++;
                        break;
                    case (false, var read):
                        failures.Add($"{head} {leg} of library {library[index].Text} reads {Show(read!)}, PostgreSQL reads {Show(server[index].Text!)}");
                        break;
                }
            }
        }

        return new Tally(corpus.Count, cells, bothRefuse, domain, documented, compared);
    }

    /// <summary>Each entry point's answer; <c>Parse(string)</c> first, which the others are compared with.</summary>
    private static IEnumerable<(string Leg, Answer Answer)> EntryPoints<TSelf>(string literal, string? element, Grammar grammar, RelationalTypeMapping mapping)
        where TSelf : ISpanParsable<TSelf>
    {
        yield return ("Parse(string)", Try(() => StringOverloads.Parse<TSelf>(literal, Invariant)));
        yield return ("Parse(span)", Try(() => TSelf.Parse(literal.AsSpan(), Invariant)));
        yield return ("TryParse(string)", TryNeverThrows(() => (StringOverloads.TryParse<TSelf>(literal, Invariant, out var r), r)));
        yield return ("TryParse(span)", TryNeverThrows(() => (TSelf.TryParse(literal.AsSpan(), Invariant, out var r), r)));

        if (grammar != Grammar.Array)
            yield return ("JSON", Try(() => JsonSerializer.Deserialize<TSelf>(JsonSerializer.Serialize(literal))!));
        else if (element is not null && JsonElementArray(element, mapping) is { } array)
            yield return ($"JSON {array}", Try(() => JsonSerializer.Deserialize<TSelf>(array)!));

        // An empty string converts to null (claim J14) — never to a value, so it is the refusal here.
        yield return ("TypeConverter", Try(() => (TSelf?)TypeDescriptor.GetConverter(typeof(TSelf)).ConvertFromInvariantString(literal)
                                                 ?? throw new FormatException("null")));

        yield return ("EF JSON document", Try(() => (TSelf)mapping.JsonValueReaderWriter!.FromJsonString(JsonSerializer.Serialize(literal))));

        // A refusal is FormatException (the literal), OverflowException (an element too large),
        // JsonException (the converter), or what TypeDescriptor wraps them in; anything else is reported.
        static Answer Try(Func<TSelf> read)
        {
            try
            {
                return Accepted(read());
            }
            catch (Exception e) when (e is FormatException or OverflowException or JsonException or NotSupportedException or ArgumentException)
            {
                return Answer.Refused(e);
            }
            catch (Exception e)
            {
                return Answer.Unexpected(e);
            }
        }

        static Answer TryNeverThrows(Func<(bool, TSelf?)> read)
        {
            try
            {
                var (parsed, value) = read();
                return parsed ? Accepted(value!) : new Answer(false, null, "refused (false)");
            }
            catch (Exception e)
            {
                return Answer.Unexpected(e);
            }
        }

        static Answer Accepted(TSelf value) => new(true, value, ((IFormattable)value!).ToString(null, Invariant));
    }

    // A value set's JSON is an array, not the literal, so its JSON leg is the one-element array of an
    // element the literal names bare: a number for the numeric families, a string otherwise. Text is
    // left out — a JSON string is not parsed — and so is anything JSON cannot spell as written.
    private static readonly Regex JsonNumber = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant);

    private static string? JsonElementArray(string element, RelationalTypeMapping mapping)
    {
        // A Guid element is read by System.Text.Json's own converter, whose grammar is not the
        // literal's (docs/serialization.md: only the temporal sets read JSON with the literal grammar).
        var store = mapping.StoreType;
        if (store.StartsWith("text", StringComparison.Ordinal) || store.StartsWith("uuid", StringComparison.Ordinal)) return null;
        if (element.Length == 0 || element.Trim() != element || element.IndexOfAny(['"', '\\', '{', '}', ',']) >= 0) return null;

        var numeric = store is "smallint[]" or "integer[]" or "bigint[]" or "numeric[]";
        if (numeric) return JsonNumber.IsMatch(element) ? $"[{element}]" : null;
        return $"[{JsonSerializer.Serialize(element)}]";
    }

    // =========================================================================
    // The server
    // =========================================================================

    private sealed record ServerAnswer(bool Valid, string? Text, bool Representable);

    /// <summary>
    /// <c>pg_input_is_valid</c> and the canonical text for every literal in one statement; a literal the
    /// soft-error path cannot report (a NUL byte the protocol refuses) sends the batch to one statement each.
    /// </summary>
    private static async Task<ServerAnswer[]> ReadOnServer(NpgsqlConnection connection, string storeType, Grammar grammar, string[] literals)
    {
        // An array is printed without its dimension decoration ([0:1]={1,2} is the set {1,2}): a set
        // has no index, and the decoration is not something its literal has to read back.
        var (text, representable) = grammar == Grammar.Array
            ? ($"ARRAY(SELECT unnest(l::{storeType}))::text",
               $"CASE WHEN array_ndims(l::{storeType}) > 1 THEN false ELSE array_position(l::{storeType}, NULL) IS NULL END")
            : ($"(l::{storeType})::text", "true");

        var sql = $"""
                   SELECT ok, CASE WHEN ok THEN {text} END, CASE WHEN ok THEN {representable} END
                   FROM (SELECT l, i, pg_input_is_valid(l, $2) AS ok
                         FROM unnest($1::text[]) WITH ORDINALITY AS u(l, i)) AS s
                   ORDER BY i
                   """;
        try
        {
            return await Read(literals);
        }
        catch (Exception e) when (e is PostgresException or NpgsqlException or ArgumentException)
        {
            var answers = new ServerAnswer[literals.Length];
            for (var i = 0; i < literals.Length; i++)
            {
                try
                {
                    answers[i] = (await Read([literals[i]]))[0];
                }
                catch (Exception inner) when (inner is PostgresException or NpgsqlException or ArgumentException)
                {
                    answers[i] = new ServerAnswer(false, null, false);
                }
            }

            return answers;
        }

        async Task<ServerAnswer[]> Read(string[] batch)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(new NpgsqlParameter { Value = batch, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
            command.Parameters.Add(new NpgsqlParameter { Value = storeType, NpgsqlDbType = NpgsqlDbType.Text });
            var answers = new List<ServerAnswer>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                answers.Add(new ServerAnswer(reader.GetBoolean(0),
                                             reader.IsDBNull(1) ? null : reader.GetString(1),
                                             !reader.IsDBNull(2) && reader.GetBoolean(2)));
            }

            return [.. answers];
        }
    }

    /// <summary>The server's text for each SQL constant EF Core generated, or null where the server refuses it.</summary>
    private static async Task<string?[]> EvaluateOnServer(NpgsqlConnection connection, string[] constants)
    {
        if (constants.Length == 0) return [];
        try
        {
            await using var command = new NpgsqlCommand($"SELECT ARRAY[{string.Join(", ", constants.Select(c => $"({c})::text"))}]", connection);
            return (string?[])(await command.ExecuteScalarAsync())!;
        }
        catch (PostgresException)
        {
            var texts = new string?[constants.Length];
            for (var i = 0; i < constants.Length; i++)
            {
                try
                {
                    await using var command = new NpgsqlCommand($"SELECT ({constants[i]})::text", connection);
                    texts[i] = (string?)await command.ExecuteScalarAsync();
                }
                catch (PostgresException)
                {
                    texts[i] = null;
                }
            }

            return texts;
        }
    }

    /// <summary>
    /// For each pair, whether the server reads the library's text as the value it reads from the
    /// literal — typed equality for a range or multirange, containment both ways for an array — and
    /// its canonical text; <c>(null, …)</c> where it refuses the library's text.
    /// </summary>
    private static async Task<(bool? Same, string? Read)[]> CompareOnServer(
        NpgsqlConnection connection, string storeType, Grammar grammar, string[] literals, string?[] texts)
    {
        var same = grammar == Grammar.Array
            ? $"a::{storeType} @> b::{storeType} AND a::{storeType} <@ b::{storeType}"
            : $"a::{storeType} = b::{storeType}";
        var sql = $"""
                   SELECT CASE WHEN ok THEN {same} END, CASE WHEN ok THEN (b::{storeType})::text END
                   FROM (SELECT a, b, i, b IS NOT NULL AND pg_input_is_valid(b, $3) AS ok
                         FROM unnest($1::text[], $2::text[]) WITH ORDINALITY AS u(a, b, i)) AS s
                   ORDER BY i
                   """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = literals, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = texts, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = storeType, NpgsqlDbType = NpgsqlDbType.Text });

        var verdicts = new List<(bool?, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            verdicts.Add((reader.IsDBNull(0) ? null : reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return [.. verdicts];
    }

    // =========================================================================
    // The corpus
    // =========================================================================

    /// <summary>
    /// Families <c>docs/serialization.md</c> says the library refuses although PostgreSQL reads them:
    /// a refusal there is counted, not reported. An acceptance is still compared with the server.
    /// </summary>
    private static string? DocumentedRefusal(string family, Kind kind) => family switch
    {
        // J07, #dates-and-times-are-strict-iso-8601: "Temporal bounds and elements accept strict ISO
        // 8601 only … and nothing else." PostgreSQL's datetime input is lenient: it reads other
        // orders, words, zone names and 24:00, and skips punctuation it does not know.
        "non-ISO temporal" or "special input" or "infinity spelling" or "date with a time" or "timestamp without time"
            or "timestamptz without offset" or "zone name" or "time with an offset" or "hour 24 in a timestamp"
            or "leap second" or "stray punctuation" when IsTemporal(kind) => "J07",
        // J06, #parse-a-literal: "Decimal bounds follow PostgreSQL's numeric grammar: sign, digits,
        // point and exponent." PostgreSQL 16 also reads 0x, 0o, 0b and digit separators.
        "non-decimal numeric" or "digit separator numeric" when kind == Kind.Numeric => "J06",
        _ => null
    };

    private static bool IsTemporal(Kind kind) => kind is Kind.Date or Kind.Timestamp or Kind.Timestamptz or Kind.Time;

    private sealed record ElementCorpus(string[] Ascending, (string Text, string Family)[] Elements);

    private static Kind? KindOf(Type primitive) => primitive switch
    {
        _ when primitive == typeof(short)                                                 => Kind.Int2,
        _ when primitive == typeof(int)                                                   => Kind.Int4,
        _ when primitive == typeof(long)                                                  => Kind.Int8,
        _ when primitive == typeof(decimal)                                               => Kind.Numeric,
        _ when primitive == typeof(DateOnly) || primitive == typeof(LocalDate)            => Kind.Date,
        _ when primitive == typeof(DateTime) || primitive == typeof(LocalDateTime)        => Kind.Timestamp,
        _ when primitive == typeof(DateTimeOffset) || primitive == typeof(Instant)        => Kind.Timestamptz,
        _ when primitive == typeof(TimeOnly) || primitive == typeof(LocalTime)            => Kind.Time,
        _ when primitive == typeof(Guid)                                                  => Kind.Uuid,
        _ when primitive == typeof(string)                                                => Kind.Text,
        // YearMonth's literal (2026-01) is not the text of the daterange it is stored as.
        _                                                                                 => null
    };

    private static ElementCorpus Elements(Kind kind) => kind switch
    {
        Kind.Int2 => Integers("32767", "-32768", "32768", "-32769"),
        Kind.Int4 => Integers("2147483647", "-2147483648", "2147483648", "-2147483649"),
        Kind.Int8 => Integers("9223372036854775807", "-9223372036854775808", "9223372036854775808", "-9223372036854775809"),
        Kind.Numeric => new(["1.5", "2", "5.25", "9"],
        [
            ("1.5", "element"), ("+1.5", "sign"), ("-1.5", "sign"), ("-0", "sign"), (".5", "point"), ("5.", "point"),
            ("1e3", "exponent"), ("1E-3", "exponent"), ("1.5e+2", "exponent"), ("007.50", "leading zeros"), ("1.50", "scale"),
            ("NaN", "numeric special"), ("nan", "numeric special"), ("Infinity", "numeric special"), ("-Infinity", "numeric special"), ("inf", "numeric special"),
            ("1.00000000000000000000000000001", "beyond 28 digits"), ("0.00000000000000000000000000001", "beyond 28 digits"),
            ("1e-30", "beyond 28 digits"), ("79228162514264337593543950335.1", "beyond 28 digits"),
            ("79228162514264337593543950335", "extreme"), ("79228162514264337593543950336", "overflow"), ("1e30", "overflow"),
            ("0x1F", "non-decimal numeric"), ("0o17", "non-decimal numeric"), ("0b101", "non-decimal numeric"), ("1_000.5", "digit separator numeric"),
            ("1,5", "group separator"), ("1.5.5", "malformed element"), ("$1", "malformed element"), ("1 5", "malformed element"), ("e3", "malformed element")
        ]),
        Kind.Date => new(["2026-01-01", "2026-01-02", "2026-02-01", "2026-03-01"],
        [
            ("2026-01-01", "element"), ("0001-01-01", "extreme"), ("9999-12-31", "extreme"),
            ("10000-01-01", "beyond the CLR domain"), ("0001-01-01 BC", "beyond the CLR domain"), ("2026-02-30", "malformed element"),
            ("infinity", "infinity"), ("-infinity", "infinity"), ("Infinity", "infinity spelling"), ("INFINITY", "infinity spelling"),
            ("+infinity", "infinity spelling"), ("- infinity", "infinity spelling"),
            ("epoch", "special input"), ("now", "special input"), ("today", "special input"), ("tomorrow", "special input"),
            ("yesterday", "special input"), ("J2461042", "special input"),
            ("2026-1-1", "non-ISO temporal"), ("20260101", "non-ISO temporal"), ("Jan 1 2026", "non-ISO temporal"),
            ("01/02/2026", "non-ISO temporal"), ("2026-01-01T00:00:00", "date with a time"), ("2026-01-01 10:00", "date with a time")
        ]),
        Kind.Timestamp => new(["2026-01-01 10:00:00", "2026-01-01 10:00:00.000001", "2026-01-02 00:00:00", "2026-02-01 00:00:00"],
        [
            ("2026-01-01 10:00:00", "element"), ("2026-01-01T10:00:00", "T separator"), ("2026-01-01 10:00", "HH:mm"),
            ("2026-01-01 10:00:00.123456", "fraction"), ("2026-01-01 10:00:00.1234567", "sub-microsecond"), ("2026-01-01 10:00:00.0000015", "sub-microsecond"),
            ("2026-01-01 10:00:00+02", "offset"), ("2026-01-01 10:00:00Z", "offset"), ("2026-01-01 10:00:00+05:30", "offset"),
            ("0001-01-01 00:00:00", "extreme"), ("9999-12-31 23:59:59.999999", "extreme"), ("10000-01-01 00:00:00", "beyond the CLR domain"),
            ("2026-01-01 24:00:00", "hour 24 in a timestamp"), ("2026-01-01 23:59:60", "leap second"), ("2026-01-01", "timestamp without time"),
            ("infinity", "infinity"), ("-infinity", "infinity"), ("Infinity", "infinity spelling"), ("+infinity", "infinity spelling"),
            ("epoch", "special input"), ("now", "special input"), ("today", "special input"), ("allballs", "special input"),
            ("2026-1-1 10:00:00", "non-ISO temporal"), ("Jan 1 2026 10:00", "non-ISO temporal"), ("2026-01-01 10:00:00 BC", "beyond the CLR domain"),
            ("2026-01-01 25:00:00", "malformed element")
        ]),
        Kind.Timestamptz => new(["2026-01-01 10:00:00+00", "2026-01-01 10:00:00.000001+00", "2026-01-02 00:00:00+00", "2026-02-01 00:00:00+00"],
        [
            ("2026-01-01 10:00:00+00", "element"), ("2026-01-01 10:00:00+05:30", "offset"), ("2026-01-01 10:00:00Z", "offset"),
            ("2026-01-01T10:00:00+00", "T separator"), ("2026-01-01 10:00:00-03", "offset"), ("2026-01-01 10:00:00+05:53:28", "seconds offset"),
            ("2026-01-01 10:00:00+14:00", "offset"), ("2026-01-01 10:00:00+15:00", "offset range"), ("2026-01-01 10:00:00+15:59", "offset range"),
            ("2026-01-01 10:00:00.123456+00", "fraction"), ("2026-01-01 10:00:00.1234567+00", "sub-microsecond"),
            ("2026-01-01 10:00:00", "timestamptz without offset"), ("2026-01-01 10:00:00 UTC", "zone name"), ("2026-01-01 10:00:00 Europe/Berlin", "zone name"),
            ("2026-01-01 24:00:00+00", "hour 24 in a timestamp"), ("0001-01-01 00:00:00+00", "extreme"),
            ("infinity", "infinity"), ("-infinity", "infinity"), ("Infinity", "infinity spelling"),
            ("epoch", "special input"), ("now", "special input"), ("2026-1-1 10:00:00+00", "non-ISO temporal")
        ]),
        Kind.Time => new(["09:00:00", "09:00:00.000001", "12:00:00", "17:30:00"],
        [
            ("09:00:00", "element"), ("09:00", "HH:mm"), ("9:00", "non-ISO temporal"), ("0900", "non-ISO temporal"), ("09:00 PM", "non-ISO temporal"),
            ("09:00:00.123456", "fraction"), ("09:00:00.1234567", "sub-microsecond"), ("00:00:00", "extreme"), ("23:59:59.999999", "extreme"),
            ("24:00:00", "24:00"), ("24:00", "24:00"), ("24:00:00.000001", "24:00"), ("23:59:60", "leap second"), ("25:00:00", "malformed element"),
            ("09:00:00+02", "time with an offset"), ("T09:00:00", "non-ISO temporal"), ("allballs", "special input"), ("now", "special input"),
            ("infinity", "infinity spelling")
        ]),
        Kind.Uuid => new(["00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002",
                          "6f9619ff-8b86-d011-b42d-00c04fc964ff", "ffffffff-ffff-ffff-ffff-ffffffffffff"],
        [
            ("6f9619ff-8b86-d011-b42d-00c04fc964ff", "element"), ("6F9619FF-8B86-D011-B42D-00C04FC964FF", "upper case"),
            ("6f9619ff8b86d011b42d00c04fc964ff", "no hyphens"), ("6f96-19ff-8b86-d011-b42d-00c0-4fc9-64ff", "hyphen groups"),
            ("{6f9619ff-8b86-d011-b42d-00c04fc964ff}", "braces"), ("(6f9619ff-8b86-d011-b42d-00c04fc964ff)", "parentheses"),
            ("6f9619ff-8b86-d011-b42d-00c04fc964f", "malformed element"), ("6f9619ff-8b86-d011-b42d-00c04fc964ffa", "malformed element"),
            ("0x6f9619ff8b86d011b42d00c04fc964ff", "malformed element"), ("urn:uuid:6f9619ff-8b86-d011-b42d-00c04fc964ff", "malformed element")
        ]),
        Kind.Text => new(["a", "b", "c", "d"],
        [
            ("a", "element"), ("NULL", "null element"), ("null", "null element"), ("ab c", "inner whitespace"), ("é", "non-ASCII"),
            ("\u00A0a", "unicode whitespace"), ("a\u2003", "unicode whitespace"), ("a\u0000b", "NUL"), ("\"a\"\"b\"", "nested quotes"),
            ("a\\,b", "backslash escape"), ("\\\"a", "backslash escape"), ("a\\\\b", "backslash escape"), ("\\N", "backslash escape")
        ]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static ElementCorpus Integers(string max, string min, string aboveMax, string belowMin) => new(["1", "2", "5", "9"],
    [
        ("1", "element"), ("+1", "sign"), ("-1", "sign"), ("-0", "sign"), ("007", "leading zeros"), ("1e3", "exponent"), ("1.0", "fraction"),
        ("1.5", "fraction"), (max, "extreme"), (min, "extreme"), (aboveMax, "overflow"), (belowMin, "overflow"),
        ("0x1F", "non-decimal integer"), ("0o17", "non-decimal integer"), ("0b101", "non-decimal integer"), ("1_000", "digit separator integer"),
        ("\uFF11", "unicode digit"), ("\u0663", "unicode digit"), ("1 2", "malformed element"), ("- 1", "malformed element"),
        ("NaN", "numeric special"), ("infinity", "numeric special")
    ]);

    /// <summary>The literals for one element kind and grammar, as (family, literal, element) — element set for the element families.</summary>
    private static IEnumerable<(string Family, string Literal, string? Element)> Corpus(Kind kind, Grammar grammar)
    {
        // Bare: range_in and array_in keep whitespace inside a bound or element, so a timestamp needs no
        // quotes; the quoted families spell them.
        var elements = Elements(kind);
        var (a, b, c, d) = (elements.Ascending[0], elements.Ascending[1], elements.Ascending[2], elements.Ascending[3]);

        IEnumerable<(string, string)> structural = grammar switch
        {
            Grammar.Range      => RangeFamilies(a, b, c, d),
            Grammar.Multirange => MultirangeFamilies(a, b, c, d),
            _                  => ArrayFamilies(a, b, c, d, kind)
        };

        foreach (var (family, literal) in structural)
            yield return (family, literal, null);

        // An infinite temporal bound next to a finite one: the form daterange(x, 'infinity') prints.
        if (IsTemporal(kind) && kind != Kind.Time && grammar != Grammar.Array)
        {
            foreach (var range in new[] { $"[{a},infinity)", $"[{a},infinity]", $"(-infinity,{a}]", $"[-infinity,{a})", "[-infinity,infinity)" })
                yield return ("infinity bound", grammar == Grammar.Range ? range : $"{{{range}}}", null);
        }

        foreach (var (text, family) in elements.Elements)
        {
            switch (grammar)
            {
                case Grammar.Range:
                    yield return (family, $"[{Quote(text)},)", text);
                    yield return (family, $"(,{Quote(text)}]", text);
                    break;
                case Grammar.Multirange:
                    yield return (family, $"{{[{Quote(text)},)}}", text);
                    break;
                default:
                    yield return (family, $"{{{ArrayElement(text)}}}", text);
                    yield return (family, $"{{\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}", null);
                    break;
            }
        }
    }

    // A bound with a comma or a bracket is quoted, as PostgreSQL prints it; one that holds a quote or a
    // backslash is the element's own test of them and stays as written.
    private static string Quote(string text) =>
        text.IndexOfAny([',', '(', ')', '[', ']', '{', '}']) >= 0 && !text.Contains('"') && !text.Contains('\\')
            ? $"\"{text}\""
            : text;

    // An array element is written bare unless array_in would split it; the families that test
    // quoting and escapes spell it themselves.
    private static string ArrayElement(string text) =>
        text.IndexOfAny(['{', '}', ',']) >= 0 && !text.Contains('"') && !text.Contains('\\') ? $"\"{text}\"" : text;

    private static IEnumerable<(string, string)> RangeFamilies(string a, string b, string c, string d)
    {
        foreach (var literal in new[] { $"[{a},{b})", $"({a},{b}]", $"[{a},{b}]", $"({a},{b})", $"[{a},{d})", $"(,{b})", $"[,{b})", $"({a},)", $"({a},]", "(,)", "[,]" })
            yield return ("bounds", literal);
        foreach (var literal in new[] { $"[{a},{a}]", $"[{a},{a})", $"({a},{a}]", $"({a},{a})" })
            yield return ("equal bounds", literal);
        foreach (var literal in new[] { $"[{b},{a})", $"({b},{a}]", $"[{b},{a}]", $"({b},{a})", $"[{d},{a})" })
            yield return ("inverted", literal);
        foreach (var literal in new[] { "empty", "EMPTY", "Empty", " empty ", "\tempty\n", "\"empty\"", "empty x", "emptyx", "empt", "\u00A0empty", "(empty)" })
            yield return ("empty keyword", literal);
        foreach (var literal in new[] { "", " ", "\t" })
            yield return ("blank", literal);
        foreach (var literal in new[] { $"[\"{a}\",\"{b}\")", $"[\"{a}\",{b})", $"[\"\",{b})", $"[{a},\"\")", $"[{a}\"\",{b})", $"[\"\"{a},{b})", $"[\"{a},{b})" })
            yield return ("quoted bound", literal);
        foreach (var literal in new[] { $"[\\{a},{b})", $"[{a},{b}\\)", $"[{a}\\,{b},{c})", $"[\"\\{a}\",{b})" })
            yield return ("backslash escape", literal);
        foreach (var literal in new[] { $"[{a},{b})x", $"[{a},{b}) x", $"[{a},{b});", $"[{a},{b}))", $"x[{a},{b})", $"[{a},{b}", $"[{a} {b})", $"[{a},{b},{c})", $"{{[{a},{b})}}", $"[{a};{b})", $"[{a},{b}]]", $"{a},{b}" })
            yield return ("junk", literal);
        // A bound that keeps a quote, a backslash or a bracket: no element type reads one, except
        // that PostgreSQL's datetime input skips punctuation it does not know.
        foreach (var literal in new[] { $"[\"{a}\"\"\",{b})", $"[\\\\{a},{b})", $"[[{a},{b})" })
            yield return ("stray punctuation", literal);
        foreach (var literal in new[] { $" [{a},{b}) ", $"\t[{a},{b})\n", $"[ {a} , {b} )", $"[\" {a}\",{b})", $"[{a},\"{b} \")", $"[\\ {a},{b})", $"\u00A0[{a},{b})", $"[{a},{b})\u2003", $"[\u00A0{a},{b})", $"\u3000[{a},{b})", $"[{a},{b})\u0085" })
            yield return ("whitespace", literal);
    }

    private static IEnumerable<(string, string)> MultirangeFamilies(string a, string b, string c, string d)
    {
        foreach (var literal in new[] { "{}", "{ }", $"{{[{a},{b})}}", $"{{[{a},{b}),[{c},{d})}}", $"{{(,{a}),[{c},)}}", "{(,)}", $"{{[{a},{b}],({c},{d}]}}" })
            yield return ("bounds", literal);
        foreach (var literal in new[] { $"{{[{a},{c}),[{b},{d})}}", $"{{[{a},{b}),[{b},{c})}}", $"{{[{c},{d}),[{a},{b})}}", $"{{[{a},{b}),[{a},{b})}}",
                                        "{empty}", $"{{empty,[{a},{b})}}", $"{{[{a},{a})}}", $"{{(,),[{a},{b})}}", $"{{[{a},{d}),[{b},{c})}}" })
            yield return ("canonicalized", literal);
        foreach (var literal in new[] { $"{{[{b},{a})}}", $"{{[{b},{a}),[{c},{d})}}", $"{{[{c},{d}),({d},{c}]}}" })
            yield return ("inverted", literal);
        foreach (var literal in new[] { "empty", "{EMPTY}", $"{{Empty,[{a},{b})}}", "{ empty }", "{\"empty\"}" })
            yield return ("empty keyword", literal);
        foreach (var literal in new[] { "", " " })
            yield return ("blank", literal);
        foreach (var literal in new[] { $"{{[{a},{b})}}x", $"{{[{a},{b})", $"[{a},{b})", $"{{[{a},{b}),}}", $"{{,[{a},{b})}}", $"{{[{a},{b}) [{c},{d})}}",
                                        $"{{[{a},{b})}};", $"{{{{[{a},{b})}}}}", $"{{\"[{a},{b})\"}}", $"{{[{a},{b}),,[{c},{d})}}", $"x{{[{a},{b})}}" })
            yield return ("junk", literal);
        foreach (var literal in new[] { $" {{[{a},{b})}} ", $"{{ [{a},{b}) , [{c},{d}) }}", $"{{[\" {a}\",{b})}}", $"\u00A0{{[{a},{b})}}", $"{{[{a},{b})}}\u2003", $"{{\u00A0[{a},{b})}}" })
            yield return ("whitespace", literal);
        foreach (var literal in new[] { $"{{[\"{a}\",\"{b}\")}}", $"{{[\"\",{b})}}", $"{{[\\{a},{b})}}" })
            yield return ("quoted bound", literal);
    }

    private static IEnumerable<(string, string)> ArrayFamilies(string a, string b, string c, string d, Kind kind)
    {
        foreach (var literal in new[] { "{}", "{ }", $"{{{a}}}", $"{{{a},{b}}}", $"{{{b},{a}}}", $"{{{a},{a}}}", $"{{{a},{b},{a}}}", $"{{{d},{c},{b},{a}}}" })
            yield return ("bounds", literal);
        foreach (var literal in new[] { $"{{\"{a}\"}}", $"{{\"{a}\",\"{b}\"}}", $"{{\"{a}\" , \"{b}\"}}", "{\"\"}", $"{{\"{a}\"{b}}}", $"{{{a}\"\"}}", $"{{\"{a}\"\"{b}\"}}" })
            yield return ("quoted element", literal);
        foreach (var literal in new[] { "{NULL}", $"{{{a},NULL}}", "{null}", "{\"NULL\"}", "{NuLl}" })
            yield return ("null element", literal);
        foreach (var literal in new[] { $"{{{{{a}}},{{{b}}}}}", $"{{{{{a},{b}}}}}", $"[1:1][1:1]={{{{{a}}}}}" })
            yield return ("dimensions", literal);
        foreach (var literal in new[] { $"[1:1]={{{a}}}", $"[0:1]={{{a},{b}}}", $"[1:2]={{{a}}}", "{{}}", "{{},{}}" })
            yield return ("dimension decoration", literal);
        foreach (var literal in new[] { $"{{{a}}}x", $"{{{a}", a, $"{{{a},}}", $"{{,{a}}}", $"{{{a},,{b}}}", $"{{{a}}}}}", $"{{{{{a}}}", $"{{{a}}};", $"x{{{a}}}", $"{{{a}}}{{{b}}}" })
            yield return ("junk", literal);
        foreach (var literal in new[] { $" {{{a}}} ", $"{{ {a} }}", $"{{ {a} , {b} }}", $"{{\" {a}\"}}", $"{{\"{a} \"}}", $"{{\\ {a}}}", $"\u00A0{{{a}}}", $"{{{a}}}\u2003", $"\t{{{a}}}\n", $"{{{a}\u00A0}}" })
            yield return ("whitespace", literal);
        foreach (var literal in new[] { "", " ", "empty" })
            yield return ("blank", literal);
        foreach (var literal in new[] { $"{{\\{a}}}", $"{{\"\\{a}\"}}", $"{{{a}\\,{b}}}" })
            yield return ("backslash escape", literal);

        // An element spelled as each of its neighbours prints it: a duplicate written two ways is one element.
        if (kind == Kind.Text) yield return ("quoted element", $"{{{a},\"{a}\"}}");
    }

    private static string Show(string literal)
        => string.Concat(literal.Select(ch => ch < 0x20 || ch > 0x7E ? $"\\u{(int)ch:X4}" : ch.ToString()));
}

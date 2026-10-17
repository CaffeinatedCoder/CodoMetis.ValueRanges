using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;

/// <summary>The run's seed and budget, from the environment, with the defaults CI runs.</summary>
internal static class FuzzRun
{
    public const int DefaultSeed = 20261017;

    public const int DefaultRows = 200;

    /// <summary><c>VALUERANGES_FUZZ_SEED</c>, or the fixed default — CI replays the same rows every run.</summary>
    public static int Seed { get; } = Read("VALUERANGES_FUZZ_SEED", DefaultSeed);

    /// <summary><c>VALUERANGES_FUZZ_ROWS</c>: rows drawn per type. The first rows of a seed never change with it.</summary>
    public static int Rows { get; } = Math.Max(1, Read("VALUERANGES_FUZZ_ROWS", DefaultRows));

    /// <summary>
    /// Whether the registered candidates can be required to fail. They are rows of the default seed;
    /// another seed draws other rows, so a candidate not met there is not evidence that it was fixed.
    /// </summary>
    public static bool IsDefaultSeed => Seed == DefaultSeed;

    /// <summary>A generator per subject, so adding a type does not reshuffle the rows of another.</summary>
    public static Random For(string subject)
    {
        var hash = 2166136261u;
        foreach (var c in subject) hash = (hash ^ c) * 16777619u;
        return new Random(unchecked(Seed ^ (int)hash));
    }

    private static int Read(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}

/// <summary>
/// One operation of a sweep. The expression is the single definition of the operation: compiled,
/// it is the in-memory leg; spliced into a query, the EF leg. <see cref="Sql"/> is the operation
/// written by hand against PostgreSQL's own operators, and <see cref="Agrees"/> the SQL that asks
/// the server whether its answer equals the model's.
/// </summary>
internal sealed class FuzzOp<TL, TR>
{
    public FuzzOp(string name, LambdaExpression expression, Func<string, string, string> sql,
                  Func<object?, string, string>? agrees, bool unary = false)
    {
        Name       = name;
        Expression = expression;
        Sql        = sql;
        Agrees     = agrees;
        Unary      = unary;

        var (l, r) = (System.Linq.Expressions.Expression.Parameter(typeof(TL)), System.Linq.Expressions.Expression.Parameter(typeof(TR)));
        Memory = System.Linq.Expressions.Expression.Lambda<Func<TL, TR, object?>>(
            System.Linq.Expressions.Expression.Convert(FuzzEngine.Splice(expression, l, r), typeof(object)), l, r).Compile();
    }

    public string Name { get; }

    public LambdaExpression Expression { get; }

    public Func<string, string, string> Sql { get; }

    /// <summary>
    /// The SQL asking the server whether its answer equals the model's; <see langword="null"/> for an
    /// operation producing a range, multirange or value set, whose answer is read back the way EF Core
    /// reads it and compared in memory. Read back, a value PostgreSQL holds beyond the model's domain —
    /// <c>(,-2147483648)</c>, or a bound at <c>-infinity</c> — is the value the model sees, which is the
    /// documented domain boundary rather than a disagreement.
    /// </summary>
    public Func<object?, string, string>? Agrees { get; }

    /// <summary>The result type, read back through its mapping when <see cref="Agrees"/> is null.</summary>
    public Type ResultType => Expression.ReturnType;

    /// <summary>Reads the left operand only, so the right one's constant and parameter forms add nothing.</summary>
    public bool Unary { get; }

    public Func<TL, TR, object?> Memory { get; }
}

/// <summary>
/// One question for the server: the operation's SQL, and either the SQL deciding whether it equals the
/// model's answer, or the result type to read it back as and the model's answer to compare it with.
/// </summary>
internal sealed record FuzzQuestion(string Actual, string? Agree, Type? Read, object? Expected);

/// <summary>A drawn operand pair, with the shape labels a candidate row is keyed on.</summary>
internal sealed record FuzzCase<TL, TR>(int Id, TL Left, TR Right, string LeftShape, string RightShape);

/// <summary>
/// Everything one sweep needs: the subject, the operand columns, the drawn rows and probes, how to
/// write an operand as SQL, and the operations.
/// </summary>
internal sealed record FuzzSweep<TRow, TL, TR>(
    string                         Subject,
    string                         LeftColumn,
    string?                        RightColumn,
    IReadOnlyList<FuzzCase<TL, TR>> Cases,
    IReadOnlyList<(TR Value, string Shape)> Probes,
    Func<TL, string>               LeftSql,
    Func<TR, string>               RightSql,
    IReadOnlyList<FuzzOp<TL, TR>>  Ops,
    Func<TL, bool>?                LeftAtEdge = null,
    Func<TR, bool>?                RightAtEdge = null
) where TRow : class;

/// <summary>
/// Runs a sweep's three legs — memory, EF Core, and PostgreSQL on the literals — and returns one line
/// per disagreement.
/// </summary>
/// <remarks>
/// <para>
/// The EF leg runs the operations once over the right operand as a <b>column</b> (every row's own
/// pair, written through Npgsql's parameters on insert), and once per probe as an inlined
/// <b>constant</b> and as a <b>parameter</b>, against every row's left column. Each of those is one
/// query projecting every operation; when it fails to translate or execute, each operation is run
/// alone so the failure names the operation.
/// </para>
/// <para>
/// The literal leg asks PostgreSQL for every column pair, and for a probe pair only once the other
/// two legs disagree on it — a probe pair is otherwise a second answer to a question the column pairs
/// already put to the server, at sixteen times the round trips.
/// </para>
/// </remarks>
internal static class FuzzEngine
{
    private const int LiteralBatch = 250;

    public static async Task<List<string>> RunAsync<TRow, TL, TR>(FuzzSweep<TRow, TL, TR> sweep, Func<FuzzDbContext> context)
        where TRow : class
    {
        var failures = new List<string>();
        var cases    = sweep.Cases;

        // Sources of the right operand: the column, then every probe twice.
        var sources = new List<(string Name, Func<ParameterExpression, Expression> Right, Func<FuzzCase<TL, TR>, TR> Value, string Shape, bool Probe)>();
        if (sweep.RightColumn is { } column)
            sources.Add(("column", row => Expression.Property(row, column), c => c.Right, "", false));
        for (var index = 0; index < sweep.Probes.Count; index++)
        {
            var (probe, shape) = sweep.Probes[index];
            sources.Add(($"constant {index}", _ => Inline(probe), _ => probe, shape, true));
            sources.Add(($"parameter {index}", _ => Captured(probe), _ => probe, shape, true));
        }

        foreach (var source in sources)
        {
            var ops = sweep.Ops.Where(op => !(source.Probe && op.Unary)).ToList();
            if (ops.Count == 0) continue;

            var ef = await QueryEf<TRow>(context, sweep.LeftColumn, source.Right, ops.Select(op => op.Expression).ToList());

            // The literal leg, for column pairs (and for a probe-only sweep, every pair).
            var literal = !source.Probe || sweep.RightColumn is null
                              ? await Literal(context, sweep, cases, source.Value, ops)
                              : null;

            var lazy = new List<(FuzzCase<TL, TR> Case, int Op, string Memory, string Ef)>();

            for (var opIndex = 0; opIndex < ops.Count; opIndex++)
            {
                var op = ops[opIndex];

                if (ef.Errors.TryGetValue(opIndex, out var efError))
                {
                    failures.Add($"{sweep.Subject} {op.Name} [EF {source.Name}]: {efError}");
                    continue;
                }

                foreach (var @case in cases)
                {
                    var right  = source.Value(@case);
                    var memory = Evaluate(op, @case.Left, right, out var memoryError);
                    var efText = ef.Values.TryGetValue(@case.Id, out var row) ? row[opIndex] : Missing;
                    var efOk   = memoryError is null && Equals(memory, efText);

                    (bool Agree, string Text)? pg = literal is not null && literal.TryGetValue((@case.Id, opIndex), out var answer) ? answer : null;
                    if (efOk && pg is null or { Agree: true }) continue;

                    if (pg is null)
                    {
                        lazy.Add((@case, opIndex, memoryError ?? Show(memory), Show(efText)));
                        continue;
                    }

                    failures.Add(Line(sweep.Subject, op.Name, @case, source.Shape, source.Name, sweep, right,
                                      memoryError ?? Show(memory), Show(efText), pg.Value.Text));
                }
            }

            if (lazy.Count > 0)
            {
                var asked = await EvaluateAll(context, lazy.Select(l => Question(sweep, ops[l.Op], l.Case.Left, source.Value(l.Case))
                                                                           ?? new FuzzQuestion("NULL", "false", null, null)).ToList());
                for (var index = 0; index < lazy.Count; index++)
                {
                    var (@case, opIndex, memory, efText) = lazy[index];
                    failures.Add(Line(sweep.Subject, ops[opIndex].Name, @case, source.Shape, source.Name, sweep,
                                      source.Value(@case), memory, efText, asked[index].Text));
                }
            }
        }

        return failures;
    }

    // -------------------------------------------------------------------------
    // The EF leg
    // -------------------------------------------------------------------------

    private static readonly object Missing = new MissingRow();

    private sealed class MissingRow
    {
        public override string ToString() => "<row not returned>";
    }

    private static async Task<(Dictionary<int, object?[]> Values, Dictionary<int, string> Errors)> QueryEf<TRow>(
        Func<FuzzDbContext> context, string leftColumn, Func<ParameterExpression, Expression> right, IReadOnlyList<LambdaExpression> ops)
        where TRow : class
    {
        try
        {
            return (await Project<TRow>(context, leftColumn, right, ops), []);
        }
        catch (Exception)
        {
            // One operation spoiled the query: run each alone, so the failure names it.
        }

        var values = new Dictionary<int, object?[]>();
        var errors = new Dictionary<int, string>();
        for (var index = 0; index < ops.Count; index++)
        {
            try
            {
                foreach (var (id, row) in await Project<TRow>(context, leftColumn, right, [ops[index]]))
                {
                    if (!values.TryGetValue(id, out var all)) values[id] = all = new object?[ops.Count];
                    all[index] = row[0];
                }
            }
            catch (Exception exception)
            {
                errors[index] = $"{exception.GetType().Name}: {FirstLine((exception.InnerException ?? exception).Message)}";
            }
        }

        return (values, errors);
    }

    private static async Task<Dictionary<int, object?[]>> Project<TRow>(
        Func<FuzzDbContext> context, string leftColumn, Func<ParameterExpression, Expression> right, IReadOnlyList<LambdaExpression> ops)
        where TRow : class
    {
        var row  = Expression.Parameter(typeof(TRow), "row");
        var left = Expression.Property(row, leftColumn);
        var body = Expression.MemberInit(
            Expression.New(typeof(FuzzOut)),
            Expression.Bind(typeof(FuzzOut).GetProperty(nameof(FuzzOut.Id))!, Expression.Property(row, "Id")),
            Expression.Bind(
                typeof(FuzzOut).GetProperty(nameof(FuzzOut.Values))!,
                Expression.NewArrayInit(
                    typeof(object),
                    ops.Select(op => Expression.Convert(Splice(op, left, right(row)), typeof(object))))));

        await using var db = context();
        var results = await db.Set<TRow>().AsNoTracking()
                              .Select(Expression.Lambda<Func<TRow, FuzzOut>>(body, row))
                              .ToListAsync();
        return results.ToDictionary(r => r.Id, r => r.Values);
    }

    /// <summary>
    /// A constant the way EF Core inlines one: a call evaluated on the client with nothing captured,
    /// which reaches the translator typed as the value's runtime type (a range's sealed variant).
    /// </summary>
    private static Expression Inline<T>(T value) =>
        Expression.Call(typeof(FuzzEngine).GetMethod(nameof(Identity), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                                          .MakeGenericMethod(typeof(T)),
                        Expression.Constant(value, typeof(T)));

    private static T Identity<T>(T value) => value;

    /// <summary>A captured variable, which EF Core sends as a parameter.</summary>
    private static Expression Captured<T>(T value) =>
        Expression.Field(Expression.Constant(new StrongBox<T>(value)), nameof(StrongBox<T>.Value));

    internal static Expression Splice(LambdaExpression lambda, Expression left, Expression right) =>
        new Replace(lambda.Parameters[0], left, lambda.Parameters[1], right).Visit(lambda.Body);

    private sealed class Replace(ParameterExpression a, Expression withA, ParameterExpression b, Expression withB) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == a ? withA : node == b ? withB : node;
    }

    // -------------------------------------------------------------------------
    // The literal leg
    // -------------------------------------------------------------------------

    private static async Task<Dictionary<(int, int), (bool Agree, string Text)>> Literal<TRow, TL, TR>(
        Func<FuzzDbContext> context, FuzzSweep<TRow, TL, TR> sweep, IReadOnlyList<FuzzCase<TL, TR>> cases,
        Func<FuzzCase<TL, TR>, TR> right, IReadOnlyList<FuzzOp<TL, TR>> ops)
        where TRow : class
    {
        var keys      = new List<(int, int)>();
        var questions = new List<FuzzQuestion>();

        foreach (var @case in cases)
        for (var opIndex = 0; opIndex < ops.Count; opIndex++)
        {
            var question = Question(sweep, ops[opIndex], @case.Left, right(@case));
            if (question is null) continue; // the model threw; reported by the comparison with EF
            keys.Add((@case.Id, opIndex));
            questions.Add(question);
        }

        var answers = await EvaluateAll(context, questions);
        var result  = new Dictionary<(int, int), (bool Agree, string Text)>();
        for (var index = 0; index < keys.Count; index++) result[keys[index]] = answers[index];
        return result;
    }

    private static FuzzQuestion? Question<TRow, TL, TR>(FuzzSweep<TRow, TL, TR> sweep, FuzzOp<TL, TR> op, TL left, TR right)
        where TRow : class
    {
        var memory = Evaluate(op, left, right, out var error);
        if (error is not null) return null;

        var actual = op.Sql(sweep.LeftSql(left), op.Unary ? "" : sweep.RightSql(right));
        return op.Agrees is { } agrees
                   ? new FuzzQuestion(actual, agrees(memory, actual), null, null)
                   : new FuzzQuestion(actual, null, op.ResultType, memory);
    }

    /// <summary>
    /// Evaluates each question — PostgreSQL's answer as text, and whether it equals the model's — in
    /// batches on EF Core's own connection, so a value read back goes through the mapping's converter
    /// exactly as a query result does. A batch the server refuses is split until the refusing
    /// expression is alone, and its error is the answer.
    /// </summary>
    internal static async Task<List<(bool Agree, string Text)>> EvaluateAll(Func<FuzzDbContext> context, IReadOnlyList<FuzzQuestion> questions)
    {
        await using var db = context();
        var mappings = db.GetService<IRelationalTypeMappingSource>();
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();

        var results = new List<(bool Agree, string Text)>(questions.Count);
        foreach (var batch in questions.Chunk(LiteralBatch)) results.AddRange(await Evaluate(connection, mappings, batch));
        return results;
    }

    private static async Task<List<(bool Agree, string Text)>> Evaluate(DbConnection connection, IRelationalTypeMappingSource mappings, FuzzQuestion[] batch)
    {
        var projection = string.Join(", ", batch.Select((q, i) => $"({q.Agree ?? q.Actual}) AS v{i}, ({q.Actual})::text AS t{i}"));
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {projection}";
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();

            var results = new List<(bool Agree, string Text)>(batch.Length);
            for (var index = 0; index < batch.Length; index++)
            {
                var question = batch[index];
                var text     = reader.IsDBNull(2 * index + 1) ? "NULL" : reader.GetString(2 * index + 1);
                if (question.Read is null)
                {
                    results.Add((!reader.IsDBNull(2 * index) && reader.GetBoolean(2 * index), text));
                    continue;
                }

                try
                {
                    var read = Reader(mappings, question.Read)(reader, 2 * index);
                    results.Add((Equals(question.Expected, read), $"{text} (read back: {Show(read)})"));
                }
                catch (Exception exception)
                {
                    results.Add((false, $"{text} (read back fails: {exception.GetType().Name}: {FirstLine(exception.Message)})"));
                }
            }

            return results;
        }
        catch (PostgresException) when (batch.Length > 1)
        {
            var half  = batch.Length / 2;
            var first = await Evaluate(connection, mappings, batch[..half]);
            first.AddRange(await Evaluate(connection, mappings, batch[half..]));
            return first;
        }
        catch (PostgresException exception)
        {
            return [(false, $"<error {exception.SqlState}: {exception.MessageText}>")];
        }
    }

    private static readonly ConcurrentDictionary<Type, Func<DbDataReader, int, object?>> Readers = new();

    /// <summary>Reads a column as EF Core does: the provider value through the mapping's converter.</summary>
    private static Func<DbDataReader, int, object?> Reader(IRelationalTypeMappingSource mappings, Type type) =>
        Readers.GetOrAdd(type, t =>
        {
            var mapping   = mappings.FindMapping(t) ?? throw new InvalidOperationException($"No mapping for {t}.");
            var converter = mapping.Converter ?? throw new InvalidOperationException($"The mapping for {t} has no converter.");
            var get       = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetFieldValue))!.MakeGenericMethod(converter.ProviderClrType);
            return (reader, ordinal) => converter.ConvertFromProvider(get.Invoke(reader, [ordinal]));
        });

    // -------------------------------------------------------------------------
    // Comparison and reporting
    // -------------------------------------------------------------------------

    private static object? Evaluate<TL, TR>(FuzzOp<TL, TR> op, TL left, TR right, out string? error)
    {
        try
        {
            error = null;
            return op.Memory(left, right);
        }
        catch (Exception exception)
        {
            error = $"<throws {exception.GetType().Name}: {FirstLine(exception.Message)}>";
            return null;
        }
    }

    private static string Line<TRow, TL, TR>(string subject, string op, FuzzCase<TL, TR> @case, string probeShape, string source,
                                             FuzzSweep<TRow, TL, TR> sweep, TR right, string memory, string ef, string pg)
        where TRow : class
    {
        var rightShape = probeShape.Length > 0 ? probeShape : @case.RightShape;
        var operands   = sweep.RightColumn is null && probeShape.Length == 0
                             ? Show(@case.Left)
                             : $"{Show(@case.Left)} ⋄ {Show(right)}";

        // A row touching an exact extreme says so before its shapes, so a candidate for a defect at the
        // edge of a domain cannot also swallow the same operation's rows in the middle of it.
        // An operation that reads only its left operand is at the edge only through that operand.
        var unary = sweep.Ops.First(o => o.Name == op).Unary;
        var edge  = sweep.LeftAtEdge?.Invoke(@case.Left) == true || !unary && sweep.RightAtEdge?.Invoke(right) == true
                        ? " at the domain edge"
                        : "";
        return $"{subject} {op}{edge} {@case.LeftShape}×{rightShape} [{source}]: {operands} → memory={memory}, EF={ef}, PostgreSQL={pg} "
             + $"(seed={FuzzRun.Seed}, row={@case.Id})";
    }

    internal static string Show(object? value) => value switch
    {
        null               => "null",
        bool b             => b ? "true" : "false",
        string s           => $"\"{s}\"",
        DateTimeOffset d   => d.ToString("O", CultureInfo.InvariantCulture),
        DateTime d         => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f     => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable e      => $"[{string.Join(",", e.Cast<object?>().Select(Show))}]",
        _                  => value.ToString() ?? "null"
    };

    private static string FirstLine(string message) => message.Split('\n')[0].Trim();
}

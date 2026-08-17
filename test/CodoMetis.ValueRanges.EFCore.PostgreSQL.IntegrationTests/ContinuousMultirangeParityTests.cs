using System.Globalization;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Sweeps the continuous multirange algebra against <c>nummultirange</c> — every subset of a small
/// universe of fragments, and every ordered pair of those subsets, through <c>+</c>, <c>-</c>,
/// <c>*</c> and the complement.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the multirange surface no oracle could reach.
/// <c>SmallModelMultirangeOracleTests</c> is exhaustive and discrete <em>by construction</em>: its
/// model is a set of grid points, and decomposing a point set into maximal runs is the canonical
/// form only when consecutive points are contiguous. Over the reals it is not — <c>{0, 0.5}</c> is
/// both the interval <c>[0, 0.5]</c> and the two singletons, at every grid resolution — so the
/// continuous case was left to the two-element <c>Set.Union</c>/<c>Set.Except</c> cases in
/// <c>SmallModelOracleTests</c> and to worked examples. What went unswept is precisely
/// <b>continuous adjacency deciding a merge</b>: whether <c>[1,2)</c> absorbs into <c>[0,1)</c>,
/// whether <c>(1,2)</c> stays out of it, and whether the degenerate <c>[2,2]</c> closes the gap.
/// </para>
/// <para>
/// PostgreSQL has no such difficulty. Its multirange constructor merges overlapping <em>and
/// adjacent</em> ranges, and it decides adjacency from the bounds rather than from a point set, so
/// it is a second implementation of exactly the rule the in-memory model could not be checked
/// against. It answers as an oracle here in the same way it does for single ranges in
/// <see cref="ShapeMatrixParityTests"/> — that is the whole reason this closes the hole rather than
/// merely widening the examples.
/// </para>
/// <para>
/// The fragments are chosen so that every pair of neighbours at each meeting point differs only in
/// inclusivity, which is the one thing that can decide the merge when there is no step: at 1 the
/// universe holds a bound that closes the gap and one that does not, and at 2 it holds a
/// single-value range whose only purpose is to be the thing a merge either absorbs or leaves
/// standing. The server's text is re-parsed through the model before comparing, so a difference in
/// rendering is not mistaken for a difference in value.
/// </para>
/// </remarks>
[TestClass]
public sealed class ContinuousMultirangeParityTests
{
    private static string Literal(object range)
        => ((IFormattable)range).ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// Six fragments — 64 subsets and 4,096 ordered pairs. Whole-number bounds throughout, so a
    /// decimal's scale never becomes the difference between the two renderings.
    /// </summary>
    private static readonly (string Name, DecimalRange Range)[] Universe =
    [
        ("[0,1)", DecimalRange.CreateFinite(0m, 1m)),
        ("[1,2)", DecimalRange.CreateFinite(1m, 2m)),               // meets [0,1) exactly: merges
        ("(1,2)", DecimalRange.CreateFinite(1m, 2m, false, false)), // leaves 1 out: does not merge
        ("[2,2]", DecimalRange.CreateFinite(2m, 2m, true, true)),   // one value, and it closes gaps
        ("(,0)",  DecimalRange.CreateUnboundedStart(0m, false)),    // merges with [0,1)
        ("(2,)",  DecimalRange.CreateUnboundedEnd(2m, false))       // merges with [2,2]
    ];

    private const int Subsets = 1 << 6;

    private static RangeSet<DecimalRange, decimal> Build(int mask) =>
        RangeSet<DecimalRange, decimal>.From(Fragments(mask));

    private static DecimalRange[] Fragments(int mask) =>
        [.. Indices(mask).Select(index => Universe[index].Range)];

    /// <summary>
    /// The server-side value, built from the <em>raw</em> fragments rather than from the model's
    /// merged elements — otherwise PostgreSQL would be handed input that is already canonical and
    /// asked to do nothing, and its merge would never run.
    /// </summary>
    private static string Constructor(int mask) =>
        $"nummultirange({string.Join(", ", Fragments(mask).Select(range => $"'{Literal(range)}'::numrange"))})";

    private static string Name(int mask) =>
        mask == 0 ? "{}" : $"{{{string.Join(", ", Indices(mask).Select(index => Universe[index].Name))}}}";

    private static IEnumerable<int> Indices(int mask) =>
        Enumerable.Range(0, Universe.Length).Where(index => (mask & (1 << index)) != 0);

    /// <summary>
    /// Normalization and the complement, per subset: what the model merges the fragments into,
    /// what is left over, and that complementing twice is the identity.
    /// </summary>
    [TestMethod]
    public async Task EverySubset_MergesAndComplementsAsPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        var labels = new List<(string Label, string Model)>();
        var sql    = new List<string>();

        for (var mask = 0; mask < Subsets; mask++)
        {
            var set = Build(mask);

            labels.Add(($"From {Name(mask)}", set.ToString()));
            sql.Add(Constructor(mask));

            labels.Add(($"Complement {Name(mask)}", set.Complement().ToString()));
            sql.Add($"'{{(,)}}'::nummultirange - {Constructor(mask)}");

            labels.Add(($"Complement twice {Name(mask)}", set.Complement().Complement().ToString()));
            sql.Add($"'{{(,)}}'::nummultirange - ('{{(,)}}'::nummultirange - {Constructor(mask)})");
        }

        await AssertAgrees(labels, sql);
    }

    /// <summary>
    /// The binary algebra over every ordered pair of subsets: union, difference and intersection
    /// against <c>+</c>, <c>-</c> and <c>*</c>.
    /// </summary>
    /// <remarks>
    /// Both operands are already canonical here, so what this sweeps is not the constructor's merge
    /// but the merge each operation has to perform on its own result — the sorted merge behind
    /// <c>Union</c>, the two-pointer merge-join behind <c>Except</c>, and the pairwise walk behind
    /// <c>Intersect</c>, each of which decides adjacency again on values it just produced. The
    /// difference of two open ranges is the case worth naming: PostgreSQL answers
    /// <c>{(,1],[5,5],[9,)}</c> for the whole line minus <c>(1,5)</c> and <c>(5,9)</c>, so a
    /// degenerate single-value element in the middle of a result is a value the model has to
    /// produce too, not an artefact.
    /// </remarks>
    [TestMethod]
    public async Task EveryOrderedPairOfSubsets_AgreesWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        var labels = new List<(string Label, string Model)>();
        var sql    = new List<string>();

        for (var leftMask = 0; leftMask < Subsets; leftMask++)
        {
            var left     = Build(leftMask);
            var leftSql  = Constructor(leftMask);
            var leftName = Name(leftMask);

            for (var rightMask = 0; rightMask < Subsets; rightMask++)
            {
                var right     = Build(rightMask);
                var rightSql  = Constructor(rightMask);
                var rightName = Name(rightMask);

                labels.Add(($"Union {leftName} ∪ {rightName}", left.Union(right).ToString()));
                sql.Add($"{leftSql} + {rightSql}");

                labels.Add(($"Except {leftName} ∖ {rightName}", left.Except(right).ToString()));
                sql.Add($"{leftSql} - {rightSql}");

                labels.Add(($"Intersect {leftName} ∩ {rightName}", left.Intersect(right).ToString()));
                sql.Add($"{leftSql} * {rightSql}");
            }
        }

        await AssertAgrees(labels, sql);
    }

    /// <summary>
    /// The one case in this file worth stating rather than sweeping: a difference that leaves a
    /// single value stranded between two intervals. Both implementations produce it, and it is the
    /// shape a merge that was slightly too eager would quietly swallow.
    /// </summary>
    [TestMethod]
    public async Task ADifferenceThatStrandsOneValue_ProducesADegenerateElement()
    {
        ContainerLifecycle.RequireDatabase();

        var whole = RangeSet<DecimalRange, decimal>.Infinite;
        var holes = RangeSet<DecimalRange, decimal>.From([
            DecimalRange.CreateFinite(1m, 5m, false, false),
            DecimalRange.CreateFinite(5m, 9m, false, false)
        ]);

        var difference = whole.Except(holes);

        Assert.AreEqual(3, difference.Count, "the value 5 survives between the two open holes");
        Assert.AreEqual("{(,1],[5,5],[9,)}", difference.ToString());

        var server = await EvaluateText([
            "'{(,)}'::nummultirange - nummultirange('(1,5)'::numrange, '(5,9)'::numrange)"
        ]);

        Assert.AreEqual("{(,1],[5,5],[9,)}", server[0]);
    }

    // -------------------------------------------------------------------------

    private static async Task AssertAgrees(List<(string Label, string Model)> labels, List<string> sql)
    {
        var server        = await EvaluateText(sql);
        var disagreements = new List<string>();

        for (var index = 0; index < labels.Count; index++)
        {
            var (label, model) = labels[index];
            var text = server[index];

            string asModel;
            try
            {
                asModel = RangeSet<DecimalRange, decimal>.Parse(text, CultureInfo.InvariantCulture).ToString();
            }
            catch (Exception exception)
            {
                asModel = $"<could not parse '{text}': {exception.GetType().Name}>";
            }

            if (asModel != model)
                disagreements.Add($"  {label}: model={model}, PostgreSQL={text} (as model: {asModel})");
        }

        Assert.AreEqual(
            0, disagreements.Count,
            $"{disagreements.Count} of {labels.Count} nummultirange results disagree with PostgreSQL:\n"
          + string.Join("\n", disagreements.Take(20))
          + (disagreements.Count > 20 ? $"\n  … and {disagreements.Count - 20} more" : ""));
    }

    /// <summary>Evaluates every expression as text, in batches, over one connection.</summary>
    private static async Task<List<string>> EvaluateText(List<string> expressions)
    {
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        var results = new List<string>();

        foreach (var batch in expressions.Chunk(200))
        {
            var projection = string.Join(", ", batch.Select((expression, index) => $"({expression})::text AS c{index}"));

            await using var command = new NpgsqlCommand($"SELECT {projection}", connection);
            await using var reader  = await command.ExecuteReaderAsync();
            await reader.ReadAsync();

            for (var index = 0; index < batch.Length; index++) results.Add(reader.GetString(index));
        }

        return results;
    }
}

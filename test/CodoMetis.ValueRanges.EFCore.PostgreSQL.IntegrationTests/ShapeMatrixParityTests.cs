using System.Globalization;
using CodoMetis.ValueRanges.Core;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Asks PostgreSQL for every binary predicate over every ordered pair of range shapes and
/// requires the in-memory implementation to give the same answer.
/// </summary>
/// <remarks>
/// <para>
/// The per-predicate tests elsewhere pick the shapes a human thought to try, which is how
/// <c>IsAdjacentTo</c> (6.2.1) and <c>IsStrictlyLeftOf</c> (7.0.0) both shipped answering
/// <see langword="false"/> for a whole family of receivers: each was written as a switch on the
/// receiver's shape whose inner switch handled the operand's, so the two directions could drift
/// apart without any single test noticing. A full matrix has no such blind spot — it asks every
/// combination in both orders, and the server decides.
/// </para>
/// <para>
/// The literals sent to PostgreSQL are the model's own <c>ToString</c> output, so a formatting
/// change that altered a value would move the server's answer with it rather than hiding.
/// </para>
/// </remarks>
[TestClass]
public sealed class ShapeMatrixParityTests
{
    private static string Literal(object range)
        => ((IFormattable)range).ToString(null, CultureInfo.InvariantCulture);

    /// <summary>
    /// The eight operators the range algebra mirrors, paired with the method that must agree
    /// with each. Both operand orders are generated, so a one-directional method is exercised
    /// from both sides.
    /// </summary>
    private static readonly (string Name, string Operator)[] Predicates =
    [
        ("Contains",             "@>"),
        ("IsContainedBy",        "<@"),
        ("Overlaps",             "&&"),
        ("IsStrictlyLeftOf",     "<<"),
        ("IsStrictlyRightOf",    ">>"),
        ("DoesNotExtendRightOf", "&<"),
        ("DoesNotExtendLeftOf",  "&>"),
        ("IsAdjacentTo",         "-|-")
    ];

    private static bool InMemory<TRange, T>(string predicate, TRange a, IRange<T> b)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => predicate switch
        {
            "Contains"             => a.Contains(b),
            "IsContainedBy"        => a.IsContainedBy(b),
            "Overlaps"             => a.Overlaps(b),
            "IsStrictlyLeftOf"     => a.IsStrictlyLeftOf(b),
            "IsStrictlyRightOf"    => a.IsStrictlyRightOf(b),
            "DoesNotExtendRightOf" => a.DoesNotExtendRightOf(b),
            "DoesNotExtendLeftOf"  => a.DoesNotExtendLeftOf(b),
            "IsAdjacentTo"         => a.IsAdjacentTo(b),
            _                      => throw new ArgumentOutOfRangeException(nameof(predicate))
        };

    private static async Task AssertMatrixAgrees<TRange, T>(
        string storeType,
        (string Name, TRange Range)[] shapes
    )
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var cases = new List<(string Label, bool Expected)>();
        var sql = new List<string>();

        foreach (var (leftName, left) in shapes)
        foreach (var (rightName, right) in shapes)
        foreach (var (predicate, @operator) in Predicates)
        {
            cases.Add(($"{predicate}: '{leftName}' vs '{rightName}'", InMemory<TRange, T>(predicate, left, right)));
            sql.Add($"('{Literal(left!)}'::{storeType} {@operator} '{Literal(right!)}'::{storeType})");
        }

        var server = await Evaluate(sql);

        var disagreements = cases
           .Select((c, index) => (c.Label, Model: c.Expected, Server: server[index]))
           .Where(row => row.Model != row.Server)
           .Select(row => $"{row.Label}: model={row.Model}, PostgreSQL={row.Server}")
           .ToList();

        Assert.AreEqual(
            0, disagreements.Count,
            $"{disagreements.Count} of {cases.Count} {storeType} comparisons disagree with PostgreSQL:\n"
          + string.Join("\n", disagreements));
    }

    /// <summary>Evaluates every boolean expression in one round trip, in batches.</summary>
    private static async Task<List<bool>> Evaluate(List<string> expressions)
    {
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        var results = new List<bool>();

        foreach (var batch in expressions.Chunk(400))
        {
            var projection = string.Join(", ", batch.Select((expression, index) => $"{expression} AS c{index}"));

            await using var command = new NpgsqlCommand($"SELECT {projection}", connection);
            await using var reader  = await command.ExecuteReaderAsync();
            await reader.ReadAsync();

            for (var index = 0; index < batch.Length; index++) results.Add(reader.GetBoolean(index));
        }

        return results;
    }

    /// <summary>
    /// A discrete domain, where the model's closed canonical form and PostgreSQL's half-open one
    /// have to be compared through the same literals.
    /// </summary>
    [TestMethod]
    public async Task Int32Range_EveryShapePair_AgreesWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        await AssertMatrixAgrees<Int32Range, int>("int4range",
        [
            ("empty",   Int32Range.Empty),
            ("[1,5]",   Int32Range.CreateFinite(1, 5)),
            ("[6,10]",  Int32Range.CreateFinite(6, 10)),   // discretely adjacent to [1,5]
            ("[3,8]",   Int32Range.CreateFinite(3, 8)),    // overlaps [1,5]
            ("[20,30]", Int32Range.CreateFinite(20, 30)),  // disjoint from everything finite above
            ("(,0]",    Int32Range.CreateUnboundedStart(0)),
            ("(,5]",    Int32Range.CreateUnboundedStart(5)),
            ("[6,)",    Int32Range.CreateUnboundedEnd(6)),
            ("[1,)",    Int32Range.CreateUnboundedEnd(1)),
            ("(,)",     Int32Range.Infinite)
        ]);
    }

    /// <summary>
    /// A continuous domain, where inclusivity rather than a step decides every boundary case —
    /// so the shapes here vary it on both sides of each meeting point.
    /// </summary>
    [TestMethod]
    public async Task DecimalRange_EveryShapePair_AgreesWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        await AssertMatrixAgrees<DecimalRange, decimal>("numrange",
        [
            ("empty", DecimalRange.Empty),
            ("[1,5)", DecimalRange.CreateFinite(1m, 5m)),
            ("[5,9)", DecimalRange.CreateFinite(5m, 9m)),
            ("(1,5]", DecimalRange.CreateFinite(1m, 5m, false, true)),
            ("[5,9]", DecimalRange.CreateFinite(5m, 9m, true, true)),
            ("(5,9)", DecimalRange.CreateFinite(5m, 9m, false, false)),
            ("[3,7)", DecimalRange.CreateFinite(3m, 7m)),
            ("(,5)",  DecimalRange.CreateUnboundedStart(5m, false)),
            ("(,5]",  DecimalRange.CreateUnboundedStart(5m, true)),
            ("[5,)",  DecimalRange.CreateUnboundedEnd(5m)),
            ("(5,)",  DecimalRange.CreateUnboundedEnd(5m, false)),
            ("(,)",   DecimalRange.Infinite)
        ]);
    }

    /// <summary>
    /// A date domain, which is discrete like <see cref="int"/> but canonicalizes through a
    /// different step and renders through a different literal form.
    /// </summary>
    [TestMethod]
    public async Task DateRange_EveryShapePair_AgreesWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        DateOnly D(int month, int day) => new(2024, month, day);

        await AssertMatrixAgrees<DateRange, DateOnly>("daterange",
        [
            ("empty",       DateRange.Empty),
            ("[Jan,Mar]",   DateRange.CreateFinite(D(1, 1), D(3, 31))),
            ("[Apr,Jun]",   DateRange.CreateFinite(D(4, 1), D(6, 30))),  // adjacent by one day
            ("[Mar,May]",   DateRange.CreateFinite(D(3, 1), D(5, 31))),  // overlapping
            ("[Nov,Dec]",   DateRange.CreateFinite(D(11, 1), D(12, 31))),
            ("(,Mar]",      DateRange.CreateUnboundedStart(D(3, 31))),
            ("[Apr,)",      DateRange.CreateUnboundedEnd(D(4, 1))),
            ("(,)",         DateRange.Infinite)
        ]);
    }

    /// <summary>
    /// The empty range is contained by every range and every set, itself included — ∅ ⊆ S holds
    /// vacuously, and it is what PostgreSQL's <c>@&gt;</c> answers. Asserted here across both
    /// containment overloads and both container kinds, because the model answered
    /// <see langword="false"/> for the single-range case until 7.0.0.
    /// </summary>
    /// <remarks>
    /// <c>RangeSet.Contains(RangeSet)</c> already answered <see langword="true"/>, by iterating
    /// zero elements — and since <c>From</c> drops empty elements, <c>RangeSet.Empty</c> and
    /// <c>Int32Range.Empty</c> are each other's normalized form. So the model was answering the
    /// same question two ways before this was corrected, which is why the multirange half of this
    /// test passes with or without the fix.
    /// </remarks>
    [TestMethod]
    public async Task EmptyRange_IsContainedByEverything_AsInPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        var finite = Int32Range.CreateFinite(1, 5);
        var set    = RangeSet<Int32Range, int>.From([finite]);

        Assert.IsTrue(finite.Contains(Int32Range.Empty));
        Assert.IsTrue(Int32Range.Empty.IsContainedBy(finite));
        Assert.IsTrue(Int32Range.Infinite.Contains(Int32Range.Empty));
        Assert.IsTrue(Int32Range.Empty.Contains(Int32Range.Empty), "∅ ⊆ ∅");
        Assert.IsTrue(set.Contains(Int32Range.Empty));
        Assert.IsTrue(set.Contains(RangeSet<Int32Range, int>.Empty));
        Assert.IsTrue(RangeSet<Int32Range, int>.Empty.Contains(Int32Range.Empty),
            "the empty set contains the empty range, as From makes them the same value");

        // …while the converse still fails: ∅ contains nothing non-empty.
        Assert.IsFalse(Int32Range.Empty.Contains(finite));
        Assert.IsFalse(RangeSet<Int32Range, int>.Empty.Contains(finite));

        var server = await Evaluate(
        [
            "('[1,5]'::int4range @> 'empty'::int4range)",
            "('empty'::int4range <@ '[1,5]'::int4range)",
            "('(,)'::int4range @> 'empty'::int4range)",
            "('empty'::int4range @> 'empty'::int4range)",
            "('{[1,6)}'::int4multirange @> 'empty'::int4range)",
            "('{}'::int4multirange @> 'empty'::int4range)",
            "('empty'::int4range @> '[1,5]'::int4range)",
            "('{}'::int4multirange @> '[1,5]'::int4range)"
        ]);

        CollectionAssert.AreEqual(new[] { true, true, true, true, true, true, false, false }, server);
    }
}

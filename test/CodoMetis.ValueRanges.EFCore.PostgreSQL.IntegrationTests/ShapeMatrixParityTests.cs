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
/// <c>IsAdjacentTo</c> (6.2.1) and <c>IsStrictlyLeftOf</c> (6.4.0) both shipped answering
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

    /// <summary>
    /// The one rule on which the model and PostgreSQL deliberately disagree: PostgreSQL applies
    /// "the empty set is a subset of every set", so <c>x @&gt; 'empty'</c> is true for every
    /// <c>x</c>, while the model answers <see langword="false"/> — as its
    /// <c>Contains</c>/<c>IsContainedBy</c> documentation states. Excluded here and asserted
    /// separately below, so the divergence stays a decision on record rather than a gap.
    /// </summary>
    private static bool IsEmptySubsetRule<T>(string predicate, IRange<T> a, IRange<T> b)
        where T : struct, IComparable<T>, IEquatable<T>
        => (predicate is "Contains" && b.IsEmpty())
        || (predicate is "IsContainedBy" && a.IsEmpty());

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
            if (IsEmptySubsetRule<T>(predicate, left, right)) continue;

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
    /// The empty-operand containment rule the matrix above excludes, asserted directly in both
    /// worlds so the divergence is a recorded decision. PostgreSQL treats the empty range as a
    /// subset of everything; the model does not, and says so in the
    /// <c>Contains</c>/<c>IsContainedBy</c> documentation.
    /// </summary>
    /// <remarks>
    /// This is the one place where <c>Contains</c> evaluated in memory and the <c>@&gt;</c> it
    /// translates to answer differently. It matters only for an explicitly empty operand, which
    /// is why it has survived: <c>x.Contains(SomeRange.Empty)</c> is a question nobody asks on
    /// purpose. Anyone who does ask it should know the two sides differ.
    /// </remarks>
    [TestMethod]
    public async Task EmptyOperandContainment_DivergesFromPostgres_Deliberately()
    {
        ContainerLifecycle.RequireDatabase();

        var finite = Int32Range.CreateFinite(1, 5);

        Assert.IsFalse(finite.Contains(Int32Range.Empty), "the model does not treat empty as a subset");
        Assert.IsFalse(Int32Range.Empty.IsContainedBy(finite), "…in either direction");
        Assert.IsFalse(Int32Range.Infinite.Contains(Int32Range.Empty), "…not even the infinite range");

        var server = await Evaluate(
        [
            "('[1,5]'::int4range @> 'empty'::int4range)",
            "('empty'::int4range <@ '[1,5]'::int4range)",
            "('(,)'::int4range @> 'empty'::int4range)"
        ]);

        CollectionAssert.AreEqual(
            new[] { true, true, true }, server,
            "PostgreSQL is expected to apply the empty-subset rule; if this changed, the "
          + "exclusion in the shape matrix above is no longer describing a real divergence.");
    }
}

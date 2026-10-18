using System.Reflection;
using CodoMetis.ValueRanges.Conventions.Tests;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Asks PostgreSQL to order every range and multirange type the packages ship, and requires
/// <c>CompareTo</c> to give the same answer: for every ordered pair of shapes (<c>&lt;</c>, <c>=</c>,
/// <c>&gt;</c>) and for the whole list at once (<c>ORDER BY</c>).
/// </summary>
/// <remarks>
/// <para>
/// The range order is permanent once it ships: a sorted collection treats <c>CompareTo == 0</c> as a
/// duplicate key, and a query's <c>ORDER BY</c> and the same sort in memory must not disagree. So the
/// server decides every case, the way <see cref="ShapeMatrixParityTests"/> decides the relations. The
/// shapes are built from two values per element type and vary inclusivity on both sides of each,
/// because the bound comparison at a shared value is the one place the order is not the element's.
/// </para>
/// <para>
/// The literals are the type mapping's own SQL literals, so a type whose model value is not its
/// literal text (<c>YearMonthRange</c>, stored as a month-aligned <c>daterange</c>) is ordered as the
/// column would be. Types are discovered, so a new range type is ordered with no edit here.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeOrderParityTests
{
    private static IRelationalTypeMappingSource Mappings { get; } =
        new IntegrationDbContext().GetService<IRelationalTypeMappingSource>();

    [TestMethod]
    public async Task EveryRangeAndMultirange_OrdersAsPostgres()
    {
        ContainerLifecycle.RequireDatabase();
        Assert.IsGreaterThanOrEqualTo(11, FuzzTypes.Ranges.Count, "range type discovery found too few types");

        var method = typeof(RangeOrderParityTests).GetMethod(nameof(Sweep), BindingFlags.NonPublic | BindingFlags.Static)!;
        var lines  = new List<string>();

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();

        foreach (var (range, element) in FuzzTypes.Ranges)
            lines.AddRange(await (Task<List<string>>)method.MakeGenericMethod(range, element).Invoke(null, [connection])!);

        Candidates.Verdict(nameof(RangeOrderParityTests), lines,
            "CompareTo orders ranges or multiranges differently from PostgreSQL");
    }

    /// <summary>
    /// <c>CompareTo</c> in a predicate runs on the server as <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>,
    /// <c>&gt;=</c> and <c>=</c> (EF Core rewrites <c>a.CompareTo(b) &lt; 0</c> to <c>a &lt; b</c>), and
    /// <c>OrderBy</c> on the column sorts as <c>CompareTo</c> does, for a range and a multirange column.
    /// </summary>
    [TestMethod]
    public async Task CompareToInAQuery_ReturnsTheRowsMemoryReturns()
    {
        ContainerLifecycle.RequireDatabase();

        Int32Range[] seats =
        [
            Int32Range.Empty, Int32Range.Infinite, Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(1, 9),
            Int32Range.CreateFinite(2, 3), Int32Range.CreateUnboundedStart(4), Int32Range.CreateUnboundedEnd(1)
        ];
        var rows = seats.Select((range, index) => new Reservation
        {
            Id         = 39001 + index,
            Seats      = range,
            SeatBlocks = RangeSet<Int32Range, int>.From([range, Int32Range.CreateFinite(20 + index, 30)])
        }).ToList();

        await using (var seed = new IntegrationDbContext())
        {
            seed.Reservations.AddRange(rows);
            await seed.SaveChangesAsync();
        }

        var probe    = Int32Range.CreateFinite(1, 5);
        var probeSet = RangeSet<Int32Range, int>.From([probe, Int32Range.CreateFinite(25, 30)]);

        await using var context = new IntegrationDbContext();
        var mine = context.Reservations.Where(r => r.Id >= 39001 && r.Id <= 39000 + seats.Length);

        async Task Agree(string label, System.Linq.Expressions.Expression<Func<Reservation, bool>> predicate)
        {
            var server = await mine.Where(predicate).Select(r => r.Id).OrderBy(id => id).ToListAsync();
            var memory = rows.Where(predicate.Compile()).Select(r => r.Id).OrderBy(id => id).ToList();
            CollectionAssert.AreEqual(memory, server, label);
        }

        await Agree("<",  r => r.Seats.CompareTo(probe) < 0);
        await Agree("<=", r => r.Seats.CompareTo(probe) <= 0);
        await Agree(">",  r => r.Seats.CompareTo(probe) > 0);
        await Agree(">=", r => r.Seats.CompareTo(probe) >= 0);
        await Agree("==", r => r.Seats.CompareTo(probe) == 0);
        await Agree("multirange <", r => r.SeatBlocks.CompareTo(probeSet) < 0);
        await Agree("multirange >=", r => r.SeatBlocks.CompareTo(probeSet) >= 0);

        // A null operand: null sorts before every range, so > 0, >= 0 and != 0 hold for every row and
        // the other three for none. SQL's "col > NULL" is NULL, which returned no rows for any (#220).
        Int32Range? none = null;
        RangeSet<Int32Range, int>? noSet = null;

        await Agree("null <",  r => r.Seats.CompareTo(none) < 0);
        await Agree("null <=", r => r.Seats.CompareTo(none) <= 0);
        await Agree("null >",  r => r.Seats.CompareTo(none) > 0);
        await Agree("null >=", r => r.Seats.CompareTo(none) >= 0);
        await Agree("null ==", r => r.Seats.CompareTo(none) == 0);
        await Agree("null !=", r => r.Seats.CompareTo(none) != 0);
        await Agree("null, 0 on the left: 0 < CompareTo", r => 0 < r.Seats.CompareTo(none));
        await Agree("null, 0 on the left: 0 >= CompareTo", r => 0 >= r.Seats.CompareTo(none));
        await Agree("null, negated: !(CompareTo < 0)", r => !(r.Seats.CompareTo(none) < 0));
        await Agree("null multirange >", r => r.SeatBlocks.CompareTo(noSet) > 0);
        await Agree("null multirange <=", r => r.SeatBlocks.CompareTo(noSet) <= 0);
        Assert.HasCount(seats.Length, await mine.Where(r => r.Seats.CompareTo(none) > 0).ToListAsync(),
                        "every row sorts after a null range");

        // …and a non-null operand still answers as before, through the same rewrite.
        await Agree("non-null >= after the rewrite", r => r.Seats.CompareTo(probe) >= 0 && r.SeatBlocks.CompareTo(probeSet) != 0);

        CollectionAssert.AreEqual(
            rows.OrderBy(r => r.Seats).Select(r => r.Id).ToList(),
            await mine.OrderBy(r => r.Seats).Select(r => r.Id).ToListAsync(),
            "ORDER BY a range column");
        CollectionAssert.AreEqual(
            rows.OrderBy(r => r.SeatBlocks).Select(r => r.Id).ToList(),
            await mine.OrderBy(r => r.SeatBlocks).Select(r => r.Id).ToListAsync(),
            "ORDER BY a multirange column");
    }

    private static async Task<List<string>> Sweep<TRange, T>(NpgsqlConnection connection)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (lo, hi) = ((T)RangeProbes.Bounds[typeof(T)].Low, (T)RangeProbes.Bounds[typeof(T)].High);

        (string Name, TRange Value)[] ranges =
        [
            ("empty",   TRange.Empty),
            ("(,)",     TRange.Infinite),
            ("[lo,hi]", TRange.CreateFinite(lo, hi, true, true)),
            ("[lo,hi)", TRange.CreateFinite(lo, hi, true, false)),
            ("(lo,hi]", TRange.CreateFinite(lo, hi, false, true)),
            ("(lo,hi)", TRange.CreateFinite(lo, hi, false, false)),
            ("[lo,lo]", TRange.CreateFinite(lo, lo, true, true)),
            ("[hi,hi]", TRange.CreateFinite(hi, hi, true, true)),
            ("(,lo]",   TRange.CreateUnboundedStart(lo, true)),
            ("(,lo)",   TRange.CreateUnboundedStart(lo, false)),
            ("(,hi]",   TRange.CreateUnboundedStart(hi, true)),
            ("(,hi)",   TRange.CreateUnboundedStart(hi, false)),
            ("[lo,)",   TRange.CreateUnboundedEnd(lo, true)),
            ("(lo,)",   TRange.CreateUnboundedEnd(lo, false)),
            ("[hi,)",   TRange.CreateUnboundedEnd(hi, true)),
            ("(hi,)",   TRange.CreateUnboundedEnd(hi, false))
        ];

        // Every non-empty range alone, and two-element sets that share a first element with a
        // singleton, so the element-by-element comparison and the shorter-set-first rule both decide.
        var sets = new List<(string Name, RangeSet<TRange, T> Value)> { ("{}", RangeSet<TRange, T>.Empty) };
        sets.AddRange(ranges.Where(r => !r.Value.IsEmpty()).Select(r => ($"{{{r.Name}}}", RangeSet<TRange, T>.From([r.Value]))));
        sets.Add(("{[lo,lo],[hi,hi]}", RangeSet<TRange, T>.From([ranges[6].Value, ranges[7].Value])));
        sets.Add(("{(,lo),(hi,)}",     RangeSet<TRange, T>.From([ranges[9].Value, ranges[15].Value])));
        sets.Add(("{(,lo),[hi,hi]}",   RangeSet<TRange, T>.From([ranges[9].Value, ranges[7].Value])));
        sets.Add(("{[lo,lo],(hi,)}",   RangeSet<TRange, T>.From([ranges[6].Value, ranges[15].Value])));

        var failures = new List<string>();
        failures.AddRange(await Compare(connection, typeof(TRange).Name, ranges));
        failures.AddRange(await Compare(connection, $"RangeSet<{typeof(TRange).Name}>", [.. sets]));
        return failures;
    }

    private static async Task<List<string>> Compare<TValue>(NpgsqlConnection connection, string label, (string Name, TValue Value)[] values)
        where TValue : IComparable<TValue>
    {
        var mapping  = Mappings.FindMapping(typeof(TValue)) ?? throw new InvalidOperationException($"No mapping for {typeof(TValue)}.");
        var literals = values.Select(v => mapping.GenerateSqlLiteral(v.Value)).ToArray();
        var failures = new List<string>();

        // Every ordered pair, both directions, as the sign PostgreSQL's <, = and > give.
        var pairs = (from i in Enumerable.Range(0, values.Length)
                     from j in Enumerable.Range(0, values.Length)
                     select (i, j)).ToList();

        var signs = new List<int>();
        foreach (var batch in pairs.Chunk(400))
        {
            var projection = string.Join(", ", batch.Select((p, k) =>
                $"CASE WHEN {literals[p.i]} < {literals[p.j]} THEN -1 WHEN {literals[p.i]} = {literals[p.j]} THEN 0 "
              + $"WHEN {literals[p.i]} > {literals[p.j]} THEN 1 END AS c{k}"));

            await using var command = new NpgsqlCommand($"SELECT {projection}", connection);
            await using var reader  = await command.ExecuteReaderAsync();
            await reader.ReadAsync();
            for (var k = 0; k < batch.Length; k++) signs.Add(reader.GetInt32(k));
        }

        for (var index = 0; index < pairs.Count; index++)
        {
            var (i, j) = pairs[index];
            var (left, right) = (values[i].Value, values[j].Value);
            int model = Math.Sign(left.CompareTo(right));

            if (model != signs[index])
                failures.Add($"{label} '{values[i].Name}' CompareTo '{values[j].Name}': model={model}, PostgreSQL={signs[index]}");

            if (left.Equals(right) != (signs[index] == 0))
                failures.Add($"{label} '{values[i].Name}' Equals '{values[j].Name}': model={left.Equals(right)}, PostgreSQL ={signs[index] == 0}");
        }

        // The whole list at once, as ORDER BY sorts it, ties broken by position on both sides.
        var array = $"ARRAY[{string.Join(", ", literals)}]";
        await using (var command = new NpgsqlCommand(
                         $"SELECT ord FROM unnest({array}) WITH ORDINALITY AS t(v, ord) ORDER BY v, ord", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            var server = new List<int>();
            while (await reader.ReadAsync()) server.Add((int)reader.GetInt64(0) - 1);

            var model = Enumerable.Range(0, values.Length)
                                  .OrderBy(i => values[i].Value)
                                  .ThenBy(i => i)
                                  .ToList();

            if (!server.SequenceEqual(model))
                failures.Add($"{label} ORDER BY: model={string.Join(" ", model.Select(i => values[i].Name))}, "
                           + $"PostgreSQL={string.Join(" ", server.Select(i => values[i].Name))}");
        }

        return failures;
    }
}

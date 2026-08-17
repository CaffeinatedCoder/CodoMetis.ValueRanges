using System.Globalization;
using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// Asks PostgreSQL for every translated set operator over every ordered pair of subsets of a small
/// probe universe, and requires the in-memory implementation to give the same answer — the value
/// set counterpart of <see cref="ShapeMatrixParityTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// The sets had two defences and a hole between them. <c>SmallModelSetOracleTests</c> sweeps every
/// subset pair against set theory, but entirely in memory: it is the oracle for the client, and it
/// has nothing to say about what the server answers. <see cref="SetIntegrationTests"/> runs the real
/// operators against a real database, but over one hand-picked pair of sets. Nothing asked
/// PostgreSQL to confirm <c>@&gt;</c>, <c>&lt;@</c>, <c>&amp;&amp;</c> and <c>cardinality</c> over
/// the whole value space, which is exactly the check that earned its keep on the range side — the
/// shape matrix found three bugs the per-predicate tests had each looked straight past.
/// </para>
/// <para>
/// That the set translations are simpler than the range ones is an argument for expecting this to
/// pass, not evidence that it does. It is also not static: the operators compose (the proper
/// subset/superset pair is <c>&lt;@ AND NOT @&gt;</c>, assembled by this package rather than
/// provided by PostgreSQL), and 8.0.0 changed which compositions are permitted at all.
/// </para>
/// <para>
/// The sweep runs through EF rather than raw SQL, so the translation is inside the loop and not
/// assumed: a correct operator reached by a wrong translation fails here.
/// <see cref="AssertSweepReachesTheServer"/> keeps that honest — EF answers a projection it cannot translate by evaluating it client-side,
/// which would turn every row of this sweep into the in-memory implementation agreeing with itself,
/// so one query's SQL is asserted to carry the actual operators.
/// </para>
/// <para>
/// Three families, chosen for what each can break that the others cannot: <see cref="StringSet"/>
/// for <c>text[]</c>, where the client's ordinal canonical order and the database's collation
/// disagree (the probes are picked so the two orders differ, which array equality would see);
/// <see cref="Int64Set"/> for the element types whose literals PostgreSQL does not coerce; and
/// <c>StringSet&lt;TestKey&gt;</c> for the wrapper arities, which reach the store through a text
/// bridge rather than natively.
/// </para>
/// </remarks>
[TestClass]
public sealed class SetOperatorParityTests
{
    /// <summary>Four probes give sixteen subsets and 256 ordered pairs per family.</summary>
    private const int UniverseSize = 4;

    private const int Subsets = 1 << UniverseSize;

    /// <summary>
    /// One row of the sweep: every translated member of the set surface, evaluated on the server
    /// for one stored subset against one probe subset.
    /// </summary>
    private sealed class ParityRow
    {
        public int Id { get; set; }

        public bool IsSupersetOf { get; set; }

        public bool IsSubsetOf { get; set; }

        public bool Overlaps { get; set; }

        public bool IsProperSupersetOf { get; set; }

        public bool IsProperSubsetOf { get; set; }

        public bool Equal { get; set; }

        public int Count { get; set; }

        public bool IsEmpty { get; set; }

        public bool Contains0 { get; set; }

        public bool Contains1 { get; set; }

        public bool Contains2 { get; set; }

        public bool Contains3 { get; set; }

        // The three compositions on Union that 8.0.0 kept legal. Union translates to array_cat,
        // which concatenates without canonicalizing, so only order- and multiplicity-insensitive
        // operators may read its result — these are them, and this is what proves the claim
        // against a server rather than restating it.
        public bool UnionIsSupersetOfProbe { get; set; }

        public bool UnionIsSubsetOfProbe { get; set; }

        public bool UnionOverlapsProbe { get; set; }
    }

    [TestMethod]
    public async Task StringSet_EverySubsetPair_AgreesWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        // Ordinal order is Banana < Zebra < apple < zebra; a culture-aware collation interleaves
        // the cases instead. The operators below are order-insensitive by construction, but array
        // equality is not — it compares element by element — so a stored array that had been
        // ordered by the database rather than by the client's canonical comparer would fail the
        // Equal column for the pairs where the two orders differ.
        string[] universe = ["Zebra", "apple", "Banana", "zebra"];

        await SweepEverySubsetPair<StringSet, string>(
            "StringSet", 8200, universe,
            static (row, set) => row.Tags = set,
            (rows, probe) => rows.Select(r => new ParityRow
            {
                Id                     = r.Id,
                IsSupersetOf           = r.Tags.IsSupersetOf(probe),
                IsSubsetOf             = r.Tags.IsSubsetOf(probe),
                Overlaps               = r.Tags.Overlaps(probe),
                IsProperSupersetOf     = r.Tags.IsProperSupersetOf(probe),
                IsProperSubsetOf       = r.Tags.IsProperSubsetOf(probe),
                Equal                  = r.Tags == probe,
                Count                  = r.Tags.Count,
                IsEmpty                = r.Tags.IsEmpty,
                Contains0              = r.Tags.Contains(universe[0]),
                Contains1              = r.Tags.Contains(universe[1]),
                Contains2              = r.Tags.Contains(universe[2]),
                Contains3              = r.Tags.Contains(universe[3]),
                UnionIsSupersetOfProbe = r.Tags.Union(probe).IsSupersetOf(probe),
                UnionIsSubsetOfProbe   = r.Tags.Union(probe).IsSubsetOf(probe),
                UnionOverlapsProbe     = r.Tags.Union(probe).Overlaps(probe)
            }));
    }

    [TestMethod]
    public async Task Int64Set_EverySubsetPair_AgreesWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        // Two of these are outside int, and one is outside the exactly representable range of a
        // double — the element type whose constant operand PostgreSQL refuses to coerce, and the
        // one where a value routed through a floating-point conversion comes back changed.
        long[] universe = [1L, 5L, 9_007_199_254_740_993L, long.MaxValue];

        await SweepEverySubsetPair<Int64Set, long>(
            "Int64Set", 8300, universe,
            static (row, set) => row.BigCodes = set,
            (rows, probe) => rows.Select(r => new ParityRow
            {
                Id                     = r.Id,
                IsSupersetOf           = r.BigCodes.IsSupersetOf(probe),
                IsSubsetOf             = r.BigCodes.IsSubsetOf(probe),
                Overlaps               = r.BigCodes.Overlaps(probe),
                IsProperSupersetOf     = r.BigCodes.IsProperSupersetOf(probe),
                IsProperSubsetOf       = r.BigCodes.IsProperSubsetOf(probe),
                Equal                  = r.BigCodes == probe,
                Count                  = r.BigCodes.Count,
                IsEmpty                = r.BigCodes.IsEmpty,
                Contains0              = r.BigCodes.Contains(universe[0]),
                Contains1              = r.BigCodes.Contains(universe[1]),
                Contains2              = r.BigCodes.Contains(universe[2]),
                Contains3              = r.BigCodes.Contains(universe[3]),
                UnionIsSupersetOfProbe = r.BigCodes.Union(probe).IsSupersetOf(probe),
                UnionIsSubsetOfProbe   = r.BigCodes.Union(probe).IsSubsetOf(probe),
                UnionOverlapsProbe     = r.BigCodes.Union(probe).Overlaps(probe)
            }));
    }

    [TestMethod]
    public async Task WrapperSet_EverySubsetPair_AgreesWithPostgres()
    {
        ContainerLifecycle.RequireDatabase();

        // TestKey lower-cases in Parse, so 'Admin.All' and 'admin.all' are one element: the
        // universe stays four distinct keys, and the normalization is on the path to the column.
        TestKey[] universe =
        [
            Key("users.read"), Key("users.write"), Key("Admin.All"), Key("billing.view")
        ];

        await SweepEverySubsetPair<StringSet<TestKey>, TestKey>(
            "StringSet<TestKey>", 8400, universe,
            static (row, set) => row.Permissions = set,
            (rows, probe) => rows.Select(r => new ParityRow
            {
                Id                     = r.Id,
                IsSupersetOf           = r.Permissions.IsSupersetOf(probe),
                IsSubsetOf             = r.Permissions.IsSubsetOf(probe),
                Overlaps               = r.Permissions.Overlaps(probe),
                IsProperSupersetOf     = r.Permissions.IsProperSupersetOf(probe),
                IsProperSubsetOf       = r.Permissions.IsProperSubsetOf(probe),
                Equal                  = r.Permissions == probe,
                Count                  = r.Permissions.Count,
                IsEmpty                = r.Permissions.IsEmpty,
                Contains0              = r.Permissions.Contains(universe[0]),
                Contains1              = r.Permissions.Contains(universe[1]),
                Contains2              = r.Permissions.Contains(universe[2]),
                Contains3              = r.Permissions.Contains(universe[3]),
                UnionIsSupersetOfProbe = r.Permissions.Union(probe).IsSupersetOf(probe),
                UnionIsSubsetOfProbe   = r.Permissions.Union(probe).IsSubsetOf(probe),
                UnionOverlapsProbe     = r.Permissions.Union(probe).Overlaps(probe)
            }));

        static TestKey Key(string value) => TestKey.Parse(value, CultureInfo.InvariantCulture);
    }

    // -------------------------------------------------------------------------
    // The sweep
    // -------------------------------------------------------------------------

    /// <summary>
    /// Seeds one row per subset of <paramref name="universe"/>, then asks the server for every
    /// operator of every row against every subset used as the probe, and compares each answer with
    /// the in-memory one.
    /// </summary>
    private static async Task SweepEverySubsetPair<TSet, TElement>(
        string                                                  family,
        int                                                     idBlock,
        TElement[]                                              universe,
        Action<Reservation, TSet>                               assign,
        Func<IQueryable<Reservation>, TSet, IQueryable<ParityRow>> project
    )
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        Assert.AreEqual(UniverseSize, universe.Length, $"{family}: the probe universe is fixed at four elements");

        var subsets = Enumerable.Range(0, Subsets).Select(Build).ToArray();

        await using (var seed = new IntegrationDbContext())
        {
            for (var mask = 0; mask < Subsets; mask++)
            {
                var row = new Reservation { Id = idBlock + mask };
                assign(row, subsets[mask]);
                seed.Reservations.Add(row);
            }

            await seed.SaveChangesAsync();
        }

        await using var context = new IntegrationDbContext();

        var stored = context.Reservations
                            .Where(r => r.Id >= idBlock && r.Id < idBlock + Subsets)
                            .OrderBy(r => r.Id);

        AssertSweepReachesTheServer(family, project(stored, subsets[0b0101]));

        var disagreements = new List<string>();

        for (var probeMask = 0; probeMask < Subsets; probeMask++)
        {
            var probe = subsets[probeMask];
            var rows  = await project(stored, probe).ToListAsync();

            Assert.AreEqual(Subsets, rows.Count, $"{family}: the sweep read {rows.Count} of {Subsets} rows");

            foreach (var row in rows)
            {
                var leftMask = row.Id - idBlock;
                var left     = subsets[leftMask];

                Check("IsSupersetOf",       row.IsSupersetOf,       left.IsSupersetOf(probe));
                Check("IsSubsetOf",         row.IsSubsetOf,         left.IsSubsetOf(probe));
                Check("Overlaps",           row.Overlaps,           left.Overlaps(probe));
                Check("IsProperSupersetOf", row.IsProperSupersetOf, left.IsProperSupersetOf(probe));
                Check("IsProperSubsetOf",   row.IsProperSubsetOf,   left.IsProperSubsetOf(probe));
                Check("==",                 row.Equal,              left.Equals(probe));
                Check("IsEmpty",            row.IsEmpty,            left.Values.IsEmpty);

                Check("Union.IsSupersetOf", row.UnionIsSupersetOfProbe, left.Union(probe).IsSupersetOf(probe));
                Check("Union.IsSubsetOf",   row.UnionIsSubsetOfProbe,   left.Union(probe).IsSubsetOf(probe));
                Check("Union.Overlaps",     row.UnionOverlapsProbe,     left.Union(probe).Overlaps(probe));

                bool[] contains = [row.Contains0, row.Contains1, row.Contains2, row.Contains3];
                for (var index = 0; index < UniverseSize; index++)
                    Check($"Contains({universe[index]})", contains[index], left.Contains(universe[index]));

                if (row.Count != left.Values.Length)
                {
                    disagreements.Add(
                        $"  cardinality: {Name(leftMask)} = {row.Count}, in memory {left.Values.Length}");
                }

                void Check(string operation, bool fromServer, bool fromModel)
                {
                    if (fromServer != fromModel)
                    {
                        disagreements.Add(
                            $"  {operation}: {Name(leftMask)} vs {Name(probeMask)} — "
                          + $"PostgreSQL={fromServer}, in memory={fromModel}");
                    }
                }
            }
        }

        Assert.AreEqual(
            0, disagreements.Count,
            $"{disagreements.Count} {family} answers disagree with PostgreSQL:\n"
          + string.Join("\n", disagreements.Take(20))
          + (disagreements.Count > 20 ? $"\n  … and {disagreements.Count - 20} more" : ""));

        TSet Build(int mask) => TSet.From(Indices(mask).Select(index => universe[index]));

        string Name(int mask) =>
            mask == 0 ? "{}" : $"{{{string.Join(", ", Indices(mask).Select(index => universe[index]))}}}";

        static IEnumerable<int> Indices(int mask) =>
            Enumerable.Range(0, UniverseSize).Where(index => (mask & (1 << index)) != 0);
    }

    /// <summary>
    /// The operators this sweep compares must be in the SQL. EF answers a projection it cannot
    /// translate by materializing the row and evaluating client-side, which produces the right
    /// answer for every case here — the in-memory implementation agreeing with itself, 4,000 times,
    /// green.
    /// </summary>
    private static void AssertSweepReachesTheServer(string family, IQueryable<ParityRow> query)
    {
        var      sql       = query.ToQueryString();
        string[] operators = ["@>", "<@", "&&", "cardinality(", "array_cat("];

        foreach (var fragment in operators)
        {
            StringAssert.Contains(
                sql, fragment,
                $"{family}: the sweep's projection does not send '{fragment}' to the server, so those "
              + "columns are being evaluated client-side and compare the model against itself:\n" + sql);
        }
    }
}

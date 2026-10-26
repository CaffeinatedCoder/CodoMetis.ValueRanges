using System.Linq.Expressions;
using CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Composition;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// A value set <c>Union</c> or <c>Remove</c> with a NULL operand is NULL in a projection, as its filter
/// <c>== null</c> says it is (#360), and so are <c>Add</c>, <c>Intersect</c> and <c>Except</c> where they
/// translate (#478).
/// </summary>
/// <remarks>
/// <para>
/// <c>array_cat</c> and <c>array_remove</c> are not strict: <c>array_cat(NULL, '{z}')</c> and
/// <c>array_remove('{a}', NULL)</c> are <c>{z}</c> and <c>{a}</c>. Declared as propagating nullability,
/// they made <c>Where(v == null)</c> select a row whose <c>Select(v)</c> loaded the other operand. The
/// operands here are NULL in every way a query reaches one: a nullable set column, a nullable element
/// column, a captured <see langword="null"/>, and the child of an outer join on either side. Each value
/// runs through <see cref="CompositionRunner.Judge"/>, so its two filters and its projection agree with
/// each other and with rule (c), and so does each predicate built on one.
/// </para>
/// <para>
/// The composition sweeps (<c>OuterJoinProbeTests</c>, <c>NullableOperandPairProbeTests</c>,
/// <c>DeclaredNullabilityTests</c>, <c>RepeatedExecutionProbeTests</c>) found it on <c>main</c> and hold it
/// there too. This class is what holds it on a line without them.
/// </para>
/// </remarks>
[TestClass]
public sealed class ValueSetNullOperandParityTests
{
    private static readonly StringSet[] Sets =
    [
        StringSet.From(["a", "b"]), StringSet.Empty, StringSet.From(["a"]), StringSet.From(["b", "c"]), StringSet.From(["z"])
    ];

    private static readonly string[] Labels = ["a", "z", "b"];

    // The canonical operations translate for every family but text (#478): Int32Set stands for them.
    private static readonly Int32Set[] CodeSets =
    [
        Int32Set.From([1, 2]), Int32Set.Empty, Int32Set.From([1]), Int32Set.From([2, 3]), Int32Set.From([9])
    ];

    private static readonly int[] Numbers = [1, 9, 2];

    [TestMethod]
    public async Task SetOperations_OverANullOperand_AreNullInAProjectionAsInAFilter()
    {
        ContainerLifecycle.RequireServer();
        var connectionString = await Seed();

        await using var context = new NullOperandContext(connectionString);
        var loaded = await context.Rows.Include(r => r.Child).AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.IsTrue(loaded.Any(r => r.MaybeTags is null) && loaded.Any(r => r.Label is null) && loaded.Any(r => r.Child is null),
                      "Some rows must hold a NULL set and a NULL element and have no child, or no operand is ever NULL.");

        var failures = new List<string>();
        foreach (var (name, lambda) in Calls())
        {
            var outcome = await CompositionRunner.Judge(
                "null operand", new Call(name, lambda.Body), lambda.Parameters[0], context.Rows.AsNoTracking(), loaded,
                r => r.Id, id => $"{id} ({Describe(loaded.Single(r => r.Id == id))})", failures);

            if (outcome != Outcome.Compared) failures.Add($"{name}: {outcome}, where it must run as SQL and be compared");
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The NULL test covers only the operands that can be NULL: a required column and a constant keep the
    /// bare call, so their SQL is what it was before #360, and a predicate over it gets no
    /// <c>IS NOT NULL</c> of its own.
    /// </summary>
    [TestMethod]
    public void UnionAndRemove_TestOnlyTheOperandsThatCanBeNull()
    {
        using var context = new NullOperandContext("Host=localhost;Database=unused");
        var rows = context.Rows;

        Assert.AreEqual(
            """SELECT array_cat(r."Tags", r."Tags")""",
            Select(rows.Select(r => r.Tags.Union(r.Tags))));
        Assert.AreEqual(
            """SELECT array_remove(r."Tags", 'a')""",
            Select(rows.Select(r => r.Tags.Remove("a"))));
        Assert.AreEqual(
            """SELECT CASE WHEN r."MaybeTags" IS NULL THEN NULL ELSE array_cat(r."MaybeTags", r."Tags") END""",
            Select(rows.Select(r => r.MaybeTags!.Union(r.Tags))));
        Assert.AreEqual(
            """SELECT CASE WHEN r."Label" IS NULL THEN NULL ELSE array_remove(r."Tags", r."Label") END""",
            Select(rows.Select(r => r.Tags.Remove(r.Label!))));

        var filter = rows.Where(r => r.Tags.Union(r.Tags).Contains("a")).ToQueryString();
        StringAssert.Contains(filter, """WHERE array_cat(r."Tags", r."Tags") @> ARRAY['a']::text[]""");
        Assert.DoesNotContain("IS NOT NULL", filter);

        // The projection, before FROM, on one line.
        static string Select<T>(IQueryable<T> query)
            => string.Join(' ', query.ToQueryString().Split("FROM")[0].Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static IEnumerable<(string Name, LambdaExpression Lambda)> Calls()
    {
        (string, LambdaExpression) Value(string name, Expression<Func<NullOperandRow, StringSet>> lambda) => (name, lambda);
        (string, LambdaExpression) Predicate(string name, Expression<Func<NullOperandRow, bool>> lambda) => (name, lambda);

        var other = StringSet.From(["z"]);
        StringSet? none = null;

        yield return Value("r.MaybeTags.Union(r.Tags)", r => r.MaybeTags!.Union(r.Tags));
        yield return Value("r.Tags.Union(r.MaybeTags)", r => r.Tags.Union(r.MaybeTags!));
        yield return Value("r.MaybeTags.Union(@{z})", r => r.MaybeTags!.Union(other));
        // The fallback docs/migration.md suggests for code that wants the other set back.
        yield return Value("(r.MaybeTags ?? StringSet.Empty).Union(@{z})", r => (r.MaybeTags ?? StringSet.Empty).Union(other));
        yield return Value("r.Tags.Union(@null)", r => r.Tags.Union(none!));
        yield return Value("r.Child.Tags.Union(r.Tags)", r => r.Child!.Tags.Union(r.Tags));
        yield return Value("r.Tags.Union(r.Child.Tags)", r => r.Tags.Union(r.Child!.Tags));
        yield return Value("r.Tags.Union(r.Tags)", r => r.Tags.Union(r.Tags));
        yield return Value("r.Tags.Remove(r.Label)", r => r.Tags.Remove(r.Label!));
        yield return Value("r.MaybeTags.Remove(\"a\")", r => r.MaybeTags!.Remove("a"));
        yield return Value("r.MaybeTags.Remove(r.Label)", r => r.MaybeTags!.Remove(r.Label!));
        yield return Value("r.Tags.Remove(r.Child.Name)", r => r.Tags.Remove(r.Child!.Name));
        yield return Value("r.Child.Tags.Remove(\"a\")", r => r.Child!.Tags.Remove("a"));
        yield return Value("r.MaybeTags.Union(r.Tags).Remove(r.Label)", r => r.MaybeTags!.Union(r.Tags).Remove(r.Label!));

        yield return Predicate("r.MaybeTags.Union(r.Tags).Contains(\"a\")", r => r.MaybeTags!.Union(r.Tags).Contains("a"));
        yield return Predicate("r.Tags.Union(r.MaybeTags).IsSupersetOf(r.Tags)", r => r.Tags.Union(r.MaybeTags!).IsSupersetOf(r.Tags));
        yield return Predicate("r.Tags.Union(r.Child.Tags).IsEmpty", r => r.Tags.Union(r.Child!.Tags).IsEmpty);
        yield return Predicate("r.Tags.Remove(r.Label).IsEmpty", r => r.Tags.Remove(r.Label!).IsEmpty);
        yield return Predicate("r.Tags.Remove(r.Label).Count > 1", r => r.Tags.Remove(r.Label!).Count > 1);

        (string, LambdaExpression) Codes(string name, Expression<Func<NullOperandRow, Int32Set>> lambda) => (name, lambda);

        var nine = Int32Set.From([9]);
        Int32Set? noCodes = null;

        yield return Codes("r.MaybeCodes.Union(r.Codes)", r => r.MaybeCodes!.Union(r.Codes));
        yield return Codes("r.Codes.Union(r.Child.Codes)", r => r.Codes.Union(r.Child!.Codes));
        yield return Codes("r.Codes.Add(r.Number)", r => r.Codes.Add(r.Number!.Value));
        yield return Codes("r.MaybeCodes.Add(2)", r => r.MaybeCodes!.Add(2));
        yield return Codes("r.Codes.Intersect(r.MaybeCodes)", r => r.Codes.Intersect(r.MaybeCodes!));
        yield return Codes("r.Child.Codes.Intersect(r.Codes)", r => r.Child!.Codes.Intersect(r.Codes));
        yield return Codes("r.MaybeCodes.Except(@{9})", r => r.MaybeCodes!.Except(nine));
        yield return Codes("r.Codes.Except(@null)", r => r.Codes.Except(noCodes!));
        yield return Codes("r.Codes.Except(r.Child.Codes)", r => r.Codes.Except(r.Child!.Codes));

        yield return Predicate("r.MaybeCodes.Union(r.Codes) == r.Codes", r => r.MaybeCodes!.Union(r.Codes) == r.Codes);
        yield return Predicate("r.Codes.Intersect(r.MaybeCodes).Count > 0", r => r.Codes.Intersect(r.MaybeCodes!).Count > 0);
        yield return Predicate("r.Codes.Except(r.Child.Codes).IsEmpty", r => r.Codes.Except(r.Child!.Codes).IsEmpty);
        yield return Predicate("r.Codes.Add(r.Number).Contains(1)", r => r.Codes.Add(r.Number!.Value).Contains(1));
    }

    private static string Describe(NullOperandRow row) =>
        $"Tags {row.Tags}, MaybeTags {row.MaybeTags?.ToString() ?? "NULL"}, Label {row.Label ?? "NULL"}, "
      + $"Codes {row.Codes}, MaybeCodes {row.MaybeCodes?.ToString() ?? "NULL"}, Number {row.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "NULL"}, "
      + (row.Child is { } child ? $"child {child.Tags} / {child.Name} / {child.Codes}" : "no child");

    /// <summary>A database of its own: a NULL set every third row, a NULL element every third row from the second, a child every other row.</summary>
    private static async Task<string> Seed()
    {
        var connectionString = await CompositionDatabase.Create("set_null_operand");
        await using var seed = new NullOperandContext(connectionString);
        await seed.Database.EnsureCreatedAsync();

        for (var i = 0; i < 12; i++)
        {
            seed.Rows.Add(new NullOperandRow
            {
                Id         = i + 1,
                Tags       = Sets[i % Sets.Length],
                MaybeTags  = i % 3 == 0 ? null : Sets[(i + 2) % Sets.Length],
                Label      = i % 3 == 1 ? null : Labels[i % Labels.Length],
                Codes      = CodeSets[i % CodeSets.Length],
                MaybeCodes = i % 3 == 0 ? null : CodeSets[(i + 2) % CodeSets.Length],
                Number     = i % 3 == 1 ? null : Numbers[i % Numbers.Length],
                Child      = i % 2 == 0
                                 ? new NullOperandChild
                                 {
                                     Id = 100 + i, Tags = Sets[(i + 3) % Sets.Length], Name = Labels[(i + 1) % Labels.Length],
                                     Codes = CodeSets[(i + 3) % CodeSets.Length]
                                 }
                                 : null
            });
        }

        await seed.SaveChangesAsync();
        return connectionString;
    }

    public sealed class NullOperandRow
    {
        public int Id { get; set; }

        public StringSet Tags { get; set; } = StringSet.Empty;

        public StringSet? MaybeTags { get; set; }

        public string? Label { get; set; }

        public Int32Set Codes { get; set; } = Int32Set.Empty;

        public Int32Set? MaybeCodes { get; set; }

        public int? Number { get; set; }

        public NullOperandChild? Child { get; set; }
    }

    public sealed class NullOperandChild
    {
        public int Id { get; set; }

        public int ParentId { get; set; }

        public StringSet Tags { get; set; } = StringSet.Empty;

        public string Name { get; set; } = "";

        public Int32Set Codes { get; set; } = Int32Set.Empty;
    }

    private sealed class NullOperandContext(string connectionString) : DbContext
    {
        public DbSet<NullOperandRow> Rows => Set<NullOperandRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => CompositionDatabase.Configure(optionsBuilder, connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullOperandRow>().Property(r => r.Id).ValueGeneratedNever();
            modelBuilder.Entity<NullOperandChild>().Property(c => c.Id).ValueGeneratedNever();
            modelBuilder.Entity<NullOperandRow>().HasOne(r => r.Child).WithOne().HasForeignKey<NullOperandChild>(c => c.ParentId);
        }
    }
}

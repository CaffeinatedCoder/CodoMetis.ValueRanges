using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// A predicate with a NULL range, multirange or value set on either side is <c>false</c> (#355). The
/// translators write each such operand as <c>x IS NOT NULL AND &lt;predicate&gt;</c>, and EF Core's
/// nullability processing leaves the check out for a required column and turns it into
/// <c>x IS NULL OR NOT (…)</c> under a negation. These pin both halves: the SQL of a required column is
/// what it was before, and a nullable one gets the check.
/// <c>ExecutedQueryTests.EveryPredicate_OverANullableColumn_IsFalseForTheNullRow</c> is the authority.
/// </summary>
[TestClass]
public class NullOperandTranslationTests
{
    public sealed class Row
    {
        public int Id { get; set; }

        public DateRange Period { get; set; } = DateRange.Empty;

        public DateRange? Maybe { get; set; }

        public RangeSet<DateRange, DateOnly>? MaybeBlocks { get; set; }

        public StringSet? MaybeTags { get; set; }

        public DateOnly Start { get; set; }

        public DateOnly? Day { get; set; }
    }

    private sealed class Context : DbContext
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql("Host=localhost;Database=design_time", npgsql => npgsql.UseValueRanges());
    }

    // A captured local, so EF Core sends it as a parameter; a static readonly field is inlined.
    private static DateRange NewWindow() => DateRange.CreateFinite(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 5), true, true);

    private static string Where(Func<IQueryable<Row>, IQueryable<Row>> query)
    {
        using var context = new Context();
        return query(context.Rows).ToQueryString().Split("WHERE")[1].Trim();
    }

    /// <summary><see cref="Where"/> with line breaks as one space, for a <c>CASE</c> the SQL spreads over lines.</summary>
    private static string Flat(Func<IQueryable<Row>, IQueryable<Row>> query) => Where(query).ReplaceLineEndings(" ");

    [TestMethod]
    public void ARequiredColumn_KeepsItsSql()
    {
        var window = NewWindow();

        Assert.AreEqual("isempty(r.\"Period\")", Where(rows => rows.Where(r => r.Period.IsEmpty())));
        Assert.AreEqual("NOT (isempty(r.\"Period\"))", Where(rows => rows.Where(r => !r.Period.IsEmpty())));
        Assert.AreEqual("NOT (r.\"Period\" && @window)", Where(rows => rows.Where(r => !r.Period.Overlaps(window))));
    }

    [TestMethod]
    public void ANullableRangeColumn_IsCheckedForNull()
    {
        var window = NewWindow();

        Assert.AreEqual("r.\"Maybe\" IS NOT NULL AND r.\"Maybe\" && @window", Where(rows => rows.Where(r => r.Maybe!.Overlaps(window))));
        Assert.AreEqual("r.\"Maybe\" IS NULL OR NOT (r.\"Maybe\" && @window)", Where(rows => rows.Where(r => !r.Maybe!.Overlaps(window))));
        Assert.AreEqual("r.\"Maybe\" IS NULL OR NOT (isempty(r.\"Maybe\"))", Where(rows => rows.Where(r => !r.Maybe!.IsEmpty())));

        // The column as the operand.
        Assert.AreEqual("r.\"Maybe\" IS NULL OR NOT (@window @> r.\"Maybe\")", Where(rows => rows.Where(r => !window.Contains(r.Maybe!))));
    }

    [TestMethod]
    public void ANullableMultirangeOrValueSetColumn_IsCheckedForNull()
    {
        var window = NewWindow();

        Assert.AreEqual("r.\"MaybeBlocks\" IS NULL OR NOT (r.\"MaybeBlocks\" && @window)", Where(rows => rows.Where(r => !r.MaybeBlocks!.Overlaps(window))));
        Assert.AreEqual("r.\"MaybeTags\" IS NULL OR NOT (r.\"MaybeTags\" @> ARRAY['x']::text[])", Where(rows => rows.Where(r => !r.MaybeTags!.Contains("x"))));
    }

    [TestMethod]
    public void AProjectedPredicate_IsFalseForANullColumn()
    {
        using var context = new Context();
        var sql = context.Rows.Select(r => r.Maybe!.IsEmpty()).ToQueryString();
        StringAssert.Contains(sql, "SELECT r.\"Maybe\" IS NOT NULL AND isempty(r.\"Maybe\")");
    }

    /// <summary>
    /// An element is NULL on the optional side of an outer join or in a nullable column read with
    /// <c>.Value</c>, and <c>Contains(T)</c> over it is <c>false</c> too (#381): <c>NOT (r."Period" @&gt;
    /// r."Day")</c> dropped the row EF Core keeps for its own comparisons, and a projection threw.
    /// </summary>
    [TestMethod]
    public void ANullableElement_IsCheckedForNull()
    {
        Assert.AreEqual("r.\"Day\" IS NOT NULL AND r.\"Period\" @> r.\"Day\"", Where(rows => rows.Where(r => r.Period.Contains(r.Day!.Value))));
        Assert.AreEqual("r.\"Day\" IS NULL OR NOT (r.\"Period\" @> r.\"Day\")", Where(rows => rows.Where(r => !r.Period.Contains(r.Day!.Value))));
        Assert.AreEqual(
            "r.\"MaybeBlocks\" IS NULL OR r.\"Day\" IS NULL OR NOT (r.\"MaybeBlocks\" @> r.\"Day\")",
            Where(rows => rows.Where(r => !r.MaybeBlocks!.Contains(r.Day!.Value))));

        // A required element keeps its SQL.
        Assert.AreEqual("NOT (r.\"Period\" @> r.\"Start\")", Where(rows => rows.Where(r => !r.Period.Contains(r.Start))));
    }
    /// <summary>
    /// A range factory over a NULL element is NULL (#382). <c>CreateFinite</c> gave <c>empty</c> through
    /// its inverted-bounds guard, and the unbounded factories gave <c>(,)</c>, because PostgreSQL's
    /// constructor reads a NULL bound as unbounded, so <c>Contains(v)</c> was <c>true</c> for a row with
    /// no element. Over required elements the test folds away and the SQL is what it was.
    /// </summary>
    [TestMethod]
    public void ARangeFactoryOverANullableElement_IsNull()
    {
        Assert.AreEqual("CASE     WHEN r.\"Day\" IS NULL THEN NULL     ELSE daterange(r.\"Day\", NULL, '[)') END IS NOT NULL AND CASE     WHEN r.\"Day\" IS NULL THEN NULL     ELSE daterange(r.\"Day\", NULL, '[)') END @> r.\"Start\"", Flat(rows => rows.Where(r => DateRange.CreateUnboundedEnd(r.Day!.Value, true).Contains(r.Start))));
        Assert.AreEqual("CASE     WHEN r.\"Day\" IS NULL THEN NULL     ELSE daterange(NULL, r.\"Day\", '(]') END IS NULL", Flat(rows => rows.Where(r => DateRange.CreateUnboundedStart(r.Day!.Value, true) == null)));
        Assert.AreEqual("CASE     WHEN r.\"Day\" IS NULL THEN NULL     WHEN r.\"Start\" <= r.\"Day\" THEN daterange(r.\"Start\", r.\"Day\", '[]')     ELSE 'empty'::daterange END IS NOT NULL AND isempty(CASE     WHEN r.\"Day\" IS NULL THEN NULL     WHEN r.\"Start\" <= r.\"Day\" THEN daterange(r.\"Start\", r.\"Day\", '[]')     ELSE 'empty'::daterange END)", Flat(rows => rows.Where(r => DateRange.CreateFinite(r.Start, r.Day!.Value, true, true).IsEmpty())));

        // Required elements keep their SQL.
        Assert.AreEqual("daterange(r.\"Start\", NULL, '[)') IS NOT NULL AND daterange(r.\"Start\", NULL, '[)') && r.\"Period\"", Flat(rows => rows.Where(r => DateRange.CreateUnboundedEnd(r.Start, true).Overlaps(r.Period))));
        Assert.AreEqual("CASE     WHEN r.\"Start\" <= r.\"Start\" THEN daterange(r.\"Start\", r.\"Start\", '[]')     ELSE 'empty'::daterange END IS NOT NULL AND isempty(CASE     WHEN r.\"Start\" <= r.\"Start\" THEN daterange(r.\"Start\", r.\"Start\", '[]')     ELSE 'empty'::daterange END)", Flat(rows => rows.Where(r => DateRange.CreateFinite(r.Start, r.Start, true, true).IsEmpty())));
    }
}

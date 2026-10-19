using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// A captured collection of ranges — <c>wanted.Contains(b.Seats)</c> — is sent one parameter per
/// range, because as one parameter Npgsql reads it as a multirange.
/// </summary>
/// <remarks>
/// Up to 9.0.0 the collection went out as <c>b."Seats" = ANY (@wanted)</c>, an untyped
/// <c>NpgsqlRange&lt;int&gt;[]</c> that Npgsql infers as <c>int4multirange</c>, and PostgreSQL failed
/// with <c>42809 op ANY/ALL (array) requires array on right side</c>. The answers are checked against
/// live PostgreSQL in <c>RangeCollectionContainsTests</c>; these pin the SQL.
/// </remarks>
[TestClass]
public sealed class RangeCollectionTranslationTests
{
    /// <summary>The WHERE clause only — the projection lists every column and drowns the assertion.</summary>
    private static string Where(Func<TestDbContext, IQueryable<Booking>> query)
    {
        using var context = new TestDbContext();
        var       sql     = query(context).ToQueryString();
        var       index   = sql.IndexOf("WHERE", StringComparison.Ordinal);

        Assert.IsTrue(index >= 0, $"No WHERE clause in the generated SQL:\n{sql}");
        return sql[index..].ReplaceLineEndings(" ");
    }

    [TestMethod]
    public void Contains_OverACapturedList_IsInOverOneParameterPerRange()
    {
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5), Int32Range.Empty };

        Assert.AreEqual("WHERE b.\"Seats\" IN (@wanted1, @wanted2)", Where(db => db.Bookings.Where(b => wanted.Contains(b.Seats))));
    }

    [TestMethod]
    public void Contains_OverACapturedArray_IsInOverOneParameterPerRange()
    {
        var wanted = new[] { DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 2, 1)), DateRange.Infinite };

        Assert.AreEqual("WHERE b.\"Period\" IN (@wanted1, @wanted2)", Where(db => db.Bookings.Where(b => wanted.Contains(b.Period))));
    }

    [TestMethod]
    public void Contains_OverACapturedCollectionOfTheCustomTimeRange_IsInOverOneParameterPerRange()
    {
        // timerange has no type Npgsql could infer for an array of it, so it threw InvalidCastException.
        IEnumerable<TimeRange> wanted = [TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0)), TimeRange.Empty];

        Assert.AreEqual("WHERE b.\"OpeningHours\" IN (@wanted1, @wanted2)", Where(db => db.Bookings.Where(b => wanted.Contains(b.OpeningHours))));
    }

    [TestMethod]
    public void NotContains_OverACapturedList_IsNotIn()
    {
        var wanted = new List<DecimalRange> { DecimalRange.CreateFinite(1.5m, 2m), DecimalRange.Empty };

        Assert.AreEqual("WHERE b.\"Price\" NOT IN (@wanted1, @wanted2)", Where(db => db.Bookings.Where(b => !wanted.Contains(b.Price))));
    }

    /// <summary>
    /// Until 10.0.0 <c>Any</c> without a <c>Contains</c> went out as one parameter, read through
    /// <c>unnest</c> of the multirange Npgsql typed it as — which merged overlapping and adjacent
    /// ranges and dropped empty ones (#76). It reads the list from <c>VALUES</c> now, like a value set
    /// collection and like a range collection beside a <c>Contains</c>.
    /// </summary>
    [TestMethod]
    public void AnyAlone_OverACapturedList_ReadsTheRangesFromValues()
    {
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(10, 20) };

        Assert.AreEqual(
            "WHERE EXISTS (     SELECT 1     FROM (VALUES (@wanted1), (@wanted2)) AS w(\"Value\")     WHERE w.\"Value\" && b.\"Seats\")",
            Where(db => db.Bookings.Where(b => wanted.Any(w => w.Overlaps(b.Seats)))));
    }

    [TestMethod]
    public void CountAlone_OverACapturedList_ReadsTheRangesFromValues()
    {
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(3, 8) };

        StringAssert.Contains(
            Where(db => db.Bookings.Where(b => wanted.Count(w => w.Overlaps(b.Seats)) == 2)),
            "FROM (VALUES (@wanted1), (@wanted2)) AS w(\"Value\")     WHERE w.\"Value\" && b.\"Seats\") = 2");
    }

    [TestMethod]
    public void AnyAlone_OverAnEmptyCapturedList_ReadsNoRows()
    {
        var wanted = new List<Int32Range>();

        Assert.AreEqual(
            "WHERE EXISTS (     SELECT 1     FROM (         SELECT NULL::int4range AS \"Value\"         WHERE FALSE     ) AS w     WHERE w.\"Value\" && b.\"Seats\")",
            Where(db => db.Bookings.Where(b => wanted.Any(w => w.Overlaps(b.Seats)))));
    }

    [TestMethod]
    public void Any_OverACapturedListAContainsAlsoReads_ReadsTheRangesFromValues()
    {
        // The Contains made the whole query fail before, so switching the parameter changes no answer.
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(10, 20) };

        Assert.AreEqual(
            "WHERE b.\"Seats\" IN (@wanted1, @wanted2) OR EXISTS (     SELECT 1     FROM (VALUES (@wanted1), (@wanted2)) AS w(\"Value\")     WHERE w.\"Value\" && b.\"Seats\")",
            Where(db => db.Bookings.Where(b => wanted.Contains(b.Seats) || wanted.Any(w => w.Overlaps(b.Seats)))));
    }

    /// <summary>
    /// An empty captured collection sent one parameter per range expands to a <c>VALUES</c> with no
    /// rows (#137). EF Core 11 reads no rows for it itself — a table-less <c>SELECT</c> of <c>NULL</c>s
    /// under <c>WHERE FALSE</c> — and the plugin casts each <c>NULL</c> to the column's type, since a
    /// bare one is <c>text</c> to PostgreSQL and fails the operator on it (#290). <c>Contains</c> over
    /// it is EF Core's own <c>FALSE</c>, folded out of the <c>OR</c>.
    /// </summary>
    [TestMethod]
    public void AnyBesideContains_OverAnEmptyCapturedList_ReadsNoRows()
    {
        var wanted = new List<Int32Range>();

        Assert.AreEqual(
            "WHERE EXISTS (     SELECT 1     FROM (         SELECT NULL::int4range AS \"Value\"         WHERE FALSE     ) AS w     WHERE w.\"Value\" && b.\"Seats\")",
            Where(db => db.Bookings.Where(b => wanted.Contains(b.Seats) || wanted.Any(w => w.Overlaps(b.Seats)))));
    }

    [TestMethod]
    public void Contains_OverEFParameter_IsRefusedWithWhatToWriteInstead()
    {
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5) };
        using var context = new TestDbContext();

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Bookings.Where(b => EF.Parameter(wanted).Contains(b.Seats)).ToQueryString());

        StringAssert.Contains(refusal.Message, "A collection of Int32Range cannot be sent as a single parameter ('EF.Parameter')");
        StringAssert.Contains(refusal.Message, "reads as a multirange");
        StringAssert.Contains(refusal.Message, "Remove 'EF.Parameter': without it the collection is sent one parameter per range");
    }

    [TestMethod]
    public void AnyAlone_OverEFParameter_IsRefusedWithWhatToWriteInstead()
    {
        // Until 10.0.0 only Contains over a range collection was refused; Any ran, through the merging
        // multirange (#76). One parameter is wrong everywhere, so it is refused everywhere.
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5) };
        using var context = new TestDbContext();

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(
            () => context.Bookings.Where(b => EF.Parameter(wanted).Any(w => w.Overlaps(b.Seats))).ToQueryString());

        StringAssert.Contains(refusal.Message, "merges overlapping and adjacent ranges and drops empty ones");
        StringAssert.Contains(refusal.Message, "Any or Count read it from VALUES");
    }

    [TestMethod]
    public void Contains_OverEFConstant_KeepsItsLiterals()
    {
        var wanted = new List<Int32Range> { Int32Range.CreateFinite(1, 5), Int32Range.Empty };

        Assert.AreEqual(
            "WHERE b.\"Seats\" IN ('[1,5]'::int4range, 'empty'::int4range)",
            Where(db => db.Bookings.Where(b => EF.Constant(wanted).Contains(b.Seats))));
    }

    [TestMethod]
    public void Contains_OverACapturedListOfRangeSets_IsUnchanged()
    {
        // A collection of multiranges binds as one multirange array, which is what it is, and worked.
        var wanted = new List<RangeSet<Int32Range, int>> { RangeSet<Int32Range, int>.Empty };

        Assert.AreEqual("WHERE b.\"SeatBlocks\" = ANY (@wanted)", Where(db => db.Bookings.Where(b => wanted.Contains(b.SeatBlocks))));
    }
}

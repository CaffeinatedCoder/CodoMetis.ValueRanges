using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// Verifies LINQ-to-SQL translation of the value set algebra via
/// <see cref="EntityFrameworkQueryableExtensions.ToQueryString"/>. Static set operands are
/// inlined by EF as constants and rendered as <c>ARRAY[...]::type</c> literals; captured
/// locals become parameters. Both paths are covered per operator.
/// </summary>
[TestClass]
public sealed class SetQueryTranslationTests
{
    private static readonly StringSet Wanted = StringSet.From("a", "b");

    private static readonly TestKey ReadKey = TestKey.Parse("users.read", CultureInfo.InvariantCulture);

    private static string Sql(Func<TestDbContext, IQueryable<object?>> query)
    {
        using var context = new TestDbContext();
        return query(context).ToQueryString();
    }

    // -------------------------------------------------------------------------
    // Contains — always @>, so a GIN index can serve it
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Contains_Constant()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Contains("x")));
        StringAssert.Contains(sql, "b.\"Tags\" @> ARRAY['x']::text[]");
    }

    [TestMethod]
    public void Contains_Parameter()
    {
        var tag = "x";
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Contains(tag)));
        StringAssert.Contains(sql, "b.\"Tags\" @> ARRAY[@");
    }

    [TestMethod]
    public void Contains_Column()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Contains(b.Tag)));
        StringAssert.Contains(sql, "b.\"Tags\" @> ARRAY[b.\"Tag\"]::text[]");
    }

    [TestMethod]
    public void Contains_Int32Constant()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Codes.Contains(42)));
        StringAssert.Contains(sql, "b.\"Codes\" @> ARRAY[42]::integer[]");
    }

    [TestMethod]
    public void Contains_DateConstant()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.BlackoutDays.Contains(new DateOnly(2024, 6, 15))));
        StringAssert.Contains(sql, "b.\"BlackoutDays\" @> ARRAY[DATE '2024-06-15']::date[]");
    }

    // -------------------------------------------------------------------------
    // Wrapper elements bind as the primitive store type
    // -------------------------------------------------------------------------

    [TestMethod]
    public void WrapperContains_Parameter_BindsAsText()
    {
        var key = TestKey.Parse("users.read", CultureInfo.InvariantCulture);
        var sql = Sql(db => db.Bookings.Where(b => b.Permissions.Contains(key)));
        StringAssert.Contains(sql, "b.\"Permissions\" @> ARRAY[@");
    }

    [TestMethod]
    public void WrapperContains_Constant_RendersBackingText()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Permissions.Contains(ReadKey)));
        StringAssert.Contains(sql, "b.\"Permissions\" @> ARRAY['users.read']::text[]");
    }

    // -------------------------------------------------------------------------
    // Set-operand comparisons
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Overlaps_Constant()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Overlaps(Wanted)));
        StringAssert.Contains(sql, "b.\"Tags\" && ARRAY['a','b']::text[]");
    }

    [TestMethod]
    public void Overlaps_Parameter()
    {
        var other = StringSet.From("a");
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Overlaps(other)));
        StringAssert.Contains(sql, "b.\"Tags\" && @");
    }

    [TestMethod]
    public void IsSubsetOf_Constant()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.IsSubsetOf(Wanted)));
        StringAssert.Contains(sql, "b.\"Tags\" <@ ARRAY['a','b']::text[]");
    }

    [TestMethod]
    public void IsSupersetOf_Constant()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.IsSupersetOf(Wanted)));
        StringAssert.Contains(sql, "b.\"Tags\" @> ARRAY['a','b']::text[]");
    }

    [TestMethod]
    public void IsSubsetOf_Parameter()
    {
        var other = StringSet.From("a", "b", "c");
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.IsSubsetOf(other)));
        StringAssert.Contains(sql, "b.\"Tags\" <@ @");
    }

    // -------------------------------------------------------------------------
    // Proper containment — the operator paired with its negated converse, so both
    // halves stay multiplicity-insensitive (`<>` would not be)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void IsProperSubsetOf_Constant()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.IsProperSubsetOf(Wanted)));
        StringAssert.Contains(
            sql, "b.\"Tags\" <@ ARRAY['a','b']::text[] AND NOT (b.\"Tags\" @> ARRAY['a','b']::text[])");
    }

    [TestMethod]
    public void IsProperSupersetOf_Constant()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.IsProperSupersetOf(Wanted)));
        StringAssert.Contains(
            sql, "b.\"Tags\" @> ARRAY['a','b']::text[] AND NOT (b.\"Tags\" <@ ARRAY['a','b']::text[])");
    }

    // -------------------------------------------------------------------------
    // Remove — array_remove, which preserves canonical form
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Remove_TranslatesToArrayRemove()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Remove("x").IsEmpty));
        StringAssert.Contains(sql, "array_remove(b.\"Tags\", 'x')");
    }

    [TestMethod]
    public void Remove_Count_StaysTranslated()
    {
        // The contrast with Union: array_remove keeps the array canonical, so cardinality
        // over it is correct and the Count guard must not refuse it.
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Remove("x").Count > 1));
        StringAssert.Contains(sql, "cardinality(array_remove(b.\"Tags\", 'x')) > 1");
    }

    [TestMethod]
    public void Add_StillFailsTranslation()
    {
        // PostgreSQL cannot insert at a sorted position: array_append would break canonical
        // order, and array_sort orders by collation rather than ordinal.
        using var context = new TestDbContext();
        var query = context.Bookings.Where(b => b.Tags.Add("x").IsEmpty);

        Assert.ThrowsExactly<InvalidOperationException>(() => query.ToQueryString());
    }

    // -------------------------------------------------------------------------
    // Cardinality
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Count_TranslatesToCardinality()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Count > 2));
        StringAssert.Contains(sql, "cardinality(b.\"Tags\") > 2");
    }

    [TestMethod]
    public void IsEmpty_TranslatesToCardinalityZero()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.IsEmpty));
        StringAssert.Contains(sql, "cardinality(b.\"Tags\") = 0");
    }

    [TestMethod]
    public void Count_Select()
    {
        var sql = Sql(db => db.Bookings.Select(b => (object?)b.Tags.Count));
        StringAssert.Contains(sql, "cardinality(b.\"Tags\")");
    }

    // -------------------------------------------------------------------------
    // Union — array_cat; the server value re-canonicalizes on read
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Union_InPredicate()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).Contains("x")));
        StringAssert.Contains(sql, "array_cat(b.\"Tags\", ARRAY['a','b']::text[]) @> ARRAY['x']::text[]");
    }

    [TestMethod]
    public void Union_Count_FailsTranslation()
    {
        // array_cat concatenates, so cardinality would count shared elements twice.
        // Refusing beats a silently inflated number.
        using var context = new TestDbContext();
        var query = context.Bookings.Where(b => b.Tags.Union(Wanted).Count > 2);

        Assert.ThrowsExactly<InvalidOperationException>(() => query.ToQueryString());
    }

    [TestMethod]
    public void Union_IsEmpty_StaysTranslated()
    {
        // A concatenation is empty exactly when both sides are, so duplicates cannot affect it.
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).IsEmpty));
        StringAssert.Contains(sql, "cardinality(array_cat(b.\"Tags\", ARRAY['a','b']::text[])) = 0");
    }

    [TestMethod]
    public void Count_OnAPlainColumn_StillTranslates()
    {
        // The Union guard keys off array_cat, so an ordinary set column is unaffected.
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Count > 2));
        StringAssert.Contains(sql, "cardinality(b.\"Tags\") > 2");
    }

    // -------------------------------------------------------------------------
    // Equality — translated by EF itself; assumes canonical writers (documented)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Equality_Parameter()
    {
        var other = StringSet.From("a");
        var sql = Sql(db => db.Bookings.Where(b => b.Tags == other));
        StringAssert.Contains(sql, "b.\"Tags\" = @");
    }

    // -------------------------------------------------------------------------
    // Client-only members fail translation by design
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Intersect_FailsTranslation()
    {
        using var context = new TestDbContext();
        var query = context.Bookings.Where(b => b.Tags.Intersect(Wanted).IsEmpty);

        Assert.ThrowsExactly<InvalidOperationException>(() => query.ToQueryString());
    }
}

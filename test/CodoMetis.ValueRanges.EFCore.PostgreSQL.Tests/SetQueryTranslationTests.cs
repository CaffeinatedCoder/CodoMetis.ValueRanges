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
    public void Add_OnText_IsAConcatenation_WhereOrderAndRepeatsDontCount()
    {
        // Canonical order would mean sorting in SQL, and text sorts by the database collation
        // rather than ordinally (#479). So Add stays unsorted, under the uses that ignore that (#485).
        // The other families canonicalize (CanonicalSetTranslationTests).
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Add("x").IsEmpty)),
                              "cardinality(array_cat(b.\"Tags\", ARRAY['x']::text[])) = 0");
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
    // Union on text — array_cat; the server value re-canonicalizes on read
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Union_InPredicate()
    {
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).Contains("x")));
        StringAssert.Contains(sql, "array_cat(b.\"Tags\", ARRAY['a','b']::text[]) @> ARRAY['x']::text[]");
    }

    [TestMethod]
    public void Union_Equality_ComparesByMutualContainment()
    {
        // array_cat concatenates, and array equality is sensitive to both multiplicity and order, so
        // = would answer false for sets that are equal in memory: {a,c} ∪ {a,b} differs from {a,b,c}
        // by the repeated a, and {a,c} ∪ {b} by order alone. Two sets are equal when each contains the
        // other, which neither changes (#485).
        const string same = "array_cat(b.\"Tags\", ARRAY['a','b']::text[]) @> ARRAY['a','b']::text[] AND ARRAY['a','b']::text[] @> array_cat(b.\"Tags\", ARRAY['a','b']::text[])";

        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted) == Wanted)), same);
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted) != Wanted)), $"NOT ({same})");
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).Equals(Wanted))), same);

        // And through array_remove, which keeps an unsorted array unsorted.
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).Remove("x") == Wanted)),
                              "array_remove(array_cat(b.\"Tags\", ARRAY['a','b']::text[]), 'x') @> ARRAY['a','b']::text[] AND");
    }

    [TestMethod]
    public void Union_EqualityAndCount_InAProjection_RunAsSql()
    {
        // In a projection EF emitted SELECT array_cat(…) = @p, which answered false against live PostgreSQL
        // for {a,c} ∪ {a,b} = {a,b,c}, and Count declined the union and ran in memory. Both run as SQL now
        // (#485), and UnionCanonicalityTests runs them against a server.
        StringAssert.Contains(Sql(db => db.Bookings.Select(b => (object?)(b.Tags.Union(Wanted) == Wanted))),
                              "array_cat(b.\"Tags\", ARRAY['a','b']::text[]) @> ARRAY['a','b']::text[] AND");
        StringAssert.Contains(Sql(db => db.Bookings.Select(b => (object?)b.Tags.Union(Wanted).Count)),
                              "count(DISTINCT (vr0.x))::int\n    FROM unnest(array_cat(b.\"Tags\", ARRAY['a','b']::text[])) AS vr0(x)");
    }

    [TestMethod]
    public void Equality_WithoutAUnion_StillTranslates()
    {
        // The refusal is scoped to a server-computed union: comparing a column against a
        // canonical value is ordinary array equality and stays translatable.
        var sql = Sql(db => db.Bookings.Where(b => b.Tags == Wanted));
        StringAssert.Contains(sql, "ARRAY['a','b']::text[]");
    }

    [TestMethod]
    public void Union_InsensitivePredicates_StillTranslate()
    {
        // The operators that ignore order and multiplicity keep composing on a union — that is
        // the whole reason array_cat is an acceptable translation for it.
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).IsSupersetOf(Wanted))),
                              "array_cat");
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).Overlaps(Wanted))),
                              "array_cat");
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).IsProperSubsetOf(Wanted))),
                              "array_cat");
    }

    [TestMethod]
    public void Union_Count_CountsDistinctElements()
    {
        // array_cat concatenates, so cardinality would count shared elements twice. The distinct
        // elements are the set's (#485).
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).Count > 2)),
                              "count(DISTINCT (vr0.x))::int\n    FROM unnest(array_cat(b.\"Tags\", ARRAY['a','b']::text[])) AS vr0(x)) > 2");
    }

    [TestMethod]
    public void Union_Remove_Count_CountsDistinctElements()
    {
        // array_remove keeps an unsorted array unsorted, so a concatenation wrapped in one is still one.
        // Matching only the outermost call let this through as cardinality: against live PostgreSQL,
        // {a,c}.Union({a,b}).Remove("x") counted 4 where the in-memory expression is {a,b,c}, 3.
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).Remove("x").Count > 2)),
                              "FROM unnest(array_remove(array_cat(b.\"Tags\", ARRAY['a','b']::text[]), 'x')) AS vr0(x)) > 2");
    }

    [TestMethod]
    public void Remove_Count_OnACanonicalOperand_StillTranslates()
    {
        // The refusal must not overreach: array_remove over a column preserves canonical form,
        // so cardinality is exact.
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Remove("x").Count > 2));
        StringAssert.Contains(sql, "cardinality(array_remove(b.\"Tags\", 'x')) > 2");
    }

    [TestMethod]
    public void Union_Remove_IsEmpty_StaysTranslated()
    {
        // Still duplicate-insensitive: the result is empty exactly when every element of both
        // sides was removed.
        var sql = Sql(db => db.Bookings.Where(b => b.Tags.Union(Wanted).Remove("x").IsEmpty));
        StringAssert.Contains(sql, "cardinality(array_remove(array_cat(b.\"Tags\", ARRAY['a','b']::text[]), 'x')) = 0");
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
    // Intersect and Except on text: a filter of the left array, left unsorted (#485)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Intersect_OnText_FiltersTheLeftArray()
    {
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Intersect(Wanted).IsEmpty)),
                              "WHERE vr0.x IS NOT NULL AND ARRAY['a','b']::text[] @> ARRAY[vr0.x]::text[]))) = 0");
    }

    [TestMethod]
    public void Except_OnText_FiltersTheLeftArray()
    {
        StringAssert.Contains(Sql(db => db.Bookings.Where(b => b.Tags.Except(Wanted).IsEmpty)),
                              "WHERE vr0.x IS NOT NULL AND NOT (ARRAY['a','b']::text[] @> ARRAY[vr0.x]::text[])))) = 0");
    }
}

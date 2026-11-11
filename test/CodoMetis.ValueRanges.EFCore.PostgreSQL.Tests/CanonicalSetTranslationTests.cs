using System.Linq.Expressions;
using System.Reflection;
using CodoMetis.ValueRanges.Core;
using Microsoft.EntityFrameworkCore;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.Tests;

/// <summary>
/// <c>Union</c>, <c>Add</c>, <c>Intersect</c> and <c>Except</c> for every family PostgreSQL sorts as
/// memory does (#478): the canonical subquery where the consumer sees order or duplicates, the cheaper
/// multiset form where it cannot, and text still refused. The answers are asserted against a server in
/// <c>CanonicalSetOperationParityTests</c> (integration).
/// </summary>
[TestClass]
public sealed class CanonicalSetTranslationTests
{
    private static readonly Int32Set Wanted = Int32Set.From(1, 2);

    private static string Sql(Func<TestDbContext, IQueryable> query)
    {
        using var context = new TestDbContext();
        return query(context).ToQueryString().ReplaceLineEndings("\n");
    }

    private static void AssertContains(string sql, string fragment)
        => StringAssert.Contains(sql, fragment.ReplaceLineEndings("\n"));

    // -------------------------------------------------------------------------
    // Canonical form, where the consumer sees order and duplicates
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Union_UnderEquality_IsCanonical()
    {
        var other = Wanted;

        AssertContains(
            Sql(db => db.Bookings.Where(b => b.Codes.Union(other) == other).Select(b => b.Id)),
            """
            WHERE ARRAY((
                SELECT DISTINCT ON (vr0.x) vr0.x
                FROM unnest(array_cat(b."Codes", @other)) WITH ORDINALITY AS vr0(x)
                WHERE vr0.x IS NOT NULL
                ORDER BY vr0.x, vr0.ordinality)) = @other
            """);
    }

    [TestMethod]
    public void Add_UnderCount_IsCanonical()
    {
        var element = 7;

        AssertContains(
            Sql(db => db.Bookings.Where(b => b.Codes.Add(element).Count > 1).Select(b => b.Id)),
            """
            WHERE cardinality(ARRAY((
                SELECT DISTINCT ON (vr0.x) vr0.x
                FROM unnest(array_cat(b."Codes", ARRAY[@element]::integer[])) WITH ORDINALITY AS vr0(x)
                WHERE vr0.x IS NOT NULL
                ORDER BY vr0.x, vr0.ordinality))) > 1
            """);
    }

    [TestMethod]
    public void Except_UnderEquality_IsCanonical_AndFiltersByContainment()
        => AssertContains(
            Sql(db => db.Bookings.Where(b => b.Rates.Except(b.Rates) == b.Rates).Select(b => b.Id)),
            """
            WHERE ARRAY((
                SELECT DISTINCT ON (vr0.x) vr0.x
                FROM unnest(b."Rates") WITH ORDINALITY AS vr0(x)
                WHERE vr0.x IS NOT NULL AND NOT (b."Rates" @> ARRAY[vr0.x]::numeric[])
                ORDER BY vr0.x, vr0.ordinality)) = b."Rates"
            """);

    /// <summary>
    /// The outer subquery canonicalizes, so the union inside it needs only be a multiset, and each
    /// subquery gets an alias of its own.
    /// </summary>
    [TestMethod]
    public void NestedOperations_CanonicalizeOnce_WithOneAliasEach()
    {
        var other = Wanted;

        AssertContains(
            Sql(db => db.Bookings.Select(b => b.Codes.Union(other).Intersect(b.Codes) == other)),
            """
            SELECT ARRAY((
                SELECT DISTINCT ON (vr0.x) vr0.x
                FROM unnest(array_cat(b."Codes", @other)) WITH ORDINALITY AS vr0(x)
                WHERE vr0.x IS NOT NULL AND b."Codes" @> ARRAY[vr0.x]::integer[]
                ORDER BY vr0.x, vr0.ordinality)) = @other
            """);

        var twice = Sql(db => db.Bookings.Where(b => b.Codes.Union(other) == b.Codes.Intersect(other)).Select(b => b.Id));
        AssertContains(twice, "FROM unnest(array_cat(b.\"Codes\", @other)) WITH ORDINALITY AS vr0(x)");
        AssertContains(twice, "FROM unnest(b.\"Codes\") WITH ORDINALITY AS vr1(x)");
    }

    /// <summary>A grouping key and a deduplicated projection compare the values, so both stay canonical.</summary>
    [TestMethod]
    public void GroupingAndDistinct_KeepCanonicalForm()
    {
        var other = Wanted;

        AssertContains(
            Sql(db => db.Bookings.GroupBy(b => b.Codes.Union(other)).Select(g => g.Count())),
            """
                SELECT ARRAY((
                    SELECT DISTINCT ON (vr0.x) vr0.x
                    FROM unnest(array_cat(b."Codes", @other)) WITH ORDINALITY AS vr0(x)
                    WHERE vr0.x IS NOT NULL
                    ORDER BY vr0.x, vr0.ordinality)) AS "Key"
            """);

        AssertContains(
            Sql(db => db.Bookings.Select(b => b.Codes.Union(other)).Distinct()),
            """
            SELECT DISTINCT ARRAY((
                SELECT DISTINCT ON (vr0.x) vr0.x
            """);
    }

    // -------------------------------------------------------------------------
    // The multiset form, where the consumer ignores order and duplicates
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Union_UnderContainment_IsTheBareConcatenation()
    {
        var other = Wanted;

        AssertContains(
            Sql(db => db.Bookings.Where(b => b.Codes.Union(other).IsSupersetOf(other)).Select(b => b.Id)),
            "WHERE array_cat(b.\"Codes\", @other) @> @other");
    }

    [TestMethod]
    public void Intersect_UnderContainment_IsAFilterWithoutSorting()
    {
        var other = Wanted;

        AssertContains(
            Sql(db => db.Bookings.Where(b => b.Codes.Intersect(other).Contains(1)).Select(b => b.Id)),
            """
            WHERE ARRAY((
                SELECT vr0.x
                FROM unnest(b."Codes") AS vr0(x)
                WHERE vr0.x IS NOT NULL AND @other @> ARRAY[vr0.x]::integer[])) @> ARRAY[1]::integer[]
            """);
    }

    [TestMethod]
    public void Except_UnderIsEmpty_IsAFilterWithoutSorting()
    {
        var other = Wanted;

        AssertContains(
            Sql(db => db.Bookings.Where(b => b.Codes.Except(other).IsEmpty).Select(b => b.Id)),
            """
            WHERE cardinality(ARRAY((
                SELECT vr0.x
                FROM unnest(b."Codes") AS vr0(x)
                WHERE vr0.x IS NOT NULL AND NOT (@other @> ARRAY[vr0.x]::integer[])))) = 0
            """);
    }

    /// <summary>The outermost projection is read, and reading canonicalizes.</summary>
    [TestMethod]
    public void TheOutermostProjection_IsAMultiset()
    {
        var other = Wanted;

        AssertContains(Sql(db => db.Bookings.Select(b => b.Codes.Union(other))), "SELECT array_cat(b.\"Codes\", @other)\nFROM");
        AssertContains(
            Sql(db => db.Bookings.Select(b => b.Codes.Intersect(other))),
            """
            SELECT ARRAY((
                SELECT vr0.x
                FROM unnest(b."Codes") AS vr0(x)
                WHERE vr0.x IS NOT NULL AND @other @> ARRAY[vr0.x]::integer[]))
            FROM "Bookings" AS b
            """);
    }

    // -------------------------------------------------------------------------
    // Every family
    // -------------------------------------------------------------------------

    private static IEnumerable<PropertyInfo> SetColumns
        => typeof(Booking).GetProperties()
                          .Where(p => p.PropertyType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueSet<>)));

    private static bool IsText(PropertyInfo column)
        => column.PropertyType == typeof(StringSet)
        || column.PropertyType.IsGenericType && column.PropertyType.GetGenericTypeDefinition() == typeof(StringSet<>);

    /// <summary>
    /// <c>b.Column.Operation(b.Column).Count &gt; 1</c>, built per column: the count sees duplicates, so
    /// it needs the canonical form.
    /// </summary>
    private static IQueryable CountOf(TestDbContext db, PropertyInfo column, string operation)
    {
        var b       = Expression.Parameter(typeof(Booking), "b");
        var set     = Expression.Property(b, column);
        var element = column.PropertyType.GetInterfaces()
                            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueSet<>))
                            .GetGenericArguments()[0];
        var method  = typeof(ValueSetExtensions).GetMethods()
                                                .Single(m => m.Name == operation && m.GetParameters().Length == 2)
                                                .MakeGenericMethod(column.PropertyType, element);
        var call    = Expression.Call(method, set, set);
        var count   = Expression.GreaterThan(Expression.Property(call, "Count"), Expression.Constant(1));
        return db.Bookings.Where(Expression.Lambda<Func<Booking, bool>>(count, b));
    }

    [TestMethod]
    public void EveryFamilyButText_Canonicalizes()
    {
        var columns = SetColumns.Where(c => !IsText(c)).ToList();
        Assert.IsGreaterThanOrEqualTo(15, columns.Count, "the model holds too few set columns");

        using var db = new TestDbContext();
        foreach (var column in columns)
        foreach (var operation in new[] { "Union", "Intersect", "Except" })
        {
            var sql = CountOf(db, column, operation).ToQueryString();
            StringAssert.Contains(sql, "SELECT DISTINCT ON (vr0.x) vr0.x", $"{column.Name}.{operation}");
        }
    }

    /// <summary>
    /// Text has no canonical form in SQL (#479), so its count is of the distinct elements of the unsorted
    /// result (#485), and an ordering, which would see the order, is still refused.
    /// </summary>
    [TestMethod]
    public void Text_CountsDistinctElements_AndStillRefusesWhereOrderMatters()
    {
        var columns = SetColumns.Where(IsText).ToList();
        Assert.AreEqual(2, columns.Count, "StringSet and StringSet<TestKey>");

        using var db = new TestDbContext();
        foreach (var column in columns)
        foreach (var operation in new[] { "Union", "Intersect", "Except" })
        {
            var sql = CountOf(db, column, operation).ToQueryString();
            StringAssert.Contains(sql, "count(DISTINCT (vr", $"{column.Name}.{operation}");
            Assert.DoesNotContain("DISTINCT ON", sql, $"{column.Name}.{operation}");
            Assert.ThrowsExactly<InvalidOperationException>(() => OrderedBy(db, column, operation).ToQueryString(), $"{column.Name}.{operation}");
        }
    }

    /// <summary><c>b.Column.Operation(b.Column)</c> as an ordering key.</summary>
    private static IQueryable OrderedBy(TestDbContext db, PropertyInfo column, string operation)
    {
        var b       = Expression.Parameter(typeof(Booking), "b");
        var set     = Expression.Property(b, column);
        var element = column.PropertyType.GetInterfaces()
                            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueSet<>))
                            .GetGenericArguments()[0];
        var method  = typeof(ValueSetExtensions).GetMethods()
                                                .Single(m => m.Name == operation && m.GetParameters().Length == 2)
                                                .MakeGenericMethod(column.PropertyType, element);
        var key     = Expression.Lambda(Expression.Call(method, set, set), b);
        var orderBy = typeof(Queryable).GetMethods().Single(m => m.Name == nameof(Queryable.OrderBy) && m.GetParameters().Length == 2)
                                       .MakeGenericMethod(typeof(Booking), column.PropertyType);
        return (IQueryable)orderBy.Invoke(null, [db.Bookings, key])!;
    }
}

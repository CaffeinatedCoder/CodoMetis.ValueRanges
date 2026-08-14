using System.Globalization;

namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class SetCanonicalFormTests
{
    // -----------------------------------------------------------------------
    // Deduplication and sorting
    // -----------------------------------------------------------------------

    [TestMethod]
    public void From_DedupesAndSortsNumerically()
    {
        var set = Int32Set.From(3, 1, 3, 2);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, set.Values.ToArray());
    }

    [TestMethod]
    public void From_NegativeNumbers_SortNumerically()
    {
        var set = Int32Set.From(-3, 10, -5, 2);

        CollectionAssert.AreEqual(new[] { -5, -3, 2, 10 }, set.Values.ToArray());
    }

    [TestMethod]
    public void From_TenAndTwo_SortNumericallyNotTextually()
    {
        // Ordinal text order would give "10" < "2" — canonical order must be numeric.
        var set = Int32Set.From(10, 2);

        CollectionAssert.AreEqual(new[] { 2, 10 }, set.Values.ToArray());
    }

    [TestMethod]
    public void From_EmptyInput_ReturnsEmptySingleton()
    {
        Assert.AreSame(Int32Set.Empty, Int32Set.From());
        Assert.AreSame(StringSet.Empty, StringSet.From(Enumerable.Empty<string>()));
    }

    [TestMethod]
    public void From_ParamsSpan_Works()
    {
        var set = Int64Set.From(2L, 1L);

        CollectionAssert.AreEqual(new[] { 1L, 2L }, set.Values.ToArray());
    }

    [TestMethod]
    public void CollectionExpression_BuildsCanonicalSet()
    {
        Int32Set set = [3, 1, 2, 3];

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, set.Values.ToArray());
    }

    // -----------------------------------------------------------------------
    // String ordering: ordinal, never culture
    // -----------------------------------------------------------------------

    [TestMethod]
    public void StringSet_SortsOrdinal_NotCulture()
    {
        // Ordinal: 'z' (U+007A) sorts before 'ä' (U+00E4); any culture-aware comparison
        // (including the invariant culture's linguistic sort) would order "ä" first.
        var set = StringSet.From("ä", "z");

        CollectionAssert.AreEqual(new[] { "z", "ä" }, set.Values.ToArray());
    }

    [TestMethod]
    public void StringSet_UppercaseSortsBeforeLowercase_Ordinal()
    {
        var set = StringSet.From("a", "B");

        CollectionAssert.AreEqual(new[] { "B", "a" }, set.Values.ToArray());
    }

    [TestMethod]
    public void WrapperStringSet_SortsOrdinalOverTextForm_IgnoringCompareTo()
    {
        // TestPermission's CompareTo is culture-sensitive; canonical order must come from the
        // ordinal comparison of the invariant text forms instead.
        var zulu = TestPermission.Parse("zulu.read", CultureInfo.InvariantCulture);
        var ähre = TestPermission.Parse("ähre.read", CultureInfo.InvariantCulture);

        var set = StringSet<TestPermission>.From(ähre, zulu);

        CollectionAssert.AreEqual(new[] { zulu, ähre }, set.Values.ToArray());
    }

    // -----------------------------------------------------------------------
    // Comparer-equal representatives
    // -----------------------------------------------------------------------

    [TestMethod]
    public void DecimalSet_DedupesByValue_KeepsFirstRepresentation()
    {
        var set = DecimalSet.From(1.00m, 1.0m);

        Assert.AreEqual(1, set.Count);
        Assert.AreEqual("1.00", set.Values[0].ToString(CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void DateTimeOffsetSet_DedupesByInstant()
    {
        var utc      = new DateTimeOffset(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var sameTime = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.FromHours(2));

        var set = DateTimeOffsetSet.From(utc, sameTime);

        Assert.AreEqual(1, set.Count);
    }

    // -----------------------------------------------------------------------
    // Null rejection
    // -----------------------------------------------------------------------

    [TestMethod]
    public void From_NullElement_Throws()
        => Assert.ThrowsExactly<ArgumentException>(() => StringSet.From("a", null!));

    [TestMethod]
    public void From_NullEnumerable_Throws()
        => Assert.ThrowsExactly<ArgumentNullException>(() => StringSet.From((IEnumerable<string>)null!));

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Empty_IsEmptyAndCountZero()
    {
        Assert.IsTrue(Int32Set.Empty.IsEmpty);
        Assert.AreEqual(0, Int32Set.Empty.Count);
    }

    [TestMethod]
    public void NonEmpty_CountMatches()
    {
        var set = StringSet.From("a", "b", "a");

        Assert.IsFalse(set.IsEmpty);
        Assert.AreEqual(2, set.Count);
    }

    [TestMethod]
    public void Foreach_EnumeratesCanonicalOrder()
    {
        var collected = new List<int>();
        foreach (var value in Int32Set.From(2, 1)) collected.Add(value);

        CollectionAssert.AreEqual(new[] { 1, 2 }, collected);
    }

    [TestMethod]
    public void DateSet_SortsChronologically()
    {
        var set = DateSet.From(new DateOnly(2024, 12, 24), new DateOnly(2024, 1, 1));

        Assert.AreEqual(new DateOnly(2024, 1, 1), set.Values[0]);
        Assert.AreEqual(new DateOnly(2024, 12, 24), set.Values[1]);
    }
}

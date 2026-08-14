namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class SetAlgebraTests
{
    // -----------------------------------------------------------------------
    // Contains
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Contains_PresentElement_True()
        => Assert.IsTrue(Int32Set.From(1, 2, 3).Contains(2));

    [TestMethod]
    public void Contains_AbsentElement_False()
        => Assert.IsFalse(Int32Set.From(1, 2, 3).Contains(9));

    [TestMethod]
    public void Contains_OnEmpty_False()
        => Assert.IsFalse(Int32Set.Empty.Contains(1));

    [TestMethod]
    public void Contains_String_UsesEquality()
    {
        var set = StringSet.From("a", "b");

        Assert.IsTrue(set.Contains("a"));
        Assert.IsFalse(set.Contains("A"));
    }

    [TestMethod]
    public void Contains_NullValue_Throws()
        => Assert.ThrowsExactly<ArgumentNullException>(() => StringSet.From("a").Contains(null!));

    [TestMethod]
    public void Contains_And_Remove_SearchInOrdinalOrder_NotCultureOrder()
    {
        // Membership binary-searches the canonical array, so it has to use the same order the
        // array was sorted with. StringSet sorts ordinal, where 'B' (U+0042) precedes 'a'
        // (U+0061) and 'z' precedes 'ä' — every culture-aware comparison disagrees on both, so
        // a search using Comparer<string>.Default would walk into the wrong half and miss.
        var set = StringSet.From("a", "B", "ä", "z");

        Assert.AreEqual("{B,a,z,ä}", set.ToString());

        foreach (var element in new[] { "a", "B", "ä", "z" })
            Assert.IsTrue(set.Contains(element), $"Contains missed the stored element '{element}'");

        Assert.AreEqual(StringSet.From("B", "z", "ä"), set.Remove("a"));
        Assert.AreEqual(StringSet.From("a", "B", "z"), set.Remove("ä"));
    }

    [TestMethod]
    public void Contains_ScalesToLargeSets()
    {
        var set = Int32Set.From(Enumerable.Range(0, 1000).Select(i => i * 3));

        Assert.IsTrue(set.Contains(2997));
        Assert.IsFalse(set.Contains(2998));
    }

    // -----------------------------------------------------------------------
    // Overlaps
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Overlaps_SharedElement_True()
        => Assert.IsTrue(Int32Set.From(1, 2, 3).Overlaps(Int32Set.From(3, 4)));

    [TestMethod]
    public void Overlaps_Disjoint_False()
        => Assert.IsFalse(Int32Set.From(1, 2).Overlaps(Int32Set.From(3, 4)));

    [TestMethod]
    public void Overlaps_Empty_False()
    {
        Assert.IsFalse(Int32Set.Empty.Overlaps(Int32Set.From(1)));
        Assert.IsFalse(Int32Set.From(1).Overlaps(Int32Set.Empty));
    }

    // -----------------------------------------------------------------------
    // IsSubsetOf / IsSupersetOf
    // -----------------------------------------------------------------------

    [TestMethod]
    public void IsSubsetOf_ProperSubset_True()
        => Assert.IsTrue(Int32Set.From(1, 2).IsSubsetOf(Int32Set.From(1, 2, 3)));

    [TestMethod]
    public void IsSubsetOf_Reflexive_True()
    {
        var set = Int32Set.From(1, 2);

        Assert.IsTrue(set.IsSubsetOf(set));
    }

    [TestMethod]
    public void IsSubsetOf_MissingElement_False()
        => Assert.IsFalse(Int32Set.From(1, 4).IsSubsetOf(Int32Set.From(1, 2, 3)));

    [TestMethod]
    public void IsSubsetOf_EmptyIsSubsetOfAnything()
    {
        Assert.IsTrue(Int32Set.Empty.IsSubsetOf(Int32Set.From(1)));
        Assert.IsTrue(Int32Set.Empty.IsSubsetOf(Int32Set.Empty));
    }

    [TestMethod]
    public void IsSupersetOf_MirrorsSubset()
    {
        Assert.IsTrue(Int32Set.From(1, 2, 3).IsSupersetOf(Int32Set.From(1, 2)));
        Assert.IsFalse(Int32Set.From(1, 2).IsSupersetOf(Int32Set.From(1, 2, 3)));
        Assert.IsTrue(Int32Set.From(1).IsSupersetOf(Int32Set.Empty));
    }

    // -----------------------------------------------------------------------
    // Union / Intersect / Except
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Union_MergesAndDedupes()
    {
        var union = Int32Set.From(1, 2, 3).Union(Int32Set.From(3, 4));

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, union.Values.ToArray());
    }

    [TestMethod]
    public void Union_WithEmpty_PreservesInstance()
    {
        var set = Int32Set.From(1, 2);

        Assert.AreSame(set, set.Union(Int32Set.Empty));
        Assert.AreSame(set, Int32Set.Empty.Union(set));
    }

    [TestMethod]
    public void Union_KeepsLeftRepresentative_AmongComparerEqualElements()
    {
        // decimal compares equal across scales, so {1.0} and {1.00} are the same element with
        // two representations. Canonicalize documents that the first in input order survives;
        // Union follows the same tie-break, including where the left side is a proper subset
        // and returning the right operand wholesale would have been a tempting shortcut.
        var left  = DecimalSet.From(1.0m);
        var right = DecimalSet.From(1.00m, 2m);

        Assert.AreEqual("{1.0,2}", left.Union(right).ToString());
        Assert.AreEqual("{1.00,2}", right.Union(left).ToString());
    }

    [TestMethod]
    public void Union_SubsetOperand_StillEqualsTheSuperset()
    {
        var left  = Int32Set.From(2);
        var right = Int32Set.From(1, 2, 3);

        Assert.AreEqual(right, left.Union(right));
    }

    [TestMethod]
    public void Intersect_KeepsSharedElements()
    {
        var intersection = Int32Set.From(1, 2, 3).Intersect(Int32Set.From(2, 3, 4));

        CollectionAssert.AreEqual(new[] { 2, 3 }, intersection.Values.ToArray());
    }

    [TestMethod]
    public void Intersect_Disjoint_ReturnsEmptySingleton()
        => Assert.AreSame(Int32Set.Empty, Int32Set.From(1).Intersect(Int32Set.From(2)));

    [TestMethod]
    public void Intersect_WithSuperset_PreservesInstance()
    {
        var set = Int32Set.From(1, 2);

        Assert.AreSame(set, set.Intersect(Int32Set.From(1, 2, 3)));
    }

    [TestMethod]
    public void Except_RemovesSharedElements()
    {
        var difference = Int32Set.From(1, 2, 3).Except(Int32Set.From(2));

        CollectionAssert.AreEqual(new[] { 1, 3 }, difference.Values.ToArray());
    }

    [TestMethod]
    public void Except_NothingShared_PreservesInstance()
    {
        var set = Int32Set.From(1, 2);

        Assert.AreSame(set, set.Except(Int32Set.From(9)));
    }

    [TestMethod]
    public void Except_Everything_ReturnsEmptySingleton()
        => Assert.AreSame(Int32Set.Empty, Int32Set.From(1, 2).Except(Int32Set.From(1, 2)));

    // -----------------------------------------------------------------------
    // Add / Remove
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Add_NewElement_InsertsInCanonicalPosition()
    {
        var set = Int32Set.From(1, 3).Add(2);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, set.Values.ToArray());
    }

    [TestMethod]
    public void Add_ExistingElement_PreservesInstance()
    {
        var set = Int32Set.From(1, 2);

        Assert.AreSame(set, set.Add(2));
    }

    [TestMethod]
    public void Remove_PresentElement_Removes()
    {
        var set = Int32Set.From(1, 2).Remove(2);

        CollectionAssert.AreEqual(new[] { 1 }, set.Values.ToArray());
    }

    [TestMethod]
    public void Remove_AbsentElement_PreservesInstance()
    {
        var set = Int32Set.From(1, 2);

        Assert.AreSame(set, set.Remove(9));
    }

    [TestMethod]
    public void Remove_LastElement_ReturnsEmptySingleton()
        => Assert.AreSame(Int32Set.Empty, Int32Set.From(1).Remove(1));

    // -----------------------------------------------------------------------
    // Algebra over string sets (ordinal comparer path)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void StringSet_AlgebraUsesOrdinalComparer()
    {
        var left  = StringSet.From("B", "a");
        var right = StringSet.From("a", "z");

        Assert.IsTrue(left.Overlaps(right));
        CollectionAssert.AreEqual(new[] { "B", "a", "z" }, left.Union(right).Values.ToArray());
        CollectionAssert.AreEqual(new[] { "a" }, left.Intersect(right).Values.ToArray());
    }
}

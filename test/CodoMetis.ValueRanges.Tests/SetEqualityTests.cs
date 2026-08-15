namespace CodoMetis.ValueRanges.Tests;

[TestClass]
public class SetEqualityTests
{
    [TestMethod]
    public void Equals_SameElementsAnyInputOrder_True()
    {
        Assert.AreEqual(Int32Set.From(1, 2), Int32Set.From(2, 1));
        Assert.IsTrue(Int32Set.From(1, 2) == Int32Set.From(2, 1, 1));
    }

    [TestMethod]
    public void Equals_DifferentElements_False()
    {
        Assert.AreNotEqual(Int32Set.From(1, 2), Int32Set.From(1, 3));
        Assert.IsTrue(Int32Set.From(1, 2) != Int32Set.From(1, 3));
    }

    [TestMethod]
    public void Equals_DifferentCounts_False()
        => Assert.AreNotEqual(Int32Set.From(1), Int32Set.From(1, 2));

    [TestMethod]
    public void Equals_EmptyEqualsEmpty()
    {
        Assert.AreEqual(Int32Set.Empty, Int32Set.From());
        Assert.IsTrue(Int32Set.Empty == Int32Set.From());
    }

    [TestMethod]
    public void Equals_NullHandling()
    {
        var set = Int32Set.From(1);

        Assert.IsFalse(set.Equals(null));
        Assert.IsFalse(set == null);
        Assert.IsFalse(null == set);
        Assert.IsTrue((Int32Set?)null == null);
        Assert.IsTrue(set != null);
    }

    [TestMethod]
    public void GetHashCode_EqualSets_EqualHashes()
        => Assert.AreEqual(Int32Set.From(1, 2).GetHashCode(), Int32Set.From(2, 1).GetHashCode());

    [TestMethod]
    public void Equals_StringSets_CaseSensitive()
    {
        Assert.AreEqual(StringSet.From("a"), StringSet.From("a"));
        Assert.AreNotEqual(StringSet.From("a"), StringSet.From("A"));
    }

    [TestMethod]
    public void Equals_ObjectOverload()
    {
        object boxed = Int32Set.From(1, 2);

        Assert.IsTrue(Int32Set.From(2, 1).Equals(boxed));
        Assert.IsFalse(Int32Set.From(1).Equals("not a set"));
    }
}

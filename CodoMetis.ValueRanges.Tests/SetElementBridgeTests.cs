using System.Globalization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The validated-wrapper arities (<see cref="StringSet{TElement}"/>, <see cref="GuidSet{TElement}"/>,
/// <see cref="Int32Set{TElement}"/>, <see cref="Int64Set{TElement}"/>) bridge elements through
/// their BCL <see cref="IFormattable"/>/<see cref="IParsable{TSelf}"/> surface — these tests pin
/// that contract with generator-shaped test keys.
/// </summary>
[TestClass]
public class SetElementBridgeTests
{
    private static TestPermission Permission(string value) => TestPermission.Parse(value, CultureInfo.InvariantCulture);

    // -----------------------------------------------------------------------
    // StringSet<TElement>
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperStringSet_DedupesByEquality()
    {
        var set = StringSet<TestPermission>.From(Permission("users.read"), Permission("USERS.READ"));

        Assert.AreEqual(1, set.Count);
    }

    [TestMethod]
    public void WrapperStringSet_FormatsAsTextArray()
    {
        var set = StringSet<TestPermission>.From(Permission("users.write"), Permission("users.read"));

        Assert.AreEqual("{users.read,users.write}", set.ToString());
    }

    [TestMethod]
    public void WrapperStringSet_ParseRevalidatesElements()
    {
        var set = StringSet<TestPermission>.Parse("{USERS.READ}", CultureInfo.InvariantCulture);

        Assert.IsTrue(set.Contains(Permission("users.read")));
    }

    [TestMethod]
    public void WrapperStringSet_ParseInvalidElement_Throws()
        => Assert.ThrowsExactly<FormatException>(
            () => StringSet<TestPermission>.Parse("{no-dot}", CultureInfo.InvariantCulture));

    [TestMethod]
    public void WrapperStringSet_RoundTrips()
    {
        var original = StringSet<TestPermission>.From(Permission("roles.assign"), Permission("users.read"));

        Assert.AreEqual(original, StringSet<TestPermission>.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperStringSet_AlgebraWorks()
    {
        var granted  = StringSet<TestPermission>.From(Permission("users.read"), Permission("users.write"));
        var required = StringSet<TestPermission>.From(Permission("users.read"));

        Assert.IsTrue(required.IsSubsetOf(granted));
        Assert.IsTrue(granted.Contains(Permission("users.write")));
        Assert.AreEqual(2, granted.Union(required).Count);
    }

    [TestMethod]
    public void WrapperStringSet_CollectionExpression()
    {
        StringSet<TestPermission> set = [Permission("b.x"), Permission("a.x")];

        Assert.AreEqual("{a.x,b.x}", set.ToString());
    }

    // -----------------------------------------------------------------------
    // GuidSet<TElement>
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperGuidSet_CanonicalOrderMatchesBackingGuidOrder()
    {
        var first  = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var wrapperSet = GuidSet<TestId>.From(TestId.FromGuid(second), TestId.FromGuid(first));
        var rawSet     = GuidSet.From(second, first);

        Assert.AreEqual(rawSet.Values[0].ToString("D"), wrapperSet.Values[0].ToString(null, CultureInfo.InvariantCulture));
        Assert.AreEqual(rawSet.Values[1].ToString("D"), wrapperSet.Values[1].ToString(null, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperGuidSet_RoundTrips()
    {
        var original = GuidSet<TestId>.From(TestId.FromGuid(Guid.NewGuid()), TestId.FromGuid(Guid.NewGuid()));

        Assert.AreEqual(original, GuidSet<TestId>.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    // -----------------------------------------------------------------------
    // Int32Set<TElement> / Int64Set<TElement>
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperInt32Set_SortsByBackingValue_NotTextForm()
    {
        // Ordinal text order would give "10" < "2"; the numeric families sort by CompareTo.
        var set = Int32Set<TestIntId>.From(new TestIntId(10), new TestIntId(2));

        Assert.AreEqual("{2,10}", set.ToString());
    }

    [TestMethod]
    public void WrapperInt32Set_RoundTrips()
    {
        var original = Int32Set<TestIntId>.From(new TestIntId(42), new TestIntId(-7));

        Assert.AreEqual(original, Int32Set<TestIntId>.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperInt64Set_RoundTrips()
    {
        var original = Int64Set<TestLongId>.From(new TestLongId(long.MaxValue), new TestLongId(1));

        Assert.AreEqual("{1,9223372036854775807}", original.ToString());
        Assert.AreEqual(original, Int64Set<TestLongId>.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    // -----------------------------------------------------------------------
    // Empty singletons per closed instantiation
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperSets_EmptySingletonPerInstantiation()
    {
        Assert.AreSame(StringSet<TestPermission>.Empty, StringSet<TestPermission>.From());
        Assert.AreSame(GuidSet<TestId>.Empty, GuidSet<TestId>.From());
        Assert.AreEqual("{}", Int32Set<TestIntId>.Empty.ToString());
    }
}

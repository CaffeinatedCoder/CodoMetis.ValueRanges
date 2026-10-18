using System.Globalization;
using System.Text.Json;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The validated-wrapper arities bridge elements through their BCL
/// <see cref="IFormattable"/>/<see cref="IParsable{TSelf}"/> surface — these tests pin that
/// contract with generator-shaped test keys, one per core family.
/// </summary>
/// <remarks>
/// The families split on which text form they ask their elements for. The string, Guid, integer
/// and decimal arities take the element's default, because for those primitives it round-trips.
/// The four temporal arities pin a round-trip format instead, so their tests assert on
/// sub-second components: those are exactly the digits a default-form bridge would drop.
/// </remarks>
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

    [TestMethod]
    public void WrapperInt16Set_SortsByBackingValue_NotTextForm()
    {
        var set = Int16Set<TestSmallId>.From(new TestSmallId(10), new TestSmallId(2));

        Assert.AreEqual("{2,10}", set.ToString());
        Assert.AreEqual(set, Int16Set<TestSmallId>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    // -----------------------------------------------------------------------
    // DecimalSet<TElement>
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperDecimalSet_KeepsScaleThroughTheTextForm()
    {
        var set = DecimalSet<TestMoney>.From(new TestMoney(12.50m), new TestMoney(1.5m));

        Assert.AreEqual("{1.5,12.50}", set.ToString());
        Assert.AreEqual(set, DecimalSet<TestMoney>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    // -----------------------------------------------------------------------
    // The temporal arities. These ask their elements for a round-trip format rather than
    // accepting the default, so the assertions that matter are the sub-second ones: with the
    // element's own default form, TimeOnly renders as 09:30 and DateTime as
    // 06/15/2024 10:30:00, and every one of these round trips would come back truncated.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperDateSet_FormatsIso_AndRoundTrips()
    {
        var set = DateSet<TestDay>.From(
            new TestDay(new DateOnly(2024, 12, 24)),
            new TestDay(new DateOnly(2024, 1, 1)));

        Assert.AreEqual("{2024-01-01,2024-12-24}", set.ToString());
        Assert.AreEqual(set, DateSet<TestDay>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperTimeSet_KeepsSubSecondPrecision()
    {
        var set = TimeSet<TestSlot>.From(
            new TestSlot(new TimeOnly(17, 30, 0)),
            new TestSlot(new TimeOnly(9, 30, 15, 250)));

        Assert.AreEqual("{09:30:15.2500000,17:30:00.0000000}", set.ToString());
        Assert.AreEqual(set, TimeSet<TestSlot>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperDateTimeSet_KeepsSubSecondPrecision()
    {
        var precise = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified).AddTicks(1_234_567);
        var set     = DateTimeSet<TestStamp>.From(new TestStamp(precise));

        Assert.AreEqual("{2024-06-15T10:30:00.1234567}", set.ToString());
        Assert.AreEqual(set, DateTimeSet<TestStamp>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperDateTimeOffsetSet_KeepsSubSecondPrecisionAndOffset()
    {
        var stamp = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(2)).AddTicks(1_234_567);
        var set   = DateTimeOffsetSet<TestOffsetStamp>.From(new TestOffsetStamp(stamp));

        Assert.AreEqual("{2024-06-15T10:30:00.1234567+02:00}", set.ToString());
        Assert.AreEqual(set, DateTimeOffsetSet<TestOffsetStamp>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The counterexample. A wrapper that swallows the format argument answers with its own
    /// default form — here the invariant culture's, which drops the sub-seconds. Until 9.0.0 that
    /// literal still parsed, through the element's own lenient <c>Parse</c>, into a truncated value:
    /// a silent loss in the pure model. Since 9.0.0 the family reads the text strictly as ISO 8601
    /// before the element sees it, so the culture form is refused on parse; since 10.0.0 it is refused
    /// on write too, naming the text-form contract, because the text the element gives cannot be read
    /// back (#53). The EF Core bridge refuses it as well (<c>SqlLiteralTests</c>).
    /// </summary>
    [TestMethod]
    public void WrapperDateTimeSet_ElementIgnoringTheFormat_IsRefusedOnWriteAndOnParse()
    {
        var precise = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified).AddTicks(1_234_567);
        var set     = DateTimeSet<TestLossyStamp>.From(new TestLossyStamp(precise));

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => set.ToString());
        StringAssert.Contains(refusal.Message, nameof(TestLossyStamp));
        StringAssert.Contains(refusal.Message, "must format to exactly the backing primitive's text form");

        // Quoted, because the culture form contains a space — itself a hint that the element is
        // not answering with anything the store would recognize.
        Assert.ThrowsExactly<FormatException>(
            () => DateTimeSet<TestLossyStamp>.Parse("{\"06/15/2024 10:30:00\"}", CultureInfo.InvariantCulture));
    }

    // -----------------------------------------------------------------------
    // The round trip of the element's text form (#53): Parse(Format(x)) must equal x
    // -----------------------------------------------------------------------

    /// <summary>
    /// An element whose text form is a different number passes the integer grammar; only the round
    /// trip tells. Until 10.0.0 <c>{124}</c> was written for 123 and read back as 124, silently.
    /// </summary>
    [TestMethod]
    public void WrapperIntSet_ElementWhoseTextIsAnotherNumber_IsRefusedOnWriteAndOnRead()
    {
        var set = Int32Set<TestOffByOne>.From(new TestOffByOne(123));

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => set.ToString());
        StringAssert.Contains(refusal.Message, nameof(TestOffByOne));
        StringAssert.Contains(refusal.Message, "'124'");
        StringAssert.Contains(refusal.Message, "reads back as a different element");
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(set));

        // Data written before the check: read back as a different element, so it is refused too.
        var read = Assert.ThrowsExactly<FormatException>(() => Int32Set<TestOffByOne>.Parse("{124}", CultureInfo.InvariantCulture));
        StringAssert.Contains(read.Message, "text form");
        Assert.IsFalse(Int32Set<TestOffByOne>.TryParse("{124}", CultureInfo.InvariantCulture, out _));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<Int32Set<TestOffByOne>>("[124]"));

        // A display format is not the wire form and is not checked.
        Assert.AreEqual("{124}", ((IFormattable)set).ToString("D", CultureInfo.InvariantCulture));
    }

    /// <summary>Every text is a valid string, so the string arity can only tell by the round trip.</summary>
    [TestMethod]
    public void WrapperStringSet_ElementKeepingItsDecoration_IsRefusedOnWriteAndOnRead()
    {
        var set = StringSet<TestLenientKey>.From(new TestLenientKey("123"));

        var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => set.ToString());
        StringAssert.Contains(refusal.Message, "'CUST-123'");
        StringAssert.Contains(refusal.Message, "text form");
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Serialize(set));

        Assert.ThrowsExactly<FormatException>(() => StringSet<TestLenientKey>.Parse("{CUST-123}", CultureInfo.InvariantCulture));
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<StringSet<TestLenientKey>>("[\"CUST-123\"]"));
    }

    /// <summary>
    /// The check is on the element, not the text: an element that normalizes on parse — trims and
    /// lowercases, as the documented <c>AccessRight</c> does — stores one value and reads it back,
    /// so a literal in the un-normalized form still parses.
    /// </summary>
    [TestMethod]
    public void WrapperStringSet_ElementNormalizingOnParse_StillRoundTrips()
    {
        var set = StringSet<TestPermission>.From(TestPermission.Parse(" Users.Read ", null));

        Assert.AreEqual("{users.read}", set.ToString());
        Assert.AreEqual(set, StringSet<TestPermission>.Parse("{users.read}", CultureInfo.InvariantCulture));
        Assert.AreEqual(set, StringSet<TestPermission>.Parse("{\" Users.Read \"}", CultureInfo.InvariantCulture));
    }

    // -----------------------------------------------------------------------
    // Empty singletons per closed instantiation
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperSets_EmptySingletonPerInstantiation()
    {
        Assert.AreSame(StringSet<TestPermission>.Empty, StringSet<TestPermission>.From());
        Assert.AreSame(GuidSet<TestId>.Empty, GuidSet<TestId>.From());
        Assert.AreSame(DateTimeSet<TestStamp>.Empty, DateTimeSet<TestStamp>.From());
        Assert.AreEqual("{}", Int32Set<TestIntId>.Empty.ToString());
        Assert.AreEqual("{}", DecimalSet<TestMoney>.Empty.ToString());
    }
}

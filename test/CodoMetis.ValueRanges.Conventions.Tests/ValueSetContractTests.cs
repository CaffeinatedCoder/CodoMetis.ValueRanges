using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;
using NodaTime.Calendars;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Turns three of the load-bearing value set rules in CLAUDE.md into tests, over every set type
/// that exists rather than a hard-coded list — so a set family added without honouring them
/// fails here instead of shipping.
/// </summary>
/// <remarks>
/// <para>
/// Each rule describes the same class of defect: the type answers, and the answer is wrong. A
/// set whose <c>From</c> normalizes but whose <c>NormalizeElement</c> does not reports
/// <c>Contains</c> as <see langword="false"/> for an element it holds. A set overriding
/// <c>CanonicalComparer</c> without <c>CanonicalOrder</c> binary-searches an array with an order
/// it was not sorted by, and misses. A set whose element type System.Text.Json cannot serialize
/// writes a property dump and reads back <see langword="default"/>. Nothing throws in any of the
/// three.
/// </para>
/// <para>
/// The rules are checked through behaviour, not reflection over the hooks: the hooks are
/// internal interface members, and asserting on an observable consequence is what makes the test
/// catch a wrong implementation rather than a missing declaration.
/// </para>
/// </remarks>
[TestClass]
public sealed class ValueSetContractTests
{
    /// <summary>
    /// Guards the tests below against the worst failure a discovery-driven suite has: finding
    /// nothing and passing. Every assertion here loops over <see cref="SetProbes.AllSetTypes"/>,
    /// so a reflection predicate that stops matching would retire the whole class silently.
    /// </summary>
    [TestMethod]
    public void Discovery_FindsEverySetFamily()
    {
        // 10 closed core types + 10 core wrapper arities + 5 NodaTime types + 5 NodaTime
        // wrapper arities, as of 7.0.0. A floor rather than an equality, so adding a set type
        // does not fail an unrelated test — the contract tests below cover it automatically,
        // and the probe test insists on probes.
        const int knownSetTypes = 30;

        var discovered = SetProbes.AllSetTypes().Select(pair => pair.Set.Name).ToList();

        Assert.IsTrue(
            discovered.Count >= knownSetTypes,
            $"Set type discovery found {discovered.Count} types, fewer than the {knownSetTypes} known "
          + $"to exist: {string.Join(", ", discovered)}. Every assertion in this class iterates that "
          + "list, so a predicate that stopped matching would retire them all while the suite stayed "
          + "green.");
    }

    [TestMethod]
    public void EverySetType_IsCoveredByAProbe()
    {
        var uncovered = SetProbes.AllSetTypes()
                       .Where(pair => !SetProbes.HasProbes(pair.Element))
                       .Select(pair => $"{pair.Set.Name} (element {pair.Element.Name})")
                       .ToList();

        Assert.AreEqual(
            0, uncovered.Count,
            $"These set types have no probe values, so the contract tests below silently skip them: "
          + $"{string.Join(", ", uncovered)}. Add probes to keep the coverage complete.");
    }

    [TestMethod]
    public void EverySetType_FindsAnElementItWasBuiltFrom()
    {
        // The observable consequence of the NormalizeElement and CanonicalOrder rules: whatever
        // From did to an element, Contains must undo in the same way, or membership lies.
        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
            Invoke(nameof(AssertContainsAgreesWithFrom), setType, elementType);
    }

    [TestMethod]
    public void EverySetType_RoundTripsThroughJson()
    {
        // The ElementJsonConverter rule: an element type System.Text.Json cannot serialize as a
        // scalar is property-dumped on write and read back as default — silently, on both legs.
        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
            Invoke(nameof(AssertJsonRoundTrips), setType, elementType);
    }

    /// <summary>
    /// String-backed families sort ordinal — never culture, never the element's own
    /// <see cref="IComparable"/>.
    /// </summary>
    /// <remarks>
    /// This is the one ordering claim nothing else can make. Both
    /// <see cref="EverySetType_KeepsItsElementsInCanonicalOrder"/> and
    /// <see cref="SmallModelSetOracleTests"/> read the order from
    /// <c>CanonicalComparer</c> itself, so they verify that every path agrees with the declared
    /// order — not that the declared order is the right one. Swapping
    /// <c>StringComparer.Ordinal</c> for <c>StringComparer.InvariantCulture</c> keeps both of them
    /// green while silently changing what the database and the client each consider sorted.
    /// </remarks>
    [TestMethod]
    public void StringBackedFamilies_SortOrdinal()
    {
        // 'Z' is 90 and 'a' is 97, so ordinal puts Zebra first; every culture puts apple first.
        // The probe table carries this pair for the same reason.
        var checkedFamilies = new List<string>();

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            if (!IsStringBacked(setType)) continue;

            Reflect.InvokeGeneric(typeof(ValueSetContractTests), nameof(AssertOrdinalOrder),
                                  setType, elementType);

            checkedFamilies.Add(setType.Name);
        }

        Assert.AreEqual(
            2, checkedFamilies.Count,
            $"Expected exactly the two string-backed families (StringSet and its wrapper arity), "
          + $"found [{string.Join(", ", checkedFamilies)}]. This is the one check here identified by "
          + "type rather than discovered, so a renamed or added string-backed family must be added "
          + "to IsStringBacked — silently matching none is what this assertion prevents.");
    }

    private static bool IsStringBacked(Type setType) =>
        setType == typeof(StringSet)
     || (setType.IsGenericType && setType.GetGenericTypeDefinition() == typeof(StringSet<>));

    private static void AssertOrdinalOrder<TSet, TElement>()
        where TSet : IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var probes = SetProbes.For<TElement>();
        var zebra  = probes.Single(probe => probe!.ToString()!.Contains("Zebra", StringComparison.Ordinal));
        var apple  = probes.Single(probe => probe!.ToString()!.Contains("apple", StringComparison.Ordinal));

        Assert.IsTrue(
            TSet.CanonicalComparer.Compare(zebra, apple) < 0,
            $"{typeof(TSet).Name}.CanonicalComparer orders {apple} before {zebra}, which is a culture "
          + "comparison — ordinal puts 'Z' (90) before 'a' (97). String-backed families sort ordinal: "
          + "the array is sorted client-side and binary-searched, and PostgreSQL's own ordering of a "
          + "text[] is not the current culture's.");
    }

    [TestMethod]
    public void EverySetType_KeepsItsElementsInCanonicalOrder()
    {
        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
            Invoke(nameof(AssertValuesAreCanonical), setType, elementType);
    }

    private static void Invoke(string method, Type setType, Type elementType)
    {
        if (!SetProbes.HasProbes(elementType)) return; // reported by EverySetType_IsCoveredByAProbe

        Reflect.InvokeGeneric(typeof(ValueSetContractTests), method, setType, elementType);
    }

    private static void AssertContainsAgreesWithFrom<TSet, TElement>()
        where TSet : IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        foreach (var probe in SetProbes.For<TElement>())
        {
            var set = TSet.From(probe);

            Assert.IsTrue(
                set.Contains(probe),
                $"{typeof(TSet).Name}.From({probe}) does not Contain the element it was built from. "
              + "A set type that normalizes or validates elements in From must override "
              + "IValueSet<T>.NormalizeElement, and one that overrides CanonicalComparer must "
              + "override IValueSet<T>.CanonicalOrder — otherwise the probe is compared against "
              + "storage it does not match, or binary-searched with the wrong order.");
        }

        // The whole probe set at once: every element must still be found once they coexist.
        var all = TSet.From(SetProbes.For<TElement>());

        foreach (var probe in SetProbes.For<TElement>())
        {
            Assert.IsTrue(
                all.Contains(probe),
                $"{typeof(TSet).Name} built from all probes does not Contain {probe}. "
              + "This is the ordering rule: the canonical array is binary-searched, so a "
              + "CanonicalOrder that disagrees with CanonicalComparer misses present elements.");
        }
    }

    private static void AssertJsonRoundTrips<TSet, TElement>()
        where TSet : IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var options = new JsonSerializerOptions();
        options.AddRangeConverters();

        var original = TSet.From(SetProbes.For<TElement>());
        var json     = JsonSerializer.Serialize(original, options);

        Assert.IsFalse(
            json.Contains('{', StringComparison.Ordinal),
            $"{typeof(TSet).Name} serialized to {json} — its elements were written as objects, "
          + "which means System.Text.Json had no scalar converter for the element type and fell "
          + "back to reflection. Override IValueSetFactory<TSet,T>.ElementJsonConverter.");

        var restored = JsonSerializer.Deserialize<TSet>(json, options);

        Assert.IsNotNull(restored, $"{typeof(TSet).Name} deserialized to null from {json}.");

        CollectionAssert.AreEqual(
            original.Values.ToArray(),
            restored.Values.ToArray(),
            $"{typeof(TSet).Name} did not survive a JSON round trip: {json} restored to "
          + $"[{string.Join(", ", restored.Values)}] instead of [{string.Join(", ", original.Values)}].");
    }

    private static void AssertValuesAreCanonical<TSet, TElement>()
        where TSet : IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        var values = TSet.From(SetProbes.For<TElement>()).Values;

        for (var index = 1; index < values.Length; index++)
        {
            Assert.IsTrue(
                TSet.CanonicalComparer.Compare(values[index - 1], values[index]) < 0,
                $"{typeof(TSet).Name} holds {values[index - 1]} before {values[index]}, which its own "
              + "CanonicalComparer does not order that way. Canonical form is sorted and "
              + "deduplicated on every construction path.");
        }
    }
}

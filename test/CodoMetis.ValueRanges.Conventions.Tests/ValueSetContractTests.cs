using System.Reflection;
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
    /// Probe elements per element type. Values that a normalizing set type would rewrite are
    /// deliberately included — an ISO-normalizing set is only exercised by a non-ISO probe.
    /// </summary>
    private static readonly Dictionary<Type, object[]> Probes = new()
    {
        // "Zebra" and "apple" are load-bearing: ordinal puts 'Z' (90) before 'a' (97), a culture
        // comparison puts apple first. Probes that both orders agree on would let the
        // CanonicalOrder rule pass while broken — they did, until a seeded defect showed it.
        [typeof(string)]         = ["beta", "Alpha", "gamma delta", "Zebra", "apple"],
        [typeof(Guid)]           = [Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), Guid.Empty],
        [typeof(short)]          = [(short)-7, (short)0, (short)32767],
        [typeof(int)]            = [-7, 0, 42],
        [typeof(long)]           = [-7L, 0L, 9_000_000_000L],
        [typeof(decimal)]        = [-1.5m, 0m, 12.75m],
        [typeof(DateOnly)]       = [new DateOnly(2024, 6, 15), new DateOnly(1970, 1, 1)],
        [typeof(TimeOnly)]       = [new TimeOnly(9, 30), new TimeOnly(23, 59, 59)],
        [typeof(DateTime)]       = [
            new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified),
            new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Local)
        ],
        [typeof(DateTimeOffset)] = [
            new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.FromHours(2))
        ],

        // NodaTime: the calendar-bearing types normalize to ISO at construction, so a probe in
        // another calendar is the one that exercises NormalizeElement.
        [typeof(LocalDate)]      = [
            new LocalDate(2024, 6, 15),
            new LocalDate(1740, 10, 8, CalendarSystem.Coptic)
        ],
        [typeof(LocalDateTime)]  = [
            new LocalDateTime(2024, 6, 15, 10, 30),
            new LocalDateTime(1740, 10, 8, 10, 30, CalendarSystem.Coptic)
        ],
        [typeof(Instant)]        = [Instant.FromUtc(2024, 6, 15, 10, 30), Instant.FromUnixTimeSeconds(0)],
        [typeof(LocalTime)]      = [new LocalTime(9, 30), new LocalTime(23, 59, 59)],

        // YearMonth rejects non-ISO calendars outright rather than normalizing, so every probe
        // must be ISO — a non-ISO probe would assert the throw, not the round trip.
        [typeof(YearMonth)]      = [new YearMonth(2024, 6), new YearMonth(1970, 1)],

        // Validated wrapper elements, one per family arity.
        // Same ordinal-vs-culture split as the plain string probes, for the wrapper arity.
        [typeof(TextKey)]        = [
            TextKey.Parse("users.read", null),
            TextKey.Parse("  Admin  ", null),
            TextKey.Parse("Zebra", null),
            TextKey.Parse("apple", null)
        ],
        [typeof(TenantId)]       = [TenantId.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff", null)],
        [typeof(SmallCode)]      = [SmallCode.Parse("42", null), SmallCode.Parse("-7", null)],
        [typeof(LargeCode)]      = [LargeCode.Parse("9000000000", null), LargeCode.Parse("0", null)]
    };

    /// <summary>
    /// Every set type in the shipping assemblies, with the wrapper families closed over a
    /// representative validated element type.
    /// </summary>
    private static IEnumerable<(Type Set, Type Element)> AllSetTypes()
    {
        Type[] assemblyMarkers = [typeof(StringSet), typeof(LocalDateSet)];

        foreach (var setType in assemblyMarkers
                               .Select(marker => marker.Assembly)
                               .Distinct()
                               .SelectMany(assembly => assembly.GetExportedTypes())
                               .Where(IsValueSetType)
                               .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            if (!setType.IsGenericTypeDefinition)
            {
                yield return (setType, ElementTypeOf(setType));
                continue;
            }

            // A wrapper family: close it over the element type its arity is meant for.
            var closed = setType.MakeGenericType(WrapperElementFor(setType));
            yield return (closed, ElementTypeOf(closed));
        }
    }

    private static bool IsValueSetType(Type type) =>
        type is { IsClass: true, IsAbstract: false }
     && type.GetInterfaces().Any(@interface =>
            @interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IValueSet<>));

    private static Type ElementTypeOf(Type setType) =>
        setType.GetInterfaces()
               .First(@interface => @interface.IsGenericType
                                 && @interface.GetGenericTypeDefinition() == typeof(IValueSet<>))
               .GetGenericArguments()[0];

    private static Type WrapperElementFor(Type family) => family.Name switch
    {
        "StringSet`1" => typeof(TextKey),
        "GuidSet`1"   => typeof(TenantId),
        "Int32Set`1"  => typeof(SmallCode),
        "Int64Set`1"  => typeof(LargeCode),
        _             => throw new InvalidOperationException(
            $"No representative wrapper element type registered for the set family '{family.Name}'. "
          + "Add one to WrapperElementFor (and probes for it) so the new family is covered.")
    };

    /// <summary>
    /// Guards the tests below against the worst failure a discovery-driven suite has: finding
    /// nothing and passing. Every assertion here loops over <see cref="AllSetTypes"/>, so a
    /// reflection predicate that stops matching would retire the whole class silently.
    /// </summary>
    [TestMethod]
    public void Discovery_FindsEverySetFamily()
    {
        // 10 closed core types + 4 wrapper arities + 5 NodaTime types, as of 6.1.0. A floor
        // rather than an equality, so adding a set type does not fail an unrelated test — the
        // contract tests below cover it automatically, and the probe test insists on probes.
        const int knownSetTypes = 19;

        var discovered = AllSetTypes().Select(pair => pair.Set.Name).ToList();

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
        var uncovered = AllSetTypes()
                       .Where(pair => !Probes.ContainsKey(pair.Element))
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
        foreach (var (setType, elementType) in AllSetTypes())
            Invoke(nameof(AssertContainsAgreesWithFrom), setType, elementType);
    }

    [TestMethod]
    public void EverySetType_RoundTripsThroughJson()
    {
        // The ElementJsonConverter rule: an element type System.Text.Json cannot serialize as a
        // scalar is property-dumped on write and read back as default — silently, on both legs.
        foreach (var (setType, elementType) in AllSetTypes())
            Invoke(nameof(AssertJsonRoundTrips), setType, elementType);
    }

    [TestMethod]
    public void EverySetType_KeepsItsElementsInCanonicalOrder()
    {
        foreach (var (setType, elementType) in AllSetTypes())
            Invoke(nameof(AssertValuesAreCanonical), setType, elementType);
    }

    private static void Invoke(string method, Type setType, Type elementType)
    {
        if (!Probes.ContainsKey(elementType)) return; // reported by EverySetType_IsCoveredByAProbe

        typeof(ValueSetContractTests)
           .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
           .MakeGenericMethod(setType, elementType)
           .Invoke(null, null);
    }

    private static TElement[] ProbesFor<TElement>() => [.. Probes[typeof(TElement)].Cast<TElement>()];

    private static void AssertContainsAgreesWithFrom<TSet, TElement>()
        where TSet : IValueSetFactory<TSet, TElement>, IValueSet<TElement>
        where TElement : IEquatable<TElement>
    {
        foreach (var probe in ProbesFor<TElement>())
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
        var all = TSet.From(ProbesFor<TElement>());

        foreach (var probe in ProbesFor<TElement>())
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

        var original = TSet.From(ProbesFor<TElement>());
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
        var values = TSet.From(ProbesFor<TElement>()).Values;

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

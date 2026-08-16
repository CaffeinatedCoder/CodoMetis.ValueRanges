using System.Reflection;
using CodoMetis.ValueRanges.Core;
using NodaTime;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Guards the one claim a range type makes about its domain that nothing else can check:
/// whether it has a step.
/// </summary>
/// <remarks>
/// <para>
/// <c>IsDiscrete</c> and <c>NextValueAfter</c> are separate members that must agree.
/// <c>DiscreteCanonical</c> closes exclusive bounds by stepping, and it is called only by the
/// factories of discrete domains; a type that overrides <c>NextValueAfter</c> and forgets
/// <c>IsDiscrete</c> — or the reverse — would canonicalize one way and report the other.
/// Neither mistake produces a compile error, and neither throws.
/// </para>
/// <para>
/// Both facts are read through a generic helper rather than by reflecting on the members
/// directly: static abstract interface members cannot be invoked reflectively in a way that
/// respects the constraint, and asserting on a declaration would prove the member exists rather
/// than that it tells the truth.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeFactoryContractTests
{
    /// <summary>
    /// A probe value per element type, chosen well inside the domain so that
    /// <c>NextValueAfter</c> returning <see langword="null"/> means "continuous" rather than
    /// "at the maximum".
    /// </summary>
    private static readonly Dictionary<Type, object> Probes = new()
    {
        [typeof(int)]            = 0,
        [typeof(long)]           = 0L,
        [typeof(decimal)]        = 0m,
        [typeof(DateOnly)]       = new DateOnly(2024, 6, 15),
        [typeof(TimeOnly)]       = new TimeOnly(9, 30),
        [typeof(DateTime)]       = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Unspecified),
        [typeof(DateTimeOffset)] = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero),
        [typeof(LocalDate)]      = new LocalDate(2024, 6, 15),
        [typeof(LocalDateTime)]  = new LocalDateTime(2024, 6, 15, 10, 30),
        [typeof(Instant)]        = Instant.FromUtc(2024, 6, 15, 10, 30),
        [typeof(YearMonth)]      = new YearMonth(2024, 6)
    };

    private static IEnumerable<(Type Range, Type Element)> AllRangeTypes()
    {
        Type[] assemblyMarkers = [typeof(Int32Range), typeof(LocalDateRange)];

        foreach (var type in assemblyMarkers
                            .Select(marker => marker.Assembly)
                            .Distinct()
                            .SelectMany(assembly => assembly.GetExportedTypes())
                            .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            // The range unions are abstract records with their variants nested inside, so this
            // must not filter to concrete types — the mapping parity tests once did, and matched
            // nothing at all.
            var factory = type.GetInterfaces().FirstOrDefault(
                @interface => @interface.IsGenericType
                           && @interface.GetGenericTypeDefinition() == typeof(IRangeFactory<,>));

            if (factory is not null && factory.GetGenericArguments()[0] == type)
                yield return (type, factory.GetGenericArguments()[1]);
        }
    }

    /// <summary>
    /// The floor that keeps the rest of this class from passing by finding nothing.
    /// </summary>
    [TestMethod]
    public void Discovery_FindsEveryRangeType()
    {
        // 7 core + 4 NodaTime, as of 6.3.0.
        const int knownRangeTypes = 11;

        var discovered = AllRangeTypes().Select(pair => pair.Range.Name).ToList();

        Assert.IsTrue(
            discovered.Count >= knownRangeTypes,
            $"Range type discovery found {discovered.Count}, fewer than the {knownRangeTypes} known to "
          + $"exist: {string.Join(", ", discovered)}. Every assertion below iterates that list.");
    }

    [TestMethod]
    public void EveryRangeType_IsCoveredByAProbe()
    {
        var uncovered = AllRangeTypes()
                       .Where(pair => !Probes.ContainsKey(pair.Element))
                       .Select(pair => $"{pair.Range.Name} (element {pair.Element.Name})")
                       .ToList();

        Assert.AreEqual(
            0, uncovered.Count,
            $"No probe value registered for: {string.Join(", ", uncovered)}. Add one to Probes so the "
          + "agreement test below actually exercises the new type.");
    }

    /// <summary>
    /// <c>IsDiscrete</c> must say exactly what <c>NextValueAfter</c> does.
    /// </summary>
    [TestMethod]
    public void IsDiscrete_AgreesWithNextValueAfter()
    {
        var checker = typeof(RangeFactoryContractTests)
           .GetMethod(nameof(AssertAgrees), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var (rangeType, elementType) in AllRangeTypes())
            checker.MakeGenericMethod(rangeType, elementType)
                   .Invoke(null, [Probes[elementType]]);
    }

    private static void AssertAgrees<TRange, T>(T probe)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var hasStep = TRange.NextValueAfter(probe) is not null;

        Assert.AreEqual(
            TRange.IsDiscrete, hasStep,
            $"{typeof(TRange).Name}.IsDiscrete is {TRange.IsDiscrete} but NextValueAfter({probe}) "
          + $"{(hasStep ? "returned a successor" : "returned null")}. The two describe the same "
          + "property and are read by different code paths — DiscreteCanonical steps bounds, "
          + "Values() and the value-set bridge branch on IsDiscrete.");
    }

    /// <summary>
    /// The same agreement, read from the other direction: a type that steps must also
    /// canonicalize closed, which is what makes the discrete count in <c>Length</c> correct.
    /// </summary>
    [TestMethod]
    public void DiscreteTypes_CanonicalizeExclusiveBoundsToClosedForm()
    {
        var checker = typeof(RangeFactoryContractTests)
           .GetMethod(nameof(AssertCanonicalizesClosed), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var (rangeType, elementType) in AllRangeTypes())
            checker.MakeGenericMethod(rangeType, elementType)
                   .Invoke(null, [Probes[elementType]]);
    }

    private static void AssertCanonicalizesClosed<TRange, T>(T probe)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (!TRange.IsDiscrete) return;

        var next  = TRange.NextValueAfter(probe)!.Value;
        var after = TRange.NextValueAfter(next)!.Value;

        // [probe, after) must collapse to the closed [probe, next].
        var halfOpen = TRange.CreateFinite(probe, after, true, false);
        var closed   = TRange.CreateFinite(probe, next, true, true);

        Assert.AreEqual(
            closed, halfOpen,
            $"{typeof(TRange).Name} reports IsDiscrete but does not canonicalize an exclusive upper "
          + "bound to the closed form, so equal sets would compare unequal.");
    }
}

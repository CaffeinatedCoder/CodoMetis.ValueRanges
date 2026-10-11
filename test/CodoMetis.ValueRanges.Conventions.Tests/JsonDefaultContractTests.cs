using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Guards the wire form a type has when nobody registered anything: every range, variant,
/// multirange and value set must serialize exactly as it does under <c>AddRangeConverters()</c>,
/// and read back.
/// </summary>
/// <remarks>
/// <para>
/// Before 9.0.0 the converters were opt-in, and System.Text.Json's fallback for a type it has no
/// converter for is a property dump: <c>DateRange</c> wrote <c>{"Length":181}</c>, a value set
/// <c>{"Values":[…],"Count":2,"IsEmpty":false}</c>, and neither read back. Nothing threw on the
/// way out. An ASP.NET host that forgot the registration answered every request body with a 400
/// and every response with the wrong shape.
/// </para>
/// <para>
/// The fix is a <c>[JsonConverter]</c> on each type, and the trap is that System.Text.Json does
/// not inherit it: an attribute on the union leaves its sealed variants — which is what a value
/// reached through <see langword="object"/> presents — on the property dump. So this checks the
/// behaviour per shape, through the union and through <see langword="object"/>, rather than the
/// presence of an attribute somewhere in the hierarchy. A new range type, variant or set family
/// is discovered here with no edit.
/// </para>
/// </remarks>
[TestClass]
public sealed class JsonDefaultContractTests
{
    private static readonly JsonSerializerOptions Unregistered = new();
    private static readonly JsonSerializerOptions Registered   = new JsonSerializerOptions().AddRangeConverters();

    /// <summary>Two ordered values per element type, for the bounded shapes.</summary>
    private static readonly Dictionary<Type, (object Low, object High)> Bounds = new()
    {
        [typeof(int)]            = (1, 10),
        [typeof(long)]           = (1L, 10L),
        [typeof(decimal)]        = (1.5m, 9.25m),
        [typeof(DateOnly)]       = (new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)),
        [typeof(TimeOnly)]       = (new TimeOnly(9, 30), new TimeOnly(17, 0)),
        [typeof(DateTime)]       = (new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Unspecified),
                                    new DateTime(2026, 6, 30, 17, 0, 0, DateTimeKind.Unspecified)),
        [typeof(DateTimeOffset)] = (new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.FromHours(1)),
                                    new DateTimeOffset(2026, 6, 30, 17, 0, 0, TimeSpan.FromHours(2))),
        [typeof(LocalDate)]      = (new LocalDate(2026, 1, 1), new LocalDate(2026, 6, 30)),
        [typeof(LocalDateTime)]  = (new LocalDateTime(2026, 1, 1, 9, 30), new LocalDateTime(2026, 6, 30, 17, 0)),
        [typeof(Instant)]        = (Instant.FromUtc(2026, 1, 1, 9, 30), Instant.FromUtc(2026, 6, 30, 17, 0)),
        [typeof(YearMonth)]      = (new YearMonth(2026, 1), new YearMonth(2026, 6))
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
            var factory = type.GetInterfaces().FirstOrDefault(
                @interface => @interface.IsGenericType
                           && @interface.GetGenericTypeDefinition() == typeof(IRangeFactory<,>));

            // The union declares the factory over itself; its variants inherit that interface
            // and are reached through the union below, so they are not listed separately.
            if (factory is not null && factory.GetGenericArguments()[0] == type)
                yield return (type, factory.GetGenericArguments()[1]);
        }
    }

    /// <summary>The floor that keeps the loops below from passing by finding nothing.</summary>
    [TestMethod]
    public void Discovery_FindsEveryRangeAndSetType()
    {
        // 7 core + 4 NodaTime ranges; 10 core + 5 NodaTime set families, each in two arities.
        const int knownRangeTypes = 11;
        const int knownSetTypes   = 30;

        var ranges = AllRangeTypes().Count();
        var sets   = SetProbes.AllSetTypes().Count();

        Assert.IsTrue(ranges >= knownRangeTypes, $"Range discovery found {ranges}, fewer than {knownRangeTypes}.");
        Assert.IsTrue(sets >= knownSetTypes, $"Set discovery found {sets}, fewer than {knownSetTypes}.");
    }

    [TestMethod]
    public void EveryRangeType_IsCoveredByBounds()
    {
        var uncovered = AllRangeTypes()
                       .Where(pair => !Bounds.ContainsKey(pair.Element))
                       .Select(pair => $"{pair.Range.Name} (element {pair.Element.Name})")
                       .ToList();

        Assert.AreEqual(
            0, uncovered.Count,
            $"No bounds registered for: {string.Join(", ", uncovered)}. Add a pair to Bounds so the "
          + "wire-form test below exercises the new type's bounded shapes.");
    }

    /// <summary>
    /// Every shape of every range type, unregistered, writes the literal — through the union,
    /// through <see langword="object"/> (the variant's own converter), and inside a multirange —
    /// and reads back equal through each of the three declarations.
    /// </summary>
    [TestMethod]
    public void EveryRangeShape_SerializesAsItsLiteral_WithoutRegistration()
    {
        foreach (var (rangeType, elementType) in AllRangeTypes())
            Reflect.InvokeGeneric(typeof(JsonDefaultContractTests), nameof(AssertRangeShapes), rangeType, elementType);
    }

    private static void AssertRangeShapes<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (low, high) = ((T)Bounds[typeof(T)].Low, (T)Bounds[typeof(T)].High);

        TRange[] shapes =
        [
            TRange.Empty,
            TRange.Infinite,
            TRange.CreateFinite(low, high, startInclusive: true, endInclusive: false),
            TRange.CreateUnboundedEnd(low, startInclusive: true),
            TRange.CreateUnboundedStart(high, endInclusive: false)
        ];

        foreach (var range in shapes)
        {
            var variant = range.GetType();
            var label   = $"{typeof(TRange).Name}.{variant.Name} {range}";

            var expected = JsonSerializer.Serialize(range, Registered);
            Assert.StartsWith("\"", expected, $"{label}: the registered converter did not write a string.");

            Assert.AreEqual(expected, JsonSerializer.Serialize(range, Unregistered),
                $"{label}: declared as the union, the unregistered wire form differs. Is the union missing "
              + "its [JsonConverter]?");
            Assert.AreEqual(expected, JsonSerializer.Serialize<object>(range, Unregistered),
                $"{label}: reached through object, the unregistered wire form differs. System.Text.Json "
              + "does not inherit [JsonConverter] — is the variant missing its own?");

            Assert.AreEqual(range, JsonSerializer.Deserialize<TRange>(expected, Unregistered),
                $"{label}: did not read back through the union.");
            Assert.AreEqual(range, JsonSerializer.Deserialize(expected, variant, Unregistered),
                $"{label}: did not read back through its variant.");

            var set         = RangeSet<TRange, T>.From([range]);
            var expectedSet = JsonSerializer.Serialize(set, Registered);
            Assert.AreEqual(expectedSet, JsonSerializer.Serialize(set, Unregistered),
                $"{label}: as a RangeSet, the unregistered wire form differs.");
            Assert.AreEqual(set, JsonSerializer.Deserialize<RangeSet<TRange, T>>(expectedSet, Unregistered),
                $"{label}: as a RangeSet, did not read back.");
        }
    }

    /// <summary>Every value set type, both arities, unregistered, writes its JSON array and reads back.</summary>
    [TestMethod]
    public void EverySetType_SerializesAsItsArray_WithoutRegistration()
    {
        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
            Reflect.InvokeGeneric(typeof(JsonDefaultContractTests), nameof(AssertSet), setType, elementType);
    }

    private static void AssertSet<TSet, T>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        foreach (var set in new[] { TSet.Empty, TSet.From(SetProbes.For<T>()) })
        {
            var label    = $"{typeof(TSet).Name} {set}";
            var expected = JsonSerializer.Serialize(set, Registered);

            Assert.StartsWith("[", expected, $"{label}: the registered converter did not write an array.");
            Assert.AreEqual(expected, JsonSerializer.Serialize(set, Unregistered),
                $"{label}: the unregistered wire form differs. Is the set type missing its [JsonConverter]?");
            Assert.AreEqual(set, JsonSerializer.Deserialize<TSet>(expected, Unregistered),
                $"{label}: did not read back.");
        }
    }
}

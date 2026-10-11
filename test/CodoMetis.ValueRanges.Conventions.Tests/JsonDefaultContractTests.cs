using System.Collections;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;

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

    /// <summary>The floor that keeps the loops below from passing by finding nothing.</summary>
    [TestMethod]
    public void Discovery_FindsEveryRangeAndSetType()
    {
        // 7 core + 4 NodaTime ranges; 10 core + 5 NodaTime set families, each in two arities.
        const int knownRangeTypes = 11;
        const int knownSetTypes   = 30;

        var ranges = RangeProbes.AllRangeTypes().Count();
        var sets   = SetProbes.AllSetTypes().Count();

        Assert.IsTrue(ranges >= knownRangeTypes, $"Range discovery found {ranges}, fewer than {knownRangeTypes}.");
        Assert.IsTrue(sets >= knownSetTypes, $"Set discovery found {sets}, fewer than {knownSetTypes}.");
    }

    [TestMethod]
    public void EveryRangeType_IsCoveredByBounds()
    {
        var uncovered = RangeProbes.AllRangeTypes()
                       .Where(pair => !RangeProbes.Bounds.ContainsKey(pair.Element))
                       .Select(pair => $"{pair.Range.Name} (element {pair.Element.Name})")
                       .ToList();

        Assert.AreEqual(
            0, uncovered.Count,
            $"No bounds registered for: {string.Join(", ", uncovered)}. Add a pair to RangeProbes.Bounds so the "
          + "wire-form test below exercises the new type's bounded shapes.");
    }

    /// <summary>
    /// Every shape of every range type, unregistered, writes the literal — through the union,
    /// through <see langword="object"/> (the variant's own converter), and inside a multirange —
    /// and reads back equal through each of the three declarations. Each is also a dictionary key,
    /// keyed by the union, by its variant and as a multirange, writing the same literal.
    /// </summary>
    [TestMethod]
    public void EveryRangeShape_SerializesAsItsLiteral_WithoutRegistration()
    {
        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
            Reflect.InvokeGeneric(typeof(JsonDefaultContractTests), nameof(AssertRangeShapes), rangeType, elementType);
    }

    private static void AssertRangeShapes<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        foreach (var range in RangeProbes.Shapes<TRange, T>())
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

            AssertKey(label, range, expected);
            AssertKey(label, set, expectedSet);

            // Keyed by the variant itself: the narrowing converter needs the overrides too.
            var byVariant = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(variant, typeof(int)))!;
            byVariant.Add(range, 1);
            var variantJson = JsonSerializer.Serialize(byVariant, byVariant.GetType(), Unregistered);
            Assert.AreEqual($"{{{expected}:1}}", variantJson, $"{label}: as a variant-typed key, the wire form differs.");
            var variantBack = (IDictionary)JsonSerializer.Deserialize(variantJson, byVariant.GetType(), Unregistered)!;
            Assert.AreEqual(range, variantBack.Keys.Cast<object>().Single(), $"{label}: as a variant-typed key, did not read back.");
        }
    }

    /// <summary>
    /// A converter registered on the options for a union reaches every one of its shapes, however
    /// the value is declared — the union, its variant, <see langword="object"/>, an
    /// <c>object</c> collection, a dictionary value or a variant-typed key. Before 9.0.0 it reached
    /// only union-typed declarations: the rest resolved to the variant's own attribute and wrote the
    /// literal, so one payload mixed two wire forms.
    /// </summary>
    [TestMethod]
    public void EveryRangeShape_HonoursAUnionConverterOnTheOptions()
    {
        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
            Reflect.InvokeGeneric(typeof(JsonDefaultContractTests), nameof(AssertUnionConverterReachesShapes), rangeType, elementType);
    }

    private static void AssertUnionConverterReachesShapes<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var options = new JsonSerializerOptions { Converters = { new TaggedRangeConverter<TRange, T>() } };

        foreach (var range in RangeProbes.Shapes<TRange, T>())
        {
            var variant  = range.GetType();
            var label    = $"{typeof(TRange).Name}.{variant.Name} {range}";
            var expected = JsonSerializer.Serialize(range, options);

            Assert.StartsWith("\"tag:", expected, $"{label}: declared as the union, the registered converter was not used.");
            Assert.AreEqual(expected, JsonSerializer.Serialize(range, variant, options), $"{label}: declared as its variant.");
            Assert.AreEqual(expected, JsonSerializer.Serialize<object>(range, options), $"{label}: through object.");
            Assert.AreEqual($"[{expected}]", JsonSerializer.Serialize(new List<object> { range }, options), $"{label}: in a List<object>.");
            Assert.AreEqual($"{{\"v\":{expected}}}",
                JsonSerializer.Serialize(new Dictionary<string, object> { ["v"] = range }, options), $"{label}: as a dictionary value.");

            Assert.AreEqual(range, JsonSerializer.Deserialize(expected, variant, options), $"{label}: did not read back through its variant.");

            var byVariant = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(variant, typeof(int)))!;
            byVariant.Add(range, 1);
            var variantJson = JsonSerializer.Serialize(byVariant, byVariant.GetType(), options);
            Assert.AreEqual($"{{{expected}:1}}", variantJson, $"{label}: as a variant-typed key.");
            Assert.AreEqual(range, ((IDictionary)JsonSerializer.Deserialize(variantJson, byVariant.GetType(), options)!).Keys.Cast<object>().Single(),
                $"{label}: as a variant-typed key, did not read back.");
        }
    }

    /// <summary>A custom wire form for any range type: its literal behind a prefix, keys included.</summary>
    private sealed class TaggedRangeConverter<TRange, T> : System.Text.Json.Serialization.JsonConverter<TRange>
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        public override TRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString()!);

        public override void Write(Utf8JsonWriter writer, TRange value, JsonSerializerOptions options)
            => writer.WriteStringValue($"tag:{value}");

        public override TRange ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Parse(reader.GetString()!);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, TRange value, JsonSerializerOptions options)
            => writer.WritePropertyName($"tag:{value}");

        private static TRange Parse(string s)
            => s.StartsWith("tag:", StringComparison.Ordinal)
                   ? TRange.Parse(s.AsSpan("tag:".Length), null)
                   : throw new JsonException($"Expected a tagged literal, got '{s}'.");
    }

    /// <summary>
    /// As a dictionary key the value is the same literal as its value form, and reads back.
    /// </summary>
    private static void AssertKey<TKey>(string label, TKey key, string expectedValueJson)
        where TKey : notnull
    {
        var keyed = new Dictionary<TKey, int> { [key] = 1 };

        var json = JsonSerializer.Serialize(keyed, Unregistered);

        Assert.AreEqual($"{{{expectedValueJson}:1}}", json,
            $"{label}: as a {typeof(TKey).Name} dictionary key, the property name is not the value literal.");
        Assert.AreEqual(key, JsonSerializer.Deserialize<Dictionary<TKey, int>>(json, Unregistered)!.Keys.Single(),
            $"{label}: as a {typeof(TKey).Name} dictionary key, did not read back.");
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

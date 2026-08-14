using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The pre-built named converters exist for <c>[JsonConverter(typeof(…))]</c> and for registering
/// a single type without the factory. Each is a one-line subclass, so what can go wrong is a
/// mismatched pair — <c>TimeSetJsonConverter</c> bound to the wrong family — which compiles as
/// long as the constraints happen to hold. These tests pin the binding rather than the behaviour,
/// which the generic converters already cover.
/// </summary>
[TestClass]
public class NamedJsonConverterTests
{
    /// <summary>Every exported converter whose name ends in <c>JsonConverter</c>.</summary>
    private static IEnumerable<Type> DeclaredConverters =>
        typeof(RangeJsonConverterFactory).Assembly
            .GetExportedTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false }
                     && t.Name.EndsWith("JsonConverter", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    public static IEnumerable<object[]> ConverterCases =>
        DeclaredConverters.Select(t => new object[] { t });

    /// <summary>The type the converter actually converts — the argument of its <c>JsonConverter&lt;T&gt;</c> base.</summary>
    private static Type ConvertedType(Type converterType)
    {
        for (var t = converterType; t is not null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(JsonConverter<>))
                return t.GetGenericArguments()[0];

        throw new InvalidOperationException($"{converterType} does not derive from JsonConverter<T>.");
    }

    /// <summary>
    /// The full inventory: one converter per range type, per multirange, and per non-generic set
    /// type. A new public range or set family without one shows up here as a count mismatch.
    /// </summary>
    [TestMethod]
    public void EveryFamilyHasExactlyOneNamedConverter()
    {
        var names = DeclaredConverters.Select(t => t.Name).ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "DateRangeJsonConverter", "DateRangeSetJsonConverter", "DateSetJsonConverter",
                "DateTimeOffsetRangeJsonConverter", "DateTimeOffsetRangeSetJsonConverter",
                "DateTimeOffsetSetJsonConverter", "DateTimeRangeJsonConverter",
                "DateTimeRangeSetJsonConverter", "DateTimeSetJsonConverter",
                "DecimalRangeJsonConverter", "DecimalRangeSetJsonConverter", "DecimalSetJsonConverter",
                "GuidSetJsonConverter", "Int16SetJsonConverter",
                "Int32RangeJsonConverter", "Int32RangeSetJsonConverter", "Int32SetJsonConverter",
                "Int64RangeJsonConverter", "Int64RangeSetJsonConverter", "Int64SetJsonConverter",
                "StringSetJsonConverter",
                "TimeRangeJsonConverter", "TimeRangeSetJsonConverter", "TimeSetJsonConverter"
            },
            names);
    }

    /// <summary>
    /// The converter's name must match the type it converts: <c>XJsonConverter</c> converts
    /// <c>X</c>, and <c>XRangeSetJsonConverter</c> converts <c>RangeSet&lt;XRange, T&gt;</c>.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(ConverterCases))]
    public void ConverterIsBoundToTheFamilyItIsNamedFor(Type converterType)
    {
        var converter = (JsonConverter)Activator.CreateInstance(converterType)!;
        var converted = ConvertedType(converterType);

        var expected = converterType.Name.EndsWith("RangeSetJsonConverter", StringComparison.Ordinal)
                           ? $"RangeSet`1[{converterType.Name[..^"RangeSetJsonConverter".Length]}Range]"
                           : converterType.Name[..^"JsonConverter".Length];

        var actual = converted.IsGenericType
                         ? $"RangeSet`1[{converted.GetGenericArguments()[0].Name}]"
                         : converted.Name;

        Assert.AreEqual(expected, actual, converterType.Name);
        Assert.IsTrue(converter.CanConvert(converted), $"{converterType.Name} cannot convert {converted}");
    }

    /// <summary>
    /// Registered on its own — the `[JsonConverter]`-attribute path — each converter must produce
    /// the same payload as the factory does.
    /// </summary>
    [TestMethod]
    [DynamicData(nameof(ConverterCases))]
    public void ConverterMatchesTheFactory(Type converterType)
    {
        var converted = ConvertedType(converterType);
        var sample    = Sample(converted);

        var alone = new JsonSerializerOptions();
        alone.Converters.Add((JsonConverter)Activator.CreateInstance(converterType)!);

        var viaFactory = JsonSerializer.Serialize(sample, converted, new JsonSerializerOptions().AddRangeConverters());
        var viaNamed   = JsonSerializer.Serialize(sample, converted, alone);

        Assert.AreEqual(viaFactory, viaNamed, converterType.Name);
        Assert.AreEqual(sample, JsonSerializer.Deserialize(viaNamed, converted, alone), converterType.Name);
    }

    /// <summary>A non-empty, non-default instance of a range, multirange or set type.</summary>
    private static object Sample(Type family)
    {
        if (family.IsGenericType)   // RangeSet<TRange, T>
        {
            var rangeType = family.GetGenericArguments()[0];
            var one       = family.GetMethod("From", [rangeType.MakeArrayType()])
                         ?? family.GetMethods().First(m => m.Name == "From" && m.GetParameters().Length == 1);
            var array     = Array.CreateInstance(rangeType, 1);
            array.SetValue(Sample(rangeType), 0);
            return one.Invoke(null, [array])!;
        }

        if (family.Name.EndsWith("Range", StringComparison.Ordinal))
        {
            var element = family.GetInterfaces()
                .First(i => i.IsGenericType && i.Name.StartsWith("IRangeFactory", StringComparison.Ordinal))
                .GetGenericArguments()[1];

            // CreateFinite's inclusivity flags are optional parameters, so the arity is always four.
            return family.GetMethod("CreateFinite", [element, element, typeof(bool), typeof(bool)])!
                         .Invoke(null, [ElementSample(element, 1), ElementSample(element, 2), true, true])!;
        }

        var elementType = family.GetInterfaces()
            .First(i => i.IsGenericType && i.Name.StartsWith("IValueSet`", StringComparison.Ordinal))
            .GetGenericArguments()[0];

        var values = Array.CreateInstance(elementType, 1);
        values.SetValue(ElementSample(elementType, 1), 0);
        return family.GetMethod("From", [elementType.MakeArrayType()])!.Invoke(null, [values])!;
    }

    private static object ElementSample(Type element, int seed) => element switch
    {
        _ when element == typeof(int)            => seed,
        _ when element == typeof(long)           => (long)seed,
        _ when element == typeof(short)          => (short)seed,
        _ when element == typeof(decimal)        => seed + 0.5m,
        _ when element == typeof(string)         => $"value{seed}",
        _ when element == typeof(Guid)           => new Guid($"{seed:D8}-0000-0000-0000-000000000000"),
        _ when element == typeof(DateOnly)       => new DateOnly(2024, seed, 1),
        _ when element == typeof(TimeOnly)       => new TimeOnly(seed, 30),
        _ when element == typeof(DateTime)       => new DateTime(2024, seed, 1, 8, 0, 0, DateTimeKind.Unspecified),
        _ when element == typeof(DateTimeOffset) => new DateTimeOffset(2024, seed, 1, 8, 0, 0, TimeSpan.Zero),
        _ => throw new NotSupportedException($"No sample for {element}.")
    };
}

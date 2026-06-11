using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// A <see cref="JsonConverterFactory"/> that automatically handles any type that implements
/// <see cref="IRangeFactory{TRange,T}"/> or is a <see cref="RangeSet{TRange,T}"/>.
/// Register once via <see cref="JsonSerializerOptions.Converters"/> to cover all range and
/// range set types without adding per-type converters explicitly.
/// </summary>
/// <example>
/// <code>
/// var options = new JsonSerializerOptions();
/// options.Converters.Add(new RangeJsonConverterFactory());
/// // or: options.AddRangeConverters();
/// </code>
/// </example>
public sealed class RangeJsonConverterFactory : JsonConverterFactory
{
    private static readonly Type RangeSetOpenType    = typeof(RangeSet<,>);
    private static readonly Type RangeFactoryOpenType = typeof(IRangeFactory<,>);

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => IsRangeSet(typeToConvert) || IsRangeType(typeToConvert);

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (IsRangeSet(typeToConvert))
        {
            var typeArgs      = typeToConvert.GetGenericArguments();
            var converterType = typeof(RangeSetJsonConverter<,>).MakeGenericType(typeArgs);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }

        var iface   = GetRangeFactoryInterface(typeToConvert)!;
        var tArg    = iface.GetGenericArguments()[1]; // T in IRangeFactory<TRange, T>
        var rangeConverter = typeof(RangeJsonConverter<,>).MakeGenericType(typeToConvert, tArg);
        return (JsonConverter)Activator.CreateInstance(rangeConverter)!;
    }

    private static bool IsRangeSet(Type t)
        => t.IsGenericType && t.GetGenericTypeDefinition() == RangeSetOpenType;

    private static bool IsRangeType(Type t)
        => GetRangeFactoryInterface(t) is not null;

    private static Type? GetRangeFactoryInterface(Type t)
        => Array.Find(t.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == RangeFactoryOpenType);
}

/// <summary>
/// Extension methods for registering range JSON converters.
/// </summary>
public static class RangeJsonSerializerOptionsExtensions
{
    /// <summary>
    /// Registers a <see cref="RangeJsonConverterFactory"/> that handles all range and range set types.
    /// </summary>
    public static JsonSerializerOptions AddRangeConverters(this JsonSerializerOptions options)
    {
        options.Converters.Add(new RangeJsonConverterFactory());
        return options;
    }
}

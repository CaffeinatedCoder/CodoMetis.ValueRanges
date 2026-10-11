using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// A <see cref="JsonConverterFactory"/> for every range, range set and value set type, and the range and set
/// interfaces (see <see cref="InterfaceDeclaredJsonConverter"/>). Since 9.0.0
/// each of those types carries its converter as a <c>[JsonConverter]</c> attribute, so registering
/// this factory is no longer needed; it remains valid and produces the same converters.
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
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => OwnConverterOf(typeToConvert) is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Nothing here builds a generic type at runtime, so the factory works under Native AOT and
    /// trimming. Every type it handles names its converter in its own <c>[JsonConverter]</c>: a
    /// non-generic type names a closed converter, which is created from that attribute; a generic
    /// type (<see cref="RangeSet{TRange,T}"/>, a validated-wrapper value set) names this factory,
    /// and its closed converter comes from the delegate the type registered for itself.
    /// </remarks>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converter = OwnConverterOf(typeToConvert)
                     ?? throw new NotSupportedException(
                            $"'{typeToConvert}' is not a range, range set or value set type of this package.");

        return converter == typeof(RangeJsonConverterFactory)
                   ? GenericConverters.Get(typeToConvert).Json()
                   : (JsonConverter)Activator.CreateInstance(converter)!;
    }

    /// <summary>
    /// The converter a type of this package names in its own <c>[JsonConverter]</c> — the type's
    /// own declaration, not an inherited one, since System.Text.Json does not inherit it either.
    /// </summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private static Type? OwnConverterOf(Type type)
        => type.GetCustomAttribute<JsonConverterAttribute>(inherit: false)?.ConverterType is { } converter
        && converter.Assembly == typeof(RangeJsonConverterFactory).Assembly
               ? converter
               : null;
}

/// <summary>
/// Extension methods for registering range JSON converters.
/// </summary>
public static class RangeJsonSerializerOptionsExtensions
{
    /// <summary>
    /// Registers a <see cref="RangeJsonConverterFactory"/> that handles all range, range set,
    /// and value set types.
    /// </summary>
    public static JsonSerializerOptions AddRangeConverters(this JsonSerializerOptions options)
    {
        options.Converters.Add(new RangeJsonConverterFactory());
        return options;
    }
}

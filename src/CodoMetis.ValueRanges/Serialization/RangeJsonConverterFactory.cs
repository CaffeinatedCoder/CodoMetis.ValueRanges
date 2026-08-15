using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// A <see cref="JsonConverterFactory"/> that automatically handles any type that implements
/// <see cref="IRangeFactory{TRange,T}"/> or <see cref="IValueSetFactory{TSet,T}"/>, or is a
/// <see cref="RangeSet{TRange,T}"/>. Register once via
/// <see cref="JsonSerializerOptions.Converters"/> to cover all range, range set, and value set
/// types without adding per-type converters explicitly.
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
    private static readonly Type RangeSetOpenType        = typeof(RangeSet<,>);
    private static readonly Type RangeFactoryOpenType    = typeof(IRangeFactory<,>);
    private static readonly Type ValueSetFactoryOpenType = typeof(IValueSetFactory<,>);

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
        => IsRangeSet(typeToConvert) || IsRangeType(typeToConvert) || IsValueSetType(typeToConvert);

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (IsRangeSet(typeToConvert))
        {
            var typeArgs      = typeToConvert.GetGenericArguments();
            var converterType = typeof(RangeSetJsonConverter<,>).MakeGenericType(typeArgs);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }

        if (GetFactoryInterface(typeToConvert, ValueSetFactoryOpenType) is { } setIface)
        {
            var setArgs     = setIface.GetGenericArguments(); // TSet, T in IValueSetFactory<TSet, T>
            var declaredSet = setArgs[0];

            if (declaredSet != typeToConvert)
                throw new NotSupportedException(
                    $"'{typeToConvert}' derives from the value set type '{declaredSet}' and cannot be "
                  + "serialized: only the type that declares IValueSetFactory<TSet, T> satisfies the "
                  + "converter's constraints. Value set types are meant to be sealed.");

            var setConverter = typeof(ValueSetJsonConverter<,>).MakeGenericType(typeToConvert, setArgs[1]);
            return (JsonConverter)Activator.CreateInstance(setConverter)!;
        }

        var iface  = GetFactoryInterface(typeToConvert, RangeFactoryOpenType)!;
        var args   = iface.GetGenericArguments(); // TRange, T in IRangeFactory<TRange, T>
        var union  = args[0];
        var tArg   = args[1];

        // The union's sealed variants inherit its IRangeFactory interface but do not satisfy
        // TRange : IRangeFactory<TRange, T> themselves, so they take the narrowing converter.
        var rangeConverter = union == typeToConvert
                                 ? typeof(RangeJsonConverter<,>).MakeGenericType(typeToConvert, tArg)
                                 : typeof(RangeVariantJsonConverter<,,>).MakeGenericType(typeToConvert, union, tArg);

        return (JsonConverter)Activator.CreateInstance(rangeConverter)!;
    }

    private static bool IsRangeSet(Type t)
        => t.IsGenericType && t.GetGenericTypeDefinition() == RangeSetOpenType;

    private static bool IsRangeType(Type t)
        => GetFactoryInterface(t, RangeFactoryOpenType) is not null;

    private static bool IsValueSetType(Type t)
        => GetFactoryInterface(t, ValueSetFactoryOpenType) is not null;

    private static Type? GetFactoryInterface(Type t, Type openFactoryType)
        => Array.Find(t.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == openFactoryType);
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

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// Serializes a value set as a plain JSON array, delegating element serialization to
/// <see cref="System.Text.Json"/> — element converters registered for
/// <typeparamref name="T"/> apply. Reads normalize to canonical form and reject
/// <see langword="null"/> elements.
/// </summary>
/// <remarks>
/// When <see cref="System.Text.Json"/> has no scalar converter for <typeparamref name="T"/> and
/// would fall back to writing it as an object of its properties, the set family's
/// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/> is used instead, if it defines one.
/// </remarks>
/// <typeparam name="TSet">The concrete set type.</typeparam>
/// <typeparam name="T">The element type of the set.</typeparam>
public class ValueSetJsonConverter<TSet, T> : JsonConverter<TSet>
    where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    /// <inheritdoc />
    public override TSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected a JSON array for {typeof(TSet).Name}.");

        var fallback = ResolveElementConverter(options);
        var strict   = ReadsThroughTheLiteralGrammar(options);
        var values   = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Null)
                throw new JsonException($"{typeof(TSet).Name} cannot contain null elements.");

            var element = strict
                              ? reader.TokenType == JsonTokenType.String
                                    ? ValueSetElementJson.Parse<TSet, T>(reader.GetString()!)
                                    : throw new JsonException($"Expected a JSON string for a {typeof(T).Name} element of {typeof(TSet).Name}.")
                              : fallback is null
                                  ? JsonSerializer.Deserialize(ref reader, ElementTypeInfo(options))
                                  : fallback.Read(ref reader, typeof(T), options);

            if (element is null)
                throw new JsonException($"{typeof(TSet).Name} cannot contain null elements.");

            values.Add(element);
        }

        try
        {
            return TSet.From(values);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            throw new JsonException($"Cannot construct {typeof(TSet).Name} from the JSON array.", ex);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TSet value, JsonSerializerOptions options)
    {
        var fallback = ResolveElementConverter(options);
        var info     = fallback is null ? ElementTypeInfo(options) : null;

        writer.WriteStartArray();
        foreach (var element in ((IValueSet<T>)value).Values)
        {
            if (fallback is null)
                JsonSerializer.Serialize(writer, element, info!);
            else
                fallback.Write(writer, element, options);
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// The element's contract, resolved through the options' own resolver — reflection-based or
    /// source-generated alike. Going through the typed overloads rather than
    /// <c>JsonSerializer.Serialize&lt;T&gt;(…, options)</c> is what keeps this converter free of the
    /// trimming and AOT annotations those carry; the behaviour is the same lookup.
    /// </summary>
    private static JsonTypeInfo<T> ElementTypeInfo(JsonSerializerOptions options)
        => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

    /// <summary>
    /// Returns the family's element converter when — and only when — System.Text.Json has
    /// nothing better. Anything it can already write as a scalar (a built-in converter, one
    /// registered on the options, or a <see cref="JsonConverterAttribute"/> on the element type)
    /// reports <see cref="JsonTypeInfoKind.None"/> and stays authoritative; only the reflection
    /// fallback, which would property-dump the element, hands over.
    /// </summary>
    /// <summary>
    /// Whether a BCL temporal element would be read by System.Text.Json's own converter — in which case
    /// it is read through the family's strict <see cref="IValueSetFactory{TSet,T}.ParseValue"/>
    /// instead, the grammar its literal uses.
    /// </summary>
    /// <remarks>
    /// System.Text.Json's <see cref="DateTimeOffset"/> converter reads an ISO timestamp without an
    /// offset — or a bare date — in the parsing machine's offset, so a <c>DateTimeOffsetSet</c> from the
    /// same JSON held different instants on different servers, while its literal refused the text. The
    /// write side is unchanged: System.Text.Json's output is ISO 8601 that the literal grammar reads.
    /// Compared by converter type, so a converter registered on the options — or one a source-generated
    /// context substitutes — still reads, as for every other element type.
    /// </remarks>
    private static bool ReadsThroughTheLiteralGrammar(JsonSerializerOptions options)
    {
        if (BuiltInTemporalConverter is not { } builtIn) return false;

        try
        {
            return options.GetTypeInfo(typeof(T)).Converter.GetType() == builtIn;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static readonly Type? BuiltInTemporalConverter =
        typeof(T) == typeof(DateTimeOffset) ? JsonMetadataServices.DateTimeOffsetConverter.GetType()
      : typeof(T) == typeof(DateTime)       ? JsonMetadataServices.DateTimeConverter.GetType()
      : typeof(T) == typeof(DateOnly)       ? JsonMetadataServices.DateOnlyConverter.GetType()
      : typeof(T) == typeof(TimeOnly)       ? JsonMetadataServices.TimeOnlyConverter.GetType()
      : null;

    private static JsonConverter<T>? ResolveElementConverter(JsonSerializerOptions options)
    {
        try
        {
            return options.GetTypeInfo(typeof(T)).Kind == JsonTypeInfoKind.None
                       ? null
                       : TSet.ElementJsonConverter;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // No contract resolvable for T. Leave the decision with System.Text.Json.
            return null;
        }
    }
}

// -----------------------------------------------------------------------
// Pre-built converters for each built-in set type
// -----------------------------------------------------------------------

/// <summary>JSON converter for <see cref="StringSet"/>.</summary>
public sealed class StringSetJsonConverter : ValueSetJsonConverter<StringSet, string>;

/// <summary>JSON converter for <see cref="GuidSet"/>.</summary>
public sealed class GuidSetJsonConverter : ValueSetJsonConverter<GuidSet, Guid>;

/// <summary>JSON converter for <see cref="Int16Set"/>.</summary>
public sealed class Int16SetJsonConverter : ValueSetJsonConverter<Int16Set, short>;

/// <summary>JSON converter for <see cref="Int32Set"/>.</summary>
public sealed class Int32SetJsonConverter : ValueSetJsonConverter<Int32Set, int>;

/// <summary>JSON converter for <see cref="Int64Set"/>.</summary>
public sealed class Int64SetJsonConverter : ValueSetJsonConverter<Int64Set, long>;

/// <summary>JSON converter for <see cref="DecimalSet"/>.</summary>
public sealed class DecimalSetJsonConverter : ValueSetJsonConverter<DecimalSet, decimal>;

/// <summary>JSON converter for <see cref="DateSet"/>.</summary>
public sealed class DateSetJsonConverter : ValueSetJsonConverter<DateSet, DateOnly>;

/// <summary>JSON converter for <see cref="TimeSet"/>.</summary>
public sealed class TimeSetJsonConverter : ValueSetJsonConverter<TimeSet, TimeOnly>;

/// <summary>JSON converter for <see cref="DateTimeSet"/>.</summary>
public sealed class DateTimeSetJsonConverter : ValueSetJsonConverter<DateTimeSet, DateTime>;

/// <summary>JSON converter for <see cref="DateTimeOffsetSet"/>.</summary>
public sealed class DateTimeOffsetSetJsonConverter : ValueSetJsonConverter<DateTimeOffsetSet, DateTimeOffset>;

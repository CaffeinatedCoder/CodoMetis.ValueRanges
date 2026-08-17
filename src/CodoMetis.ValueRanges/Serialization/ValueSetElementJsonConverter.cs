using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// Shared plumbing for the element converters a set family hands to
/// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>. Both forms route through the
/// family's own <see cref="IValueSetFactory{TSet,T}.ParseValue"/> and
/// <see cref="IValueSetFactory{TSet,T}.FormatValue"/>, so JSON, the PostgreSQL array literal and
/// the wire form share one text form — and reads re-run whatever validation
/// <see cref="IParsable{TSelf}"/> performs.
/// </summary>
internal static class ValueSetElementJson
{
    /// <summary>
    /// Whether the numeric converters must emit a JSON string rather than a number.
    /// </summary>
    /// <remarks>
    /// The options-level setting is the only one that can reach here:
    /// <see cref="JsonNumberHandlingAttribute"/> on a property is rejected by
    /// <see cref="System.Text.Json"/> itself for a type that is not a number or a collection of
    /// numbers, which a value set is not. Honouring it is what keeps a wrapper arity's payload
    /// identical to its primitive sibling's — the whole point of the numeric converters — and
    /// <c>WriteAsString</c> is normally switched on precisely because the consumer is JavaScript
    /// and a bare number above 2^53 would be rounded on arrival.
    /// </remarks>
    internal static bool WritesNumbersAsStrings(JsonSerializerOptions options)
        => (options.NumberHandling & JsonNumberHandling.WriteAsString) != 0;

    internal static T Parse<TSet, T>(string s)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        try
        {
            return TSet.ParseValue(s.AsSpan(), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            // Not chained: the BCL's format error embeds the whole offending text. The reason
            // survives as a bounded excerpt, which keeps a validated wrapper's own message.
            throw new JsonException(
                $"Cannot parse '{LiteralExcerpt.Of(s)}' as {typeof(T).Name}: {LiteralExcerpt.Of(ex.Message)}");
        }
    }
}

/// <summary>
/// Writes a set element as a JSON string holding its invariant text form — the shape used by
/// string- and Guid-backed families, whose backing primitives System.Text.Json also writes as
/// strings.
/// </summary>
/// <typeparam name="TSet">The value set family that owns the element's text form.</typeparam>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ValueSetTextElementJsonConverter<TSet, T> : JsonConverter<T>
    where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    /// <summary>The shared instance — the converter is stateless.</summary>
    internal static readonly ValueSetTextElementJsonConverter<TSet, T> Instance = new();

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()
             ?? throw new JsonException($"Expected a non-null JSON string for a {typeof(T).Name} value.");

        return ValueSetElementJson.Parse<TSet, T>(s);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(TSet.FormatValue(value, null, CultureInfo.InvariantCulture));
}

/// <summary>
/// Writes a set element as a JSON number — the shape used by the integer-backed families, so a
/// validated wrapper serializes identically to the primitive it wraps and swapping
/// <c>Int32Set</c> for <c>Int32Set&lt;TElement&gt;</c> does not change the payload.
/// </summary>
/// <remarks>
/// Both legs go through the family's text form, which is the wrapper contract: the element's
/// invariant text must be exactly the backing primitive's text. A wrapper that violates it —
/// by padding, prefixing, or formatting non-numerically — surfaces as a <see cref="JsonException"/>
/// naming the offending text rather than emitting malformed JSON. Reads accept a JSON string
/// unconditionally, and writes honour <see cref="JsonNumberHandling.WriteAsString"/> on the
/// options — without which a wrapper would emit a bare number where its primitive sibling emits a
/// string, which is how an <see cref="long"/> above 2^53 loses digits at a JavaScript consumer.
/// </remarks>
/// <typeparam name="TSet">The value set family that owns the element's text form.</typeparam>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ValueSetIntegerElementJsonConverter<TSet, T> : JsonConverter<T>
    where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    /// <summary>The shared instance — the converter is stateless.</summary>
    internal static readonly ValueSetIntegerElementJsonConverter<TSet, T> Instance = new();

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.String => reader.GetString()!,
            var other            => throw new JsonException(
                                        $"Expected a JSON number for a {typeof(T).Name} value, got {other}.")
        };

        return ValueSetElementJson.Parse<TSet, T>(text);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var text = TSet.FormatValue(value, null, CultureInfo.InvariantCulture);

        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            throw new JsonException(
                $"{typeof(T).Name} formats as '{text}', which is not an integer. An element of "
              + $"{typeof(TSet).Name} must format as exactly the text form of the primitive it wraps.");

        // The parsed number's text, not the element's: that is what the primitive sibling writes,
        // and parity with it is the contract.
        if (ValueSetElementJson.WritesNumbersAsStrings(options))
            writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
        else
            writer.WriteNumberValue(number);
    }
}

/// <summary>
/// Writes a set element as a JSON number with a fractional part — the shape used by
/// <c>DecimalSet&lt;TElement&gt;</c>. Separate from
/// <see cref="ValueSetIntegerElementJsonConverter{TSet,T}"/> rather than a widening of it:
/// that one reads and writes through <see cref="long"/> on both legs, which would silently
/// truncate every element of a decimal-backed wrapper.
/// </summary>
/// <remarks>
/// Both legs go through the family's text form, as in the integer converter, and
/// <see cref="JsonNumberHandling.WriteAsString"/> is honoured the same way.
/// Scale is preserved: <see cref="decimal"/> keeps trailing zeros through parse and format, so an
/// element formatting as <c>12.50</c> is written as <c>12.50</c> — the same text
/// <see cref="System.Text.Json"/> writes for the <see cref="decimal"/> it wraps.
/// </remarks>
/// <typeparam name="TSet">The value set family that owns the element's text form.</typeparam>
/// <typeparam name="T">The element type.</typeparam>
internal sealed class ValueSetDecimalElementJsonConverter<TSet, T> : JsonConverter<T>
    where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    /// <summary>The shared instance — the converter is stateless.</summary>
    internal static readonly ValueSetDecimalElementJsonConverter<TSet, T> Instance = new();

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType switch
        {
            // The raw token text, not GetDecimal().ToString(): a round trip through decimal
            // would renormalize the scale the payload was written with.
            JsonTokenType.Number => Encoding.UTF8.GetString(
                                        reader.HasValueSequence
                                            ? reader.ValueSequence.ToArray()
                                            : reader.ValueSpan),
            JsonTokenType.String => reader.GetString()!,
            var other            => throw new JsonException(
                                        $"Expected a JSON number for a {typeof(T).Name} value, got {other}.")
        };

        return ValueSetElementJson.Parse<TSet, T>(text);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var text = TSet.FormatValue(value, null, CultureInfo.InvariantCulture);

        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            throw new JsonException(
                $"{typeof(T).Name} formats as '{text}', which is not a decimal number. An element of "
              + $"{typeof(TSet).Name} must format as exactly the text form of the primitive it wraps.");

        // As in the integer converter — and the round trip through decimal keeps the scale, so
        // the string form carries the same digits the number form would have.
        if (ValueSetElementJson.WritesNumbersAsStrings(options))
            writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
        else
            writer.WriteNumberValue(number);
    }
}

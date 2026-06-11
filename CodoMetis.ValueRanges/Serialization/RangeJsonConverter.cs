using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// Serializes a range value as a PostgreSQL range literal JSON string, e.g. <c>"[1,5)"</c>,
/// <c>"empty"</c>, or <c>"(,)"</c>.
/// </summary>
/// <typeparam name="TRange">The concrete range type.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public class RangeJsonConverter<TRange, T> : JsonConverter<TRange>
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override TRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            throw new JsonException(
                $"Expected a JSON string for {typeof(TRange).Name}, got null. Use \"empty\" to represent an empty range.");

        var s = reader.GetString()
             ?? throw new JsonException($"Expected a non-null JSON string for a {typeof(TRange).Name} value.");

        if (!TRange.TryParse(s, CultureInfo.InvariantCulture, out var result))
            throw new JsonException($"Cannot parse '{s}' as {typeof(TRange).Name}.");

        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TRange value, JsonSerializerOptions options)
        => writer.WriteStringValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
}

/// <summary>
/// Serializes a <see cref="RangeSet{TRange,T}"/> as a PostgreSQL multirange literal JSON string,
/// e.g. <c>"{[1,5],[7,10]}"</c> or <c>"{}"</c> for the empty set.
/// </summary>
/// <typeparam name="TRange">The concrete range type.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public class RangeSetJsonConverter<TRange, T> : JsonConverter<RangeSet<TRange, T>>
    where TRange : IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <inheritdoc />
    public override RangeSet<TRange, T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()
             ?? throw new JsonException($"Expected a non-null JSON string for a RangeSet value.");

        try
        {
            var literals = RangeFormat.SplitSetLiterals(s.AsSpan());
            return RangeSet<TRange, T>.From(literals.Select(l => TRange.Parse(l, CultureInfo.InvariantCulture)));
        }
        catch (Exception ex)
        {
            throw new JsonException($"Cannot parse '{s}' as RangeSet<{typeof(TRange).Name}, {typeof(T).Name}>.", ex);
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RangeSet<TRange, T> value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(null, CultureInfo.InvariantCulture));
}

// -----------------------------------------------------------------------
// Pre-built converters for each built-in range type
// -----------------------------------------------------------------------

/// <summary>JSON converter for <see cref="Int32Range"/>.</summary>
public sealed class Int32RangeJsonConverter : RangeJsonConverter<Int32Range, int>;

/// <summary>JSON converter for <see cref="Int64Range"/>.</summary>
public sealed class Int64RangeJsonConverter : RangeJsonConverter<Int64Range, long>;

/// <summary>JSON converter for <see cref="DecimalRange"/>.</summary>
public sealed class DecimalRangeJsonConverter : RangeJsonConverter<DecimalRange, decimal>;

/// <summary>JSON converter for <see cref="DateRange"/>.</summary>
public sealed class DateRangeJsonConverter : RangeJsonConverter<DateRange, DateOnly>;

/// <summary>JSON converter for <see cref="DateTimeRange"/>.</summary>
public sealed class DateTimeRangeJsonConverter : RangeJsonConverter<DateTimeRange, DateTime>;

/// <summary>JSON converter for <see cref="DateTimeOffsetRange"/>.</summary>
public sealed class DateTimeOffsetRangeJsonConverter : RangeJsonConverter<DateTimeOffsetRange, DateTimeOffset>;

/// <summary>JSON converter for <see cref="RangeSet{TRange,T}"/> of <see cref="Int32Range"/>.</summary>
public sealed class Int32RangeSetJsonConverter : RangeSetJsonConverter<Int32Range, int>;

/// <summary>JSON converter for <see cref="RangeSet{TRange,T}"/> of <see cref="Int64Range"/>.</summary>
public sealed class Int64RangeSetJsonConverter : RangeSetJsonConverter<Int64Range, long>;

/// <summary>JSON converter for <see cref="RangeSet{TRange,T}"/> of <see cref="DecimalRange"/>.</summary>
public sealed class DecimalRangeSetJsonConverter : RangeSetJsonConverter<DecimalRange, decimal>;

/// <summary>JSON converter for <see cref="RangeSet{TRange,T}"/> of <see cref="DateRange"/>.</summary>
public sealed class DateRangeSetJsonConverter : RangeSetJsonConverter<DateRange, DateOnly>;

/// <summary>JSON converter for <see cref="RangeSet{TRange,T}"/> of <see cref="DateTimeRange"/>.</summary>
public sealed class DateTimeRangeSetJsonConverter : RangeSetJsonConverter<DateTimeRange, DateTime>;

/// <summary>JSON converter for <see cref="RangeSet{TRange,T}"/> of <see cref="DateTimeOffsetRange"/>.</summary>
public sealed class DateTimeOffsetRangeSetJsonConverter : RangeSetJsonConverter<DateTimeOffsetRange, DateTimeOffset>;
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
/// <remarks>
/// <para>
/// <c>null</c> is left to System.Text.Json in both directions, as for any other reference type:
/// a null property writes as <c>null</c> and reads back as <c>null</c>. It stays distinct from
/// the empty range, which is the literal <c>"empty"</c> — an absent value and an empty interval
/// are different facts and have different wire forms.
/// </para>
/// <para>
/// This converter previously declared <c>HandleNull</c> so that <see cref="Read"/> could reject
/// a null token with a directed message. The cost was that the package could not read a document
/// it had just written: a null <c>Int32Range?</c> property serialized to <c>null</c> and threw
/// <see cref="JsonException"/> on the way back in, so an API could return a payload it was unable
/// to accept. The message was not worth that, and it guarded against a confusion the obvious
/// behaviour does not create — reading null yields null, never the empty range.
/// </para>
/// </remarks>
/// <typeparam name="TRange">The concrete range type.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public class RangeJsonConverter<TRange, T> : JsonConverter<TRange>
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <inheritdoc />
    public override TRange Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()
             ?? throw new JsonException($"Expected a non-null JSON string for a {typeof(TRange).Name} value.");

        if (!TRange.TryParse(s, CultureInfo.InvariantCulture, out var result))
            throw new JsonException($"Cannot parse '{s}' as {typeof(TRange).Name}.");

        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TRange value, JsonSerializerOptions options)
    {
        // System.Text.Json writes null itself and does not call this converter for it. The guard
        // is kept so that re-declaring HandleNull could never reintroduce the dereference it
        // caused before 6.1.0.
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Serializes one sealed variant of a range union — <c>Int32Range.Finite</c>,
/// <c>Int32Range.EmptyRange</c>, … — to the same literal string as the union itself.
/// </summary>
/// <remarks>
/// <para>
/// System.Text.Json resolves converters by the type it is handed, which for a value reached
/// through <see langword="object"/>, an <c>object</c>-typed collection, or a variant-typed
/// declaration is the variant rather than the union. Only the union satisfies
/// <c>TRange : IRangeFactory&lt;TRange, T&gt;</c>, so the variant needs its own converter; it
/// parses through the union and narrows.
/// </para>
/// <para>
/// A literal that parses to a different variant is a read error, not a silent widening:
/// <c>"empty"</c> cannot be read into a declaration of type <c>Finite</c>.
/// </para>
/// </remarks>
/// <typeparam name="TVariant">The sealed variant type being converted.</typeparam>
/// <typeparam name="TRange">The range union that declares the variant.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public class RangeVariantJsonConverter<TVariant, TRange, T> : JsonConverter<TVariant>
    where TVariant : TRange
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <inheritdoc />
    public override TVariant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString()
             ?? throw new JsonException($"Expected a non-null JSON string for a {typeof(TVariant).Name} value.");

        if (!TRange.TryParse(s, CultureInfo.InvariantCulture, out var parsed))
            throw new JsonException($"Cannot parse '{s}' as {typeof(TRange).Name}.");

        if (parsed is not TVariant variant)
            throw new JsonException(
                $"Cannot read '{s}' as {typeof(TRange).Name}.{typeof(TVariant).Name}: "
              + $"the literal is a {parsed!.GetType().Name}.");

        return variant;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TVariant value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
    }
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

/// <summary>JSON converter for <see cref="TimeRange"/>.</summary>
public sealed class TimeRangeJsonConverter : RangeJsonConverter<TimeRange, TimeOnly>;

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

/// <summary>JSON converter for <see cref="RangeSet{TRange,T}"/> of <see cref="TimeRange"/>.</summary>
public sealed class TimeRangeSetJsonConverter : RangeSetJsonConverter<TimeRange, TimeOnly>;
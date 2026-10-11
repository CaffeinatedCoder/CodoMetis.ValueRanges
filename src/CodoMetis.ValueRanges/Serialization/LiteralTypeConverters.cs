using System.ComponentModel;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges.Serialization;

/// <summary>
/// The <see cref="TypeConverter"/> shape shared by every type in this package: to and from
/// <see cref="string"/> through the PostgreSQL literal, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TypeConverter"/> is what <c>Microsoft.Extensions.Configuration</c> binding and
/// <see cref="TypeDescriptor"/>-based tooling such as a property grid consult. Without one,
/// configuration binding of a range property yielded <see langword="null"/> with no error.
/// (ASP.NET Core model binding does not need it: minimal APIs and MVC bind through the static
/// <c>TryParse</c> every type here already has.)
/// </para>
/// <para>
/// The <see cref="CultureInfo"/> argument is ignored: the literal is a wire format, the text
/// PostgreSQL itself reads and writes, so it is always parsed and formatted invariantly. Honouring
/// the culture would make <c>[1.5,2.5)</c> parse differently, or not at all, depending on the
/// caller's language.
/// </para>
/// <para>
/// An empty string converts to <see langword="null"/>, as an absent value, never to the empty
/// range, which is the literal <c>empty</c>. A malformed literal throws
/// <see cref="FormatException"/> carrying a bounded excerpt of the input.
/// </para>
/// </remarks>
/// <typeparam name="TValue">The converted type.</typeparam>
public abstract class LiteralTypeConverterBase<TValue> : TypeConverter
    where TValue : class
{
    private protected LiteralTypeConverterBase()
    {
    }

    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string s
               ? s.Length == 0 ? null : Parse(s)
               : base.ConvertFrom(context, culture, value);

    /// <inheritdoc />
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => destinationType == typeof(string) && value is TValue typed
               ? ((IFormattable)typed).ToString(null, CultureInfo.InvariantCulture)
               : base.ConvertTo(context, culture, value, destinationType);

    private protected abstract TValue Parse(string s);
}

/// <summary>Converts a range type to and from its PostgreSQL range literal, e.g. <c>[1,5)</c>.</summary>
/// <typeparam name="TRange">The range union.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public class RangeTypeConverter<TRange, T> : LiteralTypeConverterBase<TRange>
    where TRange : class, IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    private protected override TRange Parse(string s) => TRange.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>
/// Converts one sealed variant of a range union to and from the same literal as the union,
/// refusing a literal of a different shape.
/// </summary>
/// <remarks>
/// Unlike <c>[JsonConverter]</c>, <c>[TypeConverter]</c> is inherited, so without this a variant
/// would get the union's converter — and a configuration value of <c>empty</c> bound to a
/// <c>Finite</c>-typed property would produce an <c>EmptyRange</c> the property cannot hold.
/// </remarks>
/// <typeparam name="TVariant">The sealed variant type.</typeparam>
/// <typeparam name="TRange">The range union that declares the variant.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public class RangeVariantTypeConverter<TVariant, TRange, T> : LiteralTypeConverterBase<TVariant>
    where TVariant : class, TRange
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    private protected override TVariant Parse(string s)
    {
        var parsed = TRange.Parse(s, CultureInfo.InvariantCulture);

        return parsed as TVariant
            ?? throw new FormatException(
                   $"Cannot read '{LiteralExcerpt.Of(s)}' as {typeof(TRange).Name}.{typeof(TVariant).Name}: "
                 + $"the literal is a {parsed.GetType().Name}.");
    }
}

/// <summary>
/// Converts a <see cref="RangeSet{TRange,T}"/> to and from its PostgreSQL multirange literal,
/// e.g. <c>{[1,5],[7,10]}</c>.
/// </summary>
/// <typeparam name="TRange">The range type.</typeparam>
/// <typeparam name="T">The element type of the ranges.</typeparam>
public class RangeSetTypeConverter<TRange, T> : LiteralTypeConverterBase<RangeSet<TRange, T>>
    where TRange : IRangeFactory<TRange, T>, IRange<T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    private protected override RangeSet<TRange, T> Parse(string s)
        => RangeSet<TRange, T>.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>
/// Converts a value set to and from its PostgreSQL array literal, e.g. <c>{alpha,beta}</c>.
/// </summary>
/// <typeparam name="TSet">The value set type.</typeparam>
/// <typeparam name="T">The element type.</typeparam>
public class ValueSetTypeConverter<TSet, T> : LiteralTypeConverterBase<TSet>
    where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    private protected override TSet Parse(string s) => TSet.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>
/// The <see cref="TypeConverter"/> named by the generic types — <see cref="RangeSet{TRange,T}"/>
/// and the validated-wrapper value set arities — which an attribute cannot close over their type
/// arguments. <see cref="TypeDescriptor"/> constructs it with the closed type, and it delegates to
/// that type's <see cref="RangeSetTypeConverter{TRange,T}"/> or <see cref="ValueSetTypeConverter{TSet,T}"/>.
/// </summary>
public sealed class LiteralTypeConverter : TypeConverter
{
    private readonly TypeConverter _inner;

    /// <summary>Creates the converter for <paramref name="type"/>, a closed range set or value set type.</summary>
    /// <param name="type">The type being converted.</param>
    /// <exception cref="NotSupportedException"><paramref name="type"/> is neither.</exception>
    /// <remarks>
    /// The closed converter comes from the delegate the type registered for itself, never from
    /// <c>MakeGenericType</c>, so this works under Native AOT.
    /// </remarks>
    public LiteralTypeConverter(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        _inner = GenericConverters.Get(type).Text();
    }

    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => _inner.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => _inner.CanConvertTo(context, destinationType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => _inner.ConvertFrom(context, culture, value);

    /// <inheritdoc />
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => _inner.ConvertTo(context, culture, value, destinationType);
}

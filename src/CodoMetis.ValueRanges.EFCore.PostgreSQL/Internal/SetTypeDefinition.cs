using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Storage;
using Microsoft.EntityFrameworkCore.Storage;

namespace CodoMetis.ValueRanges.EntityFrameworkCore.PostgreSQL.Internal;

/// <summary>
/// The identity implementation of <see cref="ISetTypeDefinition"/> for set types whose element
/// CLR type is itself the primitive store representation (e.g. <c>StringSet</c>/<c>string</c>,
/// <c>DateSet</c>/<see cref="DateOnly"/>). Given only the element store type name (and an
/// optional element normalization for the way to the database), it derives everything else.
/// </summary>
/// <typeparam name="TSet">The set type, e.g. <c>StringSet</c>.</typeparam>
/// <typeparam name="T">The element type, which is also the primitive store representation.</typeparam>
internal sealed class SetTypeDefinition<TSet, T> : ISetTypeDefinition
    where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    internal SetTypeDefinition(
        string           elementStoreType,
        Func<T, T>?      normalizeValue = null,
        Func<T, string>? literalText    = null
    )
    {
        ElementStoreType = elementStoreType;
        ArrayStoreType   = elementStoreType + "[]";
        SetTypeMapping   = new ValueSetTypeMapping<TSet, T, T>(
            ArrayStoreType,
            normalizeValue ?? (static value => value),
            static value => value,
            literalText);

        // The same normalization has to reach a bare element operand: in `column @> ARRAY[@p]`
        // the probe bypasses the set mapping entirely, and the provider's default mapping for
        // the element CLR type does not normalize. Without this, a probe that From would have
        // normalized binds raw — a non-ISO NodaTime date silently queries for its own
        // calendar's field values read as ISO.
        ElementTypeMapping = normalizeValue is null
                                 ? null
                                 : new BridgedElementTypeMapping<T, T>(
                                     elementStoreType, normalizeValue, static value => value, literalText);
    }

    public Type SetClrType => typeof(TSet);

    public Type ElementClrType => typeof(T);

    public string ElementStoreType { get; }

    public string ArrayStoreType { get; }

    public RelationalTypeMapping SetTypeMapping { get; }

    public RelationalTypeMapping? ElementTypeMapping { get; }

    public object EmptySet => TSet.Empty;
}

/// <summary>
/// The <see cref="ISetTypeDefinition"/> for string-backed validated-wrapper set instantiations
/// (<c>StringSet&lt;TElement&gt;</c>): elements bridge to <c>text</c> through their
/// <see cref="IFormattable"/>/<see cref="IParsable{TSelf}"/> surface, and reads re-run the
/// element's validation.
/// </summary>
internal sealed class StringBridgedSetTypeDefinition<TSet, TElement> : ISetTypeDefinition
    where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
    where TElement : struct, IEquatable<TElement>, IFormattable, IParsable<TElement>
{
    private static string ToPrimitive(TElement element)
        => element.ToString(null, CultureInfo.InvariantCulture);

    private static TElement FromPrimitive(string primitive)
        => TElement.Parse(primitive, CultureInfo.InvariantCulture);

    public Type SetClrType => typeof(TSet);

    public Type ElementClrType => typeof(TElement);

    public string ElementStoreType => "text";

    public string ArrayStoreType => "text[]";

    public RelationalTypeMapping SetTypeMapping { get; } = new ValueSetTypeMapping<TSet, TElement, string>(
        "text[]", ToPrimitive, FromPrimitive);

    public RelationalTypeMapping ElementTypeMapping { get; } = new BridgedElementTypeMapping<TElement, string>(
        "text", ToPrimitive, FromPrimitive);

    public object EmptySet => TSet.Empty;
}

/// <summary>
/// The <see cref="ISetTypeDefinition"/> for validated-wrapper set instantiations backed by a
/// struct primitive (<c>GuidSet&lt;TElement&gt;</c>, <c>Int32Set&lt;TElement&gt;</c>,
/// <c>DateTimeSet&lt;TElement&gt;</c>, the NodaTime arities, …): the bridge goes through a text
/// form in both directions, which is lossless exactly when the element's text form is the
/// backing primitive's text form — the documented contract. A decorative element format fails
/// loudly here with an error naming the type and the contract.
/// </summary>
/// <remarks>
/// <para>
/// The text form is the family's, not the element's default. The <c>elementFormat</c> argument
/// is what the element's <see cref="IFormattable"/> is asked for, and it is the same specifier
/// the primitive-backed sibling's <c>FormatValue</c> defaults to, so a set's array literal, its
/// JSON and this bridge all agree. A <see langword="null"/> format is right only where the
/// element's default already round-trips: <see cref="Guid"/> and the integers. It is wrong for
/// every temporal — <c>TimeOnly</c> renders as <c>09:30</c> and <c>DateTime</c> as
/// <c>06/15/2024 10:30:00</c> with a null format, dropping sub-seconds and
/// <see cref="DateTimeKind"/> silently.
/// </para>
/// <para>
/// The primitive legs are delegates rather than <see cref="IParsable{TSelf}"/>: NodaTime's
/// value types do not implement it at all, and <c>DateTime.Parse</c> reaches through it without
/// <see cref="DateTimeStyles.RoundtripKind"/>, which rewrites a UTC element to
/// <see cref="DateTimeKind.Local"/> on the way to the parameter.
/// </para>
/// </remarks>
/// <typeparam name="TSet">The closed wrapper set type, e.g. <c>Int32Set&lt;OrderId&gt;</c>.</typeparam>
/// <typeparam name="TElement">The validated wrapper element type.</typeparam>
/// <typeparam name="TPrimitive">The primitive store representation of one element.</typeparam>
internal sealed class BridgedSetTypeDefinition<TSet, TElement, TPrimitive> : ISetTypeDefinition
    where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>
    where TElement : struct, IEquatable<TElement>, IFormattable, IParsable<TElement>
    where TPrimitive : struct
{
    private readonly string?                  _elementFormat;
    private readonly Func<string, TPrimitive> _parsePrimitive;
    private readonly Func<TPrimitive, string> _formatPrimitive;

    /// <param name="elementStoreType">The PostgreSQL type of one element, e.g. <c>integer</c>.</param>
    /// <param name="elementFormat">
    /// The format specifier handed to the element's <see cref="IFormattable"/>, or
    /// <see langword="null"/> to take its default.
    /// </param>
    /// <param name="parsePrimitive">The element's text form to the store primitive.</param>
    /// <param name="formatPrimitive">
    /// The store primitive back to text the element's <see cref="IParsable{TSelf}"/> accepts.
    /// Not necessarily the literal form — <c>YearMonthSet&lt;T&gt;</c> stores a first-of-month
    /// <c>date</c> and hands its element <c>2024-06</c>.
    /// </param>
    /// <param name="literalText">
    /// The primitive's SQL literal form; defaults to <see cref="SetProviderText.Of"/>.
    /// </param>
    public BridgedSetTypeDefinition(
        string                    elementStoreType,
        string?                   elementFormat,
        Func<string, TPrimitive>  parsePrimitive,
        Func<TPrimitive, string>  formatPrimitive,
        Func<TPrimitive, string>? literalText = null
    )
    {
        _elementFormat   = elementFormat;
        _parsePrimitive  = parsePrimitive;
        _formatPrimitive = formatPrimitive;

        ElementStoreType = elementStoreType;
        ArrayStoreType   = elementStoreType + "[]";
        SetTypeMapping   = new ValueSetTypeMapping<TSet, TElement, TPrimitive>(
            ArrayStoreType, ToPrimitive, FromPrimitive, literalText);
        ElementTypeMapping = new BridgedElementTypeMapping<TElement, TPrimitive>(
            ElementStoreType, ToPrimitive, FromPrimitive, literalText);
    }

    private TPrimitive ToPrimitive(TElement element)
    {
        var text = element.ToString(_elementFormat, CultureInfo.InvariantCulture);
        try
        {
            return _parsePrimitive(text);
        }
        catch (FormatException ex)
        {
            // NodaTime's UnparsableValueException derives from FormatException, so the
            // satellite's patterns surface the same message rather than an opaque one.
            var asked = _elementFormat is null ? "invariant" : $"'{_elementFormat}'";

            throw new InvalidOperationException(
                $"The {asked} text form '{text}' of '{typeof(TElement)}' is not a valid "
              + $"'{typeof(TPrimitive)}' value. Value set elements must format to exactly the "
              + "backing primitive's text form.", ex);
        }
    }

    private TElement FromPrimitive(TPrimitive primitive)
        => TElement.Parse(_formatPrimitive(primitive), CultureInfo.InvariantCulture);

    public Type SetClrType => typeof(TSet);

    public Type ElementClrType => typeof(TElement);

    public string ElementStoreType { get; }

    public string ArrayStoreType { get; }

    public RelationalTypeMapping SetTypeMapping { get; }

    public RelationalTypeMapping ElementTypeMapping { get; }

    public object EmptySet => TSet.Empty;
}

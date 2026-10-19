using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of finite single-precision numbers — the value-set counterpart of a
/// PostgreSQL <c>real[]</c> (<c>float4[]</c>) column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, sorted by numeric order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// <para>
/// Elements are finite: <see cref="float.NaN"/> and the infinities throw <see cref="ArgumentException"/>
/// from <c>From</c>, <c>Contains</c>, <c>Add</c> and <c>Remove</c>, and fail every parse. PostgreSQL sorts
/// <c>NaN</c> above every number where .NET sorts it below, so a set holding one would be in a different
/// order on each side. <c>-0</c> and <c>0</c> are equal on both sides; deduplication keeps the first
/// representation in input order, as <c>DecimalSet</c> does for <c>1.0</c> and <c>1.00</c>.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(Float32Set), nameof(From))]
[JsonConverter(typeof(ValueSetJsonConverter<Float32Set, float>))]
[TypeConverter(typeof(ValueSetTypeConverter<Float32Set, float>))]
public sealed class Float32Set : IValueSet<float>, IValueSetFactory<Float32Set, float>, IEquatable<Float32Set>
{
    private readonly ImmutableArray<float> _elements;

    private Float32Set(ImmutableArray<float> elements) => _elements = elements;

    private static float RequireFinite(float value) => FloatLiteral.RequireFinite(value, nameof(Float32Set), nameof(value));

    // Contains, Add and Remove refuse the probe From refuses: against the empty set nothing is
    // compared, so a NaN probe would answer false and Add would store it.
    float IValueSet<float>.NormalizeElement(float value) => RequireFinite(value);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static Float32Set Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    /// <exception cref="ArgumentException">An element is <c>NaN</c> or an infinity.</exception>
    public static Float32Set From(IEnumerable<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromTrusted(ValueSetCore.Canonicalize(values.Select(RequireFinite), Comparer<float>.Default));
    }

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    /// <exception cref="ArgumentException">An element is <c>NaN</c> or an infinity.</exception>
    public static Float32Set From(params ReadOnlySpan<float> values)
    {
        foreach (var value in values) RequireFinite(value);
        return FromTrusted(ValueSetCore.Canonicalize(values, Comparer<float>.Default));
    }

    /// <summary>Collection-expression builder for <see cref="Float32Set{TElement}"/>.</summary>
    public static Float32Set<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => Float32Set<TElement>.From(values);

    internal static Float32Set FromTrusted(ImmutableArray<float> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Float32Set IValueSetFactory<Float32Set, float>.FromTrusted(ImmutableArray<float> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by numeric order.</summary>
    public ImmutableArray<float> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public float this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<float>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    /// <remarks>PostgreSQL's <c>float4in</c> grammar for a finite number, refused where it would be out of
    /// range there (<see cref="FloatLiteral"/>); the provider is ignored.</remarks>
    public static float ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => FloatLiteral.ParseSingle(s);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.FormatValue"/>
    /// <remarks>The text PostgreSQL's <c>float4out</c> prints (<see cref="FloatLiteral"/>), rather than
    /// .NET's shortest form; an explicit format applies as for any number.</remarks>
    public static string FormatValue(float value, string? format, IFormatProvider? provider)
        => string.IsNullOrEmpty(format) ? FloatLiteral.Format(value) : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{1.5,2.25}</c>, <c>{}</c>) into a
    /// <see cref="Float32Set"/>, normalizing to canonical form.
    /// </summary>
    public static Float32Set Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<Float32Set, float>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static Float32Set Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<Float32Set, float>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="Float32Set"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Float32Set result)
        => SetFormat.TryParse<Float32Set, float>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Float32Set result)
        => SetFormat.TryParse<Float32Set, float>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(Float32Set? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Float32Set);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(Float32Set)"/>.</summary>
    public static bool operator ==(Float32Set? left, Float32Set? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(Float32Set? left, Float32Set? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => SetFormat.Format<Float32Set, float>(this, null, CultureInfo.InvariantCulture);

    // Implemented here rather than left to the interface's default: ILCompiler 11 RC2 compiles the
    // default's shared-generic stub to a trap under Native AOT (#291).
    string IFormattable.ToString(string? format, IFormatProvider? provider)
        => SetFormat.Format<Float32Set, float>(this, format, provider);
}

/// <summary>
/// An immutable, canonical set of validated float-backed elements (e.g. scores or measurements)
/// — the value-set counterpart of a PostgreSQL <c>real[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators provide out of the box. The contract that cannot be expressed in constraints:
/// <em>the element's invariant text form must be exactly the backing <see cref="float"/>'s
/// text form</em> — a decorative format fails loudly at the persistence boundary.
/// </para>
/// <para>
/// The family asks its elements for <c>G9</c>, not their default: <c>G9</c> reads back as the same float
/// for every value, which .NET guarantees of no shorter form (its default does on every float sampled, but
/// the double sibling's default does not for two values, and the families pin the same way). The array literal and JSON then carry the text PostgreSQL prints, exactly as
/// <see cref="Float32Set"/> writes it. Elements wrapping <c>NaN</c> or an infinity are refused, as there.
/// Canonical order is the element's own <see cref="IComparable{T}"/>.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="float"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(Float32Set), nameof(Float32Set.From))]
[JsonConverter(typeof(RangeJsonConverterFactory))]
[TypeConverter(typeof(LiteralTypeConverter))]
public sealed class Float32Set<TElement> :
    IValueSet<TElement>, IValueSetFactory<Float32Set<TElement>, TElement>, IEquatable<Float32Set<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    // Hands the closed converters to the factory without reflection; see GenericConverters.
    internal static readonly bool ConvertersRegistered = GenericConverters.Register(
        typeof(Float32Set<TElement>),
        static () => new ValueSetJsonConverter<Float32Set<TElement>, TElement>(),
        static () => new ValueSetTypeConverter<Float32Set<TElement>, TElement>());

    /// <summary>
    /// The format the family asks its elements for: <c>G9</c>, which reads back as the same float for
    /// every value. See the class remarks.
    /// </summary>
    private const string RoundTripFormat = "G9";

    private readonly ImmutableArray<TElement> _elements;

    private Float32Set(ImmutableArray<TElement> elements) => _elements = elements;

    /// <summary>
    /// <paramref name="value"/>, unless the float it wraps is <c>NaN</c> or an infinity. Read through the
    /// element's own <c>G9</c> text; an element whose text is not a number at all is left to the
    /// text-form contract (<see cref="WrapperElement"/>), which refuses it when it is written.
    /// </summary>
    private static TElement RequireFinite(TElement value)
        => float.TryParse(value.ToString(RoundTripFormat, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var primitive)
        && !float.IsFinite(primitive)
               ? throw new ArgumentException(
                     $"Float32Set<{typeof(TElement).Name}> holds finite numbers only; this element wraps {primitive.ToString(CultureInfo.InvariantCulture)}.",
                     nameof(value))
               : value;

    // Contains, Add and Remove refuse the probe From refuses.
    TElement IValueSet<TElement>.NormalizeElement(TElement value) => RequireFinite(value);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static Float32Set<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    /// <exception cref="ArgumentException">An element wraps <c>NaN</c> or an infinity.</exception>
    public static Float32Set<TElement> From(IEnumerable<TElement> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromTrusted(ValueSetCore.Canonicalize(values.Select(RequireFinite), Comparer<TElement>.Default));
    }

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    /// <exception cref="ArgumentException">An element wraps <c>NaN</c> or an infinity.</exception>
    public static Float32Set<TElement> From(params ReadOnlySpan<TElement> values)
    {
        foreach (var value in values) RequireFinite(value);
        return FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));
    }

    internal static Float32Set<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Float32Set<TElement> IValueSetFactory<Float32Set<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by the element's <see cref="IComparable{T}"/>.</summary>
    public ImmutableArray<TElement> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public TElement this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<TElement>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <summary>The family's element parser, unchecked: the grammar, then the element's own <c>Parse</c>. See <see cref="WrapperElement"/>.</summary>
    /// <remarks>
    /// The element's own <c>Parse</c> never sees the raw text: it is handed the parsed float's <c>G9</c>
    /// text, so the grammar is <see cref="Float32Set.ParseValue"/>'s whatever the wrapper's own would be.
    /// </remarks>
    private static TElement ParseElement(string s)
        => TElement.Parse(
            Float32Set.ParseValue(s, CultureInfo.InvariantCulture).ToString(RoundTripFormat, CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses an element with the family's grammar — PostgreSQL's <c>float4in</c> for a finite number,
    /// exactly as <see cref="Float32Set.ParseValue"/> reads it — and hands the element the parsed
    /// float's <c>G9</c> text via <see cref="IParsable{TSelf}"/>, which re-runs its validation:
    /// corrupt data throws rather than materializing. Checked: the element must format back to text
    /// that reads as the same element (<see cref="WrapperElement"/>, <c>FormatException</c>).
    /// </summary>
    public static TElement ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => WrapperElement.Parsed(ParseElement(s.ToString()), RoundTripFormat, ParseElement);

    /// <summary>
    /// Formats an element as the text PostgreSQL prints for the float it wraps — the same text
    /// <see cref="Float32Set"/> writes — after checking that its <c>G9</c> text reads back as the
    /// same element. An explicit format applies to the element as for any number.
    /// </summary>
    public static string FormatValue(TElement value, string? format, IFormatProvider? provider)
        => string.IsNullOrEmpty(format)
               ? Float32Set.FormatValue(
                   float.Parse(WrapperElement.Formatted(value, RoundTripFormat, ParseElement, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                   null, CultureInfo.InvariantCulture)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The JSON fallback for elements that carry no converter of their own: a JSON number
    /// holding the same text form as <see cref="FormatValue"/>, so the wrapper serializes
    /// exactly like the primitive it wraps and <see cref="ParseValue"/> re-runs its validation
    /// on the way in. Consulted only when nothing else has claimed
    /// <typeparamref name="TElement"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<TElement>? IValueSetFactory<Float32Set<TElement>, TElement>.ElementJsonConverter
        => ValueSetFloatElementJsonConverter<Float32Set<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="Float32Set{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static Float32Set<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<Float32Set<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static Float32Set<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<Float32Set<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="Float32Set{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Float32Set<TElement> result)
        => SetFormat.TryParse<Float32Set<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Float32Set<TElement> result)
        => SetFormat.TryParse<Float32Set<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(Float32Set<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Float32Set<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(Float32Set{TElement})"/>.</summary>
    public static bool operator ==(Float32Set<TElement>? left, Float32Set<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(Float32Set<TElement>? left, Float32Set<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => SetFormat.Format<Float32Set<TElement>, TElement>(this, null, CultureInfo.InvariantCulture);

    // Implemented here rather than left to the interface's default: ILCompiler 11 RC2 compiles the
    // default's shared-generic stub to a trap under Native AOT (#291).
    string IFormattable.ToString(string? format, IFormatProvider? provider)
        => SetFormat.Format<Float32Set<TElement>, TElement>(this, format, provider);
}

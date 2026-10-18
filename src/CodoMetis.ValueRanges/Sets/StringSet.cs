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
/// An immutable, canonical set of <see cref="string"/> values — the value-set counterpart of a
/// PostgreSQL <c>text[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated and sorted with <see cref="StringComparer.Ordinal"/> — is
/// enforced at construction. Because every writer produces the identical array for the same
/// set, SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(StringSet), nameof(From))]
[JsonConverter(typeof(ValueSetJsonConverter<StringSet, string>))]
[TypeConverter(typeof(ValueSetTypeConverter<StringSet, string>))]
public sealed class StringSet : IValueSet<string>, IValueSetFactory<StringSet, string>, IEquatable<StringSet>
{
    private readonly ImmutableArray<string> _elements;

    private StringSet(ImmutableArray<string> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static StringSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    /// <exception cref="ArgumentException">An element holds a NUL character, which PostgreSQL <c>text</c> cannot.</exception>
    public static StringSet From(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromTrusted(ValueSetCore.Canonicalize(values.Select(TextElement.Require), CanonicalComparer));
    }

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    /// <exception cref="ArgumentException">An element holds a NUL character, which PostgreSQL <c>text</c> cannot.</exception>
    public static StringSet From(params ReadOnlySpan<string> values)
    {
        foreach (var value in values) TextElement.Require(value);
        return FromTrusted(ValueSetCore.Canonicalize(values, CanonicalComparer));
    }

    // Contains, Add and Remove refuse the element From would refuse — see TextElement.
    string IValueSet<string>.NormalizeElement(string value) => TextElement.Require(value);

    /// <summary>Collection-expression builder for <see cref="StringSet{TElement}"/>.</summary>
    public static StringSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IFormattable, IParsable<TElement>
        => StringSet<TElement>.From(values);

    internal static StringSet FromTrusted(ImmutableArray<string> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static StringSet IValueSetFactory<StringSet, string>.FromTrusted(ImmutableArray<string> elements)
        => FromTrusted(elements);

    /// <summary>
    /// Canonical order: <see cref="StringComparer.Ordinal"/>. Never a culture-sensitive
    /// comparison — canonical form is a cross-writer storage contract, not a display order.
    /// </summary>
    public static IComparer<string> CanonicalComparer => StringComparer.Ordinal;

    IComparer<string> IValueSet<string>.CanonicalOrder => CanonicalComparer;

    /// <summary>The canonical elements: deduplicated, ordinal-sorted.</summary>
    public ImmutableArray<string> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public string this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<string>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    /// <remarks>Text is its own literal — <c>textin</c> applies no grammar and trims nothing — except that a
    /// NUL character is refused, which <c>text</c> cannot hold; see <see cref="TextElement"/>.</remarks>
    public static string ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TextElement.RequireParsable(s).ToString();

    /// <summary>Formats an element — the identity function for strings.</summary>
    public static string FormatValue(string value, string? format, IFormatProvider? provider) => value;

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{a,b}</c>, <c>{"a b"}</c>, <c>{}</c>)
    /// into a <see cref="StringSet"/>, normalizing to canonical form.
    /// </summary>
    public static StringSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<StringSet, string>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static StringSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<StringSet, string>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="StringSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out StringSet result)
        => SetFormat.TryParse<StringSet, string>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out StringSet result)
        => SetFormat.TryParse<StringSet, string>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(StringSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StringSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(StringSet)"/>.</summary>
    public static bool operator ==(StringSet? left, StringSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(StringSet? left, StringSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated string-backed elements — the value-set counterpart
/// of a PostgreSQL <c>text[]</c> column whose elements are domain values such as typed keys.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators (Vogen, Metalama aspects, StronglyTypedId, or hand-written wrappers) provide out
/// of the box: <see cref="IFormattable"/> supplies the element's backing text, and
/// <see cref="IParsable{TSelf}"/> re-validates on the way in — so reading corrupt data throws.
/// The contract that cannot be expressed in constraints: <em>the element's invariant text form
/// must be exactly the backing primitive's text form</em>.
/// </para>
/// <para>
/// <typeparamref name="TElement"/> must be a <c>struct</c>, which is stricter than the machinery
/// needs — the core is constrained only on <see cref="IEquatable{T}"/>, and the closed
/// <see cref="StringSet"/> over <see cref="string"/> runs through it. The reason is the canonical
/// array: it is sorted and binary-searched, and a reference element would leave the caller holding
/// a handle to something inside it. Mutating that silently unsorts the array, and
/// <c>Contains</c> then misses an element it holds. A struct makes it unrepresentable rather than
/// forbidden by a comment. A class-shaped domain type wraps in a <c>readonly record struct</c> for
/// storage.
/// </para>
/// <para>
/// Canonical order is ordinal over the invariant text form — deliberately not the element's
/// own <see cref="IComparable{T}"/>, whose generated implementations typically delegate to
/// culture-sensitive string comparison.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a string.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(StringSet), nameof(StringSet.From))]
[JsonConverter(typeof(RangeJsonConverterFactory))]
[TypeConverter(typeof(LiteralTypeConverter))]
public sealed class StringSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<StringSet<TElement>, TElement>, IEquatable<StringSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IFormattable, IParsable<TElement>
{
    // Hands the closed converters to the factory without reflection; see GenericConverters.
    internal static readonly bool ConvertersRegistered = GenericConverters.Register(
        typeof(StringSet<TElement>),
        static () => new ValueSetJsonConverter<StringSet<TElement>, TElement>(),
        static () => new ValueSetTypeConverter<StringSet<TElement>, TElement>());

    /// <summary>
    /// The format the family asks its elements for: their default, which is the backing primitive's
    /// own text form — the contract <see cref="WrapperElement"/> checks.
    /// </summary>
    private const string? RoundTripFormat = null;

    private readonly ImmutableArray<TElement> _elements;

    private StringSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static StringSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    /// <exception cref="ArgumentException">An element's text form holds a NUL character, which PostgreSQL <c>text</c> cannot.</exception>
    public static StringSet<TElement> From(IEnumerable<TElement> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromTrusted(ValueSetCore.Canonicalize(values.Select(RequireText), CanonicalComparer));
    }

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    /// <exception cref="ArgumentException">An element's text form holds a NUL character, which PostgreSQL <c>text</c> cannot.</exception>
    public static StringSet<TElement> From(params ReadOnlySpan<TElement> values)
    {
        foreach (var value in values) RequireText(value);
        return FromTrusted(ValueSetCore.Canonicalize(values, CanonicalComparer));
    }

    // The element's text form is what the column holds, so the text rule applies to it — see TextElement.
    private static TElement RequireText(TElement value)
    {
        TextElement.Require(value.ToString(null, CultureInfo.InvariantCulture));
        return value;
    }

    // Contains, Add and Remove refuse the element From would refuse.
    TElement IValueSet<TElement>.NormalizeElement(TElement value) => RequireText(value);

    internal static StringSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static StringSet<TElement> IValueSetFactory<StringSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
        => FromTrusted(elements);

    /// <summary>
    /// Canonical order: ordinal over the element's invariant text form (see the class remarks).
    /// </summary>
    public static IComparer<TElement> CanonicalComparer { get; } = Comparer<TElement>.Create(
        static (x, y) => string.CompareOrdinal(
            x.ToString(null, CultureInfo.InvariantCulture),
            y.ToString(null, CultureInfo.InvariantCulture)));

    IComparer<TElement> IValueSet<TElement>.CanonicalOrder => CanonicalComparer;

    /// <summary>The canonical elements: deduplicated, sorted ordinal over the invariant text form.</summary>
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

    /// <summary>
    /// Parses an element via <see cref="IParsable{TSelf}"/>, which re-runs the element's
    /// validation — corrupt data throws rather than materializing. The raw text reaches it, as
    /// <c>textin</c> applies no grammar; only a NUL character is refused first (<see cref="TextElement"/>).
    /// </summary>
    /// <summary>The family's element parser, unchecked: the grammar, then the element's own <c>Parse</c>. See <see cref="WrapperElement"/>.</summary>
    private static TElement ParseElement(string s)
        => TElement.Parse(TextElement.RequireParsable(s).ToString(), CultureInfo.InvariantCulture);

    /// <summary>
    /// Checked: the element the text parsed to must format back to text that reads as the same
    /// element, or the read fails (<see cref="WrapperElement"/>, <c>FormatException</c>).
    /// </summary>
    public static TElement ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => WrapperElement.Parsed(ParseElement(s.ToString()), RoundTripFormat, ParseElement);

    /// <summary>Formats an element via <see cref="IFormattable"/> — its backing text form.</summary>
    public static string FormatValue(TElement value, string? format, IFormatProvider? provider)
        => string.IsNullOrEmpty(format)
               ? WrapperElement.Formatted(value, RoundTripFormat, ParseElement, provider)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The JSON fallback for elements that carry no converter of their own: a JSON string holding
    /// the same text form as <see cref="FormatValue"/>, so the wrapper serializes exactly like the
    /// primitive it wraps and <see cref="ParseValue"/> re-runs its validation on the way in.
    /// Consulted only when nothing else has claimed <typeparamref name="TElement"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<TElement>? IValueSetFactory<StringSet<TElement>, TElement>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<StringSet<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{users.read,users.write}</c>, <c>{}</c>)
    /// into a <see cref="StringSet{TElement}"/>, re-validating every element and normalizing
    /// to canonical form.
    /// </summary>
    public static StringSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<StringSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static StringSet<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<StringSet<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="StringSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out StringSet<TElement> result)
        => SetFormat.TryParse<StringSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out StringSet<TElement> result)
        => SetFormat.TryParse<StringSet<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(StringSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StringSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(StringSet{TElement})"/>.</summary>
    public static bool operator ==(StringSet<TElement>? left, StringSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(StringSet<TElement>? left, StringSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

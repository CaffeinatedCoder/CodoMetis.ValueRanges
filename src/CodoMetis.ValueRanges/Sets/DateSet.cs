using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of calendar dates — the value-set counterpart of a
/// PostgreSQL <c>date[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, sorted by chronological order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(DateSet), nameof(From))]
public sealed class DateSet : IValueSet<DateOnly>, IValueSetFactory<DateSet, DateOnly>, IEquatable<DateSet>
{
    private readonly ImmutableArray<DateOnly> _elements;

    private DateSet(ImmutableArray<DateOnly> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static DateSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateSet From(IEnumerable<DateOnly> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<DateOnly>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateSet From(params ReadOnlySpan<DateOnly> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<DateOnly>.Default));

    /// <summary>Collection-expression builder for <see cref="DateSet{TElement}"/>.</summary>
    public static DateSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => DateSet<TElement>.From(values);

    internal static DateSet FromTrusted(ImmutableArray<DateOnly> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateSet IValueSetFactory<DateSet, DateOnly>.FromTrusted(ImmutableArray<DateOnly> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<DateOnly> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public DateOnly this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<DateOnly>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static DateOnly ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => DateOnly.Parse(s, provider ?? CultureInfo.InvariantCulture);

    /// <summary>Formats a <see cref="DateOnly"/> value using ISO 8601 (<c>yyyy-MM-dd</c>) by default.</summary>
    public static string FormatValue(DateOnly value, string? format, IFormatProvider? provider)
        => value.ToString(format ?? "yyyy-MM-dd", provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-01-01,2024-12-24}</c>, <c>{}</c>) into a
    /// <see cref="DateSet"/>, normalizing to canonical form.
    /// </summary>
    public static DateSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<DateSet, DateOnly>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static DateSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<DateSet, DateOnly>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateSet result)
        => SetFormat.TryParse<DateSet, DateOnly>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateSet result)
        => SetFormat.TryParse<DateSet, DateOnly>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(DateSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DateSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(DateSet)"/>.</summary>
    public static bool operator ==(DateSet? left, DateSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(DateSet? left, DateSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated date-backed elements (e.g. a domain-specific
/// business date) — the value-set counterpart of a PostgreSQL <c>date[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators provide out of the box. The contract that cannot be expressed in constraints:
/// <em>the element's ISO 8601 (<c>yyyy-MM-dd</c>) text form must be exactly the backing
/// <see cref="DateOnly"/>'s</em>. The family asks its elements for that format rather than
/// their default, because a <see cref="DateOnly"/>'s default invariant form is
/// <c>06/15/2024</c>, which is not what the sibling <see cref="DateSet"/> writes. An element
/// that ignores the format specifier is rejected at the persistence boundary rather than stored
/// truncated.
/// </para>
/// <para>
/// Canonical order is the element's own <see cref="IComparable{T}"/>, which for generated
/// wrappers delegates to the backing <see cref="DateOnly"/>.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="DateOnly"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(DateSet), nameof(DateSet.From))]
public sealed class DateSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<DateSet<TElement>, TElement>, IEquatable<DateSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private DateSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <summary>
    /// The format the family asks its elements for — ISO 8601 (<c>yyyy-MM-dd</c>) — so the
    /// array literal, JSON and the EF Core bridge all agree on one text form. See the class
    /// remarks for why the element's own default will not do.
    /// </summary>
    private const string RoundTripFormat = "yyyy-MM-dd";

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static DateSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateSet<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateSet<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static DateSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateSet<TElement> IValueSetFactory<DateSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
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

    /// <summary>
    /// Parses an element via <see cref="IParsable{TSelf}"/>, which re-runs the element's
    /// validation — corrupt data throws rather than materializing.
    /// </summary>
    public static TElement ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TElement.Parse(s.ToString(), provider ?? CultureInfo.InvariantCulture);

    /// <summary>Formats an element using ISO 8601 (<c>yyyy-MM-dd</c>) by default — see the
    /// class remarks.</summary>
    public static string FormatValue(TElement value, string? format, IFormatProvider? provider)
        => value.ToString(format ?? RoundTripFormat, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The JSON fallback for elements that carry no converter of their own: a JSON string
    /// holding the same text form as <see cref="FormatValue"/>, so the wrapper serializes
    /// exactly like the primitive it wraps and <see cref="ParseValue"/> re-runs its validation
    /// on the way in. Consulted only when nothing else has claimed
    /// <typeparamref name="TElement"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<TElement>? IValueSetFactory<DateSet<TElement>, TElement>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<DateSet<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="DateSet{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static DateSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<DateSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static DateSet<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<DateSet<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateSet<TElement> result)
        => SetFormat.TryParse<DateSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateSet<TElement> result)
        => SetFormat.TryParse<DateSet<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(DateSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DateSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(DateSet{TElement})"/>.</summary>
    public static bool operator ==(DateSet<TElement>? left, DateSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(DateSet<TElement>? left, DateSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

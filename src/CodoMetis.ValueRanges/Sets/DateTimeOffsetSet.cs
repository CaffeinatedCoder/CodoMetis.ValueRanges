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
/// An immutable, canonical set of instants — the value-set counterpart of a
/// PostgreSQL <c>timestamp with time zone[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, sorted by instant order (UTC ticks) — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// Equality and canonical order are instant-based — same-instant values with different
/// offsets deduplicate. The EF Core satellite normalizes to UTC at the provider boundary,
/// mirroring <see cref="DateTimeOffsetRange"/>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(DateTimeOffsetSet), nameof(From))]
public sealed class DateTimeOffsetSet : IValueSet<DateTimeOffset>, IValueSetFactory<DateTimeOffsetSet, DateTimeOffset>, IEquatable<DateTimeOffsetSet>
{
    private readonly ImmutableArray<DateTimeOffset> _elements;

    private DateTimeOffsetSet(ImmutableArray<DateTimeOffset> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static DateTimeOffsetSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateTimeOffsetSet From(IEnumerable<DateTimeOffset> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<DateTimeOffset>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateTimeOffsetSet From(params ReadOnlySpan<DateTimeOffset> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<DateTimeOffset>.Default));

    /// <summary>Collection-expression builder for <see cref="DateTimeOffsetSet{TElement}"/>.</summary>
    public static DateTimeOffsetSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => DateTimeOffsetSet<TElement>.From(values);

    internal static DateTimeOffsetSet FromTrusted(ImmutableArray<DateTimeOffset> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateTimeOffsetSet IValueSetFactory<DateTimeOffsetSet, DateTimeOffset>.FromTrusted(ImmutableArray<DateTimeOffset> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by instant order (UTC ticks).</summary>
    public ImmutableArray<DateTimeOffset> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public DateTimeOffset this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<DateTimeOffset>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static DateTimeOffset ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => DateTimeOffset.Parse(s, provider ?? CultureInfo.InvariantCulture);

    /// <summary>Formats a <see cref="DateTimeOffset"/> value using the round-trip format specifier (<c>O</c>) by default, preserving full precision and UTC offset.</summary>
    public static string FormatValue(DateTimeOffset value, string? format, IFormatProvider? provider)
        => value.ToString(format ?? "O", provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-01-01T08:00:00+00:00}</c>, <c>{}</c>) into a
    /// <see cref="DateTimeOffsetSet"/>, normalizing to canonical form.
    /// </summary>
    public static DateTimeOffsetSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<DateTimeOffsetSet, DateTimeOffset>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static DateTimeOffsetSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<DateTimeOffsetSet, DateTimeOffset>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateTimeOffsetSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeOffsetSet result)
        => SetFormat.TryParse<DateTimeOffsetSet, DateTimeOffset>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTimeOffsetSet result)
        => SetFormat.TryParse<DateTimeOffsetSet, DateTimeOffset>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(DateTimeOffsetSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DateTimeOffsetSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(DateTimeOffsetSet)"/>.</summary>
    public static bool operator ==(DateTimeOffsetSet? left, DateTimeOffsetSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(DateTimeOffsetSet? left, DateTimeOffsetSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated instant-backed elements — the value-set counterpart
/// of a PostgreSQL <c>timestamp with time zone[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators provide out of the box. The contract that cannot be expressed in constraints:
/// <em>the element's round-trip (<c>O</c>) text form must be exactly the backing
/// <see cref="DateTimeOffset"/>'s</em>. The family asks its elements for that format rather
/// than their default, because a <see cref="DateTimeOffset"/>'s default invariant form is
/// <c>06/15/2024 10:30:00 +02:00</c>, which silently drops the fractional seconds. An element
/// that ignores the format specifier is rejected at the persistence boundary rather than stored
/// truncated.
/// </para>
/// <para>
/// Canonical order is the element's own <see cref="IComparable{T}"/>, and — as for
/// <see cref="DateTimeOffsetSet"/> — the EF Core satellite normalizes elements to UTC at the
/// provider boundary, which preserves the instant.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="DateTimeOffset"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(DateTimeOffsetSet), nameof(DateTimeOffsetSet.From))]
public sealed class DateTimeOffsetSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<DateTimeOffsetSet<TElement>, TElement>, IEquatable<DateTimeOffsetSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private DateTimeOffsetSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <summary>
    /// The format the family asks its elements for — round-trip (<c>O</c>) — so the array
    /// literal, JSON and the EF Core bridge all agree on one text form. See the class remarks
    /// for why the element's own default will not do.
    /// </summary>
    private const string RoundTripFormat = "O";

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static DateTimeOffsetSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateTimeOffsetSet<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateTimeOffsetSet<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static DateTimeOffsetSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateTimeOffsetSet<TElement> IValueSetFactory<DateTimeOffsetSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
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

    /// <summary>Formats an element using the round-trip format specifier (<c>O</c>) by default
    /// — see the class remarks.</summary>
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
    static JsonConverter<TElement>? IValueSetFactory<DateTimeOffsetSet<TElement>, TElement>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<DateTimeOffsetSet<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="DateTimeOffsetSet{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static DateTimeOffsetSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<DateTimeOffsetSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static DateTimeOffsetSet<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<DateTimeOffsetSet<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateTimeOffsetSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeOffsetSet<TElement> result)
        => SetFormat.TryParse<DateTimeOffsetSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTimeOffsetSet<TElement> result)
        => SetFormat.TryParse<DateTimeOffsetSet<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(DateTimeOffsetSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DateTimeOffsetSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(DateTimeOffsetSet{TElement})"/>.</summary>
    public static bool operator ==(DateTimeOffsetSet<TElement>? left, DateTimeOffsetSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(DateTimeOffsetSet<TElement>? left, DateTimeOffsetSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

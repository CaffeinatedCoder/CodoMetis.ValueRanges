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
/// An immutable, canonical set of timestamps — the value-set counterpart of a
/// PostgreSQL <c>timestamp without time zone[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, sorted by chronological order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// Canonical order and equality compare ticks and ignore <see cref="DateTimeKind"/>;
/// the EF Core satellite normalizes to <see cref="DateTimeKind.Unspecified"/> at the
/// provider boundary, mirroring <see cref="DateTimeRange"/>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(DateTimeSet), nameof(From))]
public sealed class DateTimeSet : IValueSet<DateTime>, IValueSetFactory<DateTimeSet, DateTime>, IEquatable<DateTimeSet>
{
    private readonly ImmutableArray<DateTime> _elements;

    private DateTimeSet(ImmutableArray<DateTime> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static DateTimeSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateTimeSet From(IEnumerable<DateTime> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<DateTime>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateTimeSet From(params ReadOnlySpan<DateTime> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<DateTime>.Default));

    /// <summary>Collection-expression builder for <see cref="DateTimeSet{TElement}"/>.</summary>
    public static DateTimeSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => DateTimeSet<TElement>.From(values);

    internal static DateTimeSet FromTrusted(ImmutableArray<DateTime> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateTimeSet IValueSetFactory<DateTimeSet, DateTime>.FromTrusted(ImmutableArray<DateTime> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<DateTime> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public DateTime this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<DateTime>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static DateTime ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => DateTime.Parse(s, provider ?? CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Formats a <see cref="DateTime"/> value using the round-trip format specifier (<c>O</c>) by default, preserving full precision and <see cref="DateTimeKind"/>.</summary>
    public static string FormatValue(DateTime value, string? format, IFormatProvider? provider)
        => value.ToString(format ?? "O", provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-01-01T08:00:00,2024-01-02T08:00:00}</c>, <c>{}</c>) into a
    /// <see cref="DateTimeSet"/>, normalizing to canonical form.
    /// </summary>
    public static DateTimeSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<DateTimeSet, DateTime>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static DateTimeSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<DateTimeSet, DateTime>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateTimeSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeSet result)
        => SetFormat.TryParse<DateTimeSet, DateTime>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTimeSet result)
        => SetFormat.TryParse<DateTimeSet, DateTime>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(DateTimeSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DateTimeSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(DateTimeSet)"/>.</summary>
    public static bool operator ==(DateTimeSet? left, DateTimeSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(DateTimeSet? left, DateTimeSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated timestamp-backed elements — the value-set
/// counterpart of a PostgreSQL <c>timestamp without time zone[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators provide out of the box. The contract that cannot be expressed in constraints:
/// <em>the element's round-trip (<c>O</c>) text form must be exactly the backing
/// <see cref="DateTime"/>'s</em>. The family asks its elements for that format rather than
/// their default, because a <see cref="DateTime"/>'s default invariant form is
/// <c>06/15/2024 10:30:00</c>, which silently drops the fractional seconds and the
/// <see cref="DateTimeKind"/>. An element that ignores the format specifier is rejected at the
/// persistence boundary rather than stored truncated.
/// </para>
/// <para>
/// Canonical order and equality compare ticks through the element's own
/// <see cref="IComparable{T}"/>, and — as for <see cref="DateTimeSet"/> — the EF Core satellite
/// normalizes elements to <see cref="DateTimeKind.Unspecified"/> at the provider boundary,
/// because PostgreSQL <c>timestamp</c> carries no time zone.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="DateTime"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(DateTimeSet), nameof(DateTimeSet.From))]
public sealed class DateTimeSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<DateTimeSet<TElement>, TElement>, IEquatable<DateTimeSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private DateTimeSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <summary>
    /// The format the family asks its elements for — round-trip (<c>O</c>) — so the array
    /// literal, JSON and the EF Core bridge all agree on one text form. See the class remarks
    /// for why the element's own default will not do.
    /// </summary>
    private const string RoundTripFormat = "O";

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static DateTimeSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateTimeSet<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DateTimeSet<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static DateTimeSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateTimeSet<TElement> IValueSetFactory<DateTimeSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
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
    static JsonConverter<TElement>? IValueSetFactory<DateTimeSet<TElement>, TElement>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<DateTimeSet<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="DateTimeSet{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static DateTimeSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<DateTimeSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static DateTimeSet<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<DateTimeSet<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateTimeSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeSet<TElement> result)
        => SetFormat.TryParse<DateTimeSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTimeSet<TElement> result)
        => SetFormat.TryParse<DateTimeSet<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(DateTimeSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DateTimeSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(DateTimeSet{TElement})"/>.</summary>
    public static bool operator ==(DateTimeSet<TElement>? left, DateTimeSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(DateTimeSet<TElement>? left, DateTimeSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

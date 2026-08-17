using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;
using NodaTime.Text;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of <see cref="LocalTime"/> values — the value-set counterpart of a
/// PostgreSQL <c>time[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// Canonical form — deduplicated, sorted by chronological order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </para>
/// <para>
/// <c>time[]</c> is a built-in PostgreSQL array type — unlike <c>timerange</c>, no
/// <c>CREATE TYPE</c> is needed. PostgreSQL <c>time</c>'s special value <c>24:00:00</c> is not
/// representable in <see cref="LocalTime"/> — the same caveat as <see cref="TimeRange"/>.
/// </para>
/// <para>
/// System.Text.Json serialization produces ISO 8601 element strings with no setup beyond the
/// converter factory: System.Text.Json has no built-in converter for NodaTime types, so the
/// family supplies its own through
/// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>. Registering an element converter
/// yourself takes precedence — <c>AddNodaTimeRangeConverters()</c> does that, and additionally
/// covers bare elements outside a set.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(LocalTimeSet), nameof(From))]
public sealed class LocalTimeSet : IValueSet<LocalTime>, IValueSetFactory<LocalTimeSet, LocalTime>, IEquatable<LocalTimeSet>
{
    private readonly ImmutableArray<LocalTime> _elements;

    private LocalTimeSet(ImmutableArray<LocalTime> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static LocalTimeSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalTimeSet From(IEnumerable<LocalTime> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<LocalTime>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalTimeSet From(params ReadOnlySpan<LocalTime> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<LocalTime>.Default));

    /// <summary>Collection-expression builder for <see cref="LocalTimeSet{TElement}"/>.</summary>
    public static LocalTimeSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => LocalTimeSet<TElement>.From(values);

    internal static LocalTimeSet FromTrusted(ImmutableArray<LocalTime> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static LocalTimeSet IValueSetFactory<LocalTimeSet, LocalTime>.FromTrusted(ImmutableArray<LocalTime> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<LocalTime> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public LocalTime this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<LocalTime>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static LocalTime ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseTime(s.ToString());

    /// <summary>
    /// Formats a <see cref="LocalTime"/> value using ISO 8601 (<c>HH:mm:ss</c> with optional subseconds) by default.
    /// </summary>
    public static string FormatValue(LocalTime value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.Time.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The JSON fallback for <see cref="LocalTime"/> elements: the same ISO 8601 text form as
    /// <see cref="FormatValue"/>, so JSON, array literals and the wire form agree. Consulted only
    /// when no converter has claimed <see cref="LocalTime"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<LocalTime>? IValueSetFactory<LocalTimeSet, LocalTime>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<LocalTimeSet, LocalTime>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{09:00:00,17:30:00}</c>, <c>{}</c>) into a
    /// <see cref="LocalTimeSet"/>, normalizing to canonical form.
    /// </summary>
    public static LocalTimeSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<LocalTimeSet, LocalTime>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static LocalTimeSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<LocalTimeSet, LocalTime>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="LocalTimeSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out LocalTimeSet result)
        => SetFormat.TryParse<LocalTimeSet, LocalTime>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out LocalTimeSet result)
        => SetFormat.TryParse<LocalTimeSet, LocalTime>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(LocalTimeSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LocalTimeSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(LocalTimeSet)"/>.</summary>
    public static bool operator ==(LocalTimeSet? left, LocalTimeSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(LocalTimeSet? left, LocalTimeSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated time-of-day-backed elements — the value-set
/// counterpart of a PostgreSQL <c>time without time zone[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators provide out of the box. The contract that cannot be expressed in constraints:
/// <em>the element's extended ISO 8601 (<c>HH:mm:ss.FFFFFFFFF</c>) text form must be exactly
/// the backing <see cref="LocalTime"/>'s</em>. The family asks its elements for that format
/// rather than their default, because NodaTime's <see cref="IFormattable"/> with a null format
/// produces the culture's form, not ISO — a <see cref="LocalDate"/> renders as
/// <c>Saturday, 15 June 2024</c>. An element that ignores the format specifier is rejected at
/// the persistence boundary rather than stored truncated.
/// </para>
/// <para>
/// Canonical order is the element's own <see cref="IComparable{T}"/>, which for generated
/// wrappers delegates to the backing <see cref="LocalTime"/>.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="LocalTime"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(LocalTimeSet), nameof(LocalTimeSet.From))]
public sealed class LocalTimeSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<LocalTimeSet<TElement>, TElement>, IEquatable<LocalTimeSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private LocalTimeSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <summary>
    /// The format the family asks its elements for — extended ISO 8601
    /// (<c>HH:mm:ss.FFFFFFFFF</c>) — so the array literal, JSON and the EF Core bridge all
    /// agree on one text form. See the class remarks for why the element's own default will not
    /// do.
    /// </summary>
    private static readonly string RoundTripFormat = LocalTimePattern.ExtendedIso.PatternText;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static LocalTimeSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalTimeSet<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalTimeSet<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static LocalTimeSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static LocalTimeSet<TElement> IValueSetFactory<LocalTimeSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
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

    /// <summary>Formats an element using extended ISO 8601 (<c>HH:mm:ss.FFFFFFFFF</c>) by
    /// default — see the class remarks.</summary>
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
    static JsonConverter<TElement>? IValueSetFactory<LocalTimeSet<TElement>, TElement>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<LocalTimeSet<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="LocalTimeSet{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static LocalTimeSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<LocalTimeSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static LocalTimeSet<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<LocalTimeSet<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="LocalTimeSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out LocalTimeSet<TElement> result)
        => SetFormat.TryParse<LocalTimeSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out LocalTimeSet<TElement> result)
        => SetFormat.TryParse<LocalTimeSet<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(LocalTimeSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LocalTimeSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(LocalTimeSet{TElement})"/>.</summary>
    public static bool operator ==(LocalTimeSet<TElement>? left, LocalTimeSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(LocalTimeSet<TElement>? left, LocalTimeSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

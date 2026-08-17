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
/// An immutable, canonical set of times of day — the value-set counterpart of a
/// PostgreSQL <c>time[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, sorted by chronological order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// PostgreSQL <c>time</c>'s special value <c>24:00:00</c> is not representable in
/// <see cref="TimeOnly"/> — the same caveat as <see cref="TimeRange"/>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(TimeSet), nameof(From))]
public sealed class TimeSet : IValueSet<TimeOnly>, IValueSetFactory<TimeSet, TimeOnly>, IEquatable<TimeSet>
{
    private readonly ImmutableArray<TimeOnly> _elements;

    private TimeSet(ImmutableArray<TimeOnly> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static TimeSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static TimeSet From(IEnumerable<TimeOnly> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TimeOnly>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static TimeSet From(params ReadOnlySpan<TimeOnly> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TimeOnly>.Default));

    /// <summary>Collection-expression builder for <see cref="TimeSet{TElement}"/>.</summary>
    public static TimeSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => TimeSet<TElement>.From(values);

    internal static TimeSet FromTrusted(ImmutableArray<TimeOnly> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static TimeSet IValueSetFactory<TimeSet, TimeOnly>.FromTrusted(ImmutableArray<TimeOnly> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<TimeOnly> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public TimeOnly this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<TimeOnly>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static TimeOnly ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TimeOnly.Parse(s, provider ?? CultureInfo.InvariantCulture);

    /// <summary>Formats a <see cref="TimeOnly"/> value using the round-trip format specifier (<c>O</c>) by default, preserving full precision.</summary>
    public static string FormatValue(TimeOnly value, string? format, IFormatProvider? provider)
        => value.ToString(format ?? "O", provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{09:00:00,17:00:00}</c>, <c>{}</c>) into a
    /// <see cref="TimeSet"/>, normalizing to canonical form.
    /// </summary>
    public static TimeSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<TimeSet, TimeOnly>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static TimeSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<TimeSet, TimeOnly>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="TimeSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out TimeSet result)
        => SetFormat.TryParse<TimeSet, TimeOnly>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TimeSet result)
        => SetFormat.TryParse<TimeSet, TimeOnly>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(TimeSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as TimeSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(TimeSet)"/>.</summary>
    public static bool operator ==(TimeSet? left, TimeSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(TimeSet? left, TimeSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated time-of-day-backed elements — the value-set
/// counterpart of a PostgreSQL <c>time[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators provide out of the box. The contract that cannot be expressed in constraints:
/// <em>the element's round-trip (<c>O</c>) text form must be exactly the backing
/// <see cref="TimeOnly"/>'s</em>. The family asks its elements for that format rather than
/// their default, because a <see cref="TimeOnly"/>'s default invariant form is <c>09:30</c>,
/// which silently drops the seconds and everything below them. An element that ignores the
/// format specifier is rejected at the persistence boundary rather than stored truncated.
/// </para>
/// <para>
/// Canonical order is the element's own <see cref="IComparable{T}"/>, which for generated
/// wrappers delegates to the backing <see cref="TimeOnly"/>.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="TimeOnly"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(TimeSet), nameof(TimeSet.From))]
public sealed class TimeSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<TimeSet<TElement>, TElement>, IEquatable<TimeSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private TimeSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <summary>
    /// The format the family asks its elements for — round-trip (<c>O</c>) — so the array
    /// literal, JSON and the EF Core bridge all agree on one text form. See the class remarks
    /// for why the element's own default will not do.
    /// </summary>
    private const string RoundTripFormat = "O";

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static TimeSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static TimeSet<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static TimeSet<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static TimeSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static TimeSet<TElement> IValueSetFactory<TimeSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
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
    static JsonConverter<TElement>? IValueSetFactory<TimeSet<TElement>, TElement>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<TimeSet<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="TimeSet{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static TimeSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<TimeSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static TimeSet<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<TimeSet<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="TimeSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out TimeSet<TElement> result)
        => SetFormat.TryParse<TimeSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TimeSet<TElement> result)
        => SetFormat.TryParse<TimeSet<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(TimeSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as TimeSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(TimeSet{TElement})"/>.</summary>
    public static bool operator ==(TimeSet<TElement>? left, TimeSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(TimeSet<TElement>? left, TimeSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

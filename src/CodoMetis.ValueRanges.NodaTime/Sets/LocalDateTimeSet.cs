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
/// An immutable, canonical set of <see cref="LocalDateTime"/> values — the value-set counterpart of a
/// PostgreSQL <c>timestamp without time zone[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// Canonical form — deduplicated, sorted by chronological order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </para>
/// <para>
/// Elements are normalized to the ISO calendar at construction, mirroring
/// <see cref="LocalDateTimeRange"/>. LocalDateTime is wall-clock time by construction, so the
/// <see cref="DateTimeKind"/> caveats of <see cref="DateTimeSet"/> do not arise.
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
[CollectionBuilder(typeof(LocalDateTimeSet), nameof(From))]
public sealed class LocalDateTimeSet : IValueSet<LocalDateTime>, IValueSetFactory<LocalDateTimeSet, LocalDateTime>, IEquatable<LocalDateTimeSet>
{
    private readonly ImmutableArray<LocalDateTime> _elements;

    private LocalDateTimeSet(ImmutableArray<LocalDateTime> elements) => _elements = elements;

    private static LocalDateTime ToIso(LocalDateTime value)
        => value.Calendar == CalendarSystem.Iso ? value : value.WithCalendar(CalendarSystem.Iso);

    // Element-level operations (Contains, Add, Remove) normalize their probe through the same
    // helper as From: LocalDateTime.CompareTo throws across calendar systems and Equals returns
    // false, so an un-normalized probe would neither match nor sort against stored elements.
    LocalDateTime IValueSet<LocalDateTime>.NormalizeElement(LocalDateTime value) => ToIso(value);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static LocalDateTimeSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalDateTimeSet From(IEnumerable<LocalDateTime> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromTrusted(ValueSetCore.Canonicalize(values.Select(ToIso), Comparer<LocalDateTime>.Default));
    }

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalDateTimeSet From(params ReadOnlySpan<LocalDateTime> values)
    {
        var normalized = new List<LocalDateTime>(values.Length);
        foreach (var value in values) normalized.Add(ToIso(value));
        return FromTrusted(ValueSetCore.Canonicalize(normalized, Comparer<LocalDateTime>.Default));
    }

    /// <summary>Collection-expression builder for <see cref="LocalDateTimeSet{TElement}"/>.</summary>
    public static LocalDateTimeSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => LocalDateTimeSet<TElement>.From(values);

    internal static LocalDateTimeSet FromTrusted(ImmutableArray<LocalDateTime> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static LocalDateTimeSet IValueSetFactory<LocalDateTimeSet, LocalDateTime>.FromTrusted(ImmutableArray<LocalDateTime> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<LocalDateTime> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public LocalDateTime this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<LocalDateTime>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static LocalDateTime ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseDateTime(s.ToString());

    /// <summary>
    /// Formats a <see cref="LocalDateTime"/> value using ISO 8601 (<c>uuuu-MM-ddTHH:mm:ss</c> with optional subseconds) by default.
    /// </summary>
    public static string FormatValue(LocalDateTime value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.DateTime.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The JSON fallback for <see cref="LocalDateTime"/> elements: the same ISO 8601 text form as
    /// <see cref="FormatValue"/>, so JSON, array literals and the wire form agree. Consulted only
    /// when no converter has claimed <see cref="LocalDateTime"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<LocalDateTime>? IValueSetFactory<LocalDateTimeSet, LocalDateTime>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<LocalDateTimeSet, LocalDateTime>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-06-01T08:00:00,2024-06-02T08:00:00}</c>, <c>{}</c>) into a
    /// <see cref="LocalDateTimeSet"/>, normalizing to canonical form.
    /// </summary>
    public static LocalDateTimeSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<LocalDateTimeSet, LocalDateTime>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static LocalDateTimeSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<LocalDateTimeSet, LocalDateTime>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="LocalDateTimeSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out LocalDateTimeSet result)
        => SetFormat.TryParse<LocalDateTimeSet, LocalDateTime>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out LocalDateTimeSet result)
        => SetFormat.TryParse<LocalDateTimeSet, LocalDateTime>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(LocalDateTimeSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LocalDateTimeSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(LocalDateTimeSet)"/>.</summary>
    public static bool operator ==(LocalDateTimeSet? left, LocalDateTimeSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(LocalDateTimeSet? left, LocalDateTimeSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated wall-clock-timestamp-backed elements — the
/// value-set counterpart of a PostgreSQL <c>timestamp without time zone[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators provide out of the box. The contract that cannot be expressed in constraints:
/// <em>the element's extended ISO 8601 (<c>uuuu-MM-ddTHH:mm:ss.FFFFFFFFF</c>) text form must be
/// exactly the backing <see cref="LocalDateTime"/>'s</em>. The family asks its elements for
/// that format rather than their default, because NodaTime's <see cref="IFormattable"/> with a
/// null format produces the culture's form, not ISO — a <see cref="LocalDate"/> renders as
/// <c>Saturday, 15 June 2024</c>. An element that ignores the format specifier is rejected at
/// the persistence boundary rather than stored truncated.
/// </para>
/// <para>
/// Canonical order is the element's own <see cref="IComparable{T}"/>, which for generated
/// wrappers delegates to the backing <see cref="LocalDateTime"/>.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="LocalDateTime"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(LocalDateTimeSet), nameof(LocalDateTimeSet.From))]
public sealed class LocalDateTimeSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<LocalDateTimeSet<TElement>, TElement>, IEquatable<LocalDateTimeSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private LocalDateTimeSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <summary>
    /// The format the family asks its elements for — extended ISO 8601
    /// (<c>uuuu-MM-ddTHH:mm:ss.FFFFFFFFF</c>) — so the array literal, JSON and the EF Core
    /// bridge all agree on one text form. See the class remarks for why the element's own
    /// default will not do.
    /// </summary>
    private static readonly string RoundTripFormat = LocalDateTimePattern.ExtendedIso.PatternText;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static LocalDateTimeSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalDateTimeSet<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalDateTimeSet<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static LocalDateTimeSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static LocalDateTimeSet<TElement> IValueSetFactory<LocalDateTimeSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
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

    /// <summary>Formats an element using extended ISO 8601
    /// (<c>uuuu-MM-ddTHH:mm:ss.FFFFFFFFF</c>) by default — see the class remarks.</summary>
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
    static JsonConverter<TElement>? IValueSetFactory<LocalDateTimeSet<TElement>, TElement>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<LocalDateTimeSet<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="LocalDateTimeSet{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static LocalDateTimeSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<LocalDateTimeSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static LocalDateTimeSet<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<LocalDateTimeSet<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="LocalDateTimeSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out LocalDateTimeSet<TElement> result)
        => SetFormat.TryParse<LocalDateTimeSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out LocalDateTimeSet<TElement> result)
        => SetFormat.TryParse<LocalDateTimeSet<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(LocalDateTimeSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LocalDateTimeSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(LocalDateTimeSet{TElement})"/>.</summary>
    public static bool operator ==(LocalDateTimeSet<TElement>? left, LocalDateTimeSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(LocalDateTimeSet<TElement>? left, LocalDateTimeSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

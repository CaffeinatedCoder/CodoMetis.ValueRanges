using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;
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

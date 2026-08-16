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

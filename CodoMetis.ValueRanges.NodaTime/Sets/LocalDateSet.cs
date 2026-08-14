using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of <see cref="LocalDate"/> values — the value-set counterpart of a
/// PostgreSQL <c>date[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// Canonical form — deduplicated, sorted by chronological order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </para>
/// <para>
/// Elements are normalized to the ISO calendar at construction — <see cref="LocalDate.CompareTo"/>
/// is only defined between dates of the same calendar system, and PostgreSQL <c>date</c> is
/// proleptic Gregorian. A date whose day is out of range of the ISO calendar throws
/// <see cref="ArgumentOutOfRangeException"/> at construction.
/// </para>
/// <para>
/// System.Text.Json serialization delegates elements to the configured serializer — register
/// NodaTime.Serialization.SystemTextJson's converters for ISO 8601 element output.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(LocalDateSet), nameof(From))]
public sealed class LocalDateSet : IValueSet<LocalDate>, IValueSetFactory<LocalDateSet, LocalDate>, IEquatable<LocalDateSet>
{
    private readonly ImmutableArray<LocalDate> _elements;

    private LocalDateSet(ImmutableArray<LocalDate> elements) => _elements = elements;

    private static LocalDate ToIso(LocalDate value)
        => value.Calendar == CalendarSystem.Iso ? value : value.WithCalendar(CalendarSystem.Iso);

    // Element-level operations (Contains, Add, Remove) normalize their probe through the same
    // helper as From: LocalDate.CompareTo throws across calendar systems and Equals returns
    // false, so an un-normalized probe would neither match nor sort against stored elements.
    LocalDate IValueSet<LocalDate>.NormalizeElement(LocalDate value) => ToIso(value);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static LocalDateSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalDateSet From(IEnumerable<LocalDate> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromTrusted(ValueSetCore.Canonicalize(values.Select(ToIso), Comparer<LocalDate>.Default));
    }

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static LocalDateSet From(params ReadOnlySpan<LocalDate> values)
    {
        var normalized = new List<LocalDate>(values.Length);
        foreach (var value in values) normalized.Add(ToIso(value));
        return FromTrusted(ValueSetCore.Canonicalize(normalized, Comparer<LocalDate>.Default));
    }

    internal static LocalDateSet FromTrusted(ImmutableArray<LocalDate> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static LocalDateSet IValueSetFactory<LocalDateSet, LocalDate>.FromTrusted(ImmutableArray<LocalDate> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<LocalDate> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<LocalDate>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static LocalDate ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseDate(s.ToString());

    /// <summary>
    /// Formats a <see cref="LocalDate"/> value using ISO 8601 (<c>uuuu-MM-dd</c>) by default.
    /// </summary>
    public static string FormatValue(LocalDate value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.Date.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-01-01,2024-12-24}</c>, <c>{}</c>) into a
    /// <see cref="LocalDateSet"/>, normalizing to canonical form.
    /// </summary>
    public static LocalDateSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<LocalDateSet, LocalDate>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="LocalDateSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out LocalDateSet result)
        => SetFormat.TryParse<LocalDateSet, LocalDate>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(LocalDateSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LocalDateSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(LocalDateSet)"/>.</summary>
    public static bool operator ==(LocalDateSet? left, LocalDateSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(LocalDateSet? left, LocalDateSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

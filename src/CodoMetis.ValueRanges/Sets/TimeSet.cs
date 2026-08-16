using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

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

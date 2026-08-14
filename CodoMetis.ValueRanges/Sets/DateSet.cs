using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

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

    internal static DateSet FromTrusted(ImmutableArray<DateOnly> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateSet IValueSetFactory<DateSet, DateOnly>.FromTrusted(ImmutableArray<DateOnly> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<DateOnly> Values => _elements;

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

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateSet result)
        => SetFormat.TryParse<DateSet, DateOnly>(s.AsSpan(), provider, out result);

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

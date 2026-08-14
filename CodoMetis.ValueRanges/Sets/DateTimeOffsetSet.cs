using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

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

    internal static DateTimeOffsetSet FromTrusted(ImmutableArray<DateTimeOffset> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateTimeOffsetSet IValueSetFactory<DateTimeOffsetSet, DateTimeOffset>.FromTrusted(ImmutableArray<DateTimeOffset> elements)
        => FromTrusted(elements);

    ImmutableArray<DateTimeOffset> IValueSet<DateTimeOffset>.Elements => _elements;

    /// <summary>The canonical elements: deduplicated, sorted by instant order (UTC ticks).</summary>
    public ImmutableArray<DateTimeOffset> Values => _elements;

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

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateTimeOffsetSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeOffsetSet result)
        => SetFormat.TryParse<DateTimeOffsetSet, DateTimeOffset>(s.AsSpan(), provider, out result);

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

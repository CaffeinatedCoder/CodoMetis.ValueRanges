using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

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

    internal static DateTimeSet FromTrusted(ImmutableArray<DateTime> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DateTimeSet IValueSetFactory<DateTimeSet, DateTime>.FromTrusted(ImmutableArray<DateTime> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by chronological order.</summary>
    public ImmutableArray<DateTime> Values => _elements;

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

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DateTimeSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeSet result)
        => SetFormat.TryParse<DateTimeSet, DateTime>(s.AsSpan(), provider, out result);

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

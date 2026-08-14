using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of decimal numbers — the value-set counterpart of a
/// PostgreSQL <c>numeric[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, sorted by numeric order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// PostgreSQL <c>numeric</c> and .NET <see cref="decimal"/> both compare by value across
/// scales (<c>{1.0}</c> equals <c>{1.00}</c>); deduplication keeps the first representation
/// in input order.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(DecimalSet), nameof(From))]
public sealed class DecimalSet : IValueSet<decimal>, IValueSetFactory<DecimalSet, decimal>, IEquatable<DecimalSet>
{
    private readonly ImmutableArray<decimal> _elements;

    private DecimalSet(ImmutableArray<decimal> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static DecimalSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DecimalSet From(IEnumerable<decimal> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<decimal>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static DecimalSet From(params ReadOnlySpan<decimal> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<decimal>.Default));

    internal static DecimalSet FromTrusted(ImmutableArray<decimal> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static DecimalSet IValueSetFactory<DecimalSet, decimal>.FromTrusted(ImmutableArray<decimal> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by numeric order.</summary>
    public ImmutableArray<decimal> Values => _elements;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<decimal>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static decimal ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => decimal.Parse(s, NumberStyles.Any, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{1.5,2.25}</c>, <c>{}</c>) into a
    /// <see cref="DecimalSet"/>, normalizing to canonical form.
    /// </summary>
    public static DecimalSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<DecimalSet, decimal>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="DecimalSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DecimalSet result)
        => SetFormat.TryParse<DecimalSet, decimal>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(DecimalSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DecimalSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(DecimalSet)"/>.</summary>
    public static bool operator ==(DecimalSet? left, DecimalSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(DecimalSet? left, DecimalSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

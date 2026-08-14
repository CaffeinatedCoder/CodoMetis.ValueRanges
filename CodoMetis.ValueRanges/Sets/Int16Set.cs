using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of 16-bit signed integers — the value-set counterpart of a
/// PostgreSQL <c>smallint[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, sorted by numeric order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(Int16Set), nameof(From))]
public sealed class Int16Set : IValueSet<short>, IValueSetFactory<Int16Set, short>, IEquatable<Int16Set>
{
    private readonly ImmutableArray<short> _elements;

    private Int16Set(ImmutableArray<short> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static Int16Set Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int16Set From(IEnumerable<short> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<short>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int16Set From(params ReadOnlySpan<short> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<short>.Default));

    internal static Int16Set FromTrusted(ImmutableArray<short> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Int16Set IValueSetFactory<Int16Set, short>.FromTrusted(ImmutableArray<short> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by numeric order.</summary>
    public ImmutableArray<short> Values => _elements;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<short>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static short ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => short.Parse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{1,2}</c>, <c>{}</c>) into a
    /// <see cref="Int16Set"/>, normalizing to canonical form.
    /// </summary>
    public static Int16Set Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<Int16Set, short>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="Int16Set"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int16Set result)
        => SetFormat.TryParse<Int16Set, short>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(Int16Set? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Int16Set);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(Int16Set)"/>.</summary>
    public static bool operator ==(Int16Set? left, Int16Set? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(Int16Set? left, Int16Set? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

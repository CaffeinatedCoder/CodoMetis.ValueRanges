using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of 32-bit signed integers — the value-set counterpart of a
/// PostgreSQL <c>integer[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, numerically sorted — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(Int32Set), nameof(From))]
public sealed class Int32Set : IValueSet<int>, IValueSetFactory<Int32Set, int>, IEquatable<Int32Set>
{
    private readonly ImmutableArray<int> _elements;

    private Int32Set(ImmutableArray<int> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static Int32Set Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int32Set From(IEnumerable<int> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<int>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int32Set From(params ReadOnlySpan<int> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<int>.Default));

    /// <summary>Collection-expression builder for <see cref="Int32Set{TElement}"/>.</summary>
    public static Int32Set<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => Int32Set<TElement>.From(values);

    internal static Int32Set FromTrusted(ImmutableArray<int> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Int32Set IValueSetFactory<Int32Set, int>.FromTrusted(ImmutableArray<int> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, numerically sorted.</summary>
    public ImmutableArray<int> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<int>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static int ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => int.Parse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{1,2}</c>, <c>{}</c>) into an
    /// <see cref="Int32Set"/>, normalizing to canonical form.
    /// </summary>
    public static Int32Set Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<Int32Set, int>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into an <see cref="Int32Set"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int32Set result)
        => SetFormat.TryParse<Int32Set, int>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(Int32Set? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Int32Set);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(Int32Set)"/>.</summary>
    public static bool operator ==(Int32Set? left, Int32Set? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(Int32Set? left, Int32Set? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated int-backed elements (e.g. typed IDs) — the
/// value-set counterpart of a PostgreSQL <c>integer[]</c> column.
/// </summary>
/// <remarks>
/// <typeparamref name="TElement"/> requires only BCL interfaces. The contract that cannot be
/// expressed in constraints: <em>the element's invariant text form must be exactly the backing
/// <see cref="int"/>'s text form</em> — a decorative format fails loudly at the persistence
/// boundary. Canonical order is the element's own <see cref="IComparable{T}"/>, which for
/// generated wrappers delegates to the backing <see cref="int"/>.
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by an <see cref="int"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(Int32Set), nameof(Int32Set.From))]
public sealed class Int32Set<TElement> :
    IValueSet<TElement>, IValueSetFactory<Int32Set<TElement>, TElement>, IEquatable<Int32Set<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private Int32Set(ImmutableArray<TElement> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static Int32Set<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int32Set<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int32Set<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static Int32Set<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Int32Set<TElement> IValueSetFactory<Int32Set<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by the element's <see cref="IComparable{T}"/>.</summary>
    public ImmutableArray<TElement> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

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

    /// <summary>Formats an element via <see cref="IFormattable"/> — its backing text form.</summary>
    public static string FormatValue(TElement value, string? format, IFormatProvider? provider)
        => value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal into an <see cref="Int32Set{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static Int32Set<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<Int32Set<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into an <see cref="Int32Set{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int32Set<TElement> result)
        => SetFormat.TryParse<Int32Set<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(Int32Set<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Int32Set<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(Int32Set{TElement})"/>.</summary>
    public static bool operator ==(Int32Set<TElement>? left, Int32Set<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(Int32Set<TElement>? left, Int32Set<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

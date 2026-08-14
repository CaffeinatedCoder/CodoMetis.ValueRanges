using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of 64-bit signed integers — the value-set counterpart of a
/// PostgreSQL <c>bigint[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated, numerically sorted — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(Int64Set), nameof(From))]
public sealed class Int64Set : IValueSet<long>, IValueSetFactory<Int64Set, long>, IEquatable<Int64Set>
{
    private readonly ImmutableArray<long> _elements;

    private Int64Set(ImmutableArray<long> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static Int64Set Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int64Set From(IEnumerable<long> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<long>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int64Set From(params ReadOnlySpan<long> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<long>.Default));

    /// <summary>Collection-expression builder for <see cref="Int64Set{TElement}"/>.</summary>
    public static Int64Set<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => Int64Set<TElement>.From(values);

    internal static Int64Set FromTrusted(ImmutableArray<long> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Int64Set IValueSetFactory<Int64Set, long>.FromTrusted(ImmutableArray<long> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, numerically sorted.</summary>
    public ImmutableArray<long> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<long>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static long ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => long.Parse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{1,2}</c>, <c>{}</c>) into an
    /// <see cref="Int64Set"/>, normalizing to canonical form.
    /// </summary>
    public static Int64Set Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<Int64Set, long>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into an <see cref="Int64Set"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int64Set result)
        => SetFormat.TryParse<Int64Set, long>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(Int64Set? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Int64Set);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(Int64Set)"/>.</summary>
    public static bool operator ==(Int64Set? left, Int64Set? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(Int64Set? left, Int64Set? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated long-backed elements (e.g. typed IDs) — the
/// value-set counterpart of a PostgreSQL <c>bigint[]</c> column.
/// </summary>
/// <remarks>
/// <typeparamref name="TElement"/> requires only BCL interfaces. The contract that cannot be
/// expressed in constraints: <em>the element's invariant text form must be exactly the backing
/// <see cref="long"/>'s text form</em> — a decorative format fails loudly at the persistence
/// boundary. Canonical order is the element's own <see cref="IComparable{T}"/>, which for
/// generated wrappers delegates to the backing <see cref="long"/>.
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="long"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(Int64Set), nameof(Int64Set.From))]
public sealed class Int64Set<TElement> :
    IValueSet<TElement>, IValueSetFactory<Int64Set<TElement>, TElement>, IEquatable<Int64Set<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private Int64Set(ImmutableArray<TElement> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static Int64Set<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int64Set<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int64Set<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static Int64Set<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Int64Set<TElement> IValueSetFactory<Int64Set<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
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
    /// The JSON fallback for elements that carry no converter of their own: a JSON number holding
    /// the same text form as <see cref="FormatValue"/>, so the wrapper serializes exactly like the
    /// primitive it wraps and <see cref="ParseValue"/> re-runs its validation on the way in.
    /// Consulted only when nothing else has claimed <typeparamref name="TElement"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<TElement>? IValueSetFactory<Int64Set<TElement>, TElement>.ElementJsonConverter
        => ValueSetIntegerElementJsonConverter<Int64Set<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into an <see cref="Int64Set{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static Int64Set<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<Int64Set<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into an <see cref="Int64Set{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int64Set<TElement> result)
        => SetFormat.TryParse<Int64Set<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(Int64Set<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Int64Set<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(Int64Set{TElement})"/>.</summary>
    public static bool operator ==(Int64Set<TElement>? left, Int64Set<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(Int64Set<TElement>? left, Int64Set<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

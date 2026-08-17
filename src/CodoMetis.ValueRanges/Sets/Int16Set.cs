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

    /// <summary>Collection-expression builder for <see cref="Int16Set{TElement}"/>.</summary>
    public static Int16Set<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => Int16Set<TElement>.From(values);

    internal static Int16Set FromTrusted(ImmutableArray<short> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Int16Set IValueSetFactory<Int16Set, short>.FromTrusted(ImmutableArray<short> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by numeric order.</summary>
    public ImmutableArray<short> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public short this[int index] => _elements[index];

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

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

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static Int16Set Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<Int16Set, short>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="Int16Set"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int16Set result)
        => SetFormat.TryParse<Int16Set, short>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Int16Set result)
        => SetFormat.TryParse<Int16Set, short>(s, provider, out result);

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

/// <summary>
/// An immutable, canonical set of validated short-backed elements (e.g. compact typed codes) —
/// the value-set counterpart of a PostgreSQL <c>smallint[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators provide out of the box. The contract that cannot be expressed in constraints:
/// <em>the element's invariant text form must be exactly the backing <see cref="short"/>'s text
/// form</em> — a decorative format fails loudly at the persistence boundary.
/// </para>
/// <para>
/// Canonical order is the element's own <see cref="IComparable{T}"/>, which for generated
/// wrappers delegates to the backing <see cref="short"/>.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="short"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(Int16Set), nameof(Int16Set.From))]
public sealed class Int16Set<TElement> :
    IValueSet<TElement>, IValueSetFactory<Int16Set<TElement>, TElement>, IEquatable<Int16Set<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private Int16Set(ImmutableArray<TElement> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static Int16Set<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int16Set<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static Int16Set<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static Int16Set<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static Int16Set<TElement> IValueSetFactory<Int16Set<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by the element's <see cref="IComparable{T}"/>.</summary>
    public ImmutableArray<TElement> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the element at <paramref name="index"/>, in canonical order.</summary>
    /// <param name="index">The zero-based index.</param>
    public TElement this[int index] => _elements[index];

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

    /// <summary>Formats an element via <see cref="IFormattable"/> — its backing text
    /// form.</summary>
    public static string FormatValue(TElement value, string? format, IFormatProvider? provider)
        => value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The JSON fallback for elements that carry no converter of their own: a JSON number
    /// holding the same text form as <see cref="FormatValue"/>, so the wrapper serializes
    /// exactly like the primitive it wraps and <see cref="ParseValue"/> re-runs its validation
    /// on the way in. Consulted only when nothing else has claimed
    /// <typeparamref name="TElement"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<TElement>? IValueSetFactory<Int16Set<TElement>, TElement>.ElementJsonConverter
        => ValueSetIntegerElementJsonConverter<Int16Set<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="Int16Set{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static Int16Set<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<Int16Set<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static Int16Set<TElement> Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<Int16Set<TElement>, TElement>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="Int16Set{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int16Set<TElement> result)
        => SetFormat.TryParse<Int16Set<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Int16Set<TElement> result)
        => SetFormat.TryParse<Int16Set<TElement>, TElement>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(Int16Set<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Int16Set<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(Int16Set{TElement})"/>.</summary>
    public static bool operator ==(Int16Set<TElement>? left, Int16Set<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(Int16Set<TElement>? left, Int16Set<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

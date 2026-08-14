using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of <see cref="string"/> values — the value-set counterpart of a
/// PostgreSQL <c>text[]</c> column.
/// </summary>
/// <remarks>
/// Canonical form — deduplicated and sorted with <see cref="StringComparer.Ordinal"/> — is
/// enforced at construction. Because every writer produces the identical array for the same
/// set, SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(StringSet), nameof(From))]
public sealed class StringSet : IValueSet<string>, IValueSetFactory<StringSet, string>, IEquatable<StringSet>
{
    private readonly ImmutableArray<string> _elements;

    private StringSet(ImmutableArray<string> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static StringSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static StringSet From(IEnumerable<string> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, CanonicalComparer));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static StringSet From(params ReadOnlySpan<string> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, CanonicalComparer));

    /// <summary>Collection-expression builder for <see cref="StringSet{TElement}"/>.</summary>
    public static StringSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IFormattable, IParsable<TElement>
        => StringSet<TElement>.From(values);

    internal static StringSet FromTrusted(ImmutableArray<string> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static StringSet IValueSetFactory<StringSet, string>.FromTrusted(ImmutableArray<string> elements)
        => FromTrusted(elements);

    /// <summary>
    /// Canonical order: <see cref="StringComparer.Ordinal"/>. Never a culture-sensitive
    /// comparison — canonical form is a cross-writer storage contract, not a display order.
    /// </summary>
    public static IComparer<string> CanonicalComparer => StringComparer.Ordinal;

    IComparer<string> IValueSet<string>.CanonicalOrder => CanonicalComparer;

    /// <summary>The canonical elements: deduplicated, ordinal-sorted.</summary>
    public ImmutableArray<string> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<string>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static string ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider) => s.ToString();

    /// <summary>Formats an element — the identity function for strings.</summary>
    public static string FormatValue(string value, string? format, IFormatProvider? provider) => value;

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{a,b}</c>, <c>{"a b"}</c>, <c>{}</c>)
    /// into a <see cref="StringSet"/>, normalizing to canonical form.
    /// </summary>
    public static StringSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<StringSet, string>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="StringSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out StringSet result)
        => SetFormat.TryParse<StringSet, string>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(StringSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StringSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(StringSet)"/>.</summary>
    public static bool operator ==(StringSet? left, StringSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(StringSet? left, StringSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated string-backed elements — the value-set counterpart
/// of a PostgreSQL <c>text[]</c> column whose elements are domain values such as typed keys.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which validated-value
/// generators (Vogen, Metalama aspects, StronglyTypedId, or hand-written wrappers) provide out
/// of the box: <see cref="IFormattable"/> supplies the element's backing text, and
/// <see cref="IParsable{TSelf}"/> re-validates on the way in — so reading corrupt data throws.
/// The contract that cannot be expressed in constraints: <em>the element's invariant text form
/// must be exactly the backing primitive's text form</em>.
/// </para>
/// <para>
/// Canonical order is ordinal over the invariant text form — deliberately not the element's
/// own <see cref="IComparable{T}"/>, whose generated implementations typically delegate to
/// culture-sensitive string comparison.
/// </para>
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a string.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(StringSet), nameof(StringSet.From))]
public sealed class StringSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<StringSet<TElement>, TElement>, IEquatable<StringSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private StringSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static StringSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static StringSet<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, CanonicalComparer));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static StringSet<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, CanonicalComparer));

    internal static StringSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static StringSet<TElement> IValueSetFactory<StringSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
        => FromTrusted(elements);

    /// <summary>
    /// Canonical order: ordinal over the element's invariant text form (see the class remarks).
    /// </summary>
    public static IComparer<TElement> CanonicalComparer { get; } = Comparer<TElement>.Create(
        static (x, y) => string.CompareOrdinal(
            x.ToString(null, CultureInfo.InvariantCulture),
            y.ToString(null, CultureInfo.InvariantCulture)));

    IComparer<TElement> IValueSet<TElement>.CanonicalOrder => CanonicalComparer;

    /// <summary>The canonical elements: deduplicated, sorted ordinal over the invariant text form.</summary>
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
    /// Parses a PostgreSQL array literal (e.g. <c>{users.read,users.write}</c>, <c>{}</c>)
    /// into a <see cref="StringSet{TElement}"/>, re-validating every element and normalizing
    /// to canonical form.
    /// </summary>
    public static StringSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<StringSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="StringSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out StringSet<TElement> result)
        => SetFormat.TryParse<StringSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(StringSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as StringSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(StringSet{TElement})"/>.</summary>
    public static bool operator ==(StringSet<TElement>? left, StringSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(StringSet<TElement>? left, StringSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

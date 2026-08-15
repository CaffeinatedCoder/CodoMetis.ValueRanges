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
/// An immutable, canonical set of <see cref="Guid"/> values — the value-set counterpart of a
/// PostgreSQL <c>uuid[]</c> column.
/// </summary>
/// <remarks>
/// Canonical order is <see cref="Guid.CompareTo(Guid)"/> — .NET's ordering, which PostgreSQL
/// never re-sorts; only CLR-side writer agreement matters for the canonical contract.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(GuidSet), nameof(From))]
public sealed class GuidSet : IValueSet<Guid>, IValueSetFactory<GuidSet, Guid>, IEquatable<GuidSet>
{
    private readonly ImmutableArray<Guid> _elements;

    private GuidSet(ImmutableArray<Guid> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static GuidSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static GuidSet From(IEnumerable<Guid> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<Guid>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static GuidSet From(params ReadOnlySpan<Guid> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<Guid>.Default));

    /// <summary>Collection-expression builder for <see cref="GuidSet{TElement}"/>.</summary>
    public static GuidSet<TElement> From<TElement>(ReadOnlySpan<TElement> values)
        where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
        => GuidSet<TElement>.From(values);

    internal static GuidSet FromTrusted(ImmutableArray<Guid> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static GuidSet IValueSetFactory<GuidSet, Guid>.FromTrusted(ImmutableArray<Guid> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by <see cref="Guid.CompareTo(Guid)"/>.</summary>
    public ImmutableArray<Guid> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<Guid>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static Guid ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider) => Guid.Parse(s);

    /// <summary>
    /// Parses a PostgreSQL array literal of UUIDs into a <see cref="GuidSet"/>,
    /// normalizing to canonical form.
    /// </summary>
    public static GuidSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<GuidSet, Guid>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="GuidSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out GuidSet result)
        => SetFormat.TryParse<GuidSet, Guid>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(GuidSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as GuidSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(GuidSet)"/>.</summary>
    public static bool operator ==(GuidSet? left, GuidSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(GuidSet? left, GuidSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

/// <summary>
/// An immutable, canonical set of validated Guid-backed elements (e.g. strongly typed IDs) —
/// the value-set counterpart of a PostgreSQL <c>uuid[]</c> column.
/// </summary>
/// <remarks>
/// <typeparamref name="TElement"/> requires only BCL interfaces, which typed-ID generators
/// provide out of the box. The contract that cannot be expressed in constraints: <em>the
/// element's invariant text form must be exactly the backing <see cref="Guid"/>'s text
/// form</em> — a decorative format fails loudly at the persistence boundary.
/// Canonical order is the element's own <see cref="IComparable{T}"/>, which for generated
/// wrappers delegates to the backing <see cref="Guid"/> — culture-free by construction.
/// </remarks>
/// <typeparam name="TElement">The validated element type backed by a <see cref="Guid"/>.</typeparam>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(GuidSet), nameof(GuidSet.From))]
public sealed class GuidSet<TElement> :
    IValueSet<TElement>, IValueSetFactory<GuidSet<TElement>, TElement>, IEquatable<GuidSet<TElement>>
    where TElement : struct, IEquatable<TElement>, IComparable<TElement>, IFormattable, IParsable<TElement>
{
    private readonly ImmutableArray<TElement> _elements;

    private GuidSet(ImmutableArray<TElement> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static GuidSet<TElement> Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static GuidSet<TElement> From(IEnumerable<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static GuidSet<TElement> From(params ReadOnlySpan<TElement> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<TElement>.Default));

    internal static GuidSet<TElement> FromTrusted(ImmutableArray<TElement> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static GuidSet<TElement> IValueSetFactory<GuidSet<TElement>, TElement>.FromTrusted(ImmutableArray<TElement> elements)
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
    /// The JSON fallback for elements that carry no converter of their own: a JSON string holding
    /// the same text form as <see cref="FormatValue"/>, so the wrapper serializes exactly like the
    /// primitive it wraps and <see cref="ParseValue"/> re-runs its validation on the way in.
    /// Consulted only when nothing else has claimed <typeparamref name="TElement"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<TElement>? IValueSetFactory<GuidSet<TElement>, TElement>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<GuidSet<TElement>, TElement>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal into a <see cref="GuidSet{TElement}"/>, re-validating
    /// every element and normalizing to canonical form.
    /// </summary>
    public static GuidSet<TElement> Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<GuidSet<TElement>, TElement>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="GuidSet{TElement}"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out GuidSet<TElement> result)
        => SetFormat.TryParse<GuidSet<TElement>, TElement>(s.AsSpan(), provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(GuidSet<TElement>? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as GuidSet<TElement>);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(GuidSet{TElement})"/>.</summary>
    public static bool operator ==(GuidSet<TElement>? left, GuidSet<TElement>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(GuidSet<TElement>? left, GuidSet<TElement>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

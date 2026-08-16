using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// An immutable, canonical set of <see cref="Instant"/> values — the value-set counterpart of a
/// PostgreSQL <c>timestamp with time zone[]</c> column.
/// </summary>
/// <remarks>
/// <para>
/// Canonical form — deduplicated, sorted by instant order — is enforced at construction, so
/// SQL <c>=</c> on the stored array is equivalent to CLR set equality.
/// The empty set is distinct from a NULL column and maps to the empty array <c>{}</c>.
/// </para>
/// <para>
/// An <see cref="Instant"/> is an instant by construction, so the offset-normalization
/// caveats of <see cref="DateTimeOffsetSet"/> do not arise. Parsing accepts arbitrary-offset
/// ISO 8601 forms and the PostgreSQL wire form; formatting always produces UTC (<c>Z</c>).
/// </para>
/// <para>
/// System.Text.Json serialization produces ISO 8601 element strings with no setup beyond the
/// converter factory: System.Text.Json has no built-in converter for NodaTime types, so the
/// family supplies its own through
/// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>. Registering an element converter
/// yourself takes precedence — <c>AddNodaTimeRangeConverters()</c> does that, and additionally
/// covers bare elements outside a set.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[CollectionBuilder(typeof(InstantSet), nameof(From))]
public sealed class InstantSet : IValueSet<Instant>, IValueSetFactory<InstantSet, Instant>, IEquatable<InstantSet>
{
    private readonly ImmutableArray<Instant> _elements;

    private InstantSet(ImmutableArray<Instant> elements) => _elements = elements;

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.Empty"/>
    public static InstantSet Empty { get; } = new([]);

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static InstantSet From(IEnumerable<Instant> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<Instant>.Default));

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.From(IEnumerable{T})"/>
    public static InstantSet From(params ReadOnlySpan<Instant> values)
        => FromTrusted(ValueSetCore.Canonicalize(values, Comparer<Instant>.Default));

    internal static InstantSet FromTrusted(ImmutableArray<Instant> elements)
        => elements.IsEmpty ? Empty : new(elements);

    static InstantSet IValueSetFactory<InstantSet, Instant>.FromTrusted(ImmutableArray<Instant> elements)
        => FromTrusted(elements);

    /// <summary>The canonical elements: deduplicated, sorted by instant order.</summary>
    public ImmutableArray<Instant> Values => _elements;

    /// <summary>The number of elements — PostgreSQL <c>cardinality</c>.</summary>
    public int Count => _elements.Length;

    /// <summary>Whether the set contains no elements — PostgreSQL <c>cardinality(…) = 0</c>.</summary>
    public bool IsEmpty => _elements.IsEmpty;

    /// <summary>Enumerates the canonical elements (supports <see langword="foreach"/>).</summary>
    public ImmutableArray<Instant>.Enumerator GetEnumerator() => _elements.GetEnumerator();

    /// <inheritdoc cref="IValueSetFactory{TSet,T}.ParseValue"/>
    public static Instant ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseInstant(s.ToString());

    /// <summary>
    /// Formats a <see cref="Instant"/> value using ISO 8601 (<c>uuuu-MM-ddTHH:mm:ssZ</c> with optional subseconds) by default.
    /// </summary>
    public static string FormatValue(Instant value, string? format, IFormatProvider? provider)
        => format is null
               ? NodaPatterns.Instant.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// The JSON fallback for <see cref="Instant"/> elements: the same ISO 8601 text form as
    /// <see cref="FormatValue"/>, so JSON, array literals and the wire form agree. Consulted only
    /// when no converter has claimed <see cref="Instant"/> — see
    /// <see cref="IValueSetFactory{TSet,T}.ElementJsonConverter"/>.
    /// </summary>
    static JsonConverter<Instant>? IValueSetFactory<InstantSet, Instant>.ElementJsonConverter
        => ValueSetTextElementJsonConverter<InstantSet, Instant>.Instance;

    /// <summary>
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-06-01T08:00:00Z}</c>, <c>{}</c>) into a
    /// <see cref="InstantSet"/>, normalizing to canonical form.
    /// </summary>
    public static InstantSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<InstantSet, Instant>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL array literal from a character span.</summary>
    public static InstantSet Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<InstantSet, Instant>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="InstantSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out InstantSet result)
        => SetFormat.TryParse<InstantSet, Instant>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out InstantSet result)
        => SetFormat.TryParse<InstantSet, Instant>(s, provider, out result);

    /// <summary>Structural equality — set equality over canonical form.</summary>
    public bool Equals(InstantSet? other)
        => other is not null && ValueSetCore.SetEquals(_elements, other._elements);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as InstantSet);

    /// <inheritdoc />
    public override int GetHashCode() => ValueSetCore.SetHashCode(_elements);

    /// <summary>Structural equality — delegates to <see cref="Equals(InstantSet)"/>.</summary>
    public static bool operator ==(InstantSet? left, InstantSet? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Structural inequality — the negation of <see cref="operator =="/>.</summary>
    public static bool operator !=(InstantSet? left, InstantSet? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

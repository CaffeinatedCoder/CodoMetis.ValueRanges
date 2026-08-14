using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
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
/// System.Text.Json serialization delegates elements to the configured serializer — register
/// NodaTime.Serialization.SystemTextJson's converters for ISO 8601 element output.
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
    /// Parses a PostgreSQL array literal (e.g. <c>{2024-06-01T08:00:00Z}</c>, <c>{}</c>) into a
    /// <see cref="InstantSet"/>, normalizing to canonical form.
    /// </summary>
    public static InstantSet Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<InstantSet, Instant>(s.AsSpan(), provider);

    /// <summary>
    /// Tries to parse a PostgreSQL array literal into a <see cref="InstantSet"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out InstantSet result)
        => SetFormat.TryParse<InstantSet, Instant>(s.AsSpan(), provider, out result);

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

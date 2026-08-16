using System.Collections.Immutable;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Provides abstract static factory methods for constructing value set instances, and default
/// implementations of <see cref="IParsable{TSelf}"/> and <see cref="IFormattable"/> using the
/// PostgreSQL array literal format (e.g. <c>{a,b}</c>, <c>{}</c>).
/// Implement this interface on a concrete set type to gain access to the set algebra extension
/// methods (<c>Overlaps</c>, <c>IsSubsetOf</c>, <c>Union</c>, …) as well as automatic
/// <see cref="System.Text.Json"/> support via <c>ValueSetJsonConverter&lt;TSet, T&gt;</c>.
/// </summary>
/// <typeparam name="TSet">The concrete set type being constructed.</typeparam>
/// <typeparam name="T">The element type of the set.</typeparam>
public interface IValueSetFactory<TSet, T> : ISpanParsable<TSet>, IFormattable
    where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
    where T : IEquatable<T>
{
    /// <summary>
    /// Returns the empty set — a set that contains no elements. Distinct from a NULL column:
    /// the empty set maps to the PostgreSQL empty array <c>{}</c>.
    /// </summary>
    abstract static TSet Empty { get; }

    /// <summary>
    /// Creates a set from the given values, deduplicating and sorting into canonical form.
    /// Rejects <see langword="null"/> elements.
    /// </summary>
    abstract static TSet From(IEnumerable<T> values);

    /// <inheritdoc cref="From(IEnumerable{T})"/>
    abstract static TSet From(params ReadOnlySpan<T> values);

    /// <summary>
    /// Wraps elements that are already in canonical form without re-normalizing.
    /// Callers must guarantee the canonical invariant (sorted by
    /// <see cref="CanonicalComparer"/>, deduplicated, no <see langword="null"/>s).
    /// </summary>
    internal abstract static TSet FromTrusted(ImmutableArray<T> elements);

    /// <summary>
    /// The comparer that defines the canonical element order. String-backed set types use
    /// ordinal comparison over the invariant text form — never a culture-sensitive comparison —
    /// so that every writer produces the identical array for the same set, keeping SQL <c>=</c>
    /// on the stored array equivalent to CLR set equality.
    /// </summary>
    virtual static IComparer<T> CanonicalComparer => Comparer<T>.Default;

    /// <summary>
    /// Parses a single element from its string representation.
    /// Called by the default <see cref="IParsable{TSelf}.Parse"/> implementation.
    /// </summary>
    abstract static T ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider);

    /// <summary>
    /// Formats a single element for inclusion in an array literal string.
    /// The default implementation delegates to <see cref="IFormattable"/> when available,
    /// and falls back to <see cref="object.ToString"/>. Override for types that require a
    /// specific canonical format (e.g. ISO 8601 for date types).
    /// </summary>
    virtual static string FormatValue(T value, string? format, IFormatProvider? provider)
        => value is IFormattable f ? f.ToString(format, provider) : value.ToString()!;

    /// <summary>
    /// A last-resort element converter for JSON, used only when
    /// <see cref="System.Text.Json"/> has no scalar converter for <typeparamref name="T"/> at
    /// all and would otherwise serialize it as a property dump.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Value sets serialize as plain JSON arrays and delegate their elements to
    /// <see cref="System.Text.Json"/>, which is the right default: it keeps element converters
    /// registered on the options, on the property and on the element type authoritative. It is
    /// also a trap for element types the serializer does not know — a type with no converter
    /// anywhere is written out as an object of its properties and read back as
    /// <see langword="default"/>, silently, on both legs.
    /// </para>
    /// <para>
    /// A set family whose element type is not natively serializable must override this to close
    /// that hole. It is consulted last, so registering a converter — by any of the three normal
    /// routes — still overrides it. The default is <see langword="null"/>: no fallback, delegate
    /// to <see cref="System.Text.Json"/> as before.
    /// </para>
    /// </remarks>
    virtual static JsonConverter<T>? ElementJsonConverter => null;

    // -----------------------------------------------------------------------
    // ISpanParsable<TSet> / IParsable<TSet> — default implementations
    //
    // The array literal grammar is parsed over a span throughout, so the string overloads are
    // the ones that widen: every entry point converges on SetFormat's span parser.
    // -----------------------------------------------------------------------

    /// <inheritdoc cref="IParsable{TSelf}.Parse"/>
    static TSet IParsable<TSet>.Parse(string s, IFormatProvider? provider)
        => SetFormat.Parse<TSet, T>(s.AsSpan(), provider);

    /// <inheritdoc cref="IParsable{TSelf}.TryParse"/>
    static bool IParsable<TSet>.TryParse(string? s, IFormatProvider? provider, out TSet result)
        => SetFormat.TryParse<TSet, T>(s.AsSpan(), provider, out result);

    /// <inheritdoc cref="ISpanParsable{TSelf}.Parse"/>
    static TSet ISpanParsable<TSet>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => SetFormat.Parse<TSet, T>(s, provider);

    /// <inheritdoc cref="ISpanParsable{TSelf}.TryParse"/>
    static bool ISpanParsable<TSet>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TSet result)
        => SetFormat.TryParse<TSet, T>(s, provider, out result);

    // -----------------------------------------------------------------------
    // IFormattable — default implementation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the PostgreSQL array literal representation of this set, e.g. <c>{a,b}</c> or
    /// <c>{}</c>. The optional <paramref name="format"/> string is forwarded to
    /// <see cref="FormatValue"/> for each element.
    /// </summary>
    /// <param name="format">
    /// A format string forwarded to the element formatter, or <see langword="null"/>
    /// to use the type-specific default (ISO 8601 for date/time types).
    /// </param>
    /// <param name="provider">
    /// An <see cref="IFormatProvider"/> forwarded to the element formatter;
    /// <see langword="null"/> defaults to the invariant culture.
    /// </param>
    string IFormattable.ToString(string? format, IFormatProvider? provider)
        => SetFormat.Format<TSet, T>((IValueSet<T>)this, format, provider);
}

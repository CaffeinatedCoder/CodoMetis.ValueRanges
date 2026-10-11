using System.Globalization;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Provides abstract static factory methods for constructing range instances, and
/// default implementations of <see cref="IParsable{TSelf}"/> and <see cref="IFormattable"/>
/// using the PostgreSQL range literal format (e.g. <c>[1,5)</c>, <c>empty</c>, <c>(,)</c>).
/// Every range type this library ships implements it; it is what the set operation extension
/// methods (<c>Intersect</c>, <c>Union</c>, <c>Except</c>), <see cref="RangeSet{TRange,T}"/> and the
/// JSON and <see cref="System.ComponentModel.TypeConverter"/> support are written against.
/// </summary>
/// <remarks>
/// The range interfaces are the shared vocabulary of the shipped range types, not an extension
/// point: implementing them outside this library is not supported. The engines, the EF Core provider
/// (whose registries are internal) and the parity tests against PostgreSQL cover only the shipped
/// types, and these interfaces may gain members in a minor version.
/// </remarks>
/// <typeparam name="TRange">The concrete range type being constructed.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IRangeFactory<TRange, T> : ISpanParsable<TRange>, IFormattable
    where TRange : IRangeFactory<TRange, T>
    where T : struct, IComparable<T>, IEquatable<T>
{
    /// <summary>
    /// Returns the empty range — a range that contains no values.
    /// </summary>
    abstract static TRange Empty { get; }

    /// <summary>
    /// Creates a range that is unbounded on both sides: <c>(-∞, +∞)</c> — the entire domain.
    /// </summary>
    abstract static TRange Infinite { get; }

    /// <summary>
    /// Creates a range bounded on both sides.
    /// Returns the empty range when <paramref name="start"/> is greater than
    /// <paramref name="end"/>, or when the bounds are equal but not both inclusive.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="startInclusive"><see langword="true"/> to include <paramref name="start"/> in the range.</param>
    /// <param name="endInclusive"><see langword="true"/> to include <paramref name="end"/> in the range.</param>
    abstract static TRange CreateFinite(T start, T end, bool startInclusive, bool endInclusive);

    /// <summary>
    /// Creates a range that is unbounded on the right: <c>[start, +∞)</c> or <c>(start, +∞)</c>.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive"><see langword="true"/> to include <paramref name="start"/> in the range.</param>
    abstract static TRange CreateUnboundedEnd(T start, bool startInclusive);

    /// <summary>
    /// Creates a range that is unbounded on the left: <c>(-∞, end]</c> or <c>(-∞, end)</c>.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive"><see langword="true"/> to include <paramref name="end"/> in the range.</param>
    abstract static TRange CreateUnboundedStart(T end, bool endInclusive);

    /// <summary>
    /// Whether the element domain has a step: every value below the maximum has an immediate
    /// successor. Discrete domains canonicalize to the closed form <c>[start, end]</c>, count
    /// their values, and can be enumerated; continuous ones can do none of those.
    /// </summary>
    /// <remarks>
    /// This cannot be derived from <see cref="NextValueAfter"/>, which returns
    /// <see langword="null"/> both for a continuous domain and for the last representable value
    /// of a discrete one. A type that overrides <see cref="NextValueAfter"/> must override this
    /// too — <c>RangeFactoryContractTests</c> checks that the two agree.
    /// </remarks>
    virtual static bool IsDiscrete => false;

    /// <summary>
    /// Returns the immediate successor of <paramref name="value"/> in a discrete domain.
    /// The default implementation returns <see langword="null"/>, marking the domain as continuous.
    /// Discrete domains also return <see langword="null"/> when <paramref name="value"/> is the
    /// last representable value.
    /// </summary>
    virtual static T? NextValueAfter(T value) => null;

    /// <summary>
    /// Returns the immediate predecessor of <paramref name="value"/> in a discrete domain.
    /// The default implementation returns <see langword="null"/>, marking the domain as continuous.
    /// </summary>
    virtual static T? PreviousValueBefore(T value) => null;

    /// <summary>
    /// Parses a single bound value from its string representation.
    /// Called by the default <see cref="IParsable{TSelf}.Parse"/> implementation.
    /// </summary>
    abstract static T ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider);

    /// <summary>
    /// Formats a single bound value for inclusion in a range literal string.
    /// The default implementation delegates to <see cref="IFormattable"/> when available,
    /// and falls back to <see cref="object.ToString"/>. Override for types that require a
    /// specific canonical format (e.g. ISO 8601 for date types).
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> provider means the invariant culture, never the current one — it is
    /// what string interpolation and <c>string.Format</c> pass, and the result is a range literal.
    /// Before 9.0.0 it fell through to the element's own <c>ToString</c>, so under <c>de-DE</c> a
    /// decimal range interpolated as <c>[1,5,2)</c>, which parses back as <c>[1,52)</c>.
    /// </remarks>
    virtual static string FormatValue(T value, string? format, IFormatProvider? provider)
        => value is IFormattable f ? f.ToString(format, provider ?? CultureInfo.InvariantCulture) : value.ToString()!;

    // -----------------------------------------------------------------------
    // ISpanParsable<TRange> / IParsable<TRange> — default implementations
    //
    // The literal grammar is parsed over a span throughout, so the string overloads are the
    // ones that widen: every entry point converges on RangeFormat's span parser.
    // -----------------------------------------------------------------------

    /// <inheritdoc cref="IParsable{TSelf}.Parse"/>
    static TRange IParsable<TRange>.Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<TRange, T>(s.AsSpan(), provider);

    /// <inheritdoc cref="IParsable{TSelf}.TryParse"/>
    static bool IParsable<TRange>.TryParse(string? s, IFormatProvider? provider, out TRange result)
        => RangeFormat.TryParse<TRange, T>(s.AsSpan(), provider, out result);

    /// <inheritdoc cref="ISpanParsable{TSelf}.Parse"/>
    static TRange ISpanParsable<TRange>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<TRange, T>(s, provider);

    /// <inheritdoc cref="ISpanParsable{TSelf}.TryParse"/>
    static bool ISpanParsable<TRange>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TRange result)
        => RangeFormat.TryParse<TRange, T>(s, provider, out result);

    // -----------------------------------------------------------------------
    // IFormattable — default implementation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the PostgreSQL range literal representation of this range.
    /// The optional <paramref name="format"/> string is forwarded to
    /// <see cref="FormatValue"/> for each bound value.
    /// </summary>
    /// <param name="format">
    /// A format string forwarded to the bound value formatter, or <see langword="null"/> or empty
    /// for the literal — the type-specific default (ISO 8601 for date/time types).
    /// </param>
    /// <param name="provider">
    /// An <see cref="IFormatProvider"/> forwarded to the bound value formatter alongside an explicit
    /// <paramref name="format"/>. Without one it is ignored: the literal is a wire format and is
    /// always invariant — a <c>th-TH</c> provider would otherwise write Buddhist-era years, and a
    /// <c>de-DE</c> one a decimal comma that collides with the bound separator.
    /// </param>
    string IFormattable.ToString(string? format, IFormatProvider? provider)
    {
        // An empty format is the general format, exactly as null is — .NET reads the two alike — so
        // it asks for the literal too. Normalized here so FormatValue sees one spelling of "none".
        if (string.IsNullOrEmpty(format)) format = null;
        var effective = format is null ? CultureInfo.InvariantCulture : provider;
        string Fmt(T v) => TRange.FormatValue(v, format, effective);
        return this switch
        {
            IEmptyRange<T>          => "empty",
            IInfinityRange<T>       => "(,)",
            IFiniteRange<T> f       => $"{(f.StartInclusive ? '[' : '(')}{Fmt(f.Start)},{Fmt(f.End)}{(f.EndInclusive ? ']' : ')')}",
            IUnboundedStartRange<T> us => $"(,{Fmt(us.End)}{(us.EndInclusive ? ']' : ')')}",
            IUnboundedEndRange<T>   ue => $"{(ue.StartInclusive ? '[' : '(')}{Fmt(ue.Start)},)",

            // All five shapes are named above, so this is only reachable through an implementation
            // of IRange<T> that is none of them — which the sealed-variant rule forbids. It
            // answered "empty" until 8.0.0, and that is the worst possible fallback here: this text
            // is what Parse round-trips, what the EF literal sends to PostgreSQL and what the shape
            // matrix compares, so an unrecognised range would have been silently stored, queried
            // and asserted as the empty range rather than rejected.
            _ => throw new InvalidOperationException($"Unknown range variant: {GetType()}.")
        };
    }
}

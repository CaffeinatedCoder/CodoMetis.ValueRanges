using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges.Core;

/// <summary>
/// Provides abstract static factory methods for constructing range instances, and
/// default implementations of <see cref="IParsable{TSelf}"/> and <see cref="IFormattable"/>
/// using the PostgreSQL range literal format (e.g. <c>[1,5)</c>, <c>empty</c>, <c>(,)</c>).
/// Implement this interface on a concrete range type to gain access to the set operation
/// extension methods (<c>Intersect</c>, <c>Union</c>, <c>Except</c>) as well as automatic
/// <see cref="System.Text.Json"/> support via <c>RangeJsonConverter&lt;TRange, T&gt;</c>.
/// </summary>
/// <typeparam name="TRange">The concrete range type being constructed.</typeparam>
/// <typeparam name="T">The element type of the range.</typeparam>
public interface IRangeFactory<TRange, T> : IParsable<TRange>, IFormattable
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
    virtual static string FormatValue(T value, string? format, IFormatProvider? provider)
        => value is IFormattable f ? f.ToString(format, provider) : value.ToString()!;

    // -----------------------------------------------------------------------
    // IParsable<TRange> — default implementations
    // -----------------------------------------------------------------------

    /// <inheritdoc cref="IParsable{TSelf}.Parse"/>
    static TRange IParsable<TRange>.Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<TRange, T>(s.AsSpan(), provider);

    /// <inheritdoc cref="IParsable{TSelf}.TryParse"/>
    static bool IParsable<TRange>.TryParse(string? s, IFormatProvider? provider, out TRange result)
        => RangeFormat.TryParse<TRange, T>(s.AsSpan(), provider, out result);

    // -----------------------------------------------------------------------
    // IFormattable — default implementation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the PostgreSQL range literal representation of this range.
    /// The optional <paramref name="format"/> string is forwarded to
    /// <see cref="FormatValue"/> for each bound value.
    /// </summary>
    /// <param name="format">
    /// A format string forwarded to the bound value formatter, or <see langword="null"/>
    /// to use the type-specific default (ISO 8601 for date/time types).
    /// </param>
    /// <param name="provider">
    /// An <see cref="IFormatProvider"/> forwarded to the bound value formatter.
    /// </param>
    string IFormattable.ToString(string? format, IFormatProvider? provider)
    {
        string Fmt(T v) => TRange.FormatValue(v, format, provider);
        return this switch
        {
            IEmptyRange<T>          => "empty",
            IInfinityRange<T>       => "(,)",
            IFiniteRange<T> f       => $"{(f.StartInclusive ? '[' : '(')}{Fmt(f.Start)},{Fmt(f.End)}{(f.EndInclusive ? ']' : ')')}",
            IUnboundedStartRange<T> us => $"(,{Fmt(us.End)}{(us.EndInclusive ? ']' : ')')}",
            IUnboundedEndRange<T>   ue => $"{(ue.StartInclusive ? '[' : '(')}{Fmt(ue.Start)},)",
            _                       => "empty"
        };
    }
}

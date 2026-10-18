using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="decimal"/> values, equivalent to the PostgreSQL <c>numrange</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for continuous numeric types such as monetary amounts.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<DecimalRange, decimal>))]
[TypeConverter(typeof(RangeTypeConverter<DecimalRange, decimal>))]
public abstract partial record DecimalRange : IRange<decimal>, IRangeFactory<DecimalRange, decimal>
{
    private DecimalRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="DecimalRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, DecimalRange, decimal>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, DecimalRange, decimal>))]
    public sealed partial record EmptyRange : DecimalRange, IEmptyRange<decimal>;

    /// <summary>
    /// Represents a <see cref="DecimalRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, DecimalRange, decimal>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, DecimalRange, decimal>))]
    public sealed partial record Finite : DecimalRange, IFiniteRange<decimal>
    {
        internal Finite(decimal start, decimal end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public decimal Start { get; }

        /// <inheritdoc/>
        public decimal End { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }

        /// <summary>
        /// Deconstructs the range into its bounds and their inclusiveness:
        /// <c>var (start, end, startInclusive, endInclusive) = finite;</c>.
        /// </summary>
        /// <remarks>
        /// There is deliberately no form without the inclusiveness: passing only the bounds back to
        /// <see cref="DecimalRange.CreateFinite"/> would silently apply its half-open default.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        /// <param name="startInclusive">Receives <see cref="StartInclusive"/>.</param>
        /// <param name="endInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out decimal start, out decimal end, out bool startInclusive, out bool endInclusive) =>
            (start, end, startInclusive, endInclusive) = (Start, End, StartInclusive, EndInclusive);
    }

    /// <summary>
    /// Represents a <see cref="DecimalRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, DecimalRange, decimal>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, DecimalRange, decimal>))]
    public sealed partial record UnboundedStart(decimal End, bool EndInclusive) : DecimalRange, IUnboundedStartRange<decimal>;

    /// <summary>
    /// Represents a <see cref="DecimalRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, DecimalRange, decimal>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, DecimalRange, decimal>))]
    public sealed partial record UnboundedEnd(decimal Start, bool StartInclusive) : DecimalRange, IUnboundedEndRange<decimal>;

    /// <summary>
    /// Represents a <see cref="DecimalRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, DecimalRange, decimal>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, DecimalRange, decimal>))]
    public sealed partial record Infinity : DecimalRange, IInfinityRange<decimal>;

    /// <summary>
    /// Creates a <see cref="DecimalRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DecimalRange CreateUnboundedStart(decimal end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="DecimalRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DecimalRange CreateUnboundedEnd(decimal start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="DecimalRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all decimal values.</returns>
    public static DecimalRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="DecimalRange"/> that contains no values.
    /// </summary>
    public static DecimalRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DecimalRange"/> bounded on both sides.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/> (half-open convention).
    /// </param>
    /// <returns>
    /// A <see cref="Finite"/> range when <paramref name="start"/> is strictly less than
    /// <paramref name="end"/>, or when they are equal and both bounds are inclusive.
    /// Returns <see cref="EmptyRange"/> when <paramref name="start"/> is greater than
    /// <paramref name="end"/>, or when the bounds are equal but not both inclusive.
    /// </returns>
    public static DecimalRange CreateFinite(
        decimal start,
        decimal end,
        bool    startInclusive = true,
        bool    endInclusive   = false
    ) =>
        start.CompareTo(end) switch
        {
            > 0 => Empty,
            0 => startInclusive && endInclusive
                     ? new Finite(start, end, startInclusive, endInclusive)
                     : Empty,
            _ => new Finite(start, end, startInclusive, endInclusive)
        };

    /// <summary>
    /// The span between the bounds, or <see langword="null"/> when the range is unbounded or the
    /// span is too large to represent. The empty range measures 0.
    /// </summary>
    /// <remarks>
    /// A span rather than a count: the domain is continuous, so there is nothing to count.
    /// Bound inclusiveness does not affect the span — <c>[1, 5]</c> and <c>[1, 5)</c> both
    /// measure 4.
    /// <para>
    /// A range straddling zero can be wider than <see cref="decimal"/> itself — the span of
    /// <c>[decimal.MinValue, decimal.MaxValue]</c> is twice <see cref="decimal.MaxValue"/> — and
    /// there is no wider type to compute it in. That case returns <see langword="null"/>, the same
    /// answer <c>Int64Range.Length</c> gives when a count exceeds <see cref="long.MaxValue"/>,
    /// rather than throwing <see cref="OverflowException"/> out of a property.
    /// </para>
    /// </remarks>
    public decimal? Length =>
        this switch
        {
            IEmptyRange<decimal>    => 0m,
            IFiniteRange<decimal> f => FiniteLength(f.Start, f.End),
            _                       => null
        };

    /// <summary>
    /// <c>end - start</c>, or <see langword="null"/> when that subtraction would overflow.
    /// </summary>
    /// <remarks>
    /// Only a negative lower bound can overflow: with <c>start >= 0</c> the difference is at most
    /// <c>end</c>. Otherwise the largest representable <c>end</c> is
    /// <c>decimal.MaxValue + start</c>, and the comparison is written that way so the check itself
    /// cannot overflow.
    /// </remarks>
    private static decimal? FiniteLength(decimal start, decimal end)
        => start < 0m && end > decimal.MaxValue + start ? null : end - start;

    /// <inheritdoc />
    /// <remarks>PostgreSQL's <c>numeric</c> grammar, refused where <see cref="decimal"/> would round the number
    /// the text spells (<see cref="DecimalLiteral"/>); the provider is ignored.</remarks>
    public static decimal ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => DecimalLiteral.Parse(s);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[1.5,9.9)</c>, <c>empty</c>, <c>(,)</c>)
    /// into a <see cref="DecimalRange"/>.
    /// </summary>
    public static DecimalRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<DecimalRange, decimal>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static DecimalRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<DecimalRange, decimal>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="DecimalRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DecimalRange result)
        => RangeFormat.TryParse<DecimalRange, decimal>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DecimalRange result)
        => RangeFormat.TryParse<DecimalRange, decimal>(s, provider, out result);

    /// <summary>
    /// Compares this range with <paramref name="other"/> in PostgreSQL's range order, the order
    /// <c>ORDER BY</c> and <c>&lt;</c> use on a range column: the empty range first, then by lower
    /// bound, then by upper bound.
    /// </summary>
    /// <remarks>
    /// An unbounded side is the smallest lower bound and the largest upper bound. At the same value
    /// an inclusive lower bound sorts first and an inclusive upper bound sorts last, so
    /// <c>[1,5)</c> sorts before <c>[1,5]</c>, which sorts before <c>(1,5)</c>. The result is 0 exactly
    /// when the two ranges are equal, and <see langword="null"/> sorts before every range.
    /// </remarks>
    /// <param name="other">The range to compare with.</param>
    /// <returns>
    /// A negative number when this range sorts first, 0 when the two are equal, and a positive
    /// number when <paramref name="other"/> sorts first or is <see langword="null"/>.
    /// </returns>
    public int CompareTo(DecimalRange? other) => RangeOrder.Compare<decimal>(this, other);

    /// <summary>
    /// Returns the range moved by <paramref name="delta"/>: both bounds move together and keep their
    /// inclusiveness, so <c>[1.5,2).Shift(0.5m)</c> is <c>[2.0,2.5)</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="delta"/> moves the range down.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="delta">How far to move the range.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="decimal"/> can hold.</exception>
    public DecimalRange Shift(decimal delta) => RangeArithmetic.Move<DecimalRange, decimal>(this, value => checked(value + delta), value => checked(value + delta));

    /// <summary>
    /// Returns the range grown by <paramref name="amount"/> on both sides: the lower bound moves down
    /// and the upper bound up, each keeping its inclusiveness, so <c>[1,2).Expand(0.5m)</c> is <c>[0.5,2.5)</c>.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="amount"/> shrinks the range, and a range shrunk past its own width is
    /// <see cref="Empty"/>. <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an
    /// unbounded side stays unbounded. This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="amount">How far to move each bound outward.</param>
    /// <returns>The grown, or shrunk, range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="decimal"/> can hold.</exception>
    public DecimalRange Expand(decimal amount) => RangeArithmetic.Move<DecimalRange, decimal>(this, value => checked(value - amount), value => checked(value + amount));

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
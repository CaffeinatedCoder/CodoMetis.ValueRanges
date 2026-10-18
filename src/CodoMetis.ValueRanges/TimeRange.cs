using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="TimeOnly"/> values — a time-of-day range, equivalent to a custom
/// PostgreSQL range type over <c>time</c> (<c>CREATE TYPE timerange AS RANGE (subtype = time)</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for continuous types and matches how opening hours and shifts compose:
/// <c>[09:00, 12:00)</c> and <c>[12:00, 17:00)</c> are adjacent, not overlapping.
/// </para>
/// <para>
/// A single range cannot cross midnight — a window such as 22:00–06:00 is two ranges,
/// naturally represented as a <see cref="RangeSet{TRange,T}"/> of two elements.
/// PostgreSQL's <c>time</c> additionally admits the special value <c>24:00:00</c>, which
/// <see cref="TimeOnly"/> cannot represent; express "until end of day" as an unbounded end
/// or an inclusive upper bound of <see cref="TimeOnly.MaxValue"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<TimeRange, TimeOnly>))]
[TypeConverter(typeof(RangeTypeConverter<TimeRange, TimeOnly>))]
public abstract partial record TimeRange : IRange<TimeOnly>, IRangeFactory<TimeRange, TimeOnly>
{
    private TimeRange()
    {
    }

    /// <summary>
    /// The copy constructor a <c>with</c> expression on a variant chains to.
    /// </summary>
    /// <remarks>
    /// A record's copy constructor cannot be private, so this check, not the private constructor
    /// above, is what keeps the union closed: a type declared outside the nested variants is
    /// refused here, before it can reach a switch.
    /// </remarks>
    /// <param name="original">The variant being copied.</param>
    /// <exception cref="InvalidOperationException">The runtime type is not one of the nested variants.</exception>
    protected TimeRange(TimeRange original) => ClosedUnion.Require(this);

    /// <summary>
    /// Represents an empty <see cref="TimeRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, TimeRange, TimeOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, TimeRange, TimeOnly>))]
    public sealed partial record EmptyRange : TimeRange, IEmptyRange<TimeOnly>;

    /// <summary>
    /// Represents a <see cref="TimeRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, TimeRange, TimeOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, TimeRange, TimeOnly>))]
    public sealed partial record Finite : TimeRange, IFiniteRange<TimeOnly>
    {
        internal Finite(TimeOnly start, TimeOnly end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public TimeOnly Start { get; }

        /// <inheritdoc/>
        public TimeOnly End { get; }

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
        /// <see cref="TimeRange.CreateFinite"/> would silently apply its half-open default.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        /// <param name="startInclusive">Receives <see cref="StartInclusive"/>.</param>
        /// <param name="endInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out TimeOnly start, out TimeOnly end, out bool startInclusive, out bool endInclusive) =>
            (start, end, startInclusive, endInclusive) = (Start, End, StartInclusive, EndInclusive);
    }

    /// <summary>
    /// Represents a <see cref="TimeRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, TimeRange, TimeOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, TimeRange, TimeOnly>))]
    public sealed partial record UnboundedStart(TimeOnly End, bool EndInclusive) : TimeRange, IUnboundedStartRange<TimeOnly>;

    /// <summary>
    /// Represents a <see cref="TimeRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, TimeRange, TimeOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, TimeRange, TimeOnly>))]
    public sealed partial record UnboundedEnd(TimeOnly Start, bool StartInclusive) : TimeRange, IUnboundedEndRange<TimeOnly>;

    /// <summary>
    /// Represents a <see cref="TimeRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, TimeRange, TimeOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, TimeRange, TimeOnly>))]
    public sealed partial record Infinity : TimeRange, IInfinityRange<TimeOnly>;

    /// <summary>
    /// Creates a <see cref="TimeRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static TimeRange CreateUnboundedStart(TimeOnly end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="TimeRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static TimeRange CreateUnboundedEnd(TimeOnly start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="TimeRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all time values.</returns>
    public static TimeRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="TimeRange"/> that contains no values.
    /// </summary>
    public static TimeRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="TimeRange"/> bounded on both sides.
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
    public static TimeRange CreateFinite(
        TimeOnly start,
        TimeOnly end,
        bool     startInclusive = true,
        bool     endInclusive   = false
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
    /// Cuts the range into consecutive pieces of <paramref name="step"/> each, from its start:
    /// <c>[09:00,17:30).Split(TimeSpan.FromMinutes(30))</c> is the 17 half-hour slots of the opening hours.
    /// </summary>
    /// <remarks>
    /// The pieces are adjacent and hold exactly the values of the range, so
    /// <c>RangeSet.From(range.Split(step))</c> is the range itself. The last piece is clipped to the range's end and keeps the range's upper bound. On a continuous type each piece but the last is half-open, so a closed range ends in a closed piece rather than a single point.
    /// The empty range yields nothing. This runs in memory only: PostgreSQL's
    /// <c>generate_series</c> returns rows, not a value.
    /// </remarks>
    /// <param name="step">The length of each piece; it must be positive.</param>
    /// <returns>The pieces, in order. The checks run at the call; the pieces are computed as you enumerate.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is zero or negative.</exception>
    public IEnumerable<TimeRange> Split(TimeSpan step)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        return RangeSplitting.Split<TimeRange, TimeOnly>(this, RangeSplitting.Steps<TimeOnly>(this, (start, n) => new TimeOnly(checked(start.Ticks + checked(n * step.Ticks)))));
    }

    /// <summary>
    /// The elapsed time between the bounds, or <see langword="null"/> when the range is
    /// unbounded. The empty range measures <see cref="TimeSpan.Zero"/>.
    /// </summary>
    /// <remarks>
    /// A span rather than a count: the domain is continuous. A window crossing midnight is two
    /// ranges rather than one, so this never wraps — measure the set, not a single range.
    /// </remarks>
    public TimeSpan? Length =>
        this switch
        {
            IEmptyRange<TimeOnly>    => TimeSpan.Zero,
            IFiniteRange<TimeOnly> f => f.End - f.Start,
            _                        => null
        };

    /// <inheritdoc />
    /// <remarks>Strict ISO 8601 — the forms this library writes and PostgreSQL's ISO output, plus
    /// <c>HH:mm</c> without seconds; the provider is ignored. A lenient parse read <c>01.02.2027</c> month first.</remarks>
    public static TimeOnly ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => IsoTemporal.ParseTime(s);

    /// <summary>
    /// Formats a <see cref="TimeOnly"/> value using the round-trip format specifier (<c>O</c>) by default,
    /// preserving full precision.
    /// </summary>
    public static string FormatValue(TimeOnly value, string? format, IFormatProvider? provider)
        => value.ToString(string.IsNullOrEmpty(format) ? "O" : format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[09:00:00,17:00:00)</c>, <c>empty</c>, <c>(,)</c>)
    /// into a <see cref="TimeRange"/>.
    /// </summary>
    public static TimeRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<TimeRange, TimeOnly>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static TimeRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<TimeRange, TimeOnly>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="TimeRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out TimeRange result)
        => RangeFormat.TryParse<TimeRange, TimeOnly>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out TimeRange result)
        => RangeFormat.TryParse<TimeRange, TimeOnly>(s, provider, out result);

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
    public int CompareTo(TimeRange? other) => RangeOrder.Compare<TimeOnly>(this, other);

    /// <inheritdoc cref="CompareTo(TimeRange)"/>
    /// <exception cref="ArgumentException"><paramref name="other"/> is not a <see cref="TimeRange"/>.</exception>
    int IComparable.CompareTo(object? other) => RangeOrder.CompareObject<TimeRange>(this, other, static (a, b) => a.CompareTo(b));

    /// <summary>
    /// Returns the range moved by <paramref name="delta"/>: both bounds move together and keep their
    /// inclusiveness, so <c>[09:00,10:00).Shift(TimeSpan.FromHours(2))</c> is <c>[11:00,12:00)</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="delta"/> moves the range down.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="delta">How far to move the range.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="TimeOnly"/> can hold.</exception>
    public TimeRange Shift(TimeSpan delta) => RangeArithmetic.Move<TimeRange, TimeOnly>(this, value => RangeArithmetic.AddWithinDay(value, delta), value => RangeArithmetic.AddWithinDay(value, delta));

    /// <summary>
    /// Returns the range grown by <paramref name="amount"/> on both sides: the lower bound moves down
    /// and the upper bound up, each keeping its inclusiveness, so <c>[09:00,10:00).Expand(TimeSpan.FromMinutes(15))</c> is <c>[08:45,10:15)</c>.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="amount"/> shrinks the range, and a range shrunk past its own width is
    /// <see cref="Empty"/>. <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an
    /// unbounded side stays unbounded. This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="amount">How far to move each bound outward.</param>
    /// <returns>The grown, or shrunk, range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="TimeOnly"/> can hold.</exception>
    public TimeRange Expand(TimeSpan amount) => RangeArithmetic.Move<TimeRange, TimeOnly>(this, value => RangeArithmetic.AddWithinDay(value, amount.Negate()), value => RangeArithmetic.AddWithinDay(value, amount));

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

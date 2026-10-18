using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateTimeOffset"/> values, equivalent to the PostgreSQL <c>tstzrange</c>
/// (timestamp with time zone) type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for timestamp ranges.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<DateTimeOffsetRange, DateTimeOffset>))]
[TypeConverter(typeof(RangeTypeConverter<DateTimeOffsetRange, DateTimeOffset>))]
public abstract partial record DateTimeOffsetRange : IRange<DateTimeOffset>, IRangeFactory<DateTimeOffsetRange, DateTimeOffset>
{
    private DateTimeOffsetRange()
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
    protected DateTimeOffsetRange(DateTimeOffsetRange original) => ClosedUnion.Require(this);

    /// <summary>
    /// Represents an empty <see cref="DateTimeOffsetRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, DateTimeOffsetRange, DateTimeOffset>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, DateTimeOffsetRange, DateTimeOffset>))]
    public sealed partial record EmptyRange : DateTimeOffsetRange, IEmptyRange<DateTimeOffset>;

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, DateTimeOffsetRange, DateTimeOffset>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, DateTimeOffsetRange, DateTimeOffset>))]
    public sealed partial record Finite : DateTimeOffsetRange, IFiniteRange<DateTimeOffset>
    {
        internal Finite(DateTimeOffset start, DateTimeOffset end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public DateTimeOffset Start { get; }

        /// <inheritdoc/>
        public DateTimeOffset End { get; }

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
        /// <see cref="DateTimeOffsetRange.CreateFinite"/> would silently apply its half-open default.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        /// <param name="startInclusive">Receives <see cref="StartInclusive"/>.</param>
        /// <param name="endInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out DateTimeOffset start, out DateTimeOffset end, out bool startInclusive, out bool endInclusive) =>
            (start, end, startInclusive, endInclusive) = (Start, End, StartInclusive, EndInclusive);
    }

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, DateTimeOffsetRange, DateTimeOffset>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, DateTimeOffsetRange, DateTimeOffset>))]
    public sealed partial record UnboundedStart(DateTimeOffset End, bool EndInclusive)
        : DateTimeOffsetRange, IUnboundedStartRange<DateTimeOffset>;

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, DateTimeOffsetRange, DateTimeOffset>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, DateTimeOffsetRange, DateTimeOffset>))]
    public sealed partial record UnboundedEnd(DateTimeOffset Start, bool StartInclusive)
        : DateTimeOffsetRange, IUnboundedEndRange<DateTimeOffset>;

    /// <summary>
    /// Represents a <see cref="DateTimeOffsetRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, DateTimeOffsetRange, DateTimeOffset>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, DateTimeOffsetRange, DateTimeOffset>))]
    public sealed partial record Infinity : DateTimeOffsetRange, IInfinityRange<DateTimeOffset>;

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DateTimeOffsetRange CreateUnboundedStart(DateTimeOffset end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DateTimeOffsetRange CreateUnboundedEnd(DateTimeOffset start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all timestamp-with-offset values.</returns>
    public static DateTimeOffsetRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="DateTimeOffsetRange"/> that contains no values.
    /// </summary>
    public static DateTimeOffsetRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateTimeOffsetRange"/> bounded on both sides.
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
    public static DateTimeOffsetRange CreateFinite(
        DateTimeOffset start,
        DateTimeOffset end,
        bool           startInclusive = true,
        bool           endInclusive   = false
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
    /// <c>[09:00,12:00).Split(TimeSpan.FromMinutes(30))</c> is the six half-hour slots from 09:00, at the start's offset.
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
    public IEnumerable<DateTimeOffsetRange> Split(TimeSpan step)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        return RangeSplitting.Split<DateTimeOffsetRange, DateTimeOffset>(this, RangeSplitting.Steps<DateTimeOffset>(this, (start, n) => start.AddTicks(checked(n * step.Ticks))));
    }

    /// <summary>
    /// The elapsed time between the bounds, or <see langword="null"/> when the range is
    /// unbounded. The empty range measures <see cref="TimeSpan.Zero"/>.
    /// </summary>
    /// <remarks>
    /// A span rather than a count: the domain is continuous. Both bounds denote instants, so the
    /// result is the real elapsed time even when the two carry different offsets.
    /// </remarks>
    public TimeSpan? Length =>
        this switch
        {
            IEmptyRange<DateTimeOffset>    => TimeSpan.Zero,
            IFiniteRange<DateTimeOffset> f => f.End - f.Start,
            _                              => null
        };

    /// <inheritdoc />
    /// <remarks>Strict ISO 8601 — the forms this library writes and PostgreSQL's ISO output, plus
    /// <c>infinity</c> / <c>-infinity</c> as the maximum / minimum value; the provider is ignored. A lenient parse read <c>01.02.2027</c> month first.</remarks>
    public static DateTimeOffset ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => IsoTemporal.ParseDateTimeOffset(s);

    /// <summary>
    /// Formats a <see cref="DateTimeOffset"/> value using the round-trip format specifier (<c>O</c>) by default,
    /// preserving full precision and UTC offset.
    /// </summary>
    public static string FormatValue(DateTimeOffset value, string? format, IFormatProvider? provider)
        => value.ToString(string.IsNullOrEmpty(format) ? "O" : format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal into a <see cref="DateTimeOffsetRange"/>.
    /// </summary>
    public static DateTimeOffsetRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<DateTimeOffsetRange, DateTimeOffset>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static DateTimeOffsetRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<DateTimeOffsetRange, DateTimeOffset>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="DateTimeOffsetRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeOffsetRange result)
        => RangeFormat.TryParse<DateTimeOffsetRange, DateTimeOffset>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTimeOffsetRange result)
        => RangeFormat.TryParse<DateTimeOffsetRange, DateTimeOffset>(s, provider, out result);

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
    public int CompareTo(DateTimeOffsetRange? other) => RangeOrder.Compare<DateTimeOffset>(this, other);

    /// <summary>
    /// Returns the range moved by <paramref name="delta"/>: both bounds move together and keep their
    /// inclusiveness, so <c>[09:00,10:00).Shift(TimeSpan.FromDays(7))</c> is the same hour a week later, at the same offset.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="delta"/> moves the range down.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// A bound at <see cref="DateTimeOffset.MinValue"/> or <see cref="DateTimeOffset.MaxValue"/> stands for PostgreSQL's <c>-infinity</c> or
    /// <c>infinity</c>, and it stays where it is, as <c>infinity ± d</c> does in PostgreSQL. A finite
    /// bound moved past either one still throws.
    /// </remarks>
    /// <param name="delta">How far to move the range.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="DateTimeOffset"/> can hold.</exception>
    public DateTimeOffsetRange Shift(TimeSpan delta) => RangeArithmetic.MoveKeepingInfinity<DateTimeOffsetRange, DateTimeOffset>(this, value => RangeArithmetic.Checked(() => value.Add(delta)), value => RangeArithmetic.Checked(() => value.Add(delta)), IsInfinity, IsInfinity);

    /// <summary>
    /// Returns the range grown by <paramref name="amount"/> on both sides: the lower bound moves down
    /// and the upper bound up, each keeping its inclusiveness, so <c>[09:00,10:00).Expand(TimeSpan.FromMinutes(15))</c> is <c>[08:45,10:15)</c>.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="amount"/> shrinks the range, and a range shrunk past its own width is
    /// <see cref="Empty"/>. <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an
    /// unbounded side stays unbounded. This runs in memory only: PostgreSQL has no range arithmetic.
    /// A bound at <see cref="DateTimeOffset.MinValue"/> or <see cref="DateTimeOffset.MaxValue"/> stands for PostgreSQL's <c>-infinity</c> or
    /// <c>infinity</c>, and it stays where it is, as <c>infinity ± d</c> does in PostgreSQL. A finite
    /// bound moved past either one still throws.
    /// </remarks>
    /// <param name="amount">How far to move each bound outward.</param>
    /// <returns>The grown, or shrunk, range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="DateTimeOffset"/> can hold.</exception>
    public DateTimeOffsetRange Expand(TimeSpan amount) => RangeArithmetic.MoveKeepingInfinity<DateTimeOffsetRange, DateTimeOffset>(this, value => RangeArithmetic.Checked(() => value.Subtract(amount)), value => RangeArithmetic.Checked(() => value.Add(amount)), IsInfinity, IsInfinity);

    /// <summary>
    /// Whether <paramref name="value"/> is an extreme that stands for PostgreSQL's <c>-infinity</c> or
    /// <c>infinity</c>, which <see cref="Shift"/> and <see cref="Expand"/> leave where it is (#224).
    /// </summary>
    private static bool IsInfinity(DateTimeOffset value) => value == DateTimeOffset.MinValue || value == DateTimeOffset.MaxValue;

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
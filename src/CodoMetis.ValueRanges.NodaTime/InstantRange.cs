using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="Instant"/> values, equivalent to the PostgreSQL <c>tstzrange</c>
/// (timestamp with time zone) type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for timestamp ranges.
/// An <see cref="Instant"/> is exactly what <c>timestamptz</c> stores — a point on the global
/// timeline with no offset or zone attached — so no normalization happens at the database
/// boundary. Zoned or offset values convert explicitly via <c>ZonedDateTime.ToInstant()</c> /
/// <c>OffsetDateTime.ToInstant()</c> before entering a range.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<InstantRange, Instant>))]
[TypeConverter(typeof(RangeTypeConverter<InstantRange, Instant>))]
public abstract partial record InstantRange : IRange<Instant>, IRangeFactory<InstantRange, Instant>
{
    private InstantRange()
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
    protected InstantRange(InstantRange original) => ClosedUnion.Require(this);

    /// <summary>
    /// Represents an empty <see cref="InstantRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, InstantRange, Instant>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, InstantRange, Instant>))]
    public sealed partial record EmptyRange : InstantRange, IEmptyRange<Instant>;

    /// <summary>
    /// Represents an <see cref="InstantRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, InstantRange, Instant>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, InstantRange, Instant>))]
    public sealed partial record Finite : InstantRange, IFiniteRange<Instant>
    {
        internal Finite(Instant start, Instant end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public Instant Start { get; }

        /// <inheritdoc/>
        public Instant End { get; }

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
        /// <see cref="InstantRange.CreateFinite"/> would silently apply its half-open default.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        /// <param name="startInclusive">Receives <see cref="StartInclusive"/>.</param>
        /// <param name="endInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out Instant start, out Instant end, out bool startInclusive, out bool endInclusive) =>
            (start, end, startInclusive, endInclusive) = (Start, End, StartInclusive, EndInclusive);
    }

    /// <summary>
    /// Represents an <see cref="InstantRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, InstantRange, Instant>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, InstantRange, Instant>))]
    public sealed partial record UnboundedStart(Instant End, bool EndInclusive) : InstantRange, IUnboundedStartRange<Instant>;

    /// <summary>
    /// Represents an <see cref="InstantRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, InstantRange, Instant>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, InstantRange, Instant>))]
    public sealed partial record UnboundedEnd(Instant Start, bool StartInclusive) : InstantRange, IUnboundedEndRange<Instant>;

    /// <summary>
    /// Represents an <see cref="InstantRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, InstantRange, Instant>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, InstantRange, Instant>))]
    public sealed partial record Infinity : InstantRange, IInfinityRange<Instant>;

    /// <summary>
    /// Creates an <see cref="InstantRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static InstantRange CreateUnboundedStart(Instant end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates an <see cref="InstantRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static InstantRange CreateUnboundedEnd(Instant start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates an <see cref="InstantRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all instants.</returns>
    public static InstantRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="InstantRange"/> that contains no values.
    /// </summary>
    public static InstantRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates an <see cref="InstantRange"/> bounded on both sides.
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
    public static InstantRange CreateFinite(
        Instant start,
        Instant end,
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
    /// Cuts the range into consecutive pieces of <paramref name="step"/> each, from its start:
    /// <c>[09:00Z,12:00Z).Split(Duration.FromMinutes(30))</c> is the six half-hour slots from 09:00Z.
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
    public IEnumerable<InstantRange> Split(Duration step)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, Duration.Zero);
        return RangeSplitting.Split<InstantRange, Instant>(this, RangeSplitting.Steps<Instant>(this, (start, n) => start.Plus(step * n)));
    }

    /// <summary>
    /// The elapsed time between the bounds, or <see langword="null"/> when the range is
    /// unbounded. The empty range measures <see cref="Duration.Zero"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="Duration"/>, not a <see cref="Period"/>: both bounds are instants on the
    /// time line, so the elapsed time is exact and calendar-independent.
    /// </remarks>
    public Duration? Length =>
        this switch
        {
            IEmptyRange<Instant>    => Duration.Zero,
            IFiniteRange<Instant> f => f.End - f.Start,
            _                       => null
        };

    /// <inheritdoc />
    public static Instant ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => NodaPatterns.ParseInstant(s.ToString());

    /// <summary>
    /// Formats an <see cref="Instant"/> value using extended ISO 8601
    /// (<c>uuuu-MM-ddTHH:mm:ss.FFFFFFFFFZ</c>, subsecond digits only when present) by default.
    /// </summary>
    public static string FormatValue(Instant value, string? format, IFormatProvider? provider)
        => string.IsNullOrEmpty(format)
               ? NodaPatterns.Instant.Format(value)
               : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[2024-01-01T00:00:00Z,2024-12-31T00:00:00Z)</c>,
    /// <c>empty</c>, <c>(,)</c>) into an <see cref="InstantRange"/>. Bounds are accepted in
    /// extended ISO 8601 form or in the PostgreSQL wire form with a numeric offset
    /// (e.g. <c>2024-06-01 14:30:00+02</c>) — offsets are converted to the instant they denote.
    /// </summary>
    public static InstantRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<InstantRange, Instant>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static InstantRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<InstantRange, Instant>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into an <see cref="InstantRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out InstantRange result)
        => RangeFormat.TryParse<InstantRange, Instant>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out InstantRange result)
        => RangeFormat.TryParse<InstantRange, Instant>(s, provider, out result);

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
    public int CompareTo(InstantRange? other) => RangeOrder.Compare<Instant>(this, other);

    /// <summary>
    /// Returns the range moved by <paramref name="delta"/>: both bounds move together and keep their
    /// inclusiveness, so <c>[09:00Z,10:00Z).Shift(Duration.FromHours(1))</c> is <c>[10:00Z,11:00Z)</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="delta"/> moves the range down.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// A bound at <see cref="Instant.MinValue"/> or <see cref="Instant.MaxValue"/> stands for PostgreSQL's <c>-infinity</c> or
    /// <c>infinity</c>, and it stays where it is, as <c>infinity ± d</c> does in PostgreSQL. A finite
    /// bound moved past either one still throws.
    /// </remarks>
    /// <param name="delta">How far to move the range.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="Instant"/> can hold.</exception>
    public InstantRange Shift(Duration delta) => RangeArithmetic.MoveKeepingInfinity<InstantRange, Instant>(this, value => RangeArithmetic.Checked(() => value.Plus(delta)), value => RangeArithmetic.Checked(() => value.Plus(delta)), IsInfinity, IsInfinity);

    /// <summary>
    /// Returns the range grown by <paramref name="amount"/> on both sides: the lower bound moves down
    /// and the upper bound up, each keeping its inclusiveness, so <c>[09:00Z,10:00Z).Expand(Duration.FromMinutes(15))</c> is <c>[08:45Z,10:15Z)</c>.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="amount"/> shrinks the range, and a range shrunk past its own width is
    /// <see cref="Empty"/>. <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an
    /// unbounded side stays unbounded. This runs in memory only: PostgreSQL has no range arithmetic.
    /// A bound at <see cref="Instant.MinValue"/> or <see cref="Instant.MaxValue"/> stands for PostgreSQL's <c>-infinity</c> or
    /// <c>infinity</c>, and it stays where it is, as <c>infinity ± d</c> does in PostgreSQL. A finite
    /// bound moved past either one still throws.
    /// </remarks>
    /// <param name="amount">How far to move each bound outward.</param>
    /// <returns>The grown, or shrunk, range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="Instant"/> can hold.</exception>
    public InstantRange Expand(Duration amount) => RangeArithmetic.MoveKeepingInfinity<InstantRange, Instant>(this, value => RangeArithmetic.Checked(() => value.Minus(amount)), value => RangeArithmetic.Checked(() => value.Plus(amount)), IsInfinity, IsInfinity);

    /// <summary>
    /// Whether <paramref name="value"/> is an extreme that stands for PostgreSQL's <c>-infinity</c> or
    /// <c>infinity</c>, which <see cref="Shift"/> and <see cref="Expand"/> leave where it is (#224).
    /// </summary>
    private static bool IsInfinity(Instant value) => value == Instant.MinValue || value == Instant.MaxValue;

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

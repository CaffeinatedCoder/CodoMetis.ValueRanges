using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateTime"/> values, equivalent to the PostgreSQL <c>tsrange</c>
/// (timestamp without time zone) type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for timestamp ranges.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<DateTimeRange, DateTime>))]
[TypeConverter(typeof(RangeTypeConverter<DateTimeRange, DateTime>))]
public abstract partial record DateTimeRange : IRange<DateTime>, IRangeFactory<DateTimeRange, DateTime>
{
    private DateTimeRange()
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
    protected DateTimeRange(DateTimeRange original) => ClosedUnion.Require(this);

    /// <summary>
    /// Represents an empty <see cref="DateTimeRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, DateTimeRange, DateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, DateTimeRange, DateTime>))]
    public sealed partial record EmptyRange : DateTimeRange, IEmptyRange<DateTime>;

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, DateTimeRange, DateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, DateTimeRange, DateTime>))]
    public sealed partial record Finite : DateTimeRange, IFiniteRange<DateTime>
    {
        internal Finite(DateTime start, DateTime end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public DateTime Start { get; }

        /// <inheritdoc/>
        public DateTime End { get; }

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
        /// <see cref="DateTimeRange.CreateFinite"/> would silently apply its half-open default.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        /// <param name="startInclusive">Receives <see cref="StartInclusive"/>.</param>
        /// <param name="endInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out DateTime start, out DateTime end, out bool startInclusive, out bool endInclusive) =>
            (start, end, startInclusive, endInclusive) = (Start, End, StartInclusive, EndInclusive);
    }

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, DateTimeRange, DateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, DateTimeRange, DateTime>))]
    public sealed partial record UnboundedStart(DateTime End, bool EndInclusive) : DateTimeRange, IUnboundedStartRange<DateTime>;

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, DateTimeRange, DateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, DateTimeRange, DateTime>))]
    public sealed partial record UnboundedEnd(DateTime Start, bool StartInclusive) : DateTimeRange, IUnboundedEndRange<DateTime>;

    /// <summary>
    /// Represents a <see cref="DateTimeRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, DateTimeRange, DateTime>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, DateTimeRange, DateTime>))]
    public sealed partial record Infinity : DateTimeRange, IInfinityRange<DateTime>;

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DateTimeRange CreateUnboundedStart(DateTime end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DateTimeRange CreateUnboundedEnd(DateTime start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all timestamp values.</returns>
    public static DateTimeRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="DateTimeRange"/> that contains no values.
    /// </summary>
    public static DateTimeRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateTimeRange"/> bounded on both sides.
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
    public static DateTimeRange CreateFinite(
        DateTime start,
        DateTime end,
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
    /// The elapsed time between the bounds, or <see langword="null"/> when the range is
    /// unbounded. The empty range measures <see cref="TimeSpan.Zero"/>.
    /// </summary>
    /// <remarks>
    /// A span rather than a count: the domain is continuous. Bound inclusiveness does not affect
    /// it. The bounds are wall-clock times, so the result does not account for any offset or
    /// daylight-saving transition between them.
    /// </remarks>
    public TimeSpan? Length =>
        this switch
        {
            IEmptyRange<DateTime>    => TimeSpan.Zero,
            IFiniteRange<DateTime> f => f.End - f.Start,
            _                        => null
        };

    /// <inheritdoc />
    /// <remarks>Strict ISO 8601 — the forms this library writes and PostgreSQL's ISO output, plus
    /// <c>infinity</c> / <c>-infinity</c> as the maximum / minimum value; the provider is ignored. A lenient parse read <c>01.02.2027</c> month first.</remarks>
    public static DateTime ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => IsoTemporal.ParseDateTime(s);

    /// <summary>
    /// Formats a <see cref="DateTime"/> value using the round-trip format specifier (<c>O</c>) by default,
    /// preserving full precision and <see cref="DateTimeKind"/>.
    /// </summary>
    public static string FormatValue(DateTime value, string? format, IFormatProvider? provider)
        => value.ToString(string.IsNullOrEmpty(format) ? "O" : format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[2024-01-01T00:00:00,2024-12-31T23:59:59)</c>,
    /// <c>empty</c>, <c>(,)</c>) into a <see cref="DateTimeRange"/>.
    /// </summary>
    public static DateTimeRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<DateTimeRange, DateTime>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static DateTimeRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<DateTimeRange, DateTime>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="DateTimeRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateTimeRange result)
        => RangeFormat.TryParse<DateTimeRange, DateTime>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateTimeRange result)
        => RangeFormat.TryParse<DateTimeRange, DateTime>(s, provider, out result);

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
    public int CompareTo(DateTimeRange? other) => RangeOrder.Compare<DateTime>(this, other);

    /// <summary>
    /// Returns the range moved by <paramref name="delta"/>: both bounds move together and keep their
    /// inclusiveness, so <c>[09:00,10:00).Shift(TimeSpan.FromDays(7))</c> is the same hour a week later.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="delta"/> moves the range down.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="delta">How far to move the range.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="DateTime"/> can hold.</exception>
    public DateTimeRange Shift(TimeSpan delta) => RangeArithmetic.Move<DateTimeRange, DateTime>(this, value => RangeArithmetic.Checked(() => value.Add(delta)), value => RangeArithmetic.Checked(() => value.Add(delta)));

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
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="DateTime"/> can hold.</exception>
    public DateTimeRange Expand(TimeSpan amount) => RangeArithmetic.Move<DateTimeRange, DateTime>(this, value => RangeArithmetic.Checked(() => value.Subtract(amount)), value => RangeArithmetic.Checked(() => value.Add(amount)));

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
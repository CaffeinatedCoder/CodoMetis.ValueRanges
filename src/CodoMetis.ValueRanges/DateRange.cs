using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over <see cref="DateOnly"/> values, equivalent to the PostgreSQL <c>daterange</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[start, end]</c>.
/// As a discrete type, two ranges whose boundaries are exactly one day apart are considered adjacent.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<DateRange, DateOnly>))]
[TypeConverter(typeof(RangeTypeConverter<DateRange, DateOnly>))]
public abstract record DateRange : IRange<DateOnly>, IRangeFactory<DateRange, DateOnly>
{
    private DateRange()
    {
    }

    /// <summary>
    /// Represents an empty <see cref="DateRange"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, DateRange, DateOnly>))]
    public sealed record EmptyRange : DateRange, IEmptyRange<DateOnly>;

    /// <summary>
    /// Represents a <see cref="DateRange"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, DateRange, DateOnly>))]
    public sealed record Finite : DateRange, IFiniteRange<DateOnly>
    {
        internal Finite(DateOnly start, DateOnly end)
        {
            Start = start;
            End   = end;
        }

        /// <inheritdoc/>
        public DateOnly Start { get; }

        /// <inheritdoc/>
        public DateOnly End { get; }

        /// <inheritdoc/>
        public bool StartInclusive => true;

        /// <inheritdoc/>
        public bool EndInclusive => true;

        /// <summary>
        /// Deconstructs the range into its bounds: <c>var (start, end) = finite;</c>.
        /// </summary>
        /// <remarks>
        /// Both bounds are inclusive — the canonical form of a discrete range — so the pair passes
        /// straight back to <see cref="DateRange.CreateFinite"/>, whose default is closed.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        public void Deconstruct(out DateOnly start, out DateOnly end) => (start, end) = (Start, End);
    }

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, DateRange, DateOnly>))]
    public sealed record UnboundedStart(DateOnly End) : DateRange, IUnboundedStartRange<DateOnly>
    {
        /// <inheritdoc />
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, DateRange, DateOnly>))]
    public sealed record UnboundedEnd(DateOnly Start) : DateRange, IUnboundedEndRange<DateOnly>
    {
        /// <inheritdoc />
        public bool StartInclusive => true;
    }

    /// <summary>
    /// Represents a <see cref="DateRange"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, DateRange, DateOnly>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, DateRange, DateOnly>))]
    public sealed record Infinity : DateRange, IInfinityRange<DateOnly>;

    /// <summary>
    /// Creates a <see cref="DateRange"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static DateRange CreateUnboundedStart(DateOnly end, bool endInclusive = false) =>
        endInclusive
            ? new UnboundedStart(end)
            : PreviousValueBefore(end) is { } e
                ? new UnboundedStart(e)
                : Empty;

    /// <summary>
    /// Creates a <see cref="DateRange"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static DateRange CreateUnboundedEnd(DateOnly start, bool startInclusive = true) =>
        startInclusive
            ? new UnboundedEnd(start)
            : NextValueAfter(start) is { } s
                ? new UnboundedEnd(s)
                : Empty;

    /// <summary>
    /// Creates a <see cref="DateRange"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all date values.</returns>
    public static DateRange Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="DateRange"/> that contains no values.
    /// </summary>
    public static DateRange Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="DateRange"/> bounded on both sides.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Finite"/> range when <paramref name="start"/> is strictly less than
    /// <paramref name="end"/>, or when they are equal and both bounds are inclusive.
    /// Returns <see cref="EmptyRange"/> when <paramref name="start"/> is greater than
    /// <paramref name="end"/>, or when the bounds are equal but not both inclusive.
    /// </returns>
    public static DateRange CreateFinite(
        DateOnly start,
        DateOnly end,
        bool     startInclusive = true,
        bool     endInclusive   = true
    ) => DiscreteCanonical.Finite<DateRange, DateOnly>(start, end, startInclusive, endInclusive) is { } b
             ? new Finite(b.Start, b.End)
             : Empty;

    /// <inheritdoc />
    public static bool IsDiscrete => true;

    /// <inheritdoc />
    public static DateOnly? NextValueAfter(DateOnly value) => value == DateOnly.MaxValue ? null : value.AddDays(1);

    /// <inheritdoc />
    public static DateOnly? PreviousValueBefore(DateOnly value) => value == DateOnly.MinValue ? null : value.AddDays(-1);

    /// <summary>
    /// Enumerates the dates the range contains, ascending and inclusive of both bounds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Available here because the domain is discrete. The continuous range types have no
    /// step to walk and do not declare this member at all, so asking for the values of a
    /// <c>DecimalRange</c> is a compile error rather than a runtime failure.
    /// </para>
    /// <para>
    /// The empty range yields nothing. An unbounded range would not terminate and throws
    /// immediately — the check is eager, so the failure surfaces at this call rather than at
    /// the <c>foreach</c> that consumes it.
    /// </para>
    /// </remarks>
    /// <returns>The contained dates, ascending.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    public IEnumerable<DateOnly> Values() => DiscreteEnumeration.Values<DateRange, DateOnly>(this);

    /// <summary>
    /// The number of days the range covers, or <see langword="null"/> when it is unbounded.
    /// The empty range measures 0.
    /// </summary>
    /// <remarks>
    /// Inclusive of both ends, because the domain is discrete and canonicalizes closed:
    /// <c>[2024-01-01, 2024-01-31]</c> measures 31, which is the answer a calendar gives.
    /// </remarks>
    public int? Length =>
        this switch
        {
            IEmptyRange<DateOnly>    => 0,
            IFiniteRange<DateOnly> f => f.End.DayNumber - f.Start.DayNumber + 1,
            _                        => null
        };

    /// <inheritdoc />
    /// <remarks>Strict ISO 8601 — the forms this library writes and PostgreSQL's ISO output, plus
    /// <c>infinity</c> / <c>-infinity</c> as the maximum / minimum value; the provider is ignored. A lenient parse read <c>01.02.2027</c> month first.</remarks>
    public static DateOnly ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => IsoTemporal.ParseDate(s);

    /// <summary>
    /// Formats a <see cref="DateOnly"/> value using ISO 8601 (<c>yyyy-MM-dd</c>) by default.
    /// </summary>
    public static string FormatValue(DateOnly value, string? format, IFormatProvider? provider)
        => value.ToString(string.IsNullOrEmpty(format) ? "yyyy-MM-dd" : format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[2024-01-01,2024-12-31]</c>, <c>empty</c>, <c>(,)</c>)
    /// into a <see cref="DateRange"/>.
    /// </summary>
    public static DateRange Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<DateRange, DateOnly>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static DateRange Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<DateRange, DateOnly>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="DateRange"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out DateRange result)
        => RangeFormat.TryParse<DateRange, DateOnly>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DateRange result)
        => RangeFormat.TryParse<DateRange, DateOnly>(s, provider, out result);

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
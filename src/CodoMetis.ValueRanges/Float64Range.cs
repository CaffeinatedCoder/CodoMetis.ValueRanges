using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over finite <see cref="double"/> values, equivalent to a PostgreSQL <c>float8range</c> — a
/// custom range type the database declares with <c>CREATE TYPE float8range AS RANGE (subtype = float8)</c>.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a half-open interval <c>[start, end)</c>,
/// which is conventional for continuous numeric types.
/// <para>
/// Bounds and operands are finite: <see cref="double.NaN"/>, <see cref="double.PositiveInfinity"/> and
/// <see cref="double.NegativeInfinity"/> throw <see cref="ArgumentException"/> from every factory, from
/// <c>Contains</c> and <c>Clamp</c>, and make <c>Parse</c> throw and <c>TryParse</c> answer
/// <see langword="false"/>. PostgreSQL sorts <c>NaN</c> above every number and .NET below, so no
/// comparison involving it could agree with the server; an infinity only repeats what an unbounded side
/// says. Without them, <see cref="double.CompareTo(double)"/>, the IEEE operators and PostgreSQL agree on
/// every pair, <c>-0</c> equal to <c>0</c> included.
/// </para>
/// <para>
/// The domain is continuous and never canonicalized: <c>(1, 1.0000000000000002)</c> holds no double, yet
/// it is not empty, as in PostgreSQL. Comparisons are exact; there is no epsilon.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<Float64Range, double>))]
[TypeConverter(typeof(RangeTypeConverter<Float64Range, double>))]
public abstract partial record Float64Range : IRange<double>, IRangeFactory<Float64Range, double>
{
    private Float64Range()
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
    protected Float64Range(Float64Range original) => ClosedUnion.Require(this);

    private static double RequireFinite(double value, string paramName)
        => FloatLiteral.RequireFinite(value, nameof(Float64Range), paramName);

    double IRange<double>.NormalizeElement(double value) => RequireFinite(value, nameof(value));

    /// <summary>
    /// Represents an empty <see cref="Float64Range"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, Float64Range, double>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, Float64Range, double>))]
    public sealed partial record EmptyRange : Float64Range, IEmptyRange<double>;

    /// <summary>
    /// Represents a <see cref="Float64Range"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, Float64Range, double>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, Float64Range, double>))]
    public sealed partial record Finite : Float64Range, IFiniteRange<double>
    {
        internal Finite(double start, double end, bool startInclusive, bool endInclusive)
        {
            Start          = start;
            End            = end;
            StartInclusive = startInclusive;
            EndInclusive   = endInclusive;
        }

        /// <inheritdoc/>
        public double Start { get; }

        /// <inheritdoc/>
        public double End { get; }

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
        /// <see cref="Float64Range.CreateFinite"/> would silently apply its half-open default.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        /// <param name="startInclusive">Receives <see cref="StartInclusive"/>.</param>
        /// <param name="endInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out double start, out double end, out bool startInclusive, out bool endInclusive) =>
            (start, end, startInclusive, endInclusive) = (Start, End, StartInclusive, EndInclusive);
    }

    /// <summary>
    /// Represents a <see cref="Float64Range"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <remarks>
    /// Not a positional record, unlike the other range types: its bound is checked in the constructor,
    /// and a positional record's <c>init</c> accessor would let <c>with { End = double.NaN }</c> past it.
    /// </remarks>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, Float64Range, double>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, Float64Range, double>))]
    public sealed partial record UnboundedStart : Float64Range, IUnboundedStartRange<double>
    {
        /// <summary>Creates a range unbounded on the left.</summary>
        /// <param name="End">The upper (right) bound of the range; finite.</param>
        /// <param name="EndInclusive"><see langword="true"/> to include <paramref name="End"/> in the range.</param>
        /// <exception cref="ArgumentException"><paramref name="End"/> is <c>NaN</c> or an infinity.</exception>
        public UnboundedStart(double End, bool EndInclusive)
        {
            this.End          = RequireFinite(End, nameof(End));
            this.EndInclusive = EndInclusive;
        }

        /// <inheritdoc/>
        public double End { get; }

        /// <inheritdoc/>
        public bool EndInclusive { get; }

        /// <summary>
        /// Deconstructs the range into its upper bound and its inclusiveness, as the positional
        /// variants of the other range types do: <c>range is Float64Range.UnboundedStart(var end, var inclusive)</c>.
        /// </summary>
        /// <param name="End">Receives <see cref="End"/>.</param>
        /// <param name="EndInclusive">Receives <see cref="EndInclusive"/>.</param>
        public void Deconstruct(out double End, out bool EndInclusive) => (End, EndInclusive) = (this.End, this.EndInclusive);
    }

    /// <summary>
    /// Represents a <see cref="Float64Range"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <remarks>
    /// Not a positional record, unlike the other range types: its bound is checked in the constructor,
    /// and a positional record's <c>init</c> accessor would let <c>with { Start = double.NaN }</c> past it.
    /// </remarks>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, Float64Range, double>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, Float64Range, double>))]
    public sealed partial record UnboundedEnd : Float64Range, IUnboundedEndRange<double>
    {
        /// <summary>Creates a range unbounded on the right.</summary>
        /// <param name="Start">The lower (left) bound of the range; finite.</param>
        /// <param name="StartInclusive"><see langword="true"/> to include <paramref name="Start"/> in the range.</param>
        /// <exception cref="ArgumentException"><paramref name="Start"/> is <c>NaN</c> or an infinity.</exception>
        public UnboundedEnd(double Start, bool StartInclusive)
        {
            this.Start          = RequireFinite(Start, nameof(Start));
            this.StartInclusive = StartInclusive;
        }

        /// <inheritdoc/>
        public double Start { get; }

        /// <inheritdoc/>
        public bool StartInclusive { get; }

        /// <summary>
        /// Deconstructs the range into its lower bound and its inclusiveness, as the positional
        /// variants of the other range types do: <c>range is Float64Range.UnboundedEnd(var start, var inclusive)</c>.
        /// </summary>
        /// <param name="Start">Receives <see cref="Start"/>.</param>
        /// <param name="StartInclusive">Receives <see cref="StartInclusive"/>.</param>
        public void Deconstruct(out double Start, out bool StartInclusive) => (Start, StartInclusive) = (this.Start, this.StartInclusive);
    }

    /// <summary>
    /// Represents a <see cref="Float64Range"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, Float64Range, double>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, Float64Range, double>))]
    public sealed partial record Infinity : Float64Range, IInfinityRange<double>;

    /// <summary>
    /// Creates a <see cref="Float64Range"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range; finite.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="end"/> is <c>NaN</c> or an infinity.</exception>
    public static Float64Range CreateUnboundedStart(double end, bool endInclusive = false)
        => new UnboundedStart(end, endInclusive);

    /// <summary>
    /// Creates a <see cref="Float64Range"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range; finite.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="start"/> is <c>NaN</c> or an infinity.</exception>
    public static Float64Range CreateUnboundedEnd(double start, bool startInclusive = true)
        => new UnboundedEnd(start, startInclusive);

    /// <summary>
    /// Creates a <see cref="Float64Range"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all finite double values.</returns>
    public static Float64Range Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="Float64Range"/> that contains no values.
    /// </summary>
    public static Float64Range Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates a <see cref="Float64Range"/> bounded on both sides.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range; finite.</param>
    /// <param name="end">The upper (right) bound of the range; finite.</param>
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
    /// <exception cref="ArgumentException">A bound is <c>NaN</c> or an infinity.</exception>
    public static Float64Range CreateFinite(
        double start,
        double end,
        bool   startInclusive = true,
        bool   endInclusive   = false
    ) =>
        RequireFinite(start, nameof(start)).CompareTo(RequireFinite(end, nameof(end))) switch
        {
            > 0 => Empty,
            0 => startInclusive && endInclusive
                     ? new Finite(start, end, startInclusive, endInclusive)
                     : Empty,
            _ => new Finite(start, end, startInclusive, endInclusive)
        };

    /// <summary>
    /// Cuts the range into consecutive pieces of <paramref name="step"/> each, from its start:
    /// <c>Float64Range.CreateFinite(0, 10).Split(4)</c> is <c>[0,4)</c>, <c>[4,8)</c>, <c>[8,10)</c>.
    /// </summary>
    /// <remarks>
    /// The pieces are adjacent and hold exactly the values of the range, so
    /// <c>RangeSet.From(range.Split(step))</c> is the range itself. The last piece is clipped to the range's end and keeps the range's upper bound. On a continuous type each piece but the last is half-open, so a closed range ends in a closed piece rather than a single point.
    /// The empty range yields nothing. This runs in memory only: PostgreSQL's
    /// <c>generate_series</c> returns rows, not a value.
    /// <para>
    /// The <c>n</c>th boundary is <c>Start + n × step</c>, each rounded to the nearest double, so the error
    /// does not accumulate from piece to piece. Where <c>n × step</c> alone would overflow while the
    /// boundary still fits, the boundary is the previous one plus the step. A step too small to move
    /// the start at its magnitude throws rather than looping.
    /// </para>
    /// </remarks>
    /// <param name="step">The length of each piece; it must be positive and finite.</param>
    /// <returns>The pieces, in order. The checks run at the call; the pieces are computed as you enumerate.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    /// <exception cref="ArgumentException"><paramref name="step"/> is <c>NaN</c> or an infinity.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is zero or negative, or too small to move the start.</exception>
    public IEnumerable<Float64Range> Split(double step)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RequireFinite(step, nameof(step)));
        return RangeSplitting.Split<Float64Range, double>(this, RangeSplitting.Steps<double>(this, (start, n) => Moved(start + n * step), previous => Moved(previous + step)));
    }

    /// <summary>
    /// The span between the bounds, or <see langword="null"/> when the range is unbounded. The empty
    /// range measures 0.
    /// </summary>
    /// <remarks>
    /// A span rather than a count: the domain is continuous, so there is nothing to count.
    /// Bound inclusiveness does not affect the span — <c>[1, 5]</c> and <c>[1, 5)</c> both
    /// measure 4.
    /// <para>
    /// The span is the IEEE difference <c>End - Start</c>, rounded to the nearest double, and it can
    /// exceed <see cref="double.MaxValue"/>: <c>[double.MinValue, double.MaxValue]</c> measures
    /// <see cref="double.PositiveInfinity"/>. That infinity is an answer, not a bound — the range is
    /// finite and holds only finite values — so it is returned rather than <see langword="null"/>, which
    /// means unbounded here.
    /// </para>
    /// </remarks>
    public double? Length =>
        this switch
        {
            IEmptyRange<double>    => 0d,
            IFiniteRange<double> f => f.End - f.Start,
            _                      => null
        };

    /// <inheritdoc />
    /// <remarks>PostgreSQL's <c>float8in</c> grammar for a finite number, refused where it would be out of
    /// range there (<see cref="FloatLiteral"/>); the provider is ignored.</remarks>
    public static double ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => FloatLiteral.ParseDouble(s);

    /// <inheritdoc />
    /// <remarks>
    /// The text PostgreSQL's <c>float8out</c> prints — the shortest that reads back as the value, with a
    /// lowercase exponent from <c>1e+15</c> up and below <c>0.0001</c> — rather than .NET's shortest
    /// form, which differs in digits for about one double in three hundred and does not read back at all
    /// for two (<see cref="FloatLiteral"/>). An explicit format applies as for any number.
    /// </remarks>
    public static string FormatValue(double value, string? format, IFormatProvider? provider)
        => format is null ? FloatLiteral.Format(value) : value.ToString(format, provider ?? CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[1.5,9.9)</c>, <c>empty</c>, <c>(,)</c>)
    /// into a <see cref="Float64Range"/>.
    /// </summary>
    public static Float64Range Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<Float64Range, double>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static Float64Range Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<Float64Range, double>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into a <see cref="Float64Range"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Float64Range result)
        => RangeFormat.TryParse<Float64Range, double>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Float64Range result)
        => RangeFormat.TryParse<Float64Range, double>(s, provider, out result);

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
    public int CompareTo(Float64Range? other) => RangeOrder.Compare<double>(this, other);

    /// <summary>
    /// Returns the range moved by <paramref name="delta"/>: both bounds move together and keep their
    /// inclusiveness, so <c>[1.5,2).Shift(0.5)</c> is <c>[2,2.5)</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="delta"/> moves the range down.
    /// Each moved bound is rounded to the nearest double, as any addition is, so a range narrower than
    /// that rounding at its new magnitude can come back <see cref="Empty"/>.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="delta">How far to move the range; it must be finite.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="ArgumentException"><paramref name="delta"/> is <c>NaN</c> or an infinity.</exception>
    /// <exception cref="OverflowException">A moved bound is outside the finite values <see cref="double"/> can hold.</exception>
    public Float64Range Shift(double delta)
    {
        RequireFinite(delta, nameof(delta));
        return RangeArithmetic.Move<Float64Range, double>(this, value => Moved(value + delta), value => Moved(value + delta));
    }

    /// <summary>
    /// Returns the range grown by <paramref name="amount"/> on both sides: the lower bound moves down
    /// and the upper bound up, each keeping its inclusiveness, so <c>[1,2).Expand(0.5)</c> is <c>[0.5,2.5)</c>.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="amount"/> shrinks the range, and a range shrunk past its own width is
    /// <see cref="Empty"/>. <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an
    /// unbounded side stays unbounded. Each moved bound is rounded to the nearest double.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="amount">How far to move each bound outward; it must be finite.</param>
    /// <returns>The grown, or shrunk, range.</returns>
    /// <exception cref="ArgumentException"><paramref name="amount"/> is <c>NaN</c> or an infinity.</exception>
    /// <exception cref="OverflowException">A moved bound is outside the finite values <see cref="double"/> can hold.</exception>
    public Float64Range Expand(double amount)
    {
        RequireFinite(amount, nameof(amount));
        return RangeArithmetic.Move<Float64Range, double>(this, value => Moved(value - amount), value => Moved(value + amount));
    }

    /// <summary>
    /// A moved bound or split boundary, or <see cref="OverflowException"/> where IEEE arithmetic would give an
    /// infinity — the exception the other range types throw past their last value, and the one that ends a split.
    /// </summary>
    private static double Moved(double value)
        => double.IsFinite(value)
               ? value
               : throw new OverflowException("The moved bound is outside the finite values Double can hold.");

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}

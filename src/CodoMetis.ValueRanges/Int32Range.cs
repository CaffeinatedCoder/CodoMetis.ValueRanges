using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges;

/// <summary>
/// A range over 32-bit signed integers, equivalent to the PostgreSQL <c>int4range</c> type.
/// </summary>
/// <remarks>
/// This is a discriminated union with five variants: <see cref="Finite"/>, <see cref="UnboundedStart"/>,
/// <see cref="UnboundedEnd"/>, <see cref="Infinity"/> and <see cref="EmptyRange"/>. Use a <see langword="switch"/> expression for
/// exhaustive handling of all variants.
/// The default boundary convention for <see cref="CreateFinite"/> is a fully closed interval <c>[start, end]</c>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
[JsonConverter(typeof(RangeJsonConverter<Int32Range, int>))]
[TypeConverter(typeof(RangeTypeConverter<Int32Range, int>))]
public abstract partial record Int32Range : IRange<int>, IRangeFactory<Int32Range, int>
{
    private Int32Range()
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
    protected Int32Range(Int32Range original) => ClosedUnion.Require(this);

    /// <summary>
    /// Represents an empty <see cref="Int32Range"/> that contains no values.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<EmptyRange, Int32Range, int>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<EmptyRange, Int32Range, int>))]
    public sealed partial record EmptyRange : Int32Range, IEmptyRange<int>;

    /// <summary>
    /// Represents an <see cref="Int32Range"/> bounded on both sides.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Finite, Int32Range, int>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Finite, Int32Range, int>))]
    public sealed partial record Finite : Int32Range, IFiniteRange<int>
    {
        internal Finite(int start, int end)
        {
            Start = start;
            End   = end;
        }

        /// <inheritdoc/>
        public int Start { get; }

        /// <inheritdoc/>
        public int End { get; }

        /// <inheritdoc/>
        public bool StartInclusive => true;

        /// <inheritdoc/>
        public bool EndInclusive => true;

        /// <summary>
        /// Deconstructs the range into its bounds: <c>var (start, end) = finite;</c>.
        /// </summary>
        /// <remarks>
        /// Both bounds are inclusive — the canonical form of a discrete range — so the pair passes
        /// straight back to <see cref="Int32Range.CreateFinite"/>, whose default is closed.
        /// </remarks>
        /// <param name="start">Receives <see cref="Start"/>.</param>
        /// <param name="end">Receives <see cref="End"/>.</param>
        public void Deconstruct(out int start, out int end) => (start, end) = (Start, End);
    }

    /// <summary>
    /// Represents an <see cref="Int32Range"/> unbounded on the left:
    /// <c>(-∞, End]</c> or <c>(-∞, End)</c>.
    /// </summary>
    /// <param name="End">The upper (right) bound of the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedStart, Int32Range, int>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedStart, Int32Range, int>))]
    public sealed partial record UnboundedStart(int End) : Int32Range, IUnboundedStartRange<int>
    {
        /// <inheritdoc />
        public bool EndInclusive => true;
    }

    /// <summary>
    /// Represents an <see cref="Int32Range"/> unbounded on the right:
    /// <c>[Start, +∞)</c> or <c>(Start, +∞)</c>.
    /// </summary>
    /// <param name="Start">The lower (left) bound of the range.</param>
    [JsonConverter(typeof(RangeVariantJsonConverter<UnboundedEnd, Int32Range, int>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<UnboundedEnd, Int32Range, int>))]
    public sealed partial record UnboundedEnd(int Start) : Int32Range, IUnboundedEndRange<int>
    {
        /// <inheritdoc />
        public bool StartInclusive => true;
    }

    /// <summary>
    /// Represents an <see cref="Int32Range"/> unbounded on both sides: <c>(-∞, +∞)</c>.
    /// </summary>
    [JsonConverter(typeof(RangeVariantJsonConverter<Infinity, Int32Range, int>))]
    [TypeConverter(typeof(RangeVariantTypeConverter<Infinity, Int32Range, int>))]
    public sealed partial record Infinity : Int32Range, IInfinityRange<int>;

    /// <summary>
    /// Creates an <see cref="Int32Range"/> unbounded on the left.
    /// </summary>
    /// <param name="end">The upper (right) bound of the range.</param>
    /// <param name="endInclusive">
    /// <see langword="true"/> to include <paramref name="end"/> in the range.
    /// Defaults to <see langword="false"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedStart"/> range: <c>(-∞, end]</c> or <c>(-∞, end)</c>.</returns>
    public static Int32Range CreateUnboundedStart(int end, bool endInclusive = false) =>
        endInclusive
            ? new UnboundedStart(end)
            : PreviousValueBefore(end) is { } e
                ? new UnboundedStart(e) // (-∞, end) ≡ (-∞, end - 1]
                : Empty;                // (-∞, int.MinValue) contains nothing

    /// <summary>
    /// Creates an <see cref="Int32Range"/> unbounded on the right.
    /// </summary>
    /// <param name="start">The lower (left) bound of the range.</param>
    /// <param name="startInclusive">
    /// <see langword="true"/> to include <paramref name="start"/> in the range.
    /// Defaults to <see langword="true"/>.
    /// </param>
    /// <returns>An <see cref="UnboundedEnd"/> range: <c>[start, +∞)</c> or <c>(start, +∞)</c>.</returns>
    public static Int32Range CreateUnboundedEnd(int start, bool startInclusive = true) =>
        startInclusive
            ? new UnboundedEnd(start)
            : NextValueAfter(start) is { } s
                ? new UnboundedEnd(s) // (start, +∞) ≡ [start + 1, +∞)
                : Empty;              // (int.MaxValue, +∞) contains nothing

    /// <summary>
    /// Creates an <see cref="Int32Range"/> that spans the entire domain: <c>(-∞, +∞)</c>.
    /// </summary>
    /// <returns>An <see cref="Infinity"/> range covering all integer values.</returns>
    public static Int32Range Infinite { get; } = new Infinity();

    /// <summary>
    /// Returns an empty <see cref="Int32Range"/> that contains no values.
    /// </summary>
    public static Int32Range Empty { get; } = new EmptyRange();

    /// <summary>
    /// Creates an <see cref="Int32Range"/> bounded on both sides.
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
    public static Int32Range CreateFinite(
        int  start,
        int  end,
        bool startInclusive = true,
        bool endInclusive   = true
    ) => DiscreteCanonical.Finite<Int32Range, int>(start, end, startInclusive, endInclusive) is { } b
             ? new Finite(b.Start, b.End)
             : Empty;

    /// <inheritdoc />
    public static bool IsDiscrete => true;

    /// <inheritdoc />
    public static int? NextValueAfter(int value) => value == int.MaxValue ? null : value + 1;

    /// <inheritdoc />
    public static int? PreviousValueBefore(int value) => value == int.MinValue ? null : value - 1;

    /// <summary>
    /// Enumerates the integers the range contains, ascending and inclusive of both bounds.
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
    /// <returns>The contained integers, ascending.</returns>
    /// <exception cref="NotSupportedException">The range is unbounded on either side.</exception>
    public IEnumerable<int> Values() => DiscreteEnumeration.Values<Int32Range, int>(this);

    /// <summary>
    /// The number of integers the range contains, or <see langword="null"/> when it is unbounded.
    /// The empty range measures 0.
    /// </summary>
    /// <remarks>
    /// A count rather than a span: <c>[1, 10]</c> measures 10, not 9, matching what PostgreSQL's
    /// <c>upper(x) - lower(x)</c> yields for the half-open form it canonicalizes to. Widened to
    /// <see cref="long"/> because the full <see cref="int"/> domain holds more values than an
    /// <see cref="int"/> can count.
    /// </remarks>
    public long? Length =>
        this switch
        {
            IEmptyRange<int>    => 0L,
            IFiniteRange<int> f => (long)f.End - f.Start + 1,
            _                   => null
        };

    /// <inheritdoc />
    /// <remarks>PostgreSQL 16\'s integer grammar — decimal, or <c>0x</c>, <c>0o</c> or <c>0b</c> digits, with single
    /// underscores between digits (<see cref="IntegerLiteral"/>); the provider is ignored.</remarks>
    public static int ParseValue(ReadOnlySpan<char> s, IFormatProvider? provider)
        => IntegerLiteral.Parse<int>(s);

    /// <summary>
    /// Parses a PostgreSQL range literal (e.g. <c>[1,10]</c>, <c>empty</c>, <c>(,)</c>)
    /// into an <see cref="Int32Range"/>.
    /// </summary>
    public static Int32Range Parse(string s, IFormatProvider? provider)
        => RangeFormat.Parse<Int32Range, int>(s.AsSpan(), provider);

    /// <summary>Parses a PostgreSQL range literal from a character span.</summary>
    public static Int32Range Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => RangeFormat.Parse<Int32Range, int>(s, provider);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal into an <see cref="Int32Range"/>.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(string? s, IFormatProvider? provider, out Int32Range result)
        => RangeFormat.TryParse<Int32Range, int>(s.AsSpan(), provider, out result);

    /// <summary>
    /// Tries to parse a PostgreSQL range literal from a character span.
    /// Returns <see langword="false"/> and <see cref="Empty"/> on failure.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Int32Range result)
        => RangeFormat.TryParse<Int32Range, int>(s, provider, out result);

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
    public int CompareTo(Int32Range? other) => RangeOrder.Compare<int>(this, other);

    /// <summary>
    /// Returns the range moved by <paramref name="delta"/>: both bounds move together and keep their
    /// inclusiveness, so <c>[1,5].Shift(10)</c> is <c>[11,15]</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an unbounded side stays
    /// unbounded. A negative <paramref name="delta"/> moves the range down.
    /// This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="delta">How far to move the range.</param>
    /// <returns>The moved range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="int"/> can hold.</exception>
    public Int32Range Shift(int delta) => RangeArithmetic.Move<Int32Range, int>(this, value => checked(value + delta), value => checked(value + delta));

    /// <summary>
    /// Returns the range grown by <paramref name="amount"/> on both sides: the lower bound moves down
    /// and the upper bound up, each keeping its inclusiveness, so <c>[5,10].Expand(2)</c> is <c>[3,12]</c>.
    /// </summary>
    /// <remarks>
    /// A negative <paramref name="amount"/> shrinks the range, and a range shrunk past its own width is
    /// <see cref="Empty"/>. <see cref="Empty"/> and <see cref="Infinite"/> come back as they are, and an
    /// unbounded side stays unbounded. This runs in memory only: PostgreSQL has no range arithmetic.
    /// </remarks>
    /// <param name="amount">How far to move each bound outward.</param>
    /// <returns>The grown, or shrunk, range.</returns>
    /// <exception cref="OverflowException">A moved bound is outside the values <see cref="int"/> can hold.</exception>
    public Int32Range Expand(int amount) => RangeArithmetic.Move<Int32Range, int>(this, value => checked(value - amount), value => checked(value + amount));

    /// <inheritdoc />
    public override sealed string ToString()
        => ((IFormattable)this).ToString(null, CultureInfo.InvariantCulture);
}
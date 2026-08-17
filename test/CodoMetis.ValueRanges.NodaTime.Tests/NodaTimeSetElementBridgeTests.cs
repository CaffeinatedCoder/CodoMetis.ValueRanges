using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Serialization;
using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

/// <summary>
/// The NodaTime validated-wrapper arities. They differ from the core arities in what they ask
/// their elements for: NodaTime's <see cref="IFormattable"/> with a null format produces the
/// culture's form — a <see cref="LocalDate"/> renders as <c>Saturday, 15 June 2024</c> — so each
/// family pins its ISO pattern instead, and the element's own
/// <c>ToString(format, provider)</c> is what produces it.
/// </summary>
/// <remarks>
/// The elements below forward the format argument, which is the contract and the shape the
/// generators emit. <see cref="CultureBoundDay"/> is the counterexample: it swallows the
/// argument, and the tests show the culture form leaking into the array literal.
/// </remarks>
[TestClass]
public class NodaTimeSetElementBridgeTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddRangeConverters();

    private static JsonConverter<T>? HookFor<TSet, T>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
        => TSet.ElementJsonConverter;

    // -----------------------------------------------------------------------
    // Literal round trips — one per family
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperLocalDateSet_FormatsIso_AndRoundTrips()
    {
        var set = LocalDateSet<CalendarDay>.From(
            new CalendarDay(new LocalDate(2024, 12, 24)),
            new CalendarDay(new LocalDate(2024, 1, 1)));

        Assert.AreEqual("{2024-01-01,2024-12-24}", set.ToString());
        Assert.AreEqual(set, LocalDateSet<CalendarDay>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperLocalDateTimeSet_KeepsSubSecondPrecision()
    {
        var set = LocalDateTimeSet<WallClockStamp>.From(
            new WallClockStamp(new LocalDateTime(2024, 6, 15, 10, 30, 15).PlusNanoseconds(123456789)));

        Assert.AreEqual("{2024-06-15T10:30:15.123456789}", set.ToString());
        Assert.AreEqual(set, LocalDateTimeSet<WallClockStamp>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperInstantSet_FormatsIsoWithZ_AndRoundTrips()
    {
        var set = InstantSet<EventInstant>.From(
            new EventInstant(Instant.FromUtc(2024, 6, 15, 10, 30)),
            new EventInstant(Instant.FromUnixTimeSeconds(0)));

        Assert.AreEqual("{1970-01-01T00:00:00Z,2024-06-15T10:30:00Z}", set.ToString());
        Assert.AreEqual(set, InstantSet<EventInstant>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperLocalTimeSet_KeepsSubSecondPrecision()
    {
        var set = LocalTimeSet<OpeningTime>.From(
            new OpeningTime(new LocalTime(17, 30, 0)),
            new OpeningTime(LocalTime.FromHourMinuteSecondNanosecond(9, 30, 15, 123456789)));

        Assert.AreEqual("{09:30:15.123456789,17:30:00}", set.ToString());
        Assert.AreEqual(set, LocalTimeSet<OpeningTime>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void WrapperYearMonthSet_FormatsIso_AndRoundTrips()
    {
        var set = YearMonthSet<BillingMonth>.From(
            new BillingMonth(new YearMonth(2024, 6)),
            new BillingMonth(new YearMonth(2024, 1)));

        Assert.AreEqual("{2024-01,2024-06}", set.ToString());
        Assert.AreEqual(set, YearMonthSet<BillingMonth>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    // -----------------------------------------------------------------------
    // Ordering, membership, algebra
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WrapperSets_SortChronologicallyThroughTheElementsComparable()
    {
        var set = InstantSet<EventInstant>.From(
            new EventInstant(Instant.FromUtc(2024, 6, 15, 10, 30)),
            new EventInstant(Instant.FromUtc(2024, 1, 1, 0, 0)));

        Assert.AreEqual(new EventInstant(Instant.FromUtc(2024, 1, 1, 0, 0)), set[0]);
    }

    [TestMethod]
    public void WrapperSets_AlgebraWorks()
    {
        var all      = LocalDateSet<CalendarDay>.From(
            new CalendarDay(new LocalDate(2024, 1, 1)),
            new CalendarDay(new LocalDate(2024, 12, 24)));
        var required = LocalDateSet<CalendarDay>.From(new CalendarDay(new LocalDate(2024, 1, 1)));

        Assert.IsTrue(required.IsSubsetOf(all));
        Assert.IsTrue(all.Contains(new CalendarDay(new LocalDate(2024, 12, 24))));
        Assert.AreEqual(2, all.Union(required).Count);
    }

    [TestMethod]
    public void WrapperSets_CollectionExpression()
    {
        YearMonthSet<BillingMonth> set =
            [new BillingMonth(new YearMonth(2024, 6)), new BillingMonth(new YearMonth(2024, 1))];

        Assert.AreEqual("{2024-01,2024-06}", set.ToString());
    }

    [TestMethod]
    public void WrapperSets_EmptySingletonPerInstantiation()
    {
        Assert.AreSame(LocalDateSet<CalendarDay>.Empty, LocalDateSet<CalendarDay>.From());
        Assert.AreEqual("{}", YearMonthSet<BillingMonth>.Empty.ToString());
    }

    [TestMethod]
    public void WrapperSets_ParseInvalidElement_Throws()
        => Assert.ThrowsExactly<FormatException>(
            () => LocalDateSet<CalendarDay>.Parse("{not-a-date}", CultureInfo.InvariantCulture));

    // -----------------------------------------------------------------------
    // JSON
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ElementJsonConverter_IsDefined_ForEveryNodaTimeArity()
    {
        Assert.IsNotNull(HookFor<LocalDateSet<CalendarDay>, CalendarDay>());
        Assert.IsNotNull(HookFor<LocalDateTimeSet<WallClockStamp>, WallClockStamp>());
        Assert.IsNotNull(HookFor<InstantSet<EventInstant>, EventInstant>());
        Assert.IsNotNull(HookFor<LocalTimeSet<OpeningTime>, OpeningTime>());
        Assert.IsNotNull(HookFor<YearMonthSet<BillingMonth>, BillingMonth>());
    }

    /// <summary>
    /// The wrapper's JSON is the same ISO text its closed sibling writes — for these families
    /// byte-identical, because both sides go through the same pattern rather than through
    /// System.Text.Json's own temporal formatting.
    /// </summary>
    [TestMethod]
    public void WrapperSets_SerializeAsTheSameIsoStringsAsTheirSiblings()
    {
        var day = new LocalDate(2024, 6, 15);

        Assert.AreEqual(
            JsonSerializer.Serialize(LocalDateSet.From(day), Options),
            JsonSerializer.Serialize(LocalDateSet<CalendarDay>.From(new CalendarDay(day)), Options));

        var month = new YearMonth(2024, 6);

        Assert.AreEqual(
            JsonSerializer.Serialize(YearMonthSet.From(month), Options),
            JsonSerializer.Serialize(YearMonthSet<BillingMonth>.From(new BillingMonth(month)), Options));
    }

    [TestMethod]
    public void WrapperSets_RoundTripThroughJson()
    {
        var set = InstantSet<EventInstant>.From(
            new EventInstant(Instant.FromUtc(2024, 6, 15, 10, 30)),
            new EventInstant(Instant.FromUnixTimeSeconds(0)));

        var json = JsonSerializer.Serialize(set, Options);

        Assert.AreEqual("[\"1970-01-01T00:00:00Z\",\"2024-06-15T10:30:00Z\"]", json);
        Assert.AreEqual(set, JsonSerializer.Deserialize<InstantSet<EventInstant>>(json, Options));
    }

    [TestMethod]
    public void WrapperSets_ElementValidation_RunsOnTheJsonPath()
        => Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<LocalDateSet<CalendarDay>>("[\"not-a-date\"]", Options));

    // -----------------------------------------------------------------------
    // The counterexample
    // -----------------------------------------------------------------------

    /// <summary>
    /// An element that swallows the format argument answers with NodaTime's culture form, which
    /// is what the pinned ISO pattern exists to avoid. The literal is visibly wrong and no longer
    /// round-trips, because the family's own <c>ParseValue</c> reads ISO.
    /// </summary>
    [TestMethod]
    public void WrapperLocalDateSet_ElementIgnoringTheFormat_LeaksTheCultureForm()
    {
        var set = LocalDateSet<CultureBoundDay>.From(new CultureBoundDay(new LocalDate(2024, 6, 15)));

        StringAssert.Contains(set.ToString(), "June");

        Assert.ThrowsExactly<FormatException>(
            () => LocalDateSet<CultureBoundDay>.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }
}

// Generator-shaped wrapper elements over NodaTime values. Each forwards the format argument, so
// the family's pinned ISO pattern reaches the underlying value.

internal readonly record struct CalendarDay : IFormattable, IParsable<CalendarDay>, IComparable<CalendarDay>
{
    private readonly LocalDate _value;

    public CalendarDay(LocalDate value) => _value = value;

    public static CalendarDay Parse(string s, IFormatProvider? provider)
        => new(LocalDatePattern.Iso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out CalendarDay result)
    {
        var parsed = s is null ? null : LocalDatePattern.Iso.Parse(s);
        result = parsed is { Success: true } ? new CalendarDay(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(CalendarDay other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalDatePattern.Iso.Format(_value);
}

internal readonly record struct WallClockStamp
    : IFormattable, IParsable<WallClockStamp>, IComparable<WallClockStamp>
{
    private readonly LocalDateTime _value;

    public WallClockStamp(LocalDateTime value) => _value = value;

    public static WallClockStamp Parse(string s, IFormatProvider? provider)
        => new(LocalDateTimePattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out WallClockStamp result)
    {
        var parsed = s is null ? null : LocalDateTimePattern.ExtendedIso.Parse(s);
        result = parsed is { Success: true } ? new WallClockStamp(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(WallClockStamp other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalDateTimePattern.ExtendedIso.Format(_value);
}

internal readonly record struct EventInstant : IFormattable, IParsable<EventInstant>, IComparable<EventInstant>
{
    private readonly Instant _value;

    public EventInstant(Instant value) => _value = value;

    public static EventInstant Parse(string s, IFormatProvider? provider)
        => new(InstantPattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out EventInstant result)
    {
        var parsed = s is null ? null : InstantPattern.ExtendedIso.Parse(s);
        result = parsed is { Success: true } ? new EventInstant(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(EventInstant other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => InstantPattern.ExtendedIso.Format(_value);
}

internal readonly record struct OpeningTime : IFormattable, IParsable<OpeningTime>, IComparable<OpeningTime>
{
    private readonly LocalTime _value;

    public OpeningTime(LocalTime value) => _value = value;

    public static OpeningTime Parse(string s, IFormatProvider? provider)
        => new(LocalTimePattern.ExtendedIso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out OpeningTime result)
    {
        var parsed = s is null ? null : LocalTimePattern.ExtendedIso.Parse(s);
        result = parsed is { Success: true } ? new OpeningTime(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(OpeningTime other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => LocalTimePattern.ExtendedIso.Format(_value);
}

internal readonly record struct BillingMonth : IFormattable, IParsable<BillingMonth>, IComparable<BillingMonth>
{
    private readonly YearMonth _value;

    public BillingMonth(YearMonth value) => _value = value;

    public static BillingMonth Parse(string s, IFormatProvider? provider)
        => new(YearMonthPattern.Iso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out BillingMonth result)
    {
        var parsed = s is null ? null : YearMonthPattern.Iso.Parse(s);
        result = parsed is { Success: true } ? new BillingMonth(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(BillingMonth other) => _value.CompareTo(other._value);

    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => YearMonthPattern.Iso.Format(_value);
}

/// <summary>
/// A deliberately non-conforming element: it ignores the format argument, so it answers with
/// NodaTime's culture long form rather than the ISO pattern the family asked for.
/// </summary>
internal readonly record struct CultureBoundDay
    : IFormattable, IParsable<CultureBoundDay>, IComparable<CultureBoundDay>
{
    private readonly LocalDate _value;

    public CultureBoundDay(LocalDate value) => _value = value;

    public static CultureBoundDay Parse(string s, IFormatProvider? provider)
        => new(LocalDatePattern.Iso.Parse(s).GetValueOrThrow());

    public static bool TryParse(string? s, IFormatProvider? provider, out CultureBoundDay result)
    {
        var parsed = s is null ? null : LocalDatePattern.Iso.Parse(s);
        result = parsed is { Success: true } ? new CultureBoundDay(parsed.Value) : default;
        return parsed is { Success: true };
    }

    public int CompareTo(CultureBoundDay other) => _value.CompareTo(other._value);

    // The defect: `format` is ignored.
    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value.ToString(null, CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
}

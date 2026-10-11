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
    // Elements backed by a calendar other than ISO — refused, not stored as another value
    // -----------------------------------------------------------------------

    private static readonly CalendarDay JulianDay =
        new(new LocalDate(2024, 1, 1, CalendarSystem.Julian));               // ISO 2024-01-14

    private static readonly WallClockStamp JulianStamp =
        new(new LocalDateTime(2024, 1, 1, 8, 0, CalendarSystem.Julian));

    private static readonly BillingMonth JulianMonth =
        new(new YearMonth(2024, 1, CalendarSystem.Julian));

    /// <summary>
    /// The pinned ISO pattern formats a value in its own calendar's fields, so a wrapper over a
    /// Julian 2024-01-01 — ISO 2024-01-14 — wrote <c>{2024-01-01}</c>, and JSON
    /// <c>["2024-01-01"]</c>, both reading back as ISO 2024-01-01: thirteen days off, silently. The
    /// closed sets convert such a value to ISO; a wrapper arity cannot construct its element from
    /// an ISO primitive except by parsing, so it refuses it instead.
    /// </summary>
    [TestMethod]
    public void WrapperSets_RefuseANonIsoElement_AtConstruction()
    {
        var day = Assert.ThrowsExactly<ArgumentException>(() => LocalDateSet<CalendarDay>.From(JulianDay));
        StringAssert.Contains(day.Message, "ISO");
        StringAssert.Contains(day.Message, "2024-01-01");

        Assert.ThrowsExactly<ArgumentException>(() => LocalDateSet<CalendarDay>.From(new List<CalendarDay> { JulianDay }));
        Assert.ThrowsExactly<ArgumentException>(() => { LocalDateSet<CalendarDay> _ = [JulianDay]; });

        Assert.ThrowsExactly<ArgumentException>(() => LocalDateTimeSet<WallClockStamp>.From(JulianStamp));
        Assert.ThrowsExactly<ArgumentException>(() => LocalDateTimeSet<WallClockStamp>.From(new List<WallClockStamp> { JulianStamp }));

        Assert.ThrowsExactly<ArgumentException>(() => YearMonthSet<BillingMonth>.From(JulianMonth));
        Assert.ThrowsExactly<ArgumentException>(() => YearMonthSet<BillingMonth>.From(new List<BillingMonth> { JulianMonth }));
    }

    /// <summary>
    /// The bare-element probe: <c>Contains</c>, <c>Add</c> and <c>Remove</c> refuse what
    /// <c>From</c> refuses. A Julian probe compared against ISO storage would otherwise answer
    /// <see langword="false"/> for a date the set holds.
    /// </summary>
    [TestMethod]
    public void WrapperSets_RefuseANonIsoProbe()
    {
        var days   = LocalDateSet<CalendarDay>.From(new CalendarDay(new LocalDate(2024, 1, 14)));
        var stamps = LocalDateTimeSet<WallClockStamp>.From(new WallClockStamp(new LocalDateTime(2024, 1, 14, 8, 0)));
        var months = YearMonthSet<BillingMonth>.From(new BillingMonth(new YearMonth(2024, 2)));

        Assert.ThrowsExactly<ArgumentException>(() => days.Contains(JulianDay));
        Assert.ThrowsExactly<ArgumentException>(() => days.Add(JulianDay));
        Assert.ThrowsExactly<ArgumentException>(() => days.Remove(JulianDay));
        Assert.ThrowsExactly<ArgumentException>(() => stamps.Contains(JulianStamp));
        Assert.ThrowsExactly<ArgumentException>(() => months.Contains(JulianMonth));

        // Against a non-empty set NodaTime's own CompareTo happens to throw across calendars; against
        // the empty set nothing is compared, so Contains answered false and Add stored the Julian
        // element that From refuses.
        StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() => days.Contains(JulianDay)).Message, "ISO 8601");
        Assert.ThrowsExactly<ArgumentException>(() => LocalDateSet<CalendarDay>.Empty.Contains(JulianDay));
        Assert.ThrowsExactly<ArgumentException>(() => LocalDateSet<CalendarDay>.Empty.Add(JulianDay));
        Assert.ThrowsExactly<ArgumentException>(() => LocalDateTimeSet<WallClockStamp>.Empty.Add(JulianStamp));
        Assert.ThrowsExactly<ArgumentException>(() => YearMonthSet<BillingMonth>.Empty.Add(JulianMonth));
    }

    /// <summary>
    /// A converter registered for the element is consulted on read, and can hand back a non-ISO
    /// value; the set refuses it there too, as a <see cref="JsonException"/>.
    /// </summary>
    [TestMethod]
    public void WrapperSets_RefuseANonIsoElement_FromARegisteredElementConverter()
    {
        var options = new JsonSerializerOptions { Converters = { new JulianDayConverter() } };

        var ex = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<LocalDateSet<CalendarDay>>("[\"2024-01-01\"]", options));
        Assert.IsInstanceOfType<ArgumentException>(ex.InnerException);
    }

    /// <summary>ISO elements are untouched, at the extremes too.</summary>
    [TestMethod]
    public void WrapperSets_AcceptIsoElements_AtTheExtremes()
    {
        var days = LocalDateSet<CalendarDay>.From(
            new CalendarDay(LocalDate.MinIsoValue), new CalendarDay(LocalDate.MaxIsoValue));
        Assert.AreEqual(2, days.Count);
        Assert.IsTrue(days.Contains(new CalendarDay(LocalDate.MaxIsoValue)));

        var stamps = LocalDateTimeSet<WallClockStamp>.From(
            new WallClockStamp(LocalDateTime.MinIsoValue), new WallClockStamp(LocalDateTime.MaxIsoValue));
        Assert.AreEqual(2, stamps.Count);
    }

    private sealed class JulianDayConverter : JsonConverter<CalendarDay>
    {
        public override CalendarDay Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(LocalDatePattern.Iso.Parse(reader.GetString()!).Value.WithCalendar(CalendarSystem.Julian));

        public override void Write(Utf8JsonWriter writer, CalendarDay value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }

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

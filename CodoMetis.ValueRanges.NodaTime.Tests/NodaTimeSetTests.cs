using System.Globalization;
using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

using CodoMetis.ValueRanges;

[TestClass]
public class NodaTimeSetTests
{
    // -----------------------------------------------------------------------
    // LocalDateSet
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LocalDateSet_From_DedupesAndSortsChronologically()
    {
        var set = LocalDateSet.From(
            new LocalDate(2024, 12, 24),
            new LocalDate(2024, 1, 1),
            new LocalDate(2024, 12, 24));

        Assert.AreEqual(2, set.Count);
        Assert.AreEqual(new LocalDate(2024, 1, 1), set.Values[0]);
        Assert.AreEqual(new LocalDate(2024, 12, 24), set.Values[1]);
    }

    [TestMethod]
    public void LocalDateSet_NormalizesNonIsoCalendarToIso()
    {
        var isoDate    = new LocalDate(2024, 6, 1);
        var copticDate = isoDate.WithCalendar(CalendarSystem.Coptic);

        var set = LocalDateSet.From(copticDate);

        Assert.AreEqual(CalendarSystem.Iso, set.Values[0].Calendar);
        Assert.AreEqual(LocalDateSet.From(isoDate), set);
    }

    [TestMethod]
    public void LocalDateSet_MixedCalendars_SortSafely()
    {
        // Without ISO normalization LocalDate.CompareTo would throw on mixed calendars.
        var iso    = new LocalDate(2024, 1, 1);
        var coptic = new LocalDate(2024, 6, 1).WithCalendar(CalendarSystem.Coptic);

        var set = LocalDateSet.From(coptic, iso);

        Assert.AreEqual(2, set.Count);
        Assert.AreEqual(iso, set.Values[0]);
    }

    [TestMethod]
    public void LocalDateSet_RoundTripsArrayLiteral()
    {
        var original = LocalDateSet.From(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 24));

        Assert.AreEqual("{2024-01-01,2024-12-24}", original.ToString());
        Assert.AreEqual(original, LocalDateSet.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void LocalDateSet_CollectionExpression()
    {
        LocalDateSet set = [new LocalDate(2024, 6, 2), new LocalDate(2024, 6, 1)];

        Assert.AreEqual("{2024-06-01,2024-06-02}", set.ToString());
    }

    [TestMethod]
    public void LocalDateSet_AlgebraSmoke()
    {
        var holidays = LocalDateSet.From(new LocalDate(2024, 1, 1), new LocalDate(2024, 12, 24));
        var subset   = LocalDateSet.From(new LocalDate(2024, 1, 1));

        Assert.IsTrue(subset.IsSubsetOf(holidays));
        Assert.IsTrue(holidays.Contains(new LocalDate(2024, 12, 24)));
        Assert.AreEqual(2, holidays.Union(subset).Count);
    }

    [TestMethod]
    public void LocalDateSet_EmptySingleton()
        => Assert.AreSame(LocalDateSet.Empty, LocalDateSet.From());

    // Element-level operations normalize their probe the way From does. Without that,
    // Contains/Remove silently miss (LocalDate.Equals is calendar-sensitive) and Add either
    // smuggles a non-ISO element into an empty set or throws out of LocalDate.CompareTo.

    [TestMethod]
    public void LocalDateSet_Contains_NormalizesNonIsoProbe()
    {
        var coptic = new LocalDate(2024, 6, 1).WithCalendar(CalendarSystem.Coptic);

        Assert.IsTrue(LocalDateSet.From(coptic).Contains(coptic));
        Assert.IsTrue(LocalDateSet.From(new LocalDate(2024, 6, 1)).Contains(coptic));
    }

    [TestMethod]
    public void LocalDateSet_Add_NormalizesNonIsoElement()
    {
        var coptic = new LocalDate(2024, 6, 1).WithCalendar(CalendarSystem.Coptic);

        var fromEmpty = LocalDateSet.Empty.Add(coptic);
        Assert.AreEqual(CalendarSystem.Iso, fromEmpty.Values[0].Calendar);

        var fromNonEmpty = LocalDateSet.From(new LocalDate(2024, 1, 1)).Add(coptic);
        Assert.AreEqual(2, fromNonEmpty.Count);
        Assert.AreEqual(CalendarSystem.Iso, fromNonEmpty.Values[1].Calendar);
    }

    [TestMethod]
    public void LocalDateSet_Add_NonIsoDuplicateIsNoOp()
    {
        var set = LocalDateSet.From(new LocalDate(2024, 6, 1));

        Assert.AreSame(set, set.Add(new LocalDate(2024, 6, 1).WithCalendar(CalendarSystem.Coptic)));
    }

    [TestMethod]
    public void LocalDateSet_Remove_NormalizesNonIsoProbe()
    {
        var set = LocalDateSet.From(new LocalDate(2024, 1, 1), new LocalDate(2024, 6, 1));

        var removed = set.Remove(new LocalDate(2024, 6, 1).WithCalendar(CalendarSystem.Coptic));

        Assert.AreEqual(1, removed.Count);
        Assert.AreEqual(new LocalDate(2024, 1, 1), removed.Values[0]);
    }

    // -----------------------------------------------------------------------
    // LocalDateTimeSet
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LocalDateTimeSet_RoundTripsArrayLiteral()
    {
        var original = LocalDateTimeSet.From(
            new LocalDateTime(2024, 6, 1, 12, 30, 0),
            new LocalDateTime(2024, 6, 1, 8, 0, 0));

        Assert.AreEqual("{2024-06-01T08:00:00,2024-06-01T12:30:00}", original.ToString());
        Assert.AreEqual(original, LocalDateTimeSet.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void LocalDateTimeSet_ParsesPostgresWireForm()
    {
        // The space-separated wire form needs quoting inside an array literal.
        var set = LocalDateTimeSet.Parse("{\"2024-06-01 12:30:00\"}", CultureInfo.InvariantCulture);

        Assert.IsTrue(set.Contains(new LocalDateTime(2024, 6, 1, 12, 30, 0)));
    }

    [TestMethod]
    public void LocalDateTimeSet_NormalizesNonIsoCalendarToIso()
    {
        var iso    = new LocalDateTime(2024, 6, 1, 8, 0, 0);
        var coptic = iso.WithCalendar(CalendarSystem.Coptic);

        Assert.AreEqual(LocalDateTimeSet.From(iso), LocalDateTimeSet.From(coptic));
    }

    [TestMethod]
    public void LocalDateTimeSet_ElementOperations_NormalizeNonIsoProbe()
    {
        var iso    = new LocalDateTime(2024, 6, 1, 8, 0, 0);
        var coptic = iso.WithCalendar(CalendarSystem.Coptic);

        Assert.IsTrue(LocalDateTimeSet.From(iso).Contains(coptic));
        Assert.AreEqual(CalendarSystem.Iso, LocalDateTimeSet.Empty.Add(coptic).Values[0].Calendar);
        Assert.IsTrue(LocalDateTimeSet.From(iso).Remove(coptic).IsEmpty);
    }

    // -----------------------------------------------------------------------
    // InstantSet
    // -----------------------------------------------------------------------

    [TestMethod]
    public void InstantSet_RoundTripsArrayLiteral()
    {
        var original = InstantSet.From(
            Instant.FromUtc(2024, 6, 1, 12, 30),
            Instant.FromUtc(2024, 6, 1, 8, 0));

        Assert.AreEqual("{2024-06-01T08:00:00Z,2024-06-01T12:30:00Z}", original.ToString());
        Assert.AreEqual(original, InstantSet.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void InstantSet_ParseDedupesAcrossTextualOffsets()
    {
        var set = InstantSet.Parse(
            "{2024-06-01T14:30:00+02:00,2024-06-01T12:30:00Z}", CultureInfo.InvariantCulture);

        Assert.AreEqual(1, set.Count);
        Assert.IsTrue(set.Contains(Instant.FromUtc(2024, 6, 1, 12, 30)));
    }

    // -----------------------------------------------------------------------
    // LocalTimeSet
    // -----------------------------------------------------------------------

    [TestMethod]
    public void LocalTimeSet_RoundTripsArrayLiteral()
    {
        var original = LocalTimeSet.From(new LocalTime(17, 30), new LocalTime(9, 0));

        Assert.AreEqual("{09:00:00,17:30:00}", original.ToString());
        Assert.AreEqual(original, LocalTimeSet.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void LocalTimeSet_PreservesSubseconds()
    {
        var time = LocalTime.FromHourMinuteSecondNanosecond(9, 0, 0, 123_000_000);

        var set = LocalTimeSet.From(time);

        Assert.AreEqual("{09:00:00.123}", set.ToString());
        Assert.AreEqual(set, LocalTimeSet.Parse(set.ToString(), CultureInfo.InvariantCulture));
    }

    // -----------------------------------------------------------------------
    // YearMonthSet
    // -----------------------------------------------------------------------

    [TestMethod]
    public void YearMonthSet_RoundTripsArrayLiteral()
    {
        var original = YearMonthSet.From(new YearMonth(2024, 6), new YearMonth(2024, 1));

        Assert.AreEqual("{2024-01,2024-06}", original.ToString());
        Assert.AreEqual(original, YearMonthSet.Parse(original.ToString(), CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void YearMonthSet_RejectsNonIsoCalendar()
    {
        var copticYearMonth = new LocalDate(2024, 6, 1).WithCalendar(CalendarSystem.Coptic).ToYearMonth();

        Assert.ThrowsExactly<ArgumentException>(() => YearMonthSet.From(copticYearMonth));
    }

    [TestMethod]
    public void YearMonthSet_ElementOperations_RejectNonIsoCalendar()
    {
        // A non-ISO year-month has no lossless ISO equivalent, so every entry point rejects it
        // rather than silently missing or smuggling it past the ISO invariant.
        var coptic   = new LocalDate(2024, 6, 1).WithCalendar(CalendarSystem.Coptic).ToYearMonth();
        var populated = YearMonthSet.From(new YearMonth(2024, 6));

        Assert.ThrowsExactly<ArgumentException>(() => YearMonthSet.Empty.Add(coptic));
        Assert.ThrowsExactly<ArgumentException>(() => populated.Add(coptic));
        Assert.ThrowsExactly<ArgumentException>(() => populated.Contains(coptic));
        Assert.ThrowsExactly<ArgumentException>(() => populated.Remove(coptic));
    }

    [TestMethod]
    public void YearMonthSet_DedupesAndSorts()
    {
        var set = YearMonthSet.From(new YearMonth(2025, 1), new YearMonth(2024, 12), new YearMonth(2025, 1));

        Assert.AreEqual(2, set.Count);
        Assert.AreEqual(new YearMonth(2024, 12), set.Values[0]);
    }
}

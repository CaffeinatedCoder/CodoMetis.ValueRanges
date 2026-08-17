using NodaTime;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

/// <summary>
/// The discrete step functions decide "is this the end of the domain?" by comparing against
/// <see cref="LocalDate.MaxIsoValue"/>, and NodaTime's equality includes the calendar. A date that
/// <em>is</em> the domain maximum in a different calendar system must still be recognised as such.
/// </summary>
/// <remarks>
/// <para>
/// Only one calendar makes this reachable, and it is not an exotic one. Of NodaTime's nineteen
/// calendars, ISO and Gregorian are the only two that can represent <c>9999-12-31</c> at all —
/// every other one tops out earlier and <c>WithCalendar</c> throws before such a date can be built.
/// Gregorian shares ISO's arithmetic but is a distinct <see cref="CalendarSystem"/> instance, so
/// <c>gregorianMax == LocalDate.MaxIsoValue</c> is <see langword="false"/>: the guard was skipped
/// and <c>PlusDays(1)</c> ran off the end of the domain.
/// </para>
/// <para>
/// Each type is fixed the way its own documented policy already says. <c>LocalDateRange</c>
/// normalizes a non-ISO bound to ISO, so its step functions normalize before comparing.
/// <c>YearMonthRange</c> rejects non-ISO bounds outright — a non-ISO year-month spans parts of two
/// ISO months — so its step functions reject too, instead of quietly stepping in the caller's
/// calendar and handing back a value its own constructors would refuse.
/// </para>
/// </remarks>
[TestClass]
public class CalendarBoundaryTests
{
    // The ISO domain runs -9998-01-01 to 9999-12-31, so the minimum is not year 1 — taking both
    // extremes from LocalDate itself keeps that from being guessed at.
    private static LocalDate GregorianMax => LocalDate.MaxIsoValue.WithCalendar(CalendarSystem.Gregorian);
    private static LocalDate GregorianMin => LocalDate.MinIsoValue.WithCalendar(CalendarSystem.Gregorian);

    [TestMethod]
    public void OnlyIsoAndGregorian_CanRepresentTheIsoExtremes()
    {
        // The premise of the tests below. If another calendar gained the range, it would reach
        // these guards too and this is what says so.
        var reaching = CalendarSystem.Ids
                                     .Select(CalendarSystem.ForId)
                                     .Where(calendar => CanRepresentIsoMax(calendar))
                                     .Select(calendar => calendar.Id)
                                     .Order(StringComparer.Ordinal)
                                     .ToList();

        CollectionAssert.AreEqual(
            new[] { "Gregorian", "ISO" }, reaching,
            $"Expected only ISO and Gregorian to represent 9999-12-31, found [{string.Join(", ", reaching)}].");

        static bool CanRepresentIsoMax(CalendarSystem calendar)
        {
            try
            {
                _ = LocalDate.MaxIsoValue.WithCalendar(calendar);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }

    [TestMethod]
    public void LocalDateRange_StepFunctions_RecogniseTheGregorianExtremes()
    {
        Assert.IsNull(LocalDateRange.NextValueAfter(GregorianMax),
                      "The Gregorian spelling of 9999-12-31 is the domain maximum, so there is no "
                    + "value after it. Comparing calendar-sensitively skipped the guard and stepped "
                    + "off the end of the domain.");

        Assert.IsNull(LocalDateRange.PreviousValueBefore(GregorianMin),
                      "The Gregorian spelling of the ISO minimum (-9998-01-01) is the domain minimum.");

        // The interior still steps, so the guards above are not simply refusing everything.
        Assert.IsNotNull(LocalDateRange.NextValueAfter(new LocalDate(2024, 6, 15, CalendarSystem.Gregorian)));
        Assert.IsNotNull(LocalDateRange.PreviousValueBefore(new LocalDate(2024, 6, 15, CalendarSystem.Gregorian)));
    }

    [TestMethod]
    public void LocalDateRange_Factories_TreatTheGregorianMaximumAsTheDomainEdge()
    {
        // The observable consequence: (max, +∞) holds nothing, whichever calendar spells max.
        Assert.IsTrue(LocalDateRange.CreateUnboundedEnd(GregorianMax, false).IsEmpty());
        Assert.IsTrue(LocalDateRange.CreateUnboundedStart(GregorianMin, false).IsEmpty());
        Assert.IsTrue(LocalDateRange.CreateFinite(GregorianMax, GregorianMax, false, true).IsEmpty());

        // And the inclusive form at the same bound is a one-day range, not empty.
        Assert.IsFalse(LocalDateRange.CreateUnboundedEnd(GregorianMax, true).IsEmpty());
    }

    [TestMethod]
    public void YearMonthRange_StepFunctions_RefuseANonIsoCalendar()
    {
        // YearMonthRange rejects non-ISO bounds rather than normalizing them, so its step
        // functions reject too — otherwise they would hand back a value its own constructors
        // refuse, in the caller's calendar.
        var gregorian = new YearMonth(2024, 6, CalendarSystem.Gregorian);

        Assert.ThrowsExactly<ArgumentException>(() => YearMonthRange.NextValueAfter(gregorian));
        Assert.ThrowsExactly<ArgumentException>(() => YearMonthRange.PreviousValueBefore(gregorian));

        // The ISO spelling of the same month steps normally.
        Assert.IsNotNull(YearMonthRange.NextValueAfter(new YearMonth(2024, 6)));
    }
}

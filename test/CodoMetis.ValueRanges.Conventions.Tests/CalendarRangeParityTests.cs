using System.Globalization;
using CodoMetis.ValueRanges.Oracles;
using NodaTime;
using NodaTime.Calendars;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The calendar factories of <see cref="DateRange"/>, <see cref="LocalDateRange"/> and
/// <see cref="YearMonthRange"/> agree with each other and with an independent calendar, for every day
/// of a span of years and at both ends of each domain, under current cultures whose weeks start on
/// different days.
/// </summary>
/// <remarks>
/// <para>
/// Three implementations answer the same questions: the BCL one (<see cref="ISOWeek"/>, day
/// arithmetic), the NodaTime one (<see cref="WeekYearRules.Iso"/>), and the month arithmetic of
/// <see cref="YearMonth"/>. A week that started on Sunday in one of them, or a quarter that began in
/// the wrong month, would be a value that looks right and means another week. Each side is checked
/// against the definition (seven days from a Monday, the day inside the range) and against the other.
/// </para>
/// <para>
/// Weeks are ISO 8601 whatever the current culture says, which is the library's rule for literals
/// applied to the calendar. <c>en-US</c> starts a week on Sunday, <c>fa-IR</c> on Saturday and
/// <c>ar-EG</c> on Saturday with a non-Gregorian default calendar in some runtimes, so a factory that
/// read <see cref="DateTimeFormatInfo.FirstDayOfWeek"/> or the culture's calendar fails here.
/// </para>
/// </remarks>
[TestClass]
public sealed class CalendarRangeParityTests
{
    private static readonly string[] Cultures = ["", "en-US", "fa-IR", "de-DE", "ar-EG"];

    [TestMethod]
    public void EveryDay_IsInItsCalendarRanges_AndBothPackagesAgree()
    {
        var failures = new List<string>();

        foreach (var culture in Cultures)
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            try
            {
                for (var day = new DateOnly(1999, 12, 1); day <= new DateOnly(2031, 1, 31); day = day.AddDays(1))
                    Check(day, failures, culture);

                foreach (var day in EdgeDays()) Check(day, failures, culture);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        Candidates.Verdict(nameof(CalendarRangeParityTests), failures, "calendar ranges disagree with the calendar or with each other");
    }

    [TestMethod]
    public void NumberedRanges_PartitionTheirYear()
    {
        var failures = new List<string>();

        foreach (var year in (int[]) [1, 2004, 2020, 2026, 2027, 9998])
        {
            var quarters = RangeSet<DateRange, DateOnly>.From([.. Enumerable.Range(1, 4).Select(q => DateRange.Quarter(year, q))]);
            var months   = RangeSet<DateRange, DateOnly>.From([.. Enumerable.Range(1, 12).Select(m => DateRange.Month(year, m))]);
            Expect(failures, $"{year} quarters", quarters.ToString(), $"{{{DateRange.Year(year)}}}");
            Expect(failures, $"{year} months", months.ToString(), $"{{{DateRange.Year(year)}}}");

            // The ISO weeks of a week-year are consecutive, seven days each, and cover it with no gap.
            var weeks = ISOWeek.GetWeeksInYear(year);
            for (var week = 1; week < weeks; week++)
            {
                if (!DateRange.IsoWeek(year, week).IsAdjacentTo(DateRange.IsoWeek(year, week + 1)))
                    failures.Add($"IsoWeek({year}, {week}) is not adjacent to week {week + 1}");
            }

            if (year < 9998 && !DateRange.IsoWeek(year, weeks).IsAdjacentTo(DateRange.IsoWeek(year + 1, 1)))
                failures.Add($"the last ISO week of {year} is not adjacent to week 1 of {year + 1}");

            // A week past the last is an error in both packages, never the next year's first week.
            foreach (var past in (int[]) [0, weeks + 1])
            {
                Expect(failures, $"DateRange.IsoWeek({year}, {past})", Outcome(() => DateRange.IsoWeek(year, past)), "throws ArgumentOutOfRangeException");
                Expect(failures, $"LocalDateRange.IsoWeek({year}, {past})", Outcome(() => LocalDateRange.IsoWeek(year, past)), "throws ArgumentOutOfRangeException");
            }

            Expect(failures, $"YearMonthRange.Year({year})", YearMonthRange.Year(year).ToLocalDateRange().ToString(),
                   LocalDateRange.Year(year).ToString());
        }

        Candidates.Verdict(nameof(CalendarRangeParityTests), failures, "calendar ranges do not partition their year", method: nameof(NumberedRanges_PartitionTheirYear));
    }

    private static IEnumerable<DateOnly> EdgeDays()
    {
        for (var offset = 0; offset < 14; offset++)
        {
            yield return DateOnly.MinValue.AddDays(offset);
            yield return DateOnly.MaxValue.AddDays(-offset);
        }
    }

    private static void Check(DateOnly day, List<string> failures, string culture)
    {
        var label = string.Create(CultureInfo.InvariantCulture, $"{day:yyyy-MM-dd} [{(culture == "" ? "invariant" : culture)}]");
        var local = LocalDate.FromDateOnly(day);

        // ---- the BCL side, against the definitions
        var month   = DateRange.Month(day);
        var quarter = DateRange.Quarter(day);
        var year    = DateRange.Year(day);

        Expect(failures, $"{label} Month", Text(month),
               string.Create(CultureInfo.InvariantCulture, $"[{day.Year:D4}-{day.Month:D2}-01,{day.Year:D4}-{day.Month:D2}-{DateTime.DaysInMonth(day.Year, day.Month):D2}]"));
        Expect(failures, $"{label} Month(y, m)", Text(DateRange.Month(day.Year, day.Month)), Text(month));
        Expect(failures, $"{label} Quarter", Text(quarter), Text(DateRange.Quarter(day.Year, (day.Month - 1) / 3 + 1)));
        Expect(failures, $"{label} Year", Text(year), string.Create(CultureInfo.InvariantCulture, $"[{day.Year:D4}-01-01,{day.Year:D4}-12-31]"));

        if (quarter is not DateRange.Finite { Start: { Day: 1 } first } || (first.Month - 1) % 3 != 0 || !quarter.Contains(day) || !quarter.Contains(month))
            failures.Add($"{label} Quarter {Text(quarter)} does not start a quarter or hold the day");

        var weekText = Outcome(() => DateRange.IsoWeek(day));
        if (day.DayNumber <= DateOnly.MaxValue.DayNumber - 6 + ((int)day.DayOfWeek + 6) % 7)
        {
            if (weekText.StartsWith("throws", StringComparison.Ordinal))
            {
                failures.Add($"{label} IsoWeek → {weekText}, expected the week holding the day");
                return;
            }

            var week = DateRange.IsoWeek(day);
            if (week is not DateRange.Finite { Start.DayOfWeek: DayOfWeek.Monday } || week.Length != 7 || !week.Contains(day))
                failures.Add($"{label} IsoWeek {Text(week)} is not the Monday-to-Sunday week holding the day");

            // .NET's own ISO 8601 calendar, as the independent answer for the numbering.
            var dateTime = day.ToDateTime(TimeOnly.MinValue);
            Expect(failures, $"{label} IsoWeek(weekYear, week)",
                   Outcome(() => DateRange.IsoWeek(ISOWeek.GetYear(dateTime), ISOWeek.GetWeekOfYear(dateTime))), weekText);
        }
        else if (!weekText.StartsWith("throws ArgumentOutOfRangeException", StringComparison.Ordinal))
        {
            failures.Add($"{label} IsoWeek runs past DateOnly.MaxValue but answered {weekText}");
        }

        // ---- the NodaTime side, against the BCL side
        Expect(failures, $"{label} LocalDateRange.Month", LocalDateRange.Month(local).ToString(), month.ToString());
        Expect(failures, $"{label} LocalDateRange.Quarter", LocalDateRange.Quarter(local).ToString(), quarter.ToString());
        Expect(failures, $"{label} LocalDateRange.Year", LocalDateRange.Year(local).ToString(), year.ToString());
        Expect(failures, $"{label} LocalDateRange.IsoWeek", Outcome(() => LocalDateRange.IsoWeek(local)), weekText);
        Expect(failures, $"{label} LocalDateRange.IsoWeek(weekYear, week)",
               Outcome(() => LocalDateRange.IsoWeek(WeekYearRules.Iso.GetWeekYear(local), WeekYearRules.Iso.GetWeekOfWeekYear(local))),
               Outcome(() => LocalDateRange.IsoWeek(local)));

        // ---- the month ranges, against the day ranges
        var yearMonth = local.ToYearMonth();
        Expect(failures, $"{label} YearMonthRange.Quarter", YearMonthRange.Quarter(yearMonth).ToLocalDateRange().ToString(), quarter.ToString());
        Expect(failures, $"{label} YearMonthRange.Year", YearMonthRange.Year(yearMonth).ToLocalDateRange().ToString(), year.ToString());
    }

    private static string Text(DateRange range) => range.ToString();

    private static void Expect(List<string> failures, string what, string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            failures.Add($"{what} → {actual}, expected {expected}");
    }

    private static string Outcome(Func<object> run)
    {
        try
        {
            return run().ToString() ?? "null";
        }
        catch (Exception exception)
        {
            return $"throws {exception.GetType().Name}";
        }
    }
}

using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests.Fuzzing;

/// <summary>
/// How to draw values of one element type for the differential sweep: the exact extremes, the
/// finite edge both sides store, a centre, and the units a value is stepped by.
/// </summary>
/// <remarks>
/// <para>
/// A value is drawn near something on purpose — an extreme, an edge, the centre, or another value
/// of the same row — and then stepped by a few units, so equal, adjacent, touching and
/// off-by-one bounds come up constantly instead of once in a few billion draws.
/// </para>
/// <para>
/// Temporal values are drawn at the store's precision (whole microseconds, offset kept). A value
/// below a microsecond is a different value once stored, which is the store rule and has its own
/// authority (<c>TemporalStoreParityTests</c>); drawing them here would only re-find that rule on
/// every other row.
/// </para>
/// </remarks>
internal sealed class FuzzDomain<T>(
    T             low,
    T             high,
    T             centre,
    T[]           extremes,
    Func<T, long, T>[] units,
    Func<T, T>?   snap = null
) where T : IComparable<T>
{
    /// <summary>Whether <paramref name="value"/> is one of the exact extremes (a value the store writes as ±infinity, or the domain's end).</summary>
    public bool IsExtreme(T value) => extremes.Any(e => e.CompareTo(value) == 0);

    /// <summary>Draws one value.</summary>
    public T Draw(Random random) => random.Next(10) switch
    {
        0 => extremes.Length > 0 ? extremes[random.Next(extremes.Length)] : Near(random, centre),
        1 => Clamp(Step(random, low, random.Next(0, 4))),
        2 => Clamp(Step(random, high, -random.Next(0, 4))),
        _ => Near(random, centre)
    };

    /// <summary>Draws a value a few units from <paramref name="anchor"/>, either side.</summary>
    public T Near(Random random, T anchor) => Clamp(Step(random, anchor, random.Next(-6, 7)));

    /// <summary>Draws a value at or a few units above <paramref name="anchor"/>.</summary>
    public T AtOrAbove(Random random, T anchor) => Clamp(Step(random, anchor, random.Next(0, 4)));

    private T Step(Random random, T value, long count)
    {
        var unit = units[random.Next(units.Length)];
        try
        {
            return unit(value, count);
        }
        catch (ArgumentOutOfRangeException)
        {
            return count < 0 ? low : high;
        }
        catch (OverflowException)
        {
            return count < 0 ? low : high;
        }
    }

    // An extreme is kept exactly when it is drawn as one; a stepped value stays inside the edges,
    // so stepping never walks off the domain one of the two sides can store, and is snapped to the
    // store's precision — a step from DateTime.MaxValue would otherwise keep its last tick.
    private T Clamp(T value) =>
        extremes.Any(e => e.CompareTo(value) == 0) ? value
      : value.CompareTo(low)  < 0 ? low
      : value.CompareTo(high) > 0 ? high
                                  : snap is null ? value : snap(value);
}

/// <summary>The element domains, by element type. A type with no entry fails the sweep loudly.</summary>
internal static class FuzzDomains
{
    private const long Microsecond = TimeSpan.TicksPerMillisecond / 1000;

    private static readonly TimeSpan[] Offsets =
        [TimeSpan.Zero, TimeSpan.FromHours(5.5), TimeSpan.FromHours(-8), TimeSpan.FromHours(14), TimeSpan.FromHours(-12)];

    public static FuzzDomain<T> For<T>() where T : IComparable<T> =>
        (FuzzDomain<T>)(object)(typeof(T) switch
        {
            var t when t == typeof(int) => new FuzzDomain<int>(
                int.MinValue + 1, int.MaxValue - 1, 0, [int.MinValue, int.MaxValue],
                [(v, k) => checked(v + (int)k), (v, k) => checked(v + (int)k * 1000)]),

            var t when t == typeof(short) => new FuzzDomain<short>(
                (short)(short.MinValue + 1), (short)(short.MaxValue - 1), (short)0, [short.MinValue, short.MaxValue],
                [(v, k) => checked((short)(v + k))]),

            var t when t == typeof(long) => new FuzzDomain<long>(
                long.MinValue + 1, long.MaxValue - 1, 0L, [long.MinValue, long.MaxValue],
                [(v, k) => checked(v + k), (v, k) => checked(v + k * 1_000_000_007L)]),

            // numeric holds every decimal; scale is varied (1, 1.0, 0.5, 0.0001) because the literal
            // keeps it and the value does not, and both sides must agree that 1.0 and 1 are one value.
            var t when t == typeof(decimal) => new FuzzDomain<decimal>(
                decimal.MinValue, decimal.MaxValue, 0m, [decimal.MinValue, decimal.MaxValue],
                [(v, k) => v + k, (v, k) => v + k * 1.0m, (v, k) => v + k * 0.5m, (v, k) => v + k * 0.0001m]),

            // DateOnly.MinValue and MaxValue reach PostgreSQL as -infinity and infinity; the edges
            // one day inside them are ordinary dates on both sides.
            var t when t == typeof(DateOnly) => new FuzzDomain<DateOnly>(
                DateOnly.MinValue.AddDays(1), DateOnly.MaxValue.AddDays(-1), new DateOnly(2024, 2, 28),
                [DateOnly.MinValue, DateOnly.MaxValue],
                [(v, k) => v.AddDays((int)k), (v, k) => v.AddMonths((int)k)]),

            var t when t == typeof(TimeOnly) => new FuzzDomain<TimeOnly>(
                TimeOnly.MinValue, new TimeOnly(23, 59, 59, 999, 999), new TimeOnly(12, 0),
                [TimeOnly.MinValue, new TimeOnly(23, 59, 59, 999, 999)],
                [(v, k) => AddTime(v, k * Microsecond), (v, k) => AddTime(v, k * TimeSpan.TicksPerMinute),
                 (v, k) => AddTime(v, k * TimeSpan.TicksPerHour)],
                v => new TimeOnly(v.Ticks - v.Ticks % Microsecond)),

            var t when t == typeof(DateTime) => new FuzzDomain<DateTime>(
                new DateTime(1, 1, 2), new DateTime(9999, 12, 30), new DateTime(2024, 2, 28, 23, 59, 59),
                [DateTime.MinValue, DateTime.MaxValue],
                [(v, k) => v.AddTicks(k * Microsecond), (v, k) => v.AddSeconds(k), (v, k) => v.AddDays(k)],
                v => v.AddTicks(-(v.Ticks % Microsecond))),

            var t when t == typeof(DateTimeOffset) => new FuzzDomain<DateTimeOffset>(
                new DateTimeOffset(1, 1, 2, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(9999, 12, 30, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 2, 28, 23, 59, 59, TimeSpan.Zero),
                [DateTimeOffset.MinValue, DateTimeOffset.MaxValue],
                [(v, k) => Reoffset(v.AddTicks(k * Microsecond), k), (v, k) => Reoffset(v.AddSeconds(k), k),
                 (v, k) => Reoffset(v.AddDays(k), k)],
                v => v.AddTicks(-(v.Ticks % Microsecond))),

            // LocalDate.MinIsoValue (-9998) and MaxIsoValue reach PostgreSQL as -infinity and infinity;
            // a finite date below 4713 BC does not exist there, so the low edge is year 1.
            var t when t == typeof(LocalDate) => new FuzzDomain<LocalDate>(
                new LocalDate(1, 1, 2), new LocalDate(9999, 12, 30), new LocalDate(2024, 2, 28),
                [LocalDate.MinIsoValue, LocalDate.MaxIsoValue],
                [(v, k) => v.PlusDays((int)k), (v, k) => v.PlusMonths((int)k)]),

            var t when t == typeof(LocalDateTime) => new FuzzDomain<LocalDateTime>(
                new LocalDateTime(1, 1, 2, 0, 0), new LocalDateTime(9999, 12, 30, 0, 0), new LocalDateTime(2024, 2, 28, 23, 59, 59),
                [LocalDateTime.MinIsoValue, LocalDateTime.MaxIsoValue],
                [(v, k) => v.PlusTicks(k * Microsecond), (v, k) => v.PlusSeconds(k), (v, k) => v.PlusDays((int)k)],
                v => v.PlusNanoseconds(-(v.NanosecondOfSecond % 1000))),

            var t when t == typeof(Instant) => new FuzzDomain<Instant>(
                Instant.FromUtc(1, 1, 2, 0, 0), Instant.FromUtc(9999, 12, 30, 0, 0), Instant.FromUtc(2024, 2, 28, 23, 59, 59),
                [Instant.MinValue, Instant.MaxValue],
                [(v, k) => v.PlusTicks(k * Microsecond), (v, k) => v.Plus(Duration.FromSeconds(k)), (v, k) => v.Plus(Duration.FromDays(k))],
                v => v.PlusNanoseconds(-(v.InUtc().NanosecondOfSecond % 1000))),

            var t when t == typeof(LocalTime) => new FuzzDomain<LocalTime>(
                LocalTime.Midnight, LocalTime.FromHourMinuteSecondMillisecondTick(23, 59, 59, 999, 9990), LocalTime.Noon,
                [LocalTime.MinValue, LocalTime.FromHourMinuteSecondMillisecondTick(23, 59, 59, 999, 9990)],
                [(v, k) => AddLocalTime(v, k * Microsecond), (v, k) => AddLocalTime(v, k * TimeSpan.TicksPerMinute)],
                v => v.PlusNanoseconds(-(v.NanosecondOfSecond % 1000))),

            // A month-aligned date on the server, so the shared edges are the first and last month of
            // the years both calendars hold.
            var t when t == typeof(YearMonth) => new FuzzDomain<YearMonth>(
                new YearMonth(1, 2), new YearMonth(9999, 11), new YearMonth(2024, 2),
                [new YearMonth(1, 1), new YearMonth(9999, 12)],
                [(v, k) => v.PlusMonths((int)k), (v, k) => v.PlusMonths((int)k * 12)]),

            _ => throw new InvalidOperationException(
                     $"The differential sweep has no domain for element type '{typeof(T)}'. Add one to "
                   + $"{nameof(FuzzDomains)} — a type the sweep cannot draw is a type it does not check.")
        });

    private static TimeOnly AddTime(TimeOnly value, long ticks)
    {
        var result = value.Ticks + ticks;
        if (result < 0 || result > new TimeOnly(23, 59, 59, 999, 999).Ticks) throw new ArgumentOutOfRangeException(nameof(ticks));
        return new TimeOnly(result);
    }

    private static LocalTime AddLocalTime(LocalTime value, long ticks)
    {
        var result = value.TickOfDay + ticks;
        if (result < 0 || result >= TimeSpan.TicksPerDay) throw new ArgumentOutOfRangeException(nameof(ticks));
        return LocalTime.FromTicksSinceMidnight(result);
    }

    // The offset changes with the step so a row mixes offsets; the instant is what both sides compare.
    private static DateTimeOffset Reoffset(DateTimeOffset value, long salt)
    {
        var offset = Offsets[(int)(Math.Abs(salt) % Offsets.Length)];
        try
        {
            return value.ToOffset(offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return value;
        }
    }

    // ---------------------------------------------------------------------
    // Value set elements
    // ---------------------------------------------------------------------

    /// <summary>
    /// Strings chosen to disagree with each other under some comparison or escaping rule: case and
    /// culture folds (ß/ss, dotted and dotless i, ligatures, composed and decomposed forms), leading
    /// and trailing whitespace, and everything PostgreSQL's array literal quotes or escapes.
    /// </summary>
    public static readonly string[] Strings =
    [
        "a", "A", "b", "B", "z", "Z", "0", "9", "10",
        "ä", "ä", "é", "é", "Å", "Å", "ß", "ss", "SS", "ẞ",
        "İ", "i", "ı", "I", "ﬃ", "ffi", "æ", "ae", "Straße", "STRASSE",
        " a", "a ", " ", "  ", "\t", "\ta", "a\n", "a b", "",
        "NULL", "null", "\"", "\\", ",", "{", "}", "{a,b}", "'", "''", "\"a\"", "a,b", "\\N",
        "日本", "😀", "a😀", " ", "​"
    ];

    private static readonly Guid[] Guids =
    [
        Guid.Empty,
        Guid.AllBitsSet,
        new("00000000-0000-0000-0000-000000000001"),
        new("10000000-0000-0000-0000-000000000000"),
        new("7fffffff-ffff-ffff-ffff-ffffffffffff"),
        new("80000000-0000-0000-0000-000000000000"),
        new("0f0f0f0f-0000-0000-0000-000000000000"),
        new("f0f0f0f0-0000-0000-0000-000000000000")
    ];

    /// <summary>A drawer of set elements by element type — a primitive the sets store directly.</summary>
    public static Func<Random, object> SetElement(Type elementType)
    {
        if (elementType == typeof(string)) return random => Strings[random.Next(Strings.Length)];
        if (elementType == typeof(Guid))
            return random => random.Next(3) == 0 ? RandomGuid(random) : Guids[random.Next(Guids.Length)];

        var draw = typeof(FuzzDomains).GetMethod(nameof(DrawBoxed), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                                      .MakeGenericMethod(elementType);
        var domain = typeof(FuzzDomains).GetMethod(nameof(For))!.MakeGenericMethod(elementType).Invoke(null, null)!;
        return random => draw.Invoke(null, [domain, random])!;
    }

    private static object DrawBoxed<T>(FuzzDomain<T> domain, Random random) where T : IComparable<T> => domain.Draw(random)!;

    private static Guid RandomGuid(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    /// <summary>
    /// The round-trip text of a primitive, which is the form a validated wrapper parses from — the
    /// contract of the wrapper arities is that the wrapper's text form is the primitive's.
    /// </summary>
    public static string RoundTripText(object value) => value switch
    {
        string s         => s,
        Guid g           => g.ToString("D"),
        DateOnly d       => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly t       => t.ToString("O", CultureInfo.InvariantCulture),
        DateTime d       => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        LocalDate d      => LocalDatePattern.Iso.Format(d),
        LocalDateTime d  => LocalDateTimePattern.ExtendedIso.Format(d),
        Instant i        => InstantPattern.ExtendedIso.Format(i),
        LocalTime t      => LocalTimePattern.ExtendedIso.Format(t),
        YearMonth m      => YearMonthPattern.Iso.Format(m),
        IFormattable f   => f.ToString(null, CultureInfo.InvariantCulture),
        _                => value.ToString()!
    };
}

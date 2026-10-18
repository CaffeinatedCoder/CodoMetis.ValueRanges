using System.Globalization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The temporal element grammar: strict ISO 8601 — the forms this library writes and the forms
/// PostgreSQL writes in its default ISO <c>DateStyle</c> — and nothing else.
/// </summary>
/// <remarks>
/// Until 9.0.0 the BCL temporal types parsed through the lenient BCL <c>Parse</c>, which reads the
/// invariant culture month first: <c>[01.02.2027,05.03.2027)</c> was January 2 to May 2, and a
/// <c>DateTimeOffset</c> without an offset took the parsing machine's. Every row in the
/// <c>Rejects_*</c> tests below parsed before — most of them into a different value.
/// <c>TemporalLiteralStrictnessContractTests</c> (conventions) sweeps the rejection through every
/// range and set type and every entry point.
/// </remarks>
[TestClass]
public class TemporalLiteralGrammarTests
{
    private static readonly DateTime Wall = new(2026, 1, 1, 9, 30, 0, DateTimeKind.Unspecified);

    // ── DateOnly ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("2026-01-01", "2026-01-01")]
    [DataRow("infinity", "9999-12-31")]
    [DataRow("-infinity", "0001-01-01")]
    [DataRow("INFINITY", "9999-12-31")]
    public void Date_Accepts(string text, string expected)
        => Assert.AreEqual(DateOnly.ParseExact(expected, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateRange.ParseValue(text, null));

    [TestMethod]
    [DataRow("01.02.2027")]
    [DataRow("01/02/2027")]
    [DataRow("Feb 1 2027")]
    [DataRow("2027-2-1")]
    [DataRow("27-02-01")]
    [DataRow("2027-02-01T00:00:00")]
    [DataRow("2027-02-01 BC")]
    [DataRow("10000-01-01")]
    [DataRow(" 2027-02-01")]
    public void Date_Rejects(string text)
        => Assert.ThrowsExactly<FormatException>(() => DateRange.ParseValue(text, null));

    // ── DateTime ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("2026-01-01T09:30:00.1234567", 1_234_567L)]   // this library ("O", Unspecified)
    [DataRow("2026-01-01T09:30:00", 0L)]
    [DataRow("2026-01-01 09:30:00.123456", 1_234_560L)]    // PostgreSQL, ISO DateStyle
    [DataRow("2026-01-01 09:30:00", 0L)]
    [DataRow("2026-01-01T09:30", 0L)]                      // reduced precision, hand-written
    [DataRow("2026-01-01 09:30", 0L)]
    public void DateTime_Accepts_Unspecified(string text, long extraTicks)
    {
        var value = DateTimeRange.ParseValue(text, null);

        Assert.AreEqual(Wall.AddTicks(extraTicks), value);
        Assert.AreEqual(DateTimeKind.Unspecified, value.Kind);
    }

    [TestMethod]
    public void DateTime_Accepts_Utc()
    {
        var utc = DateTimeRange.ParseValue("2026-01-01T09:30:00.0000000Z", null);

        Assert.AreEqual(DateTimeKind.Utc, utc.Kind);
        Assert.AreEqual(DateTime.SpecifyKind(Wall, DateTimeKind.Utc), utc);
    }

    /// <summary>
    /// An offset is accepted and not applied: the wall clock is kept as written, which is what
    /// PostgreSQL's <c>timestamp</c> input does (<c>'2026-01-01 09:30+05:30'::timestamp</c> is
    /// 09:30). Before 9.0.0 the offset was converted into the parsing machine's zone, so one literal
    /// held a different wall clock — the thing <c>tsrange</c> stores — on every server. Two offsets
    /// no machine shares at once make the old behaviour fail wherever this runs.
    /// </summary>
    [TestMethod]
    [DataRow("2026-01-01T09:30:00.0000000+14:00")]   // this library ("O", Local)
    [DataRow("2026-01-01T09:30:00.0000000-12:00")]
    [DataRow("2026-01-01T09:30:00+05:30")]
    [DataRow("2026-01-01 09:30:00+14")]              // PostgreSQL's short form
    [DataRow("2026-01-01 09:30:00-12")]
    [DataRow("2026-01-01 09:30:00+05:53:28")]        // tstzrange output, local mean time
    [DataRow("2026-01-01T09:30+14:00")]
    public void DateTime_Accepts_AnOffset_KeepingTheWallClock(string text)
    {
        var value = DateTimeRange.ParseValue(text, null);

        Assert.AreEqual(Wall.Ticks, value.Ticks);
        Assert.AreEqual(DateTimeKind.Local, value.Kind);
    }

    /// <summary>
    /// A local value round-trips exactly, <see cref="DateTimeKind"/> included: it formats with an
    /// offset and the offset is not applied on the way back, so no zone rule is consulted — not even
    /// for a wall clock the zone skips or repeats. Run in Berlin, because in UTC, the CI runner's
    /// zone, those two rows are ordinary wall clocks and the test checks nothing zone-specific (#119).
    /// </summary>
    [TestMethod]
    [DoNotParallelize]
    public void LocalValue_RoundTrips_WithItsKind()
    {
        using var zone = LocalTimeZone.UseBerlin();

        foreach (var local in new[]
                 {
                     new DateTime(2026, 1, 1, 9, 30, 0, DateTimeKind.Local).AddTicks(1_234_567),
                     LocalTimeZone.Skipped,
                     LocalTimeZone.Repeated,
                     DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Local),
                     DateTime.SpecifyKind(DateTime.MaxValue.AddDays(-1), DateTimeKind.Local)
                 })
        {
            var range = DateTimeRange.CreateFinite(local, local.AddHours(1));
            var back  = DateTimeRange.Parse(range.ToString(), CultureInfo.InvariantCulture);

            Assert.AreEqual(range, back, range.ToString());
            Assert.AreEqual(DateTimeKind.Local, ((DateTimeRange.Finite) back).Start.Kind);

            var set = DateTimeSet.From(local);
            Assert.AreEqual(local.Ticks, DateTimeSet.Parse(set.ToString(), CultureInfo.InvariantCulture)[0].Ticks, set.ToString());
        }
    }

    /// <summary>
    /// ISO 8601 puts no limit on fraction digits; Go and Java write nine. They are truncated to the
    /// 100 ns the BCL types hold, as System.Text.Json truncates them.
    /// </summary>
    [TestMethod]
    [DataRow("2026-01-01T09:30:00.123456789")]
    [DataRow("2026-01-01T09:30:00.1234567890123456")]
    public void DateTime_Accepts_MoreThanSevenFractionDigits_Truncated(string text)
        => Assert.AreEqual(Wall.AddTicks(1_234_567), DateTimeRange.ParseValue(text, null));

    [TestMethod]
    public void DateTime_Accepts_Infinity()
    {
        Assert.AreEqual(DateTime.MaxValue, DateTimeRange.ParseValue("infinity", null));
        Assert.AreEqual(DateTime.MinValue, DateTimeRange.ParseValue("-infinity", null));
    }

    [TestMethod]
    [DataRow("01.02.2027 09:30:00")]
    [DataRow("01/02/2027 09:30:00")]
    [DataRow("Feb 1 2027 09:30")]
    [DataRow("2027-02-01")]                         // date-only for a date-time
    [DataRow("2027-02-01T09")]
    [DataRow("2027-02-01T09:30:00,5")]              // comma fraction
    [DataRow("2027-02-01T25:00:00")]
    [DataRow("2027-02-01T09:30:00 BC")]
    [DataRow("2027-02-01T09:30:00+0530")]           // offsets are ±HH, ±HH:mm or ±HH:mm:ss
    [DataRow("2027-02-01T09:30:00+5:30")]
    [DataRow("2027-02-01T09:30:00+15:00")]          // no such offset
    [DataRow("2027-02-01T09:30:00z")]
    [DataRow("2027-02-01T09:30:00+05:99")]          // each offset field is range-checked,
    [DataRow("2027-02-01T09:30:00+05:30:99")]       // the minutes and the seconds too,
    [DataRow("2027-02-01T09:30:00+05x30")]          // and each separator is a colon
    [DataRow("2027-02-01T09:30:00+05:30x00")]
    public void DateTime_Rejects(string text)
        => Assert.ThrowsExactly<FormatException>(() => DateTimeRange.ParseValue(text, null));

    // ── DateTimeOffset ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("2026-01-01T09:30:00.0000000+00:00", 0)]   // this library ("O")
    [DataRow("2026-01-01T09:30:00.0000000+05:30", 330)]
    [DataRow("2026-01-01T09:30:00Z", 0)]
    [DataRow("2026-01-01 09:30:00+00", 0)]              // PostgreSQL, ISO DateStyle
    [DataRow("2026-01-01 09:30:00.123456+05:30", 330)]
    [DataRow("2026-01-01 09:30:00-03", -180)]
    [DataRow("2026-01-01T09:30+01:00", 60)]
    public void DateTimeOffset_Accepts(string text, int offsetMinutes)
    {
        var value = DateTimeOffsetRange.ParseValue(text, null);

        Assert.AreEqual(TimeSpan.FromMinutes(offsetMinutes), value.Offset);
        Assert.AreEqual(new DateTime(2026, 1, 1, 9, 30, 0), value.DateTime.AddTicks(-(value.DateTime.Ticks % TimeSpan.TicksPerSecond)));
    }

    /// <summary>
    /// PostgreSQL writes a historical local-mean-time offset with seconds; <c>DateTimeOffset</c> cannot
    /// hold one, so the instant is kept at offset zero rather than refused.
    /// </summary>
    [TestMethod]
    public void DateTimeOffset_Accepts_PostgresSecondsOffset_AsTheInstantAtUtc()
    {
        var value = DateTimeOffsetRange.ParseValue("1850-01-01 00:00:00+05:53:28", null);

        Assert.AreEqual(TimeSpan.Zero, value.Offset);
        Assert.AreEqual(new DateTime(1849, 12, 31, 18, 6, 32), value.UtcDateTime);
        Assert.AreEqual(new DateTimeOffset(1850, 1, 1, 5, 53, 28, TimeSpan.Zero),
                        DateTimeOffsetRange.ParseValue("1850-01-01T00:00:00-05:53:28", null));
    }

    [TestMethod]
    public void DateTimeOffset_Accepts_NineFractionDigits_Truncated()
        => Assert.AreEqual(
            new DateTimeOffset(Wall.AddTicks(1_234_567), TimeSpan.Zero),
            DateTimeOffsetRange.ParseValue("2026-01-01T09:30:00.123456789Z", null));

    [TestMethod]
    public void DateTimeOffset_Accepts_Infinity()
    {
        Assert.AreEqual(DateTimeOffset.MaxValue, DateTimeOffsetRange.ParseValue("infinity", null));
        Assert.AreEqual(DateTimeOffset.MinValue, DateTimeOffsetRange.ParseValue("-infinity", null));
    }

    [TestMethod]
    [DataRow("2026-01-01T09:30:00")]                // no offset: used to take the machine's
    [DataRow("2026-01-01 09:30:00")]
    [DataRow("01.01.2026 09:30:00 +01:00")]
    [DataRow("2026-01-01")]
    [DataRow("2026-01-01T09:30:00+15:00:00")]       // no such offset
    [DataRow("2026-01-01T09:30:00+05:30:60")]
    [DataRow("2026-01-01T09:30:00x05:30:00")]       // a seconds offset needs its sign
    [DataRow("2026-01-01T09:30:00+05x30:00")]       // and both colons
    [DataRow("0001-01-01T00:00:00+00:00:01")]       // the instant falls before DateTime.MinValue
    [DataRow("9999-12-31T23:59:59-00:00:01")]       // or after DateTime.MaxValue
    public void DateTimeOffset_Rejects(string text)
        => Assert.ThrowsExactly<FormatException>(() => DateTimeOffsetRange.ParseValue(text, null));

    /// <summary>
    /// The largest offset fields a seconds offset may carry, and the instants at the very edge of
    /// the domain: each is the last value its range check lets through.
    /// </summary>
    [TestMethod]
    [DataRow("2026-01-01T09:30:00+14:00:00", "2025-12-31T19:30:00")]
    [DataRow("2026-01-01T09:30:00+05:59:00", "2026-01-01T03:31:00")]
    [DataRow("2026-01-01T09:30:00+05:30:59", "2026-01-01T03:59:01")]
    [DataRow("0001-01-01T00:00:00+00:00:00", "0001-01-01T00:00:00")]
    [DataRow("9999-12-31T23:59:59.9999999-00:00:00", "9999-12-31T23:59:59.9999999")]
    public void DateTimeOffset_Accepts_SecondsOffset_AtItsLimits(string text, string utc)
        => Assert.AreEqual(
            new DateTimeOffset(DateTime.ParseExact(utc, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture), TimeSpan.Zero),
            DateTimeOffsetRange.ParseValue(text, null));

    // ── TimeOnly ────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow("09:30", 0L)]
    [DataRow("09:30:00", 0L)]
    [DataRow("09:30:00.1234567", 1_234_567L)]
    [DataRow("09:30:00.123456", 1_234_560L)]
    [DataRow("09:30:00.123456789", 1_234_567L)]
    public void Time_Accepts(string text, long extraTicks)
        => Assert.AreEqual(new TimeOnly(9, 30).Add(TimeSpan.FromTicks(extraTicks)), TimeRange.ParseValue(text, null));

    [TestMethod]
    [DataRow("9:30 AM")]
    [DataRow("9:30")]
    [DataRow("24:00:00")]                           // PostgreSQL's end of day: unrepresentable
    [DataRow("09.30")]
    [DataRow("09:30:00,5")]
    [DataRow("T09:30")]
    [DataRow("infinity")]                           // time has no infinity in PostgreSQL either
    public void Time_Rejects(string text)
        => Assert.ThrowsExactly<FormatException>(() => TimeRange.ParseValue(text, null));

    // ── Through the literals ────────────────────────────────────────────────────────────────

    /// <summary>The motivating case: dotted dates used to parse, month first.</summary>
    [TestMethod]
    public void DottedDateRange_IsRefused_NotReadMonthFirst()
    {
        Assert.ThrowsExactly<FormatException>(() => DateRange.Parse("[01.02.2027,05.03.2027)", CultureInfo.InvariantCulture));
        Assert.IsFalse(DateRange.TryParse("[01.02.2027,05.03.2027)", CultureInfo.InvariantCulture, out _));
        Assert.ThrowsExactly<FormatException>(() => DateSet.Parse("{01.02.2027}", CultureInfo.InvariantCulture));
        Assert.ThrowsExactly<FormatException>(() => DateSet<TestDay>.Parse("{01.02.2027}", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// PostgreSQL's own range output — quoted, space-separated, short offsets, <c>infinity</c> — reads
    /// back, so text copied from <c>psql</c> or stored by <c>to_jsonb</c> parses.
    /// </summary>
    [TestMethod]
    public void PostgresRangeOutput_Parses()
    {
        Assert.AreEqual(
            DateTimeRange.CreateFinite(Wall, Wall.AddHours(7).AddTicks(1_234_560)),
            DateTimeRange.Parse("[\"2026-01-01 09:30:00\",\"2026-01-01 16:30:00.123456\")", CultureInfo.InvariantCulture));
        Assert.AreEqual(
            DateTimeOffsetRange.CreateFinite(new DateTimeOffset(Wall, TimeSpan.Zero), new DateTimeOffset(Wall.AddHours(1), TimeSpan.Zero)),
            DateTimeOffsetRange.Parse("[\"2026-01-01 09:30:00+00\",\"2026-01-01 11:30:00+01\")", CultureInfo.InvariantCulture));
        Assert.AreEqual(
            DateRange.CreateFinite(new DateOnly(2026, 1, 1), DateOnly.MaxValue, endInclusive: false),
            DateRange.Parse("[2026-01-01,infinity)", CultureInfo.InvariantCulture));
    }

    /// <summary>Infinity reads as the extreme value; formatting is unchanged, so the value round-trips.</summary>
    [TestMethod]
    public void InfinityBound_RoundTripsAsTheExtremeValue()
    {
        var parsed = DateTimeRange.Parse("[-infinity,2026-01-01T00:00:00)", CultureInfo.InvariantCulture);

        Assert.AreEqual(DateTimeRange.CreateFinite(DateTime.MinValue, new DateTime(2026, 1, 1)), parsed);
        Assert.AreEqual(parsed, DateTimeRange.Parse(parsed.ToString(), CultureInfo.InvariantCulture));
    }

    /// <summary>A hand-written opening-hours literal without seconds.</summary>
    [TestMethod]
    public void TimeRange_WithoutSeconds_Parses()
        => Assert.AreEqual(
            TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0)),
            TimeRange.Parse("[09:00,17:00)", CultureInfo.InvariantCulture));

    /// <summary>
    /// A wrapper arity reads the text as its primitive first, so a lenient element <c>Parse</c> never
    /// sees non-ISO text, and the element still re-validates the canonical form.
    /// </summary>
    [TestMethod]
    public void WrapperArity_ReadsPostgresForm_ThroughTheStrictPrimitive()
    {
        var set = DateTimeSet<TestStamp>.Parse("{\"2026-01-01 09:30:00.123456\"}", CultureInfo.InvariantCulture);

        Assert.AreEqual(DateTimeSet<TestStamp>.From(TestStamp.Parse(Wall.AddTicks(1_234_560).ToString("O", CultureInfo.InvariantCulture), null)), set);
        Assert.ThrowsExactly<FormatException>(() => DateTimeSet<TestStamp>.Parse("{\"01/01/2026 09:30:00\"}", CultureInfo.InvariantCulture));
    }
}

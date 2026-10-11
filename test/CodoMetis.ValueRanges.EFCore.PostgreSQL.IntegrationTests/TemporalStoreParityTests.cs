using System.Globalization;
using System.Linq.Expressions;
using CodoMetis.ValueRanges.Conventions.Tests;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NodaTime;
using NodaTime.Text;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>The temporal values of <see cref="TemporalParityValues"/>, once as columns and once in a JSON document.</summary>
public sealed class TemporalParityRow
{
    public int Id { get; set; }
    public TemporalParityValues Native { get; set; } = new();
    public TemporalParityValues Doc { get; set; } = new();
}

/// <summary>
/// One property per registered store conversion: every temporal range type, closed set and wrapper
/// set family, except the three over <see cref="YearMonth"/> — month-aligned dates, with nothing
/// below a day to truncate — plus two of the multiranges.
/// </summary>
/// <remarks>
/// A multirange has no registration of its own; it takes its range type's definition, so
/// <c>WallMulti</c> and <c>NInstMulti</c> stand for the other five. Every wrapper family is here
/// because each is a separate registration that has to pass its own truncation.
/// </remarks>
public sealed class TemporalParityValues
{
    public DateTimeRange                        Wall       { get; set; } = DateTimeRange.Empty;
    public DateTimeOffsetRange                  Stz        { get; set; } = DateTimeOffsetRange.Empty;
    public TimeRange                            Time       { get; set; } = TimeRange.Empty;
    public DateRange                            Day        { get; set; } = DateRange.Empty;
    public LocalDateTimeRange                   NWall      { get; set; } = LocalDateTimeRange.Empty;
    public InstantRange                         NInst      { get; set; } = InstantRange.Empty;
    public LocalDateRange                       NDay       { get; set; } = LocalDateRange.Empty;
    public RangeSet<DateTimeRange, DateTime>    WallMulti  { get; set; } = RangeSet<DateTimeRange, DateTime>.Empty;
    public RangeSet<InstantRange, Instant>      NInstMulti { get; set; } = RangeSet<InstantRange, Instant>.Empty;
    public DateTimeSet                          WallSet    { get; set; } = DateTimeSet.Empty;
    public DateTimeOffsetSet                    StzSet     { get; set; } = DateTimeOffsetSet.Empty;
    public TimeSet                              TimeSet    { get; set; } = TimeSet.Empty;
    public DateSet                              DaySet     { get; set; } = DateSet.Empty;
    public InstantSet                           NInstSet   { get; set; } = InstantSet.Empty;
    public LocalDateTimeSet                     NWallSet   { get; set; } = LocalDateTimeSet.Empty;
    public LocalTimeSet                         NTimeSet   { get; set; } = LocalTimeSet.Empty;
    public LocalDateSet                         NDaySet    { get; set; } = LocalDateSet.Empty;
    public DateTimeSet<AuditStamp>              Audits     { get; set; } = DateTimeSet<AuditStamp>.Empty;
    public TimeSet<ShiftTime>                   Shifts     { get; set; } = TimeSet<ShiftTime>.Empty;
    public DateTimeOffsetSet<EventStamp>        Events     { get; set; } = DateTimeOffsetSet<EventStamp>.Empty;
    public DateSet<BusinessDate>                BizDays    { get; set; } = DateSet<BusinessDate>.Empty;
    public LocalDateTimeSet<WallClockStamp>     NWallMarks { get; set; } = LocalDateTimeSet<WallClockStamp>.Empty;
    public InstantSet<EventInstant>             NInstMarks { get; set; } = InstantSet<EventInstant>.Empty;
    public LocalTimeSet<OpeningTime>            NOpenings  { get; set; } = LocalTimeSet<OpeningTime>.Empty;
    public LocalDateSet<CalendarDay>            NCalDays   { get; set; } = LocalDateSet<CalendarDay>.Empty;
}

/// <summary>
/// A temporal value written as text — inside a JSON column, or as a SQL literal — must be the value a
/// parameter and a native column hold for it. This is the authority on that rule, against a live server.
/// </summary>
/// <remarks>
/// <para>
/// Until 9.0.0 the JSON writer and the literals sent seven fraction digits (nine for NodaTime) and let
/// PostgreSQL round them half-to-even, while Npgsql truncates a parameter to the microsecond toward
/// 2000-01-01. For a remainder above 500 ns after 2000 — below it before — the document held a
/// different value from the column: <c>Doc.X == v</c> missed its own row, <c>Contains</c> disagreed at
/// the bounds, and a range narrower than a microsecond was empty only on the server. <c>MinValue</c> /
/// <c>MaxValue</c> stayed finite in text while a parameter carried <c>-infinity</c> / <c>infinity</c>.
/// </para>
/// <para>
/// Each probe is one row holding every type twice, built from one instant. Per property it asserts:
/// the server reads the document as the column (<c>CAST(doc -&gt;&gt; 'X' AS store) = "X"</c>); a parameter
/// and a constant each match both copies; the two copies materialize equal; and <c>Contains</c> agrees
/// between the materialized value in memory and the server, on points one microsecond either side of
/// every bound. Points are whole microseconds, so the comparison is between stored values rather than
/// a test of how a probe point itself is truncated.
/// </para>
/// <para>
/// That last question — a sub-microsecond element operand, as a parameter and as a constant — is
/// asked separately, against an expected answer rather than only between the two paths:
/// <see cref="StoredElementMisses"/> and <see cref="RangeElementDisagreements"/>.
/// </para>
/// </remarks>
[TestClass]
public sealed class TemporalStoreParityTests
{
    private const long OneMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    /// <summary>The probe instants, by the rounding case each one exercises.</summary>
    private static readonly (string Name, DateTime Instant)[] Probes =
    [
        ("after 2000, remainder above 500 ns", new DateTime(2026, 3, 15, 10, 20, 30).AddTicks(1_234_567)),
        ("after 2000, remainder below 500 ns", new DateTime(2026, 3, 15, 10, 20, 30).AddTicks(1_234_561)),
        ("after 2000, exactly 500 ns, odd 6th digit", new DateTime(2026, 3, 15, 10, 20, 30).AddTicks(1_234_575)),
        ("before 2000, remainder below 500 ns", new DateTime(1999, 12, 31, 22, 59, 59).AddTicks(1_234_561)),
        ("before 2000, remainder above 500 ns", new DateTime(1999, 12, 31, 22, 59, 59).AddTicks(1_234_569)),
    ];

    private static TemporalParityValues Build(DateTime instant)
    {
        var wall   = DateTime.SpecifyKind(instant, DateTimeKind.Unspecified);
        var time   = new TimeOnly(10, 20, 30).Add(TimeSpan.FromTicks(instant.Ticks % TimeSpan.TicksPerSecond));
        var noda   = Instant.FromDateTimeUtc(DateTime.SpecifyKind(instant, DateTimeKind.Utc)).PlusNanoseconds(89);
        var nwall  = LocalDateTime.FromDateTime(wall).PlusNanoseconds(89);
        var ntime  = LocalTime.FromTicksSinceMidnight(time.Ticks).PlusNanoseconds(89);
        var offset = TimeSpan.FromMinutes(330);

        return new TemporalParityValues
        {
            Wall       = DateTimeRange.CreateFinite(wall, wall.AddHours(1).AddTicks(3)),
            Stz        = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(wall, offset), new DateTimeOffset(wall.AddHours(1).AddTicks(3), offset)),
            Time       = TimeRange.CreateFinite(time, time.AddHours(1).Add(TimeSpan.FromTicks(3))),
            Day        = DateRange.CreateFinite(DateOnly.FromDateTime(wall), DateOnly.FromDateTime(wall).AddDays(10)),
            NWall      = LocalDateTimeRange.CreateFinite(nwall, nwall.PlusHours(1).PlusNanoseconds(399)),
            NInst      = InstantRange.CreateFinite(noda, noda.Plus(Duration.FromHours(1)).PlusNanoseconds(399)),
            NDay       = LocalDateRange.CreateFinite(LocalDate.FromDateTime(wall), LocalDate.FromDateTime(wall).PlusDays(10)),
            WallMulti  = RangeSet<DateTimeRange, DateTime>.From(
                             DateTimeRange.CreateFinite(wall, wall.AddHours(1).AddTicks(3)),
                             DateTimeRange.CreateFinite(wall.AddHours(2).AddTicks(7), wall.AddHours(3))),
            NInstMulti = RangeSet<InstantRange, Instant>.From(
                             InstantRange.CreateFinite(noda, noda.Plus(Duration.FromHours(1)).PlusNanoseconds(399)),
                             InstantRange.CreateFinite(noda.Plus(Duration.FromHours(2)).PlusNanoseconds(7), noda.Plus(Duration.FromHours(3)))),

            // Two elements a few hundred nanoseconds apart: one store value, so the stored array
            // must hold one — the count in memory, after materialization, and on the server agree.
            WallSet    = DateTimeSet.From(wall, wall.AddTicks(1), wall.AddHours(5)),
            StzSet     = DateTimeOffsetSet.From(new DateTimeOffset(wall, offset), new DateTimeOffset(wall.AddHours(5), offset)),
            TimeSet    = TimeSet.From(time, time.AddHours(5)),
            DaySet     = DateSet.From(DateOnly.FromDateTime(wall)),
            NInstSet   = InstantSet.From(noda, noda.Plus(Duration.FromHours(5))),
            NWallSet   = LocalDateTimeSet.From(nwall, nwall.PlusHours(5)),
            NTimeSet   = LocalTimeSet.From(ntime, ntime.PlusHours(5)),
            NDaySet    = LocalDateSet.From(LocalDate.FromDateTime(wall)),
            Audits     = DateTimeSet<AuditStamp>.From(new AuditStamp(wall), new AuditStamp(wall.AddHours(5))),

            // Every other wrapper family has its own registration, so each is here — and the sub-day
            // ones with two elements that truncate to one store value, like WallSet above.
            Shifts     = TimeSet<ShiftTime>.From(Shift(time), Shift(time.Add(TimeSpan.FromTicks(1))), Shift(time.AddHours(5))),
            Events     = DateTimeOffsetSet<EventStamp>.From(Event(new DateTimeOffset(wall, offset)), Event(new DateTimeOffset(wall.AddTicks(1), offset))),
            BizDays    = DateSet<BusinessDate>.From(BizDay(DateOnly.FromDateTime(wall))),
            NWallMarks = LocalDateTimeSet<WallClockStamp>.From(NWallMark(nwall), NWallMark(nwall.PlusNanoseconds(1))),
            NInstMarks = InstantSet<EventInstant>.From(NInstMark(noda), NInstMark(noda.PlusNanoseconds(1)), NInstMark(noda.Plus(Duration.FromHours(5)))),
            NOpenings  = LocalTimeSet<OpeningTime>.From(NOpening(ntime), NOpening(ntime.PlusNanoseconds(1))),
            NCalDays   = LocalDateSet<CalendarDay>.From(NCalDay(LocalDate.FromDateTime(wall)))
        };
    }

    // The wrapper elements' constructors are private: they are built from text, as a consumer's are.
    private static ShiftTime Shift(TimeOnly value) => ShiftTime.Parse(value.ToString("O", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    private static EventStamp Event(DateTimeOffset value) => EventStamp.Parse(value.ToString("O", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    private static BusinessDate BizDay(DateOnly value) => BusinessDate.Parse(value.ToString("O", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    private static WallClockStamp NWallMark(LocalDateTime value) => WallClockStamp.Parse(LocalDateTimePattern.ExtendedIso.Format(value), null);
    private static EventInstant NInstMark(Instant value) => EventInstant.Parse(InstantPattern.ExtendedIso.Format(value), null);
    private static OpeningTime NOpening(LocalTime value) => OpeningTime.Parse(LocalTimePattern.ExtendedIso.Format(value), null);
    private static CalendarDay NCalDay(LocalDate value) => CalendarDay.Parse(LocalDatePattern.Iso.Format(value), null);

    /// <summary>The extremes Npgsql maps to infinity, as bounds and as elements.</summary>
    private static TemporalParityValues BuildExtremes()
    {
        var wall = new DateTime(2026, 3, 15, 10, 20, 30);
        var noda = Instant.FromUtc(2026, 3, 15, 10, 20, 30);
        var day  = new DateOnly(2026, 3, 15);

        return new TemporalParityValues
        {
            Wall       = DateTimeRange.CreateFinite(DateTime.MinValue, wall),
            Stz        = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(wall, TimeSpan.Zero), DateTimeOffset.MaxValue, endInclusive: true),
            Time       = TimeRange.CreateFinite(TimeOnly.MinValue, TimeOnly.MaxValue, endInclusive: true),
            Day        = DateRange.CreateFinite(day, DateOnly.MaxValue),
            NWall      = LocalDateTimeRange.CreateFinite(LocalDateTime.MinIsoValue, LocalDateTime.FromDateTime(wall)),
            NInst      = InstantRange.CreateFinite(noda, Instant.MaxValue, endInclusive: true),
            NDay       = LocalDateRange.CreateFinite(LocalDate.MinIsoValue, LocalDate.FromDateTime(wall)),
            WallMulti  = RangeSet<DateTimeRange, DateTime>.From(DateTimeRange.CreateFinite(DateTime.MinValue, wall)),
            NInstMulti = RangeSet<InstantRange, Instant>.From(InstantRange.CreateFinite(noda, Instant.MaxValue, endInclusive: true)),
            WallSet    = DateTimeSet.From(DateTime.MinValue, wall, DateTime.MaxValue),
            StzSet     = DateTimeOffsetSet.From(DateTimeOffset.MinValue, DateTimeOffset.MaxValue),
            TimeSet    = TimeSet.From(TimeOnly.MinValue, TimeOnly.MaxValue),
            DaySet     = DateSet.From(DateOnly.MinValue, day, DateOnly.MaxValue),
            NInstSet   = InstantSet.From(Instant.MinValue, noda, Instant.MaxValue),
            NWallSet   = LocalDateTimeSet.From(LocalDateTime.MinIsoValue, LocalDateTime.MaxIsoValue),
            NTimeSet   = LocalTimeSet.From(LocalTime.MinValue, LocalTime.MaxValue),
            NDaySet    = LocalDateSet.From(LocalDate.MinIsoValue, LocalDate.MaxIsoValue),
            Audits     = DateTimeSet<AuditStamp>.From(new AuditStamp(DateTime.MinValue), new AuditStamp(DateTime.MaxValue)),
            Shifts     = TimeSet<ShiftTime>.From(Shift(TimeOnly.MinValue), Shift(TimeOnly.MaxValue)),
            Events     = DateTimeOffsetSet<EventStamp>.From(Event(DateTimeOffset.MinValue), Event(DateTimeOffset.MaxValue)),
            BizDays    = DateSet<BusinessDate>.From(BizDay(DateOnly.MinValue), BizDay(DateOnly.MaxValue)),
            NWallMarks = LocalDateTimeSet<WallClockStamp>.From(NWallMark(LocalDateTime.MinIsoValue), NWallMark(LocalDateTime.MaxIsoValue)),
            NInstMarks = InstantSet<EventInstant>.From(NInstMark(Instant.MinValue), NInstMark(Instant.MaxValue)),
            NOpenings  = LocalTimeSet<OpeningTime>.From(NOpening(LocalTime.MinValue), NOpening(LocalTime.MaxValue)),
            NCalDays   = LocalDateSet<CalendarDay>.From(NCalDay(LocalDate.MinIsoValue), NCalDay(LocalDate.MaxIsoValue))
        };
    }

    /// <summary>A range narrower than a microsecond, which the server collapses to empty.</summary>
    private static TemporalParityValues BuildNarrow()
    {
        var wall = new DateTime(2026, 3, 15, 10, 20, 30);
        var time = new TimeOnly(10, 20, 30);
        var noda = Instant.FromUtc(2026, 3, 15, 10, 20, 30);
        var nwall = LocalDateTime.FromDateTime(wall);

        return new TemporalParityValues
        {
            Wall       = DateTimeRange.CreateFinite(wall.AddTicks(1), wall.AddTicks(4)),
            Stz        = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(wall.AddTicks(6), TimeSpan.Zero), new DateTimeOffset(wall.AddTicks(9), TimeSpan.Zero)),
            Time       = TimeRange.CreateFinite(time.Add(TimeSpan.FromTicks(1)), time.Add(TimeSpan.FromTicks(4))),
            NWall      = LocalDateTimeRange.CreateFinite(nwall.PlusNanoseconds(100), nwall.PlusNanoseconds(400)),
            NInst      = InstantRange.CreateFinite(noda.PlusNanoseconds(600), noda.PlusNanoseconds(900)),
            WallMulti  = RangeSet<DateTimeRange, DateTime>.From(
                             DateTimeRange.CreateFinite(wall, wall.AddTicks(6)),
                             DateTimeRange.CreateFinite(wall.AddTicks(7), wall.AddTicks(20))),
            NInstMulti = RangeSet<InstantRange, Instant>.From(InstantRange.CreateFinite(noda.PlusNanoseconds(100), noda.PlusNanoseconds(400)))
        };
    }

    private static IEnumerable<(string Name, TemporalParityValues Values)> AllProbes()
    {
        foreach (var (name, instant) in Probes) yield return (name, Build(instant));
        yield return ("MinValue / MaxValue", BuildExtremes());
        yield return ("narrower than a microsecond", BuildNarrow());
    }

    /// <summary>Per property: name, store type, value accessor, and both copies' equality filter.</summary>
    private static IEnumerable<(string Name, string StoreType, Func<TemporalParityValues, object> Get,
                                Func<TemporalParityValues, Expression<Func<TemporalParityRow, bool>>> NativeEquals,
                                Func<TemporalParityValues, Expression<Func<TemporalParityRow, bool>>> DocEquals)> Properties()
    {
        yield return ("Wall", "tsrange", v => v.Wall, v => r => r.Native.Wall == v.Wall, v => r => r.Doc.Wall == v.Wall);
        yield return ("Stz", "tstzrange", v => v.Stz, v => r => r.Native.Stz == v.Stz, v => r => r.Doc.Stz == v.Stz);
        yield return ("Time", "timerange", v => v.Time, v => r => r.Native.Time == v.Time, v => r => r.Doc.Time == v.Time);
        yield return ("Day", "daterange", v => v.Day, v => r => r.Native.Day == v.Day, v => r => r.Doc.Day == v.Day);
        yield return ("NWall", "tsrange", v => v.NWall, v => r => r.Native.NWall == v.NWall, v => r => r.Doc.NWall == v.NWall);
        yield return ("NInst", "tstzrange", v => v.NInst, v => r => r.Native.NInst == v.NInst, v => r => r.Doc.NInst == v.NInst);
        yield return ("NDay", "daterange", v => v.NDay, v => r => r.Native.NDay == v.NDay, v => r => r.Doc.NDay == v.NDay);
        yield return ("WallMulti", "tsmultirange", v => v.WallMulti, v => r => r.Native.WallMulti == v.WallMulti, v => r => r.Doc.WallMulti == v.WallMulti);
        yield return ("NInstMulti", "tstzmultirange", v => v.NInstMulti, v => r => r.Native.NInstMulti == v.NInstMulti, v => r => r.Doc.NInstMulti == v.NInstMulti);
        yield return ("WallSet", "timestamp[]", v => v.WallSet, v => r => r.Native.WallSet == v.WallSet, v => r => r.Doc.WallSet == v.WallSet);
        yield return ("StzSet", "timestamptz[]", v => v.StzSet, v => r => r.Native.StzSet == v.StzSet, v => r => r.Doc.StzSet == v.StzSet);
        yield return ("TimeSet", "time[]", v => v.TimeSet, v => r => r.Native.TimeSet == v.TimeSet, v => r => r.Doc.TimeSet == v.TimeSet);
        yield return ("DaySet", "date[]", v => v.DaySet, v => r => r.Native.DaySet == v.DaySet, v => r => r.Doc.DaySet == v.DaySet);
        yield return ("NInstSet", "timestamptz[]", v => v.NInstSet, v => r => r.Native.NInstSet == v.NInstSet, v => r => r.Doc.NInstSet == v.NInstSet);
        yield return ("NWallSet", "timestamp[]", v => v.NWallSet, v => r => r.Native.NWallSet == v.NWallSet, v => r => r.Doc.NWallSet == v.NWallSet);
        yield return ("NTimeSet", "time[]", v => v.NTimeSet, v => r => r.Native.NTimeSet == v.NTimeSet, v => r => r.Doc.NTimeSet == v.NTimeSet);
        yield return ("NDaySet", "date[]", v => v.NDaySet, v => r => r.Native.NDaySet == v.NDaySet, v => r => r.Doc.NDaySet == v.NDaySet);
        yield return ("Audits", "timestamp[]", v => v.Audits, v => r => r.Native.Audits == v.Audits, v => r => r.Doc.Audits == v.Audits);
        yield return ("Shifts", "time[]", v => v.Shifts, v => r => r.Native.Shifts == v.Shifts, v => r => r.Doc.Shifts == v.Shifts);
        yield return ("Events", "timestamptz[]", v => v.Events, v => r => r.Native.Events == v.Events, v => r => r.Doc.Events == v.Events);
        yield return ("BizDays", "date[]", v => v.BizDays, v => r => r.Native.BizDays == v.BizDays, v => r => r.Doc.BizDays == v.BizDays);
        yield return ("NWallMarks", "timestamp[]", v => v.NWallMarks, v => r => r.Native.NWallMarks == v.NWallMarks, v => r => r.Doc.NWallMarks == v.NWallMarks);
        yield return ("NInstMarks", "timestamptz[]", v => v.NInstMarks, v => r => r.Native.NInstMarks == v.NInstMarks, v => r => r.Doc.NInstMarks == v.NInstMarks);
        yield return ("NOpenings", "time[]", v => v.NOpenings, v => r => r.Native.NOpenings == v.NOpenings, v => r => r.Doc.NOpenings == v.NOpenings);
        yield return ("NCalDays", "date[]", v => v.NCalDays, v => r => r.Native.NCalDays == v.NCalDays, v => r => r.Doc.NCalDays == v.NCalDays);
    }

    private static async Task<TemporalParityRow> Insert(int id, TemporalParityValues values)
    {
        await using (var write = new IntegrationDbContext())
        {
            write.TemporalParityRows.Add(new TemporalParityRow { Id = id, Native = values, Doc = values });
            await write.SaveChangesAsync();
        }

        await using var read = new IntegrationDbContext();
        return await read.TemporalParityRows.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private static async Task<int> Count(int id, Expression<Func<TemporalParityRow, bool>> filter)
    {
        await using var ctx = new IntegrationDbContext();
        return await ctx.TemporalParityRows.Where(r => r.Id == id).Where(filter).CountAsync();
    }

    /// <summary>Columns are flattened by the complex property: <c>Native_Wall</c> and so on.</summary>
    private static async Task<bool> ServerReadsTheDocumentAsTheColumn(int id, string property, string storeType)
    {
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""SELECT CAST("Doc" ->> '{property}' AS {storeType}) = "Native_{property}" FROM "TemporalParityRows" WHERE "Id" = {id}""",
            connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    [TestMethod]
    public async Task EveryTemporalType_IsTheSameValue_AsColumn_Document_Parameter_AndConstant()
    {
        ContainerLifecycle.RequireDatabase();

        var failures = new List<string>();
        var id       = 9500;

        foreach (var (probe, values) in AllProbes())
        {
            id++;
            var loaded = await Insert(id, values);

            foreach (var (name, storeType, get, nativeEquals, docEquals) in Properties())
            {
                var label = $"[{probe}] {name}";
                try
                {
                    if (!await ServerReadsTheDocumentAsTheColumn(id, name, storeType))
                        failures.Add($"{label}: the server reads the document as a different value from the column.");

                    if (!Equals(get(loaded.Doc), get(loaded.Native)))
                        failures.Add($"{label}: the document materialized {Text(get(loaded.Doc))}, the column {Text(get(loaded.Native))}.");

                    // The parameter is the materialized value: what the store holds.
                    var stored = get(loaded.Native);
                    var asStored = Clone(values, name, stored);
                    foreach (var (path, filter) in new[]
                             {
                                 ("column == parameter", nativeEquals(asStored)),
                                 ("document == parameter", docEquals(asStored)),
                                 ("column == parameter (as written)", nativeEquals(values)),
                                 ("document == parameter (as written)", docEquals(values)),
                             })
                    {
                        var count = await Count(id, filter);
                        if (count != 1) failures.Add($"{label}: {path} matched {count} rows.");
                    }

                    foreach (var (path, matched) in await ConstantMatches(id, name, values))
                        if (!matched) failures.Add($"{label}: {path} did not match its own row.");
                }
                catch (Exception e)
                {
                    failures.Add($"{label}: {e.GetType().Name}: {(e.InnerException ?? e).Message.Split('\n')[0]}");
                }
            }

            failures.AddRange(await ContainsDisagreements(id, probe, loaded));
            failures.AddRange(await StoredElementMisses(id, probe, values));
            failures.AddRange(await RangeElementDisagreements(id, probe, values, loaded));
        }

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The literal path: the value inlined as a constant, against the column and the document. Before
    /// 9.0.0 a constant carried seven fraction digits that the server rounded.
    /// </summary>
    private static async Task<IEnumerable<(string Path, bool Matched)>> ConstantMatches(int id, string name, TemporalParityValues v)
    {
        Expression<Func<TemporalParityRow, bool>>[] filters = name switch
        {
            "Wall"       => [r => r.Native.Wall == EF.Constant(v.Wall), r => r.Doc.Wall == EF.Constant(v.Wall)],
            "Stz"        => [r => r.Native.Stz == EF.Constant(v.Stz), r => r.Doc.Stz == EF.Constant(v.Stz)],
            "Time"       => [r => r.Native.Time == EF.Constant(v.Time), r => r.Doc.Time == EF.Constant(v.Time)],
            "Day"        => [r => r.Native.Day == EF.Constant(v.Day), r => r.Doc.Day == EF.Constant(v.Day)],
            "NWall"      => [r => r.Native.NWall == EF.Constant(v.NWall), r => r.Doc.NWall == EF.Constant(v.NWall)],
            "NInst"      => [r => r.Native.NInst == EF.Constant(v.NInst), r => r.Doc.NInst == EF.Constant(v.NInst)],
            "NDay"       => [r => r.Native.NDay == EF.Constant(v.NDay), r => r.Doc.NDay == EF.Constant(v.NDay)],
            "WallMulti"  => [r => r.Native.WallMulti == EF.Constant(v.WallMulti), r => r.Doc.WallMulti == EF.Constant(v.WallMulti)],
            "NInstMulti" => [r => r.Native.NInstMulti == EF.Constant(v.NInstMulti), r => r.Doc.NInstMulti == EF.Constant(v.NInstMulti)],
            "WallSet"    => [r => r.Native.WallSet == EF.Constant(v.WallSet), r => r.Doc.WallSet == EF.Constant(v.WallSet)],
            "StzSet"     => [r => r.Native.StzSet == EF.Constant(v.StzSet), r => r.Doc.StzSet == EF.Constant(v.StzSet)],
            "TimeSet"    => [r => r.Native.TimeSet == EF.Constant(v.TimeSet), r => r.Doc.TimeSet == EF.Constant(v.TimeSet)],
            "DaySet"     => [r => r.Native.DaySet == EF.Constant(v.DaySet), r => r.Doc.DaySet == EF.Constant(v.DaySet)],
            "NInstSet"   => [r => r.Native.NInstSet == EF.Constant(v.NInstSet), r => r.Doc.NInstSet == EF.Constant(v.NInstSet)],
            "NWallSet"   => [r => r.Native.NWallSet == EF.Constant(v.NWallSet), r => r.Doc.NWallSet == EF.Constant(v.NWallSet)],
            "NTimeSet"   => [r => r.Native.NTimeSet == EF.Constant(v.NTimeSet), r => r.Doc.NTimeSet == EF.Constant(v.NTimeSet)],
            "NDaySet"    => [r => r.Native.NDaySet == EF.Constant(v.NDaySet), r => r.Doc.NDaySet == EF.Constant(v.NDaySet)],
            "Audits"     => [r => r.Native.Audits == EF.Constant(v.Audits), r => r.Doc.Audits == EF.Constant(v.Audits)],
            "Shifts"     => [r => r.Native.Shifts == EF.Constant(v.Shifts), r => r.Doc.Shifts == EF.Constant(v.Shifts)],
            "Events"     => [r => r.Native.Events == EF.Constant(v.Events), r => r.Doc.Events == EF.Constant(v.Events)],
            "BizDays"    => [r => r.Native.BizDays == EF.Constant(v.BizDays), r => r.Doc.BizDays == EF.Constant(v.BizDays)],
            "NWallMarks" => [r => r.Native.NWallMarks == EF.Constant(v.NWallMarks), r => r.Doc.NWallMarks == EF.Constant(v.NWallMarks)],
            "NInstMarks" => [r => r.Native.NInstMarks == EF.Constant(v.NInstMarks), r => r.Doc.NInstMarks == EF.Constant(v.NInstMarks)],
            "NOpenings"  => [r => r.Native.NOpenings == EF.Constant(v.NOpenings), r => r.Doc.NOpenings == EF.Constant(v.NOpenings)],
            "NCalDays"   => [r => r.Native.NCalDays == EF.Constant(v.NCalDays), r => r.Doc.NCalDays == EF.Constant(v.NCalDays)],
            _ => throw new InvalidOperationException(name)
        };

        var results = new List<(string, bool)>();
        foreach (var (filter, path) in filters.Zip(["column == constant", "document == constant"]))
            results.Add((path, await Count(id, filter) == 1));
        return results;
    }

    /// <summary>
    /// <c>Contains</c> on whole-microsecond points one microsecond either side of every bound, in memory
    /// on the materialized value against the server on the column and on the document.
    /// </summary>
    private static async Task<IEnumerable<string>> ContainsDisagreements(int id, string probe, TemporalParityRow loaded)
    {
        var failures = new List<string>();

        async Task Check<T>(string name, IEnumerable<T> points, Func<T, bool> inMemory,
                            Func<T, Expression<Func<TemporalParityRow, bool>>> native,
                            Func<T, Expression<Func<TemporalParityRow, bool>>> doc)
        {
            foreach (var point in points)
            {
                var expected = inMemory(point) ? 1 : 0;
                var onColumn = await Count(id, native(point));
                var onDoc    = await Count(id, doc(point));
                if (onColumn != expected || onDoc != expected)
                    failures.Add($"[{probe}] {name}.Contains({Text(point)}): in memory {expected == 1}, "
                               + $"column {onColumn == 1}, document {onDoc == 1}.");
            }
        }

        IEnumerable<DateTime> Around(params DateTime[] bounds)
            => bounds.Where(b => b > DateTime.MinValue.AddTicks(OneMicrosecond) && b < DateTime.MaxValue.AddTicks(-OneMicrosecond))
                     .SelectMany(b => new[] { b.AddTicks(-OneMicrosecond), b, b.AddTicks(OneMicrosecond) });

        var n = loaded.Native;
        if (n.Wall is DateTimeRange.Finite wall)
            await Check("Wall", Around(wall.Start, wall.End), p => n.Wall.Contains(p),
                        p => r => r.Native.Wall.Contains(p), p => r => r.Doc.Wall.Contains(p));

        if (n.WallMulti.Count > 0)
            await Check("WallMulti",
                        Around([.. n.WallMulti.OfType<DateTimeRange.Finite>().SelectMany(f => new[] { f.Start, f.End })]),
                        p => n.WallMulti.Contains(p),
                        p => r => r.Native.WallMulti.Contains(p), p => r => r.Doc.WallMulti.Contains(p));

        if (n.Time is TimeRange.Finite time)
            await Check("Time",
                        new[] { time.Start, time.End }.SelectMany(b => new[] { b.Add(TimeSpan.FromTicks(-OneMicrosecond)), b, b.Add(TimeSpan.FromTicks(OneMicrosecond)) }),
                        p => n.Time.Contains(p),
                        p => r => r.Native.Time.Contains(p), p => r => r.Doc.Time.Contains(p));

        if (n.NInst is InstantRange.Finite inst && inst.End != Instant.MaxValue)
            await Check("NInst",
                        new[] { inst.Start, inst.End }.SelectMany(b => new[] { b.Minus(Duration.FromNanoseconds(1000)), b, b.Plus(Duration.FromNanoseconds(1000)) }),
                        p => n.NInst.Contains(p),
                        p => r => r.Native.NInst.Contains(p), p => r => r.Doc.NInst.Contains(p));

        foreach (var element in n.WallSet.Values.Where(e => e != DateTime.MinValue && e != DateTime.MaxValue))
            await Check("WallSet", [element], p => n.WallSet.Contains(p),
                        p => r => r.Native.WallSet.Contains(p), p => r => r.Doc.WallSet.Contains(p));

        // The stored array holds the store values, deduplicated — its cardinality is the set's Count.
        foreach (var (name, count, filter) in new (string, int, Expression<Func<TemporalParityRow, int>>)[]
                 {
                     ("WallSet", n.WallSet.Count, r => r.Native.WallSet.Count),
                     ("Audits", n.Audits.Count, r => r.Native.Audits.Count),
                     ("Shifts", n.Shifts.Count, r => r.Native.Shifts.Count),
                     ("Events", n.Events.Count, r => r.Native.Events.Count),
                     ("NWallMarks", n.NWallMarks.Count, r => r.Native.NWallMarks.Count),
                     ("NInstMarks", n.NInstMarks.Count, r => r.Native.NInstMarks.Count),
                     ("NOpenings", n.NOpenings.Count, r => r.Native.NOpenings.Count),
                 })
        {
            await using var ctx = new IntegrationDbContext();
            var onServer = await ctx.TemporalParityRows.Where(r => r.Id == id).Select(filter).SingleAsync();
            if (onServer != count) failures.Add($"[{probe}] {name}: cardinality {onServer} on the server, Count {count} in memory.");
        }

        return failures;
    }

    /// <summary>
    /// Every element the set was built from matches its own row, as a parameter and as a constant, in
    /// the column and in the document. Each one is stored — possibly as the same store value as a
    /// neighbour a few hundred nanoseconds away — so the expected answer is always a match; comparing
    /// the two paths only with each other would also pass if both missed. The NodaTime sets have no
    /// element normalization, so their probe used to fall back to Npgsql's own element literal — nine
    /// fraction digits, which the server rounds — and <c>InstantSet.Contains(EF.Constant(x))</c> missed
    /// the element <c>x</c> was stored as.
    /// </summary>
    private static async Task<IEnumerable<string>> StoredElementMisses(int id, string probe, TemporalParityValues v)
    {
        var failures = new List<string>();

        async Task Check<T>(string name, IEnumerable<T> elements,
                            Func<T, Expression<Func<TemporalParityRow, bool>>> asParameter,
                            Func<T, Expression<Func<TemporalParityRow, bool>>> asConstant)
        {
            foreach (var element in elements)
            {
                try
                {
                    var parameter = await Count(id, asParameter(element));
                    var constant  = await Count(id, asConstant(element));
                    if (parameter != 1 || constant != 1)
                        failures.Add($"[{probe}] {name}.Contains({Text(element)}) of a stored element: "
                                   + $"as a parameter {parameter == 1}, as a constant {constant == 1}.");
                }
                catch (Exception e)
                {
                    failures.Add($"[{probe}] {name}.Contains({Text(element)}): {e.GetType().Name}: {(e.InnerException ?? e).Message.Split('\n')[0]}");
                }
            }
        }

        await Check("WallSet", v.WallSet.Values, e => r => r.Native.WallSet.Contains(e), e => r => r.Native.WallSet.Contains(EF.Constant(e)));
        await Check("WallSet (document)", v.WallSet.Values, e => r => r.Doc.WallSet.Contains(e), e => r => r.Doc.WallSet.Contains(EF.Constant(e)));
        await Check("StzSet", v.StzSet.Values, e => r => r.Native.StzSet.Contains(e), e => r => r.Native.StzSet.Contains(EF.Constant(e)));
        await Check("StzSet (document)", v.StzSet.Values, e => r => r.Doc.StzSet.Contains(e), e => r => r.Doc.StzSet.Contains(EF.Constant(e)));
        await Check("TimeSet", v.TimeSet.Values, e => r => r.Native.TimeSet.Contains(e), e => r => r.Native.TimeSet.Contains(EF.Constant(e)));
        await Check("TimeSet (document)", v.TimeSet.Values, e => r => r.Doc.TimeSet.Contains(e), e => r => r.Doc.TimeSet.Contains(EF.Constant(e)));
        await Check("DaySet", v.DaySet.Values, e => r => r.Native.DaySet.Contains(e), e => r => r.Native.DaySet.Contains(EF.Constant(e)));
        await Check("DaySet (document)", v.DaySet.Values, e => r => r.Doc.DaySet.Contains(e), e => r => r.Doc.DaySet.Contains(EF.Constant(e)));
        await Check("NInstSet", v.NInstSet.Values, e => r => r.Native.NInstSet.Contains(e), e => r => r.Native.NInstSet.Contains(EF.Constant(e)));
        await Check("NInstSet (document)", v.NInstSet.Values, e => r => r.Doc.NInstSet.Contains(e), e => r => r.Doc.NInstSet.Contains(EF.Constant(e)));
        await Check("NWallSet", v.NWallSet.Values, e => r => r.Native.NWallSet.Contains(e), e => r => r.Native.NWallSet.Contains(EF.Constant(e)));
        await Check("NWallSet (document)", v.NWallSet.Values, e => r => r.Doc.NWallSet.Contains(e), e => r => r.Doc.NWallSet.Contains(EF.Constant(e)));
        await Check("NTimeSet", v.NTimeSet.Values, e => r => r.Native.NTimeSet.Contains(e), e => r => r.Native.NTimeSet.Contains(EF.Constant(e)));
        await Check("NTimeSet (document)", v.NTimeSet.Values, e => r => r.Doc.NTimeSet.Contains(e), e => r => r.Doc.NTimeSet.Contains(EF.Constant(e)));
        await Check("NDaySet", v.NDaySet.Values, e => r => r.Native.NDaySet.Contains(e), e => r => r.Native.NDaySet.Contains(EF.Constant(e)));
        await Check("NDaySet (document)", v.NDaySet.Values, e => r => r.Doc.NDaySet.Contains(e), e => r => r.Doc.NDaySet.Contains(EF.Constant(e)));
        await Check("Audits", v.Audits.Values, e => r => r.Native.Audits.Contains(e), e => r => r.Native.Audits.Contains(EF.Constant(e)));
        await Check("Audits (document)", v.Audits.Values, e => r => r.Doc.Audits.Contains(e), e => r => r.Doc.Audits.Contains(EF.Constant(e)));
        await Check("Shifts", v.Shifts.Values, e => r => r.Native.Shifts.Contains(e), e => r => r.Native.Shifts.Contains(EF.Constant(e)));
        await Check("Shifts (document)", v.Shifts.Values, e => r => r.Doc.Shifts.Contains(e), e => r => r.Doc.Shifts.Contains(EF.Constant(e)));
        await Check("Events", v.Events.Values, e => r => r.Native.Events.Contains(e), e => r => r.Native.Events.Contains(EF.Constant(e)));
        await Check("Events (document)", v.Events.Values, e => r => r.Doc.Events.Contains(e), e => r => r.Doc.Events.Contains(EF.Constant(e)));
        await Check("BizDays", v.BizDays.Values, e => r => r.Native.BizDays.Contains(e), e => r => r.Native.BizDays.Contains(EF.Constant(e)));
        await Check("BizDays (document)", v.BizDays.Values, e => r => r.Doc.BizDays.Contains(e), e => r => r.Doc.BizDays.Contains(EF.Constant(e)));
        await Check("NWallMarks", v.NWallMarks.Values, e => r => r.Native.NWallMarks.Contains(e), e => r => r.Native.NWallMarks.Contains(EF.Constant(e)));
        await Check("NWallMarks (document)", v.NWallMarks.Values, e => r => r.Doc.NWallMarks.Contains(e), e => r => r.Doc.NWallMarks.Contains(EF.Constant(e)));
        await Check("NInstMarks", v.NInstMarks.Values, e => r => r.Native.NInstMarks.Contains(e), e => r => r.Native.NInstMarks.Contains(EF.Constant(e)));
        await Check("NInstMarks (document)", v.NInstMarks.Values, e => r => r.Doc.NInstMarks.Contains(e), e => r => r.Doc.NInstMarks.Contains(EF.Constant(e)));
        await Check("NOpenings", v.NOpenings.Values, e => r => r.Native.NOpenings.Contains(e), e => r => r.Native.NOpenings.Contains(EF.Constant(e)));
        await Check("NOpenings (document)", v.NOpenings.Values, e => r => r.Doc.NOpenings.Contains(e), e => r => r.Doc.NOpenings.Contains(EF.Constant(e)));
        await Check("NCalDays", v.NCalDays.Values, e => r => r.Native.NCalDays.Contains(e), e => r => r.Native.NCalDays.Contains(EF.Constant(e)));
        await Check("NCalDays (document)", v.NCalDays.Values, e => r => r.Doc.NCalDays.Contains(e), e => r => r.Doc.NCalDays.Contains(EF.Constant(e)));

        return failures;
    }

    /// <summary>
    /// A range's element operand — <c>range @&gt; x</c> — probed with the original, sub-microsecond
    /// bounds as a parameter and as a constant. It used to take the provider's element mapping: a
    /// <c>DateTimeOffset</c> with a non-zero offset (every <c>Stz</c> probe here) threw as a
    /// parameter, and a constant was Npgsql's own literal — nine digits for NodaTime, which the server
    /// rounds.
    /// </summary>
    /// <remarks>
    /// The expected answer is the materialized range's <c>Contains</c> of the probe <em>as stored</em>
    /// (<see cref="Stored(DateTime)"/>), not the original range's: a range narrower than a microsecond
    /// is legitimately empty once stored. Both paths must give that answer — agreeing with each other
    /// is not enough, since both missing would agree. In the ordinary probes a start bound is inside
    /// and an exclusive end outside, so each of them asserts a match and a miss.
    /// </remarks>
    private static async Task<IEnumerable<string>> RangeElementDisagreements(int id, string probe, TemporalParityValues v,
                                                                              TemporalParityRow loaded)
    {
        var failures = new List<string>();

        async Task Check<T>(string name, IEnumerable<T> points, Func<T, bool> expected,
                            Func<T, Expression<Func<TemporalParityRow, bool>>> asParameter,
                            Func<T, Expression<Func<TemporalParityRow, bool>>> asConstant)
        {
            foreach (var point in points)
            {
                try
                {
                    var answer    = expected(point) ? 1 : 0;
                    var parameter = await Count(id, asParameter(point));
                    var constant  = await Count(id, asConstant(point));
                    if (parameter != answer || constant != answer)
                        failures.Add($"[{probe}] {name}.Contains({Text(point)}): the stored range holds it: {answer == 1}; "
                                   + $"as a parameter {parameter == 1}, as a constant {constant == 1}.");
                }
                catch (Exception e)
                {
                    failures.Add($"[{probe}] {name}.Contains({Text(point)}): {e.GetType().Name}: {(e.InnerException ?? e).Message.Split('\n')[0]}");
                }
            }
        }

        var n = loaded.Native;
        var d = loaded.Doc;

        if (v.Wall is DateTimeRange.Finite wall)
        {
            await Check("Wall", [wall.Start, wall.End], p => n.Wall.Contains(Stored(p)),
                        p => r => r.Native.Wall.Contains(p), p => r => r.Native.Wall.Contains(EF.Constant(p)));
            await Check("Wall (document)", [wall.Start, wall.End], p => d.Wall.Contains(Stored(p)),
                        p => r => r.Doc.Wall.Contains(p), p => r => r.Doc.Wall.Contains(EF.Constant(p)));
        }

        if (v.Stz is DateTimeOffsetRange.Finite stz)
        {
            await Check("Stz", [stz.Start, stz.End], p => n.Stz.Contains(Stored(p)),
                        p => r => r.Native.Stz.Contains(p), p => r => r.Native.Stz.Contains(EF.Constant(p)));
            await Check("Stz (document)", [stz.Start, stz.End], p => d.Stz.Contains(Stored(p)),
                        p => r => r.Doc.Stz.Contains(p), p => r => r.Doc.Stz.Contains(EF.Constant(p)));
        }

        if (v.Time is TimeRange.Finite time)
        {
            await Check("Time", [time.Start, time.End], p => n.Time.Contains(Stored(p)),
                        p => r => r.Native.Time.Contains(p), p => r => r.Native.Time.Contains(EF.Constant(p)));
            await Check("Time (document)", [time.Start, time.End], p => d.Time.Contains(Stored(p)),
                        p => r => r.Doc.Time.Contains(p), p => r => r.Doc.Time.Contains(EF.Constant(p)));
        }

        if (v.Day is DateRange.Finite day)
        {
            await Check("Day", [day.Start, day.End], p => n.Day.Contains(p),
                        p => r => r.Native.Day.Contains(p), p => r => r.Native.Day.Contains(EF.Constant(p)));
            await Check("Day (document)", [day.Start, day.End], p => d.Day.Contains(p),
                        p => r => r.Doc.Day.Contains(p), p => r => r.Doc.Day.Contains(EF.Constant(p)));
        }

        if (v.NWall is LocalDateTimeRange.Finite nwall)
        {
            await Check("NWall", [nwall.Start, nwall.End], p => n.NWall.Contains(Stored(p)),
                        p => r => r.Native.NWall.Contains(p), p => r => r.Native.NWall.Contains(EF.Constant(p)));
            await Check("NWall (document)", [nwall.Start, nwall.End], p => d.NWall.Contains(Stored(p)),
                        p => r => r.Doc.NWall.Contains(p), p => r => r.Doc.NWall.Contains(EF.Constant(p)));
        }

        if (v.NInst is InstantRange.Finite ninst)
        {
            await Check("NInst", [ninst.Start, ninst.End], p => n.NInst.Contains(Stored(p)),
                        p => r => r.Native.NInst.Contains(p), p => r => r.Native.NInst.Contains(EF.Constant(p)));
            await Check("NInst (document)", [ninst.Start, ninst.End], p => d.NInst.Contains(Stored(p)),
                        p => r => r.Doc.NInst.Contains(p), p => r => r.Doc.NInst.Contains(EF.Constant(p)));
        }

        if (v.NDay is LocalDateRange.Finite nday)
        {
            await Check("NDay", [nday.Start, nday.End], p => n.NDay.Contains(p),
                        p => r => r.Native.NDay.Contains(p), p => r => r.Native.NDay.Contains(EF.Constant(p)));
            await Check("NDay (document)", [nday.Start, nday.End], p => d.NDay.Contains(p),
                        p => r => r.Doc.NDay.Contains(p), p => r => r.Doc.NDay.Contains(EF.Constant(p)));
        }

        // A multirange's element operand, probed at the bounds of the ranges it was built from.
        var wallPoints = v.WallMulti.OfType<DateTimeRange.Finite>().SelectMany(f => new[] { f.Start, f.End }).ToList();
        await Check("WallMulti", wallPoints, p => n.WallMulti.Contains(Stored(p)),
                    p => r => r.Native.WallMulti.Contains(p), p => r => r.Native.WallMulti.Contains(EF.Constant(p)));
        await Check("WallMulti (document)", wallPoints, p => d.WallMulti.Contains(Stored(p)),
                    p => r => r.Doc.WallMulti.Contains(p), p => r => r.Doc.WallMulti.Contains(EF.Constant(p)));

        var instPoints = v.NInstMulti.OfType<InstantRange.Finite>().SelectMany(f => new[] { f.Start, f.End }).ToList();
        await Check("NInstMulti", instPoints, p => n.NInstMulti.Contains(Stored(p)),
                    p => r => r.Native.NInstMulti.Contains(p), p => r => r.Native.NInstMulti.Contains(EF.Constant(p)));
        await Check("NInstMulti (document)", instPoints, p => d.NInstMulti.Contains(Stored(p)),
                    p => r => r.Doc.NInstMulti.Contains(p), p => r => r.Doc.NInstMulti.Contains(EF.Constant(p)));

        return failures;
    }

    // -- A probe as the server compares it --
    //
    // Whole microseconds, cut toward 2000-01-01 as Npgsql binds a parameter, with the extremes
    // Npgsql sends as infinity left alone. Written out here rather than borrowed from the plugin's
    // StoreTemporal, so an error there cannot cancel out of the expected answer.

    private static readonly long PostgresEpochTicks = new DateTime(2000, 1, 1).Ticks;

    private static readonly Instant PostgresEpoch = Instant.FromUtc(2000, 1, 1, 0, 0);

    private static long StoredTicks(long ticks) => PostgresEpochTicks + (ticks - PostgresEpochTicks) / 10 * 10;

    private static DateTime Stored(DateTime point)
        => point == DateTime.MinValue || point == DateTime.MaxValue ? point : new DateTime(StoredTicks(point.Ticks), point.Kind);

    private static DateTimeOffset Stored(DateTimeOffset point)
        => point == DateTimeOffset.MinValue || point == DateTimeOffset.MaxValue
               ? point
               : new DateTimeOffset(StoredTicks(point.UtcTicks), TimeSpan.Zero);

    private static TimeOnly Stored(TimeOnly point) => new(point.Ticks / 10 * 10);

    private static Instant Stored(Instant point)
        => point == Instant.MinValue || point == Instant.MaxValue
               ? point
               : PostgresEpoch + Duration.FromNanoseconds((point - PostgresEpoch).ToBigIntegerNanoseconds() / 1000 * 1000);

    private static LocalDateTime Stored(LocalDateTime point)
        => point == LocalDateTime.MinIsoValue || point == LocalDateTime.MaxIsoValue
               ? point
               : Stored(point.InUtc().ToInstant()).InUtc().LocalDateTime;

    private static TemporalParityValues Clone(TemporalParityValues values, string name, object stored)
    {
        var clone = new TemporalParityValues();
        foreach (var property in typeof(TemporalParityValues).GetProperties())
            property.SetValue(clone, property.Name == name ? stored : property.GetValue(values));
        return clone;
    }

    // Every fraction digit: the general format drops them, and they are what these failures are about.
    private static string Text(object? value) => value switch
    {
        null             => "null",
        DateTime t       => t.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset t => t.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly t       => t.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f   => f.ToString(null, CultureInfo.InvariantCulture),
        _              => value.ToString() ?? "?"
    };
}

using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>Every temporal range, multirange and set type, once as a column and once in a JSON document.</summary>
public sealed class TemporalParityRow
{
    public int Id { get; set; }
    public TemporalParityValues Native { get; set; } = new();
    public TemporalParityValues Doc { get; set; } = new();
}

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
            Audits     = DateTimeSet<AuditStamp>.From(new AuditStamp(wall), new AuditStamp(wall.AddHours(5)))
        };
    }

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
            Audits     = DateTimeSet<AuditStamp>.From(new AuditStamp(DateTime.MinValue), new AuditStamp(DateTime.MaxValue))
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
            failures.AddRange(await ElementConstantDisagreements(id, probe, values));
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
                 })
        {
            await using var ctx = new IntegrationDbContext();
            var onServer = await ctx.TemporalParityRows.Where(r => r.Id == id).Select(filter).SingleAsync();
            if (onServer != count) failures.Add($"[{probe}] {name}: cardinality {onServer} on the server, Count {count} in memory.");
        }

        return failures;
    }

    /// <summary>
    /// A set element probed as a constant matches exactly as it does as a parameter. The NodaTime
    /// sets have no element normalization, so their probe used to fall back to Npgsql's own element
    /// literal — nine fraction digits, which the server rounds — and
    /// <c>InstantSet.Contains(EF.Constant(x))</c> missed the element <c>x</c> was stored as.
    /// </summary>
    private static async Task<IEnumerable<string>> ElementConstantDisagreements(int id, string probe, TemporalParityValues v)
    {
        var failures = new List<string>();

        async Task Check<T>(string name, IEnumerable<T> elements,
                            Func<T, Expression<Func<TemporalParityRow, bool>>> asParameter,
                            Func<T, Expression<Func<TemporalParityRow, bool>>> asConstant)
        {
            foreach (var element in elements)
            {
                var parameter = await Count(id, asParameter(element));
                var constant  = await Count(id, asConstant(element));
                if (parameter != constant)
                    failures.Add($"[{probe}] {name}.Contains({Text(element)}): as a parameter {parameter == 1}, as a constant {constant == 1}.");
            }
        }

        await Check("NInstSet", v.NInstSet.Values, e => r => r.Native.NInstSet.Contains(e), e => r => r.Native.NInstSet.Contains(EF.Constant(e)));
        await Check("NInstSet (document)", v.NInstSet.Values, e => r => r.Doc.NInstSet.Contains(e), e => r => r.Doc.NInstSet.Contains(EF.Constant(e)));
        await Check("NTimeSet", v.NTimeSet.Values, e => r => r.Native.NTimeSet.Contains(e), e => r => r.Native.NTimeSet.Contains(EF.Constant(e)));
        await Check("NWallSet", v.NWallSet.Values, e => r => r.Native.NWallSet.Contains(e), e => r => r.Native.NWallSet.Contains(EF.Constant(e)));
        await Check("TimeSet", v.TimeSet.Values, e => r => r.Native.TimeSet.Contains(e), e => r => r.Native.TimeSet.Contains(EF.Constant(e)));
        await Check("WallSet", v.WallSet.Values, e => r => r.Native.WallSet.Contains(e), e => r => r.Native.WallSet.Contains(EF.Constant(e)));
        await Check("StzSet", v.StzSet.Values, e => r => r.Native.StzSet.Contains(e), e => r => r.Native.StzSet.Contains(EF.Constant(e)));

        return failures;
    }

    private static TemporalParityValues Clone(TemporalParityValues values, string name, object stored)
    {
        var clone = new TemporalParityValues();
        foreach (var property in typeof(TemporalParityValues).GetProperties())
            property.SetValue(clone, property.Name == name ? stored : property.GetValue(values));
        return clone;
    }

    private static string Text(object? value) => value switch
    {
        null           => "null",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _              => value.ToString() ?? "?"
    };
}

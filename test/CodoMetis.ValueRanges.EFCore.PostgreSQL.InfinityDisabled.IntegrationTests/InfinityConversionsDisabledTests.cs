using System.Globalization;
using System.Linq.Expressions;
using CodoMetis.ValueRanges.Conventions.Tests;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.InfinityDisabled.IntegrationTests;

/// <summary>
/// The temporal store rule with <c>Npgsql.DisableDateTimeInfinityConversions</c> set: Npgsql then
/// sends <c>MinValue</c> / <c>MaxValue</c> as the finite values they are, truncated to the microsecond
/// like any other, and the plugin's text — a JSON document, a SQL literal — must do the same rather
/// than write <c>infinity</c>. <c>TemporalStoreParityTests</c> holds the rule with the switch off.
/// </summary>
/// <remarks>
/// Two things this configuration cannot do, whatever the path, and so are absent: store the NodaTime
/// minimums (<c>-9998-01-01</c> is before PostgreSQL's earliest timestamp and date), and read back a
/// date range whose inclusive end is the maximum — the server keeps <c>[…,10000-01-01)</c>, a date
/// neither <see cref="DateOnly"/> nor <see cref="LocalDate"/> can hold. The date ranges here end
/// exclusively at the maximum.
/// </remarks>
[TestClass]
public sealed class InfinityConversionsDisabledTests
{
    private static readonly DateTime      Wall    = new(2026, 3, 15, 10, 20, 30);
    private static readonly Instant       Noda    = Instant.FromUtc(2026, 3, 15, 10, 20, 30);
    private static readonly LocalDateTime NWallAt = LocalDateTime.FromDateTime(Wall);
    private static readonly LocalDate     NDayAt  = new(2026, 3, 15);

    private static ExtremesValues Build() => new()
    {
        Wall      = DateTimeRange.CreateFinite(DateTime.MinValue, DateTime.MaxValue, endInclusive: true),
        Stz       = DateTimeOffsetRange.CreateFinite(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, endInclusive: true),
        Day       = DateRange.CreateFinite(DateOnly.MinValue, DateOnly.MaxValue, endInclusive: false),
        NWall     = LocalDateTimeRange.CreateFinite(NWallAt, LocalDateTime.MaxIsoValue, endInclusive: true),
        NInst     = InstantRange.CreateFinite(Noda, Instant.MaxValue, endInclusive: true),
        NDay      = LocalDateRange.CreateFinite(NDayAt, LocalDate.MaxIsoValue, endInclusive: false),
        WallMulti = RangeSet<DateTimeRange, DateTime>.From(DateTimeRange.CreateFinite(DateTime.MinValue, Wall)),
        WallSet   = DateTimeSet.From(DateTime.MinValue, Wall, DateTime.MaxValue),
        StzSet    = DateTimeOffsetSet.From(DateTimeOffset.MinValue, DateTimeOffset.MaxValue),
        DaySet    = DateSet.From(DateOnly.MinValue, DateOnly.MaxValue),
        NInstSet  = InstantSet.From(Noda, Instant.MaxValue),
        NWallSet  = LocalDateTimeSet.From(NWallAt, LocalDateTime.MaxIsoValue),
        NDaySet   = LocalDateSet.From(NDayAt, LocalDate.MaxIsoValue),
        Audits    = DateTimeSet<AuditStamp>.From(Stamp(DateTime.MinValue), Stamp(DateTime.MaxValue))
    };

    private static AuditStamp Stamp(DateTime value)
        => AuditStamp.Parse(value.ToString("O", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Per property: name, store type, both copies' equality filter as a parameter and as a constant.</summary>
    private static IEnumerable<(string Name, string StoreType, Func<ExtremesValues, object> Get,
                                Expression<Func<ExtremesRow, bool>>[] Filters)> Properties(ExtremesValues v)
    {
        yield return ("Wall", "tsrange", x => x.Wall,
                      [r => r.Native.Wall == v.Wall, r => r.Doc.Wall == v.Wall, r => r.Native.Wall == EF.Constant(v.Wall), r => r.Doc.Wall == EF.Constant(v.Wall)]);
        yield return ("Stz", "tstzrange", x => x.Stz,
                      [r => r.Native.Stz == v.Stz, r => r.Doc.Stz == v.Stz, r => r.Native.Stz == EF.Constant(v.Stz), r => r.Doc.Stz == EF.Constant(v.Stz)]);
        yield return ("Day", "daterange", x => x.Day,
                      [r => r.Native.Day == v.Day, r => r.Doc.Day == v.Day, r => r.Native.Day == EF.Constant(v.Day), r => r.Doc.Day == EF.Constant(v.Day)]);
        yield return ("NWall", "tsrange", x => x.NWall,
                      [r => r.Native.NWall == v.NWall, r => r.Doc.NWall == v.NWall, r => r.Native.NWall == EF.Constant(v.NWall), r => r.Doc.NWall == EF.Constant(v.NWall)]);
        yield return ("NInst", "tstzrange", x => x.NInst,
                      [r => r.Native.NInst == v.NInst, r => r.Doc.NInst == v.NInst, r => r.Native.NInst == EF.Constant(v.NInst), r => r.Doc.NInst == EF.Constant(v.NInst)]);
        yield return ("NDay", "daterange", x => x.NDay,
                      [r => r.Native.NDay == v.NDay, r => r.Doc.NDay == v.NDay, r => r.Native.NDay == EF.Constant(v.NDay), r => r.Doc.NDay == EF.Constant(v.NDay)]);
        yield return ("WallMulti", "tsmultirange", x => x.WallMulti,
                      [r => r.Native.WallMulti == v.WallMulti, r => r.Doc.WallMulti == v.WallMulti, r => r.Native.WallMulti == EF.Constant(v.WallMulti), r => r.Doc.WallMulti == EF.Constant(v.WallMulti)]);
        yield return ("WallSet", "timestamp[]", x => x.WallSet,
                      [r => r.Native.WallSet == v.WallSet, r => r.Doc.WallSet == v.WallSet, r => r.Native.WallSet == EF.Constant(v.WallSet), r => r.Doc.WallSet == EF.Constant(v.WallSet)]);
        yield return ("StzSet", "timestamptz[]", x => x.StzSet,
                      [r => r.Native.StzSet == v.StzSet, r => r.Doc.StzSet == v.StzSet, r => r.Native.StzSet == EF.Constant(v.StzSet), r => r.Doc.StzSet == EF.Constant(v.StzSet)]);
        yield return ("DaySet", "date[]", x => x.DaySet,
                      [r => r.Native.DaySet == v.DaySet, r => r.Doc.DaySet == v.DaySet, r => r.Native.DaySet == EF.Constant(v.DaySet), r => r.Doc.DaySet == EF.Constant(v.DaySet)]);
        yield return ("NInstSet", "timestamptz[]", x => x.NInstSet,
                      [r => r.Native.NInstSet == v.NInstSet, r => r.Doc.NInstSet == v.NInstSet, r => r.Native.NInstSet == EF.Constant(v.NInstSet), r => r.Doc.NInstSet == EF.Constant(v.NInstSet)]);
        yield return ("NWallSet", "timestamp[]", x => x.NWallSet,
                      [r => r.Native.NWallSet == v.NWallSet, r => r.Doc.NWallSet == v.NWallSet, r => r.Native.NWallSet == EF.Constant(v.NWallSet), r => r.Doc.NWallSet == EF.Constant(v.NWallSet)]);
        yield return ("NDaySet", "date[]", x => x.NDaySet,
                      [r => r.Native.NDaySet == v.NDaySet, r => r.Doc.NDaySet == v.NDaySet, r => r.Native.NDaySet == EF.Constant(v.NDaySet), r => r.Doc.NDaySet == EF.Constant(v.NDaySet)]);
        yield return ("Audits", "timestamp[]", x => x.Audits,
                      [r => r.Native.Audits == v.Audits, r => r.Doc.Audits == v.Audits, r => r.Native.Audits == EF.Constant(v.Audits), r => r.Doc.Audits == EF.Constant(v.Audits)]);
    }

    private static readonly string[] Paths =
        ["column == parameter", "document == parameter", "column == constant", "document == constant"];

    /// <summary>
    /// Without this the rest proves nothing: if the runtimeconfig option stopped reaching the process,
    /// Npgsql would convert as usual and every assertion below would pass for the other configuration.
    /// </summary>
    [TestMethod]
    public async Task TheSwitch_IsInEffect_ForNpgsqlInThisProcess()
    {
        ContainerLifecycle.RequireDatabase();

        Assert.IsTrue(AppContext.TryGetSwitch("Npgsql.DisableDateTimeInfinityConversions", out var disabled) && disabled,
                      "The switch is not set in this process; see the csproj.");

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT @p::text", connection);
        command.Parameters.AddWithValue("p", DateTime.MaxValue);

        Assert.AreEqual("9999-12-31 23:59:59.999999", await command.ExecuteScalarAsync());
    }

    /// <summary>The document holds the maximum as Npgsql sends it — truncated, finite.</summary>
    [TestMethod]
    public async Task TheDocument_HoldsTheExtremesFinite()
    {
        ContainerLifecycle.RequireDatabase();
        await Insert(2);

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "Doc" ->> 'Wall', "Doc" ->> 'DaySet', "Doc" ->> 'NInstSet' FROM "Rows" WHERE "Id" = 2""", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.AreEqual("[0001-01-01T00:00:00.0000000,9999-12-31T23:59:59.9999990]", reader.GetString(0));
        Assert.AreEqual("{0001-01-01,9999-12-31}", reader.GetString(1));
        Assert.AreEqual("{2026-03-15T10:20:30Z,9999-12-31T23:59:59.999999Z}", reader.GetString(2));
    }

    [TestMethod]
    public async Task TheExtremes_AreTheSameValue_AsColumn_Document_Parameter_AndConstant()
    {
        ContainerLifecycle.RequireDatabase();

        var values   = Build();
        var loaded   = await Insert(1);
        var failures = new List<string>();

        foreach (var (name, storeType, get, filters) in Properties(values))
        {
            try
            {
                if (await DocumentText(1, name) is { } text && text.Contains("infinity", StringComparison.Ordinal))
                    failures.Add($"{name}: the document holds {text}.");

                if (!await ServerReadsTheDocumentAsTheColumn(1, name, storeType))
                    failures.Add($"{name}: the server reads the document as a different value from the column.");

                if (!Equals(get(loaded.Doc), get(loaded.Native)))
                    failures.Add($"{name}: the document materialized {get(loaded.Doc)}, the column {get(loaded.Native)}.");

                foreach (var (filter, path) in filters.Zip(Paths))
                    if (await Count(1, filter) is var count and not 1)
                        failures.Add($"{name}: {path} matched {count} rows.");
            }
            catch (Exception e)
            {
                failures.Add($"{name}: {e.GetType().Name}: {(e.InnerException ?? e).Message.Split('\n')[0]}");
            }
        }

        failures.AddRange(await ElementMisses(values));

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Each extreme as an element operand, as a parameter and as a constant, against an expected answer:
    /// every set element matches its own row, and a range answers by its bounds — an exclusive maximum
    /// stays outside.
    /// </summary>
    private static async Task<IEnumerable<string>> ElementMisses(ExtremesValues v)
    {
        var failures = new List<string>();

        async Task Check<T>(string name, T point, bool expected,
                            Func<T, Expression<Func<ExtremesRow, bool>>> asParameter,
                            Func<T, Expression<Func<ExtremesRow, bool>>> asConstant)
        {
            try
            {
                var answer    = expected ? 1 : 0;
                var parameter = await Count(1, asParameter(point));
                var constant  = await Count(1, asConstant(point));
                if (parameter != answer || constant != answer)
                    failures.Add($"{name}.Contains({point}): expected {expected}; as a parameter {parameter == 1}, as a constant {constant == 1}.");
            }
            catch (Exception e)
            {
                failures.Add($"{name}.Contains({point}): {e.GetType().Name}: {(e.InnerException ?? e).Message.Split('\n')[0]}");
            }
        }

        foreach (var e in v.WallSet.Values)
        {
            await Check("WallSet", e, true, p => r => r.Native.WallSet.Contains(p), p => r => r.Native.WallSet.Contains(EF.Constant(p)));
            await Check("WallSet (document)", e, true, p => r => r.Doc.WallSet.Contains(p), p => r => r.Doc.WallSet.Contains(EF.Constant(p)));
        }

        foreach (var e in v.StzSet.Values)
        {
            await Check("StzSet", e, true, p => r => r.Native.StzSet.Contains(p), p => r => r.Native.StzSet.Contains(EF.Constant(p)));
            await Check("StzSet (document)", e, true, p => r => r.Doc.StzSet.Contains(p), p => r => r.Doc.StzSet.Contains(EF.Constant(p)));
        }

        foreach (var e in v.DaySet.Values)
        {
            await Check("DaySet", e, true, p => r => r.Native.DaySet.Contains(p), p => r => r.Native.DaySet.Contains(EF.Constant(p)));
            await Check("DaySet (document)", e, true, p => r => r.Doc.DaySet.Contains(p), p => r => r.Doc.DaySet.Contains(EF.Constant(p)));
        }

        foreach (var e in v.NInstSet.Values)
        {
            await Check("NInstSet", e, true, p => r => r.Native.NInstSet.Contains(p), p => r => r.Native.NInstSet.Contains(EF.Constant(p)));
            await Check("NInstSet (document)", e, true, p => r => r.Doc.NInstSet.Contains(p), p => r => r.Doc.NInstSet.Contains(EF.Constant(p)));
        }

        foreach (var e in v.NWallSet.Values)
        {
            await Check("NWallSet", e, true, p => r => r.Native.NWallSet.Contains(p), p => r => r.Native.NWallSet.Contains(EF.Constant(p)));
            await Check("NWallSet (document)", e, true, p => r => r.Doc.NWallSet.Contains(p), p => r => r.Doc.NWallSet.Contains(EF.Constant(p)));
        }

        foreach (var e in v.NDaySet.Values)
        {
            await Check("NDaySet", e, true, p => r => r.Native.NDaySet.Contains(p), p => r => r.Native.NDaySet.Contains(EF.Constant(p)));
            await Check("NDaySet (document)", e, true, p => r => r.Doc.NDaySet.Contains(p), p => r => r.Doc.NDaySet.Contains(EF.Constant(p)));
        }

        foreach (var e in v.Audits.Values)
        {
            await Check("Audits", e, true, p => r => r.Native.Audits.Contains(p), p => r => r.Native.Audits.Contains(EF.Constant(p)));
            await Check("Audits (document)", e, true, p => r => r.Doc.Audits.Contains(p), p => r => r.Doc.Audits.Contains(EF.Constant(p)));
        }

        // Ranges: both ends of an inclusive [MinValue, MaxValue] are inside; DateRange's exclusive maximum is not.
        foreach (var (point, expected) in new[] { (DateTime.MinValue, true), (DateTime.MaxValue, true) })
        {
            await Check("Wall", point, expected, p => r => r.Native.Wall.Contains(p), p => r => r.Native.Wall.Contains(EF.Constant(p)));
            await Check("Wall (document)", point, expected, p => r => r.Doc.Wall.Contains(p), p => r => r.Doc.Wall.Contains(EF.Constant(p)));
        }

        foreach (var (point, expected) in new[] { (DateTimeOffset.MinValue, true), (DateTimeOffset.MaxValue, true) })
        {
            await Check("Stz", point, expected, p => r => r.Native.Stz.Contains(p), p => r => r.Native.Stz.Contains(EF.Constant(p)));
            await Check("Stz (document)", point, expected, p => r => r.Doc.Stz.Contains(p), p => r => r.Doc.Stz.Contains(EF.Constant(p)));
        }

        foreach (var (point, expected) in new[] { (DateOnly.MinValue, true), (DateOnly.MaxValue, false) })
        {
            await Check("Day", point, expected, p => r => r.Native.Day.Contains(p), p => r => r.Native.Day.Contains(EF.Constant(p)));
            await Check("Day (document)", point, expected, p => r => r.Doc.Day.Contains(p), p => r => r.Doc.Day.Contains(EF.Constant(p)));
        }

        await Check("NWall", LocalDateTime.MaxIsoValue, true, p => r => r.Native.NWall.Contains(p), p => r => r.Native.NWall.Contains(EF.Constant(p)));
        await Check("NWall (document)", LocalDateTime.MaxIsoValue, true, p => r => r.Doc.NWall.Contains(p), p => r => r.Doc.NWall.Contains(EF.Constant(p)));
        await Check("NInst", Instant.MaxValue, true, p => r => r.Native.NInst.Contains(p), p => r => r.Native.NInst.Contains(EF.Constant(p)));
        await Check("NInst (document)", Instant.MaxValue, true, p => r => r.Doc.NInst.Contains(p), p => r => r.Doc.NInst.Contains(EF.Constant(p)));
        await Check("NDay", LocalDate.MaxIsoValue, false, p => r => r.Native.NDay.Contains(p), p => r => r.Native.NDay.Contains(EF.Constant(p)));
        await Check("NDay (document)", LocalDate.MaxIsoValue, false, p => r => r.Doc.NDay.Contains(p), p => r => r.Doc.NDay.Contains(EF.Constant(p)));
        await Check("WallMulti", DateTime.MinValue, true, p => r => r.Native.WallMulti.Contains(p), p => r => r.Native.WallMulti.Contains(EF.Constant(p)));
        await Check("WallMulti (document)", DateTime.MinValue, true, p => r => r.Doc.WallMulti.Contains(p), p => r => r.Doc.WallMulti.Contains(EF.Constant(p)));

        return failures;
    }

    /// <summary>
    /// With the switch set, <see cref="DateOnly.MinValue"/> and <see cref="DateOnly.MaxValue"/> reach the
    /// server as the dates they are, so a range bounded at an extreme is adjacent to its neighbour there
    /// as it is in memory. With the switch off the same pair diverges
    /// (<c>DomainBoundaryDivergenceTests</c>): the extreme is <c>-infinity</c> / <c>infinity</c> on the
    /// server, which no finite date is next to. This is what keeps the in-memory answer as it is — a
    /// range that stopped being adjacent at the extreme would disagree here instead.
    /// </summary>
    [TestMethod]
    public async Task AtTheDateExtremes_AdjacencyAgrees_BecauseTheExtremesStayDates()
    {
        ContainerLifecycle.RequireDatabase();

        var first      = DateRange.CreateFinite(DateOnly.MinValue, DateOnly.MinValue);
        var rest       = DateRange.CreateUnboundedEnd(new DateOnly(1, 1, 2));
        var beforeLast = DateRange.CreateFinite(new DateOnly(9999, 12, 30), new DateOnly(9999, 12, 30));
        var last       = DateRange.CreateUnboundedEnd(DateOnly.MaxValue);

        Assert.IsTrue(first.IsAdjacentTo(rest));
        Assert.IsTrue(beforeLast.IsAdjacentTo(last));

        await using (var write = new InfinityDisabledDbContext())
        {
            write.Rows.AddRange(
                new ExtremesRow { Id = 31, Native = new ExtremesValues { Day = rest },       Doc = new ExtremesValues() },
                new ExtremesRow { Id = 32, Native = new ExtremesValues { Day = beforeLast }, Doc = new ExtremesValues() });
            await write.SaveChangesAsync();
        }

        Assert.AreEqual(1, await Count(31, r => r.Native.Day.IsAdjacentTo(first)), "[0001-01-02,) -|- [0001-01-01,0001-01-01] as a parameter");
        Assert.AreEqual(1, await Count(31, r => r.Native.Day.IsAdjacentTo(EF.Constant(first))), "… and as a constant");
        Assert.AreEqual(1, await Count(32, r => r.Native.Day.IsAdjacentTo(last)), "[9999-12-30,9999-12-30] -|- [9999-12-31,) as a parameter");
        Assert.AreEqual(1, await Count(32, r => r.Native.Day.IsAdjacentTo(EF.Constant(last))), "… and as a constant");

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT @first -|- @rest, @beforeLast -|- @last", connection);
        command.Parameters.AddWithValue("first", new NpgsqlTypes.NpgsqlRange<DateOnly>(DateOnly.MinValue, true, DateOnly.MinValue, true));
        command.Parameters.AddWithValue("rest", new NpgsqlTypes.NpgsqlRange<DateOnly>(new DateOnly(1, 1, 2), true, false, default, false, true));
        command.Parameters.AddWithValue("beforeLast", new NpgsqlTypes.NpgsqlRange<DateOnly>(new DateOnly(9999, 12, 30), true, new DateOnly(9999, 12, 30), true));
        command.Parameters.AddWithValue("last", new NpgsqlTypes.NpgsqlRange<DateOnly>(DateOnly.MaxValue, true, false, default, false, true));
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.IsTrue(reader.GetBoolean(0), "Npgsql sends 0001-01-01 as a date, so the server finds 0001-01-02 next to it");
        Assert.IsTrue(reader.GetBoolean(1), "Npgsql sends 9999-12-31 as a date, so the server finds 9999-12-30 next to it");
    }

    private static async Task<ExtremesRow> Insert(int id)
    {
        await using (var write = new InfinityDisabledDbContext())
        {
            var values = Build();
            write.Rows.Add(new ExtremesRow { Id = id, Native = values, Doc = values });
            await write.SaveChangesAsync();
        }

        await using var read = new InfinityDisabledDbContext();
        return await read.Rows.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private static async Task<int> Count(int id, Expression<Func<ExtremesRow, bool>> filter)
    {
        await using var ctx = new InfinityDisabledDbContext();
        return await ctx.Rows.Where(r => r.Id == id).Where(filter).CountAsync();
    }

    private static async Task<string?> DocumentText(int id, string property)
    {
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""SELECT "Doc" ->> '{property}' FROM "Rows" WHERE "Id" = {id}""", connection);
        return (string?)await command.ExecuteScalarAsync();
    }

    /// <summary>Columns are flattened by the complex property: <c>Native_Wall</c> and so on.</summary>
    private static async Task<bool> ServerReadsTheDocumentAsTheColumn(int id, string property, string storeType)
    {
        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""SELECT CAST("Doc" ->> '{property}' AS {storeType}) = "Native_{property}" FROM "Rows" WHERE "Id" = {id}""",
            connection);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}

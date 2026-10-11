using System.Linq.Expressions;
using CodoMetis.ValueRanges.Conventions.Tests;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

public sealed class JsonDocumentRow
{
    public int Id { get; set; }
    public JsonDocument Doc { get; set; } = new();
}

/// <summary>One property per range type, a multirange per family shape, and a set per family.</summary>
public sealed class JsonDocument
{
    public Int32Range          Seats       { get; set; } = Int32Range.Empty;
    public Int64Range          Tickets     { get; set; } = Int64Range.Empty;
    public DecimalRange        Price       { get; set; } = DecimalRange.Empty;
    public DateRange           Period      { get; set; } = DateRange.Empty;
    public DateTimeRange       WallClock   { get; set; } = DateTimeRange.Empty;
    public DateTimeOffsetRange Instants    { get; set; } = DateTimeOffsetRange.Empty;
    public TimeRange           Opening     { get; set; } = TimeRange.Empty;
    public LocalDateRange      NodaPeriod  { get; set; } = LocalDateRange.Empty;
    public LocalDateTimeRange  NodaWall    { get; set; } = LocalDateTimeRange.Empty;
    public InstantRange        NodaWindow  { get; set; } = InstantRange.Empty;
    public YearMonthRange      Billing     { get; set; } = YearMonthRange.Empty;
    public Int32Range?         MaybeSeats  { get; set; }
    public DateTimeOffsetRange? MaybeWindow { get; set; }
    public StringSet?          MaybeTags   { get; set; }

    public RangeSet<DateRange, DateOnly>        Blocks        { get; set; } = RangeSet<DateRange, DateOnly>.Empty;
    public RangeSet<DecimalRange, decimal>      Bands         { get; set; } = RangeSet<DecimalRange, decimal>.Empty;
    public RangeSet<YearMonthRange, YearMonth>  BillingBlocks { get; set; } = RangeSet<YearMonthRange, YearMonth>.Empty;

    public StringSet          Tags      { get; set; } = StringSet.Empty;
    public GuidSet            Ids       { get; set; } = GuidSet.Empty;
    public Int32Set           Codes     { get; set; } = Int32Set.Empty;
    public DecimalSet         Amounts   { get; set; } = DecimalSet.Empty;
    public DateSet            Days      { get; set; } = DateSet.Empty;
    public DateTimeOffsetSet  Stamps    { get; set; } = DateTimeOffsetSet.Empty;
    public LocalDateSet       NodaDays  { get; set; } = LocalDateSet.Empty;
    public InstantSet         NodaStamps { get; set; } = InstantSet.Empty;
    public YearMonthSet       Months    { get; set; } = YearMonthSet.Empty;
    public StringSet<TextKey> Keys      { get; set; } = StringSet<TextKey>.Empty;
    public YearMonthSet<BillingMonth> WrappedMonths { get; set; } = YearMonthSet<BillingMonth>.Empty;
}

public sealed class OwnedJsonRow
{
    public int Id { get; set; }
    public OwnedJsonDocument Doc { get; set; } = new();
}

/// <summary>
/// A JSON document mapped as an owned entity rather than a complex type: EF then writes a changed
/// property on its own, with <c>jsonb_set</c>, instead of rewriting the document.
/// </summary>
public sealed class OwnedJsonDocument
{
    public Int32Range           Seats       { get; set; } = Int32Range.Empty;
    public DateTimeRange        WallClock   { get; set; } = DateTimeRange.Empty;
    public StringSet            Tags        { get; set; } = StringSet.Empty;
    public InstantSet           NodaStamps  { get; set; } = InstantSet.Empty;
    public Int32Range?          MaybeSeats  { get; set; }
    public DateTimeOffsetRange? MaybeWindow { get; set; }
    public StringSet?           MaybeTags   { get; set; }
}

/// <summary>
/// Ranges, multiranges and value sets inside an EF Core JSON column (<c>ComplexProperty(…).ToJson()</c>).
/// Before 9.0.0 the plugin's type mappings had no <c>JsonValueReaderWriter</c>, and <c>SaveChanges</c>
/// failed with a <see cref="NullReferenceException"/> from inside EF for any of them. Each is now
/// written as the literal of its store type, which is also what makes a query into the document work:
/// EF extracts the value as text and casts it — <c>CAST(doc ->> 'Period' AS daterange)</c>.
/// </summary>
[TestClass]
public sealed class JsonColumnTests
{
    private static readonly JsonDocument Sample = new()
    {
        Seats      = Int32Range.CreateFinite(-5, 10),
        Tickets    = Int64Range.CreateUnboundedEnd(5_000_000_000L),
        Price      = DecimalRange.CreateFinite(1.5m, 9.25m),
        Period     = DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)),
        WallClock  = DateTimeRange.CreateFinite(new DateTime(2026, 1, 1, 9, 30, 0), new DateTime(2026, 1, 1, 17, 0, 0)),
        Instants   = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero),
                                                      new DateTimeOffset(2026, 1, 1, 17, 0, 0, TimeSpan.Zero)),
        Opening    = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 30)),
        NodaPeriod = LocalDateRange.CreateFinite(new LocalDate(2026, 1, 1), new LocalDate(2026, 6, 30)),
        NodaWall   = LocalDateTimeRange.CreateFinite(new LocalDateTime(2026, 1, 1, 9, 30), new LocalDateTime(2026, 1, 1, 17, 0)),
        NodaWindow = InstantRange.CreateFinite(Instant.FromUtc(2026, 1, 1, 9, 30), Instant.FromUtc(2026, 1, 1, 17, 0)),
        Billing    = YearMonthRange.CreateFinite(new YearMonth(2026, 1), new YearMonth(2026, 6)),
        MaybeSeats = Int32Range.Infinite,

        Blocks        = RangeSet<DateRange, DateOnly>.From([DateRange.CreateFinite(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
                                                            DateRange.CreateUnboundedEnd(new DateOnly(2026, 9, 1))]),
        Bands         = RangeSet<DecimalRange, decimal>.From([DecimalRange.CreateFinite(0.5m, 1.25m)]),
        BillingBlocks = RangeSet<YearMonthRange, YearMonth>.From([YearMonthRange.CreateFinite(new YearMonth(2026, 3), new YearMonth(2026, 4))]),

        Tags       = StringSet.From("beta", "Alpha", "with,comma", "quote\"d"),
        Ids        = GuidSet.From(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")),
        Codes      = Int32Set.From(-3, 7),
        Amounts    = DecimalSet.From(1.50m, 2.25m),
        Days       = DateSet.From(new DateOnly(2026, 1, 1)),
        Stamps     = DateTimeOffsetSet.From(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.FromHours(1))),
        NodaDays   = LocalDateSet.From(new LocalDate(2026, 1, 1)),
        NodaStamps = InstantSet.From(Instant.FromUtc(2026, 1, 1, 9, 30)),
        Months     = YearMonthSet.From(new YearMonth(2026, 1), new YearMonth(2026, 6)),
        Keys       = StringSet<TextKey>.From(TextKey.Parse("users.read", null)),
        WrappedMonths = YearMonthSet<BillingMonth>.From(new BillingMonth(new YearMonth(2026, 2)), new BillingMonth(new YearMonth(2026, 5)))
    };

    /// <summary>Every property: (name, value from the sample, value from a row, server-side equality filter).</summary>
    private static IEnumerable<(string Name, Func<JsonDocument, object?> Get, Expression<Func<JsonDocumentRow, bool>> Equal)> Properties()
    {
        var s = Sample;
        yield return ("Seats",         d => d.Seats,         r => r.Doc.Seats == s.Seats);
        yield return ("Tickets",       d => d.Tickets,       r => r.Doc.Tickets == s.Tickets);
        yield return ("Price",         d => d.Price,         r => r.Doc.Price == s.Price);
        yield return ("Period",        d => d.Period,        r => r.Doc.Period == s.Period);
        yield return ("WallClock",     d => d.WallClock,     r => r.Doc.WallClock == s.WallClock);
        yield return ("Instants",      d => d.Instants,      r => r.Doc.Instants == s.Instants);
        yield return ("Opening",       d => d.Opening,       r => r.Doc.Opening == s.Opening);
        yield return ("NodaPeriod",    d => d.NodaPeriod,    r => r.Doc.NodaPeriod == s.NodaPeriod);
        yield return ("NodaWall",      d => d.NodaWall,      r => r.Doc.NodaWall == s.NodaWall);
        yield return ("NodaWindow",    d => d.NodaWindow,    r => r.Doc.NodaWindow == s.NodaWindow);
        yield return ("Billing",       d => d.Billing,       r => r.Doc.Billing == s.Billing);
        yield return ("MaybeSeats",    d => d.MaybeSeats,    r => r.Doc.MaybeSeats == s.MaybeSeats);
        yield return ("Blocks",        d => d.Blocks,        r => r.Doc.Blocks == s.Blocks);
        yield return ("Bands",         d => d.Bands,         r => r.Doc.Bands == s.Bands);
        yield return ("BillingBlocks", d => d.BillingBlocks, r => r.Doc.BillingBlocks == s.BillingBlocks);
        yield return ("Tags",          d => d.Tags,          r => r.Doc.Tags == s.Tags);
        yield return ("Ids",           d => d.Ids,           r => r.Doc.Ids == s.Ids);
        yield return ("Codes",         d => d.Codes,         r => r.Doc.Codes == s.Codes);
        yield return ("Amounts",       d => d.Amounts,       r => r.Doc.Amounts == s.Amounts);
        yield return ("Days",          d => d.Days,          r => r.Doc.Days == s.Days);
        yield return ("Stamps",        d => d.Stamps,        r => r.Doc.Stamps == s.Stamps);
        yield return ("NodaDays",      d => d.NodaDays,      r => r.Doc.NodaDays == s.NodaDays);
        yield return ("NodaStamps",    d => d.NodaStamps,    r => r.Doc.NodaStamps == s.NodaStamps);
        yield return ("Months",        d => d.Months,        r => r.Doc.Months == s.Months);
        yield return ("Keys",          d => d.Keys,          r => r.Doc.Keys == s.Keys);
        yield return ("WrappedMonths", d => d.WrappedMonths, r => r.Doc.WrappedMonths == s.WrappedMonths);
    }

    private static async Task<JsonDocumentRow> Insert(int id)
    {
        await using (var write = new IntegrationDbContext())
        {
            write.JsonDocuments.Add(new JsonDocumentRow { Id = id, Doc = Sample });
            await write.SaveChangesAsync();
        }

        await using var read = new IntegrationDbContext();
        return await read.JsonDocuments.SingleAsync(r => r.Id == id);
    }

    [TestMethod]
    public async Task EveryType_RoundTripsThroughAJsonColumn()
    {
        ContainerLifecycle.RequireDatabase();

        var loaded = await Insert(9001);

        var wrong = Properties()
                   .Where(p => !Equals(p.Get(Sample), p.Get(loaded.Doc)))
                   .Select(p => $"{p.Name}: wrote {p.Get(Sample)}, read {p.Get(loaded.Doc)}")
                   .ToList();

        Assert.AreEqual(0, wrong.Count, string.Join("; ", wrong));
    }

    /// <summary>
    /// The stored text is what PostgreSQL reads for each store type: the server casts every
    /// property out of the document and compares it with a bound parameter of that type.
    /// </summary>
    [TestMethod]
    public async Task EveryType_IsQueryableInsideTheJsonColumn()
    {
        ContainerLifecycle.RequireDatabase();
        await Insert(9002);

        var failures = new List<string>();
        foreach (var (name, _, equal) in Properties())
        {
            try
            {
                await using var ctx = new IntegrationDbContext();
                var count = await ctx.JsonDocuments.Where(r => r.Id == 9002).Where(equal).CountAsync();
                if (count != 1) failures.Add($"{name}: matched {count} rows");
            }
            catch (Exception e)
            {
                failures.Add($"{name}: {e.GetType().Name}: {(e.InnerException ?? e).Message.Split('\n')[0]}");
            }
        }

        Assert.AreEqual(0, failures.Count, string.Join(" | ", failures));
    }

    /// <summary>
    /// A tracked document changed and saved — the path an application takes, which is not
    /// <c>ExecuteUpdate</c>'s. A range and a value set are replaced, and a nullable range, temporal
    /// range and value set each go from null to a value and back, one change per save. Once every value
    /// is set, and again once they are back to null, the document is read through a new context and
    /// queried on the server, and every property no change touched is still the sample's.
    /// A complex type's document is rewritten whole on every save (<c>SET "Doc" = @p</c>); the owned
    /// counterpart below is the property-by-property path.
    /// </summary>
    [TestMethod]
    public async Task TrackedChanges_AndNullTransitions_SaveAndReadBack()
    {
        ContainerLifecycle.RequireDatabase();
        const int id = 9004;

        var seed = Copy(Sample);
        seed.MaybeSeats = null;
        await using (var write = new IntegrationDbContext())
        {
            write.JsonDocuments.Add(new JsonDocumentRow { Id = id, Doc = seed });
            await write.SaveChangesAsync();
        }

        var seats  = Int32Range.CreateFinite(100, 200);
        var tags   = StringSet.From("replaced", "Tags");
        var maybe  = Int32Range.CreateUnboundedStart(7);
        var window = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.FromHours(2)),
                                                      new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.FromHours(2)));
        var maybeTags = StringSet.From("x");
        string[] changed = ["Seats", "Tags", "MaybeSeats", "MaybeWindow", "MaybeTags"];

        // One change per save, so each has to be detected on its own: a save rewrites the whole
        // document, which would carry along a change the tracker missed next to one it saw.
        Action<JsonDocument>[] toValues =
            [d => d.Seats = seats, d => d.Tags = tags, d => d.MaybeSeats = maybe, d => d.MaybeWindow = window, d => d.MaybeTags = maybeTags];
        foreach (var change in toValues) await Change(id, change);

        var afterSet = await ReadBack(id);
        Assert.AreEqual(seats, afterSet.Seats);
        Assert.AreEqual(tags, afterSet.Tags);
        Assert.AreEqual(maybe, afterSet.MaybeSeats);
        Assert.AreEqual(window, afterSet.MaybeWindow);
        Assert.AreEqual(maybeTags, afterSet.MaybeTags);
        AssertUntouched(afterSet, changed);
        Assert.AreEqual(1, await CountWhere(id, r => r.Doc.Seats == seats && r.Doc.Tags == tags && r.Doc.MaybeSeats == maybe
                                                  && r.Doc.MaybeWindow == window && r.Doc.MaybeTags == maybeTags));

        Action<JsonDocument>[] toNull = [d => d.MaybeSeats = null, d => d.MaybeWindow = null, d => d.MaybeTags = null];
        foreach (var change in toNull) await Change(id, change);

        var afterNull = await ReadBack(id);
        Assert.IsNull(afterNull.MaybeSeats);
        Assert.IsNull(afterNull.MaybeWindow);
        Assert.IsNull(afterNull.MaybeTags);
        Assert.AreEqual(seats, afterNull.Seats);
        Assert.AreEqual(tags, afterNull.Tags);
        AssertUntouched(afterNull, changed);
        Assert.AreEqual(1, await CountWhere(id, r => r.Doc.MaybeSeats == null && r.Doc.MaybeWindow == null && r.Doc.MaybeTags == null
                                                  && r.Doc.Seats == seats && r.Doc.Tags == tags));
    }

    /// <summary>
    /// The same transitions on an owned JSON document, one property per save, which EF writes as
    /// <c>jsonb_set("Doc", '{MaybeTags}', @p)</c> — the value alone, through the plugin's JSON writer,
    /// next to siblings it must leave as they were.
    /// </summary>
    [TestMethod]
    public async Task OwnedDocument_TrackedChanges_AndNullTransitions_SaveAndReadBack()
    {
        ContainerLifecycle.RequireDatabase();
        const int id = 9005;

        var wall   = DateTimeRange.CreateFinite(new DateTime(2026, 1, 1, 9, 30, 0), new DateTime(2026, 1, 1, 17, 0, 0));
        var stamps = InstantSet.From(Instant.FromUtc(2026, 1, 1, 9, 30), Instant.FromUtc(2026, 6, 1, 0, 0));
        await using (var write = new IntegrationDbContext())
        {
            write.OwnedJsonDocuments.Add(new OwnedJsonRow
            {
                Id  = id,
                Doc = new OwnedJsonDocument { Seats = Int32Range.CreateFinite(1, 5), WallClock = wall, Tags = StringSet.From("a"), NodaStamps = stamps }
            });
            await write.SaveChangesAsync();
        }

        var seats  = Int32Range.CreateFinite(100, 200);
        var tags   = StringSet.From("replaced", "Tags");
        var maybe  = Int32Range.CreateUnboundedStart(7);
        var window = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(2026, 4, 1, 8, 0, 0, TimeSpan.FromHours(2)),
                                                      new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.FromHours(2)));
        var maybeTags = StringSet.From("x");

        // One change per save: EF writes a lone changed property with jsonb_set, but rewrites the
        // document when several change at once.
        Action<OwnedJsonDocument>[] toValues =
            [d => d.Seats = seats, d => d.Tags = tags, d => d.MaybeSeats = maybe, d => d.MaybeWindow = window, d => d.MaybeTags = maybeTags];
        foreach (var change in toValues) await ChangeOwned(id, change);

        var afterSet = await ReadBackOwned(id);
        Assert.AreEqual(seats, afterSet.Seats);
        Assert.AreEqual(tags, afterSet.Tags);
        Assert.AreEqual(maybe, afterSet.MaybeSeats);
        Assert.AreEqual(window, afterSet.MaybeWindow);
        Assert.AreEqual(maybeTags, afterSet.MaybeTags);
        Assert.AreEqual(wall, afterSet.WallClock);
        Assert.AreEqual(stamps, afterSet.NodaStamps);
        Assert.AreEqual(1, await CountOwnedWhere(id, r => r.Doc.Seats == seats && r.Doc.Tags == tags && r.Doc.MaybeSeats == maybe
                                                       && r.Doc.MaybeWindow == window && r.Doc.MaybeTags == maybeTags
                                                       && r.Doc.WallClock == wall && r.Doc.NodaStamps == stamps));

        Action<OwnedJsonDocument>[] toNull = [d => d.MaybeSeats = null, d => d.MaybeWindow = null, d => d.MaybeTags = null];
        foreach (var change in toNull) await ChangeOwned(id, change);

        var afterNull = await ReadBackOwned(id);
        Assert.IsNull(afterNull.MaybeSeats);
        Assert.IsNull(afterNull.MaybeWindow);
        Assert.IsNull(afterNull.MaybeTags);
        Assert.AreEqual(seats, afterNull.Seats);
        Assert.AreEqual(tags, afterNull.Tags);
        Assert.AreEqual(wall, afterNull.WallClock);
        Assert.AreEqual(stamps, afterNull.NodaStamps);
        Assert.AreEqual(1, await CountOwnedWhere(id, r => r.Doc.MaybeSeats == null && r.Doc.MaybeWindow == null && r.Doc.MaybeTags == null
                                                       && r.Doc.Seats == seats && r.Doc.Tags == tags
                                                       && r.Doc.WallClock == wall && r.Doc.NodaStamps == stamps));
    }

    private static async Task ChangeOwned(int id, Action<OwnedJsonDocument> change)
    {
        await using var ctx = new IntegrationDbContext();
        var row = await ctx.OwnedJsonDocuments.SingleAsync(r => r.Id == id);
        change(row.Doc);
        Assert.AreEqual(1, await ctx.SaveChangesAsync(), "the change tracker saw no change");
    }

    private static async Task<OwnedJsonDocument> ReadBackOwned(int id)
    {
        await using var ctx = new IntegrationDbContext();
        return (await ctx.OwnedJsonDocuments.AsNoTracking().SingleAsync(r => r.Id == id)).Doc;
    }

    private static async Task<int> CountOwnedWhere(int id, Expression<Func<OwnedJsonRow, bool>> filter)
    {
        await using var ctx = new IntegrationDbContext();
        return await ctx.OwnedJsonDocuments.Where(r => r.Id == id).Where(filter).CountAsync();
    }

    private static async Task Change(int id, Action<JsonDocument> change)
    {
        await using var ctx = new IntegrationDbContext();
        var row = await ctx.JsonDocuments.SingleAsync(r => r.Id == id);
        change(row.Doc);
        Assert.AreEqual(1, await ctx.SaveChangesAsync(), "the change tracker saw no change");
    }

    private static async Task<JsonDocument> ReadBack(int id)
    {
        await using var ctx = new IntegrationDbContext();
        return (await ctx.JsonDocuments.AsNoTracking().SingleAsync(r => r.Id == id)).Doc;
    }

    private static async Task<int> CountWhere(int id, Expression<Func<JsonDocumentRow, bool>> filter)
    {
        await using var ctx = new IntegrationDbContext();
        return await ctx.JsonDocuments.Where(r => r.Id == id).Where(filter).CountAsync();
    }

    private static void AssertUntouched(JsonDocument doc, string[] changed)
    {
        var wrong = typeof(JsonDocument).GetProperties()
                                        .Where(p => !changed.Contains(p.Name) && !Equals(p.GetValue(Sample), p.GetValue(doc)))
                                        .Select(p => $"{p.Name}: was {p.GetValue(Sample)}, now {p.GetValue(doc)}")
                                        .ToList();
        Assert.AreEqual(0, wrong.Count, string.Join("; ", wrong));
    }

    private static JsonDocument Copy(JsonDocument source)
    {
        var copy = new JsonDocument();
        foreach (var property in typeof(JsonDocument).GetProperties())
            property.SetValue(copy, property.GetValue(source));
        return copy;
    }

    /// <summary>The document itself, as another client reading the column sees it.</summary>
    [TestMethod]
    public async Task StoredDocument_HoldsTheLiterals()
    {
        ContainerLifecycle.RequireDatabase();
        await Insert(9003);

        await using var connection = new NpgsqlConnection(ContainerLifecycle.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "Doc" ->> 'Period', "Doc" ->> 'Tags', "Doc" ->> 'Billing', "Doc" ->> 'Months', "Doc" ->> 'WrappedMonths' FROM "JsonDocuments" WHERE "Id" = 9003""",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        Assert.AreEqual("[2026-01-01,2026-06-30]", reader.GetString(0));
        Assert.AreEqual("{Alpha,beta,\"quote\\\"d\",\"with,comma\"}", reader.GetString(1));

        // The YearMonth types are written in their store's month-aligned date form, not "2026-01":
        // a query into the document casts the text to daterange / date[], which only reads dates.
        Assert.AreEqual("[2026-01-01,2026-06-30]", reader.GetString(2));
        Assert.AreEqual("{2026-01-01,2026-06-01}", reader.GetString(3));

        // The wrapper arity too. Until 9.0.0 it was written in its element's own form, {2026-02,2026-05},
        // which the cast to date[] refuses — every query into it failed.
        Assert.AreEqual("{2026-02-01,2026-05-01}", reader.GetString(4));
    }
}

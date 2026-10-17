using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

public sealed class UpdateGuardRow
{
    public int Id { get; set; }
    public UpdateGuardDoc Doc { get; set; } = new();
    public UpdateGuardDoc Plain { get; set; } = new();
}

public sealed class UpdateGuardDoc
{
    public StringSet Tags { get; set; } = StringSet.Empty;
    public UpdateGuardInner Inner { get; set; } = new();
}

public sealed class UpdateGuardInner
{
    public StringSet Tags { get; set; } = StringSet.Empty;
}

public sealed class UpdateGuardNote
{
    public int Id { get; set; }
    public int RowId { get; set; }
}

/// <summary>
/// <c>ExecuteUpdate</c> inside a JSON column: what is allowed works and reads back, and the value set
/// setter is refused before it can write a document that no longer loads.
/// </summary>
/// <remarks>
/// A range setter is written by the server with <c>to_jsonb</c>, so the document then holds
/// PostgreSQL's own range output — quoted, space-separated, with the session's short offsets
/// (<c>+00</c>) — rather than the plugin's form. Reading that back is the strict temporal parser's
/// job, which is why the temporal ranges are here.
/// </remarks>
[TestClass]
public sealed class ExecuteUpdateJsonTests
{
    private static async Task<JsonDocumentRow> Seed(int id)
    {
        await using (var write = new IntegrationDbContext())
        {
            write.JsonDocuments.Add(new JsonDocumentRow { Id = id });
            await write.SaveChangesAsync();
        }

        await using var read = new IntegrationDbContext();
        return await read.JsonDocuments.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private static async Task<JsonDocument> Reload(int id)
    {
        await using var read = new IntegrationDbContext();
        return (await read.JsonDocuments.AsNoTracking().SingleAsync(r => r.Id == id)).Doc;
    }

    [TestMethod]
    public async Task Ranges_SetInsideTheDocument_ReadBack()
    {
        ContainerLifecycle.RequireDatabase();
        await Seed(9601);

        var seats   = Int32Range.CreateFinite(100, 200);
        var wall    = DateTimeRange.CreateFinite(new DateTime(2026, 1, 1, 9, 30, 0).AddTicks(1_234_560), new DateTime(2026, 1, 1, 17, 0, 0));
        var instant = DateTimeOffsetRange.CreateFinite(
            new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 1, 17, 0, 0, TimeSpan.Zero));
        var window  = InstantRange.CreateFinite(Instant.FromUtc(2026, 1, 1, 9, 30), Instant.FromUtc(2026, 1, 1, 17, 0));
        var blocks  = RangeSet<DateRange, DateOnly>.From(DateRange.CreateFinite(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));

        await using (var update = new IntegrationDbContext())
        {
            await update.JsonDocuments.Where(r => r.Id == 9601).ExecuteUpdateAsync(s => s
               .SetProperty(r => r.Doc.Seats, seats)
               .SetProperty(r => r.Doc.WallClock, wall)
               .SetProperty(r => r.Doc.Instants, instant)
               .SetProperty(r => r.Doc.NodaWindow, window)
               .SetProperty(r => r.Doc.Blocks, blocks));
        }

        var doc = await Reload(9601);
        Assert.AreEqual(seats, doc.Seats);
        Assert.AreEqual(wall, doc.WallClock);
        Assert.AreEqual(instant, doc.Instants);
        Assert.AreEqual(window, doc.NodaWindow);
        Assert.AreEqual(blocks, doc.Blocks);
    }

    [TestMethod]
    public async Task AValueSetInsideTheDocument_IsRefused_AndTheRowStillLoads()
    {
        ContainerLifecycle.RequireDatabase();
        var seeded = await Seed(9602);

        await using (var update = new IntegrationDbContext())
        {
            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => update.JsonDocuments.Where(r => r.Id == 9602)
                            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Doc.Tags, StringSet.From("c"))));
            StringAssert.Contains(refusal.Message, "'Doc.Tags'");
            StringAssert.Contains(refusal.Message, "JSON array");
            Assert.IsFalse(refusal.Message.Contains("writes NULL as the whole document"), refusal.Message);
        }

        Assert.AreEqual(seeded.Doc.Tags, (await Reload(9602)).Tags);
    }

    // -- Null: refused, with a message of its own (#57) --
    //
    // Measured against PostgreSQL 17 with the refusal disabled: a constant null and a null parameter
    // both reach the server as jsonb_set("Doc", '{MaybeTags}', to_jsonb(NULL)) — the provider inlines
    // a null parameter — and fail with 42804, the untyped NULL. A value that may be null on some rows
    // is typed (to_jsonb(CASE … THEN NULL ELSE @p END)), and jsonb_set returns NULL for a NULL value:
    // the UPDATE wrote NULL as the whole document and failed only because "Doc" is NOT NULL (23502).
    // The non-null branch would write the JSON array, so it keeps the to_jsonb message and adds this.

    [TestMethod]
    public async Task ANullValueSetInsideTheDocument_IsRefusedForWhatNullDoes_AndTheRowStillLoads()
    {
        ContainerLifecycle.RequireDatabase();

        // The parameter first: it compiles to the same SQL as the constant, and a refusal is never cached.
        StringSet? none = null;
        await AssertNullRefused(9641, s => s.SetProperty(r => r.Doc.MaybeTags, none));
        await AssertNullRefused(9642, s => s.SetProperty(r => r.Doc.MaybeTags, (StringSet?)null));

        async Task AssertNullRefused(int id, Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<JsonDocumentRow>> setter)
        {
            await SeedWithMaybeTags(id);
            await using (var update = new IntegrationDbContext())
            {
                var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                    () => update.JsonDocuments.Where(r => r.Id == id).ExecuteUpdateAsync(setter));
                StringAssert.Contains(refusal.Message, "'Doc.MaybeTags' inside a JSON column to null");
                StringAssert.Contains(refusal.Message, "writes NULL as the whole document");
                StringAssert.Contains(refusal.Message, "SaveChanges, which writes JSON null");
                Assert.IsFalse(refusal.Message.Contains("JSON array"), refusal.Message);
            }

            Assert.AreEqual(MaybeSeeded, (await Reload(id)).MaybeTags);
        }
    }

    [TestMethod]
    public async Task AValueSetThatMayBeNull_IsRefusedForBothOutcomes_AndTheRowStillLoads()
    {
        ContainerLifecycle.RequireDatabase();
        await SeedWithMaybeTags(9643);

        var some = StringSet.From("p");
        await using (var update = new IntegrationDbContext())
        {
            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => update.JsonDocuments.Where(r => r.Id == 9643)
                            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Doc.MaybeTags, r => r.Id > 0 ? null : some)));
            StringAssert.Contains(refusal.Message, "'Doc.MaybeTags' inside a JSON column.");
            StringAssert.Contains(refusal.Message, "JSON array");
            StringAssert.Contains(refusal.Message, "writes NULL as the whole document");
        }

        Assert.AreEqual(MaybeSeeded, (await Reload(9643)).MaybeTags);
    }

    /// <summary>The two ways the null refusal names, run against the server.</summary>
    [TestMethod]
    public async Task ANullValueSetInsideTheDocument_IsWrittenBySaveChanges_OrByTheWholeDocument()
    {
        ContainerLifecycle.RequireDatabase();
        await SeedWithMaybeTags(9644);
        await SeedWithMaybeTags(9645);

        await using (var tracked = new IntegrationDbContext())
        {
            var row = await tracked.JsonDocuments.SingleAsync(r => r.Id == 9644);
            row.Doc.MaybeTags = null;
            await tracked.SaveChangesAsync();
        }

        var cleared = await Reload(9645);
        cleared.MaybeTags = null;
        await using (var update = new IntegrationDbContext())
            await update.JsonDocuments.Where(r => r.Id == 9645).ExecuteUpdateAsync(s => s.SetProperty(r => r.Doc, cleared));

        await using var read = new IntegrationDbContext();
        foreach (var id in new[] { 9644, 9645 })
        {
            var doc = (await read.JsonDocuments.AsNoTracking().SingleAsync(r => r.Id == id)).Doc;
            Assert.IsNull(doc.MaybeTags, $"row {id}");
            Assert.AreEqual(StringSet.From("kept"), doc.Tags, $"row {id}");
        }

        Assert.AreEqual(2, await read.JsonDocuments.CountAsync(r => (r.Id == 9644 || r.Id == 9645) && r.Doc.MaybeTags == null));
    }

    private static readonly StringSet MaybeSeeded = StringSet.From("seed");

    private static async Task SeedWithMaybeTags(int id)
    {
        await using var write = new IntegrationDbContext();
        write.JsonDocuments.Add(new JsonDocumentRow { Id = id, Doc = new JsonDocument { MaybeTags = MaybeSeeded, Tags = StringSet.From("kept") } });
        await write.SaveChangesAsync();
    }

    [TestMethod]
    public async Task TheWholeDocument_AndANativeSetColumn_AreStillUpdatable()
    {
        ContainerLifecycle.RequireDatabase();
        await Seed(9603);

        var replacement = new JsonDocument { Tags = StringSet.From("x", "y"), Seats = Int32Range.CreateFinite(1, 2) };
        await using (var update = new IntegrationDbContext())
            await update.JsonDocuments.Where(r => r.Id == 9603).ExecuteUpdateAsync(s => s.SetProperty(r => r.Doc, replacement));

        var doc = await Reload(9603);
        Assert.AreEqual(replacement.Tags, doc.Tags);
        Assert.AreEqual(replacement.Seats, doc.Seats);

        await using (var write = new IntegrationDbContext())
        {
            write.Reservations.Add(new Reservation { Id = 9604 });
            await write.SaveChangesAsync();
        }

        var permissions = StringSet<TestKey>.From(TestKey.Parse("users.read", null));
        await using (var update = new IntegrationDbContext())
            await update.Reservations.Where(r => r.Id == 9604).ExecuteUpdateAsync(s => s.SetProperty(r => r.Permissions, permissions));

        await using var read = new IntegrationDbContext();
        Assert.AreEqual(permissions, (await read.Reservations.AsNoTracking().SingleAsync(r => r.Id == 9604)).Permissions);
    }

    /// <summary>
    /// <c>docs/efcore.md#inside-a-json-column</c>: setting a property inside the document to
    /// <c>null</c> with <c>ExecuteUpdate</c> fails for every family, and the row still loads. A range,
    /// a temporal range and a multirange reach Npgsql and fail there with <c>42804</c>; a value set
    /// never reaches it, because the value set refusal recognizes the setter first.
    /// </summary>
    [TestMethod]
    public async Task SettingAPropertyInsideTheDocumentToNull_Fails_ForEveryFamily()
    {
        ContainerLifecycle.RequireDatabase();

        var outcomes = new List<string>();
        await Attempt(9631, "range",          "42804", s => s.SetProperty(r => r.Doc.MaybeSeats, (Int32Range?)null));
        await Attempt(9632, "temporal range", "42804", s => s.SetProperty(r => r.Doc.MaybeWindow, (DateTimeOffsetRange?)null));
        await Attempt(9633, "multirange",     "42804", s => s.SetProperty(r => r.Doc.Blocks, (RangeSet<DateRange, DateOnly>)null!));
        await Attempt(9634, "value set",      "refused", s => s.SetProperty(r => r.Doc.MaybeTags, (StringSet?)null));

        Assert.AreEqual(0, outcomes.Count, "Unexpected outcomes:\n" + string.Join("\n", outcomes));

        async Task Attempt(
            int    id,
            string family,
            string expected,
            Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<JsonDocumentRow>> setter)
        {
            await Seed(id);
            string actual;
            await using (var update = new IntegrationDbContext())
            {
                try
                {
                    await update.JsonDocuments.Where(r => r.Id == id).ExecuteUpdateAsync(setter);
                    actual = "succeeded";
                }
                catch (PostgresException raised)
                {
                    actual = raised.SqlState;
                }
                catch (InvalidOperationException refusal) when (refusal.Message.Contains("cannot set the value set"))
                {
                    actual = "refused";
                }
            }

            if (actual != expected) outcomes.Add($"  {family}: {actual}, expected {expected}");
            _ = await Reload(id);
        }
    }

    // -- The refusal keys off what the UPDATE writes, not off how the setter names the property --
    //
    // Until 9.0.0 the refusal matched the setter lambda: a parameter of an entity type followed by a
    // plain member chain. A join and EF.Property reached the same jsonb_set(…, to_jsonb(@p)) without
    // being refused, and the row was corrupted. The refusal now reads the translated setter itself.

    private static readonly StringSet Seeded = StringSet.From("seed");

    private static async Task SeedGuardRow(int id)
    {
        await using var write = new IntegrationDbContext();
        write.UpdateGuardRows.Add(new UpdateGuardRow
        {
            Id    = id,
            Doc   = new UpdateGuardDoc { Tags = Seeded, Inner = new UpdateGuardInner { Tags = Seeded } },
            Plain = new UpdateGuardDoc { Tags = Seeded, Inner = new UpdateGuardInner { Tags = Seeded } }
        });
        write.UpdateGuardNotes.Add(new UpdateGuardNote { Id = id, RowId = id });
        await write.SaveChangesAsync();
    }

    /// <summary>Loads the row in full — that this still works is the assertion that matters.</summary>
    private static async Task<UpdateGuardRow> ReloadGuardRow(int id)
    {
        await using var read = new IntegrationDbContext();
        return await read.UpdateGuardRows.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    private static async Task AssertRefusedAndIntact(int id, Func<IntegrationDbContext, Task> update, string path)
    {
        await using (var context = new IntegrationDbContext())
        {
            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => update(context));
            StringAssert.Contains(refusal.Message, $"'{path}'");
            StringAssert.Contains(refusal.Message, "to_jsonb");
        }

        var row = await ReloadGuardRow(id);
        Assert.AreEqual(Seeded, row.Doc.Tags);
        Assert.AreEqual(Seeded, row.Doc.Inner.Tags);
    }

    [TestMethod]
    public async Task AValueSetReachedThroughAJoin_IsRefused_AndTheRowStillLoads()
    {
        ContainerLifecycle.RequireDatabase();
        await SeedGuardRow(9611);

        await AssertRefusedAndIntact(9611, context => context.UpdateGuardRows
           .Join(context.UpdateGuardNotes, r => r.Id, n => n.RowId, (r, n) => new { r, n })
           .Where(x => x.r.Id == 9611)
           .ExecuteUpdateAsync(s => s.SetProperty(x => x.r.Doc.Tags, StringSet.From("joined"))), "Doc.Tags");
    }

    [TestMethod]
    public async Task AValueSetNamedThroughEFProperty_IsRefused_AndTheRowStillLoads()
    {
        ContainerLifecycle.RequireDatabase();
        await SeedGuardRow(9612);

        await AssertRefusedAndIntact(9612, context => context.UpdateGuardRows
           .Where(r => r.Id == 9612)
           .ExecuteUpdateAsync(s => s.SetProperty(r => EF.Property<StringSet>(r.Doc, "Tags"), StringSet.From("named"))), "Doc.Tags");
    }

    [TestMethod]
    public async Task ANestedValueSetInsideTheDocument_IsRefused_AndTheRowStillLoads()
    {
        ContainerLifecycle.RequireDatabase();
        await SeedGuardRow(9613);

        await AssertRefusedAndIntact(9613, context => context.UpdateGuardRows
           .Where(r => r.Id == 9613)
           .ExecuteUpdateAsync(s => s.SetProperty(r => r.Doc.Inner.Tags, StringSet.From("nested"))), "Doc.Inner.Tags");
    }

    [TestMethod]
    public async Task AComputedValueSetSetter_IsRefused_AndTheRowStillLoads()
    {
        ContainerLifecycle.RequireDatabase();
        await SeedGuardRow(9614);

        // A column rather than a parameter: to_jsonb of the native array, the same JSON array.
        await AssertRefusedAndIntact(9614, context => context.UpdateGuardRows
           .Where(r => r.Id == 9614)
           .ExecuteUpdateAsync(s => s.SetProperty(r => r.Doc.Tags, r => r.Plain.Tags)), "Doc.Tags");
    }

    [TestMethod]
    public async Task AWholeComplexPropertyContainingASet_IsStillUpdatable()
    {
        ContainerLifecycle.RequireDatabase();
        await SeedGuardRow(9615);

        // EF serializes a complex value itself, through the plugin's JSON writer — the set arrives as
        // its literal string, which is what the document holds.
        var inner = new UpdateGuardInner { Tags = StringSet.From("b", "a") };
        await using (var update = new IntegrationDbContext())
            await update.UpdateGuardRows.Where(r => r.Id == 9615).ExecuteUpdateAsync(s => s.SetProperty(r => r.Doc.Inner, inner));

        var row = await ReloadGuardRow(9615);
        Assert.AreEqual(StringSet.From("a", "b"), row.Doc.Inner.Tags);
        Assert.AreEqual(Seeded, row.Doc.Tags);

        await using var query = new IntegrationDbContext();
        Assert.AreEqual(1, await query.UpdateGuardRows.CountAsync(r => r.Id == 9615 && r.Doc.Inner.Tags.Contains("b")));
    }

    [TestMethod]
    public async Task TheSameComplexTypeOutsideAJsonColumn_IsStillUpdatable()
    {
        ContainerLifecycle.RequireDatabase();
        await SeedGuardRow(9616);

        // UpdateGuardDoc is also mapped inside the document. A refusal resolved by CLR type would take
        // these setters with it; one reading the translated setter sees plain array columns.
        var tags = StringSet.From("y", "x");
        await using (var update = new IntegrationDbContext())
        {
            await update.UpdateGuardRows.Where(r => r.Id == 9616).ExecuteUpdateAsync(s => s
               .SetProperty(r => r.Plain.Tags, tags)
               .SetProperty(r => r.Plain.Inner.Tags, r => r.Plain.Tags.Remove("seed")));
        }

        var row = await ReloadGuardRow(9616);
        Assert.AreEqual(tags, row.Plain.Tags);
        Assert.AreEqual(StringSet.Empty, row.Plain.Inner.Tags);
        Assert.AreEqual(Seeded, row.Doc.Tags);
    }
}

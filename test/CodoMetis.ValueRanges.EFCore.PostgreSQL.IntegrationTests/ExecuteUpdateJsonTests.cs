using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

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
        }

        Assert.AreEqual(seeded.Doc.Tags, (await Reload(9602)).Tags);
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
}

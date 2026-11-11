using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

// INpgsqlSingletonOptions is Npgsql-internal (EF1001): the compatibility target the provider resolved
// has no public accessor, and the fourth test below is about its default.
#pragma warning disable EF1001

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// The four behaviour changes of EF Core 11 and Npgsql 11 that #103 measured under the plugin before
/// the retarget, each held by one test against the live server. None is a compile error, which is
/// why each has a test rather than a build.
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><c>JsonValueReaderWriter.ToJson</c> throws on <see langword="null"/> unless the new
///   <c>HandlesNullWrites</c> is <see langword="true"/>: EF Core must write a null property as JSON
///   null without calling the plugin's writers.</item>
///   <item><c>RuntimeConstantExpression</c>, the precompiled-query node that reduces to a constant:
///   an inlined range constant must still reach the interceptor as the <c>ConstantExpression</c> the
///   retyper handles.</item>
///   <item><c>SqlExpressionFactory</c> writes <c>a == b ? null : a</c> as <c>NULLIF(a, b)</c>: a new
///   shape the value set guard must refuse as equality over a server-side <c>Union</c>.</item>
///   <item>Npgsql's default PostgreSQL compatibility target moved from 14 to 16: the <c>timerange</c> and
///   multirange SQL must not depend on it.</item>
/// </list>
/// </remarks>
[TestClass]
public sealed class EfCore11RetargetTests
{
    private const int Id = 9801;

    /// <summary>
    /// Every property of a JSON document declares a reader/writer that leaves null to EF Core, and a
    /// document saved with null properties holds JSON <c>null</c> for them and reads them back as
    /// null — the "null and empty, kept apart" row of docs/efcore.md.
    /// </summary>
    [TestMethod]
    public async Task NullInsideAJsonDocument_IsWrittenByEfCore_NotTheWriter_AndReadsBackAsNull()
    {
        ContainerLifecycle.RequireDatabase();

        await using (var write = new IntegrationDbContext())
        {
            var complex = write.Model.FindEntityType(typeof(JsonDocumentRow))!.FindComplexProperty(nameof(JsonDocumentRow.Doc))!.ComplexType;
            var owned   = write.Model.FindEntityType(typeof(OwnedJsonRow))!.FindNavigation(nameof(OwnedJsonRow.Doc))!.TargetEntityType;

            // The plugin's writers only: the owned type's key and the ordinal columns have EF Core's own.
            var pluginWriters = complex.GetProperties().Concat(owned.GetProperties())
                                       .Select(property => (property, writer: property.GetTypeMapping().JsonValueReaderWriter))
                                       .Where(pair => pair.writer?.GetType().Namespace?.StartsWith("CodoMetis.", StringComparison.Ordinal) == true)
                                       .ToList();

            Assert.IsGreaterThanOrEqualTo(6, pluginWriters.Count, "Fewer plugin JSON writers than nullable properties in the two documents.");
            foreach (var (property, writer) in pluginWriters)
            {
                Assert.IsFalse(
                    writer!.HandlesNullWrites,
                    $"{property.DeclaringType.DisplayName()}.{property.Name}: {writer.GetType().Name} claims to handle null writes, "
                  + "so EF Core would call ToJson with null, which the plugin's writers do not accept.");
            }

            write.JsonDocuments.Add(new JsonDocumentRow { Id = Id, Doc = new JsonDocument { MaybeSeats = null, MaybeWindow = null, MaybeTags = null } });
            write.OwnedJsonDocuments.Add(new OwnedJsonRow { Id = Id, Doc = new OwnedJsonDocument { MaybeSeats = null, MaybeWindow = null, MaybeTags = null } });
            await write.SaveChangesAsync();
        }

        await using var read = new IntegrationDbContext();
        foreach (var table in new[] { "JsonDocuments", "OwnedJsonDocuments" })
        {
            await using var command = read.Database.GetDbConnection().CreateCommand();
            await read.Database.OpenConnectionAsync();
            command.CommandText = $"SELECT \"Doc\"::text FROM \"{table}\" WHERE \"Id\" = {Id}";
            var document = (string)(await command.ExecuteScalarAsync())!;

            foreach (var property in new[] { "MaybeSeats", "MaybeWindow", "MaybeTags" })
                StringAssert.Contains(document, $"\"{property}\": null", $"{table}: {document}");
        }

        var loaded = await read.JsonDocuments.AsNoTracking().SingleAsync(r => r.Id == Id);
        Assert.IsNull(loaded.Doc.MaybeSeats);
        Assert.IsNull(loaded.Doc.MaybeWindow);
        Assert.IsNull(loaded.Doc.MaybeTags);

        var ownedLoaded = await read.OwnedJsonDocuments.AsNoTracking().SingleAsync(r => r.Id == Id);
        Assert.IsNull(ownedLoaded.Doc.MaybeSeats);
        Assert.IsNull(ownedLoaded.Doc.MaybeWindow);
        Assert.IsNull(ownedLoaded.Doc.MaybeTags);
    }

    /// <summary>
    /// A range built inline from query-independent values is inlined by EF Core as a constant typed by
    /// its sealed variant, which <c>RangeVariantConstantRetyper</c> retypes to the union; with the
    /// variant left in place the comparison fails to translate ("does not have a type mapping
    /// assigned"), so the typed literal in the SQL and the matched row are the proof that it arrived as
    /// a <c>ConstantExpression</c>.
    /// </summary>
    /// <remarks>
    /// EF Core 11 creates <c>RuntimeConstantExpression</c> in shaper processing and in precompiled-query
    /// code generation (<c>RuntimeConstantProcessor</c>), after translation; the interceptor runs at
    /// <c>QueryCompilationStarting</c>, before parameter extraction, where the call is still the
    /// factory call EF Core later evaluates. <c>InlineRangeConstantExecutionTests</c> runs one per
    /// range type.
    /// </remarks>
    [TestMethod]
    public async Task AnInlinedVariantConstant_StillReachesTheRetyperAsAConstant()
    {
        ContainerLifecycle.RequireDatabase();

        await using (var write = new IntegrationDbContext())
        {
            write.Reservations.Add(new Reservation { Id = Id, Seats = Int32Range.CreateFinite(1, 5) });
            await write.SaveChangesAsync();
        }

        await using var read = new IntegrationDbContext();
        var query = read.Reservations.Where(r => r.Id == Id && r.Seats == Int32Range.CreateFinite(1, 5));

        StringAssert.Contains(query.ToQueryString(), "'[1,5]'::int4range");
        Assert.AreEqual(1, await query.CountAsync());
        Assert.AreEqual(0, await read.Reservations.CountAsync(r => r.Id == Id && r.Seats == Int32Range.CreateFinite(1, 6)));
    }

    /// <summary>
    /// EF Core 11 writes <c>a == b ? null : a</c> as <c>NULLIF(a, b)</c> — shown on a plain column
    /// first, so the test is about that shape — and a <c>NULLIF</c> whose first operand is a
    /// server-side text <c>Union</c> compares the concatenation. The guard compares by mutual
    /// containment instead, as it does <c>==</c> (#485), and the answer is memory's.
    /// </summary>
    [TestMethod]
    public async Task ANullIfOverAServerSideUnion_ComparesAsMemoryDoes()
    {
        ContainerLifecycle.RequireDatabase();

        await using (var write = new IntegrationDbContext())
        {
            write.Reservations.Add(new Reservation { Id = Id + 1, Tags = StringSet.From("a", "c") });
            await write.SaveChangesAsync();
        }

        await using var read = new IntegrationDbContext();
        var rows = read.Reservations.Where(r => r.Id == Id + 1);

        StringAssert.Contains(rows.Select(r => r.Id == 5 ? (int?)null : r.Id).ToQueryString(), "NULLIF(");

        var operand = StringSet.From("a", "b");
        var target  = StringSet.From("a", "b", "c");
        var other   = StringSet.From("a", "b", "z");
        Assert.AreEqual(target, StringSet.From("a", "c").Union(operand), "the union equals the target in memory");

        // {a,c,a,b} as the server concatenates it: = said false, so NULLIF returned the union.
        Assert.IsNull(await rows.Select(r => r.Tags.Union(operand) == target ? null : r.Tags.Union(operand)).SingleAsync());
        Assert.AreEqual(target, await rows.Select(r => r.Tags.Union(operand) == other ? null : r.Tags.Union(operand)).SingleAsync());
    }

    /// <summary>
    /// Npgsql 11 targets PostgreSQL 16 by default, and the SQL the plugin emits for the custom
    /// <c>timerange</c> and for multiranges is the same at that target as at the 14 Npgsql 10 assumed —
    /// nothing here is version-gated — and runs on the live server.
    /// </summary>
    [TestMethod]
    public async Task TheDefaultCompatibilityTarget_IsPostgreSql16_AndTimeRangeAndMultirangeSqlDoNotDependOnIt()
    {
        ContainerLifecycle.RequireDatabase();

        await using var context = new IntegrationDbContext();
        var target = context.GetService<INpgsqlSingletonOptions>().PostgresVersion;
        Assert.IsGreaterThanOrEqualTo(16, target.Major, $"Npgsql's default compatibility target is {target}; the plugin assumes 16, the 10.x floor.");

        await using var at14 = new CompatibilityContext(new Version(14, 0));
        foreach (var (name, query) in Queries())
            Assert.AreEqual(query(at14).ToQueryString(), query(context).ToQueryString(), name);

        await using (var write = new IntegrationDbContext())
        {
            write.Reservations.Add(new Reservation
            {
                Id           = Id + 2,
                OpeningHours = TimeRange.CreateFinite(new TimeOnly(9, 0), new TimeOnly(17, 0)),
                SeatBlocks   = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(10, 20)])
            });
            await write.SaveChangesAsync();
        }

        foreach (var (name, query) in Queries())
            Assert.AreEqual(1, await query(context).CountAsync(r => r.Id == Id + 2), name);
    }

    private static IEnumerable<(string Name, Func<DbContext, IQueryable<Reservation>> Query)> Queries()
    {
        var noon = new TimeOnly(12, 0);
        var late = TimeRange.CreateFinite(new TimeOnly(16, 0), new TimeOnly(18, 0));
        var band = Int32Range.CreateFinite(3, 12);
        var more = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(30, 40)]);

        yield return ("timerange contains",   db => db.Set<Reservation>().Where(r => r.OpeningHours.Contains(noon)));
        yield return ("timerange overlaps",   db => db.Set<Reservation>().Where(r => r.OpeningHours.Overlaps(late)));
        yield return ("timerange bounds",     db => db.Set<Reservation>().Where(r => r.OpeningHours.LowerBound() < noon && !r.OpeningHours.UpperBoundInclusive()));
        yield return ("multirange contains",  db => db.Set<Reservation>().Where(r => r.SeatBlocks.Contains(4)));
        yield return ("multirange overlaps",  db => db.Set<Reservation>().Where(r => r.SeatBlocks.Overlaps(band)));
        yield return ("multirange algebra",   db => db.Set<Reservation>().Where(r => r.SeatBlocks.Union(more).Except(band).Contains(15)));
        yield return ("multirange merge",     db => db.Set<Reservation>().Where(r => r.SeatBlocks.Merge() == Int32Range.CreateFinite(1, 20)));
    }

    /// <summary>The integration model under an explicit compatibility target, for the SQL alone.</summary>
    private sealed class CompatibilityContext(Version postgresVersion) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(
                ContainerLifecycle.ConnectionString!,
                npgsql => npgsql.UseValueRangesNodaTime().SetPostgresVersion(postgresVersion)
                                .ConfigureDataSource(dataSource => dataSource.EnableUnmappedTypes()));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresRange("timerange", "time");
            modelBuilder.HasPostgresRange("float8range", "double precision");
            modelBuilder.HasPostgresRange("float4range", "real");
            modelBuilder.Entity<Reservation>().ToTable("Reservations").Property(r => r.Id).ValueGeneratedNever();
        }
    }
}

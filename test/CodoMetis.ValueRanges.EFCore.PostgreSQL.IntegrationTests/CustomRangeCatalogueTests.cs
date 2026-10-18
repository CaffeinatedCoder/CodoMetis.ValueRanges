using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>docs/efcore.md#timerange-and-the-custom-timerange-type</c>, read back from the catalogue: after
/// <c>EnsureCreated</c> over a model with <c>HasPostgresRange("timerange", "time")</c>, PostgreSQL has a
/// range type <c>timerange</c> over <c>time</c>, has created <c>timemultirange</c> beside it, and the
/// <c>TimeRange</c> and <c>RangeSet&lt;TimeRange, TimeOnly&gt;</c> columns are of those two types.
/// </summary>
/// <remarks>
/// <c>EnsureCreated</c> runs the model differ through the migrations SQL generator, the same generator
/// a migration uses; <c>MigrationScaffoldTests</c> (no database) asserts that SQL. The schema is the
/// integration model's, created in a database of its own, so a defect that breaks the shared schema
/// fails here with PostgreSQL's error rather than leaving the test inconclusive.
/// </remarks>
[TestClass]
public class CustomRangeCatalogueTests
{
    [TestMethod]
    public async Task EnsureCreated_CreatesTimerangeAndItsMultirange_AndTheColumnsUseThem()
    {
        ContainerLifecycle.RequireServer();

        var connectionString = new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString)
        {
            Database = $"timerange_catalogue_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using (var context = new IntegrationDbContext(connectionString))
            Assert.IsTrue(await context.Database.EnsureCreatedAsync(), "EnsureCreated found the fresh database already created.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var range = new NpgsqlCommand(
                         """
                         SELECT format_type(r.rngsubtype, NULL), m.typname
                         FROM pg_type t
                         JOIN pg_range r ON r.rngtypid = t.oid
                         JOIN pg_type m ON m.oid = r.rngmultitypid
                         WHERE t.typname = 'timerange' AND t.typtype = 'r'
                         """, connection))
        await using (var reader = await range.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync(), "pg_type has no range type named timerange.");
            Assert.AreEqual("time without time zone", reader.GetString(0));
            Assert.AreEqual("timemultirange", reader.GetString(1));
        }

        await using (var multirange = new NpgsqlCommand(
                         "SELECT count(*) FROM pg_type WHERE typname = 'timemultirange' AND typtype = 'm'", connection))
            Assert.AreEqual(1L, await multirange.ExecuteScalarAsync(), "pg_type has no multirange type named timemultirange.");

        await using var columns = new NpgsqlCommand(
            """
            SELECT column_name, udt_name FROM information_schema.columns
            WHERE table_name = 'Reservations' AND column_name IN ('OpeningHours', 'OpeningWindows')
            ORDER BY column_name
            """, connection);
        await using var columnReader = await columns.ExecuteReaderAsync();

        var found = new List<string>();
        while (await columnReader.ReadAsync()) found.Add($"{columnReader.GetString(0)} {columnReader.GetString(1)}");

        CollectionAssert.AreEqual(new[] { "OpeningHours timerange", "OpeningWindows timemultirange" }, found);
    }
}

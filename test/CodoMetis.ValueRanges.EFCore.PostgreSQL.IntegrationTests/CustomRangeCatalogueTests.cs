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

    /// <summary>
    /// <c>docs/efcore.md</c>'s custom types table, read back from the catalogue: <c>HasPostgresRange("float8range",
    /// "double precision")</c> creates a range type over <c>double precision</c> with no <c>subtype_diff</c> (#153),
    /// <c>float8multirange</c> beside it, and the <c>Float64Range</c> columns are of those two types.
    /// </summary>
    [TestMethod]
    public async Task EnsureCreated_CreatesFloat8rangeWithoutASubtypeDiff_AndTheColumnsUseIt()
    {
        ContainerLifecycle.RequireServer();

        var connectionString = new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString)
        {
            Database = $"float8range_catalogue_{Guid.NewGuid():N}"
        }.ConnectionString;

        await using (var context = new IntegrationDbContext(connectionString))
            Assert.IsTrue(await context.Database.EnsureCreatedAsync(), "EnsureCreated found the fresh database already created.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var range = new NpgsqlCommand(
                         """
                         SELECT format_type(r.rngsubtype, NULL), m.typname, r.rngsubdiff::text
                         FROM pg_type t
                         JOIN pg_range r ON r.rngtypid = t.oid
                         JOIN pg_type m ON m.oid = r.rngmultitypid
                         WHERE t.typname = 'float8range' AND t.typtype = 'r'
                         """, connection))
        await using (var reader = await range.ExecuteReaderAsync())
        {
            Assert.IsTrue(await reader.ReadAsync(), "pg_type has no range type named float8range.");
            Assert.AreEqual("double precision", reader.GetString(0));
            Assert.AreEqual("float8multirange", reader.GetString(1));
            Assert.AreEqual("-", reader.GetString(2), "float8range was created with a subtype_diff.");
        }

        await using var columns = new NpgsqlCommand(
            """
            SELECT column_name, udt_name FROM information_schema.columns
            WHERE table_name = 'Reservations' AND column_name IN ('Score', 'ScoreBands', 'Weights')
            ORDER BY column_name
            """, connection);
        await using var columnReader = await columns.ExecuteReaderAsync();

        var found = new List<string>();
        while (await columnReader.ReadAsync()) found.Add($"{columnReader.GetString(0)} {columnReader.GetString(1)}");

        CollectionAssert.AreEqual(new[] { "Score float8range", "ScoreBands float8multirange", "Weights _float8" }, found);
    }
}

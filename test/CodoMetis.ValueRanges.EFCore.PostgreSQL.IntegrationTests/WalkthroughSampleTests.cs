using System.Net;
using System.Text;
using Bookings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CodoMetis.ValueRanges.EFCore.PostgreSQL.IntegrationTests;

/// <summary>
/// <c>docs/walkthrough.md</c>, executed: the booking service in <c>samples/Bookings</c> (linked into this
/// project) hosted in-process against its own database on the shared server, migrated by its own
/// migration. Each test asserts the responses, SQL and stored text the page shows. The consumer smoke
/// test asserts the same answers from the sample built against the packed packages; this is the holder
/// the claim catalogue can name.
/// </summary>
[TestClass]
public sealed class WalkthroughSampleTests
{
    private const string Ada =
        """{"roomId":1,"guest":"Ada","stay":"[2026-07-01,2026-07-05)","arrival":"[2026-07-01T15:00:00.1234567+02:00,2026-07-01T18:00:00+02:00)"}""";

    private const string Problem = """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":""";

    [TestMethod]
    public async Task Search_BindsTheQueryString_AndRunsAsContainment()
    {
        await using var sample = await SampleHost.StartAsync();

        await sample.Expect(HttpStatusCode.OK,
            """{"stay":"[2026-07-01,2026-07-04]","features":["balcony","sea-view"],"rooms":[{"id":1,"name":"Harbour","features":["balcony","sea-view"]},{"id":3,"name":"Lighthouse","features":["balcony","bathtub","sea-view"]}]}""",
            sample.Get("/rooms?stay=[2026-07-01,2026-07-05)&features={sea-view,balcony}"));

        var search = sample.Sql.Single(sql => sql.Contains("FROM \"Rooms\" AS r", StringComparison.Ordinal) && sql.Contains("NOT EXISTS", StringComparison.Ordinal));
        StringAssert.Contains(search, "WHERE r.\"Features\" @> @required AND r.\"Bookable\" @> @stay AND NOT EXISTS (");
        StringAssert.Contains(search, "WHERE b.\"RoomId\" = r.\"Id\" AND b.\"Period\" && @stay)");

        await sample.Expect(HttpStatusCode.BadRequest, "", sample.Get("/rooms?stay=[2026-07-10,2026-07-01)"));
    }

    [TestMethod]
    public async Task Booking_EchoesTheBody_AndReadsBackWhatPostgreSqlStored()
    {
        await using var sample = await SampleHost.StartAsync();

        await sample.Expect(HttpStatusCode.Created,
            """{"id":1,"roomId":1,"guest":"Ada","stay":"[2026-07-01,2026-07-04]","arrival":"[2026-07-01T15:00:00.1234567+02:00,2026-07-01T18:00:00.0000000+02:00)"}""",
            sample.Post(Ada));
        await sample.Expect(HttpStatusCode.OK,
            """{"id":1,"roomId":1,"guest":"Ada","stay":"[2026-07-01,2026-07-04]","arrival":"[2026-07-01T13:00:00.1234560+00:00,2026-07-01T16:00:00.0000000+00:00)"}""",
            sample.Client.GetAsync("/bookings/1"));

        Assert.AreEqual("""[2026-07-01,2026-07-05)|["2026-07-01 13:00:00.123456+00","2026-07-01 16:00:00+00")""",
            await sample.Scalar("""SELECT "Period"::text || '|' || "Arrival"::text FROM "Bookings" WHERE "Id" = 1"""));
        Assert.AreEqual("1|{balcony,sea-view}|[2026-01-01,) 2|{garden,step-free}|[2026-05-01,2026-10-01) 3|{balcony,bathtub,sea-view}|[2026-01-01,)",
            await sample.Scalar("""SELECT string_agg("Id" || '|' || "Features"::text || '|' || "Bookable"::text, ' ' ORDER BY "Id") FROM "Rooms" """));
    }

    [TestMethod]
    public async Task ExclusionConstraint_RejectsAnOverlap_AndAcceptsTheAdjacentStay()
    {
        await using var sample = await SampleHost.StartAsync();

        Assert.AreEqual("""ex_bookings_room_period EXCLUDE USING gist ("RoomId" WITH =, "Period" WITH &&)""",
            await sample.Scalar("SELECT conname || ' ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE contype = 'x'"));

        await sample.Expect(HttpStatusCode.Created, null, sample.Post(Ada));
        await sample.Expect(HttpStatusCode.Conflict, """{"error":"Harbour is already booked for some of those nights."}""",
            sample.Post("""{"roomId":1,"guest":"Grace","stay":"[2026-07-04,2026-07-06)"}"""));
        await sample.Expect(HttpStatusCode.Created,
            """{"id":3,"roomId":1,"guest":"Grace","stay":"[2026-07-05,2026-07-07]","arrival":null}""",
            sample.Post("""{"roomId":1,"guest":"Grace","stay":"[2026-07-05,2026-07-08)"}"""));
    }

    [TestMethod]
    public async Task Validation_RejectsANullStay_AnOpenStay_AStayOutOfSeason_AndALiteralThatDoesNotParse()
    {
        await using var sample = await SampleHost.StartAsync();

        await sample.Expect(HttpStatusCode.BadRequest, Problem + """{"stay":["The stay needs a first and a last night."]}}""",
            sample.Post("""{"roomId":1,"guest":"Linus","stay":"[2026-08-01,)"}"""));
        await sample.Expect(HttpStatusCode.BadRequest, Problem + """{"stay":["The stay needs a first and a last night."]}}""",
            sample.Post("""{"roomId":1,"guest":"Linus","stay":"(,2026-08-01)"}"""));
        await sample.Expect(HttpStatusCode.BadRequest, Problem + """{"stay":["The stay has no nights."]}}""",
            sample.Post("""{"roomId":1,"guest":"Linus","stay":"[2026-08-01,2026-08-01)"}"""));
        await sample.Expect(HttpStatusCode.BadRequest, Problem + """{"stay":["A stay is at most 30 nights."]}}""",
            sample.Post("""{"roomId":1,"guest":"Linus","stay":"[2026-08-01,2026-09-01)"}"""));
        await sample.Expect(HttpStatusCode.Created, null,
            sample.Post("""{"roomId":1,"guest":"Linus","stay":"[2026-08-01,2026-08-31)"}"""));
        // System.Text.Json binds a JSON null, or no stay at all, as null whatever the annotation (#465).
        await sample.Expect(HttpStatusCode.BadRequest, Problem + """{"stay":["Give the stay."]}}""",
            sample.Post("""{"roomId":1,"guest":"Linus","stay":null}"""));
        await sample.Expect(HttpStatusCode.BadRequest, Problem + """{"stay":["Give the stay."]}}""",
            sample.Post("""{"roomId":1,"guest":"Linus"}"""));
        await sample.Expect(HttpStatusCode.BadRequest, Problem + """{"guest":["Name the guest."]}}""",
            sample.Post("""{"roomId":1,"stay":"[2026-09-01,2026-09-03)"}"""));
        await sample.Expect(HttpStatusCode.BadRequest, Problem + """{"stay":["Garden takes bookings for [2026-05-01,2026-09-30] only."]}}""",
            sample.Post("""{"roomId":2,"guest":"Linus","stay":"[2026-10-01,2026-10-03)"}"""));
        await sample.Expect(HttpStatusCode.BadRequest, null,
            sample.Post("""{"roomId":1,"guest":"Linus","stay":"[2026-07-10,2026-07-01)"}"""));

        Assert.AreEqual("1", await sample.Scalar("""SELECT count(*)::text FROM "Bookings" """));
    }

    [TestMethod]
    public async Task Availability_AggregatesInSql_AndFindsTheGapsWithExcept()
    {
        await using var sample = await SampleHost.StartAsync();
        await sample.Expect(HttpStatusCode.Created, null, sample.Post(Ada));
        await sample.Expect(HttpStatusCode.Created, null, sample.Post("""{"roomId":1,"guest":"Grace","stay":"[2026-07-05,2026-07-08)"}"""));

        await sample.Expect(HttpStatusCode.OK,
            """{"roomId":1,"window":"[2026-07-01,2026-07-31]","booked":"{[2026-07-01,2026-07-07]}","free":"{[2026-07-08,2026-07-31]}"}""",
            sample.Get("/rooms/1/availability?window=[2026-07-01,2026-07-31]"));
        await sample.Expect(HttpStatusCode.OK,
            """{"roomId":1,"window":"[2026-06-28,)","booked":"{[2026-07-01,2026-07-07]}","free":"{[2026-06-28,2026-06-30],[2026-07-08,)}"}""",
            sample.Get("/rooms/1/availability?window=[2026-06-28,)"));
        await sample.Expect(HttpStatusCode.OK,
            """{"roomId":3,"window":"[2026-07-01,2026-07-31]","booked":"{}","free":"{[2026-07-01,2026-07-31]}"}""",
            sample.Get("/rooms/3/availability?window=[2026-07-01,2026-07-31]"));

        Assert.IsTrue(sample.Sql.Any(sql => sql.Contains("SELECT range_agg(b.\"Period\")", StringComparison.Ordinal)),
            "The bookings' RangeSet was not computed by range_agg:" + Environment.NewLine + string.Join(Environment.NewLine, sample.Sql));
    }

    /// <summary>The sample on a test server, over a database of its own, composed as its Program.cs composes it.</summary>
    private sealed class SampleHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly string _connectionString;

        private SampleHost(WebApplication app, string connectionString, List<string> sql)
        {
            _app              = app;
            _connectionString = connectionString;
            Sql               = sql;
            Client            = app.GetTestClient();
        }

        internal HttpClient Client { get; }

        /// <summary>The text of every command EF Core executed.</summary>
        internal List<string> Sql { get; }

        internal static async Task<SampleHost> StartAsync()
        {
            ContainerLifecycle.RequireServer();
            var connectionString = new NpgsqlConnectionStringBuilder(ContainerLifecycle.ServerConnectionString)
            {
                Database = $"walkthrough_{Guid.NewGuid():N}"
            }.ConnectionString;

            var sql     = new List<string>();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<BookingContext>(options => options
                .UseNpgsql(connectionString, npgsql => npgsql.UseValueRanges())
                .LogTo(line => { lock (sql) sql.Add(line); }, [RelationalEventId.CommandExecuted]));

            var app = builder.Build();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BookingContext>();
                await db.Database.MigrateAsync();   // creates the database, as the sample does on startup
                await BookingContext.SeedAsync(db);
            }

            app.MapBookingApi();
            await app.StartAsync();
            return new SampleHost(app, connectionString, sql);
        }

        /// <summary>A GET with the query string escaped, as curl's --data-urlencode does.</summary>
        internal Task<HttpResponseMessage> Get(string pathAndQuery)
        {
            var parts = pathAndQuery.Split('?', 2);
            var query = parts.Length == 1
                ? ""
                : "?" + string.Join("&", parts[1].Split('&').Select(pair => pair.Split('=', 2)).Select(pair => pair[0] + "=" + Uri.EscapeDataString(pair[1])));
            return Client.GetAsync(parts[0] + query);
        }

        internal Task<HttpResponseMessage> Post(string json) =>
            Client.PostAsync("/bookings", new StringContent(json, Encoding.UTF8, "application/json"));

        /// <summary>Asserts the status, and the body when <paramref name="body"/> is not <see langword="null"/>.</summary>
        internal async Task Expect(HttpStatusCode status, string? body, Task<HttpResponseMessage> request)
        {
            using var response = await request;
            var text = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(status, response.StatusCode, text);
            if (body is not null) Assert.AreEqual(body, text);
        }

        internal async Task<string?> Scalar(string sql)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return (string?)await command.ExecuteScalarAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(ContainerLifecycle.ServerConnectionString);
            await connection.OpenAsync();
            var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

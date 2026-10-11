using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.ValueRanges.AspNetCore.Tests;

public sealed record Booking(DateRange Period, RangeSet<DateRange, DateOnly> Blocks, StringSet Tags);

[ApiController]
[Route("mvc")]
public sealed class BookingController : ControllerBase
{
    /// <summary>MVC binds a query-string value through the type's static TryParse (since .NET 7).</summary>
    [HttpGet("period")]
    public ActionResult<DateRange> Period([FromQuery] DateRange period) => period;

    [HttpGet("tags")]
    public ActionResult<StringSet> Tags([FromQuery] StringSet tags) => tags;

    [HttpPost("booking")]
    public ActionResult<Booking> Echo([FromBody] Booking booking) => booking;
}

/// <summary>
/// A real ASP.NET Core host with <b>no</b> ValueRanges registration of any kind — no
/// <c>AddRangeConverters()</c>, no model binder. Query values bind through <c>TryParse</c>, which
/// always worked; what failed before 9.0.0 was the JSON on either side of it — every request body
/// a 400 and every response a property dump, for want of the type-level <c>[JsonConverter]</c>.
/// </summary>
[TestClass]
public sealed class HostBindingTests
{
    private static WebApplication? _app;
    private static HttpClient _client = null!;

    private static readonly DateRange H1 = DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

    [ClassInitialize]
    public static async Task StartHost(TestContext _)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(BookingController).Assembly);

        _app = builder.Build();
        _app.MapControllers();
        _app.MapPost("/minimal/booking", (Booking booking) => booking);
        _app.MapGet("/minimal/period", (DateRange period) => period);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [ClassCleanup]
    public static async Task StopHost()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [TestMethod]
    public async Task Mvc_BindsARangeFromTheQueryString()
    {
        var response = await _client.GetAsync("/mvc/period?period=[2026-01-01,2026-06-30]");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("\"[2026-01-01,2026-06-30]\"", await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task Mvc_BindsAValueSetFromTheQueryString()
    {
        var response = await _client.GetAsync("/mvc/tags?tags={beta,alpha}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("""["alpha","beta"]""", await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task Mvc_RefusesAMalformedQueryValueWithA400()
    {
        var response = await _client.GetAsync("/mvc/period?period=[2026-13-01,2026-14-01]");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    [DataRow("/mvc/booking")]
    [DataRow("/minimal/booking")]
    public async Task JsonBody_RoundTripsWithoutRegistration(string route)
    {
        const string body =
            """{"period":"[2026-01-01,2026-06-30]","blocks":"{[2026-01-01,2026-01-31]}","tags":["a","b"]}""";

        var response = await _client.PostAsync(route, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(body, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task Minimal_BindsTheQueryStringThroughTryParse()
    {
        var period = await _client.GetFromJsonAsync<DateRange>("/minimal/period?period=[2026-01-01,2026-06-30]");

        Assert.AreEqual(H1, period);
    }
}

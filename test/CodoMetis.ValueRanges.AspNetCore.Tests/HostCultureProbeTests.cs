using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodoMetis.ValueRanges.Conventions.Tests;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.ValueRanges.AspNetCore.Tests;

/// <summary>
/// Route B, step 2 (#105, #124, <c>test/oracles/axes.md</c>): every discovered range, multirange and
/// value set type × every way an ASP.NET Core host binds it × the request culture. The oracle is the
/// documented promise (J15, J16): with no registration, a host binds a valid literal or JSON value to the
/// value the invariant culture reads and answers 200 with it, refuses a malformed one or a literal of
/// another shape with 400 — and never answers 200 having bound <see langword="null"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HostBindingTests"/> holds the promise for a <see cref="DateRange"/> and a
/// <see cref="StringSet"/> under the default culture. This class keeps the product: query, nullable
/// query, route value, form field and JSON body, through MVC and a minimal API, for the union, each sealed
/// variant, the multirange and every set type, under four request cultures set the way a host sets them,
/// by <c>UseRequestLocalization</c> from <c>Accept-Language</c>. Form binding in MVC is the entry point
/// that hands the current culture to the binder, so the culture axis is not decorative; each answer
/// echoes the culture the handler ran under, and a row whose culture never arrived fails.
/// </para>
/// <para>
/// One host serves every type: a minimal-API route group and a closed <see cref="ProbeController{T}"/>
/// per declared type, routed under its index. A minimal-API handler that its factory refuses at startup
/// is recorded as that cell's failure instead of failing the host. The host-free entry points (Parse,
/// TypeConverter, configuration, JSON) are <c>CultureEntryPointProbeTests</c> in the conventions project.
/// </para>
/// </remarks>
[TestClass]
public sealed class HostCultureProbeTests
{
    /// <summary>The request-culture axis: none (the invariant default), comma decimals, a Persian and a Buddhist calendar.</summary>
    private static readonly string[] RequestCultures = ["", "de-DE", "fa-IR", "th-TH"];

    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;

    private static WebApplication? _app;
    private static HttpClient _client = null!;

    /// <summary>Every declared type the host binds: unions, their variants, multiranges and sets. Its index is its route prefix.</summary>
    private static readonly List<Type> Declared = DiscoverDeclared();

    /// <summary>Minimal-API handlers the request delegate factory refused at startup, keyed by type and entry point.</summary>
    private static readonly Dictionary<(Type, string), string> StartupFailures = [];

    [ClassInitialize]
    public static async Task StartHost(TestContext _)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers(options => options.Conventions.Add(new ProbeRouteConvention(Declared)))
                        .ConfigureApplicationPartManager(parts => parts.FeatureProviders.Add(new ProbeControllerFeatureProvider(Declared)));

        _app = builder.Build();

        CultureInfo[] supported = [CultureInfo.InvariantCulture, .. RequestCultures.Where(c => c.Length > 0).Select(CultureInfo.GetCultureInfo)];
        _app.UseRequestLocalization(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture);
            options.SupportedCultures     = supported;
            options.SupportedUICultures   = supported;
        });

        foreach (var declared in Declared)
            typeof(HostCultureProbeTests).GetMethod(nameof(MapMinimal), BindingFlags.NonPublic | BindingFlags.Static)!
                                         .MakeGenericMethod(declared)
                                         .Invoke(null, [_app, $"/t{Declared.IndexOf(declared)}/minimal"]);
        _app.MapControllers();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [ClassCleanup]
    public static async Task StopHost()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    /// <summary>One row per discovered type; the row's label is the prefix every failure line and candidate row starts with.</summary>
    public sealed record ProbeRow(string Label, Func<List<string>, Task<int>> Sweep)
    {
        public override string ToString() => Label;
    }

    public static IEnumerable<object[]> Rows
    {
        get
        {
            foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
            {
                yield return [new ProbeRow(rangeType.Name, failures => Reflect.InvokeGeneric<Task<int>>(typeof(HostCultureProbeTests), nameof(SweepRange), [rangeType, elementType], failures))];
                yield return [new ProbeRow($"RangeSet<{rangeType.Name}>", failures => Reflect.InvokeGeneric<Task<int>>(typeof(HostCultureProbeTests), nameof(SweepRangeSet), [rangeType, elementType], failures))];
            }

            foreach (var (setType, elementType) in SetProbes.AllSetTypes())
                yield return [new ProbeRow(Display(setType), failures => Reflect.InvokeGeneric<Task<int>>(typeof(HostCultureProbeTests), nameof(SweepSet), [setType, elementType], failures))];
        }
    }

    /// <summary>The floor that keeps the row list and the host's routes from passing by finding nothing.</summary>
    [TestMethod]
    public void Discovery_FindsEveryRowAndType()
    {
        // 11 ranges, their 11 multiranges, 30 set types; 11 unions with 5 variants each, 11 multiranges, 30 sets.
        Assert.IsTrue(Rows.Count() >= 52, $"Found {Rows.Count()} rows, fewer than 52.");
        Assert.IsTrue(Declared.Count >= 107, $"The host binds {Declared.Count} declared types, fewer than 107.");
    }

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public async Task EveryHostEntryPoint_BindsTheInvariantValue_UnderEveryRequestCulture(ProbeRow row)
    {
        var failures = new List<string>();
        var checks   = await row.Sweep(failures);

        // Four cultures × ten entry points × at least two values.
        Assert.IsTrue(checks >= 80, $"{row}: only {checks} checks ran.");

        Candidates.Verdict(nameof(HostCultureProbeTests), failures,
            $"{row}: of {checks} requests, these disagree with the documented binding", rowPrefix: row.Label + " ");
    }

    // --- the types --------------------------------------------------------------------------------

    private static async Task<int> SweepRange<TRange, T>(List<string> failures)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var row    = typeof(TRange).Name;
        var shapes = RangeProbes.Shapes<TRange, T>();
        var checks = 0;

        for (var i = 0; i < shapes.Length; i++)
        {
            var range   = shapes[i];
            var variant = range.GetType();
            checks += await Probe(failures, row, typeof(TRange), range);
            checks += await Probe(failures, row, variant, range);

            // A variant-typed parameter refuses a literal of another shape (docs: "A property typed as one shape rejects the others").
            var other = shapes[(i + 1) % shapes.Length];
            checks += await Refuse(failures, row, variant, Literal(other), JsonSerializer.Serialize(other, typeof(TRange), Web), "another shape");
        }

        checks += await Refuse(failures, row, typeof(TRange), "[2,1", "\"[2,1\"", "a malformed literal");
        return checks;
    }

    private static async Task<int> SweepRangeSet<TRange, T>(List<string> failures)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var row    = $"RangeSet<{typeof(TRange).Name}>";
        var shapes = RangeProbes.Shapes<TRange, T>();
        var checks = 0;

        foreach (var set in new[] { RangeSet<TRange, T>.Empty, RangeSet<TRange, T>.From([shapes[2]]), RangeSet<TRange, T>.From([TRange.Infinite]) })
            checks += await Probe(failures, row, typeof(RangeSet<TRange, T>), set);

        checks += await Refuse(failures, row, typeof(RangeSet<TRange, T>), "{[2,1", "\"{[2,1\"", "a malformed literal");
        return checks;
    }

    private static async Task<int> SweepSet<TSet, T>(List<string> failures)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var row    = Display(typeof(TSet));
        var checks = 0;

        foreach (var set in new[] { TSet.Empty, TSet.From(SetProbes.For<T>()) })
            checks += await Probe(failures, row, typeof(TSet), set);

        checks += await Refuse(failures, row, typeof(TSet), "{a,", "\"{a,\"", "a malformed literal");
        return checks;
    }

    // --- the entry points -------------------------------------------------------------------------

    /// <summary>
    /// The host's ways in, as the failure line names them; the second item builds the request from the
    /// literal and the JSON value. A minimal API binds the four non-body sources through the static
    /// <c>TryParse</c> (J15), and the name says so: it is the mechanism #116 is about, and it lets one
    /// candidate row cover exactly those cells.
    /// </summary>
    private static IEnumerable<(string EntryPoint, Func<string, string, HttpRequestMessage> Request)> EntryPoints(int index)
    {
        foreach (var (host, segment) in new[] { ("minimal TryParse", "minimal"), ("mvc", "mvc") })
        {
            var prefix = $"/t{index}/{segment}";
            yield return ($"{host} query",          (literal, _) => new(HttpMethod.Get, $"{prefix}/query?v={Uri.EscapeDataString(literal)}"));
            yield return ($"{host} nullable query", (literal, _) => new(HttpMethod.Get, $"{prefix}/nullable?v={Uri.EscapeDataString(literal)}"));
            yield return ($"{host} route value",    (literal, _) => new(HttpMethod.Get, $"{prefix}/route/{Uri.EscapeDataString(literal)}"));
            yield return ($"{host} form field",     (literal, _) => new(HttpMethod.Post, $"{prefix}/form") { Content = new FormUrlEncodedContent([new("v", literal)]) });
            yield return ($"{segment} JSON body",   (_, json) => new(HttpMethod.Post, $"{prefix}/body") { Content = new StringContent($$"""{"value":{{json}}}""", Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>What the declaration is, named in the line before the entry point so a candidate row can scope to one kind.</summary>
    private static string Kind(Type declared)
        => declared.IsNested ? "variant"
         : declared.IsGenericType && declared.GetGenericTypeDefinition() == typeof(RangeSet<,>) ? "multirange"
         : declared.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValueSet<>)) ? "set"
         : "union";

    /// <summary>A valid value, declared as <paramref name="declared"/>: every entry point answers 200 with it, under every request culture.</summary>
    private static async Task<int> Probe(List<string> failures, string row, Type declared, object value)
    {
        var literal  = Literal(value);
        var expected = JsonSerializer.Serialize(value, declared, Web);
        var index    = Declared.IndexOf(declared);
        var checks   = 0;

        foreach (var (entryPoint, request) in EntryPoints(index))
        {
            var where = $"{row} {Kind(declared)} {entryPoint} as {Display(declared)}";
            if (StartupFailures.TryGetValue((declared, entryPoint), out var refused))
            {
                failures.Add($"{where}: the handler was refused at startup: {refused}");
                continue;
            }

            foreach (var culture in RequestCultures)
            {
                checks++;
                var at = $"for {literal} under {(culture.Length == 0 ? "the default culture" : culture)}";
                using var message = request(literal, expected);
                if (culture.Length > 0) message.Headers.AcceptLanguage.ParseAdd(culture);

                using var response = await _client.SendAsync(message);
                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    failures.Add($"{where}: answered {(int)response.StatusCode} {at}: {Excerpt(body)}");
                    continue;
                }

                using var echo = JsonDocument.Parse(body);
                var ranUnder = echo.RootElement.GetProperty("culture").GetString();
                var type     = echo.RootElement.GetProperty("type").GetString();
                var bound    = echo.RootElement.GetProperty("value").GetRawText();

                if (ranUnder != culture)
                    failures.Add($"{where}: the handler ran under '{ranUnder}', not the request culture, {at}");
                else if (type is null)
                    failures.Add($"{where}: answered 200 having bound null {at}");
                else if (type != value.GetType().Name)
                    failures.Add($"{where}: bound a {type}, not a {value.GetType().Name}, {at}");
                // By value: ASP.NET Core writes with a relaxed encoder ('+' unescaped), the reference with the default one.
                else if (!JsonNode.DeepEquals(JsonNode.Parse(bound), JsonNode.Parse(expected)))
                    failures.Add($"{where}: answered {bound}, expected {expected}, {at}");
            }
        }

        return checks;
    }

    /// <summary>An invalid input: every entry point answers 400, under every request culture.</summary>
    private static async Task<int> Refuse(List<string> failures, string row, Type declared, string literal, string json, string what)
    {
        var index  = Declared.IndexOf(declared);
        var checks = 0;

        foreach (var (entryPoint, request) in EntryPoints(index))
        {
            if (StartupFailures.ContainsKey((declared, entryPoint))) continue;

            var where = $"{row} {Kind(declared)} {entryPoint} as {Display(declared)}";
            foreach (var culture in RequestCultures)
            {
                checks++;
                using var message = request(literal, json);
                if (culture.Length > 0) message.Headers.AcceptLanguage.ParseAdd(culture);

                using var response = await _client.SendAsync(message);
                if (response.StatusCode != HttpStatusCode.BadRequest)
                    failures.Add($"{where}: answered {(int)response.StatusCode} to {what} '{literal}' under {(culture.Length == 0 ? "the default culture" : culture)}, not 400: {Excerpt(await response.Content.ReadAsStringAsync())}");
            }
        }

        return checks;
    }

    // --- the host ---------------------------------------------------------------------------------

    /// <summary>The minimal-API endpoints for one declared type; a handler its factory refuses is recorded, not mapped.</summary>
    private static void MapMinimal<T>(WebApplication app, string prefix) where T : class
    {
        Map("minimal TryParse query",          "query",     (Delegate)(([FromQuery] T v) => Echo.Of(v)), HttpMethods.Get);
        Map("minimal TryParse nullable query", "nullable",  (Delegate)(([FromQuery] T? v) => Echo.Of(v)), HttpMethods.Get);
        Map("minimal TryParse route value",    "route/{v}", (Delegate)(([FromRoute] T v) => Echo.Of(v)), HttpMethods.Get);
        Map("minimal TryParse form field",     "form",      (Delegate)(([FromForm] T v) => Echo.Of(v)), HttpMethods.Post);
        Map("minimal JSON body",               "body",      (Delegate)(([FromBody] Body<T> body) => Echo.Of(body.Value)), HttpMethods.Post);

        void Map(string entryPoint, string route, Delegate handler, string method)
        {
            try
            {
                RequestDelegateFactory.Create(handler, new RequestDelegateFactoryOptions { ServiceProvider = app.Services, DisableInferBodyFromParameters = false });
            }
            catch (Exception e)
            {
                lock (StartupFailures) StartupFailures[(typeof(T), entryPoint)] = e.Message;
                return;
            }

            var endpoint = app.MapMethods($"{prefix}/{route}", [method], handler);
            if (method == HttpMethods.Post) endpoint.DisableAntiforgery();
        }
    }

    private static List<Type> DiscoverDeclared()
    {
        var types = new List<Type>();
        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
        {
            types.Add(rangeType);
            types.AddRange(rangeType.GetNestedTypes().Where(nested => nested.IsSubclassOf(rangeType)).OrderBy(nested => nested.Name, StringComparer.Ordinal));
            types.Add(typeof(RangeSet<,>).MakeGenericType(rangeType, elementType));
        }

        types.AddRange(SetProbes.AllSetTypes().Select(set => set.Set));
        return types;
    }

    // --- plumbing ---------------------------------------------------------------------------------

    private static string Literal(object value) => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);

    private static string Excerpt(string body) => body.Length <= 120 ? body : body[..120] + "…";

    private static string Display(Type type)
    {
        if (type.IsGenericType)
            return $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
        return type.IsNested ? $"{Display(type.DeclaringType!)}.{type.Name}" : type.Name;
    }
}

/// <summary>What every probe endpoint answers: the culture the handler ran under, the bound value's runtime type and the value itself.</summary>
public sealed record Echo<T>(string Culture, string? Type, T? Value);

public static class Echo
{
    public static Echo<T> Of<T>(T? value) => new(CultureInfo.CurrentCulture.Name, value?.GetType().Name, value);
}

/// <summary>A JSON request body with one property of the declared type, the shape a consumer's DTO has.</summary>
public sealed record Body<T>(T? Value);

/// <summary>The MVC side of <see cref="HostCultureProbeTests"/>: one closed controller per declared type, routed by <see cref="ProbeRouteConvention"/>.</summary>
[ApiController]
public sealed class ProbeController<T> : ControllerBase where T : class
{
    [HttpGet("query")]
    public ActionResult<Echo<T>> Query([FromQuery] T v) => Echo.Of(v);

    [HttpGet("nullable")]
    public ActionResult<Echo<T>> Nullable([FromQuery] T? v) => Echo.Of(v);

    [HttpGet("route/{v}")]
    public ActionResult<Echo<T>> Route([FromRoute] T v) => Echo.Of(v);

    [HttpPost("form")]
    public ActionResult<Echo<T>> Form([FromForm] T v) => Echo.Of(v);

    [HttpPost("body")]
    public ActionResult<Echo<T>> Body([FromBody] Body<T> body) => Echo.Of(body.Value);
}

/// <summary>Adds <see cref="ProbeController{T}"/> closed over each declared type; MVC discovers no open generic controller on its own.</summary>
public sealed class ProbeControllerFeatureProvider(IReadOnlyList<Type> declared) : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var type in declared)
            feature.Controllers.Add(typeof(ProbeController<>).MakeGenericType(type).GetTypeInfo());
    }
}

/// <summary>Routes each closed <see cref="ProbeController{T}"/> under its declared type's index.</summary>
public sealed class ProbeRouteConvention(List<Type> declared) : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (!controller.ControllerType.IsGenericType || controller.ControllerType.GetGenericTypeDefinition() != typeof(ProbeController<>)) return;

        var index = declared.IndexOf(controller.ControllerType.GenericTypeArguments[0]);
        controller.Selectors[0].AttributeRouteModel = new AttributeRouteModel(new RouteAttribute($"t{index}/mvc"));
    }
}

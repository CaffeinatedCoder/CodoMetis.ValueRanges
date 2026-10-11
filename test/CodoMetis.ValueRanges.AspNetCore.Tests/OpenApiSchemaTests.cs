using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CodoMetis.ValueRanges.AspNetCore.Tests;

/// <summary>
/// ASP.NET Core's OpenAPI generator cannot see through a custom JSON converter, so every range,
/// multirange and value set is described as <c>{}</c>. The documented
/// <see cref="ValueRangesSchemaTransformer"/> describes them as they are on the wire. These tests
/// generate a real document both ways, and hold the documented snippet to this project's copy.
/// </summary>
[TestClass]
public sealed class OpenApiSchemaTests
{
    private static async Task<JsonNode> GenerateDocument(Action<OpenApiOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenApi(configure);

        await using var app = builder.Build();
        app.MapOpenApi();
        app.MapPost("/booking", (Booking booking) => booking);
        app.MapGet("/period", (DateRange period) => period);
        await app.StartAsync();

        var json = await app.GetTestClient().GetStringAsync("/openapi/v1.json");
        return JsonNode.Parse(json)!;
    }

    /// <summary>Follows a <c>$ref</c> into <c>components/schemas</c>, or returns the inline schema.</summary>
    private static JsonNode Resolve(JsonNode document, JsonNode schema)
        => schema["$ref"]?.GetValue<string>() is { } reference
               ? document["components"]!["schemas"]![reference.Split('/')[^1]]!
               : schema;

    private static JsonNode BookingProperty(JsonNode document, string name)
    {
        var booking = Resolve(document, document["paths"]!["/booking"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!);
        return Resolve(document, booking["properties"]![name]!);
    }

    private static JsonNode QueryParameter(JsonNode document)
        => Resolve(document, document["paths"]!["/period"]!["get"]!["parameters"]![0]!["schema"]!);

    /// <summary>The finding the transformer exists for: without it a range is an unconstrained schema.</summary>
    [TestMethod]
    public async Task WithoutTheTransformer_ARangeIsDescribedAsAnything()
    {
        var document = await GenerateDocument(_ => { });

        var period = BookingProperty(document, "period");

        Assert.IsNull(period["type"], $"Expected the unconstrained {{}} schema, got {period.ToJsonString()}.");
        Assert.IsNull(period["pattern"]);
    }

    [TestMethod]
    public async Task ARange_IsAStringWithTheLiteralPattern()
    {
        var document = await GenerateDocument(o => o.AddSchemaTransformer<ValueRangesSchemaTransformer>());

        var period = BookingProperty(document, "period");

        Assert.AreEqual("string", period["type"]?.GetValue<string>(), period.ToJsonString());
        Assert.IsNotNull(period["pattern"], period.ToJsonString());
        StringAssert.Contains(period["description"]!.GetValue<string>(), "range literal over DateOnly");
    }

    /// <summary>
    /// A query, route or header parameter binds through <c>TryParse</c>, and ASP.NET Core describes
    /// any such parameter as a plain string itself — the schema transformer is not consulted for it,
    /// so it carries no pattern either way.
    /// </summary>
    [TestMethod]
    public async Task AQueryParameter_IsAString_WithOrWithoutTheTransformer()
    {
        foreach (var withTransformer in new[] { false, true })
        {
            var document = await GenerateDocument(o =>
            {
                if (withTransformer)
                    o.AddSchemaTransformer<ValueRangesSchemaTransformer>();
            });

            var parameter = QueryParameter(document);

            Assert.AreEqual("string", parameter["type"]?.GetValue<string>(), $"transformer={withTransformer}: {parameter.ToJsonString()}");
        }
    }

    [TestMethod]
    public async Task AMultirange_IsAStringWithTheMultirangePattern()
    {
        var document = await GenerateDocument(o => o.AddSchemaTransformer<ValueRangesSchemaTransformer>());

        var blocks = BookingProperty(document, "blocks");

        Assert.AreEqual("string", blocks["type"]?.GetValue<string>(), blocks.ToJsonString());
        StringAssert.Contains(blocks["description"]!.GetValue<string>(), "multirange literal over DateOnly");
    }

    [TestMethod]
    public async Task AValueSet_IsAnArrayOfUniqueElements()
    {
        var document = await GenerateDocument(o => o.AddSchemaTransformer<ValueRangesSchemaTransformer>());

        var tags = BookingProperty(document, "tags");

        Assert.AreEqual("array", tags["type"]?.GetValue<string>(), tags.ToJsonString());
        Assert.IsTrue(tags["uniqueItems"]?.GetValue<bool>(), tags.ToJsonString());
        Assert.AreEqual("string", Resolve(document, tags["items"]!)["type"]?.GetValue<string>(), tags.ToJsonString());
    }

    /// <summary>
    /// A pattern that rejects a literal the parser accepts would make a validating client refuse
    /// valid input. Every literal here parses; the pattern must accept all of them.
    /// </summary>
    [TestMethod]
    [DataRow("[1,10)")]
    [DataRow("(,5]")]
    [DataRow("[5,)")]
    [DataRow("(,)")]
    [DataRow("empty")]
    [DataRow(" EMPTY ")]
    [DataRow("[\"1\",\"10\"]")]
    [DataRow("[2026-01-01T09:30:00.0000000+01:00,2026-06-30T17:00:00.0000000+02:00)")]
    public void RangePattern_AcceptsWhatTheParserAccepts(string literal)
    {
        Assert.IsTrue(Int32Range.TryParse(literal, null, out _) || DateTimeOffsetRange.TryParse(literal, null, out _),
            $"Test data: '{literal}' should parse.");
        Assert.IsTrue(Regex.IsMatch(literal, RangePattern()), $"The schema pattern rejects '{literal}', which parses.");
    }

    [TestMethod]
    [DataRow("1,10")]
    [DataRow("")]
    [DataRow("{[1,5]}")]
    public void RangePattern_RejectsWhatIsNotARangeLiteral(string text)
        => Assert.IsFalse(Regex.IsMatch(text, RangePattern()), $"The schema pattern accepts '{text}'.");

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{[1,5],[7,10]}")]
    [DataRow(" { [1,5] } ")]
    public void MultirangePattern_AcceptsWhatTheParserAccepts(string literal)
    {
        Assert.IsTrue(RangeSet<Int32Range, int>.TryParse(literal, null, out _), $"Test data: '{literal}' should parse.");
        Assert.IsTrue(Regex.IsMatch(literal, MultirangePattern()), $"The schema pattern rejects '{literal}', which parses.");
    }

    private static string RangePattern()      => PatternOf("RangePattern");
    private static string MultirangePattern() => PatternOf("MultirangePattern");

    private static string PatternOf(string field)
        => (string)typeof(ValueRangesSchemaTransformer)
                  .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                  .GetRawConstantValue()!;

    /// <summary>
    /// docs/serialization.md carries the transformer for users to copy. This file is the copy that is
    /// compiled and tested; the documented one must be the same text, from its summary to its last brace.
    /// </summary>
    [TestMethod]
    public void DocumentedSnippet_IsThisFile()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CodoMetis.ValueRanges.slnx")))
            root = root.Parent;
        Assert.IsNotNull(root, "Repository root (CodoMetis.ValueRanges.slnx) not found above the test output.");

        static string Normalize(string text) => text.Replace("\r\n", "\n");

        var source = Normalize(File.ReadAllText(Path.Combine(
            root.FullName, "test", "CodoMetis.ValueRanges.AspNetCore.Tests", "ValueRangesSchemaTransformer.cs")));
        var snippet = source[source.IndexOf("/// <summary>", StringComparison.Ordinal)..].TrimEnd();
        var docs    = Normalize(File.ReadAllText(Path.Combine(root.FullName, "docs", "serialization.md")));

        Assert.IsTrue(docs.Contains(snippet, StringComparison.Ordinal),
            "docs/serialization.md no longer carries ValueRangesSchemaTransformer verbatim. Copy the class "
          + "from test/CodoMetis.ValueRanges.AspNetCore.Tests/ValueRangesSchemaTransformer.cs into the doc — "
          + "that copy is the one the tests compile and run.");
    }
}

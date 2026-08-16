using System.Text.Json;
using CodoMetis.ValueRanges.Serialization;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The parsers sit in the request path — SECURITY.md lists "denial of service through parsing" as
/// in scope — so this is the suite that backs that line: megabyte-scale malformed input must be
/// accepted or rejected in bounded time, <c>TryParse</c> must never throw, and a rejection must not
/// echo the whole input back in its message. Sizes are chosen so that a quadratic parser takes
/// minutes where a linear one takes milliseconds; the <see cref="TimeoutAttribute"/> is the
/// assertion on time.
/// </summary>
[TestClass]
public sealed class ParserResilienceTests
{
    private const int Size = 1_000_000;

    /// <summary>The most a rejection message may carry: an excerpt of the input plus its length, not the input.</summary>
    private const int MaxMessageLength = 512;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions().AddRangeConverters();

    // Each entry: a name, and the hostile text. Rejection is expected for most; a few are valid and
    // must simply not be slow (whitespace flood, huge but well-formed sets).
    public static IEnumerable<object[]> HostileRangeLiterals() =>
    [
        ["digit run of a million characters",       $"[{new string('1', Size)},2]"],
        ["a million opening brackets",              new string('[', Size)],
        ["a million characters of garbage bound",   $"[{new string('x', Size)},1]"],
        ["a million characters of whitespace",      $"{new string(' ', Size)}[1,2]"],
        ["a million quotes inside the bound",       $"[\"{new string('"', Size)},2]"],
        ["a million backslashes inside quotes",     $"[\"{new string('\\', Size)}\",2]"],
        ["a million commas",                        $"[{new string(',', Size)}]"],
    ];

    public static IEnumerable<object[]> HostileSetLiterals() =>
    [
        ["two hundred thousand ranges",             "{" + string.Join(",", Enumerable.Range(0, 200_000).Select(i => $"[{i * 2},{i * 2 + 1}]")) + "}"],
        ["two hundred thousand identical ranges",   "{" + string.Join(",", Enumerable.Repeat("[1,10]", 200_000)) + "}"],
        ["a million nested brackets",               "{" + new string('[', Size) + "}"],
        ["a million characters, never closed",      "{" + new string('[', Size)],
    ];

    public static IEnumerable<object[]> HostileArrayLiterals() =>
    [
        ["two hundred thousand elements",           "{" + string.Join(",", Enumerable.Range(0, 200_000).Select(i => $"e{i}")) + "}"],
        ["a million escaped quotes in one element", "{\"" + string.Concat(Enumerable.Repeat("\\\"", Size / 2)) + "\"}"],
        ["an unterminated quoted element",          "{\"" + new string('a', Size)],
        ["an unterminated escape",                  "{\"" + new string('a', Size) + "\\"],
        ["a million commas",                        "{" + new string(',', Size) + "}"],
        ["one element of a million characters",     "{" + new string('a', Size) + "}"],
    ];

    // ---- bounded time, documented failure modes ------------------------------------------------

    [TestMethod, DynamicData(nameof(HostileRangeLiterals)), Timeout(10_000)]
    public void RangeParse_HostileInput_IsAcceptedOrRejectedInBoundedTime(string name, string input)
    {
        AssertRejectedOrParsed(() => Int32Range.Parse(input, null));
        AssertRejectedOrParsed(() => DecimalRange.Parse(input, null));
        AssertRejectedOrParsed(() => DateTimeRange.Parse(input, null));
        Assert.IsFalse(Throws(() => Int32Range.TryParse(input, null, out _)),    "Int32Range.TryParse threw on hostile input.");
        Assert.IsFalse(Throws(() => DateTimeRange.TryParse(input, null, out _)), "DateTimeRange.TryParse threw on hostile input.");
    }

    [TestMethod, DynamicData(nameof(HostileSetLiterals)), Timeout(10_000)]
    public void RangeSetParse_HostileInput_IsAcceptedOrRejectedInBoundedTime(string name, string input)
    {
        AssertRejectedOrParsed(() => RangeSet<Int32Range, int>.Parse(input, null));
        Assert.IsFalse(Throws(() => RangeSet<Int32Range, int>.TryParse(input, null, out _)), "RangeSet.TryParse threw on hostile input.");
    }

    [TestMethod, DynamicData(nameof(HostileArrayLiterals)), Timeout(10_000)]
    public void SetParse_HostileInput_IsAcceptedOrRejectedInBoundedTime(string name, string input)
    {
        AssertRejectedOrParsed(() => StringSet.Parse(input, null));
        AssertRejectedOrParsed(() => Int32Set.Parse(input, null));
        Assert.IsFalse(Throws(() => StringSet.TryParse(input, null, out _)), "StringSet.TryParse threw on hostile input.");
        Assert.IsFalse(Throws(() => Int32Set.TryParse(input, null, out _)),  "Int32Set.TryParse threw on hostile input.");
    }

    [TestMethod, DynamicData(nameof(HostileRangeLiterals)), Timeout(10_000)]
    public void JsonRead_HostileRangeString_IsAcceptedOrRejectedInBoundedTime(string name, string input)
    {
        var payload = JsonSerializer.Serialize(input);
        AssertRejectedOrParsed(() => JsonSerializer.Deserialize<Int32Range>(payload, Json), typeof(JsonException));
        AssertRejectedOrParsed(() => JsonSerializer.Deserialize<RangeSet<Int32Range, int>>(payload, Json), typeof(JsonException));
    }

    [TestMethod, Timeout(10_000)]
    public void JsonRead_HostileSetPayloads_AreAcceptedOrRejectedInBoundedTime()
    {
        var hugeArray   = "[" + string.Join(",", Enumerable.Range(0, 200_000).Select(i => $"\"e{i}\"")) + "]";
        var hugeElement = "[\"" + new string('a', Size) + "\"]";
        var garbageInt  = "[\"" + new string('x', Size) + "\"]";
        var notAnArray  = "\"" + new string('x', Size) + "\"";

        AssertRejectedOrParsed(() => JsonSerializer.Deserialize<StringSet>(hugeArray, Json),   typeof(JsonException));
        AssertRejectedOrParsed(() => JsonSerializer.Deserialize<StringSet>(hugeElement, Json), typeof(JsonException));
        AssertRejectedOrParsed(() => JsonSerializer.Deserialize<Int32Set>(garbageInt, Json),   typeof(JsonException));
        AssertRejectedOrParsed(() => JsonSerializer.Deserialize<StringSet>(notAnArray, Json),  typeof(JsonException));
    }

    // ---- bounded messages -----------------------------------------------------------------------

    /// <summary>
    /// A rejection used to embed the entire input, and element failures chained the BCL's exception,
    /// whose message embeds it again — a megabyte in, a megabyte per log sink out. The excerpt in the
    /// message is what a reader needs to recognise the input; the rest is the attacker's.
    /// </summary>
    [TestMethod, Timeout(10_000)]
    public void Rejections_DoNotEchoTheWholeInput()
    {
        var cases = new (string Name, Func<object?> Parse)[]
        {
            ("range: unbalanced",         () => Int32Range.Parse(new string('[', Size), null)),
            ("range: garbage bound",      () => Int32Range.Parse($"[{new string('x', Size)},1]", null)),
            ("range: garbage date bound", () => DateTimeRange.Parse($"[{new string('x', Size)},1]", null)),
            ("range set: never closed",   () => RangeSet<Int32Range, int>.Parse("{" + new string('[', Size), null)),
            ("range set: bad element",    () => RangeSet<Int32Range, int>.Parse($"{{[1,2],[{new string('x', Size)},3]}}", null)),
            ("array: unterminated quote", () => StringSet.Parse("{\"" + new string('a', Size), null)),
            ("array: not a literal",      () => StringSet.Parse(new string('a', Size), null)),
            ("array: garbage int",        () => Int32Set.Parse("{1," + new string('x', Size) + "}", null)),
            ("json: range string",        () => JsonSerializer.Deserialize<Int32Range>(JsonSerializer.Serialize("[" + new string('x', Size) + ",1]"), Json)),
            ("json: range set string",    () => JsonSerializer.Deserialize<RangeSet<Int32Range, int>>(JsonSerializer.Serialize("{[" + new string('x', Size) + ",1]}"), Json)),
            ("json: int element",         () => JsonSerializer.Deserialize<Int32Set>("[\"" + new string('x', Size) + "\"]", Json)),
        };

        foreach (var (name, parse) in cases)
        {
            var exception = Catch(parse);

            Assert.IsNotNull(exception, $"{name}: expected a rejection, but the input parsed.");

            for (var e = exception; e is not null; e = e.InnerException)
            {
                Assert.IsTrue(
                    e.Message.Length <= MaxMessageLength,
                    $"{name}: {e.GetType().Name} message is {e.Message.Length:N0} characters long — it echoes the input. "
                  + $"A rejection message must carry an excerpt, not the payload (max {MaxMessageLength}).");
            }
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static void AssertRejectedOrParsed(Func<object?> parse, Type? alsoAllowed = null)
    {
        var exception = Catch(parse);
        if (exception is null) return;

        var allowed = exception is FormatException or OverflowException || (alsoAllowed?.IsInstanceOfType(exception) ?? false);

        Assert.IsTrue(
            allowed,
            $"Hostile input failed with {exception.GetType().Name}, not a documented rejection "
          + $"(FormatException, OverflowException{(alsoAllowed is null ? "" : $", {alsoAllowed.Name}")}): {exception.Message[..Math.Min(200, exception.Message.Length)]}");
    }

    private static Exception? Catch(Func<object?> action)
    {
        try { action(); return null; }
        catch (Exception e) { return e; }
    }

    private static bool Throws(Action action) => Catch(() => { action(); return null; }) is not null;
}

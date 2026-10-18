using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;
using NodaTime;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every value set reads whitespace inside a quoted or escaped element as the PostgreSQL input
/// function behind its column does: stripped for the numeric and temporal types, kept for
/// <c>text</c>, rejected for <c>uuid</c>. Swept through <c>Parse</c>, <c>TryParse</c>, the
/// <c>TypeConverter</c> and, for the temporal families, a JSON element.
/// </summary>
/// <remarks>
/// <para>
/// <c>array_in</c> drops whitespace around a bare element itself and hands quoted or escaped
/// whitespace to the element's input function. Measured on PostgreSQL 17 (#70): <c>int2in</c>,
/// <c>int4in</c>, <c>int8in</c>, <c>numeric_in</c>, <c>date_in</c>, <c>timestamp_in</c>,
/// <c>timestamptz_in</c> and <c>time_in</c> strip space, tab, LF, CR, VT and FF from both ends and
/// nothing else; <c>textin</c> keeps everything; <c>uuid_in</c> rejects all of it. Until 9.1.0 the
/// temporal families handed the whitespace to the strict ISO grammar, so <c>{" 2026-01-01"}</c>
/// threw. <c>LiteralGrammarParityTests</c> in the integration project asks a live server.
/// </para>
/// <para>
/// A trim in the shared array parser would strip a <c>text[]</c> element's own spaces, silently — the
/// string rows are what catch that. Until 10.0.0 <c>GuidSet</c> accepted whitespace <c>uuid_in</c>
/// rejects, because <c>Guid.Parse</c> trims it (#78); the Guid rows are what hold the fix.
/// </para>
/// </remarks>
[TestClass]
public sealed class SetElementWhiteSpaceContractTests
{
    private enum InputFunction
    {
        /// <summary>Strips ASCII whitespace from both ends: the numeric and temporal input functions.</summary>
        Strips,

        /// <summary>Keeps every character: <c>textin</c>.</summary>
        Keeps,

        /// <summary>Rejects any whitespace: <c>uuid_in</c>.</summary>
        Rejects
    }

    /// <summary>What the PostgreSQL input function behind each primitive does with surrounding whitespace.</summary>
    private static readonly Dictionary<Type, InputFunction> Behaviour = new()
    {
        [typeof(string)]         = InputFunction.Keeps,
        [typeof(Guid)]           = InputFunction.Rejects,
        [typeof(short)]          = InputFunction.Strips,
        [typeof(int)]            = InputFunction.Strips,
        [typeof(long)]           = InputFunction.Strips,
        [typeof(decimal)]        = InputFunction.Strips,
        [typeof(double)]         = InputFunction.Strips,
        [typeof(float)]          = InputFunction.Strips,
        [typeof(DateOnly)]       = InputFunction.Strips,
        [typeof(DateTime)]       = InputFunction.Strips,
        [typeof(DateTimeOffset)] = InputFunction.Strips,
        [typeof(TimeOnly)]       = InputFunction.Strips,
        [typeof(LocalDate)]      = InputFunction.Strips,
        [typeof(LocalDateTime)]  = InputFunction.Strips,
        [typeof(Instant)]        = InputFunction.Strips,
        [typeof(LocalTime)]      = InputFunction.Strips,
        [typeof(YearMonth)]      = InputFunction.Strips
    };

    /// <summary>The primitives whose JSON element is text read through the literal grammar.</summary>
    private static readonly HashSet<Type> Temporal =
    [
        typeof(DateOnly), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeOnly),
        typeof(LocalDate), typeof(LocalDateTime), typeof(Instant), typeof(LocalTime), typeof(YearMonth)
    ];

    /// <summary>The whitespace PostgreSQL's input functions strip: <c>isspace</c> in the C locale.</summary>
    private static readonly string[] AsciiWhiteSpace = [" ", "\t", "\n", "\r", "\v", "\f"];

    /// <summary>15 closed set types and 15 wrapper arities (10 + 10 core, 5 + 5 NodaTime), as of 9.1.0.</summary>
    private const int KnownSetTypes = 30;

    private const string Header = "A value set element's surrounding whitespace is read differently from PostgreSQL's input function";

    [TestMethod]
    public void EverySetType_ReadsElementWhitespaceAsItsInputFunctionDoes()
    {
        var swept    = new List<string>();
        var failures = new List<string>();

        var primitives = SetProbes.AllSetTypes()
                                  .Where(pair => !pair.Set.IsGenericType)
                                  .ToDictionary(pair => pair.Set.Name, pair => pair.Element);

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            // A wrapper arity is backed by the primitive of its closed sibling: DateSet<T> by DateOnly.
            var primitive = primitives.GetValueOrDefault(setType.Name.Split('`')[0]) ?? elementType;
            var label     = setType.IsGenericType ? $"{setType.Name.Split('`')[0]}<{elementType.Name}>" : setType.Name;

            if (!Behaviour.TryGetValue(primitive, out var behaviour))
            {
                failures.Add($"  {label}: no measured PostgreSQL behaviour for {primitive.Name}; measure it and add it to {nameof(Behaviour)}.");
                continue;
            }

            failures.AddRange(Reflect.InvokeGeneric<IEnumerable<string>>(
                typeof(SetElementWhiteSpaceContractTests), nameof(Failures), [setType, elementType],
                label, behaviour, Temporal.Contains(primitive)));
            swept.Add(label);
        }

        Assert.IsTrue(swept.Count >= KnownSetTypes,
            $"Only {swept.Count} set types were swept, fewer than the {KnownSetTypes} known: {string.Join(", ", swept)}.");
        Candidates.Verdict(nameof(SetElementWhiteSpaceContractTests), failures, Header);
    }

    private static IEnumerable<string> Failures<TSet, T>(string label, InputFunction behaviour, bool temporal)
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var failures = new List<string>();
        var element  = SetProbes.For<T>()[0];
        var text     = TSet.FormatValue(element, null, CultureInfo.InvariantCulture);
        var plain    = StringOverloads.Parse<TSet>($"{{\"{Escape(text)}\"}}", CultureInfo.InvariantCulture);

        foreach (var ws in AsciiWhiteSpace)
        {
            foreach (var (literal, raw) in new[]
                     {
                         ($"{{\"{ws}{Escape(text)}{ws}\"}}", $"{ws}{text}{ws}"),
                         ($"{{\\{ws}{EscapeEach(text)}}}", $"{ws}{text}"),
                         ($"{{{EscapeEach(text)}\\{ws}}}", $"{text}{ws}")
                     })
            {
                switch (behaviour)
                {
                    case InputFunction.Strips:
                        Reads(literal, plain);
                        break;
                    case InputFunction.Keeps:
                        Kept(literal, raw);
                        break;
                    case InputFunction.Rejects:
                        Rejected(literal);
                        break;
                }
            }

            // A JSON element of a temporal set is read through the same element grammar.
            if (temporal)
            {
                var json = $"[{JsonSerializer.Serialize($"{ws}{text}{ws}")}]";
                Expect(literal: json, path: "JSON", expected: plain, () => JsonSerializer.Deserialize<TSet>(json)!);
            }

            // Whitespace alone is an element of whitespace: text for a text column, nothing else.
            if (behaviour == InputFunction.Keeps)
                Kept($"{{\"{ws}\"}}", ws);
            else
                Rejected($"{{\"{ws}\"}}", whitespaceOnly: true);
        }

        // Only ASCII whitespace is stripped: a no-break space is element text, as in PostgreSQL.
        if (behaviour == InputFunction.Keeps)
            Reads($"{{\" {Escape(text)}\"}}", TSet.From([TSet.ParseValue($" {text}", CultureInfo.InvariantCulture)]));
        else
            Rejected($"{{\" {Escape(text)}\"}}");

        return failures;

        // The whitespace reaches the element's own parser unchanged: a validated wrapper may still
        // reject it, which is then the answer every entry point must give.
        void Kept(string literal, string raw)
        {
            TSet expected;
            try
            {
                expected = TSet.From([TSet.ParseValue(raw, CultureInfo.InvariantCulture)]);
            }
            catch (FormatException)
            {
                Rejected(literal, whitespaceOnly: true);
                return;
            }

            Reads(literal, expected);
        }

        void Reads(string literal, TSet expected)
        {
            Expect(literal, "Parse", expected, () => StringOverloads.Parse<TSet>(literal, CultureInfo.InvariantCulture));
            Expect(literal, "TryParse", expected,
                () => StringOverloads.TryParse<TSet>(literal, CultureInfo.InvariantCulture, out var tried) ? tried : throw new FormatException("TryParse returned false."));
            Expect(literal, "TypeConverter", expected,
                () => (TSet)TypeDescriptor.GetConverter(typeof(TSet)).ConvertFromInvariantString(literal)!);
        }

        void Expect(string literal, string path, TSet expected, Func<TSet> read)
        {
            try
            {
                var actual = read();
                if (!Equals(actual, expected))
                    failures.Add($"  {label}: {path} read {Show(literal)} as {Show(actual.ToString()!)}, expected {Show(expected.ToString()!)}.");
            }
            catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
            {
                failures.Add($"  {label}: {path} rejected {Show(literal)}, expected {Show(expected.ToString()!)}: {ex.Message}");
            }
        }

        void Rejected(string literal, bool whitespaceOnly = false)
        {
            // The stable prefix a candidate row can match: the family and the kind of disagreement.
            var kind = behaviour == InputFunction.Rejects && !whitespaceOnly
                ? "accepts whitespace uuid_in rejects"
                : "accepts an element its input function rejects";

            if (Accepted(() => StringOverloads.Parse<TSet>(literal, CultureInfo.InvariantCulture)) is { } parsed)
                failures.Add($"  {label}: {kind}: Parse read {Show(literal)} as {Show(parsed)}.");
            if (StringOverloads.TryParse<TSet>(literal, CultureInfo.InvariantCulture, out var tried))
                failures.Add($"  {label}: {kind}: TryParse read {Show(literal)} as {Show(tried.ToString()!)}.");
            if (Accepted(() => (TSet)TypeDescriptor.GetConverter(typeof(TSet)).ConvertFromInvariantString(literal)!) is { } converted)
                failures.Add($"  {label}: {kind}: TypeConverter read {Show(literal)} as {Show(converted)}.");
        }
    }

    private static string? Accepted<TSet>(Func<TSet> read)
    {
        try
        {
            return read()!.ToString();
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The text inside a quoted element: a quote or backslash is escaped.</summary>
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>The text as an unquoted element with every character escaped.</summary>
    private static string EscapeEach(string text) => string.Concat(text.Select(c => $"\\{c}"));

    private static string Show(string text)
        => string.Concat(text.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString()));
}

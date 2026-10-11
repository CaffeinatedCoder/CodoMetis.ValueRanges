using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every range type reads a bound as PostgreSQL's <c>range_in</c> does: only an <em>absent</em> bound
/// is infinite; a quoted <c>""</c> or a bound of only whitespace is a value — one no element type here
/// accepts; quotes may wrap any part of a bound and a backslash escapes the next character, inside
/// quotes or out. Swept through <c>Parse</c>, <c>TryParse</c>, the span overload, JSON, the
/// <c>TypeConverter</c> and the multirange literal.
/// </summary>
/// <remarks>
/// <para>
/// Until 9.0.0 the parser unquoted each bound and then asked whether it was empty, so
/// <c>Int32Range.Parse("[\"\",5)")</c> was <c>(,4]</c> and <c>("","")</c> was <c>(,)</c> — silently,
/// where PostgreSQL answers <c>invalid input syntax for type integer: ""</c>. A bound of whitespace
/// was trimmed to nothing the same way. Accepting those later and then refusing them would be a
/// breaking change, which is why they are refused from the first release that reads literals strictly.
/// </para>
/// <para>
/// The behaviour mirrors the probes run against PostgreSQL 17 (<c>["",5)</c>, <c>[ ,5)</c>,
/// <c>[1, )</c> refused; <c>[\1,5)</c> and <c>["1"2,5)</c> read as <c>1</c> and <c>12</c>);
/// <c>LiteralGrammarParityTests</c> in the integration project asks a live server.
/// </para>
/// </remarks>
[TestClass]
public sealed class RangeLiteralGrammarContractTests
{
    /// <summary>11 range types as of 9.0.0 (7 core, 4 NodaTime).</summary>
    private const int KnownRangeTypes = 11;

    [TestMethod]
    public void EveryRangeType_ReadsBoundsWithPostgresRangeGrammar()
    {
        var swept    = new List<string>();
        var failures = new List<string>();

        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
        {
            failures.AddRange(Reflect.InvokeGeneric<IEnumerable<string>>(
                typeof(RangeLiteralGrammarContractTests), nameof(Failures), rangeType, elementType));
            swept.Add(rangeType.Name);
        }

        Assert.IsTrue(swept.Count >= KnownRangeTypes,
            $"Only {swept.Count} range types were swept, fewer than the {KnownRangeTypes} known: {string.Join(", ", swept)}.");
        Assert.IsEmpty(failures, "A range literal is read differently from PostgreSQL's range_in:\n" + string.Join("\n", failures));
    }

    private static IEnumerable<string> Failures<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var failures = new List<string>();
        var (low, high) = ((T)RangeProbes.Bounds[typeof(T)].Low, (T)RangeProbes.Bounds[typeof(T)].High);
        var lo = TRange.FormatValue(low,  null, CultureInfo.InvariantCulture);
        var hi = TRange.FormatValue(high, null, CultureInfo.InvariantCulture);

        // A quoted empty bound, or one of only whitespace, is present — and no element type here reads it.
        foreach (var literal in new[]
                 {
                     $"[\"\",{hi})", $"[{lo},\"\")", "(\"\",\"\")", $"[ ,{hi})", $"[{lo}, )", "( , )", "(\"\",)", "(,\"\")"
                 })
        {
            Refused(literal);
        }

        // Only an absent bound is infinite — a guard against refusing too much.
        Reads($"(,{hi})", TRange.CreateUnboundedStart(high, endInclusive: false));
        Reads($"[{lo},)", TRange.CreateUnboundedEnd(low, startInclusive: true));
        Reads("(,)", TRange.Infinite);

        // Backslash escapes the next character outside quotes too, and quotes may wrap part of a bound.
        var finite = TRange.CreateFinite(low, high, startInclusive: true, endInclusive: false);
        Reads($"[{EscapeEach(lo)},{EscapeEach(hi)})", finite);
        Reads($"[\"{lo[..1]}\"{lo[1..]},{hi[..1]}\"{hi[1..]}\")", finite);
        Reads($"[\"{EscapeEach(lo)}\",\"{hi}\")", finite);

        // Whitespace around an unquoted bound is not part of it for any element type here.
        Reads($"[ {lo} , {hi} )", finite);

        // Structure PostgreSQL refuses: a second comma, a closing bracket inside a bound, an
        // unterminated quote or escape, Unicode whitespace where only ASCII whitespace is skipped.
        foreach (var literal in new[]
                 {
                     $"[{lo},{hi},{hi})", $"[{lo}),{hi})", $"[{lo}],{hi})", $"[\"{lo},{hi})", $"[{lo},{hi}\\)",
                     // Only ASCII whitespace is skipped, as in PostgreSQL: a no-break space is bound text.
                     $"[\u00A0{lo},{hi})", $"\u00A0[{lo},{hi})"
                 })
        {
            Refused(literal);
        }

        return failures;

        void Refused(string literal)
        {
            var label = $"{typeof(TRange).Name} {literal}";

            Expect<FormatException>(label, "Parse", () => TRange.Parse(literal, CultureInfo.InvariantCulture));
            Expect<FormatException>(label, "Parse(span)", () => TRange.Parse(literal.AsSpan(), CultureInfo.InvariantCulture));
            if (TRange.TryParse(literal, CultureInfo.InvariantCulture, out var tried))
                failures.Add($"  {label}: TryParse read {tried}.");
            Expect<JsonException>(label, "JSON", () => JsonSerializer.Deserialize<TRange>(JsonSerializer.Serialize(literal))!);
            Expect<FormatException>(label, "TypeConverter",
                () => (TRange)TypeDescriptor.GetConverter(typeof(TRange)).ConvertFromInvariantString(literal)!);
            Expect<FormatException>(label, "RangeSet.Parse",
                () => RangeSet<TRange, T>.Parse($"{{[{lo},{hi}),{literal}}}", CultureInfo.InvariantCulture));
        }

        void Expect<TException>(string label, string path, Func<object> read) where TException : Exception
        {
            try
            {
                failures.Add($"  {label}: {path} read {read()}.");
            }
            catch (TException)
            {
            }
            catch (Exception ex)
            {
                failures.Add($"  {label}: {path} threw {ex.GetType().Name}, not {typeof(TException).Name}: {ex.Message}");
            }
        }

        void Reads(string literal, TRange expected)
        {
            var label = $"{typeof(TRange).Name} {literal}";

            try
            {
                var actual = TRange.Parse(literal, CultureInfo.InvariantCulture);
                if (!Equals(actual, expected))
                    failures.Add($"  {label}: Parse read {actual}, expected {expected}.");

                var set = RangeSet<TRange, T>.Parse($"{{{literal}}}", CultureInfo.InvariantCulture);
                if (!Equals(set, RangeSet<TRange, T>.From([expected])))
                    failures.Add($"  {label}: RangeSet.Parse read {set}, expected {{{expected}}}.");
            }
            catch (FormatException ex)
            {
                failures.Add($"  {label}: refused, expected {expected}: {ex.Message}");
            }
        }
    }

    private static string EscapeEach(string text) => string.Concat(text.Select(c => $"\\{c}"));
}

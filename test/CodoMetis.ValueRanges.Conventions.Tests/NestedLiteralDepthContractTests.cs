using System.ComponentModel;
using System.Globalization;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Oracles;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// A value set literal nested deeper than PostgreSQL's six dimensions (MAXDIM) is refused by every
/// set type, through every entry point that reads a literal, and refused rather than read: PostgreSQL
/// rejects it on every version.
/// </summary>
/// <remarks>
/// Until 10.0.0 shipped, the nested-empty walk (#128) recursed once per brace with no limit. A nest of
/// empty arrays of any depth read as <c>{}</c> (#250), and a few thousand braces overflowed the stack,
/// which .NET cannot catch: a 24 KB <c>[FromHeader] Int32Set</c> header killed a Kestrel process (#248).
/// The depths fed here are bounded so that the old walk answers rather than crashes the test host: at a
/// thousand braces it read the nest as <c>{}</c>, which is what fails. 100,000 braces were checked by
/// hand against the capped walk.
/// </remarks>
[TestClass]
public sealed class NestedLiteralDepthContractTests
{
    private const int KnownSetTypes = 28;

    /// <summary>Seven levels, PostgreSQL's MAXDIM plus one, and two deeper nests, empty and holding an element.</summary>
    private static readonly string[] TooDeep =
    [
        "{{{{{{{}}}}}}}",
        "{{{{{{{},{}}}}}}}",
        new string('{', 20) + new string('}', 20),
        new string('{', 1000) + new string('}', 1000),
        new string('{', 1000) + "1" + new string('}', 1000)
    ];

    [TestMethod]
    public void EverySetType_RefusesALiteralDeeperThanSixDimensions_OnEveryEntryPoint()
    {
        var failures = new List<string>();
        var covered  = 0;

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            Reflect.InvokeGeneric<bool>(typeof(NestedLiteralDepthContractTests), nameof(Check), new[] { setType, elementType }, failures);
            covered++;
        }

        Assert.IsGreaterThanOrEqualTo(KnownSetTypes, covered, $"Found {covered} set types, fewer than {KnownSetTypes}.");
        Assert.IsEmpty(failures, "A literal nested deeper than PostgreSQL's six dimensions was read:\n" + string.Join("\n", failures));
    }

    private static bool Check<TSet, TElement>(List<string> failures)
        where TSet : class, IValueSetFactory<TSet, TElement>, IValueSet<TElement>, ISpanParsable<TSet>
        where TElement : IEquatable<TElement>
    {
        var converter = TypeDescriptor.GetConverter(typeof(TSet));
        foreach (var literal in TooDeep)
        {
            var shown = literal.Length > 40 ? $"{literal.Length / 2}-deep nest" : literal;
            foreach (var (leg, read) in new (string, Func<bool>)[]
                     {
                         ("Parse(string)", () => Reads(() => StringOverloads.Parse<TSet>(literal, CultureInfo.InvariantCulture))),
                         ("TryParse(string)", () => StringOverloads.TryParse<TSet>(literal, CultureInfo.InvariantCulture, out _)),
                         ("TryParse(span)", () => TSet.TryParse(literal.AsSpan(), CultureInfo.InvariantCulture, out _)),
                         ("TypeConverter", () => Reads(() => converter.ConvertFromInvariantString(literal)!))
                     })
            {
                if (read()) failures.Add($"  {typeof(TSet).Name} {leg} reads {shown}");
            }
        }

        return true;
    }

    private static bool Reads(Func<object> parse)
    {
        try
        {
            parse();
            return true;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}

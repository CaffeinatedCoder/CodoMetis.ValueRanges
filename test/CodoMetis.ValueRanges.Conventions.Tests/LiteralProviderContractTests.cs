using System.Globalization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The literal is a wire format: no <see cref="IFormatProvider"/> may change it. Formatting without
/// a format string writes the invariant literal whatever provider is passed, and parsing reads the
/// invariant literal whatever provider is passed — for every shape of every range type, every
/// multirange and every value set family.
/// </summary>
/// <remarks>
/// Before 9.0.0 the provider reached the element parser and formatter. <c>th-TH</c> uses the
/// Buddhist calendar, so <c>DateRange.Parse("[2026-01-01,2026-06-30]", th-TH)</c> was
/// <c>[1483-01-01,1483-06-30]</c> and <c>ToString(null, th-TH)</c> wrote <c>2569</c>; an explicit
/// <c>de-DE</c> provider read the decimal <c>1.5</c> as fifteen. None of it threw. A provider still
/// applies alongside an explicit format string, which asks for display text rather than a literal.
/// <para>
/// An empty format string is not an explicit one: .NET treats <c>""</c> exactly as <see langword="null"/>
/// (the general format), so both are swept. The first cut of this rule checked only
/// <c>format is null</c>, and <c>ToString("", de-DE)</c> on <c>DecimalRange [1.5,2)</c> wrote
/// <c>[1,5,2)</c> — the pre-9.0 bug, reachable through any API that passes an empty format rather
/// than none — while <c>DateRange</c>'s <c>format ?? "yyyy-MM-dd"</c> let <c>""</c> through to
/// the short-date pattern.
/// </para>
/// </remarks>
[TestClass]
public sealed class LiteralProviderContractTests
{
    private static readonly CultureInfo[] Providers =
        [.. new[] { "de-DE", "sv-SE", "fa-IR", "th-TH", "ar-SA" }.Select(CultureInfo.GetCultureInfo)];

    /// <summary>The two spellings of "no format" — both must write the literal.</summary>
    private static readonly string?[] NoFormat = [null, ""];

    [TestMethod]
    public void EveryRangeShape_IgnoresTheProvider_ForTheLiteral()
    {
        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
            Reflect.InvokeGeneric(typeof(LiteralProviderContractTests), nameof(AssertRangeShapes), rangeType, elementType);
    }

    private static void AssertRangeShapes<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        foreach (var range in RangeProbes.Shapes<TRange, T>())
        {
            var literal = ((IFormattable)range).ToString(null, CultureInfo.InvariantCulture);
            var set     = RangeSet<TRange, T>.From([range]);
            var setText = set.ToString(null, CultureInfo.InvariantCulture);
            var label   = $"{typeof(TRange).Name}.{range.GetType().Name} {literal}";

            foreach (var provider in Providers)
            {
                foreach (var format in NoFormat)
                {
                    Assert.AreEqual(literal, ((IFormattable)range).ToString(format, provider),
                        $"{label}: ToString({Show(format)}, {provider.Name}) is not the invariant literal.");
                    Assert.AreEqual(setText, set.ToString(format, provider),
                        $"{label} as RangeSet: ToString({Show(format)}, {provider.Name}) is not the invariant literal.");
                }

                Assert.AreEqual(range, TRange.Parse(literal, provider),
                    $"{label}: Parse(literal, {provider.Name}) read a different range.");
                Assert.IsTrue(TRange.TryParse(literal, provider, out var tried) && Equals(tried, range),
                    $"{label}: TryParse(literal, {provider.Name}) read a different range.");
                Assert.AreEqual(range, TRange.Parse(literal.AsSpan(), provider),
                    $"{label}: Parse(span, {provider.Name}) read a different range.");

                Assert.AreEqual(set, RangeSet<TRange, T>.Parse(setText, provider),
                    $"{label} as RangeSet: Parse(literal, {provider.Name}) read a different set.");
            }
        }
    }

    [TestMethod]
    public void EverySetType_IgnoresTheProvider_ForTheLiteral()
    {
        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
            Reflect.InvokeGeneric(typeof(LiteralProviderContractTests), nameof(AssertSet), setType, elementType);
    }

    private static void AssertSet<TSet, T>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        var set     = TSet.From(SetProbes.For<T>());
        var literal = set.ToString(null, CultureInfo.InvariantCulture);

        foreach (var provider in Providers)
        {
            foreach (var format in NoFormat)
            {
                Assert.AreEqual(literal, set.ToString(format, provider),
                    $"{typeof(TSet).Name}: ToString({Show(format)}, {provider.Name}) is not the invariant literal.");
            }

            Assert.AreEqual(set, TSet.Parse(literal, provider),
                $"{typeof(TSet).Name}: Parse(literal, {provider.Name}) read a different set.");
        }
    }

    /// <summary>
    /// <c>FormatValue</c> is public and reached directly as well as through the literal, so it must
    /// read <c>""</c> as its own default too — not hand it to the element, where a
    /// <c>format ?? "yyyy-MM-dd"</c> lets it through to the culture's short-date pattern.
    /// </summary>
    [TestMethod]
    public void EveryFormatValue_ReadsAnEmptyFormat_AsItsDefault()
    {
        var swept    = 0;
        var failures = new List<string>();

        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
        {
            failures.AddRange(Reflect.InvokeGeneric<IEnumerable<string>>(
                typeof(LiteralProviderContractTests), nameof(RangeFormatValueFailures), rangeType, elementType));
            swept++;
        }

        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
        {
            failures.AddRange(Reflect.InvokeGeneric<IEnumerable<string>>(
                typeof(LiteralProviderContractTests), nameof(SetFormatValueFailures), setType, elementType));
            swept++;
        }

        Assert.IsTrue(swept >= 30, $"The sweep discovered only {swept} types — too few to mean anything.");
        Assert.IsEmpty(failures,
            "FormatValue reads an empty format as an explicit one:\n" + string.Join("\n", failures));
    }

    private static IEnumerable<string> RangeFormatValueFailures<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
        => FormatValueFailures(
            typeof(TRange).Name,
            [(T)RangeProbes.Bounds[typeof(T)].Low, (T)RangeProbes.Bounds[typeof(T)].High],
            TRange.FormatValue);

    private static IEnumerable<string> SetFormatValueFailures<TSet, T>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
        => FormatValueFailures(typeof(TSet).Name, SetProbes.For<T>(), TSet.FormatValue);

    private static List<string> FormatValueFailures<T>(
        string label, IEnumerable<T> values, Func<T, string?, IFormatProvider?, string> formatValue)
    {
        var failures = new List<string>();

        foreach (var value in values)
        {
            var none  = formatValue(value, null, CultureInfo.InvariantCulture);
            var empty = formatValue(value, "", CultureInfo.InvariantCulture);

            if (none != empty)
                failures.Add($"  {label}: null -> '{none}', \"\" -> '{empty}'");
        }

        return failures;
    }

    private static string Show(string? format) => format is null ? "null" : $"\"{format}\"";
}

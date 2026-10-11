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
/// </remarks>
[TestClass]
public sealed class LiteralProviderContractTests
{
    private static readonly CultureInfo[] Providers =
        [.. new[] { "de-DE", "sv-SE", "fa-IR", "th-TH", "ar-SA" }.Select(CultureInfo.GetCultureInfo)];

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
                Assert.AreEqual(literal, ((IFormattable)range).ToString(null, provider),
                    $"{label}: ToString(null, {provider.Name}) is not the invariant literal.");
                Assert.AreEqual(range, TRange.Parse(literal, provider),
                    $"{label}: Parse(literal, {provider.Name}) read a different range.");
                Assert.IsTrue(TRange.TryParse(literal, provider, out var tried) && Equals(tried, range),
                    $"{label}: TryParse(literal, {provider.Name}) read a different range.");
                Assert.AreEqual(range, TRange.Parse(literal.AsSpan(), provider),
                    $"{label}: Parse(span, {provider.Name}) read a different range.");

                Assert.AreEqual(setText, set.ToString(null, provider),
                    $"{label} as RangeSet: ToString(null, {provider.Name}) is not the invariant literal.");
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
            Assert.AreEqual(literal, set.ToString(null, provider),
                $"{typeof(TSet).Name}: ToString(null, {provider.Name}) is not the invariant literal.");
            Assert.AreEqual(set, TSet.Parse(literal, provider),
                $"{typeof(TSet).Name}: Parse(literal, {provider.Name}) read a different set.");
        }
    }
}

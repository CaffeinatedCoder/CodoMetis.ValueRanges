using System.Globalization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The literal must not depend on the current culture. <c>ToString(null, null)</c> — what string
/// interpolation, <c>string.Format</c> and logging renderers call — must produce exactly the
/// invariant literal for every shape of every range type, every multirange and every value set.
/// </summary>
/// <remarks>
/// Before 9.0.0 the three range types without a <c>FormatValue</c> override (<c>Int32Range</c>,
/// <c>Int64Range</c>, <c>DecimalRange</c>) formatted a null provider in the current culture.
/// Under <c>de-DE</c>, <c>$"{DecimalRange [1.5,2)}"</c> was <c>[1,5,2)</c>, which the parser reads
/// back as <c>[1,52)</c> — a different range, silently. Under <c>sv-SE</c> a negative bound took
/// U+2212 as its minus sign and no longer parsed.
/// </remarks>
[TestClass]
public sealed class LiteralCultureContractTests
{
    /// <summary>Comma decimal separator; U+2212 minus; Arabic decimal separator and bidi marks; a non-Gregorian calendar.</summary>
    private static readonly string[] Cultures = ["de-DE", "sv-SE", "fa-IR", "th-TH"];

    [TestMethod]
    public void EveryRangeShape_FormatsTheInvariantLiteral_InAnyCurrentCulture()
    {
        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
            Reflect.InvokeGeneric(typeof(LiteralCultureContractTests), nameof(AssertRangeShapes), rangeType, elementType);
    }

    private static void AssertRangeShapes<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        foreach (var range in RangeProbes.Shapes<TRange, T>())
        {
            AssertCultureFree($"{typeof(TRange).Name}.{range.GetType().Name}", (IFormattable)range);
            AssertCultureFree($"RangeSet<{typeof(TRange).Name}>", RangeSet<TRange, T>.From([range]));
        }
    }

    [TestMethod]
    public void EverySetType_FormatsTheInvariantLiteral_InAnyCurrentCulture()
    {
        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
            Reflect.InvokeGeneric(typeof(LiteralCultureContractTests), nameof(AssertSet), setType, elementType);
    }

    private static void AssertSet<TSet, T>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
        => AssertCultureFree(typeof(TSet).Name, TSet.From(SetProbes.For<T>()));

    private static void AssertCultureFree(string label, IFormattable value)
    {
        var literal  = value.ToString(null, CultureInfo.InvariantCulture);
        var original = CultureInfo.CurrentCulture;

        foreach (var culture in Cultures)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            try
            {
                Assert.AreEqual(literal, value.ToString(null, null),
                    $"{label} under {culture}: ToString(null, null) — what $\"{{value}}\" calls — is not the "
                  + "invariant literal. A FormatValue is formatting a null provider in the current culture.");
                Assert.AreEqual(literal, value.ToString(),
                    $"{label} under {culture}: ToString() is not the invariant literal.");
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}

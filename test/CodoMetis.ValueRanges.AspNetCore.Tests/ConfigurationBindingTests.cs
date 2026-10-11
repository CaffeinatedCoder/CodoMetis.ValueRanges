using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace CodoMetis.ValueRanges.AspNetCore.Tests;

/// <summary>
/// <c>Microsoft.Extensions.Configuration</c> binding goes through <c>TypeDescriptor</c>. Before
/// 9.0.0 no type here had a <c>TypeConverter</c>, and binding <c>"Period": "[2026-01-01,2026-06-30]"</c>
/// into a <c>DateRange</c> property produced <see langword="null"/> — no exception, no warning.
/// </summary>
[TestClass]
public sealed class ConfigurationBindingTests
{
    public sealed class Settings
    {
        public DateRange?                   Period  { get; set; }
        public DateRange.Finite?            Window  { get; set; }
        public DecimalRange?                Rate    { get; set; }
        public RangeSet<Int32Range, int>?   Blocks  { get; set; }
        public StringSet?                   Tags    { get; set; }
        public Int32Set<OrderNumber>?       Orders  { get; set; }
    }

    private static Settings? Bind(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
          .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
          .Build()
          .Get<Settings>();

    [TestMethod]
    public void EveryFamily_BindsFromItsLiteral()
    {
        var settings = Bind(
            ("Period", "[2026-01-01,2026-06-30]"),
            ("Window", "[2026-07-01,2026-07-31]"),
            ("Rate",   "[1.5,2.5)"),
            ("Blocks", "{[1,5],[7,10]}"),
            ("Tags",   "{beta,alpha}"),
            ("Orders", "{42,7}"))!;

        Assert.AreEqual(DateRange.CreateFinite(new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)), settings.Period);
        Assert.AreEqual(DateRange.CreateFinite(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)), settings.Window);
        Assert.AreEqual(DecimalRange.CreateFinite(1.5m, 2.5m, endInclusive: false), settings.Rate);
        Assert.AreEqual(RangeSet<Int32Range, int>.Parse("{[1,5],[7,10]}", null), settings.Blocks);
        Assert.AreEqual(StringSet.From("alpha", "beta"), settings.Tags);
        Assert.AreEqual(Int32Set<OrderNumber>.From(new OrderNumber(7), new OrderNumber(42)), settings.Orders);
    }

    /// <summary>The literal is a wire format: the process culture must not change what it means.</summary>
    [TestMethod]
    public void Binding_IgnoresTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Assert.AreEqual(DecimalRange.CreateFinite(1.5m, 2.5m, endInclusive: false), Bind(("Rate", "[1.5,2.5)"))!.Rate);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void MalformedLiteral_FailsLoudly()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => Bind(("Period", "[2026-13-01,2026-14-01]")));

        Assert.IsInstanceOfType<FormatException>(ex.InnerException);
    }

    /// <summary>A variant-typed property refuses a literal of another shape rather than holding it.</summary>
    [TestMethod]
    public void VariantProperty_RefusesAnotherShape()
        => Assert.ThrowsExactly<InvalidOperationException>(() => Bind(("Window", "empty")));

    /// <summary>An empty value is absent, never the empty range — that is the literal <c>empty</c>.</summary>
    [TestMethod]
    public void EmptyValue_IsNull_AndTheEmptyLiteral_IsTheEmptyRange()
    {
        Assert.IsNull(Bind(("Period", ""), ("Tags", "{a}"))!.Period);
        Assert.AreEqual(DateRange.Empty, Bind(("Period", "empty"))!.Period);
    }
}

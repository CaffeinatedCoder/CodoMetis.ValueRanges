using System.ComponentModel;
using System.Globalization;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Guards the <see cref="TypeConverter"/> every range, variant, multirange and value set carries
/// since 9.0.0 — what configuration binding and TypeDescriptor-based tooling consult.
/// </summary>
/// <remarks>
/// <para>
/// Before it, <c>TypeDescriptor.GetConverter(typeof(DateRange))</c> could not convert from a
/// string, and configuration binding of <c>"Period": "[2026-01-01,2026-06-30]"</c> into a
/// <c>DateRange</c> property produced <see langword="null"/> without an error.
/// </para>
/// <para>
/// The trap here is the opposite of JSON's. <c>[TypeConverter]</c> <em>is</em> inherited, so a
/// variant without its own attribute does not fall back to nothing — it silently gets the union's
/// converter, which answers with whatever shape the literal has. So the sweep converts through each
/// variant's own converter and requires both the right value and the right type, and requires a
/// literal of another shape to be refused.
/// </para>
/// <para>
/// Every conversion runs under <c>de-DE</c>: the literal is a wire format and must not move with
/// the culture the caller passes. A converter that honoured it would misread the decimal and
/// temporal probes.
/// </para>
/// </remarks>
[TestClass]
public sealed class TypeConverterContractTests
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [TestMethod]
    public void EveryRangeShape_ConvertsThroughTypeDescriptor()
    {
        foreach (var (rangeType, elementType) in RangeProbes.AllRangeTypes())
            Reflect.InvokeGeneric(typeof(TypeConverterContractTests), nameof(AssertRangeShapes), rangeType, elementType);
    }

    private static void AssertRangeShapes<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var shapes = RangeProbes.Shapes<TRange, T>();

        for (var i = 0; i < shapes.Length; i++)
        {
            var range   = shapes[i];
            var variant = range.GetType();
            var literal = ((IFormattable)range).ToString(null, CultureInfo.InvariantCulture);
            var label   = $"{typeof(TRange).Name}.{variant.Name} {literal}";

            AssertRoundTrip(label, typeof(TRange), range, literal);
            AssertRoundTrip(label, variant, range, literal);

            // TypeConverter is inherited: without the variant's own, the union's would answer here.
            var otherShape = ((IFormattable)shapes[(i + 1) % shapes.Length]).ToString(null, CultureInfo.InvariantCulture);
            Assert.ThrowsExactly<FormatException>(
                () => TypeDescriptor.GetConverter(variant).ConvertFrom(null, German, otherShape),
                $"{label}: the variant's converter accepted '{otherShape}', a literal of another shape. "
              + "Is the variant missing its own [TypeConverter], inheriting the union's?");

            var set = RangeSet<TRange, T>.From([range]);
            AssertRoundTrip(label + " as RangeSet", typeof(RangeSet<TRange, T>), set, set.ToString(null, CultureInfo.InvariantCulture));
        }
    }

    [TestMethod]
    public void EverySetType_ConvertsThroughTypeDescriptor()
    {
        foreach (var (setType, elementType) in SetProbes.AllSetTypes())
            Reflect.InvokeGeneric(typeof(TypeConverterContractTests), nameof(AssertSet), setType, elementType);
    }

    private static void AssertSet<TSet, T>()
        where TSet : IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        foreach (var set in new[] { TSet.Empty, TSet.From(SetProbes.For<T>()) })
            AssertRoundTrip($"{typeof(TSet).Name} {set}", typeof(TSet), set, set.ToString(null, CultureInfo.InvariantCulture));
    }

    private static void AssertRoundTrip(string label, Type declared, object value, string literal)
    {
        var converter = TypeDescriptor.GetConverter(declared);

        Assert.IsTrue(converter.CanConvertFrom(typeof(string)),
            $"{label}: TypeDescriptor's converter for {declared.Name} ({converter.GetType().Name}) cannot convert "
          + "from a string. Is the type missing its [TypeConverter]?");
        Assert.IsTrue(converter.CanConvertTo(typeof(string)), $"{label}: cannot convert to a string.");

        Assert.AreEqual(literal, converter.ConvertTo(null, German, value, typeof(string)),
            $"{label}: through {declared.Name}, formatting is not the invariant literal.");

        var back = converter.ConvertFrom(null, German, literal);
        Assert.AreEqual(value, back, $"{label}: through {declared.Name}, the literal did not read back equal.");
        Assert.IsInstanceOfType(back, declared, $"{label}: through {declared.Name}, read back as {back?.GetType().Name}.");

        Assert.IsNull(converter.ConvertFrom(null, German, ""),
            $"{label}: through {declared.Name}, an empty string must be an absent value, not a range or set.");
    }
}

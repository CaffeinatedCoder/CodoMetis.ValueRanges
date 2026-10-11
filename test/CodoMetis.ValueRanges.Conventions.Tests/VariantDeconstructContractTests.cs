using System.Reflection;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every range variant that carries a bound deconstructs into exactly what its constructor takes,
/// in the same order — the deconstructor the compiler writes for a positional record.
/// </summary>
/// <remarks>
/// <para>
/// Most bounded variants are positional records and get theirs for free. The rest are written by
/// hand: every <c>Finite</c> (its constructor is internal, so the factories stay the only way to
/// build one), and the unbounded variants of <c>LocalDateRange</c>, <c>LocalDateTimeRange</c> and
/// <c>YearMonthRange</c>, whose constructors normalize or validate the calendar. Before 9.0.0 none
/// of those had one, so <c>var (start, end) = finite</c> did not compile on any range type.
/// </para>
/// <para>
/// A missing deconstructor is at least a compile error at the call site. What this class exists
/// for is the hand-written ones that compile and answer wrongly: one that swaps
/// <c>start</c> and <c>end</c>, or the two inclusive flags — the same type either way — and a
/// two-value form on a continuous type, which drops the inclusiveness so that <c>(1.5,2]</c>
/// passed back to <c>CreateFinite</c> comes out as its half-open default <c>[1.5,2)</c>.
/// </para>
/// </remarks>
[TestClass]
public sealed class VariantDeconstructContractTests
{
    /// <summary>11 range types × Finite, UnboundedStart and UnboundedEnd, as of 9.0.0.</summary>
    private const int KnownBoundedVariants = 33;

    [TestMethod]
    public void EveryBoundedVariant_DeconstructsIntoItsConstructorParameters()
    {
        var checkedVariants = RangeProbes.AllRangeTypes()
                                         .SelectMany(pair => Reflect.InvokeGeneric<IEnumerable<string>>(
                                              typeof(VariantDeconstructContractTests), nameof(AssertDeconstructs),
                                              pair.Range, pair.Element))
                                         .Distinct()
                                         .ToList();

        Assert.IsTrue(
            checkedVariants.Count >= KnownBoundedVariants,
            $"Only {checkedVariants.Count} bounded variants were checked, fewer than the "
          + $"{KnownBoundedVariants} known to exist: {string.Join(", ", checkedVariants)}.");
    }

    private static IEnumerable<string> AssertDeconstructs<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (low, high) = ((T)RangeProbes.Bounds[typeof(T)].Low, (T)RangeProbes.Bounds[typeof(T)].High);

        // The shared probes plus the opposite inclusiveness on every bounded shape, so that a
        // deconstructor writing a constant flag — or the other flag — disagrees with at least one.
        TRange[] values =
        [
            .. RangeProbes.Shapes<TRange, T>(),
            TRange.CreateFinite(low, high, startInclusive: false, endInclusive: true),
            TRange.CreateUnboundedEnd(low, startInclusive: false),
            TRange.CreateUnboundedStart(high, endInclusive: true)
        ];

        var checkedVariants = new List<string>();

        foreach (var value in values)
        {
            var variant = value.GetType();
            var name    = $"{typeof(TRange).Name}.{variant.Name}";

            var constructors = variant
                              .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              .Where(constructor => constructor.GetParameters() is not [{ } only]
                                                 || only.ParameterType != variant) // the record copy constructor
                              .ToList();

            Assert.AreEqual(1, constructors.Count, $"{name} declares {constructors.Count} constructors; expected one.");

            var expected = constructors[0].GetParameters();

            if (expected.Length == 0) continue; // EmptyRange and Infinity carry nothing to deconstruct.

            var deconstructors = variant.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(method => method.Name == "Deconstruct")
                                        .ToList();

            Assert.AreEqual(
                1, deconstructors.Count,
                $"{name} has {deconstructors.Count} public Deconstruct methods; expected exactly one, "
              + "mirroring its constructor. A second, shorter form would drop what the constructor needs "
              + "to rebuild the value.");

            var actual = deconstructors[0].GetParameters();

            Assert.AreEqual(
                string.Join(", ", expected.Select(parameter => $"out {parameter.ParameterType.Name} {parameter.Name}")),
                string.Join(", ", actual.Select(parameter => parameter.IsOut
                                                                 ? $"out {parameter.ParameterType.GetElementType()!.Name} {parameter.Name}"
                                                                 : $"{parameter.ParameterType.Name} {parameter.Name}")),
                $"{name}.Deconstruct does not mirror its constructor.");

            var arguments = new object?[actual.Length];
            deconstructors[0].Invoke(value, arguments);

            for (var i = 0; i < actual.Length; i++)
            {
                var parameter = actual[i].Name!;
                var property  = variant.GetProperty(char.ToUpperInvariant(parameter[0]) + parameter[1..])!;

                Assert.AreEqual(
                    property.GetValue(value), arguments[i],
                    $"{name}.Deconstruct of {value} wrote '{arguments[i]}' to '{parameter}', but "
                  + $"{property.Name} is '{property.GetValue(value)}'.");
            }

            checkedVariants.Add(name);
        }

        return checkedVariants;
    }
}

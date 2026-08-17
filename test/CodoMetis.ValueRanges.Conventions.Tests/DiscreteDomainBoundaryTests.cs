using System.Reflection;
using CodoMetis.ValueRanges.Core;
using NodaTime;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// At the edge of a discrete domain the successor does not exist, and every factory that needs one
/// has to answer <see cref="IRangeFactory{TRange,T}.Empty"/> rather than step off the end.
/// </summary>
/// <remarks>
/// <para>
/// <c>(int.MaxValue, +∞)</c> contains nothing, because the first value it would hold is
/// <c>int.MaxValue + 1</c>. So does <c>(-∞, int.MinValue)</c>, <c>(max, max]</c> and
/// <c>[min, min)</c>. Guava's <c>Range</c> has had this open since 2014 (#1767); the guards here
/// close it, in <c>DiscreteCanonical.Finite</c> and in each type's two unbounded factories.
/// </para>
/// <para>
/// <b>Why this needs its own test.</b> The three small-model oracles cannot reach it, and not by
/// oversight: they run over universes of eight to fifteen grid points with bounds drawn from the
/// interior, which is exactly the choice that makes them faithful. Domain extremes are outside them
/// by construction, so exhaustive-over-a-small-model and boundary testing are complements, not
/// substitutes. The existing extreme-value tests in <c>RangeValuesAndClampTests</c> cover
/// <c>DiscreteEnumeration</c> terminating at the domain maximum — a different code path from these
/// guards, which nothing exercised.
/// </para>
/// <para>
/// The repository has already lost this bet once: <c>DecimalRange.Length</c> overflowed on
/// <c>[decimal.MinValue, decimal.MaxValue]</c>, fixed in 7.0.0.
/// </para>
/// <para>
/// Types are discovered, bounds are tabulated, and a discrete type with no entry fails — the same
/// shape as <see cref="SetProbes"/>, because the extremes of a domain cannot be derived by
/// reflection.
/// </para>
/// </remarks>
[TestClass]
public sealed class DiscreteDomainBoundaryTests
{
    /// <summary>The first and last value of each discrete domain.</summary>
    private static readonly Dictionary<Type, (object Min, object Max)> Bounds = new()
    {
        [typeof(int)]       = (int.MinValue, int.MaxValue),
        [typeof(long)]      = (long.MinValue, long.MaxValue),
        [typeof(DateOnly)]  = (DateOnly.MinValue, DateOnly.MaxValue),
        [typeof(LocalDate)] = (LocalDate.MinIsoValue, LocalDate.MaxIsoValue),
        [typeof(YearMonth)] = (LocalDate.MinIsoValue.ToYearMonth(), LocalDate.MaxIsoValue.ToYearMonth())
    };

    [TestMethod]
    public void EveryDiscreteType_RefusesToStepOffItsDomain()
    {
        var covered = new List<string>();

        foreach (var (rangeType, elementType) in DiscreteRangeTypes())
        {
            Assert.IsTrue(
                Bounds.ContainsKey(elementType),
                $"{rangeType.Name} is discrete but has no domain bounds here, so it is silently "
              + $"skipped. Add the first and last {elementType.Name} to the table.");

            Reflect.InvokeGeneric(typeof(DiscreteDomainBoundaryTests), nameof(AssertBoundaryIsRefused),
                                  rangeType, elementType);

            covered.Add(rangeType.Name);
        }

        // Int32Range, Int64Range, DateRange, LocalDateRange, YearMonthRange as of 7.0.1.
        Assert.IsTrue(
            covered.Count >= 5,
            $"Found {covered.Count} discrete range types, fewer than the five known to exist: "
          + $"[{string.Join(", ", covered)}]. A discovery predicate that stopped matching would "
          + "retire this whole check while leaving it green.");
    }

    private static void AssertBoundaryIsRefused<TRange, T>()
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var (min, max) = Bounds[typeof(T)];
        var name       = typeof(TRange).Name;
        T   first      = (T) min;
        T   last       = (T) max;

        // The step functions the guards are built on.
        Assert.IsNull(TRange.NextValueAfter(last),
                      $"{name}.NextValueAfter(max) must be null — there is no value above the domain.");
        Assert.IsNull(TRange.PreviousValueBefore(first),
                      $"{name}.PreviousValueBefore(min) must be null.");

        // Positive controls, so the assertions below cannot pass by everything being empty.
        Assert.IsNotNull(TRange.NextValueAfter(first), $"{name}.NextValueAfter(min) should exist.");
        Assert.IsNotNull(TRange.PreviousValueBefore(last), $"{name}.PreviousValueBefore(max) should exist.");

        // The four constructions whose first value would have to be off the end of the domain.
        Empty($"({name} max, +∞)", TRange.CreateUnboundedEnd(last, false));
        Empty($"(-∞, {name} min)", TRange.CreateUnboundedStart(first, false));
        Empty($"({name} max, max]", TRange.CreateFinite(last, last, false, true));
        Empty($"[{name} min, min)", TRange.CreateFinite(first, first, true, false));

        // And the inclusive forms at the same bounds, which are one-value ranges rather than empty.
        // Without these the four above could be satisfied by a factory that returns Empty for
        // anything touching a domain edge.
        NotEmpty($"[{name} max, +∞)", TRange.CreateUnboundedEnd(last, true), last);
        NotEmpty($"(-∞, {name} min]", TRange.CreateUnboundedStart(first, true), first);
        NotEmpty($"[{name} max, max]", TRange.CreateFinite(last, last, true, true), last);
        NotEmpty($"[{name} min, min]", TRange.CreateFinite(first, first, true, true), first);

        static void Empty(string what, TRange range) =>
            Assert.IsTrue(range.IsEmpty(),
                          $"{what} is {range}, but it holds no value: the first one it would need is "
                        + "off the end of the domain, so the factory must answer Empty rather than "
                        + "step there.");

        static void NotEmpty(string what, TRange range, T holds)
        {
            Assert.IsFalse(range.IsEmpty(), $"{what} should hold exactly one value, but is empty.");
            Assert.IsTrue(range.Contains(holds), $"{what} should contain {holds}.");
        }
    }

    /// <summary>Every shipped range type whose domain is discrete.</summary>
    private static IEnumerable<(Type Range, Type Element)> DiscreteRangeTypes() =>
        new[] { typeof(Int32Range), typeof(LocalDateRange) }
           .Select(marker => marker.Assembly)
           .Distinct()
           .SelectMany(assembly => assembly.GetExportedTypes())
           .Where(type => type is { IsClass: true, IsAbstract: false } or { IsAbstract: true, IsSealed: false })
           .Select(type => (Range: type, Element: ElementTypeOf(type)))
           .Where(pair => pair.Element is not null)
           .Where(pair => IsDiscrete(pair.Range))
           .Select(pair => (pair.Range, Element: pair.Element!))
           .OrderBy(pair => pair.Range.Name, StringComparer.Ordinal);

    private static Type? ElementTypeOf(Type rangeType) =>
        rangeType.GetInterfaces()
                 .FirstOrDefault(@interface => @interface.IsGenericType
                                            && @interface.GetGenericTypeDefinition() == typeof(IRangeFactory<,>))
                ?.GetGenericArguments()[1];

    private static bool IsDiscrete(Type rangeType) =>
        rangeType.GetProperty(nameof(IRangeFactory<Int32Range, int>.IsDiscrete),
                              BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) is true;
}

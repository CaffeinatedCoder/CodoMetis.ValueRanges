using System.Collections;
using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every range union and every <see cref="RangeSet{TRange,T}"/> implements the non-generic
/// <see cref="IComparable"/> with the same answer as <see cref="IComparable{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The unions shipped with <see cref="IComparable{T}"/> only (#39). That sorts a list of the concrete
/// type, but generic code over the interface does not: <c>List&lt;IRange&lt;int&gt;&gt;.Sort()</c>
/// finds no <c>IComparable&lt;IRange&lt;int&gt;&gt;</c> and falls back to the non-generic interface, as
/// do <c>ArrayList.Sort()</c> and <see cref="Comparer.Default"/> — and each threw "At least one object
/// must implement IComparable" (#225).
/// </para>
/// <para>
/// The non-generic form delegates to the same range order: every ordered pair of shapes answers as
/// the generic one, <see langword="null"/> sorts first, and an argument of another type throws
/// <see cref="ArgumentException"/> rather than being ordered by some guess. Every union is discovered,
/// and <see cref="RangeCensus"/> checks that none was missed.
/// </para>
/// </remarks>
[TestClass]
public sealed class NonGenericComparableContractTests
{
    [TestMethod]
    public void EveryUnionAndItsRangeSet_AgreeWithTheGenericOrder_AndRefuseAForeignArgument()
    {
        var checkedUnions = RangeProbes.AllRangeTypes()
                                       .Select(pair => Reflect.InvokeGeneric<Type>(
                                            typeof(NonGenericComparableContractTests), nameof(AssertNonGenericOrder),
                                            pair.Range, pair.Element))
                                       .ToList();

        RangeCensus.AssertReachedEveryUnion(checkedUnions, nameof(EveryUnionAndItsRangeSet_AgreeWithTheGenericOrder_AndRefuseAForeignArgument));
    }

    private static Type AssertNonGenericOrder<TRange, T>()
        where TRange : class, IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var name = typeof(TRange).Name;
        var (low, high) = ((T)RangeProbes.Bounds[typeof(T)].Low, (T)RangeProbes.Bounds[typeof(T)].High);

        TRange[] ranges =
        [
            .. RangeProbes.Shapes<TRange, T>(),
            TRange.CreateFinite(low, high, true, true),
            TRange.CreateFinite(low, high, false, false),
            TRange.CreateFinite(low, low, true, true)
        ];
        RangeSet<TRange, T>[] sets = [RangeSet<TRange, T>.Empty, .. ranges.Select(range => RangeSet<TRange, T>.From([range]))];

        foreach (var a in ranges)
        foreach (var b in ranges)
            Assert.AreEqual(Math.Sign(a.CompareTo(b)), Math.Sign(NonGeneric(a).CompareTo(b)), $"{name}: {a} vs {b}");

        foreach (var a in sets)
        foreach (var b in sets)
            Assert.AreEqual(Math.Sign(a.CompareTo(b)), Math.Sign(NonGeneric(a).CompareTo(b)), $"RangeSet<{name}>: {a} vs {b}");

        // null sorts first, as in the generic form.
        Assert.IsGreaterThan(0, NonGeneric(ranges[0]).CompareTo(null), $"{name}: CompareTo(null)");
        Assert.IsGreaterThan(0, NonGeneric(sets[0]).CompareTo(null), $"RangeSet<{name}>: CompareTo(null)");

        // A foreign argument throws: another range type, the element itself, and a range where a set is expected.
        object other = typeof(TRange) == typeof(Int32Range) ? DecimalRange.Empty : Int32Range.Empty;
        foreach (var foreign in new[] { other, low, "[1,2)" })
        {
            var thrown = Assert.ThrowsExactly<ArgumentException>(() => NonGeneric(ranges[0]).CompareTo(foreign),
                                                                 $"{name}: CompareTo({foreign.GetType().Name})");
            StringAssert.Contains(thrown.Message, name);
        }

        Assert.ThrowsExactly<ArgumentException>(() => NonGeneric(sets[0]).CompareTo(ranges[0]), $"RangeSet<{name}>: CompareTo({name})");

        // The sorts that read only the non-generic interface: a list of the interface, and an ArrayList.
        var expected = ranges.Order().ToList();

        var viaInterface = new List<IRange<T>>(Enumerable.Reverse<IRange<T>>(ranges));
        viaInterface.Sort();
        CollectionAssert.AreEqual(expected, viaInterface, $"{name}: List<IRange<T>>.Sort()");

        var untyped = new ArrayList(Enumerable.Reverse(ranges).ToArray());
        untyped.Sort();
        CollectionAssert.AreEqual(expected, untyped, $"{name}: ArrayList.Sort()");

        return typeof(TRange);
    }

    // Through object, so that a type without the interface fails here at run time rather than the
    // whole file failing to compile.
    private static IComparable NonGeneric(object value)
        => value as IComparable
        ?? throw new AssertFailedException($"{value.GetType().Name} does not implement the non-generic IComparable.");
}

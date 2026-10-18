namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// <c>CompareTo</c> on the range types and on <see cref="RangeSet{TRange,T}"/>: PostgreSQL's range
/// and multirange order, a total order whose 0 is exactly <c>Equals</c>.
/// </summary>
/// <remarks>
/// <c>RangeOrderParityTests</c> (integration) asks PostgreSQL for every ordered shape pair of every
/// type. These are the properties a sorted collection depends on — antisymmetry, transitivity, and a
/// 0 that never merges two different ranges into one key — which hold whatever the server says.
/// </remarks>
[TestClass]
public class RangeCompareToTests
{
    private static readonly Int32Range[] Int32Shapes =
    [
        Int32Range.Empty, Int32Range.Infinite,
        Int32Range.CreateFinite(1, 5), Int32Range.CreateFinite(1, 9), Int32Range.CreateFinite(2, 3),
        Int32Range.CreateFinite(5, 5), Int32Range.CreateUnboundedStart(1), Int32Range.CreateUnboundedStart(5, endInclusive: true),
        Int32Range.CreateUnboundedEnd(1), Int32Range.CreateUnboundedEnd(5)
    ];

    private static readonly DecimalRange[] DecimalShapes =
    [
        DecimalRange.Empty, DecimalRange.Infinite,
        DecimalRange.CreateFinite(1m, 5m, true, true), DecimalRange.CreateFinite(1m, 5m, true, false),
        DecimalRange.CreateFinite(1m, 5m, false, true), DecimalRange.CreateFinite(1m, 5m, false, false),
        DecimalRange.CreateUnboundedStart(1m, true), DecimalRange.CreateUnboundedStart(1m, false),
        DecimalRange.CreateUnboundedStart(5m, true), DecimalRange.CreateUnboundedEnd(1m, true),
        DecimalRange.CreateUnboundedEnd(1m, false), DecimalRange.CreateUnboundedEnd(5m, false)
    ];

    // -------------------------------------------------------------------------
    // The order
    // -------------------------------------------------------------------------

    [TestMethod]
    public void EmptySortsFirst_ThenLowerBound_ThenUpperBound()
    {
        Int32Range[] expected =
        [
            Int32Range.Empty,
            Int32Range.CreateUnboundedStart(1),                     // (,1): no lower bound sorts first …
            Int32Range.CreateUnboundedStart(5, endInclusive: true), // … and then by the upper bound
            Int32Range.Infinite,                                    // (,): an absent upper bound sorts last
            Int32Range.CreateFinite(1, 5),
            Int32Range.CreateFinite(1, 9),
            Int32Range.CreateUnboundedEnd(1),
            Int32Range.CreateFinite(2, 3),
            Int32Range.CreateFinite(5, 5),
            Int32Range.CreateUnboundedEnd(5)
        ];

        var sorted = Int32Shapes.Order().ToList();

        CollectionAssert.AreEqual(expected, sorted);
    }

    [TestMethod]
    public void AtTheSameValue_AnInclusiveLowerBoundSortsFirst_AndAnInclusiveUpperBoundSortsLast()
    {
        var closedOpen = DecimalRange.CreateFinite(1m, 5m, true, false); // [1,5)
        var closed     = DecimalRange.CreateFinite(1m, 5m, true, true);  // [1,5]
        var open       = DecimalRange.CreateFinite(1m, 5m, false, false); // (1,5)

        Assert.IsLessThan(0, closedOpen.CompareTo(closed), "[1,5) ends before [1,5]");
        Assert.IsLessThan(0, closed.CompareTo(open), "[1,5] starts before (1,5)");
        Assert.IsLessThan(0, closedOpen.CompareTo(open), "[1,5) starts before (1,5)");
    }

    [TestMethod]
    public void TheDiscreteCanonicalForm_OrdersAsTheHalfOpenFormPostgresKeeps()
    {
        // [1,5] is [1,6) on the server and (0,5] is [1,5]: the closed form changes no answer.
        Assert.AreEqual(0, Int32Range.CreateFinite(0, 5, false, true).CompareTo(Int32Range.CreateFinite(1, 5)));
        Assert.IsGreaterThan(0, Int32Range.CreateFinite(1, 6, true, false).CompareTo(Int32Range.CreateFinite(1, 4)));
    }

    // -------------------------------------------------------------------------
    // The properties a sorted collection depends on
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Int32Range_IsATotalOrder_WhoseZeroIsEquals() => AssertTotalOrder(Int32Shapes);

    [TestMethod]
    public void DecimalRange_IsATotalOrder_WhoseZeroIsEquals() => AssertTotalOrder(DecimalShapes);

    [TestMethod]
    public void RangeSet_IsATotalOrder_WhoseZeroIsEquals()
    {
        RangeSet<DecimalRange, decimal>[] sets =
        [
            RangeSet<DecimalRange, decimal>.Empty,
            .. DecimalShapes.Select(range => RangeSet<DecimalRange, decimal>.From([range])),
            RangeSet<DecimalRange, decimal>.From([DecimalRange.CreateFinite(1m, 5m), DecimalRange.CreateFinite(7m, 9m)]),
            RangeSet<DecimalRange, decimal>.From([DecimalRange.CreateFinite(1m, 5m), DecimalRange.CreateUnboundedEnd(7m)]),
            RangeSet<DecimalRange, decimal>.From([DecimalRange.CreateUnboundedStart(1m, false), DecimalRange.CreateFinite(2m, 3m)])
        ];

        AssertTotalOrder(sets);
    }

    private static void AssertTotalOrder<TValue>(TValue[] values) where TValue : IComparable<TValue>
    {
        foreach (var a in values)
        foreach (var b in values)
        {
            Assert.AreEqual(Math.Sign(a.CompareTo(b)), -Math.Sign(b.CompareTo(a)), $"antisymmetry: {a} vs {b}");
            Assert.AreEqual(a.Equals(b), a.CompareTo(b) == 0, $"CompareTo == 0 exactly when Equals: {a} vs {b}");

            foreach (var c in values)
            {
                if (a.CompareTo(b) <= 0 && b.CompareTo(c) <= 0)
                    Assert.IsLessThanOrEqualTo(0, a.CompareTo(c), $"transitivity: {a} <= {b} <= {c}");
            }
        }
    }

    [TestMethod]
    public void EqualElements_InDifferentSpellings_CompareEqual_AsTheyAreEqual()
    {
        var oneOffset   = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
                                                           new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero));
        var otherOffset = DateTimeOffsetRange.CreateFinite(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2)),
                                                           new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.FromHours(2)));
        Assert.AreEqual(oneOffset, otherOffset);
        Assert.AreEqual(0, oneOffset.CompareTo(otherOffset));

        var scaleOne = DecimalRange.CreateFinite(1.0m, 2m);
        var scaleTwo = DecimalRange.CreateFinite(1.00m, 2m);
        Assert.AreEqual(scaleOne, scaleTwo);
        Assert.AreEqual(0, scaleOne.CompareTo(scaleTwo));
    }

    [TestMethod]
    public void ASortedSet_KeepsEveryDistinctRange_AndDropsOnlyEqualOnes()
    {
        var set = new SortedSet<DecimalRange>(DecimalShapes.Concat(DecimalShapes));

        Assert.HasCount(DecimalShapes.Distinct().Count(), set);
    }

    // -------------------------------------------------------------------------
    // Null, the variants, and generic code
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Null_SortsBeforeEveryRange()
    {
        Assert.IsGreaterThan(0, Int32Range.Empty.CompareTo(null));
        Assert.IsGreaterThan(0, RangeSet<Int32Range, int>.Empty.CompareTo(null));

        var sorted = new List<Int32Range?> { Int32Range.CreateFinite(1, 5), null, Int32Range.Empty }.Order().ToList();
        CollectionAssert.AreEqual(new Int32Range?[] { null, Int32Range.Empty, Int32Range.CreateFinite(1, 5) }, sorted);
    }

    [TestMethod]
    public void AListOfOneVariant_SortsWithTheDefaultComparer()
    {
        // IComparable<in T> is contravariant, so a Finite is an IComparable<Int32Range.Finite>.
        var finites = new List<Int32Range.Finite>
        {
            (Int32Range.Finite)Int32Range.CreateFinite(3, 4),
            (Int32Range.Finite)Int32Range.CreateFinite(1, 9),
            (Int32Range.Finite)Int32Range.CreateFinite(1, 2)
        };

        finites.Sort();

        CollectionAssert.AreEqual(new[] { "[1,2]", "[1,9]", "[3,4]" }, finites.Select(f => f.ToString()).ToArray());
    }

    [TestMethod]
    public void TheLowerBoundComparer_StaysSeparate_AndTiesOnTheLowerBound()
    {
        var a = Int32Range.CreateFinite(1, 5);
        var b = Int32Range.CreateFinite(1, 9);

        Assert.AreEqual(0, RangeSet<Int32Range, int>.LowerBoundComparer.Compare(a, b));
        Assert.IsLessThan(0, a.CompareTo(b));
    }

    // -------------------------------------------------------------------------
    // Multiranges
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RangeSet_ComparesElementByElement_AndAShorterSetSortsFirst()
    {
        var empty  = RangeSet<Int32Range, int>.Empty;
        var one    = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 3)]);
        var two    = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 3), Int32Range.CreateFinite(5, 7)]);
        var later  = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(2, 3)]);
        var longer = RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(1, 4)]);

        Assert.IsLessThan(0, empty.CompareTo(one), "{} first");
        Assert.IsLessThan(0, one.CompareTo(two), "a prefix sorts first");
        Assert.IsLessThan(0, two.CompareTo(longer), "the first element decides before the count: [1,3] ends before [1,4]");
        Assert.IsLessThan(0, two.CompareTo(later), "[1,3] starts before [2,3]");
        Assert.AreEqual(0, two.CompareTo(RangeSet<Int32Range, int>.From([Int32Range.CreateFinite(5, 7), Int32Range.CreateFinite(1, 3)])));
    }
}

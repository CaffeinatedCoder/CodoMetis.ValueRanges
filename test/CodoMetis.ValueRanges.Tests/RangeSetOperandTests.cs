namespace CodoMetis.ValueRanges.Tests;

using DecimalSet = RangeSet<DecimalRange, decimal>;
using IntSet = RangeSet<Int32Range, int>;

/// <summary>
/// A range with a <see cref="RangeSet{TRange,T}"/> on the right — PostgreSQL's
/// <c>anyrange op anymultirange</c> operators — and <c>RangeSet.IsContainedBy</c>.
/// </summary>
/// <remarks>
/// <c>ShapeMatrixParityTests</c> and <c>ContinuousMultirangeParityTests</c> (integration) ask
/// PostgreSQL for every pair. These are the worked examples the docs give, and the empty cases.
/// </remarks>
[TestClass]
public class RangeSetOperandTests
{
    private static readonly IntSet Blocked = IntSet.From([Int32Range.CreateFinite(3, 4), Int32Range.CreateFinite(8, 9)]);

    [TestMethod]
    public void Contains_NeedsEveryElement_IncludingTheGapBetweenThem()
    {
        Assert.IsTrue(Int32Range.CreateFinite(1, 10).Contains(Blocked));
        Assert.IsFalse(Int32Range.CreateFinite(1, 5).Contains(Blocked), "[8,9] is outside");
        Assert.IsTrue(Int32Range.CreateFinite(1, 5).Contains(IntSet.Empty), "the empty set is contained by every range");
        Assert.IsTrue(Int32Range.Empty.Contains(IntSet.Empty));
        Assert.IsFalse(Int32Range.Empty.Contains(Blocked));
    }

    [TestMethod]
    public void IsContainedBy_IsTheSetsContains()
    {
        Assert.IsTrue(Int32Range.CreateFinite(8, 9).IsContainedBy(Blocked));
        Assert.IsFalse(Int32Range.CreateFinite(4, 8).IsContainedBy(Blocked), "5–7 is a gap");
        Assert.IsTrue(Int32Range.Empty.IsContainedBy(IntSet.Empty));
    }

    [TestMethod]
    public void TheEdgeComparisons_ReadTheOuterElementTheyCompare()
    {
        var range = Int32Range.CreateFinite(5, 7);

        Assert.IsTrue(range.DoesNotExtendRightOf(Blocked), "7 ≤ 9, the last element's upper bound");
        Assert.IsFalse(range.DoesNotExtendLeftOf(IntSet.From([Int32Range.CreateFinite(6, 9)])), "5 < 6");
        Assert.IsTrue(range.DoesNotExtendLeftOf(Blocked), "5 ≥ 3, the first element's lower bound");
        Assert.IsFalse(range.IsStrictlyLeftOf(Blocked), "[3,4] comes first");
        Assert.IsTrue(Int32Range.CreateFinite(1, 2).IsStrictlyLeftOf(Blocked));
        Assert.IsTrue(Int32Range.CreateFinite(10, 12).IsStrictlyRightOf(Blocked));
        Assert.IsFalse(range.IsStrictlyRightOf(Blocked), "[8,9] comes last");
    }

    [TestMethod]
    public void DoesNotExtendRightOf_IsNotTheConverseOfTheSetsDoesNotExtendLeftOf()
    {
        var range = DecimalRange.CreateFinite(4m, 5m);
        var set   = DecimalSet.From([DecimalRange.CreateFinite(3m, 9m)]);

        Assert.IsTrue(range.DoesNotExtendRightOf(set));
        Assert.IsFalse(set.DoesNotExtendLeftOf(range));
    }

    [TestMethod]
    public void AnEmptyOperandOnEitherSide_IsNeitherLeftNorRightNorAdjacent()
    {
        var range = Int32Range.CreateFinite(1, 2);

        Assert.IsFalse(range.IsStrictlyLeftOf(IntSet.Empty));
        Assert.IsFalse(range.IsStrictlyRightOf(IntSet.Empty));
        Assert.IsFalse(range.DoesNotExtendRightOf(IntSet.Empty));
        Assert.IsFalse(range.DoesNotExtendLeftOf(IntSet.Empty));
        Assert.IsFalse(range.IsAdjacentTo(IntSet.Empty));
        Assert.IsFalse(range.Overlaps(IntSet.Empty));
        Assert.IsFalse(Int32Range.Empty.DoesNotExtendRightOf(Blocked));
    }

    [TestMethod]
    public void IsAdjacentTo_CountsOnlyTheOuterEdges()
    {
        Assert.IsTrue(Int32Range.CreateFinite(1, 2).IsAdjacentTo(Blocked));
        Assert.IsTrue(Int32Range.CreateFinite(10, 12).IsAdjacentTo(Blocked));
        Assert.IsFalse(Int32Range.CreateFinite(5, 7).IsAdjacentTo(Blocked), "touches both inner sides of the gap");
    }

    [TestMethod]
    public void RangeSetIsContainedBy_ARangeOrASet()
    {
        Assert.IsTrue(Blocked.IsContainedBy(Int32Range.CreateFinite(3, 9)));
        Assert.IsFalse(Blocked.IsContainedBy(Int32Range.CreateFinite(3, 8)));
        Assert.IsTrue(IntSet.Empty.IsContainedBy(Int32Range.Empty));
        Assert.IsTrue(Blocked.IsContainedBy(Blocked | Int32Range.CreateFinite(20, 30)));
        Assert.IsFalse((Blocked | Int32Range.CreateFinite(20, 30)).IsContainedBy(Blocked));
        Assert.IsTrue(Blocked.IsContainedBy(IntSet.Infinite));
    }
}

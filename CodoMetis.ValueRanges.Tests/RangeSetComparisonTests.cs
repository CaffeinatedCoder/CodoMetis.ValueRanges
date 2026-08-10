using DecimalSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.DecimalRange, decimal>;
using IntSet = CodoMetis.ValueRanges.RangeSet<CodoMetis.ValueRanges.Int32Range, int>;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Covers the multirange comparison surface added for PostgreSQL operator parity:
/// state checks (<c>isempty</c>, <c>lower_inf</c>, <c>upper_inf</c>), set-operand
/// <c>Contains</c>/<c>Overlaps</c>, adjacency, positional comparisons, and the
/// <c>==</c>/<c>!=</c> operators.
/// </summary>
[TestClass]
public class RangeSetComparisonTests
{
    private static IntSet Set(params Int32Range[] ranges) => IntSet.From(ranges);

    private static Int32Range R(int start, int end) => Int32Range.CreateFinite(start, end);

    // -------------------------------------------------------------------------
    // State checks
    // -------------------------------------------------------------------------

    [TestMethod]
    public void IsEmpty_TrueOnlyForEmptySet()
    {
        Assert.IsTrue(IntSet.Empty.IsEmpty());
        Assert.IsFalse(Set(R(1, 3)).IsEmpty());
        Assert.IsFalse(IntSet.Infinite.IsEmpty());
    }

    [TestMethod]
    public void IsUnboundedStart_ReflectsFirstElement()
    {
        Assert.IsFalse(IntSet.Empty.IsUnboundedStart());
        Assert.IsFalse(Set(R(1, 3)).IsUnboundedStart());
        Assert.IsTrue(IntSet.Infinite.IsUnboundedStart());
        Assert.IsTrue(Set(Int32Range.CreateUnboundedStart(5, true), R(10, 12)).IsUnboundedStart());
    }

    [TestMethod]
    public void IsUnboundedEnd_ReflectsLastElement()
    {
        Assert.IsFalse(IntSet.Empty.IsUnboundedEnd());
        Assert.IsFalse(Set(R(1, 3)).IsUnboundedEnd());
        Assert.IsTrue(IntSet.Infinite.IsUnboundedEnd());
        Assert.IsTrue(Set(R(1, 3), Int32Range.CreateUnboundedEnd(10)).IsUnboundedEnd());
    }

    // -------------------------------------------------------------------------
    // Contains(RangeSet)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Contains_Set_SubsetElements_ReturnsTrue()
    {
        var outer = Set(R(1, 10), R(20, 30));
        var inner = Set(R(2, 4), R(6, 8), R(21, 29));

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_Set_EmptyOther_AlwaysTrue()
    {
        Assert.IsTrue(Set(R(1, 3)).Contains(IntSet.Empty));
        Assert.IsTrue(IntSet.Empty.Contains(IntSet.Empty));
    }

    [TestMethod]
    public void Contains_Set_EmptyThis_NonEmptyOther_ReturnsFalse()
    {
        Assert.IsFalse(IntSet.Empty.Contains(Set(R(1, 3))));
    }

    [TestMethod]
    public void Contains_Set_ElementSpanningGap_ReturnsFalse()
    {
        var gappy = Set(R(1, 3), R(5, 7)); // 4 lies in the gap

        Assert.IsFalse(gappy.Contains(Set(R(1, 7))));
        Assert.IsFalse(gappy.Contains(Set(R(2, 6))));
    }

    [TestMethod]
    public void Contains_Set_PartialOverlap_ReturnsFalse()
    {
        Assert.IsFalse(Set(R(1, 10)).Contains(Set(R(5, 15))));
    }

    [TestMethod]
    public void Contains_Set_InfiniteContainsEverything()
    {
        Assert.IsTrue(IntSet.Infinite.Contains(Set(R(1, 3), Int32Range.CreateUnboundedEnd(10))));
        Assert.IsTrue(IntSet.Infinite.Contains(IntSet.Infinite));
    }

    [TestMethod]
    public void InfiniteSet_ContainsAndOverlaps_AnyRangeShape()
    {
        // Regression: these used to throw InvalidOperationException because the Infinity
        // element reached the bound helpers, which reject that shape.
        Assert.IsTrue(IntSet.Infinite.Contains(R(1, 3)));
        Assert.IsTrue(IntSet.Infinite.Contains(Int32Range.CreateUnboundedEnd(10)));
        Assert.IsTrue(IntSet.Infinite.Overlaps(R(1, 3)));
        Assert.IsTrue(IntSet.Infinite.Overlaps(Int32Range.CreateUnboundedStart(5, true)));
        Assert.IsFalse(IntSet.Infinite.Contains(Int32Range.Empty));
        Assert.IsFalse(IntSet.Infinite.Overlaps(Int32Range.Empty));
    }

    // -------------------------------------------------------------------------
    // Overlaps(RangeSet)
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Overlaps_Set_SharedValues_ReturnsTrue()
    {
        Assert.IsTrue(Set(R(1, 5), R(20, 25)).Overlaps(Set(R(24, 30))));
        Assert.IsTrue(Set(R(1, 5)).Overlaps(Set(R(5, 9))));
    }

    [TestMethod]
    public void Overlaps_Set_Disjoint_ReturnsFalse()
    {
        Assert.IsFalse(Set(R(1, 3), R(10, 12)).Overlaps(Set(R(5, 8), R(20, 22))));
    }

    [TestMethod]
    public void Overlaps_Set_EmptyOperand_ReturnsFalse()
    {
        Assert.IsFalse(IntSet.Empty.Overlaps(Set(R(1, 3))));
        Assert.IsFalse(Set(R(1, 3)).Overlaps(IntSet.Empty));
        Assert.IsFalse(IntSet.Empty.Overlaps(IntSet.Empty));
    }

    [TestMethod]
    public void Overlaps_Set_AdjacentButDisjoint_ReturnsFalse()
    {
        // [1,3] and [4,6] are adjacent for int but share no value.
        Assert.IsFalse(Set(R(1, 3)).Overlaps(Set(R(4, 6))));
    }

    // -------------------------------------------------------------------------
    // IsAdjacentTo — outermost elements only, mirroring PostgreSQL
    // -------------------------------------------------------------------------

    [TestMethod]
    public void IsAdjacentTo_Range_AtOuterEdges_ReturnsTrue()
    {
        var set = Set(R(1, 3), R(10, 12));

        Assert.IsTrue(set.IsAdjacentTo(R(13, 15))); // attaches after the last element
        Assert.IsTrue(set.IsAdjacentTo(R(-2, 0)));  // attaches before the first element
    }

    [TestMethod]
    public void IsAdjacentTo_Range_InteriorBoundaries_ReturnFalse()
    {
        // PostgreSQL multirange adjacency is directional through the outer edges only:
        // the operand must end where the first element begins or begin where the last
        // element ends. Touching any interior boundary — even the first element's inner
        // side — does not count.
        var set = Set(R(1, 3), R(7, 9), R(20, 22));

        Assert.IsFalse(set.IsAdjacentTo(R(4, 6)));   // inner side of the first element
        Assert.IsFalse(set.IsAdjacentTo(R(10, 12))); // touches only the middle element
        Assert.IsFalse(set.IsAdjacentTo(R(17, 19))); // inner side of the last element
    }

    [TestMethod]
    public void IsAdjacentTo_Range_OverlappingOrEmpty_ReturnsFalse()
    {
        var set = Set(R(1, 3), R(10, 12));

        Assert.IsFalse(set.IsAdjacentTo(R(2, 5)));
        Assert.IsFalse(set.IsAdjacentTo(Int32Range.Empty));
        Assert.IsFalse(IntSet.Empty.IsAdjacentTo(R(1, 3)));
    }

    [TestMethod]
    public void IsAdjacentTo_Set_MeetingAtBoundary_ReturnsTrue()
    {
        Assert.IsTrue(Set(R(1, 3)).IsAdjacentTo(Set(R(4, 6), R(10, 12))));
        Assert.IsTrue(Set(R(10, 12)).IsAdjacentTo(Set(R(1, 3), R(7, 9))));
    }

    [TestMethod]
    public void IsAdjacentTo_Set_GapOrOverlap_ReturnsFalse()
    {
        Assert.IsFalse(Set(R(1, 3)).IsAdjacentTo(Set(R(6, 8))));   // gap at 4-5
        Assert.IsFalse(Set(R(1, 5)).IsAdjacentTo(Set(R(5, 8))));   // overlap at 5
        Assert.IsFalse(IntSet.Empty.IsAdjacentTo(Set(R(1, 3))));
        Assert.IsFalse(Set(R(1, 3)).IsAdjacentTo(IntSet.Empty));
    }

    [TestMethod]
    public void IsAdjacentTo_Continuous_RequiresComplementaryInclusiveness()
    {
        var set = DecimalSet.From([DecimalRange.CreateFinite(1m, 5m)]); // [1, 5)

        Assert.IsTrue(set.IsAdjacentTo(DecimalRange.CreateFinite(5m, 9m)));        // [5, 9) — complementary at 5
        Assert.IsFalse(set.IsAdjacentTo(DecimalRange.CreateFinite(5m, 9m, false, false))); // (5, 9) — nobody claims 5
    }

    // -------------------------------------------------------------------------
    // Positional comparisons
    // -------------------------------------------------------------------------

    [TestMethod]
    public void IsStrictlyLeftOf_DecidedByLastElement()
    {
        var set = Set(R(1, 3), R(5, 7));

        Assert.IsTrue(set.IsStrictlyLeftOf(R(10, 12)));
        Assert.IsFalse(set.IsStrictlyLeftOf(R(6, 8))); // last element reaches 7
        Assert.IsTrue(set.IsStrictlyLeftOf(Set(R(8, 9), R(20, 22))));
        Assert.IsFalse(set.IsStrictlyLeftOf(IntSet.Empty));
        Assert.IsFalse(IntSet.Empty.IsStrictlyLeftOf(R(1, 2)));
    }

    [TestMethod]
    public void IsStrictlyRightOf_DecidedByFirstElement()
    {
        var set = Set(R(10, 12), R(20, 22));

        Assert.IsTrue(set.IsStrictlyRightOf(R(1, 3)));
        Assert.IsFalse(set.IsStrictlyRightOf(R(8, 11))); // first element starts inside
        Assert.IsTrue(set.IsStrictlyRightOf(Set(R(1, 3), R(5, 9))));
        Assert.IsFalse(set.IsStrictlyRightOf(IntSet.Empty));
    }

    [TestMethod]
    public void DoesNotExtendRightOf_ComparesUpperBounds()
    {
        var set = Set(R(1, 3), R(5, 7));

        Assert.IsTrue(set.DoesNotExtendRightOf(R(5, 7)));
        Assert.IsTrue(set.DoesNotExtendRightOf(R(6, 10)));
        Assert.IsFalse(set.DoesNotExtendRightOf(R(1, 6)));
        Assert.IsTrue(set.DoesNotExtendRightOf(Set(R(2, 4), R(6, 9))));
        Assert.IsFalse(set.DoesNotExtendRightOf(IntSet.Empty));
    }

    [TestMethod]
    public void DoesNotExtendLeftOf_ComparesLowerBounds()
    {
        var set = Set(R(5, 7), R(10, 12));

        Assert.IsTrue(set.DoesNotExtendLeftOf(R(5, 9)));
        Assert.IsTrue(set.DoesNotExtendLeftOf(R(1, 3)));
        Assert.IsFalse(set.DoesNotExtendLeftOf(R(6, 8)));
        Assert.IsTrue(set.DoesNotExtendLeftOf(Set(R(3, 4), R(20, 22))));
        Assert.IsFalse(set.DoesNotExtendLeftOf(IntSet.Empty));
    }

    // -------------------------------------------------------------------------
    // == / != operators
    // -------------------------------------------------------------------------

    [TestMethod]
    public void EqualityOperator_ComparesByValue()
    {
        var a = Set(R(1, 3), R(5, 7));
        var b = Set(R(5, 7), R(1, 3)); // same set, different input order

        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.IsFalse(a == Set(R(1, 3)));
        Assert.IsTrue(a != Set(R(1, 3)));
    }

    [TestMethod]
    public void EqualityOperator_HandlesNulls()
    {
        IntSet? left  = null;
        IntSet? right = null;

        Assert.IsTrue(left == right);
        Assert.IsFalse(left == IntSet.Empty);
        Assert.IsFalse(IntSet.Empty == right);
        Assert.IsTrue(IntSet.Empty != right);
    }
}

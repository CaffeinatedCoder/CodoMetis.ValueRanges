using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The five shape predicates on <see cref="IRange{T}"/> — <c>isempty</c>, <c>lower_inf</c>,
/// <c>upper_inf</c> and the two derived ones. They partition the union: every range answers
/// <see langword="true"/> to exactly one, which is what lets the EF translator map
/// <c>IsInfinity</c>/<c>IsFinite</c> onto combinations of the PostgreSQL predicates.
/// </summary>
/// <remarks>
/// <see cref="RangeSet{TRange,T}"/> declares its own <c>IsEmpty</c>/<c>IsUnboundedStart</c>/
/// <c>IsUnboundedEnd</c> over the element list; those are covered by
/// <see cref="RangeSetComparisonTests"/>.
/// </remarks>
[TestClass]
public class RangeShapePredicateTests
{
    private static Int32Range[] AllShapes =>
    [
        Int32Range.Empty,
        Int32Range.Infinite,
        Int32Range.CreateFinite(1, 10),
        Int32Range.CreateUnboundedStart(50, true),
        Int32Range.CreateUnboundedEnd(10, true)
    ];

    /// <summary>Asserts <paramref name="predicate"/> holds for exactly <paramref name="expected"/>.</summary>
    private static void AssertHoldsOnlyFor(Func<Int32Range, bool> predicate, Int32Range expected, string name)
    {
        foreach (var shape in AllShapes)
            Assert.AreEqual(ReferenceEquals(shape, expected) || shape.Equals(expected), predicate(shape),
                $"{name} on {shape.GetType().Name} ('{shape}')");
    }

    [TestMethod]
    public void IsEmpty_HoldsOnlyForTheEmptyRange()
        => AssertHoldsOnlyFor(r => r.IsEmpty(), Int32Range.Empty, nameof(RangeExtensions.IsEmpty));

    [TestMethod]
    public void IsInfinity_HoldsOnlyForTheInfiniteRange()
        => AssertHoldsOnlyFor(r => r.IsInfinity(), Int32Range.Infinite, nameof(RangeExtensions.IsInfinity));

    [TestMethod]
    public void IsFinite_HoldsOnlyForTheBoundedRange()
        => AssertHoldsOnlyFor(r => r.IsFinite(), Int32Range.CreateFinite(1, 10), nameof(RangeExtensions.IsFinite));

    [TestMethod]
    public void IsUnboundedStart_HoldsOnlyForTheLeftUnboundedRange()
        => AssertHoldsOnlyFor(r => r.IsUnboundedStart(), Int32Range.CreateUnboundedStart(50, true),
            nameof(RangeExtensions.IsUnboundedStart));

    [TestMethod]
    public void IsUnboundedEnd_HoldsOnlyForTheRightUnboundedRange()
        => AssertHoldsOnlyFor(r => r.IsUnboundedEnd(), Int32Range.CreateUnboundedEnd(10, true),
            nameof(RangeExtensions.IsUnboundedEnd));

    [TestMethod]
    public void EveryShape_SatisfiesExactlyOnePredicate()
    {
        foreach (var shape in AllShapes)
        {
            var hits = new[]
            {
                shape.IsEmpty(), shape.IsInfinity(), shape.IsFinite(),
                shape.IsUnboundedStart(), shape.IsUnboundedEnd()
            }.Count(hit => hit);

            Assert.AreEqual(1, hits, $"{shape.GetType().Name} ('{shape}') matched {hits} predicates");
        }
    }

    /// <summary>
    /// Bounds that collapse are normalized to the empty variant at construction, so the
    /// predicates report the shape the value actually has, not the one the caller asked for.
    /// </summary>
    [TestMethod]
    public void DegenerateFinite_ReportsAsEmptyNotFinite()
    {
        var collapsed = Int32Range.CreateFinite(5, 5, startInclusive: false, endInclusive: false);

        Assert.IsTrue(collapsed.IsEmpty());
        Assert.IsFalse(collapsed.IsFinite());
    }

    [TestMethod]
    public void Predicates_WorkAcrossElementTypes()
    {
        Assert.IsTrue(DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 1)).IsFinite());
        Assert.IsTrue(DecimalRange.Infinite.IsInfinity());
        Assert.IsTrue(TimeRange.Empty.IsEmpty());
        Assert.IsTrue(Int64Range.CreateUnboundedEnd(10L).IsUnboundedEnd());
        Assert.IsTrue(DateTimeOffsetRange.CreateUnboundedStart(DateTimeOffset.UnixEpoch, true).IsUnboundedStart());
    }
}

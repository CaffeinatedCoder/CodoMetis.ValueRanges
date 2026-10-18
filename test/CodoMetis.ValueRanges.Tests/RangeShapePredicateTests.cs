using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// The five shape predicates on <see cref="IRange{T}"/> — <c>isempty</c>, <c>lower_inf</c>,
/// <c>upper_inf</c> and the two derived ones. <c>IsEmpty</c>, <c>IsFinite</c> and <c>IsInfinity</c>
/// each hold for one variant; <c>IsUnboundedStart</c> and <c>IsUnboundedEnd</c> are
/// <c>lower_inf</c> and <c>upper_inf</c>, so each holds for its half-unbounded variant <em>and</em>
/// for the infinite range, which is what lets the EF translator map <c>IsInfinity</c>/<c>IsFinite</c>
/// onto combinations of the PostgreSQL predicates. Until 10.0.0 the two tested the variant, and
/// <c>(,)</c> answered <see langword="false"/> in memory and <see langword="true"/> on the server (#109).
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
        => AssertHoldsOnlyFor(predicate, [expected], name);

    /// <summary>Asserts <paramref name="predicate"/> holds for exactly the shapes in <paramref name="expected"/>.</summary>
    private static void AssertHoldsOnlyFor(Func<Int32Range, bool> predicate, Int32Range[] expected, string name)
    {
        foreach (var shape in AllShapes)
            Assert.AreEqual(expected.Any(e => ReferenceEquals(shape, e) || shape.Equals(e)), predicate(shape),
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
    public void IsUnboundedStart_HoldsForTheLeftUnboundedAndTheInfiniteRange()
        => AssertHoldsOnlyFor(r => r.IsUnboundedStart(), [Int32Range.CreateUnboundedStart(50, true), Int32Range.Infinite],
            nameof(RangeExtensions.IsUnboundedStart));

    [TestMethod]
    public void IsUnboundedEnd_HoldsForTheRightUnboundedAndTheInfiniteRange()
        => AssertHoldsOnlyFor(r => r.IsUnboundedEnd(), [Int32Range.CreateUnboundedEnd(10, true), Int32Range.Infinite],
            nameof(RangeExtensions.IsUnboundedEnd));

    /// <summary>
    /// <c>lower_inf('(,)')</c> and <c>upper_inf('(,)')</c> are both <see langword="true"/> in PostgreSQL,
    /// and so is the one-element <see cref="RangeSet{TRange,T}"/> holding it; the range answered
    /// <see langword="false"/> until 10.0.0.
    /// </summary>
    [TestMethod]
    public void Infinite_IsUnboundedAtBothEnds_AsLowerInfAndUpperInfAre()
    {
        Assert.IsTrue(Int32Range.Infinite.IsUnboundedStart());
        Assert.IsTrue(Int32Range.Infinite.IsUnboundedEnd());
        Assert.IsTrue(DecimalRange.Infinite.IsUnboundedStart());
        Assert.IsTrue(DateRange.Infinite.IsUnboundedEnd());

        Assert.AreEqual(RangeSet<Int32Range, int>.From(Int32Range.Infinite).IsUnboundedStart(), Int32Range.Infinite.IsUnboundedStart());
        Assert.AreEqual(RangeSet<Int32Range, int>.From(Int32Range.Infinite).IsUnboundedEnd(), Int32Range.Infinite.IsUnboundedEnd());
    }

    /// <summary>
    /// The variants still partition the union: three of the predicates name one each, and the two
    /// half-unbounded variants are the unbounded checks <em>less</em> the infinite range.
    /// </summary>
    [TestMethod]
    public void EveryShape_IsExactlyOneVariant()
    {
        foreach (var shape in AllShapes)
        {
            var hits = new[]
            {
                shape.IsEmpty(), shape.IsInfinity(), shape.IsFinite(),
                shape.IsUnboundedStart() && !shape.IsInfinity(), shape.IsUnboundedEnd() && !shape.IsInfinity()
            }.Count(hit => hit);

            Assert.AreEqual(1, hits, $"{shape.GetType().Name} ('{shape}') matched {hits} predicates");
            Assert.AreEqual(shape is IUnboundedStartRange<int>, shape.IsUnboundedStart() && !shape.IsInfinity());
            Assert.AreEqual(shape is IUnboundedEndRange<int>, shape.IsUnboundedEnd() && !shape.IsInfinity());
        }
    }

    /// <summary>
    /// The derived pair is what the translator composes from <c>lower_inf</c>, <c>upper_inf</c> and
    /// <c>isempty</c>, so it must follow from the three in memory too.
    /// </summary>
    [TestMethod]
    public void IsInfinityAndIsFinite_FollowFromTheUnboundedChecks()
    {
        foreach (var shape in AllShapes)
        {
            Assert.AreEqual(shape.IsUnboundedStart() && shape.IsUnboundedEnd(), shape.IsInfinity(), $"IsInfinity on '{shape}'");
            Assert.AreEqual(!shape.IsUnboundedStart() && !shape.IsUnboundedEnd() && !shape.IsEmpty(), shape.IsFinite(), $"IsFinite on '{shape}'");
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

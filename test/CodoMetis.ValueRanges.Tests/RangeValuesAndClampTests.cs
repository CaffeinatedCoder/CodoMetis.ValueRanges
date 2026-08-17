namespace CodoMetis.ValueRanges.Tests;

/// <summary>
/// Enumerating a discrete range's values, and snapping a value into a range. The two operations
/// that treat a range as a container of values rather than as a pair of bounds.
/// </summary>
[TestClass]
public class RangeValuesAndClampTests
{
    // -------------------------------------------------------------------------
    // Values()
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Values_FiniteDiscrete_YieldsEveryValueAscending()
        => CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4, 5 },
            Int32Range.CreateFinite(1, 5).Values().ToList());

    [TestMethod]
    public void Values_IncludesBothBounds()
    {
        var values = Int32Range.CreateFinite(3, 7).Values().ToList();

        Assert.AreEqual(3, values[0]);
        Assert.AreEqual(7, values[^1]);
        Assert.AreEqual(5, values.Count, "the canonical form is closed, so both ends are values");
    }

    [TestMethod]
    public void Values_HalfOpenSpelling_MatchesItsClosedForm()
        => CollectionAssert.AreEqual(
            Int32Range.CreateFinite(1, 5).Values().ToList(),
            Int32Range.CreateFinite(1, 6, true, false).Values().ToList());

    [TestMethod]
    public void Values_Dates_WalksDayByDay()
        => CollectionAssert.AreEqual(
            new[]
            {
                new DateOnly(2024, 2, 28), new DateOnly(2024, 2, 29), new DateOnly(2024, 3, 1)
            },
            DateRange.CreateFinite(new DateOnly(2024, 2, 28), new DateOnly(2024, 3, 1)).Values().ToList());

    [TestMethod]
    public void Values_SingleValueRange_YieldsThatValue()
        => CollectionAssert.AreEqual(new[] { 42 }, Int32Range.CreateFinite(42, 42).Values().ToList());

    [TestMethod]
    public void Values_Empty_YieldsNothing()
        => Assert.AreEqual(0, Int32Range.Empty.Values().Count());

    /// <summary>
    /// A range closed at the domain maximum has no successor to step to. The walk must stop
    /// there rather than spinning on a null step.
    /// </summary>
    [TestMethod]
    public void Values_EndingAtTheDomainMaximum_Terminates()
    {
        var atMax = Int32Range.CreateFinite(int.MaxValue - 2, int.MaxValue);

        // The same at the other end, where a decrementing step could underflow instead.
        CollectionAssert.AreEqual(
            new[] { int.MinValue, int.MinValue + 1, int.MinValue + 2 },
            Int32Range.CreateFinite(int.MinValue, int.MinValue + 2).Values().ToArray());

        CollectionAssert.AreEqual(
            new[] { int.MaxValue - 2, int.MaxValue - 1, int.MaxValue },
            atMax.Values().ToList());
    }

    /// <summary>
    /// The same termination guarantee for the other core discrete domains, which had it only for
    /// <see cref="Int32Range"/>. A step that overflowed instead of stopping does not throw — it
    /// wraps to the domain minimum and enumerates forever, so the failure mode is a hang.
    /// </summary>
    [TestMethod]
    public void Values_AtTheDomainMaximum_TerminatesForEveryDiscreteType()
    {
        CollectionAssert.AreEqual(
            new[] { long.MaxValue - 2, long.MaxValue - 1, long.MaxValue },
            Int64Range.CreateFinite(long.MaxValue - 2, long.MaxValue).Values().ToArray());

        CollectionAssert.AreEqual(
            new[] { long.MinValue, long.MinValue + 1, long.MinValue + 2 },
            Int64Range.CreateFinite(long.MinValue, long.MinValue + 2).Values().ToArray());

        CollectionAssert.AreEqual(
            new[] { DateOnly.MaxValue.AddDays(-2), DateOnly.MaxValue.AddDays(-1), DateOnly.MaxValue },
            DateRange.CreateFinite(DateOnly.MaxValue.AddDays(-2), DateOnly.MaxValue).Values().ToArray());

        CollectionAssert.AreEqual(
            new[] { DateOnly.MinValue, DateOnly.MinValue.AddDays(1), DateOnly.MinValue.AddDays(2) },
            DateRange.CreateFinite(DateOnly.MinValue, DateOnly.MinValue.AddDays(2)).Values().ToArray());
    }

    /// <summary>
    /// A continuous range type does not declare <c>Values()</c> at all, so asking for them is a
    /// compile error rather than a runtime failure — <c>DecimalRange.CreateFinite(1m, 5m).Values()</c>
    /// does not build. Asserted by reflection because the compiler check cannot be expressed as
    /// a test, and this is the observable consequence of it.
    /// </summary>
    [TestMethod]
    public void Values_IsDeclaredByDiscreteTypesOnly()
    {
        foreach (var discrete in new[] { typeof(Int32Range), typeof(Int64Range), typeof(DateRange) })
            Assert.IsNotNull(discrete.GetMethod("Values"), $"{discrete.Name} should enumerate");

        foreach (var continuous in new[]
                 {
                     typeof(DecimalRange), typeof(DateTimeRange),
                     typeof(DateTimeOffsetRange), typeof(TimeRange)
                 })
            Assert.IsNull(
                continuous.GetMethod("Values"),
                $"{continuous.Name} is continuous and must not offer Values() — there is no step to walk");
    }

    /// <summary>
    /// The unbounded refusal is eager. An iterator would defer it to the first <c>MoveNext</c>,
    /// surfacing the failure at the <c>foreach</c> rather than at the call that was wrong — so
    /// this asserts the throw happens without enumerating at all.
    /// </summary>
    [TestMethod]
    public void Values_Unbounded_ThrowsAtTheCallNotAtEnumeration()
    {
        Assert.ThrowsExactly<NotSupportedException>(() => Int32Range.CreateUnboundedEnd(1).Values());
        Assert.ThrowsExactly<NotSupportedException>(() => Int32Range.CreateUnboundedStart(1, true).Values());
        Assert.ThrowsExactly<NotSupportedException>(() => Int32Range.Infinite.Values());
    }

    [TestMethod]
    public void Values_AgreesWithContains()
    {
        var range = Int32Range.CreateFinite(-2, 3);

        foreach (var value in range.Values())
            Assert.IsTrue(range.Contains(value), $"Values() yielded {value}, which Contains rejects");

        Assert.AreEqual(6, range.Values().Count());
    }

    // -------------------------------------------------------------------------
    // Clamp()
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Clamp_ValueInside_ReturnsItUnchanged()
        => Assert.AreEqual(5, Int32Range.CreateFinite(1, 10).Clamp(5));

    [TestMethod]
    public void Clamp_ValueBelowOrAbove_ReturnsTheNearestBound()
    {
        var range = Int32Range.CreateFinite(1, 10);

        Assert.AreEqual(1, range.Clamp(-100));
        Assert.AreEqual(10, range.Clamp(100));
    }

    [TestMethod]
    public void Clamp_BoundsThemselves_AreReturned()
    {
        var range = Int32Range.CreateFinite(1, 10);

        Assert.AreEqual(1, range.Clamp(1));
        Assert.AreEqual(10, range.Clamp(10));
    }

    [TestMethod]
    public void Clamp_Empty_ReturnsNull()
        => Assert.IsNull(Int32Range.Empty.Clamp(5));

    [TestMethod]
    public void Clamp_Infinite_ReturnsTheValue()
        => Assert.AreEqual(5, Int32Range.Infinite.Clamp(5));

    /// <summary>An unbounded side never constrains — only the bounded one can pull a value in.</summary>
    [TestMethod]
    public void Clamp_Unbounded_ConstrainsOnlyTheBoundedSide()
    {
        var upTo = Int32Range.CreateUnboundedStart(10, true);   // (-∞, 10]
        Assert.AreEqual(10, upTo.Clamp(100));
        Assert.AreEqual(int.MinValue, upTo.Clamp(int.MinValue));

        var from = Int32Range.CreateUnboundedEnd(10);           // [10, +∞)
        Assert.AreEqual(10, from.Clamp(-100));
        Assert.AreEqual(int.MaxValue, from.Clamp(int.MaxValue));
    }

    [TestMethod]
    public void Clamp_ResultIsContained_ForDiscreteRanges()
    {
        var range = Int32Range.CreateFinite(1, 10);

        foreach (var probe in new[] { int.MinValue, -1, 0, 1, 5, 10, 11, int.MaxValue })
            Assert.IsTrue(range.Contains(range.Clamp(probe)!.Value), $"clamping {probe} left the range");
    }

    [TestMethod]
    public void Clamp_Dates()
    {
        var year = DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        Assert.AreEqual(new DateOnly(2024, 1, 1), year.Clamp(new DateOnly(2020, 5, 5)));
        Assert.AreEqual(new DateOnly(2024, 12, 31), year.Clamp(new DateOnly(2030, 5, 5)));
        Assert.AreEqual(new DateOnly(2024, 6, 15), year.Clamp(new DateOnly(2024, 6, 15)));
    }
}

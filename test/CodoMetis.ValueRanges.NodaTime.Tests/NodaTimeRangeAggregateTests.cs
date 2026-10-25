using CodoMetis.ValueRanges.Tests;

namespace CodoMetis.ValueRanges.NodaTime.Tests;

/// <summary>
/// The NodaTime package declares its own <c>RangeAgg</c> and <c>RangeIntersectAgg</c> overloads, one
/// per range type, so they are checked apart from the core ones by the same linked sweep.
/// </summary>
[TestClass]
public class NodaTimeRangeAggregateTests
{
    [TestMethod]
    public void EveryRangeOverload_SkipsANullRangeWhereverItStands()
    {
        var failures = AggregateNullInputs.Disagreements(typeof(NodaTimeRangeAggregateExtensions), minimumOverloads: 8);

        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }
}

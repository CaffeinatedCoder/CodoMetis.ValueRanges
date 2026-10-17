namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every public member of the shipping assemblies is named by a claim's surfaces or waived with a
/// reason, so an API cannot grow without deciding which oracle holds it.
/// </summary>
/// <remarks>
/// <para>
/// This is the API-surface half of the catalogue. <see cref="ApiSurface"/> enumerates the members
/// by reflection; <c>test/oracles/claims.json</c> says which a claim covers;
/// <c>test/oracles/surface-waivers.json</c> says which need no oracle and why (an accessor whose
/// value a sweep reads anyway, a converter's <c>CanConvert</c>, the plumbing EF Core calls). The
/// first run listed every public member as uncovered; the classification is what the two files
/// record, and a new member reopens the question.
/// </para>
/// <para>
/// A waiver that no longer matches an unclaimed member is stale and fails too, so the waiver list
/// shrinks as oracles grow rather than hiding members that have since been claimed.
/// </para>
/// </remarks>
[TestClass]
public sealed class OracleSurfaceCoverageTests
{
    // 1,000 at 9.0.0. A floor against the enumeration going quiet.
    private const int KnownMembers = 1000;

    [TestMethod]
    public void Surface_IsEnumerated()
    {
        Assert.IsTrue(ApiSurface.Members.Count >= KnownMembers,
                      $"Enumerated {ApiSurface.Members.Count} public members, fewer than the {KnownMembers} known to exist.");
        StringAssert.Contains(string.Join("\n", ApiSurface.Members), "RangeExtensions.Contains");
        StringAssert.Contains(string.Join("\n", ApiSurface.Members), "Int32Range+Finite.Start");
        StringAssert.Contains(string.Join("\n", ApiSurface.Members), "RangeSet.Union");
    }

    [TestMethod]
    public void EveryPublicMember_IsClaimedOrWaived()
    {
        var claimed = Claimed();
        var waived  = OracleCatalogue.SurfaceWaivers.Keys.SelectMany(ApiSurface.Matching).ToHashSet(StringComparer.Ordinal);

        var uncovered = ApiSurface.Members.Where(member => !claimed.Contains(member) && !waived.Contains(member)).ToList();

        Assert.AreEqual(0, uncovered.Count,
                        $"{uncovered.Count} public members are named by no claim and no waiver. Either a document promises "
                      + "something about them — then add the claim to test/oracles/claims.json — or they need no oracle, "
                      + "and test/oracles/surface-waivers.json says why:" + Environment.NewLine
                      + string.Join(Environment.NewLine, uncovered.Select(member => "  " + member)));
    }

    [TestMethod]
    public void NoWaiver_IsStale()
    {
        var claimed = Claimed();
        var stale   = new List<string>();

        foreach (var (glob, reason) in OracleCatalogue.SurfaceWaivers)
        {
            var members = ApiSurface.Matching(glob);
            if (members.Count == 0)
                stale.Add($"'{glob}' matches no public member");
            else if (members.All(claimed.Contains))
                stale.Add($"'{glob}' matches only members a claim already covers ({members.Count}) — remove the waiver");
            if (reason.Trim().Length < 10)
                stale.Add($"'{glob}' needs a reason");
        }

        Assert.AreEqual(0, stale.Count, string.Join(Environment.NewLine, stale.Prepend("Waivers to clean up:")));
    }

    private static HashSet<string> Claimed() =>
        OracleCatalogue.Claims
                       .SelectMany(claim => claim.Surfaces)
                       .Distinct()
                       .SelectMany(ApiSurface.Matching)
                       .ToHashSet(StringComparer.Ordinal);
}

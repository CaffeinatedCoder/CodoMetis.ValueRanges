using CodoMetis.ValueRanges.Oracles;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The clone-divergence sweep as a gate: every hand-copied type family under <c>src/</c> is compared
/// sibling by sibling (<c>test/oracles/CloneSweep.cs</c>), and every divergence must be waived in
/// <c>test/oracles/clone-waivers.json</c> with a reason.
/// </summary>
/// <remarks>
/// <para>
/// Bugs at one API site recur at its siblings — the NodaTime set files each carry five or six fix
/// commits, wrapper arities lag their primitive set, the 9.1.0 whitespace fix landed as two commits.
/// The claim-driven oracles hold a documented promise per type and cannot notice that one sibling
/// lacks a line the others have. This can, from the source text alone, without running the library:
/// a member present in all but one or two siblings, a member only one sibling has, and a body that
/// no sibling shares while a near-identical one is shared by several.
/// </para>
/// <para>
/// A divergence is not a defect by itself — the format constant of <c>DateSet</c> differs from
/// <c>TimeSet</c>'s on purpose — so each one is either explained in the waiver list or fails here.
/// A waiver that no longer matches fails too, so the list cannot outlive what it explains.
/// <c>dotnet run tools/oracles.cs clones</c> prints every divergence with its waiver status and the
/// JSON line to paste for a new one.
/// </para>
/// </remarks>
[TestClass]
public sealed class CloneDivergenceTests
{
    /// <summary>
    /// The families the sweep must find, with the fewest siblings each is known to have. A sweep
    /// that discovers nothing finds nothing, so a glob or a parser that stops matching has to fail
    /// here rather than pass vacuously.
    /// </summary>
    private static readonly (string Family, int AtLeast)[] KnownFamilies =
    [
        ("core value sets", 10),
        ("core wrapper arities", 10),
        ("nodatime value sets", 5),
        ("nodatime wrapper arities", 5),
        ("core ranges", 7),
        ("nodatime ranges", 4),
        ("efcore range definitions", 10),
        ("efcore set definitions", 14),
        ("efcore wrapper bridges", 14),
        ("primitive vs arity", 15)
    ];

    private static readonly IReadOnlyList<CloneFamily> Families = CloneSweep.DiscoverFamilies(RepoLayout.Root.FullName);

    private static readonly IReadOnlyList<CloneDivergence> Divergences = Families.SelectMany(CloneSweep.Compare).ToList();

    private static readonly IReadOnlyList<CloneWaiver> Waivers = CloneSweep.LoadWaivers(RepoLayout.Root.FullName);

    [TestMethod]
    public void EveryKnownFamily_IsDiscoveredWithItsSiblings()
    {
        var problems = new List<string>();
        foreach (var (name, atLeast) in KnownFamilies)
        {
            var family = Families.FirstOrDefault(f => f.Name == name);
            if (family is null) problems.Add($"'{name}' was not discovered");
            else if (family.Units.Count < atLeast) problems.Add($"'{name}' has {family.Units.Count} siblings, fewer than the {atLeast} known to exist");
        }

        foreach (var unit in Families.SelectMany(f => f.Units.Select(u => (f.Name, Unit: u))).Where(x => x.Unit.Members.Count == 0))
            problems.Add($"'{unit.Name}' sibling '{unit.Unit.Name}' has no members — the parser found nothing to compare");

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public void EveryDivergence_IsWaivedWithAReason()
    {
        var unwaived = Divergences.Where(d => !Waivers.Any(w => w.Matches(d))).ToList();

        Assert.AreEqual(0, unwaived.Count,
                        $"{unwaived.Count} divergence(s) between hand-copied siblings have no waiver. Each is a sibling that lacks a "
                      + "line the others have, carries one nobody else does, or drifted from a body the others share. Fix the "
                      + "sibling, or add the line to test/oracles/clone-waivers.json with the reason it is right as it is "
                      + "(`dotnet run tools/oracles.cs clones` prints the same list):" + Environment.NewLine
                      + string.Join(Environment.NewLine, unwaived.Select(d => $"  {d.Key}{Environment.NewLine}    {d.Detail}{Environment.NewLine}    {d.WaiverJson}")));
    }

    [TestMethod]
    public void EveryWaiver_HasAReason()
    {
        var silent = Waivers.Where(w => string.IsNullOrWhiteSpace(w.Reason)).Select(w => w.Key).ToList();
        Assert.AreEqual(0, silent.Count, "A waiver without a reason explains nothing: " + string.Join("; ", silent));
    }

    [TestMethod]
    public void EveryWaiver_StillMatchesADivergence()
    {
        var stale = Waivers.Where(w => !Divergences.Any(w.Matches)).Select(w => w.Key).ToList();
        Assert.AreEqual(0, stale.Count,
                        "A waiver matches no divergence any more — the siblings agree now, so remove the entry: "
                      + string.Join("; ", stale));
    }

    [TestMethod]
    public void NoWaiver_IsListedTwice()
    {
        var duplicates = Waivers.GroupBy(w => w.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.AreEqual(0, duplicates.Count, "Duplicate waiver entries: " + string.Join("; ", duplicates));
    }
}

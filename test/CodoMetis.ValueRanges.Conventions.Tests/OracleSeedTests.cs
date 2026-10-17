using System.Diagnostics;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every registered oracle ships a seed, and every seed still plants its defect.
/// </summary>
/// <remarks>
/// <para>
/// A test that passes with and without a defect is not a guard. The repository's rule has always
/// been to prove a guard by reverting its fix (<c>.claude/skills/verify-the-guard</c>); a seed is
/// that revert kept as a file. <c>test/oracles/seeds/&lt;defect&gt;.patch</c> changes <c>src/</c>
/// only, names the oracle classes it fails in an <c>Oracles:</c> header line, and was shown to fail
/// them by applying it locally (<c>dotnet run tools/oracles.cs seeds</c>).
/// </para>
/// <para>
/// These tests hold without building anything: an oracle without a seed, a seed that names no
/// oracle, a seed that touches a test, and a seed a refactor has stranded all fail here. Whether a
/// seed is still <em>caught</em> is the local tool's job — it takes a worktree, a build and a run
/// per seed, which is too slow for every CI run and too important to skip before a release.
/// </para>
/// </remarks>
[TestClass]
public sealed class OracleSeedTests
{
    private const int KnownOracles = 12;

    [TestMethod]
    public void EveryOracle_IsATestClassInItsProject()
    {
        var oracles = OracleCatalogue.Oracles;
        Assert.IsTrue(oracles.Count >= KnownOracles, $"Found {oracles.Count} registered oracles, fewer than the {KnownOracles} known to exist.");

        var misplaced = oracles
                       .Where(oracle => !OracleCatalogue.TestClasses.TryGetValue(oracle.Class, out var project) || project != oracle.Project)
                       .Select(oracle => $"{oracle.Class} (registered in {oracle.Project}, found in {(OracleCatalogue.TestClasses.TryGetValue(oracle.Class, out var p) ? p : "no project")})")
                       .ToList();

        Assert.AreEqual(0, misplaced.Count, "oracles.json names a class that is not a [TestClass] in that project: " + string.Join(", ", misplaced));
    }

    [TestMethod]
    public void EveryOracle_HasASeed()
    {
        var seeded   = OracleCatalogue.Seeds.SelectMany(seed => seed.Oracles).ToHashSet(StringComparer.Ordinal);
        var unproven = OracleCatalogue.Oracles.Select(oracle => oracle.Class).Where(oracle => !seeded.Contains(oracle)).ToList();

        Assert.AreEqual(0, unproven.Count,
                        "An oracle has no seed: add test/oracles/seeds/<defect>.patch with an 'Oracles:' line naming it, "
                      + "and prove it is caught with `dotnet run tools/oracles.cs seeds`: " + string.Join(", ", unproven));
    }

    [TestMethod]
    public void EverySeed_NamesADefectAndRegisteredOracles()
    {
        Assert.IsTrue(OracleCatalogue.Seeds.Count > 0, "No seeds under test/oracles/seeds/.");

        var oracles  = OracleCatalogue.Oracles.Select(oracle => oracle.Class).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var seed in OracleCatalogue.Seeds)
        {
            if (seed.Defect.Length == 0) problems.Add($"{seed.File.Name}: no 'Seed:' header line describing the defect");
            if (seed.Oracles.Count == 0) problems.Add($"{seed.File.Name}: no 'Oracles:' header line naming the classes it fails");
            foreach (var oracle in seed.Oracles.Where(oracle => !oracles.Contains(oracle)))
                problems.Add($"{seed.File.Name}: names '{oracle}', which oracles.json does not register");
            if (seed.Body.Length == 0) problems.Add($"{seed.File.Name}: holds no diff");
        }

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public void EverySeed_TouchesOnlySource()
    {
        var problems = OracleCatalogue.Seeds
                                      .SelectMany(seed => seed.ChangedFiles.Select(file => (seed.File.Name, file)))
                                      .Where(pair => !pair.file.StartsWith("src/", StringComparison.Ordinal))
                                      .Select(pair => $"{pair.Name} changes '{pair.file}'")
                                      .ToList();

        Assert.AreEqual(0, problems.Count, "A seed plants a defect in the library, never in a test: " + string.Join(", ", problems));
    }

    [TestMethod]
    public void EverySeed_AppliesToTheCurrentTree()
    {
        var problems = new List<string>();

        foreach (var seed in OracleCatalogue.Seeds)
        {
            var (exit, output) = Git("apply", "--check", seed.File.FullName);
            if (exit != 0) problems.Add($"{seed.File.Name} no longer applies — refresh it against src/: {output.Trim()}");
        }

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    private static (int Exit, string Output) Git(params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory       = RepoLayout.Root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var git = Process.Start(info) ?? throw new InvalidOperationException("git could not be started; the seed checks need it on PATH.");
        var error  = git.StandardError.ReadToEndAsync();
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        return (git.ExitCode, output + error.Result);
    }
}

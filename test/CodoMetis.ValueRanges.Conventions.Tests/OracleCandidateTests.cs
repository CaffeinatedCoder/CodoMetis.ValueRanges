using CodoMetis.ValueRanges.Oracles;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The candidate registry (<c>test/oracles/candidates.json</c>) names only registered oracles and
/// tracks every entry to an issue or to a labelled failure.
/// </summary>
/// <remarks>
/// A candidate is a disagreement an oracle found that is not fixed yet. <see cref="Candidates"/>
/// decides per sweep what a listed row means; these tests only keep the file well-formed, because a
/// misspelt oracle name would match nothing and the row would silently keep failing as unknown.
/// </remarks>
[TestClass]
public sealed class OracleCandidateTests
{
    [TestMethod]
    public void EveryCandidate_NamesARegisteredOracleAndARow()
    {
        var oracles  = OracleCatalogue.Oracles.Select(oracle => oracle.Class).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var candidate in Candidates.All)
        {
            if (!oracles.Contains(candidate.Oracle))
                problems.Add($"'{candidate.Row}' names oracle '{candidate.Oracle}', which oracles.json does not register");
            if (candidate.Row.Trim().Length < 3)
                problems.Add($"a candidate of {candidate.Oracle} has no row text");
            if (candidate.Family.Trim().Length == 0)
                problems.Add($"'{candidate.Row}' has no family label");
            if (candidate.Method is { } method && !System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(SourceOf(candidate.Oracle)), $@"\b{System.Text.RegularExpressions.Regex.Escape(method)}\s*\("))
                problems.Add($"'{candidate.Row}' is scoped to method '{method}', which {candidate.Oracle} does not declare");
            if (candidate.Issue.Length > 0 && !candidate.Issue.StartsWith('#') && !candidate.Issue.StartsWith("http", StringComparison.Ordinal))
                problems.Add($"'{candidate.Row}' has an issue that is neither '#<n>' nor a URL: '{candidate.Issue}'");
        }

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    private static string SourceOf(string oracle) =>
        Directory.EnumerateFiles(Path.Combine(RepoLayout.Root.FullName, "test"), oracle + ".cs", SearchOption.AllDirectories)
                 .First(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [TestMethod]
    public void Candidates_AreUniquePerOracle()
    {
        var duplicates = Candidates.All
                                   .GroupBy(candidate => (candidate.Oracle, candidate.Row))
                                   .Where(group => group.Count() > 1)
                                   .Select(group => $"{group.Key.Oracle}: '{group.Key.Row}'")
                                   .ToList();

        Assert.AreEqual(0, duplicates.Count, "Duplicate candidates: " + string.Join(", ", duplicates));
    }
}

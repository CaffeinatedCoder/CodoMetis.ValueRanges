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
            if (candidate.Issue.Length > 0 && !candidate.Issue.StartsWith('#') && !candidate.Issue.StartsWith("http", StringComparison.Ordinal))
                problems.Add($"'{candidate.Row}' has an issue that is neither '#<n>' nor a URL: '{candidate.Issue}'");
        }

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

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

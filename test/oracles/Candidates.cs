using System.Text.Json;

namespace CodoMetis.ValueRanges.Oracles;

/// <summary>
/// The known disagreements of an oracle, read from <c>test/oracles/candidates.json</c>, and the
/// verdict a sweep reaches once its failures are matched against them.
/// </summary>
/// <remarks>
/// <para>
/// An oracle sweep collects one line per disagreement and hands them here instead of asserting the
/// list empty. A line that matches no candidate fails. A line that matches a candidate whose issue
/// is still empty fails too, labelled <c>[known candidate: family]</c>, so nothing is quarantined
/// before it is filed. A line that matches a candidate with an issue is reported as
/// <c>SKIPPED [known candidate: family, issue]</c> on the test output and does not fail. A candidate
/// that no line matched fails with <c>candidate fixed: remove the entry</c>, so the registry can
/// never outlive the defect it recorded.
/// </para>
/// <para>
/// A candidate matches a failure line when the line contains the candidate's <c>row</c> text
/// (ordinal). The row is therefore the stable prefix of the sweep's own failure line — the operands
/// and the operation, never the actual value, which changes with the fix.
/// </para>
/// <para>
/// This file is linked into every test project that holds an oracle rather than referenced, so the
/// conventions project keeps no reference to the projects that need Docker.
/// </para>
/// </remarks>
public static class Candidates
{
    /// <summary>
    /// One known disagreement: the oracle class, the row's stable text, its family and the issue that
    /// tracks it. <paramref name="Method"/> scopes the row to one test method of the class; without it
    /// the row belongs to every sweep of the class, and an oracle whose methods have disjoint rows
    /// would read another method's rows as fixed.
    /// </summary>
    public sealed record Candidate(string Oracle, string Row, string Family, string Issue, string? Method = null);

    private static readonly Lazy<IReadOnlyList<Candidate>> s_all = new(Load);

    /// <summary>Every registered candidate, for the convention tests that validate the registry.</summary>
    public static IReadOnlyList<Candidate> All => s_all.Value;

    /// <summary>Candidates registered for <paramref name="oracle"/>.</summary>
    public static IReadOnlyList<Candidate> For(string oracle) =>
        [.. All.Where(c => string.Equals(c.Oracle, oracle, StringComparison.Ordinal))];

    /// <summary>
    /// Passes when every failure line is a filed candidate and every candidate of the oracle still
    /// fails; otherwise fails with <paramref name="header"/> followed by the offending lines.
    /// </summary>
    /// <param name="oracle">The oracle class, as registered in <c>test/oracles/oracles.json</c>.</param>
    /// <param name="failures">The sweep's disagreements, one line each.</param>
    /// <param name="header">The first line of the failure message, naming the sweep.</param>
    /// <param name="shown">How many lines of the failure message to show.</param>
    /// <param name="method">The calling test method; a candidate scoped to another method is not this sweep's.</param>
    /// <param name="requireEveryCandidate">
    /// Whether a candidate no line matched fails as fixed. A sweep whose rows depend on a seed passes
    /// <see langword="false"/> when run with another seed than the one its candidates were drawn from:
    /// there, not meeting a candidate is not evidence that it was fixed.
    /// </param>
    /// <param name="rowPrefix">
    /// The label this call's failure lines start with, for a data-driven sweep whose test method runs
    /// once per row (one type, one option set): only a candidate whose <c>row</c> starts with it is this
    /// call's, so a candidate of another row is neither matched nor judged fixed here. Without it every
    /// candidate of the oracle (and method) is judged by every row.
    /// </param>
    public static void Verdict(string oracle, IReadOnlyList<string> failures, string header, int shown = 25, string? method = null,
                               bool requireEveryCandidate = true, string? rowPrefix = null)
    {
        var known   = For(oracle).Where(c => c.Method is null || method is null || string.Equals(c.Method, method, StringComparison.Ordinal))
                                 .Where(c => rowPrefix is null || c.Row.StartsWith(rowPrefix, StringComparison.Ordinal))
                                 .ToList();
        Judge(known, failures, header, shown, requireEveryCandidate);
    }

    /// <summary>
    /// <see cref="Verdict"/> over the candidates that apply to this call, given directly: what
    /// <c>SeedVerdictTests</c> drives with an unfiled candidate, which the registry never holds for long.
    /// </summary>
    internal static void Judge(IReadOnlyList<Candidate> known, IReadOnlyList<string> failures, string header, int shown = 25,
                               bool requireEveryCandidate = true)
    {
        var failing = new List<string>();
        var unfiled = new List<string>();
        var matched = new HashSet<Candidate>();

        foreach (var line in failures)
        {
            var candidate = known.FirstOrDefault(c => line.Contains(c.Row, StringComparison.Ordinal));
            if (candidate is null)
            {
                failing.Add(line);
                continue;
            }

            matched.Add(candidate);
            if (candidate.Issue.Length == 0)
                unfiled.Add($"[known candidate: {candidate.Family}] {line.TrimStart()}");
            else
                Console.WriteLine($"SKIPPED [known candidate: {candidate.Family}, {candidate.Issue}] {line.TrimStart()}");
        }

        foreach (var fixedCandidate in known.Where(c => requireEveryCandidate && !matched.Contains(c)))
            failing.Add($"candidate fixed: remove the entry for '{fixedCandidate.Row}' ({fixedCandidate.Family}) from test/oracles/candidates.json");

        // Known rows last, so a message cut short hides them first: SeedVerdict (tools/oracles.cs seeds)
        // reads a message whose every shown row is known as one whose hidden rows are known too (#55).
        failing.AddRange(unfiled);
        if (failing.Count == 0) return;

        throw new AssertFailedException(
            $"{header} ({failing.Count} failures):" + Environment.NewLine
          + string.Join(Environment.NewLine, failing.Take(shown))
          + (failing.Count > shown ? $"{Environment.NewLine}  … and {failing.Count - shown} more" : ""));
    }

    private static IReadOnlyList<Candidate> Load()
    {
        var path = Path.Combine(RepositoryRoot(), "test", "oracles", "candidates.json");
        using var stream = File.OpenRead(path);
        var entries = JsonSerializer.Deserialize(stream, CandidatesJsonContext.Default.ListCandidate)
                   ?? throw new InvalidOperationException($"'{path}' holds no array.");
        return entries;
    }

    private static string RepositoryRoot()
    {
        const string marker = "CodoMetis.ValueRanges.slnx";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, marker))) return directory.FullName;
        }

        throw new InvalidOperationException($"Could not find '{marker}' above '{AppContext.BaseDirectory}'.");
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<Candidates.Candidate>))]
internal sealed partial class CandidatesJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

namespace CodoMetis.ValueRanges.Oracles;

/// <summary>
/// Whether a test that failed with a seed planted caught it: the rule <c>dotnet run tools/oracles.cs
/// seeds</c> applies to each failed test, linked into the conventions tests (<c>SeedVerdictTests</c>) and
/// compiled into the tool through <c>tools/Directory.Build.props</c>, so the two cannot disagree.
/// </summary>
/// <remarks>
/// <para>
/// A sweep reports through <c>Candidates.Verdict</c>: a header line, then one line per failing row,
/// where a row that matches an unfiled candidate starts with <see cref="KnownMarker"/>. Until 10.0.0 the tool
/// counted a failed test as known when its message held the marker <em>anywhere</em>, so a seed that broke
/// a row beside a known one read as missed although the sweep had caught it, and the seed proof depended on
/// which rows happened to share a method (#55). The rule is per row: a failed test catches the seed when at
/// least one of its rows is not a known candidate.
/// </para>
/// <para>
/// <c>Candidates.Verdict</c> lists the other rows before the known ones, so a message cut short
/// (<c>… and N more</c>) hides known rows first: when every row shown is known, the hidden ones are too.
/// A message with no known row at all is any other assertion, and catches.
/// </para>
/// </remarks>
public static class SeedVerdict
{
    /// <summary>The prefix <c>Candidates.Verdict</c> gives a row that matches an unfiled candidate.</summary>
    public const string KnownMarker = "[known candidate:";

    /// <summary>The line <c>Candidates.Verdict</c> ends a cut-short message with.</summary>
    public const string MorePrefix = "… and ";

    /// <summary>Whether a failed test's message holds at least one row that is not a known candidate.</summary>
    public static bool Catches(string message)
    {
        var lines = message.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (!lines.Any(IsKnown)) return true;

        // A Verdict message: the first line is its header, the last may say how many rows were cut.
        return lines.Skip(1).Any(line => !IsKnown(line) && !line.StartsWith(MorePrefix, StringComparison.Ordinal));
    }

    private static bool IsKnown(string line) => line.StartsWith(KnownMarker, StringComparison.Ordinal);
}

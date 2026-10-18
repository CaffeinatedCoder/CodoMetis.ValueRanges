using System.Globalization;
using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// SECURITY.md promises fixes for "the latest released minor version" and tabulates which line that
/// is. The table is prose: nothing ties it to <c>Directory.Build.props</c>, so a release that bumps
/// the version and forgets the table leaves the policy pointing a reporter at a line that no longer
/// receives fixes. That is not hypothetical — 6.2.0 shipped with the table still saying 6.1.x. A
/// supported-versions table that is wrong is worse than none: it reads as a considered statement.
/// It can be wrong the other way, too. Until 9.1.0 the table was compared with <c>Version</c>, which
/// names the version being prepared, so opening 9.1.0 had to promise fixes for a line that wasn't on
/// NuGet yet, and a reporter on 9.0.0 read that as "your version gets no fixes" (#79).
/// From 11.0.0 on there are two supported lines (#151), and each row is held to its own line.
/// </summary>
[TestClass]
public sealed class SecurityPolicyConventionTests
{
    private static FileInfo SecurityPolicy => new(Path.Combine(RepoLayout.Root.FullName, "SECURITY.md"));

    /// <summary>"| 6.2.x | ✅ |" — a line that receives fixes, one row per supported line.</summary>
    private static readonly Regex SupportedRow =
        new(@"^\|\s*(?<major>\d+)\.(?<minor>\d+)\.x\s*\|\s*✅\s*\|", RegexOptions.Multiline);

    /// <summary>"| &lt; 6.2 | ❌ |" — everything before the oldest supported line.</summary>
    private static readonly Regex UnsupportedRow =
        new(@"^\|\s*<\s*(?<major>\d+)\.(?<minor>\d+)\s*\|\s*❌\s*\|", RegexOptions.Multiline);

    /// <summary>"**10.x** targets **.NET 10**" — the platform a line's support horizon is anchored to.</summary>
    private static readonly Regex LineTargets =
        new(@"\*\*(?<line>\d+)\.x\*\*[^\n]*?targets \*\*\.NET (?<dotnet>\d+)\*\*", RegexOptions.None);

    /// <summary>The most lines supported at once: the LTS line and the current one (#147).</summary>
    private const int MaxSupportedLines = 2;

    [TestMethod]
    public void SecurityPolicy_Exists()
    {
        Assert.IsTrue(SecurityPolicy.Exists, $"No SECURITY.md at the repository root ({SecurityPolicy.FullName}).");
    }

    /// <summary>
    /// Each supported line names the .NET major it targets, and the line this branch prepares names
    /// the one in the core project's TargetFramework.
    /// </summary>
    /// <remarks>
    /// The horizon is phrased against the platform, so when the TFM moves the sentence has to move
    /// with it, or the policy promises support against a release the package no longer targets. Only
    /// this branch's line can be compared: the other line's TFM is on the other branch. And only
    /// once that line has a row: a branch preparing a line's first release (main at 10.0.0 while
    /// 9.1.x is the supported row, or an 11.0.0 preview) targets a platform the table does not name
    /// yet, which is right until the release ships.
    /// </remarks>
    [TestMethod]
    public void EverySupportedLine_NamesTheDotNetMajorItTargets()
    {
        var text    = File.ReadAllText(SecurityPolicy.FullName);
        var targets = LineTargets.Matches(text)
                                 .GroupBy(match => int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture))
                                 .ToDictionary(group => group.Key, group => group.First().Groups["dotnet"].Value);
        var rows    = SupportedRows(text);

        Assert.IsNotEmpty(rows, "SECURITY.md has no '| x.y.x | ✅ |' row, so no line was checked.");

        foreach (var (major, _) in rows)
        {
            Assert.IsTrue(
                targets.ContainsKey(major),
                $"SECURITY.md supports {major}.x but has no '**{major}.x** targets **.NET N**' sentence in its "
              + "support horizon — a consumer doing due diligence needs to know which platform each line's "
              + "support is tied to.");
        }

        var shipped = ReleaseVersion.Parse(RepoLayout.ShippedVersion);

        if (shipped.IsPrerelease || rows.All(row => row.Major != shipped.Major)) return;

        var core     = RepoLayout.PackableProjects.Single(project => project.PackageId == "CodoMetis.ValueRanges");
        var tfm      = core.Property("TargetFramework") ?? "";
        var tfmMajor = Regex.Match(tfm, @"^net(?<major>\d+)\.").Groups["major"].Value;

        Assert.IsFalse(string.IsNullOrEmpty(tfmMajor), $"Could not read a net<major>.<minor> TargetFramework from {core.File.Name} (got '{tfm}').");

        Assert.AreEqual(
            tfmMajor, targets[shipped.Major],
            $"SECURITY.md says {shipped.Major}.x targets .NET {targets[shipped.Major]}, but this branch "
          + $"prepares {shipped} and {core.File.Name} targets {tfm}. Move the horizon statement with the "
          + "TargetFramework.");
    }

    /// <summary>
    /// The table has one supported row per line, each naming that line's latest release: this
    /// branch's from <c>PackageValidationBaselineVersion</c>, the other line's from
    /// <c>CompanionLineRelease</c>. The unsupported row meets the oldest of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The baseline names the release already out for as long as the next one is being prepared,
    /// and it has to move when a version is opened, so the table moves with it. The commit that gets
    /// tagged still names the release before it. It can't name its own version and pass this test
    /// before its tag exists. Opening the next version straight after the tag closes that gap.
    /// </para>
    /// <para>
    /// Two lines from 11.0.0 on (#147, #151): the table gains its second row when 11.0.0 ships,
    /// not when a preview does — a preview is fixed by the next preview, and SECURITY.md says so
    /// rather than giving it a row. Neither property may name a preview for the same reason.
    /// </para>
    /// <para>
    /// The limit: <c>CompanionLineRelease</c> is this branch's record of the other line, so a minor
    /// shipped there and not yet recorded here passes on this branch. The other branch's own run
    /// checks that row against its own baseline.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TheSupportedVersionsTable_NamesTheLatestReleaseOfEachLine()
    {
        var text = File.ReadAllText(SecurityPolicy.FullName);
        var own  = ReleaseVersion.Parse(RepoLayout.LatestRelease);

        Assert.IsFalse(own.IsPrerelease, $"PackageValidationBaselineVersion is the preview {own}; a preview is never a supported line.");

        var lines = new List<ReleaseVersion> { own };

        if (RepoLayout.CompanionLineRelease is { } companionText)
        {
            var companion = ReleaseVersion.Parse(companionText);

            Assert.IsFalse(companion.IsPrerelease, $"CompanionLineRelease is the preview {companion}; a preview is never a supported line.");
            Assert.AreEqual(
                1, Math.Abs(companion.Major - own.Major),
                $"CompanionLineRelease is {companion} and this line's latest release is {own}. The two "
              + "supported lines are adjacent majors, one per .NET major (CONTRIBUTING.md, Branches and releases).");

            lines.Add(companion);
        }

        var expected = lines.OrderByDescending(version => version)
                            .Select(version => (version.Major, version.Minor))
                            .ToList();
        var rows     = SupportedRows(text);

        Assert.IsLessThanOrEqualTo(
            MaxSupportedLines, rows.Count,
            $"SECURITY.md has {rows.Count} '| x.y.x | ✅ |' rows; at most {MaxSupportedLines} lines are supported at once.");

        CollectionAssert.AreEqual(
            expected, rows,
            $"SECURITY.md lists {Describe(rows)} as supported, but the latest release of each line is "
          + $"{Describe(expected)} (PackageValidationBaselineVersion {own}"
          + (RepoLayout.CompanionLineRelease is { } c ? $", CompanionLineRelease {c}" : ", no CompanionLineRelease")
          + "), newest first. A reporter reads the table to decide whether their version still receives "
          + "fixes, so it names what is on NuGet, not the version being prepared "
          + $"({RepoLayout.ShippedVersion}). Move a row when its line's release moves.");

        var unsupported = UnsupportedRow.Match(text);

        Assert.IsTrue(
            unsupported.Success,
            "SECURITY.md should have a '| < x.y | ❌ |' row saying that everything before the oldest "
          + "supported line is unmaintained.");

        var oldest           = expected[^1];
        var unsupportedBelow = (int.Parse(unsupported.Groups["major"].Value, CultureInfo.InvariantCulture),
                                int.Parse(unsupported.Groups["minor"].Value, CultureInfo.InvariantCulture));

        Assert.AreEqual(
            oldest, unsupportedBelow,
            $"SECURITY.md says versions below {unsupportedBelow.Item1}.{unsupportedBelow.Item2} are unsupported, "
          + $"but the oldest supported line is {oldest.Major}.{oldest.Minor}.x — the two rows should meet at "
          + "the same minor, or a range is left described by neither.");
    }

    private static List<(int Major, int Minor)> SupportedRows(string text) =>
        [.. SupportedRow.Matches(text)
                        .Select(match => (int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
                                          int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture)))];

    private static string Describe(IEnumerable<(int Major, int Minor)> rows) =>
        string.Join(" and ", rows.Select(row => $"{row.Major}.{row.Minor}.x")) is { Length: > 0 } listed ? listed : "nothing";
}

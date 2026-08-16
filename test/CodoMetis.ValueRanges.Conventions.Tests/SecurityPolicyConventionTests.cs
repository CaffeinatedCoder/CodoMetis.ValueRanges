using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// SECURITY.md promises fixes for "the latest released minor version" and tabulates which line that
/// is. The table is prose: nothing ties it to <c>Directory.Build.props</c>, so a release that bumps
/// the version and forgets the table leaves the policy pointing a reporter at a line that no longer
/// receives fixes. That is not hypothetical — 6.2.0 shipped with the table still saying 6.1.x. A
/// supported-versions table that is wrong is worse than none: it reads as a considered statement.
/// </summary>
[TestClass]
public sealed class SecurityPolicyConventionTests
{
    private static FileInfo SecurityPolicy => new(Path.Combine(RepoLayout.Root.FullName, "SECURITY.md"));

    /// <summary>"| 6.2.x | ✅ |" — the one line that receives fixes.</summary>
    private static readonly Regex SupportedRow =
        new(@"^\|\s*(?<major>\d+)\.(?<minor>\d+)\.x\s*\|\s*✅\s*\|", RegexOptions.Multiline);

    /// <summary>"| &lt; 6.2 | ❌ |" — everything before it.</summary>
    private static readonly Regex UnsupportedRow =
        new(@"^\|\s*<\s*(?<major>\d+)\.(?<minor>\d+)\s*\|\s*❌\s*\|", RegexOptions.Multiline);

    /// <summary>"the current line targets **.NET 10**" — the number the horizon statement is anchored to.</summary>
    private static readonly Regex TargetsDotNet =
        new(@"targets \*\*\.NET (?<major>\d+)\*\*", RegexOptions.None);

    [TestMethod]
    public void SecurityPolicy_Exists()
    {
        Assert.IsTrue(SecurityPolicy.Exists, $"No SECURITY.md at the repository root ({SecurityPolicy.FullName}).");
    }

    /// <summary>
    /// The support horizon is phrased against the .NET major the packages target, and that number
    /// lives in the core project's TargetFramework. When the TFM moves to net11.0 the sentence has
    /// to move with it, or the policy promises support against a release the package no longer
    /// targets.
    /// </summary>
    [TestMethod]
    public void TheSupportHorizon_NamesTheTargetedDotNetMajor()
    {
        var text  = File.ReadAllText(SecurityPolicy.FullName);
        var match = TargetsDotNet.Match(text);

        Assert.IsTrue(
            match.Success,
            "SECURITY.md has no 'targets **.NET N**' sentence in its support horizon — a consumer doing "
          + "due diligence needs to know which platform line the support intention is tied to.");

        var core = RepoLayout.PackableProjects.Single(project => project.PackageId == "CodoMetis.ValueRanges");
        var tfm  = core.Property("TargetFramework") ?? "";
        var tfmMajor = Regex.Match(tfm, @"^net(?<major>\d+)\.").Groups["major"].Value;

        Assert.IsFalse(string.IsNullOrEmpty(tfmMajor), $"Could not read a net<major>.<minor> TargetFramework from {core.File.Name} (got '{tfm}').");

        Assert.AreEqual(
            tfmMajor, match.Groups["major"].Value,
            $"SECURITY.md says the current line targets .NET {match.Groups["major"].Value}, but "
          + $"{core.File.Name} targets {tfm}. Move the horizon statement with the TargetFramework.");
    }

    [TestMethod]
    public void TheSupportedVersionsTable_NamesTheShippedMinor()
    {
        var text     = File.ReadAllText(SecurityPolicy.FullName);
        var shipped  = Version.Parse(RepoLayout.ShippedVersion);
        var expected = $"{shipped.Major}.{shipped.Minor}";

        var supported = SupportedRow.Matches(text);

        Assert.AreEqual(
            1, supported.Count,
            "SECURITY.md should have exactly one '| x.y.x | ✅ |' row — the policy is that fixes land "
          + "on the latest released minor only, so there is one supported line to name.");

        var supportedLine = $"{supported[0].Groups["major"].Value}.{supported[0].Groups["minor"].Value}";

        Assert.AreEqual(
            expected, supportedLine,
            $"SECURITY.md lists {supportedLine}.x as the supported line, but Directory.Build.props ships "
          + $"{shipped}. The table is part of the version bump: a reporter reads it to decide whether "
          + "their version still receives fixes.");

        var unsupported = UnsupportedRow.Match(text);

        Assert.IsTrue(
            unsupported.Success,
            "SECURITY.md should have a '| < x.y | ❌ |' row saying that everything before the supported "
          + "line is unmaintained.");

        var unsupportedBelow = $"{unsupported.Groups["major"].Value}.{unsupported.Groups["minor"].Value}";

        Assert.AreEqual(
            expected, unsupportedBelow,
            $"SECURITY.md says versions below {unsupportedBelow} are unsupported, but the supported line "
          + $"is {expected}.x — the two rows should meet at the same minor, or a range is left "
          + "described by neither.");
    }
}

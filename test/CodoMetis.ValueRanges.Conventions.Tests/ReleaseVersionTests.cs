namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The version reader the changelog, packaging and security tests share. A changelog ordered by the
/// wrong precedence passes <c>EveryChangelog_ListsVersionsNewestFirst</c> with its previews in the
/// wrong place, and a baseline rule that thinks <c>11.0.0-preview.2</c> follows <c>11.0.0</c> picks
/// the wrong release before it.
/// </summary>
[TestClass]
public sealed class ReleaseVersionTests
{
    [TestMethod]
    [DataRow("10.0.0", "10.0.1")]
    [DataRow("10.9.0", "10.10.0")]
    [DataRow("10.2.0", "11.0.0-preview.1")]
    [DataRow("11.0.0-preview.1", "11.0.0-preview.2")]
    [DataRow("11.0.0-preview.9", "11.0.0-preview.10")]
    [DataRow("11.0.0-preview.2", "11.0.0-rc.1")]
    [DataRow("11.0.0-preview", "11.0.0-preview.1")]
    [DataRow("11.0.0-preview.10", "11.0.0")]
    public void Precedence_IsSemVers(string lower, string higher)
    {
        var low  = ReleaseVersion.Parse(lower);
        var high = ReleaseVersion.Parse(higher);

        Assert.IsTrue(low < high, $"{lower} should precede {higher}.");
        Assert.IsTrue(high > low, $"{higher} should follow {lower}.");
    }

    [TestMethod]
    [DataRow("10.1.0", true)]
    [DataRow("10.0.0", false)]
    [DataRow("10.1.1", false)]
    [DataRow("11.1.0-preview.1", false)]
    public void IsMinor_IsAMinorRelease(string version, bool minor)
    {
        Assert.AreEqual(minor, ReleaseVersion.Parse(version).IsMinor, version);
    }

    [TestMethod]
    [DataRow("10.0")]
    [DataRow("v10.0.0")]
    [DataRow("10.0.0-")]
    [DataRow("10.0.0+build.1")]
    public void Parse_RefusesWhatTheReleasePathNeverWrites(string text)
    {
        Assert.ThrowsExactly<FormatException>(() => ReleaseVersion.Parse(text));
    }
}

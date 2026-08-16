using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Guards the release record. None of this is visible at compile time: a version can ship with
/// nothing documenting what changed in it, and the first person to notice is a consumer reading
/// a NuGet page. These tests run in the same suite the release workflow gates on, so an
/// undocumented version fails the build instead.
/// </summary>
[TestClass]
public sealed class ChangelogConventionTests
{
    /// <summary>Matches a version heading: <c>## [6.1.0] — 2026-08-15</c>.</summary>
    private static readonly Regex VersionHeading =
        new(@"^##\s*\[(?<version>\d+\.\d+\.\d+)\]", RegexOptions.Multiline);

    private static IReadOnlyList<Version> VersionsIn(FileInfo changelog) =>
        [.. VersionHeading.Matches(File.ReadAllText(changelog.FullName))
                          .Select(match => Version.Parse(match.Groups["version"].Value))];

    private static FileInfo ChangelogOf(PackableProject project) =>
        new(Path.Combine(project.Directory.FullName, "CHANGELOG.md"));

    private static IEnumerable<FileInfo> AllChangelogs() =>
        [RepoLayout.RootChangelog, .. RepoLayout.PackableProjects.Select(ChangelogOf)];

    /// <summary>Matches a floating heading like <c>## [Unreleased]</c>.</summary>
    private static readonly Regex UnversionedHeading =
        new(@"^##\s*\[(?<label>[^\]]*[A-Za-z][^\]]*)\]", RegexOptions.Multiline);

    /// <summary>
    /// No changelog may park entries under a floating heading such as <c>## [Unreleased]</c>.
    /// </summary>
    /// <remarks>
    /// Every commit on <c>main</c> is a tag away from being published, and the release workflow
    /// publishes whatever <c>Directory.Build.props</c> names — so a heading that says "not
    /// released yet" becomes false the moment someone tags, silently and with no other test
    /// noticing. That is not hypothetical: 6.2.1 shipped the parse-rejection change to NuGet
    /// while the changelog still filed it under Unreleased, and the version-is-documented tests
    /// above passed throughout, because 6.2.1 did have an entry — just not the whole one.
    /// Write entries under the version being prepared instead; it is already spelled out in
    /// Directory.Build.props.
    /// </remarks>
    [TestMethod]
    public void NoChangelog_ParksEntriesUnderAnUnversionedHeading()
    {
        var offenders = AllChangelogs()
                       .Where(changelog => changelog.Exists)
                       .SelectMany(changelog => UnversionedHeading
                                               .Matches(File.ReadAllText(changelog.FullName))
                                               .Select(match => $"{changelog.Name}: '{match.Groups["label"].Value}'"))
                       .ToList();

        Assert.AreEqual(
            0, offenders.Count,
            $"Changelog entries sit under a heading that names no version: {string.Join(", ", offenders)}. "
          + "A tag can be cut at any time and the entry ships mislabelled — move it under the version "
          + $"being prepared ({RepoLayout.ShippedVersion}).");
    }

    [TestMethod]
    public void RootChangelog_Exists()
    {
        Assert.IsTrue(
            RepoLayout.RootChangelog.Exists,
            $"No CHANGELOG.md at the repository root ({RepoLayout.RootChangelog.FullName}).");
    }

    [TestMethod]
    public void RootChangelog_DocumentsTheShippedVersion()
    {
        var shipped = Version.Parse(RepoLayout.ShippedVersion);

        CollectionAssert.Contains(
            VersionsIn(RepoLayout.RootChangelog).ToList(),
            shipped,
            $"The root changelog has no entry for {shipped}, the version in Directory.Build.props. "
          + "Every shipped version must be documented before it is released.");
    }

    [TestMethod]
    public void EveryPackage_HasItsOwnChangelog()
    {
        var missing = RepoLayout.PackableProjects
                                .Where(project => !ChangelogOf(project).Exists)
                                .Select(project => project.PackageId)
                                .ToList();

        Assert.AreEqual(
            0, missing.Count,
            $"These packages have no CHANGELOG.md of their own: {string.Join(", ", missing)}.");
    }

    [TestMethod]
    public void EveryPackageChangelog_DocumentsTheShippedVersion()
    {
        var shipped = Version.Parse(RepoLayout.ShippedVersion);

        var undocumented = RepoLayout.PackableProjects
                                     .Where(project => ChangelogOf(project).Exists)
                                     .Where(project => !VersionsIn(ChangelogOf(project)).Contains(shipped))
                                     .Select(project => project.PackageId)
                                     .ToList();

        Assert.AreEqual(
            0, undocumented.Count,
            $"These packages ship at {shipped} without an entry for it: {string.Join(", ", undocumented)}. "
          + "All packages share one Version property and release together, so each one's changelog "
          + "must account for the version — with a real entry, or by saying the release did not "
          + "change that package.");
    }

    [TestMethod]
    public void PackageChangelogVersions_AreASubsetOfTheRootChangelog()
    {
        var rootVersions = VersionsIn(RepoLayout.RootChangelog).ToHashSet();

        foreach (var project in RepoLayout.PackableProjects.Where(project => ChangelogOf(project).Exists))
        {
            var orphans = VersionsIn(ChangelogOf(project)).Where(version => !rootVersions.Contains(version)).ToList();

            Assert.AreEqual(
                0, orphans.Count,
                $"{project.PackageId} documents {string.Join(", ", orphans)}, which the root changelog "
              + "does not. The root changelog is the record of what was released.");
        }
    }

    [TestMethod]
    public void NoChangelog_RunsAheadOfTheShippedVersion()
    {
        var shipped = Version.Parse(RepoLayout.ShippedVersion);

        foreach (var changelog in AllChangelogs().Where(file => file.Exists))
        {
            var ahead = VersionsIn(changelog).Where(version => version > shipped).ToList();

            Assert.AreEqual(
                0, ahead.Count,
                $"{changelog.Name} in {changelog.Directory!.Name} documents {string.Join(", ", ahead)}, "
              + $"which is ahead of the {shipped} in Directory.Build.props. Bump the version property "
              + "in the same change that writes the entry.");
        }
    }

    [TestMethod]
    public void EveryChangelog_ListsVersionsNewestFirst()
    {
        foreach (var changelog in AllChangelogs().Where(file => file.Exists))
        {
            var versions = VersionsIn(changelog);
            var sorted   = versions.OrderByDescending(version => version).ToList();

            CollectionAssert.AreEqual(
                sorted, versions.ToList(),
                $"{changelog.Name} in {changelog.Directory!.Name} lists versions out of order. "
              + $"Expected newest-first ({string.Join(", ", sorted)}), found "
              + $"{string.Join(", ", versions)}.");
        }
    }
}

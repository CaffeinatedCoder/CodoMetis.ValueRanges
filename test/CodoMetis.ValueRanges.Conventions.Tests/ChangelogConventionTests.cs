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
    /// <summary>
    /// Matches a version heading: <c>## [6.1.0] — 2026-08-15</c>, or a preview's,
    /// <c>## [11.0.0-preview.1] — 2026-11-11</c>.
    /// </summary>
    private static readonly Regex VersionHeading =
        new($@"^##\s*\[(?<version>{ReleaseVersion.Pattern})\]", RegexOptions.Multiline);

    private static IReadOnlyList<ReleaseVersion> VersionsIn(FileInfo changelog) =>
        [.. VersionHeading.Matches(File.ReadAllText(changelog.FullName))
                          .Select(match => ReleaseVersion.Parse(match.Groups["version"].Value))];

    private static FileInfo ChangelogOf(PackableProject project) =>
        new(Path.Combine(project.Directory.FullName, "CHANGELOG.md"));

    private static IEnumerable<FileInfo> AllChangelogs() =>
        [RepoLayout.RootChangelog, .. RepoLayout.PackableProjects.Select(ChangelogOf)];

    /// <summary>
    /// Matches a floating heading like <c>## [Unreleased]</c>: any bracketed heading that is not a
    /// version, a preview's included.
    /// </summary>
    private static readonly Regex UnversionedHeading =
        new($@"^##\s*\[(?!{ReleaseVersion.Pattern}\])(?<label>[^\]]*)\]", RegexOptions.Multiline);

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
        var shipped = ReleaseVersion.Parse(RepoLayout.ShippedVersion);

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
        var shipped = ReleaseVersion.Parse(RepoLayout.ShippedVersion);

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
        var shipped = ReleaseVersion.Parse(RepoLayout.ShippedVersion);

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

    /// <summary>The sections a version may have, in the order they must appear.</summary>
    private static readonly string[] SectionOrder = [ChangedAnswers, "Breaking", "Added", "Changed", "Fixed"];

    /// <summary>
    /// The section a minor opens with when it changes an answer: a call that returned one value and
    /// now returns another, without throwing either time (CONTRIBUTING.md, Branches and releases).
    /// </summary>
    private const string ChangedAnswers = "Changed answers";

    /// <summary>
    /// The longest a change may be, in words, not counting link targets. TypeKit's terse entries
    /// run to 73 at most and 25 on average; past this a line is an explanation, which belongs in
    /// a guide the line links to.
    /// </summary>
    private const int MaxWordsPerChange = 60;

    /// <summary>The longest a version's one-line introduction may be, in words.</summary>
    private const int MaxWordsPerIntro = 40;

    /// <summary>
    /// Every version section is terse: at most one introductory line, then <c>###</c> sections from
    /// <see cref="SectionOrder"/>, in that order, each holding one-line changes.
    /// </summary>
    /// <remarks>
    /// The root section is the GitHub release's notes, verbatim, and before 9.1.0 the majors opened
    /// with paragraphs of reasoning and a numbered list of what could break, and single entries ran
    /// to twenty lines of measurements and test names. A reader looking for what to change had to
    /// dig for it. The explanation belongs in <c>docs/migration.md</c> and the guides; the line says
    /// what happens now and what to do, and links there. A continuation line, a nested bullet, a
    /// code block or a second paragraph is how prose comes back, so each is refused here.
    /// </remarks>
    [TestMethod]
    public void EveryVersionSection_HasOneLineChangesUnderKnownSections()
    {
        var offenders = new List<string>();
        var sections  = VersionSections().ToList();

        // A section reader that finds nothing would pass everything below.
        var changelogs = AllChangelogs().Count(file => file.Exists);
        Assert.AreEqual(
            changelogs, sections.Select(section => section.Changelog.FullName).Distinct().Count(),
            "Some changelog has no '## [x.y.z]' section this test could read, so its form went unchecked.");

        foreach (var (changelog, version, lines) in sections)
        {
            var intro   = 0;
            var current = -1;

            foreach (var (number, line) in lines)
            {
                var where = $"{RepoPath(changelog)}:{number} ({version})";

                if (line.Length == 0 || ReferenceDefinition.IsMatch(line)) continue;

                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    var index = Array.IndexOf(SectionOrder, line[4..].Trim());

                    if (index < 0)
                        offenders.Add($"{where}: '{line}' is not one of {string.Join(", ", SectionOrder)}");
                    else if (index <= current)
                        offenders.Add($"{where}: '{line}' is repeated or out of order; sections go {string.Join(", ", SectionOrder)}");
                    else
                        current = index;
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    if (current < 0) offenders.Add($"{where}: a change sits outside a ### section");
                }
                else if (char.IsWhiteSpace(line[0]) || line.StartsWith("```", StringComparison.Ordinal))
                {
                    offenders.Add($"{where}: a continuation line, nested bullet or code block; say the change in one line");
                }
                else if (current >= 0 || ++intro > 1)
                {
                    offenders.Add($"{where}: a paragraph; a version has at most one introductory line, before its sections");
                }
            }
        }

        Assert.AreEqual(
            0, offenders.Count,
            "These changelog lines are not in the terse form (CONTRIBUTING.md, Changelog):"
          + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// A change fits in <see cref="MaxWordsPerChange"/> words and an introduction in
    /// <see cref="MaxWordsPerIntro"/>, link targets not counted.
    /// </summary>
    [TestMethod]
    public void EveryChangelogLine_IsShort()
    {
        var offenders = new List<string>();

        foreach (var (changelog, version, lines) in VersionSections())
        {
            foreach (var (number, line) in lines.Where(entry => entry.Line.Length > 0 && !entry.Line.StartsWith('#')
                                                             && !ReferenceDefinition.IsMatch(entry.Line)))
            {
                var limit = line.StartsWith("- ", StringComparison.Ordinal) ? MaxWordsPerChange : MaxWordsPerIntro;
                var words = LinkTarget.Replace(line, "]").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

                if (words > limit)
                    offenders.Add($"{RepoPath(changelog)}:{number} ({version}): {words} words, at most {limit}");
            }
        }

        Assert.AreEqual(
            0, offenders.Count,
            "These changelog lines are explanations rather than changes. Move the detail to the guide "
          + "the line links to (CONTRIBUTING.md, Changelog):"
          + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>A link inside a version section is an absolute URL.</summary>
    /// <remarks>
    /// The root section is published as the GitHub release's notes, where a relative link resolves
    /// against the release page and lands nowhere. The package changelogs follow the same rule so an
    /// entry can be copied between them as it is.
    /// </remarks>
    [TestMethod]
    public void EveryChangelogLink_IsAbsolute()
    {
        var offenders = VersionSections()
                       .SelectMany(section => section.Lines.SelectMany(entry => LinkTarget.Matches(entry.Line)
                                                                    .Select(match => match.Groups["target"].Value)
                                                                    .Where(target => !target.StartsWith("https://", StringComparison.Ordinal))
                                                                    .Select(target => $"{RepoPath(section.Changelog)}:{entry.Number} ({section.Version}): {target}")))
                       .ToList();

        Assert.AreEqual(
            0, offenders.Count,
            "These changelog links are relative, and break on the GitHub release page. Use the "
          + "https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/… form:"
          + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The last version of the old rule. Up to and including 10.0.0 a silent fix waited for the next
    /// major and was listed under Breaking; every version after it may change an answer in a minor,
    /// and then says so under <see cref="ChangedAnswers"/>.
    /// </summary>
    private static readonly ReleaseVersion LastMajorOnlyRelease = ReleaseVersion.Parse("10.0.0");

    /// <summary>
    /// <c>### Changed answers</c> appears only in a minor. A major lists a changed answer under
    /// Breaking, a patch may not change one at all, and a preview is a major's.
    /// </summary>
    /// <remarks>
    /// The section exists so that a silent fix can ship on the LTS line, which has no major number
    /// left to spend on it. A patch carrying one would be read as "nothing to check" by every
    /// consumer who upgrades patches unattended, which is the reason the rule says minor.
    /// </remarks>
    [TestMethod]
    public void ChangedAnswers_AppearOnlyInAMinor()
    {
        var offenders = VersionSections()
                       .Where(section => section.Lines.Any(entry => IsSectionHeading(entry.Line, ChangedAnswers)))
                       .Where(section => !ReleaseVersion.Parse(section.Version).IsMinor)
                       .Select(section => $"{RepoPath(section.Changelog)} ({section.Version})")
                       .ToList();

        Assert.AreEqual(
            0, offenders.Count,
            $"These versions have a '### {ChangedAnswers}' section but are not a minor (x.y.0, y > 0): "
          + $"{string.Join(", ", offenders)}. A major lists a changed answer under Breaking, and a patch "
          + "may not change one: bump the minor (CONTRIBUTING.md, Branches and releases).");
    }

    /// <summary>
    /// Every row under <c>### Changed answers</c> links the issue it closes and its entry in
    /// <c>docs/migration.md</c>, and that entry exists.
    /// </summary>
    /// <remarks>
    /// The issue link is what ties the row to the <c>silent</c> label: <c>.github/scripts/pr-hygiene.sh</c>
    /// fails a <c>silent</c> PR into a minor whose closing issue has no row here, and it finds the row
    /// by this link. The migration entry is where the consumer learns how to find the calls whose
    /// answer changed, which a one-line row cannot say.
    /// </remarks>
    [TestMethod]
    public void EveryChangedAnswer_LinksItsIssueAndItsMigrationEntry()
    {
        var anchors   = OracleCatalogue.HeadingSlugs("docs/migration.md").ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var (changelog, version, lines) in VersionSections())
        {
            foreach (var (number, line) in ChangesUnder(lines, ChangedAnswers))
            {
                var where = $"{RepoPath(changelog)}:{number} ({version})";

                if (!IssueLink.IsMatch(line))
                    offenders.Add($"{where}: no link to the issue it closes ({IssuesPrefix}n)");

                var migration = MigrationLink.Match(line);

                if (!migration.Success)
                    offenders.Add($"{where}: no link to its entry in docs/migration.md");
                else if (!anchors.Contains(migration.Groups["anchor"].Value))
                    offenders.Add($"{where}: docs/migration.md has no heading '#{migration.Groups["anchor"].Value}'");
            }
        }

        Assert.AreEqual(
            0, offenders.Count,
            $"These '### {ChangedAnswers}' rows do not lead a reader to the issue and the migration entry:"
          + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// After 10.0.0, a change that says it used to produce a value — "where it returned …", "where
    /// they wrote …" — sits under <c>### Changed answers</c>, in a minor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The changelog form already makes every fix say what happened before. A fix whose "before" was
    /// an exception is loud and belongs under Fixed; one whose "before" was a value is a changed
    /// answer, the kind a consumer cannot find by running their tests, and filed under Fixed it
    /// reads as harmless. Before 11.0.0 that was the major's Breaking section; from then on, with two
    /// lines and majors that name the platform, it is this section of a minor.
    /// </para>
    /// <para>
    /// The limit: this reads the wording, not the label. A changed answer phrased without the
    /// "where it …" clause the form prescribes passes here. The <c>silent</c> label is not
    /// reachable from a test; <c>.github/scripts/pr-hygiene.sh</c>, which sees the PR's labels,
    /// holds the other half by requiring the row for a <c>silent</c> PR's issue.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void AfterTenZero_AChangedAnswerIsUnderChangedAnswers()
    {
        var offenders = new List<string>();

        foreach (var (changelog, version, lines) in VersionSections())
        {
            var parsed = ReleaseVersion.Parse(version);

            if (parsed <= LastMajorOnlyRelease || parsed.IsPrerelease || parsed is { Minor: 0, Patch: 0 }) continue;

            var section = "";

            foreach (var (number, line) in lines)
            {
                if (line.StartsWith("### ", StringComparison.Ordinal)) section = line[4..].Trim();
                else if (section != ChangedAnswers && line.StartsWith("- ", StringComparison.Ordinal) && PriorValue.Match(line) is { Success: true } clause)
                    offenders.Add($"{RepoPath(changelog)}:{number} ({version}, ### {section}): '{clause.Value}'");
            }
        }

        Assert.AreEqual(
            0, offenders.Count,
            $"These changes say the call used to produce a value, so they changed an answer. Move each "
          + $"under '### {ChangedAnswers}' (a patch cannot carry one: bump the minor), link its issue and "
          + "its docs/migration.md entry (CONTRIBUTING.md, Branches and releases):"
          + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The "before" clause of a change whose previous behaviour was a value: "where it returned",
    /// "where they wrote". The loud verbs — the call threw, failed or was refused — are left out.
    /// </summary>
    private static readonly Regex PriorValue =
        new(@"\bwhere (?:it|they) (?!threw\b|failed\b|refused\b|was refused\b|were refused\b)[a-z]+(?: [a-z]+)?", RegexOptions.IgnoreCase);

    private const string IssuesPrefix = "https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/issues/";

    private static readonly Regex IssueLink = new($@"\]\({Regex.Escape(IssuesPrefix)}\d+\)");

    private static readonly Regex MigrationLink =
        new(@"\]\(https://github\.com/CaffeinatedCoder/CodoMetis\.ValueRanges/blob/main/docs/migration\.md#(?<anchor>[^)\s]+)\)");

    private static bool IsSectionHeading(string line, string name) =>
        line.StartsWith("### ", StringComparison.Ordinal) && line[4..].Trim() == name;

    /// <summary>The change lines under one <c>###</c> section of a version.</summary>
    private static IEnumerable<(int Number, string Line)> ChangesUnder(List<(int Number, string Line)> lines, string name)
    {
        var inside = false;

        foreach (var (number, line) in lines)
        {
            if (line.StartsWith("### ", StringComparison.Ordinal)) inside = IsSectionHeading(line, name);
            else if (inside && line.StartsWith("- ", StringComparison.Ordinal)) yield return (number, line);
        }
    }

    /// <summary>Matches a reference-style link definition: <c>[9.0.0]: https://…</c>.</summary>
    private static readonly Regex ReferenceDefinition = new(@"^\[[^\]]+\]:\s+\S+$");

    /// <summary>Matches the target of an inline link: <c>](target)</c>.</summary>
    private static readonly Regex LinkTarget = new(@"\]\((?<target>[^)\s]*)\)");

    /// <summary>
    /// Each version section of every changelog: the lines after its <c>## [x.y.z]</c> heading up to
    /// the next <c>##</c> heading, with their line numbers. The file's own header is not a section.
    /// </summary>
    private static IEnumerable<(FileInfo Changelog, string Version, List<(int Number, string Line)> Lines)> VersionSections()
    {
        foreach (var changelog in AllChangelogs().Where(file => file.Exists))
        {
            string? version = null;
            var lines = new List<(int Number, string Line)>();
            var all   = File.ReadAllLines(changelog.FullName);

            for (var index = 0; index < all.Length; index++)
            {
                var line = all[index].TrimEnd();

                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    if (version is not null) yield return (changelog, version, lines);

                    var heading = VersionHeading.Match(line);
                    version = heading.Success ? heading.Groups["version"].Value : null;
                    lines   = [];
                }
                else if (version is not null)
                {
                    lines.Add((index + 1, line));
                }
            }

            if (version is not null) yield return (changelog, version, lines);
        }
    }

    private static string RepoPath(FileInfo file) =>
        Path.GetRelativePath(RepoLayout.Root.FullName, file.FullName).Replace('\\', '/');

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

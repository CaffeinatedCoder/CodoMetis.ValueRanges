using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Guards the packaging metadata. These defects compile, build and test clean — they only
/// surface once a package is on NuGet: a page with no README, a package with no description,
/// or a consumer who cannot step into the code they are running.
/// </summary>
[TestClass]
public sealed class PackagingConventionTests
{
    [TestMethod]
    public void EveryPackage_DeclaresAReadme()
    {
        var missing = RepoLayout.PackableProjects
                                .Where(project => string.IsNullOrEmpty(project.Property("PackageReadmeFile")))
                                .Select(project => project.PackageId)
                                .ToList();

        Assert.AreEqual(
            0, missing.Count,
            $"These packages declare no PackageReadmeFile: {string.Join(", ", missing)}. "
          + "Without one the NuGet page is blank.");
    }

    [TestMethod]
    public void EveryPackage_ShipsItsOwnReadme_NotAShared()
    {
        foreach (var project in RepoLayout.PackableProjects)
        {
            var packedReadmes = project.PackedFiles
                                       .Where(include => include.EndsWith("README.md", StringComparison.OrdinalIgnoreCase))
                                       .ToList();

            Assert.AreEqual(
                1, packedReadmes.Count,
                $"{project.PackageId} packs {packedReadmes.Count} README files; expected exactly one.");

            var packed = packedReadmes[0];

            Assert.IsFalse(
                packed.Contains("..", StringComparison.Ordinal),
                $"{project.PackageId} packs '{packed}' — a README from outside its own directory. "
              + "Each package needs a README scoped to what it contains; sharing the root README "
              + "puts four packages' documentation on every package's page.");

            var readmeOnDisk = new FileInfo(Path.Combine(project.Directory.FullName, packed));

            Assert.IsTrue(
                readmeOnDisk.Exists,
                $"{project.PackageId} packs '{packed}', which does not exist at {readmeOnDisk.FullName}.");
        }
    }

    /// <summary>The phrase a package README uses to claim Native AOT compatibility.</summary>
    private const string AotClaim = "Native AOT and trimming compatible";

    /// <summary>
    /// A package README claims Native AOT compatibility exactly when its project builds with
    /// <c>IsAotCompatible</c> — the property that runs the trim and AOT analyzers on every build.
    /// </summary>
    /// <remarks>
    /// A README is what a consumer reads on NuGet, and nothing else ties it to the build. A claim
    /// without the property would be unchecked marketing — the analyzers would never run, and a
    /// reflection path could ship unannotated under a page that promises otherwise. The property
    /// without the claim hides a feature consumers choose packages by. The EF Core packages make
    /// neither: they build on EF Core's reflection, whose own AOT support is experimental.
    /// </remarks>
    [TestMethod]
    public void AotClaim_InAPackageReadme_MatchesIsAotCompatible()
    {
        foreach (var project in RepoLayout.PackableProjects)
        {
            var readme = project.PackedFiles.Single(include => include.EndsWith("README.md", StringComparison.OrdinalIgnoreCase));
            var claims = File.ReadAllText(Path.Combine(project.Directory.FullName, readme))
                             .Contains(AotClaim, StringComparison.Ordinal);
            var isAotCompatible = string.Equals(project.Property("IsAotCompatible"), "true", StringComparison.OrdinalIgnoreCase);

            Assert.AreEqual(
                isAotCompatible, claims,
                isAotCompatible
                    ? $"{project.PackageId} builds with IsAotCompatible but its README does not say "
                    + $"\"{AotClaim}\". Consumers choose packages by this; say it."
                    : $"{project.PackageId}'s README claims \"{AotClaim}\" but the project does not set "
                    + "IsAotCompatible, so the trim and AOT analyzers never run on it. Set the property, "
                    + "or remove the claim.");
        }
    }

    /// <summary>The NuGet tags that claim Native AOT and trimming compatibility.</summary>
    private static readonly string[] AotTags = ["native-aot", "trimming"];

    /// <summary>
    /// A package's NuGet tags claim Native AOT and trimming exactly when its project builds with
    /// <c>IsAotCompatible</c> — the same rule as the README claim above, for the field NuGet search
    /// filters on.
    /// </summary>
    /// <remarks>
    /// Package metadata is frozen per version, so a tag that is missing at release cannot be added
    /// to that release afterwards. Tags are compared whole, split as NuGet splits them, so a tag
    /// that merely contains <c>aot</c> does not count.
    /// </remarks>
    [TestMethod]
    public void AotTags_MatchIsAotCompatible()
    {
        foreach (var project in RepoLayout.PackableProjects)
        {
            var tags = (project.Property("PackageTags") ?? "")
                      .Split([';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isAotCompatible = string.Equals(project.Property("IsAotCompatible"), "true", StringComparison.OrdinalIgnoreCase);

            foreach (var tag in AotTags)
                Assert.AreEqual(
                    isAotCompatible, tags.Contains(tag),
                    isAotCompatible
                        ? $"{project.PackageId} builds with IsAotCompatible but its PackageTags lack '{tag}'. "
                        + "NuGet search filters on tags, and a published version's tags cannot be changed."
                        : $"{project.PackageId}'s PackageTags claim '{tag}' but the project does not set "
                        + "IsAotCompatible, so nothing checks the claim. Set the property, or remove the tag.");
        }
    }

    [TestMethod]
    public void EveryPackage_DescribesItself()
    {
        foreach (var project in RepoLayout.PackableProjects)
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(project.Property("Description")),
                $"{project.PackageId} has no Description — NuGet search has nothing to index.");

            Assert.IsFalse(
                string.IsNullOrWhiteSpace(project.Property("PackageTags")),
                $"{project.PackageId} has no PackageTags.");
        }
    }

    [TestMethod]
    public void SharedBuildProperties_ShipSymbolsAndSourceLink()
    {
        var properties = SharedBuildProperties();

        AssertProperty(properties, "IncludeSymbols",         "true");
        AssertProperty(properties, "SymbolPackageFormat",    "snupkg");
        AssertProperty(properties, "PublishRepositoryUrl",   "true");
        AssertProperty(properties, "EmbedUntrackedSources",  "true");
        AssertProperty(properties, "RepositoryType",         "git");

        static void AssertProperty(IReadOnlyDictionary<string, string> properties, string name, string expected)
            => Assert.AreEqual(
                expected,
                properties.GetValueOrDefault(name),
                $"Directory.Build.props must set {name} to '{expected}'. Symbols and Source Link are "
              + "what let a consumer step into the code they are running and confirm it was built "
              + "from the commit it claims.");
    }

    /// <summary>
    /// <c>ContinuousIntegrationBuild</c> normalizes source paths for a reproducible build — right
    /// for a published package, and it breaks local step-into debugging. It must therefore be
    /// conditioned, never set unconditionally.
    /// </summary>
    [TestMethod]
    public void ContinuousIntegrationBuild_IsSetOnlyUnderCi()
    {
        var element = XDocument.Load(Path.Combine(RepoLayout.Root.FullName, "Directory.Build.props"))
                               .Descendants("ContinuousIntegrationBuild")
                               .SingleOrDefault();

        Assert.IsNotNull(element, "Directory.Build.props does not set ContinuousIntegrationBuild at all.");

        var condition = element.Parent?.Attribute("Condition")?.Value ?? element.Attribute("Condition")?.Value;

        Assert.IsNotNull(
            condition,
            "ContinuousIntegrationBuild is set unconditionally. It must be gated on CI — unconditional, "
          + "it normalizes source paths in local builds and breaks step-into debugging.");

        StringAssert.Contains(
            condition, "$(CI)",
            "ContinuousIntegrationBuild must be gated on the CI environment variable.");
    }

    /// <summary>
    /// The SDK's pack targets prepend <c>Build</c> to <c>GenerateNuspecDependsOn</c> only when
    /// <c>NoBuild != true</c> <em>and</em> <c>GeneratePackageOnBuild != true</c>, so setting the
    /// latter silently makes plain <c>dotnet pack</c> behave as if <c>--no-build</c> had been
    /// passed: it packs whatever is in <c>bin/</c> — stale output on a laptop, NU5026 on a clean
    /// checkout. The workflows always build first, which is why it never bit here; this keeps it
    /// from being reintroduced as a convenience.
    /// </summary>
    [TestMethod]
    public void SharedBuildProperties_DoNotSetGeneratePackageOnBuild()
    {
        var all = XDocument.Load(Path.Combine(RepoLayout.Root.FullName, "Directory.Build.props"))
                           .Descendants("GeneratePackageOnBuild")
                           .ToList();

        Assert.IsEmpty(
            all,
            "Directory.Build.props sets GeneratePackageOnBuild. That makes plain `dotnet pack` skip the "
          + "build and pack whatever is in bin/ — stale locally, NU5026 on a clean checkout. Build "
          + "first and pack with --no-build instead, as the workflows do.");
    }

    /// <summary>
    /// Package validation compares each pack against <c>PackageValidationBaselineVersion</c>. A
    /// baseline left behind stops seeing API added since it — a member introduced in 6.3 and
    /// removed in 6.4 is invisible to a 6.2 baseline — so it may be the version being shipped
    /// (between releases, when <c>Version</c> is the last release) or the release directly before
    /// it (once <c>Version</c> is bumped for the next one), and never older. That lags by at most
    /// one release and forces the move at every version bump.
    /// </summary>
    /// <remarks>
    /// A preview is never a baseline. Previews promise no API stability among themselves (the 11.0.0
    /// variants rewrite breaks <c>11.0.0-preview.1</c> on purpose), so "the release before it" means
    /// the latest stable release: <c>11.0.0-preview.2</c> and <c>11.0.0</c> are both validated
    /// against 10.0.0.
    /// </remarks>
    [TestMethod]
    public void PackageValidationBaseline_IsTheShippedVersionOrTheReleaseBeforeIt()
    {
        var properties = SharedBuildProperties();

        Assert.AreEqual(
            "true", properties.GetValueOrDefault("EnablePackageValidation"),
            "Directory.Build.props does not enable package validation, so a removed public member "
          + "packs cleanly and the 'no breaking changes' claim rests on reading the diff.");

        var shipped  = ReleaseVersion.Parse(RepoLayout.ShippedVersion);
        var previous = ChangelogVersions().Where(version => version < shipped && !version.IsPrerelease).DefaultIfEmpty().Max();
        var baseline = properties.GetValueOrDefault("PackageValidationBaselineVersion");

        Assert.IsNotNull(baseline, "Directory.Build.props sets no PackageValidationBaselineVersion.");

        var allowed = new[] { shipped, previous }.Where(version => version is { IsPrerelease: false }).Distinct().ToList();

        Assert.IsTrue(
            allowed.Contains(ReleaseVersion.Parse(baseline)),
            $"PackageValidationBaselineVersion is {baseline}, but Directory.Build.props ships {shipped} "
          + $"and the stable release before it is {previous?.ToString() ?? "none"}. The baseline must be one of "
          + "those two (never a preview) — move it to the release just superseded when bumping Version, or API added "
          + "since the old baseline goes unvalidated.");
    }

    /// <summary>
    /// Every package's release notes link to its own changelog, and that file exists.
    /// </summary>
    /// <remarks>
    /// The link is set once in <c>Directory.Build.props</c> from <c>$(MSBuildProjectName)</c>, so it
    /// is right only while each project sits in a directory of its own name under <c>src/</c>, and
    /// only while no project sets its own. A renamed directory or an override would point the NuGet
    /// page's "Release notes" at a 404, which nothing else checks.
    /// </remarks>
    [TestMethod]
    public void EveryPackage_LinksItsReleaseNotesToItsOwnChangelog()
    {
        const string prefix = "https://github.com/CaffeinatedCoder/CodoMetis.ValueRanges/blob/main/";

        var notes = SharedBuildProperties().GetValueOrDefault("PackageReleaseNotes");

        Assert.IsNotNull(notes, "Directory.Build.props sets no PackageReleaseNotes, so the NuGet page links no release notes.");
        Assert.IsTrue(
            notes.StartsWith(prefix, StringComparison.Ordinal),
            $"PackageReleaseNotes is '{notes}'; expected a link under {prefix}.");

        foreach (var project in RepoLayout.PackableProjects)
        {
            Assert.IsNull(
                project.Property("PackageReleaseNotes"),
                $"{project.PackageId} sets its own PackageReleaseNotes; the shared one in Directory.Build.props already links its changelog.");

            var name   = Path.GetFileNameWithoutExtension(project.File.Name);
            var target = notes[prefix.Length..].Replace("$(MSBuildProjectName)", name, StringComparison.Ordinal);
            var file   = new FileInfo(Path.Combine(RepoLayout.Root.FullName, target));

            Assert.AreEqual(
                Path.Combine(project.Directory.FullName, "CHANGELOG.md"), file.FullName,
                $"{project.PackageId}'s release notes link to {target}, which is not its own changelog. "
              + "Keep each project in a directory of its own name under src/.");
            Assert.IsTrue(file.Exists, $"{project.PackageId}'s release notes link to {target}, which does not exist.");
        }
    }

    private static IEnumerable<ReleaseVersion> ChangelogVersions() =>
        Regex.Matches(File.ReadAllText(RepoLayout.RootChangelog.FullName), $@"^##\s*\[(?<version>{ReleaseVersion.Pattern})\]", RegexOptions.Multiline)
             .Select(match => ReleaseVersion.Parse(match.Groups["version"].Value));

    private static Dictionary<string, string> SharedBuildProperties()
    {
        var document = XDocument.Load(Path.Combine(RepoLayout.Root.FullName, "Directory.Build.props"));

        return document.Descendants("PropertyGroup")
                       .Where(group => group.Attribute("Condition") is null)
                       .SelectMany(group => group.Elements())
                       .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
                       .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.Ordinal);
    }
}

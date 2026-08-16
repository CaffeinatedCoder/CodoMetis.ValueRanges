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

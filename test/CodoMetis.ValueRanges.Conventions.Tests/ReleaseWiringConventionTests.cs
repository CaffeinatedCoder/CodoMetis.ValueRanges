using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Guards the parts of the release path that fail silently rather than loudly.
/// </summary>
/// <remarks>
/// Trusted Publishing matches on three names — the workflow <em>file name</em>, the environment,
/// and the repository owner/name — none of which live in this repository's build. Rename the
/// workflow or the environment and nothing here breaks: the next tag simply fails to publish,
/// with an authentication error that says nothing about the rename. The SBOM has the opposite
/// shape: a build-only reference added later would silently widen it into a document that
/// contradicts the nuspec, handing consumers alerts for code they never receive.
/// </remarks>
[TestClass]
public sealed class ReleaseWiringConventionTests
{
    /// <summary>The workflow file name the nuget.org trusted-publishing policy names.</summary>
    private const string ReleaseWorkflowFileName = "release.yml";

    /// <summary>The GitHub environment the policy expects, and the approval gate.</summary>
    private const string PublishEnvironment = "nuget";

    private static FileInfo ReleaseWorkflow =>
        new(Path.Combine(RepoLayout.Root.FullName, ".github", "workflows", ReleaseWorkflowFileName));

    private static string ReleaseWorkflowText => File.ReadAllText(ReleaseWorkflow.FullName);

    [TestMethod]
    public void TheReleaseWorkflow_LivesAtTheFileNameTheTrustedPublishingPolicyNames()
    {
        Assert.IsTrue(
            ReleaseWorkflow.Exists,
            $"No .github/workflows/{ReleaseWorkflowFileName}. The nuget.org trusted-publishing policy "
          + "names the workflow by file name, so renaming or moving this file stops publishing "
          + "working — with an authentication error that does not mention the rename.");
    }

    [TestMethod]
    public void ThePublishJob_IsGatedOnTheEnvironmentThePolicyExpects()
    {
        StringAssert.Contains(
            ReleaseWorkflowText, $"environment: {PublishEnvironment}",
            $"The release workflow does not gate a job on the '{PublishEnvironment}' environment. "
          + "The OIDC token carries the environment as a claim; a policy that expects it will not "
          + "match a token without it, and the environment is also the approval gate.");
    }

    /// <summary>
    /// The split exists so a reviewer is asked only after the suite has passed, and so nothing
    /// that runs unattended is able to mint a publishing token.
    /// </summary>
    [TestMethod]
    public void OnlyThePublishJob_MayMintAnOidcToken()
    {
        var text = ReleaseWorkflowText;

        Assert.IsTrue(
            Regex.IsMatch(text, @"(?m)^\s*id-token\s*:\s*write"),
            "No job in the release workflow requests id-token: write, so OIDC login cannot work.");

        var verifyJob = Regex.Match(text, @"(?ms)^  verify:.*?(?=^  publish:)").Value;

        Assert.AreNotEqual(
            0, verifyJob.Length,
            "Could not find the verify job in the release workflow — this test assumes the "
          + "verify/publish split.");

        // Matched as a YAML key, not as text: the verify job's comment says the words "id-token"
        // precisely to explain why it does not request one, and a substring search reads that
        // comment as the permission it warns against.
        Assert.IsFalse(
            Regex.IsMatch(verifyJob, @"(?m)^\s*id-token\s*:"),
            "The verify job requests an id-token permission. It runs unattended, before any "
          + "approval: nothing in it may be able to mint a token that can publish.");
    }

    [TestMethod]
    public void TheReleaseWorkflow_ChecksTheTagAgainstTheVersionProperty()
    {
        StringAssert.Contains(
            ReleaseWorkflowText, "does not match Directory.Build.props",
            "The release workflow does not verify the pushed tag against the Version property. "
          + "Deriving the version from the tag instead would publish whatever was typed.");
    }

    /// <summary>
    /// The playbook's SBOM rule, kept honest. Nothing here is referenced with
    /// <c>PrivateAssets=all</c> today, so the workflows pass no <c>--exclude-filter</c> and the
    /// SBOM matches the nuspec. Adding a build-only reference without excluding its subtree would
    /// publish a document claiming a consumer receives code they never see.
    /// </summary>
    [TestMethod]
    public void EveryBuildOnlyReference_IsExcludedFromTheSbom()
    {
        var buildOnly = RepoLayout.PackableProjects
                                  .SelectMany(BuildOnlyReferencesOf)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(name => name, StringComparer.Ordinal)
                                  .ToList();

        var workflows = new[] { ReleaseWorkflowText, BuildWorkflowText() };

        var unexcluded = buildOnly
                        .Where(reference => workflows.Any(workflow =>
                             !workflow.Contains($"--exclude-filter {reference}", StringComparison.Ordinal)))
                        .ToList();

        Assert.AreEqual(
            0, unexcluded.Count,
            $"These references are PrivateAssets=all — a consumer never receives them — but the SBOM "
          + $"steps do not exclude their subtrees: {string.Join(", ", unexcluded)}. Generating "
          + "naively pulls in the whole toolchain subtree, contradicting the nuspec and handing "
          + "consumers vulnerability alerts for code they do not ship. Add "
          + "`--exclude-filter <id>` to the SBOM step in both workflows.");
    }

    private static IEnumerable<string> BuildOnlyReferencesOf(PackableProject project) =>
        project.Document
               .Descendants("PackageReference")
               .Where(reference => string.Equals(
                    reference.Element("PrivateAssets")?.Value.Trim()
                 ?? reference.Attribute("PrivateAssets")?.Value.Trim(),
                    "all",
                    StringComparison.OrdinalIgnoreCase))
               .Select(reference => reference.Attribute("Include")?.Value)
               .Where(id => !string.IsNullOrEmpty(id))!;

    private static string BuildWorkflowText() =>
        File.ReadAllText(Path.Combine(RepoLayout.Root.FullName, ".github", "workflows", "dotnet.yml"));

    /// <summary>
    /// The manual publish script was retired when the release workflow landed, deliberately
    /// leaving one publishing path. Two paths drift, and the one that drifts is the one that
    /// skips a check.
    /// </summary>
    [TestMethod]
    public void ThereIsExactlyOnePublishingPath()
    {
        var scriptsDirectory = new DirectoryInfo(Path.Combine(RepoLayout.Root.FullName, "scripts"));

        var publishScripts = scriptsDirectory.Exists
                                 ? scriptsDirectory.EnumerateFiles("*publish*", SearchOption.AllDirectories).ToList()
                                 : [];

        Assert.AreEqual(
            0, publishScripts.Count,
            $"Found a publishing script alongside the release workflow: "
          + $"{string.Join(", ", publishScripts.Select(file => file.Name))}. Publishing goes through "
          + "release.yml and Trusted Publishing; a second path holding an API key is the one that "
          + "will skip a check.");
    }

    /// <summary>
    /// The CycloneDX tool is restored with <c>dotnet tool restore</c> in both workflows, which
    /// silently does nothing without a manifest.
    /// </summary>
    [TestMethod]
    public void TheSbomTool_IsPinnedInAToolManifest()
    {
        var manifestPath = Path.Combine(RepoLayout.Root.FullName, ".config", "dotnet-tools.json");

        Assert.IsTrue(File.Exists(manifestPath), $"No tool manifest at {manifestPath}.");

        StringAssert.Contains(
            File.ReadAllText(manifestPath), "cyclonedx",
            "The tool manifest does not pin cyclonedx, but both workflows run `dotnet cyclonedx` "
          + "after `dotnet tool restore`.");
    }
}

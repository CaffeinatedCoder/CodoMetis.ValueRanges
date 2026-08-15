using System.Xml.Linq;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Locates the repository and the projects in it. Everything here is discovered rather than
/// hard-coded: the root is found by walking up to the solution file, and the shipping projects
/// are found by globbing <c>src/</c>, so adding a package needs no edit here and moving a
/// project cannot silently retarget a test the way a positional <c>../../</c> would.
/// </summary>
internal static class RepoLayout
{
    private const string SolutionFileName = "CodoMetis.ValueRanges.slnx";

    /// <summary>The repository root, identified by the solution file it contains.</summary>
    internal static DirectoryInfo Root { get; } = FindRoot();

    /// <summary>Every packable project under <c>src/</c>.</summary>
    internal static IReadOnlyList<PackableProject> PackableProjects { get; } = DiscoverPackableProjects();

    /// <summary>The single source of truth for the version all packages ship at.</summary>
    internal static string ShippedVersion { get; } = ReadShippedVersion();

    internal static FileInfo RootChangelog => new(Path.Combine(Root.FullName, "CHANGELOG.md"));

    private static DirectoryInfo FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName))) return directory;
        }

        throw new InvalidOperationException(
            $"Could not find '{SolutionFileName}' in any ancestor of '{AppContext.BaseDirectory}'. "
          + "The convention tests locate the repository by that marker file.");
    }

    private static List<PackableProject> DiscoverPackableProjects()
    {
        var sourceDirectory = new DirectoryInfo(Path.Combine(Root.FullName, "src"));

        if (!sourceDirectory.Exists)
            throw new InvalidOperationException($"No 'src' directory under '{Root.FullName}'.");

        var projects = sourceDirectory
                      .EnumerateFiles("*.csproj", SearchOption.AllDirectories)
                      .Select(file => new PackableProject(file, XDocument.Load(file.FullName)))
                      .Where(project => !string.Equals(project.Property("IsPackable"), "false", StringComparison.OrdinalIgnoreCase))
                      .OrderBy(project => project.File.Name, StringComparer.Ordinal)
                      .ToList();

        if (projects.Count == 0)
            throw new InvalidOperationException($"No packable projects found under '{sourceDirectory.FullName}'.");

        return projects;
    }

    private static string ReadShippedVersion()
    {
        var propsPath = Path.Combine(Root.FullName, "Directory.Build.props");
        var version   = XDocument.Load(propsPath)
                                 .Descendants("Version")
                                 .FirstOrDefault()
                                ?.Value
                                 .Trim();

        return string.IsNullOrEmpty(version)
                   ? throw new InvalidOperationException($"No <Version> property in '{propsPath}'.")
                   : version;
    }
}

/// <summary>A shipping project, with the parts of its csproj the conventions care about.</summary>
internal sealed record PackableProject(FileInfo File, XDocument Document)
{
    /// <summary>The package id, defaulting to the project file name as MSBuild does.</summary>
    internal string PackageId => Property("PackageId") ?? Path.GetFileNameWithoutExtension(File.Name);

    internal DirectoryInfo Directory => File.Directory!;

    internal string? Property(string name) =>
        Document.Descendants(name).FirstOrDefault()?.Value.Trim();

    /// <summary>The <c>None</c> items the project packs, by their <c>Include</c> path.</summary>
    internal IEnumerable<string> PackedFiles =>
        Document.Descendants("None")
                .Where(item => string.Equals(item.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrEmpty(include))!;

    public override string ToString() => PackageId;
}

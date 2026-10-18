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

    /// <summary>
    /// The latest release on nuget.org, read from <c>PackageValidationBaselineVersion</c>. Between a
    /// version bump and its tag, <see cref="ShippedVersion"/> names the release being prepared and
    /// the baseline names the one already out. <c>PackagingConventionTests</c> keeps the baseline
    /// equal to that release.
    /// </summary>
    internal static string LatestRelease { get; } = ReadSharedProperty("PackageValidationBaselineVersion");

    /// <summary>
    /// The latest release of the other supported line, read from <c>CompanionLineRelease</c>, or
    /// <see langword="null"/> while only one line has a release. No branch can read the other
    /// branch's props, so this is the one place a branch records what the other line last shipped.
    /// </summary>
    internal static string? CompanionLineRelease { get; } = ReadOptionalSharedProperty("CompanionLineRelease");

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

    private static string ReadShippedVersion() => ReadSharedProperty("Version");

    private static string ReadSharedProperty(string name) =>
        ReadOptionalSharedProperty(name)
     ?? throw new InvalidOperationException($"No <{name}> property in '{Path.Combine(Root.FullName, "Directory.Build.props")}'.");

    private static string? ReadOptionalSharedProperty(string name)
    {
        var value = XDocument.Load(Path.Combine(Root.FullName, "Directory.Build.props"))
                             .Descendants(name)
                             .FirstOrDefault()
                            ?.Value
                             .Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }
}

/// <summary>
/// A package version as the release path spells it: <c>10.1.0</c>, or a prerelease such as
/// <c>11.0.0-preview.2</c>. <see cref="Version"/> cannot hold the second, and orders nothing by
/// SemVer's rules, so every test that reads a version reads it through this.
/// </summary>
/// <remarks>
/// Precedence is SemVer 2.0's: a prerelease sorts before its release (<c>11.0.0-preview.2</c> &lt;
/// <c>11.0.0</c>), and prerelease identifiers compare numerically when both are numeric, ordinally
/// otherwise (<c>preview.10</c> &gt; <c>preview.9</c>). Build metadata is not part of a version
/// here; nothing in the release path writes it.
/// </remarks>
internal sealed record ReleaseVersion(int Major, int Minor, int Patch, string? Prerelease) : IComparable<ReleaseVersion>
{
    /// <summary>The pattern a version heading or property holds, without anchors.</summary>
    internal const string Pattern = @"\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?";

    private static readonly System.Text.RegularExpressions.Regex Exact =
        new(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?$");

    internal bool IsPrerelease => Prerelease is not null;

    /// <summary>A minor: <c>x.y.0</c> with <c>y &gt; 0</c>, not a prerelease.</summary>
    internal bool IsMinor => !IsPrerelease && Minor > 0 && Patch == 0;

    /// <summary>The release this one previews, or itself: <c>11.0.0-preview.2</c> → <c>11.0.0</c>.</summary>
    internal ReleaseVersion Release => this with { Prerelease = null };

    internal static ReleaseVersion Parse(string text)
    {
        var match = Exact.Match(text.Trim());

        if (!match.Success) throw new FormatException($"'{text}' is not a version of the form x.y.z or x.y.z-prerelease.");

        return new ReleaseVersion(
            int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(match.Groups["patch"].Value, System.Globalization.CultureInfo.InvariantCulture),
            match.Groups["pre"].Success ? match.Groups["pre"].Value : null);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0) return core;

        if (Prerelease == other.Prerelease) return 0;
        if (Prerelease is null) return 1;
        if (other.Prerelease is null) return -1;

        var left  = Prerelease.Split('.');
        var right = other.Prerelease.Split('.');

        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var leftNumeric  = int.TryParse(left[index], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(right[index], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rightNumber);

            var order = (leftNumeric, rightNumeric) switch
            {
                (true, true)  => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _             => string.CompareOrdinal(left[index], right[index]),
            };

            if (order != 0) return order;
        }

        return left.Length.CompareTo(right.Length);
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
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

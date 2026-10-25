using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The files under <c>test/oracles/</c>, loaded once: the claims the documents make, the oracle
/// classes that hold them, the seeds that prove the oracles can fail, the waivers for public
/// members no claim covers, and the known disagreements.
/// </summary>
/// <remarks>
/// Everything here is data a maintainer edits by hand and the <c>Oracle*Tests</c> hold
/// consistent with the repository: a claim's quote must still be in its document, a surface must
/// still exist, an oracle must still be a test class with a seed. The tests are what makes the
/// catalogue a worklist rather than a wish list.
/// </remarks>
internal static partial class OracleCatalogue
{
    // Declared first: static initializers run in textual order, and the loaders below use it.
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true
    };

    internal static DirectoryInfo Directory { get; } = new(Path.Combine(RepoLayout.Root.FullName, "test", "oracles"));

    internal static IReadOnlyList<Claim> Claims { get; } = Load<List<Claim>>("claims.json");

    internal static IReadOnlyList<Oracle> Oracles { get; } = Load<List<Oracle>>("oracles.json");

    /// <summary>Surface patterns no claim names, each with the reason it needs no oracle.</summary>
    internal static IReadOnlyDictionary<string, string> SurfaceWaivers { get; } = Load<Dictionary<string, string>>("surface-waivers.json");

    internal static IReadOnlyList<Seed> Seeds { get; } = LoadSeeds();

    /// <summary>PostgreSQL's range, multirange and array surface, one entry per catalogue row, each with the member, the waiver or the issue.</summary>
    internal static IReadOnlyList<PostgresSurfaceEntry> PostgresSurface { get; } = Load<List<PostgresSurfaceEntry>>("postgres-surface.json");

    /// <summary>The raw <c>pg_operator</c>/<c>pg_proc</c> listing <c>tools/oracles.cs pg-surface</c> wrote, one line per row.</summary>
    internal static PostgresListing PostgresSurfaceListing { get; } = PostgresListing.Read(Path.Combine(Directory.FullName, "postgres-surface.catalog.txt"));

    /// <summary>
    /// The no-database EF translation test projects: every <c>.cs</c> file of each, concatenated. A
    /// surface entry's <c>translation</c> must occur in it, so a dropped assertion fails the entry.
    /// </summary>
    internal static string TranslationTestSources { get; } = ReadTranslationTestSources();

    /// <summary>The documents that make promises to consumers; every one must carry at least one claim.</summary>
    internal static readonly string[] PromiseDocuments =
    [
        "README.md",
        "docs/ranges.md",
        "docs/value-sets.md",
        "docs/serialization.md",
        "docs/efcore.md",
        "docs/getting-started.md",
        "docs/walkthrough.md"
    ];

    /// <summary>Every <c>[TestClass]</c> name under <c>test/</c>, with the project directory that declares it.</summary>
    internal static IReadOnlyDictionary<string, string> TestClasses { get; } = DiscoverTestClasses();

    /// <summary>GitHub's heading slug: lower case, punctuation dropped, spaces to hyphens.</summary>
    internal static string Slug(string heading)
    {
        var text = heading.Trim().ToLowerInvariant();
        text = Regex.Replace(text, @"[^\p{L}\p{N}\s-]", "");
        return Regex.Replace(text, @"\s", "-");
    }

    /// <summary>Headings of a Markdown file, as slugs, in order.</summary>
    internal static IReadOnlyList<string> HeadingSlugs(string relativePath)
    {
        var inCode = false;
        var slugs  = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(RepoLayout.Root.FullName, relativePath)))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) inCode = !inCode;
            if (inCode) continue;
            var match = Regex.Match(line, @"^#{1,6}\s+(.*?)\s*#*\s*$");
            if (match.Success) slugs.Add(Slug(match.Groups[1].Value));
        }

        return slugs;
    }

    /// <summary>A file's text with every run of whitespace collapsed to one space, for quote matching across line wraps.</summary>
    internal static string Flattened(string relativePath) =>
        Regex.Replace(File.ReadAllText(Path.Combine(RepoLayout.Root.FullName, relativePath)), @"\s+", " ");

    private static T Load<T>(string file)
    {
        var path = Path.Combine(Directory.FullName, file);
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, s_json)
            ?? throw new InvalidOperationException($"'{path}' is empty.");
    }

    private static string ReadTranslationTestSources()
    {
        var testRoot = new DirectoryInfo(Path.Combine(RepoLayout.Root.FullName, "test"));
        var projects = testRoot.EnumerateDirectories("*EFCore.PostgreSQL*.Tests")
                               .Where(project => !project.Name.EndsWith("IntegrationTests", StringComparison.Ordinal))
                               .OrderBy(project => project.Name, StringComparer.Ordinal)
                               .ToList();
        if (projects.Count == 0)
            throw new InvalidOperationException($"No EF translation test project under '{testRoot.FullName}'.");

        var text = new System.Text.StringBuilder();
        foreach (var project in projects)
        foreach (var file in project.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;
            text.Append(File.ReadAllText(file.FullName)).Append('\n');
        }

        return text.ToString();
    }

    private static List<Seed> LoadSeeds()
    {
        var seeds = new DirectoryInfo(Path.Combine(Directory.FullName, "seeds"));
        return
        [
            .. seeds.EnumerateFiles("*.patch")
                    .OrderBy(file => file.Name, StringComparer.Ordinal)
                    .Select(Seed.Read)
        ];
    }

    private static Dictionary<string, string> DiscoverTestClasses()
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        var testRoot = new DirectoryInfo(Path.Combine(RepoLayout.Root.FullName, "test"));

        foreach (var file in testRoot.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(file.FullName);
            foreach (Match match in TestClassDeclaration().Matches(text))
            {
                var project = file.Directory!;
                while (project.Parent is { } parent && parent.FullName != testRoot.FullName) project = parent;
                classes[match.Groups["name"].Value] = project.Name;
            }
        }

        return classes;
    }

    [GeneratedRegex(@"\[TestClass\]\s*(?:\[[^\]]*\]\s*)*public\s+(?:sealed\s+|static\s+|partial\s+)*class\s+(?<name>\w+)", RegexOptions.Singleline)]
    private static partial Regex TestClassDeclaration();

    /// <summary>A sentence a document states about the library's behaviour, and what holds it.</summary>
    internal sealed class Claim
    {
        public string Id { get; init; } = "";

        /// <summary><c>file#heading-slug</c>: where the sentence is.</summary>
        public string Source { get; init; } = "";

        /// <summary>The sentence, verbatim, so the catalogue test can find it in the file.</summary>
        public string Quote { get; init; } = "";

        /// <summary>The restatement as "under conditions, observable result".</summary>
        [JsonPropertyName("claim")]
        public string Statement { get; init; } = "";

        /// <summary>claim, differential, metamorphic, exception or silent-default.</summary>
        public string Oracle { get; init; } = "";

        /// <summary>The public members a probe of the claim calls, as <see cref="ApiSurface"/> patterns.</summary>
        public IReadOnlyList<string> Surfaces { get; init; } = [];

        /// <summary>The test classes that assert the claim; empty is the worklist.</summary>
        public IReadOnlyList<string> HeldBy { get; init; } = [];

        /// <summary>The source file, before the <c>#</c>.</summary>
        internal string File => Source.Split('#')[0];

        /// <summary>The heading slug after the <c>#</c>, or null for a claim about the whole file.</summary>
        internal string? Heading => Source.Contains('#') ? Source[(Source.IndexOf('#') + 1)..] : null;
    }

    /// <summary>One row of PostgreSQL's surface, keyed by its catalogue signature, and what the library does about it.</summary>
    internal sealed class PostgresSurfaceEntry
    {
        /// <summary><c>left op right</c> for an operator, <c>name(argtypes)</c> for a function or aggregate, free text for <c>other</c>.</summary>
        public string Signature { get; init; } = "";

        /// <summary>operator, function, aggregate (listed rows) or other (SQL syntax and concrete-typed functions no listing query reaches).</summary>
        public string Kind { get; init; } = "";

        /// <summary>The PostgreSQL major the row first appeared in; 8 stands for anything older than 8.4.</summary>
        public double Since { get; init; }

        /// <summary>The <see cref="ApiSurface"/> key of the member that mirrors the row, or null.</summary>
        public string? Member { get; init; }

        /// <summary>The SQL fragment the no-database translation tests assert for the member, or null when it is in-memory only.</summary>
        public string? Translation { get; init; }

        /// <summary>Why there is no member, or null.</summary>
        public string? Waiver { get; init; }

        /// <summary>The issue that will add the member; required when there is neither a member nor a waiver.</summary>
        public int? Issue { get; init; }
    }

    /// <summary>The checked-in listing: its server version and one (kind, signature, result) per line.</summary>
    internal sealed record PostgresListing(string Version, IReadOnlyList<PostgresListing.Line> Lines)
    {
        internal sealed record Line(string Kind, string Signature, string Result);

        internal int Major => int.Parse(Version.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture);

        internal static PostgresListing Read(string path)
        {
            var version = "";
            var lines   = new List<Line>();
            foreach (var raw in System.IO.File.ReadLines(path))
            {
                if (raw.Length == 0) continue;
                if (raw.StartsWith("# PostgreSQL ", StringComparison.Ordinal)) { version = raw["# PostgreSQL ".Length..].Trim(); continue; }
                if (raw.StartsWith('#')) continue;
                var match = Regex.Match(raw, @"^(?<kind>operator|function|aggregate)\s+(?<signature>.+?) -> (?<result>.+)$");
                if (!match.Success) throw new InvalidOperationException($"'{path}' has a line the listing format does not allow: '{raw}'");
                lines.Add(new Line(match.Groups["kind"].Value, match.Groups["signature"].Value, match.Groups["result"].Value));
            }

            if (version.Length == 0) throw new InvalidOperationException($"'{path}' has no '# PostgreSQL <version>' header line.");
            return new PostgresListing(version, lines);
        }
    }

    /// <summary>A test class that checks the library against an independent answer over many cases.</summary>
    internal sealed record Oracle(string Class, string Project, string Kind, string Against, IReadOnlyList<string>? Needs)
    {
        internal bool NeedsDocker => Needs?.Contains("docker") == true;
    }

    /// <summary>A patch under <c>seeds/</c>: its header lines, the oracles it names and the files it changes.</summary>
    internal sealed record Seed(FileInfo File, string Defect, IReadOnlyList<string> Oracles, string Body)
    {
        internal static Seed Read(FileInfo file)
        {
            var text    = System.IO.File.ReadAllText(file.FullName);
            var diffAt  = text.IndexOf("diff --git", StringComparison.Ordinal);
            var header  = diffAt < 0 ? text : text[..diffAt];
            var body    = diffAt < 0 ? "" : text[diffAt..];
            var defect  = HeaderValue(header, "Seed");
            var oracles = HeaderValue(header, "Oracles")
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new Seed(file, defect, oracles, body);
        }

        /// <summary>The <c>a/</c> paths the diff changes.</summary>
        internal IReadOnlyList<string> ChangedFiles =>
        [
            .. Regex.Matches(Body, @"^diff --git a/(?<path>\S+) b/", RegexOptions.Multiline)
                    .Select(match => match.Groups["path"].Value)
        ];

        private static string HeaderValue(string header, string key)
        {
            var match = Regex.Match(header, $@"^{key}:\s*(?<value>.+?)\s*$", RegexOptions.Multiline);
            return match.Success ? match.Groups["value"].Value : "";
        }
    }
}

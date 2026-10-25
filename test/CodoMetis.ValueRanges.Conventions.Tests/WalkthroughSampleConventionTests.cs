using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// <c>docs/walkthrough.md</c> quotes <c>samples/Bookings</c>, the booking service
/// <c>test/consumer-smoke-test.sh</c> builds against the packed packages and runs against PostgreSQL.
/// The smoke test keeps the sample's answers true; this keeps the page true to the sample and to the
/// smoke test. Every C# block on the page carries a marker naming the file it quotes, and its lines must
/// appear in that file in one contiguous run, up to the indentation they have there. Every line of an
/// output block (a JSON response, the SQL, the stored rows) must appear in the smoke test, which asserts
/// it against the running sample. A quote or an output edited on the page alone, or code changed in the
/// sample alone, fails here.
/// </summary>
/// <remarks>
/// The marker is an HTML comment on the line before the fence, so the page renders without it:
/// <c>&lt;!-- sample: samples/Bookings/BookingApi.cs --&gt;</c>. A block of another language may carry one
/// too, and is then checked the same way.
/// </remarks>
[TestClass]
public sealed class WalkthroughSampleConventionTests
{
    private const string Walkthrough = "docs/walkthrough.md";

    private const string SampleProject = "samples/Bookings/Bookings.csproj";

    private const string SmokeTest = "test/consumer-smoke-test.sh";

    /// <summary>The languages of the blocks that show what the running sample printed or stored.</summary>
    private static readonly string[] OutputLanguages = ["json", "sql", "text"];

    private static readonly Regex Marker = new(@"^<!--\s*sample:\s*(?<path>\S+)\s*-->\s*$");

    [TestMethod]
    public void EveryCSharpBlock_NamesTheSampleFileItQuotes()
    {
        var unmarked = Blocks().Where(block => block.Path is null && block.Language is "csharp" or "cs" or "c#")
                               .Select(block => $"line {block.Line}")
                               .ToList();

        Assert.AreEqual(0, unmarked.Count,
            $"{Walkthrough} has C# blocks with no '<!-- sample: path -->' marker, so nothing checks them: "
          + string.Join(", ", unmarked));
    }

    [TestMethod]
    public void EveryQuotedBlock_IsInTheSampleFileItNames()
    {
        var quoted = Blocks().Where(block => block.Path is not null).ToList();

        // A page reworded past the marker would otherwise pass by checking nothing.
        Assert.IsTrue(quoted.Count >= 5, $"{Walkthrough} quotes only {quoted.Count} blocks from the sample; the markers are missing.");

        var problems = new List<string>();
        foreach (var block in quoted)
        {
            var file = Path.Combine(RepoLayout.Root.FullName, block.Path!);
            if (!block.Path!.StartsWith("samples/", StringComparison.Ordinal) || !File.Exists(file))
            {
                problems.Add($"line {block.Line}: '{block.Path}' is not a file under samples/");
                continue;
            }

            if (!Quotes(File.ReadAllLines(file), block.Lines))
                problems.Add($"line {block.Line}: the block starting '{block.Lines.FirstOrDefault()}' is not a contiguous run of {block.Path}");
        }

        Assert.AreEqual(0, problems.Count,
            string.Join(Environment.NewLine, problems.Prepend($"{Walkthrough} quotes code the sample no longer has (edit both together):")));
    }

    [TestMethod]
    public void EveryOutputBlock_IsAssertedByTheSmokeTest()
    {
        var outputs = Blocks().Where(block => OutputLanguages.Contains(block.Language)).ToList();
        Assert.IsTrue(outputs.Count >= 10, $"{Walkthrough} shows only {outputs.Count} output blocks; the languages changed?");

        var smoke   = File.ReadAllText(Path.Combine(RepoLayout.Root.FullName, SmokeTest));
        var missing = outputs.SelectMany(block => block.Lines.Select(line => (block.Line, Text: line.Trim())))
                             .Where(line => line.Text.Length > 0 && !smoke.Contains(line.Text, StringComparison.Ordinal))
                             .Select(line => $"line {line.Line}: {line.Text}")
                             .ToList();

        Assert.AreEqual(0, missing.Count,
            string.Join(Environment.NewLine, missing.Prepend(
                $"{Walkthrough} shows output that {SmokeTest} does not assert, so nothing checks it against the running sample:")));
    }

    [TestMethod]
    public void TheSample_TargetsWhatThePackagesShip()
    {
        // The smoke test moves the package reference to the version under test, but a reader copies the
        // project as it is here: on the 10.x line it has to say net10.0 and a 10.x version.
        var sample = XDocument.Load(Path.Combine(RepoLayout.Root.FullName, SampleProject));
        var core   = XDocument.Load(Path.Combine(RepoLayout.Root.FullName, "src", "CodoMetis.ValueRanges", "CodoMetis.ValueRanges.csproj"));

        Assert.AreEqual(Single(core, "TargetFramework"), Single(sample, "TargetFramework"),
            $"{SampleProject} targets a different framework from the packages.");

        var references = sample.Descendants("PackageReference")
                               .Where(reference => ((string?)reference.Attribute("Include"))?.StartsWith("CodoMetis.", StringComparison.Ordinal) == true)
                               .ToList();
        Assert.AreNotEqual(0, references.Count, $"{SampleProject} references no CodoMetis package.");
        foreach (var reference in references)
            Assert.AreEqual(RepoLayout.ShippedVersion, (string?)reference.Attribute("Version"),
                $"{SampleProject} references {(string?)reference.Attribute("Include")} at a version other than the one this branch ships.");
    }

    /// <summary>The lines of <paramref name="block"/>, in order and adjacent, somewhere in <paramref name="file"/>, each indented alike.</summary>
    private static bool Quotes(string[] file, IReadOnlyList<string> block)
    {
        if (block.Count == 0) return false;

        var lines = file.Select(line => line.TrimEnd()).ToArray();
        for (var start = 0; start + block.Count <= lines.Length; start++)
        {
            if (!lines[start].EndsWith(block[0], StringComparison.Ordinal)) continue;

            var indent = lines[start][..^block[0].Length];
            if (indent.Trim().Length != 0) continue;

            var all = true;
            for (var offset = 0; offset < block.Count && all; offset++)
            {
                var expected = block[offset];
                var actual   = lines[start + offset];
                all = expected.Length == 0 ? actual.Length == 0 : actual == indent + expected;
            }

            if (all) return true;
        }

        return false;
    }

    private static string Single(XDocument project, string property) =>
        project.Descendants(property).Select(element => element.Value.Trim()).Single();

    private static List<Block> Blocks()
    {
        var lines  = File.ReadAllLines(Path.Combine(RepoLayout.Root.FullName, Walkthrough));
        var blocks = new List<Block>();
        for (var index = 0; index < lines.Length; index++)
        {
            // A fence may be indented, inside a list item; its content is then indented alike.
            var fence = lines[index].TrimStart();
            if (!fence.StartsWith("```", StringComparison.Ordinal)) continue;

            var indent   = lines[index].Length - fence.Length;
            var language = fence[3..].Trim().ToLowerInvariant();
            var marker   = index > 0 ? Marker.Match(lines[index - 1].Trim()) : Match.Empty;
            var start    = index;
            var body     = new List<string>();
            for (index++; index < lines.Length && !lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal); index++)
                body.Add(lines[index].Length >= indent ? lines[index][indent..].TrimEnd() : lines[index].TrimEnd());

            blocks.Add(new Block(start + 1, language, marker.Success ? marker.Groups["path"].Value : null, body));
        }

        Assert.AreNotEqual(0, blocks.Count, $"{Walkthrough} has no code blocks.");
        return blocks;
    }

    private sealed record Block(int Line, string Language, string? Path, IReadOnlyList<string> Lines);
}

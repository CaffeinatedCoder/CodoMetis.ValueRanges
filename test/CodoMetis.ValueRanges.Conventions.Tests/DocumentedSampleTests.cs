using System.Globalization;
using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Samples in the consumer docs whose arithmetic a reader copies, run as the doc spells them. The
/// sample is read from the doc rather than repeated here, so editing either side cannot leave the
/// other behind: until #278 the span sample in <c>docs/serialization.md</c> sliced <c>line[7..29]</c>,
/// one character short of the literal, and threw <see cref="FormatException"/> for everyone who ran it.
/// </summary>
[TestClass]
public sealed class DocumentedSampleTests
{
    private static readonly Regex SpanLine =
        new(@"ReadOnlySpan<char> line = ""(?<text>[^""]+)""\.AsSpan\(\);");

    private static readonly Regex SpanSlice =
        new(@"var period = DateRange\.Parse\(line\[(?<start>\d+)\.\.(?<end>\d+)\], null\);");

    /// <summary>The slice in "Parse from a span" is exactly the range literal inside the line.</summary>
    [TestMethod]
    public void ParseFromASpan_SliceIsTheLiteral()
    {
        var doc   = File.ReadAllText(Path.Combine(RepoLayout.Root.FullName, "docs", "serialization.md"));
        var line  = SpanLine.Match(doc);
        var slice = SpanSlice.Match(doc);

        // A sample reworded past these patterns would otherwise pass by checking nothing.
        Assert.IsTrue(line.Success && slice.Success,
                      "docs/serialization.md no longer has the span sample this test runs; update the patterns.");

        var text  = line.Groups["text"].Value.AsSpan();
        var start = int.Parse(slice.Groups["start"].Value, CultureInfo.InvariantCulture);
        var end   = int.Parse(slice.Groups["end"].Value, CultureInfo.InvariantCulture);

        Assert.AreEqual(
            DateRange.CreateFinite(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)),
            DateRange.Parse(text[start..end], null),
            $"line[{start}..{end}] is '{text[start..end]}'");
    }
}

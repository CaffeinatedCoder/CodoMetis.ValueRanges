using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// PostgreSQL's own range, multirange and array surface is accountable to the library: every
/// <c>pg_operator</c> and <c>pg_proc</c> row over <c>anyrange</c>, <c>anymultirange</c>,
/// <c>anyarray</c> and <c>anycompatiblearray</c> has a member that mirrors it, a waiver saying why
/// none should, or an issue that will add one.
/// </summary>
/// <remarks>
/// <para>
/// <c>claims.json</c> holds the documents to the tests and <c>surface-waivers.json</c> holds the
/// public members to the claims; nothing held the server's surface to the library until the sweep
/// of 2026-10-17 enumerated it by hand and found seven gaps. <c>test/oracles/postgres-surface.json</c>
/// is that enumeration kept as data, one entry per catalogue row; this class holds it to the
/// library (<see cref="ApiSurface"/>), to the translation tests (the asserted SQL fragment) and to
/// the server (<c>postgres-surface.catalog.txt</c>, the raw listing <c>tools/oracles.cs pg-surface</c>
/// regenerates from a container, locally). An entry with neither member nor waiver carries its
/// issue: that is the worklist, like a claim's empty <c>heldBy</c>.
/// </para>
/// <para>
/// None of it needs a database. A new server major is noticed when the listing is refreshed: its
/// new rows have no entry and <see cref="EveryListedRow_HasAnEntry"/> names them.
/// </para>
/// </remarks>
[TestClass]
public sealed class OraclePostgresSurfaceTests
{
    // 162 listed rows and 171 entries at the first catalogue (PostgreSQL 18.1, 2026-10-17). Floors
    // against a listing or a file going quiet, not targets.
    private const int KnownListedRows = 150;
    private const int KnownEntries    = 160;

    private static readonly string[] s_kinds = ["operator", "function", "aggregate", "other"];

    [TestMethod]
    public void Catalogue_IsNotEmpty_UniqueAndSorted()
    {
        var entries = OracleCatalogue.PostgresSurface;
        Assert.IsTrue(entries.Count >= KnownEntries, $"Found {entries.Count} entries, fewer than the {KnownEntries} known to exist.");

        var signatures = entries.Select(entry => entry.Signature).ToList();
        CollectionAssert.AllItemsAreUnique(signatures, "Duplicate signatures.");

        var sorted = entries.OrderBy(entry => Array.IndexOf(s_kinds, entry.Kind))
                            .ThenBy(entry => entry.Signature, StringComparer.Ordinal)
                            .Select(entry => entry.Signature)
                            .ToList();
        CollectionAssert.AreEqual(sorted, signatures, "Entries are sorted by kind (operator, function, aggregate, other), then by signature ordinally.");

        var unknown = entries.Where(entry => !s_kinds.Contains(entry.Kind)).Select(entry => $"{entry.Signature}: '{entry.Kind}'").ToList();
        Assert.AreEqual(0, unknown.Count, "Kind must be one of " + string.Join("|", s_kinds) + ": " + string.Join(", ", unknown));

        var major = OracleCatalogue.PostgresSurfaceListing.Major;
        var since = entries.Where(entry => entry.Since is < 8 or > 99 || entry.Since > major).Select(entry => $"{entry.Signature}: {entry.Since}").ToList();
        Assert.AreEqual(0, since.Count, $"'since' is the PostgreSQL major the row appeared in, 8 at the oldest and the listing's {major} at the newest: " + string.Join(", ", since));
    }

    [TestMethod]
    public void EveryEntry_HasAMemberAWaiverOrAnIssue()
    {
        var undecided = OracleCatalogue.PostgresSurface
                                       .Where(entry => entry.Member is null && entry.Waiver is null && entry.Issue is null)
                                       .Select(entry => entry.Signature)
                                       .ToList();

        Assert.AreEqual(0, undecided.Count,
                        $"{undecided.Count} rows of PostgreSQL's surface have no member, no waiver and no issue. Either the library "
                      + "mirrors them — then name the member — or it should not, and the waiver says why, or an issue will add it:"
                      + Environment.NewLine + string.Join(Environment.NewLine, undecided.Select(signature => "  " + signature)));
    }

    [TestMethod]
    public void EveryMember_IsAPublicMember()
    {
        var stale = OracleCatalogue.PostgresSurface
                                   .Where(entry => entry.Member is { } member && ApiSurface.Matching(member).Count == 0)
                                   .Select(entry => $"{entry.Signature} → '{entry.Member}'")
                                   .ToList();

        Assert.AreEqual(0, stale.Count,
                        "An entry names a public member that does not exist (keys are TypeName.Member, nested types with '+', '*' is a wildcard): "
                      + string.Join(", ", stale));
    }

    [TestMethod]
    public void EveryTranslation_IsAssertedByATranslationTest()
    {
        var sources = OracleCatalogue.TranslationTestSources;
        Assert.IsTrue(sources.Length > 10_000, "The EF translation test sources were not read.");

        var problems = new List<string>();
        foreach (var entry in OracleCatalogue.PostgresSurface.Where(entry => entry.Translation is not null))
        {
            if (entry.Member is null)
                problems.Add($"{entry.Signature}: has a translation '{entry.Translation}' but no member to translate");
            else if (entry.Translation!.Trim().Length == 0)
                problems.Add($"{entry.Signature}: the translation is blank");
            else if (!sources.Contains(entry.Translation, StringComparison.Ordinal))
                problems.Add($"{entry.Signature}: no EF translation test asserts '{entry.Translation}'");
        }

        Assert.AreEqual(0, problems.Count,
                        "A translation is the SQL fragment the no-database EF tests assert for the member; null means the member is in-memory only:"
                      + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public void NoWaiver_IsStale_AndEveryWaiverGivesAReason()
    {
        var problems = new List<string>();
        var headings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var entry in OracleCatalogue.PostgresSurface.Where(entry => entry.Waiver is not null))
        {
            if (entry.Member is not null)
                problems.Add($"{entry.Signature}: is waived ('{entry.Waiver}') but now has the member '{entry.Member}' — remove the waiver");
            if (entry.Issue is not null)
                problems.Add($"{entry.Signature}: is waived and also filed as #{entry.Issue} — decide");
            if (entry.Waiver!.Trim().Length < 10)
                problems.Add($"{entry.Signature}: needs a reason");

            // A waiver that points at a document section must point at one that exists.
            foreach (Match match in Regex.Matches(entry.Waiver, @"(?<file>docs/[\w-]+\.md)#(?<heading>[\w-]+)"))
            {
                var file = match.Groups["file"].Value;
                if (!headings.TryGetValue(file, out var slugs))
                    headings[file] = slugs = File.Exists(Path.Combine(RepoLayout.Root.FullName, file)) ? OracleCatalogue.HeadingSlugs(file) : [];
                if (!slugs.Contains(match.Groups["heading"].Value))
                    problems.Add($"{entry.Signature}: the waiver points at '{match.Value}', which does not exist");
            }
        }

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems.Prepend("Waivers to clean up:")));
    }

    [TestMethod]
    public void EveryIssue_IsANumber_AndOnlyFiledWhereNothingElseHolds()
    {
        var problems = OracleCatalogue.PostgresSurface
                                      .Where(entry => entry.Issue is { } issue && (issue <= 0 || entry.Member is not null))
                                      .Select(entry => entry.Member is not null
                                                  ? $"{entry.Signature}: #{entry.Issue} is filed but the member '{entry.Member}' exists — close the issue and drop the number"
                                                  : $"{entry.Signature}: '{entry.Issue}' is not an issue number")
                                      .ToList();

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    [TestMethod]
    public void EveryListedRow_HasAnEntry()
    {
        var listing = OracleCatalogue.PostgresSurfaceListing;
        Assert.IsTrue(listing.Lines.Count >= KnownListedRows, $"The listing holds {listing.Lines.Count} rows, fewer than the {KnownListedRows} known to exist.");

        var entries = OracleCatalogue.PostgresSurface.ToDictionary(entry => entry.Signature, StringComparer.Ordinal);
        var missing = listing.Lines
                             .Where(line => !entries.TryGetValue(line.Signature, out var entry) || entry.Kind != line.Kind)
                             .Select(line => $"  {line.Kind,-10} {line.Signature} -> {line.Result}")
                             .ToList();

        Assert.AreEqual(0, missing.Count,
                        $"{missing.Count} rows of the PostgreSQL {listing.Version} listing have no entry of their kind in postgres-surface.json. "
                      + "A new server function or operator is a decision: a member, a waiver, or an issue:"
                      + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [TestMethod]
    public void EveryListedKindEntry_IsInTheListing()
    {
        var listed = OracleCatalogue.PostgresSurfaceListing.Lines.Select(line => (line.Kind, line.Signature)).ToHashSet();
        var gone   = OracleCatalogue.PostgresSurface
                                    .Where(entry => entry.Kind != "other" && !listed.Contains((entry.Kind, entry.Signature)))
                                    .Select(entry => $"  {entry.Kind,-10} {entry.Signature}")
                                    .ToList();

        Assert.AreEqual(0, gone.Count,
                        "An operator, function or aggregate entry is not in the listing: either the server dropped it, or the "
                      + "signature is misspelt. Refresh the listing with `dotnet run tools/oracles.cs pg-surface` or fix the entry:"
                      + Environment.NewLine + string.Join(Environment.NewLine, gone));
    }
}

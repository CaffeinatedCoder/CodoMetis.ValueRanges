namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The claim catalogue (<c>test/oracles/claims.json</c>) stays true to the documents it quotes and
/// to the tests it names.
/// </summary>
/// <remarks>
/// <para>
/// A claim is a sentence a consumer-facing document states about the library, in a form a test can
/// decide, together with the oracle kind, the public members a probe of it calls and the test
/// classes that hold it. The catalogue is only worth keeping if it cannot drift: a reworded sentence
/// must stale its claim, a renamed test class must be found, a surface that no longer exists must
/// be noticed. Each test here closes one of those gaps mechanically.
/// </para>
/// <para>
/// A claim with an empty <c>heldBy</c> is allowed. That is the worklist: the promises no oracle
/// checks yet. <see cref="EveryPromiseDocument_CarriesAtLeastOneClaim"/> keeps the list honest in
/// the other direction, so a new document cannot ship without anyone having read it for promises.
/// </para>
/// </remarks>
[TestClass]
public sealed class OracleCatalogueTests
{
    // 60 at the first catalogue (2026-10-17). A floor, not a target: the discovery would otherwise
    // pass on an empty array.
    private const int KnownClaims = 100;

    [TestMethod]
    public void Catalogue_IsNotEmpty_AndIdsAreUniqueAndSorted()
    {
        var ids = OracleCatalogue.Claims.Select(claim => claim.Id).ToList();

        Assert.IsTrue(ids.Count >= KnownClaims, $"Found {ids.Count} claims, fewer than the {KnownClaims} known to exist.");
        CollectionAssert.AllItemsAreUnique(ids, "Duplicate claim ids.");
        CollectionAssert.AreEqual(ids.OrderBy(id => id, StringComparer.Ordinal).ToList(), ids, "Claims must be sorted by id.");
        Assert.IsTrue(ids.All(id => System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Z]\d{2}-[a-z0-9-]+$")),
                      "Every id is a letter, two digits, a hyphen and a kebab-case slug: "
                    + string.Join(", ", ids.Where(id => !System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Z]\d{2}-[a-z0-9-]+$"))));
    }

    [TestMethod]
    public void EveryClaim_NamesAnOracleKind()
    {
        string[] kinds   = ["claim", "differential", "metamorphic", "exception", "silent-default"];
        var      unknown = OracleCatalogue.Claims.Where(claim => !kinds.Contains(claim.Oracle)).Select(claim => $"{claim.Id}: '{claim.Oracle}'").ToList();

        Assert.AreEqual(0, unknown.Count, "Oracle kind must be one of " + string.Join("|", kinds) + ": " + string.Join(", ", unknown));
    }

    [TestMethod]
    public void EveryClaim_QuotesItsSourceVerbatim()
    {
        var problems = new List<string>();
        var flattened = new Dictionary<string, string>(StringComparer.Ordinal);
        var headings  = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var claim in OracleCatalogue.Claims)
        {
            var path = Path.Combine(RepoLayout.Root.FullName, claim.File);
            if (!File.Exists(path))
            {
                problems.Add($"{claim.Id}: source file '{claim.File}' does not exist");
                continue;
            }

            if (claim.Heading is { } heading)
            {
                if (!headings.TryGetValue(claim.File, out var slugs))
                    headings[claim.File] = slugs = OracleCatalogue.HeadingSlugs(claim.File);
                if (!slugs.Contains(heading))
                    problems.Add($"{claim.Id}: '{claim.File}' has no heading '#{heading}' (has: {string.Join(", ", slugs)})");
            }

            if (!flattened.TryGetValue(claim.File, out var text))
                flattened[claim.File] = text = OracleCatalogue.Flattened(claim.File);

            var quote = System.Text.RegularExpressions.Regex.Replace(claim.Quote, @"\s+", " ").Trim();
            if (quote.Length < 20)
                problems.Add($"{claim.Id}: the quote is too short to pin a sentence: '{quote}'");
            else if (!text.Contains(quote, StringComparison.Ordinal))
                problems.Add($"{claim.Id}: '{claim.File}' no longer contains the quoted sentence — reword the claim or retire it: '{quote}'");
        }

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems.Prepend("Claims out of step with their documents:")));
    }

    [TestMethod]
    public void EveryClaim_NamesTestClassesThatExist()
    {
        // A holder must be compiled, not merely present under test/: a class its project removes from
        // compilation asserts nothing, so naming it would count the claim as held while no test holds it
        // (#321). One that is compiled out on this line is allowed only with a record in compiled-out.json,
        // which makes the gap and the issue that closes it visible (`tools/oracles.cs status` lists the
        // claim as unheld).
        var recorded = OracleCatalogue.CompiledOut.Select(record => record.Class).ToHashSet(StringComparer.Ordinal);
        var missing  = new List<string>();
        foreach (var (id, held) in OracleCatalogue.Claims.SelectMany(claim => claim.HeldBy.Select(held => (claim.Id, held))))
        {
            if (OracleCatalogue.TestClasses.ContainsKey(held)) continue;
            if (OracleCatalogue.CompiledOutTestClasses.TryGetValue(held, out var project))
            {
                if (!recorded.Contains(held))
                    missing.Add($"{id} → {held} (compiled out of {project}: record it in compiled-out.json with the issue that compiles it back, or name a test that is compiled)");
                continue;
            }

            missing.Add($"{id} → {held}");
        }

        Assert.IsTrue(OracleCatalogue.TestClasses.Count >= 100, $"Discovered only {OracleCatalogue.TestClasses.Count} test classes under test/.");
        Assert.AreEqual(0, missing.Count, "heldBy names a test class that is not compiled: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void EveryCompiledOutRecord_NamesAClassItsProjectCompilesOut()
    {
        // The other direction: a record outlives its gap the moment the class is compiled again, and
        // would keep hiding a claim it no longer needs to (#333 removes the ComplexIndexes record).
        var problems = new List<string>();
        foreach (var record in OracleCatalogue.CompiledOut)
        {
            if (OracleCatalogue.TestClasses.TryGetValue(record.Class, out var compiledIn))
                problems.Add($"{record.Class} is compiled again (in {compiledIn}): drop its record");
            else if (!OracleCatalogue.CompiledOutTestClasses.TryGetValue(record.Class, out var project))
                problems.Add($"{record.Class} is not a [TestClass] under test/");
            else if (project != record.Project)
                problems.Add($"{record.Class} is recorded in {record.Project} but compiled out of {project}");

            if (record.Issue <= 0 || string.IsNullOrWhiteSpace(record.Reason))
                problems.Add($"{record.Class} needs the issue that compiles it back and a reason");
        }

        CollectionAssert.AllItemsAreUnique(OracleCatalogue.CompiledOut.Select(record => record.Class).ToList(), "Duplicate compiled-out records.");
        Assert.AreEqual(0, problems.Count, "compiled-out.json is out of step with the test projects: " + string.Join("; ", problems));
    }

    [TestMethod]
    public void EveryClaim_NamesSurfacesThatExist()
    {
        var stale = OracleCatalogue.Claims
                                   .SelectMany(claim => claim.Surfaces.Select(surface => (claim.Id, surface)))
                                   .Where(pair => ApiSurface.Matching(pair.surface).Count == 0)
                                   .Select(pair => $"{pair.Id} → '{pair.surface}'")
                                   .ToList();

        Assert.AreEqual(0, stale.Count,
                        "A claim names a public member that does not exist (keys are TypeName.Member, nested types with '+', '*' is a wildcard): "
                      + string.Join(", ", stale));
    }

    [TestMethod]
    public void EveryClaim_NamesAtLeastOneSurface()
    {
        var bare = OracleCatalogue.Claims.Where(claim => claim.Surfaces.Count == 0).Select(claim => claim.Id).ToList();
        Assert.AreEqual(0, bare.Count, "A claim with no surface cannot be probed: " + string.Join(", ", bare));
    }

    [TestMethod]
    public void EveryPromiseDocument_CarriesAtLeastOneClaim()
    {
        var claimed = OracleCatalogue.Claims.Select(claim => claim.File).ToHashSet(StringComparer.Ordinal);
        var silent  = OracleCatalogue.PromiseDocuments.Where(document => !claimed.Contains(document)).ToList();

        Assert.AreEqual(0, silent.Count, "A consumer-facing document has no claim in the catalogue: " + string.Join(", ", silent));
    }

    [TestMethod]
    public void EveryHeldClaim_IsHeldByARegisteredOracleOrATest()
    {
        // heldBy may name any test class, but the oracle registry must know every class that
        // claims to be an oracle — a sweep over generated cases, or a probe over the product of
        // consumer settings (test/oracles/axes.md) — so that it has a seed.
        var oracles = OracleCatalogue.Oracles.Select(oracle => oracle.Class).ToHashSet(StringComparer.Ordinal);
        var unregistered = OracleCatalogue.Claims
                                          .SelectMany(claim => claim.HeldBy)
                                          .Distinct()
                                          .Where(held => held.EndsWith("OracleTests", StringComparison.Ordinal) || held.EndsWith("ParityTests", StringComparison.Ordinal) || held.EndsWith("ProbeTests", StringComparison.Ordinal))
                                          .Where(held => !oracles.Contains(held))
                                          .ToList();

        Assert.AreEqual(0, unregistered.Count,
                        "A class named like an oracle holds a claim but is not registered in oracles.json, so no seed is required of it: "
                      + string.Join(", ", unregistered));
    }
}

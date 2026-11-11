using CodoMetis.ValueRanges.Oracles;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Holds <c>test/oracles/translation-support.json</c> to the public API without a database: every query
/// member (<see cref="QueryMembers"/>) is classified, every entry still classifies one, and every entry
/// says what a reader needs (#384). Whether each classification is true is
/// <c>TranslationSupportProbeTests</c>' to show, against EF Core and PostgreSQL.
/// </summary>
[TestClass]
public sealed class TranslationSupportInventoryTests
{
    [TestMethod]
    public void EveryQueryMember_IsClassified()
    {
        var unclassified = QueryMembers.All.Where(member => TranslationSupport.For(member.Key) is null).Select(member => member.Key).ToList();

        Assert.AreEqual(0, unclassified.Count,
                        "Query members test/oracles/translation-support.json does not classify. Add an entry saying whether each runs as SQL "
                      + "(server), in memory only (client, with the reason) or will run as SQL (planned, with the issue):"
                      + Environment.NewLine + string.Join(Environment.NewLine, unclassified));
    }

    [TestMethod]
    public void EveryEntry_ClassifiesAMember()
    {
        // An entry an earlier one shadows completely classifies nothing, so it is stale too.
        var stale = TranslationSupport.All.Where(entry => !QueryMembers.All.Any(member => TranslationSupport.For(member.Key) == entry))
                                          .Select(entry => entry.Members).ToList();

        Assert.AreEqual(0, stale.Count, $"Entries that classify no query member: {string.Join("; ", stale)}");
    }

    [TestMethod]
    public void EveryEntry_SaysWhatItsSupportNeeds()
    {
        var problems = new List<string>();
        foreach (var entry in TranslationSupport.All)
        {
            foreach (var support in new[] { entry.Support }.Concat(entry.Except?.Values.AsEnumerable() ?? []))
            {
                if (!TranslationSupport.Supports.Contains(support)) problems.Add($"{entry.Members}: support '{support}' is none of {string.Join(", ", TranslationSupport.Supports)}");
            }

            if (string.IsNullOrWhiteSpace(entry.Reason)) problems.Add($"{entry.Members}: no reason");
            if (entry.Support == "planned" && entry.Issue is not { } issue)
                problems.Add($"{entry.Members}: planned without the issue that adds it");
            if (entry.Issue is { } number && !System.Text.RegularExpressions.Regex.IsMatch(number, @"^#\d+$"))
                problems.Add($"{entry.Members}: issue '{number}' is not #n");
        }

        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Oracles;

/// <summary>
/// The translation-support inventory, read from <c>test/oracles/translation-support.json</c>: what EF
/// Core does with each public query member (#384).
/// </summary>
/// <remarks>
/// <para>
/// Each entry classifies the members its pattern matches, the first matching entry winning:
/// <c>server</c> (translates, and answers in the database what it answers in memory), <c>client</c>
/// (refused where the server must evaluate it, a filter or a null test, and correct in a final
/// projection, where EF Core evaluates it on the loaded rows; <c>reason</c> says why) or <c>planned</c>
/// (as <c>client</c> today, with the <c>issue</c> that adds the translation). A pattern is a member key
/// with <c>*</c> as a wildcard: <c>RangeExtensions.Clamp(*</c>, <c>*Set.Item[*</c>.
/// </para>
/// <para>
/// <c>except</c> names the closed types a generic member behaves differently for, with their support.
/// A defect is not a classification: it is a disagreement the probe finds between an entry and the
/// server, and it goes through <see cref="Candidates.Verdict"/> with its issue.
/// </para>
/// <para>
/// Linked into the conventions project, which holds the file consistent without a database, and into
/// the integration project, whose probe holds it against EF Core and PostgreSQL.
/// </para>
/// </remarks>
public static class TranslationSupport
{
    public sealed record Entry(string Members, string Support, string? Reason = null, string? Issue = null, Dictionary<string, string>? Except = null)
    {
        private Regex? _pattern;

        public bool Matches(string key) =>
            (_pattern ??= new Regex("^" + string.Join(".*", Members.Split('*').Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant)).IsMatch(key);

        /// <summary>The support for one closed type: its exception if it has one, the entry's otherwise.</summary>
        public string SupportFor(string closedType) => Except is not null && Except.TryGetValue(closedType, out var support) ? support : Support;
    }

    public static readonly string[] Supports = ["server", "client", "planned"];

    private static readonly Lazy<IReadOnlyList<Entry>> s_all = new(Load);

    public static IReadOnlyList<Entry> All => s_all.Value;

    /// <summary>The entry that classifies <paramref name="key"/>: the first whose pattern matches it.</summary>
    public static Entry? For(string key) => All.FirstOrDefault(entry => entry.Matches(key));

    private static IReadOnlyList<Entry> Load()
    {
        var path = Path.Combine(RepositoryRoot(), "test", "oracles", "translation-support.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, TranslationSupportJsonContext.Default.ListEntry)
            ?? throw new InvalidOperationException($"'{path}' holds no array.");
    }

    private static string RepositoryRoot()
    {
        const string marker = "CodoMetis.ValueRanges.slnx";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, marker))) return directory.FullName;
        }

        throw new InvalidOperationException($"Could not find '{marker}' above '{AppContext.BaseDirectory}'.");
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<TranslationSupport.Entry>))]
internal sealed partial class TranslationSupportJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

#:property PublishAot=false
// The oracle machinery's local tool. Never part of CI; the convention tests hold the catalogue.
//
//   dotnet run tools/oracles.cs seeds [name filter]   prove that every seed is caught
//   dotnet run tools/oracles.cs status                the catalogue's worklist
//
// `seeds`: for each test/oracles/seeds/<defect>.patch, a scratch copy of the working tree (bin/, obj/
// and .git/ left out, so uncommitted work is included) under the OS temp directory, the patch applied,
// and each oracle class the patch's `Oracles:` line names run in its project. A seed is caught by an
// oracle when at least one of its tests fails with a message that is not a known candidate; one line
// per (seed, oracle), and exit code 1 unless every pair is CAUGHT. Oracles that need Docker are run
// too — start Docker first, or pass a filter that leaves them out.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var root = FindRoot();
var oraclesDir = Path.Combine(root, "test", "oracles");
var command = args.Length > 0 ? args[0] : "status";
return command switch
{
    "seeds"  => Seeds(args.Length > 1 ? args[1] : ""),
    "status" => Status(),
    _        => Usage()
};

int Usage()
{
    Console.Error.WriteLine("usage: dotnet run tools/oracles.cs seeds [filter] | status");
    return 2;
}

int Seeds(string filter)
{
    var oracles = Json<List<Oracle>>("oracles.json").ToDictionary(o => o.Class, StringComparer.Ordinal);
    var seeds = Directory.GetFiles(Path.Combine(oraclesDir, "seeds"), "*.patch")
                         .Where(s => Path.GetFileName(s).Contains(filter, StringComparison.OrdinalIgnoreCase))
                         .Order(StringComparer.Ordinal)
                         .ToList();
    if (seeds.Count == 0)
    {
        Console.WriteLine($"no seed matches '{filter}' in {oraclesDir}/seeds");
        return 1;
    }

    var allCaught = true;
    foreach (var seed in seeds)
    {
        var name = Path.GetFileName(seed);
        var named = Header(File.ReadAllText(seed), "Oracles").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var scratch = Path.Combine(Path.GetTempPath(), $"valueranges-seed-{Guid.NewGuid():N}");
        try
        {
            CopyTree(root, scratch);
            Require(Run(scratch, "git", "apply", "--whitespace=nowarn", seed), "apply");

            var byProject = named.Select(n => oracles.TryGetValue(n, out var o) ? o : throw new SeedError($"'{n}' is not in oracles.json"))
                                 .GroupBy(o => o.Project);
            foreach (var group in byProject)
            {
                var project = Path.Combine("test", group.Key);
                Require(Run(scratch, "dotnet", "build", project, "-c", "Release", "-nologo", "-v", "q"), $"build {group.Key}");
                foreach (var oracle in group)
                {
                    var trx = Path.Combine(scratch, $"{oracle.Class}.trx");
                    Run(scratch, "dotnet", "test", project, "-c", "Release", "--no-build", "-nologo",
                        "--filter", $"FullyQualifiedName~.{oracle.Class}.",
                        "--logger", $"trx;LogFileName={trx}");
                    var verdict = Verdict(trx, oracle.Class);
                    allCaught &= verdict.StartsWith("CAUGHT", StringComparison.Ordinal);
                    Console.WriteLine($"{verdict[..8]}{name} → {oracle.Class}: {verdict[8..]}");
                }
            }
        }
        catch (SeedError e)
        {
            allCaught = false;
            Console.WriteLine($"ERROR   {name}: {e.Message}");
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    Console.WriteLine(allCaught ? "every seed is caught" : "A SEED IS NOT CAUGHT — the oracle it names cannot fail for that defect");
    return allCaught ? 0 : 1;
}

string Verdict(string trx, string oracle)
{
    if (!File.Exists(trx)) return "ERROR   no test results written (did the class run?)";
    XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    var results = XDocument.Load(trx).Descendants(ns + "UnitTestResult").ToList();
    if (results.Count == 0) return $"ERROR   no test of {oracle} ran";
    var failed = results.Where(r => (string?)r.Attribute("outcome") == "Failed")
                        .Select(r => ((string?)r.Attribute("testName") ?? "?", r.Descendants(ns + "Message").FirstOrDefault()?.Value ?? ""))
                        .ToList();
    var caught = failed.Where(f => !f.Item2.Contains("[known candidate:", StringComparison.Ordinal)).ToList();
    var inconclusive = results.Count(r => (string?)r.Attribute("outcome") is "NotExecuted" or "Inconclusive");
    if (caught.Count > 0) return $"CAUGHT  {caught.Count} of {results.Count} tests fail, e.g. {caught[0].Item1}";
    if (inconclusive == results.Count) return $"ERROR   all {results.Count} tests inconclusive (no database?)";
    return $"MISSED  all {results.Count} tests pass with the defect planted";
}

int Status()
{
    var claims = Json<List<Claim>>("claims.json");
    var oracles = Json<List<Oracle>>("oracles.json");
    var seeds = Directory.GetFiles(Path.Combine(oraclesDir, "seeds"), "*.patch")
                         .Select(s => (Name: Path.GetFileName(s), Oracles: Header(File.ReadAllText(s), "Oracles").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
                         .ToList();
    var candidates = Json<List<Candidate>>("candidates.json");

    var held = claims.Where(c => c.HeldBy is { Count: > 0 }).ToList();
    Console.WriteLine($"claims: {claims.Count}, held by a test: {held.Count}, unheld (the worklist): {claims.Count - held.Count}");
    foreach (var c in claims.Where(c => c.HeldBy is not { Count: > 0 }))
        Console.WriteLine($"  UNHELD  {c.Id,-32} {c.Source}");

    var seeded = seeds.SelectMany(s => s.Oracles).ToHashSet(StringComparer.Ordinal);
    Console.WriteLine($"oracles: {oracles.Count}, seeds: {seeds.Count}, oracles without a seed: {oracles.Count(o => !seeded.Contains(o.Class))}");
    foreach (var o in oracles.Where(o => !seeded.Contains(o.Class)))
        Console.WriteLine($"  UNSEEDED {o.Class}");

    Console.WriteLine($"candidates: {candidates.Count} ({candidates.Count(c => c.Issue.Length == 0)} without an issue)");
    foreach (var c in candidates)
        Console.WriteLine($"  {c.Oracle}: '{c.Row}' [{c.Family}] {(c.Issue.Length == 0 ? "NOT FILED" : c.Issue)}");

    Console.WriteLine("document headings with no claim (read them for promises):");
    foreach (var file in new[] { "README.md", "docs/ranges.md", "docs/value-sets.md", "docs/serialization.md", "docs/efcore.md", "docs/getting-started.md" })
    {
        var claimed = claims.Where(c => c.Source.StartsWith(file + "#", StringComparison.Ordinal)).Select(c => c.Source[(file.Length + 1)..]).ToHashSet(StringComparer.Ordinal);
        var silent = Headings(Path.Combine(root, file)).Where(h => !claimed.Contains(h)).ToList();
        if (silent.Count > 0) Console.WriteLine($"  {file}: {string.Join(", ", silent)}");
    }

    return 0;
}

static IEnumerable<string> Headings(string path)
{
    var inCode = false;
    foreach (var line in File.ReadLines(path))
    {
        if (line.StartsWith("```", StringComparison.Ordinal)) inCode = !inCode;
        if (inCode) continue;
        var m = Regex.Match(line, @"^#{1,6}\s+(.*?)\s*#*\s*$");
        if (!m.Success) continue;
        var text = Regex.Replace(m.Groups[1].Value.Trim().ToLowerInvariant(), @"[^\p{L}\p{N}\s-]", "");
        yield return Regex.Replace(text, @"\s", "-");
    }
}

T Json<T>(string file)
{
    using var stream = File.OpenRead(Path.Combine(oraclesDir, file));
    return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
        ?? throw new InvalidOperationException($"{file} is empty");
}

static string Header(string patch, string key)
{
    var m = Regex.Match(patch, $@"^{key}:\s*(?<v>.+?)\s*$", RegexOptions.Multiline);
    return m.Success ? m.Groups["v"].Value : "";
}

static void CopyTree(string from, string to)
{
    string[] skip = ["bin", "obj", ".git", "TestResults", "dist", ".idea", ".vs"];
    foreach (var dir in Directory.EnumerateDirectories(from, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
    {
        var relative = Path.GetRelativePath(from, dir);
        if (relative.Split(Path.DirectorySeparatorChar).Any(skip.Contains)) continue;
        Directory.CreateDirectory(Path.Combine(to, relative));
    }
    foreach (var file in Directory.EnumerateFiles(from, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
    {
        var relative = Path.GetRelativePath(from, file);
        if (relative.Split(Path.DirectorySeparatorChar).Any(skip.Contains)) continue;
        File.Copy(file, Path.Combine(to, relative));
    }
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "CodoMetis.ValueRanges.slnx"))) return dir.FullName;
    throw new InvalidOperationException("run from inside the repository");
}

static (int Exit, string Output) Run(string dir, string file, params string[] arguments)
{
    var info = new ProcessStartInfo(file) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in arguments) info.ArgumentList.Add(a);
    using var p = Process.Start(info)!;
    var err = p.StandardError.ReadToEndAsync();
    var output = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    return (p.ExitCode, output + err.Result);
}

static void Require((int Exit, string Output) result, string step)
{
    if (result.Exit != 0)
        throw new SeedError($"{step} failed: {result.Output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0)}");
}

sealed record Oracle(string Class, string Project, string Kind, string Against, List<string>? Needs);
sealed record Claim(string Id, string Source, string Quote, [property: System.Text.Json.Serialization.JsonPropertyName("claim")] string Statement, string Oracle, List<string>? Surfaces, List<string>? HeldBy);
sealed record Candidate(string Oracle, string Row, string Family, string Issue);
sealed class SeedError(string message) : Exception(message);

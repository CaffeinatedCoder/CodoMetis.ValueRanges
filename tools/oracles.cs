#:property PublishAot=false
#:package Testcontainers.PostgreSql@4.16.0
#:package Npgsql@10.0.0
#:package Microsoft.CodeAnalysis.CSharp@5.9.0
// The oracle machinery's local tool. Never part of CI; the convention tests hold the catalogue.
//
//   dotnet run tools/oracles.cs seeds [name filter]   prove that every seed is caught
//   dotnet run tools/oracles.cs status                the catalogue's worklist
//   dotnet run tools/oracles.cs pg-surface [image]    regenerate test/oracles/postgres-surface.catalog.txt
//   dotnet run tools/oracles.cs clones [filter]       the clone-divergence sweep over the hand-copied families
//
// `clones`: every hand-copied type family under src/ (test/oracles/CloneSweep.cs, compiled in through
// tools/Directory.Build.props and linked into the conventions tests), each sibling normalized and compared
// with the family's consensus. One block per divergence — WAIVED with its reason from clone-waivers.json,
// or UNWAIVED with the JSON line to paste once understood — then the stale waivers, then the counts.
// Exit code 1 if anything is unwaived or stale, which is what CloneDivergenceTests fails on in CI. The
// filter matches the family or the unit name.
//
// `seeds`: for each test/oracles/seeds/<defect>.patch, a scratch copy of the working tree (bin/, obj/
// and .git/ left out, so uncommitted work is included) under the OS temp directory, the patch applied,
// and each oracle class the patch's `Oracles:` line names run in its project. A seed is caught by an
// oracle when at least one of its tests fails with a row that is not a known candidate (SeedVerdict); one line
// per (seed, oracle), and exit code 1 unless every pair is CAUGHT. Oracles that need Docker are run
// too — start Docker first, or pass a filter that leaves them out. An oracle test/oracles/compiled-out.json
// records as compiled out on this line is SKIPPED with the issue that compiles it back, not run.
//
// `pg-surface`: starts a PostgreSQL container (default image postgres:18-alpine), lists every pg_operator
// and pg_proc row over anyrange, anymultirange, anyarray and anycompatiblearray plus the constructors of
// the built-in range and multirange types (minus I/O, planner, operator and aggregate implementation
// functions, which the listing's header names), and writes it to test/oracles/postgres-surface.catalog.txt.
// OraclePostgresSurfaceTests then requires an entry in postgres-surface.json per line, so a new server
// major shows up as missing entries once the listing is refreshed. Never run in CI: the listing is checked in.

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodoMetis.ValueRanges.Oracles;

var root = FindRoot();
var oraclesDir = Path.Combine(root, "test", "oracles");
var command = args.Length > 0 ? args[0] : "status";
return command switch
{
    "seeds"      => Seeds(args.Length > 1 ? args[1] : ""),
    "status"     => Status(),
    "pg-surface" => PgSurface(args.Length > 1 ? args[1] : "postgres:18-alpine"),
    "clones"     => Clones(args.Length > 1 ? args[1] : ""),
    _            => Usage()
};

int Usage()
{
    Console.Error.WriteLine("usage: dotnet run tools/oracles.cs seeds [filter] | status | pg-surface [image] | clones [filter]");
    return 2;
}

int Clones(string filter)
{
    var families    = CodoMetis.ValueRanges.Oracles.CloneSweep.DiscoverFamilies(root);
    var waivers     = CodoMetis.ValueRanges.Oracles.CloneSweep.LoadWaivers(root);
    var divergences = families.SelectMany(CodoMetis.ValueRanges.Oracles.CloneSweep.Compare).ToList();
    var shown       = divergences.Where(d => d.Family.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                          || d.Unit.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

    foreach (var family in families)
    {
        var count = shown.Count(d => d.Family == family.Name);
        if (filter.Length > 0 && count == 0 && !family.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
        Console.WriteLine($"## {family.Name}: {family.Units.Count} siblings ({string.Join(", ", family.Units.Select(u => u.Name))}), {count} divergence(s)");
        foreach (var d in shown.Where(d => d.Family == family.Name))
        {
            var waiver = waivers.FirstOrDefault(w => w.Matches(d));
            Console.WriteLine($"  {(waiver is null ? "UNWAIVED" : "WAIVED  ")} {d.Unit} · {d.Member} · {d.Kind}");
            Console.WriteLine($"           {d.Detail}");
            Console.WriteLine(waiver is null ? $"           {d.WaiverJson}" : $"           reason: {waiver.Reason}");
        }
        Console.WriteLine();
    }

    var stale = waivers.Where(w => !divergences.Any(w.Matches)).ToList();
    foreach (var w in stale)
        Console.WriteLine($"STALE WAIVER {w.Key}: no divergence matches it any more — remove the entry");

    var unwaived = divergences.Count(d => !waivers.Any(w => w.Matches(d)));
    Console.WriteLine($"{divergences.Count} divergence(s) in {families.Count} families: {divergences.Count - unwaived} waived, {unwaived} unwaived, {stale.Count} stale waiver(s)");
    return unwaived == 0 && stale.Count == 0 ? 0 : 1;
}

int Seeds(string filter)
{
    var oracles = Json<List<Oracle>>("oracles.json").ToDictionary(o => o.Class, StringComparer.Ordinal);
    var compiledOut = Json<List<CompiledOut>>("compiled-out.json").ToDictionary(c => c.Class, StringComparer.Ordinal);
    var skipped = 0;
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
                                 .Where(o =>
                                 {
                                     if (!compiledOut.TryGetValue(o.Class, out var gap)) return true;
                                     skipped++;
                                     Console.WriteLine($"SKIPPED {name} → {o.Class}: compiled out of {gap.Project} on this line until #{gap.Issue}");
                                     return false;
                                 })
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

    Console.WriteLine(!allCaught ? "A SEED IS NOT CAUGHT — the oracle it names cannot fail for that defect"
                    : skipped > 0 ? $"every seed is caught, except {skipped} pair(s) skipped: their oracle is compiled out on this line"
                    : "every seed is caught");
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
    var caught = failed.Where(f => SeedVerdict.Catches(f.Item2)).ToList();
    var inconclusive = results.Count(r => (string?)r.Attribute("outcome") is "NotExecuted" or "Inconclusive");
    if (caught.Count > 0) return $"CAUGHT  {caught.Count} of {results.Count} tests fail, e.g. {caught[0].Item1}";
    if (inconclusive == results.Count) return $"ERROR   all {results.Count} tests inconclusive (no database?)";
    return failed.Count == 0
        ? $"MISSED  all {results.Count} tests pass with the defect planted"
        : $"MISSED  {failed.Count} of {results.Count} tests fail, every row a known candidate";
}

int Status()
{
    var claims = Json<List<Claim>>("claims.json");
    var oracles = Json<List<Oracle>>("oracles.json");
    var seeds = Directory.GetFiles(Path.Combine(oraclesDir, "seeds"), "*.patch")
                         .Select(s => (Name: Path.GetFileName(s), Oracles: Header(File.ReadAllText(s), "Oracles").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
                         .ToList();
    var candidates = Json<List<Candidate>>("candidates.json");

    // A class compiled out on this line holds nothing here; OracleCatalogueTests keeps the record true.
    var compiledOut = Json<List<CompiledOut>>("compiled-out.json").ToDictionary(c => c.Class, StringComparer.Ordinal);
    bool Held(Claim c) => c.HeldBy?.Any(h => !compiledOut.ContainsKey(h)) == true;

    var held = claims.Where(Held).ToList();
    Console.WriteLine($"claims: {claims.Count}, held by a compiled test: {held.Count}, unheld (the worklist): {claims.Count - held.Count}");
    foreach (var c in claims.Where(c => !Held(c)))
    {
        var gaps = (c.HeldBy ?? []).Where(compiledOut.ContainsKey).Select(h => $"{h} compiled out until #{compiledOut[h].Issue}").ToList();
        Console.WriteLine($"  UNHELD  {c.Id,-32} {c.Source}{(gaps.Count > 0 ? $"  ({string.Join("; ", gaps)})" : "")}");
    }

    var seeded = seeds.SelectMany(s => s.Oracles).ToHashSet(StringComparer.Ordinal);
    Console.WriteLine($"oracles: {oracles.Count}, seeds: {seeds.Count}, oracles without a seed: {oracles.Count(o => !seeded.Contains(o.Class))}");
    foreach (var o in oracles.Where(o => !seeded.Contains(o.Class)))
        Console.WriteLine($"  UNSEEDED {o.Class}");
    foreach (var o in oracles.Where(o => compiledOut.ContainsKey(o.Class)))
        Console.WriteLine($"  COMPILED OUT {o.Class} until #{compiledOut[o.Class].Issue}: its seeds are skipped on this line");

    Console.WriteLine($"candidates: {candidates.Count} ({candidates.Count(c => c.Issue.Length == 0)} without an issue)");
    foreach (var c in candidates)
        Console.WriteLine($"  {c.Oracle}: '{c.Row}' [{c.Family}] {(c.Issue.Length == 0 ? "NOT FILED" : c.Issue)}");

    var surface = Json<List<PgSurfaceEntry>>("postgres-surface.json");
    Console.WriteLine($"postgres surface: {surface.Count} entries, with a member: {surface.Count(e => e.Member is not null)}, waived: {surface.Count(e => e.Waiver is not null)}, open as issues (the worklist): {surface.Count(e => e.Member is null && e.Waiver is null)}");
    foreach (var e in surface.Where(e => e.Member is null && e.Waiver is null))
        Console.WriteLine($"  MISSING {e.Signature,-48} {(e.Issue is { } n ? $"#{n}" : "NOT FILED")}");

    Console.WriteLine("document headings with no claim (read them for promises):");
    foreach (var file in new[] { "README.md", "docs/ranges.md", "docs/value-sets.md", "docs/serialization.md", "docs/efcore.md", "docs/getting-started.md", "docs/walkthrough.md" })
    {
        var claimed = claims.Where(c => c.Source.StartsWith(file + "#", StringComparison.Ordinal)).Select(c => c.Source[(file.Length + 1)..]).ToHashSet(StringComparer.Ordinal);
        var silent = Headings(Path.Combine(root, file)).Where(h => !claimed.Contains(h)).ToList();
        if (silent.Count > 0) Console.WriteLine($"  {file}: {string.Join(", ", silent)}");
    }

    return 0;
}

int PgSurface(string image)
{
    const string PgSurfaceSql = """
        with poly as (select array['anyrange', 'anymultirange', 'anyarray', 'anycompatiblearray']::regtype[]::oid[] as t),
             concrete as (select array['int4range', 'int8range', 'numrange', 'daterange', 'tsrange', 'tstzrange',
                                       'int4multirange', 'int8multirange', 'nummultirange', 'datemultirange', 'tsmultirange', 'tstzmultirange']::regtype[]::oid[] as t),
             uncallable as (select array['internal', 'cstring']::regtype[]::oid[] as t),
             implementation as (
                 select oprcode as f from pg_operator
                 union select aggtransfn from pg_aggregate union select aggfinalfn from pg_aggregate
                 union select aggcombinefn from pg_aggregate union select aggserialfn from pg_aggregate
                 union select aggdeserialfn from pg_aggregate union select aggmtransfn from pg_aggregate
                 union select aggminvtransfn from pg_aggregate union select aggmfinalfn from pg_aggregate
                 union select typinput from pg_type union select typoutput from pg_type
                 union select typreceive from pg_type union select typsend from pg_type
                 union select typanalyze from pg_type union select typsubscript from pg_type
                 union select rngcanonical from pg_range union select rngsubdiff from pg_range)
        select 'operator' as kind,
               format('%s %s %s', oprleft::regtype, oprname, oprright::regtype) as signature,
               oprresult::regtype::text as result
          from pg_operator, poly
         where oprleft = any (poly.t) or oprright = any (poly.t)
        union all
        select case prokind when 'a' then 'aggregate' else 'function' end,
               proname || '(' || coalesce((select string_agg(case when p.provariadic <> 0 and a.n = p.pronargs then 'VARIADIC ' else '' end || format_type(a.t, null), ', ' order by a.n)
                                             from unnest(p.proargtypes::oid[]) with ordinality as a(t, n)), '') || ')',
               pg_get_function_result(p.oid)
          from pg_proc p, poly, concrete, uncallable
         where p.pronamespace = 'pg_catalog'::regnamespace
           and (p.proargtypes::oid[] && poly.t or p.prorettype = any (poly.t) or p.prorettype = any (concrete.t))
           and not (p.proargtypes::oid[] && uncallable.t or p.prorettype = any (uncallable.t))
           and p.oid not in (select f from implementation)
        """;

    var path = Path.Combine(oraclesDir, "postgres-surface.catalog.txt");
    Console.WriteLine($"starting {image} …");
    var container = new Testcontainers.PostgreSql.PostgreSqlBuilder(image).Build();
    container.StartAsync().GetAwaiter().GetResult();
    try
    {
        using var connection = new Npgsql.NpgsqlConnection(container.GetConnectionString());
        connection.Open();

        string version;
        using (var cmd = new Npgsql.NpgsqlCommand("show server_version", connection))
            version = ((string)cmd.ExecuteScalar()!).Split(' ')[0];

        var rows = new List<(string Kind, string Signature, string Result)>();
        using (var cmd = new Npgsql.NpgsqlCommand(PgSurfaceSql, connection))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));

        string[] kinds = ["operator", "function", "aggregate"];
        var lines = rows.OrderBy(r => Array.IndexOf(kinds, r.Kind))
                        .ThenBy(r => r.Signature, StringComparer.Ordinal)
                        .Select(r => $"{r.Kind,-10} {r.Signature} -> {r.Result}");
        string[] header =
        [
            $"# PostgreSQL {version}",
            "# Every pg_operator and pg_proc row of pg_catalog over anyrange, anymultirange, anyarray and",
            "# anycompatiblearray, plus the constructors of the six built-in range types and their multiranges.",
            "# Regenerated by `dotnet run tools/oracles.cs pg-surface [image]` (local only, never CI); every line",
            "# needs an entry in postgres-surface.json, which OraclePostgresSurfaceTests holds.",
            "# Left out: functions taking or returning internal or cstring (planner and index support), a type's",
            "# I/O functions, the function behind an operator (listed as the operator), an aggregate's transition",
            "# and final functions (listed as the aggregate), and a range type's canonical and subtype-difference",
            "# functions. Operator-class support functions that are plain SQL functions (hash_range, range_cmp,",
            "# btarraycmp, …) stay in and are waived by name.",
            ""
        ];
        File.WriteAllLines(path, header.Concat(lines));
        Console.WriteLine($"PostgreSQL {version}: {rows.Count} rows ({rows.Count(r => r.Kind == "operator")} operators, {rows.Count(r => r.Kind == "function")} functions, {rows.Count(r => r.Kind == "aggregate")} aggregates) → {Path.GetRelativePath(root, path)}");
        return 0;
    }
    finally
    {
        container.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
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
sealed record PgSurfaceEntry(string Signature, string Kind, double Since, string? Member, string? Translation, string? Waiver, int? Issue);
sealed record Claim(string Id, string Source, string Quote, [property: System.Text.Json.Serialization.JsonPropertyName("claim")] string Statement, string Oracle, List<string>? Surfaces, List<string>? HeldBy);
sealed record CompiledOut(string Class, string Project, int Issue, string Reason);
sealed record Candidate(string Oracle, string Row, string Family, string Issue);
sealed class SeedError(string message) : Exception(message);


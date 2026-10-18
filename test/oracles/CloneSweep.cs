using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.ValueRanges.Oracles;

/// <summary>
/// The clone-divergence sweep: every hand-copied type family in <c>src/</c>, each member normalized
/// and compared with its siblings, so that the one sibling that lacks a line the others have — or
/// carries a line nobody else does — is named without anyone having to read ten files side by side.
/// </summary>
/// <remarks>
/// <para>
/// Bugs at one API site recur at its siblings: each NodaTime set file has been touched by five or six
/// fix commits, wrapper arities repeatedly lag their primitive set, and the 9.1.0 whitespace fix landed
/// as two commits because the second site was found only after the first was fixed. The claim-driven
/// oracles hold a documented promise per type; none of them can notice that one sibling is missing a
/// member. A normalized diff across a family can, and it is cheap.
/// </para>
/// <para>
/// <b>Families</b> are discovered, never listed: the value set types under each <c>Sets/</c> folder
/// (primitives and wrapper arities apart, core and NodaTime apart), the range unions in each library's
/// root, the EF-side <c>RangeTypeDefinition</c>/<c>SetTypeDefinition</c> registrations and the
/// <c>Bridged</c> wrapper-family entries, and every primitive set paired with its own arity.
/// </para>
/// <para>
/// <b>Normalization</b> replaces what legitimately differs between siblings with placeholders. The
/// type's own name becomes <c>$TYPE</c>, its element type <c>$T</c>, and in a wrapper arity the backing
/// primitive <c>$P</c>. The type's stem (<c>Date</c> of <c>DateSet</c>, and its camel-case suffixes) inside
/// another identifier becomes <c>$STEM</c> — but only for an identifier shape the family actually
/// shares, such as <c>ParseDate</c>/<c>ParseTime</c> or <c>LocalDatePattern</c>/<c>InstantPattern</c>; a
/// shape with one realization, or one some sibling spells with a stem that is not its own
/// (<c>ToString</c> in <c>StringSet</c>, <c>TimeSpan</c> in <c>TimeRange</c>), is left alone. Where the element
/// type has a keyword alias (<c>int</c>, <c>string</c>) that a sibling also uses for its own sake, each
/// occurrence is read both ways and the reading that agrees with the most siblings is compared, so
/// <c>int Count</c> is not a divergence of <c>Int32Set</c>. Comments, documentation and preprocessor lines
/// are stripped and the formatting is Roslyn's canonical one. String literals stay: a format constant
/// that differs is a difference worth a waiver.
/// </para>
/// <para>
/// <b>Three rules</b>, per family of <c>N</c> units and per member key. A member present in at least
/// <c>max(3, N − 2)</c> siblings is <i>missing</i> from the rest. A member only one sibling has is
/// <i>extra</i>. A body no sibling shares is a <i>lone body</i> when a body two or more siblings share
/// is near it — at least half similar by token sequence, or behind an identical non-empty signature — which is the
/// near miss a copy that drifted leaves. A member whose body and signature are both bespoke (a <c>Length</c>
/// typed per range) is near nothing and stays silent, which keeps the waiver list short.
/// </para>
/// <para>
/// Every divergence is either waived in <c>test/oracles/clone-waivers.json</c> with a reason or fails
/// <c>CloneDivergenceTests</c>; <c>dotnet run tools/oracles.cs clones</c> prints them all. This file is
/// linked into the conventions project and compiled into the tool (through <c>tools/Directory.Build.props</c>),
/// so there is one engine and the two cannot disagree.
/// </para>
/// </remarks>
internal static class CloneSweep
{
    /// <summary>A member present in all but this many siblings is consensus; the rest are missing it.</summary>
    public const int ConsensusSlack = 2;

    /// <summary>Fewer siblings than this agree on nothing; a family this small yields no divergences.</summary>
    public const int MinimumConsensus = 3;

    /// <summary>The body token-sequence similarity from which a lone body counts as a drifted copy of a shared one.</summary>
    public const double NearMissSimilarity = 0.5;

    /// <summary>Beyond this many ambiguous alias occurrences in one member, only the all-or-nothing readings are tried.</summary>
    private const int MaxAliasVariants = 6;

    public const string MissingKind  = "missing";
    public const string ExtraKind    = "extra";
    public const string LoneBodyKind = "lone body";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true
    };

    /// <summary>Every divergence of every discovered family, in family order.</summary>
    public static IReadOnlyList<CloneDivergence> Run(string repositoryRoot)
        => DiscoverFamilies(repositoryRoot).SelectMany(Compare).ToList();

    /// <summary>The waivers in <c>test/oracles/clone-waivers.json</c>.</summary>
    public static IReadOnlyList<CloneWaiver> LoadWaivers(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "test", "oracles", "clone-waivers.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<CloneWaiver>>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"'{path}' is empty.");
    }

    // ----------------------------------------------------------------------------------------------
    // Discovery
    // ----------------------------------------------------------------------------------------------

    /// <summary>The hand-copied families under <c>src/</c>, each with its normalized units.</summary>
    public static IReadOnlyList<CloneFamily> DiscoverFamilies(string repositoryRoot)
    {
        var src  = Path.Combine(repositoryRoot, "src");
        var core = Path.Combine(src, "CodoMetis.ValueRanges");
        var noda = Path.Combine(src, "CodoMetis.ValueRanges.NodaTime");

        var coreSets   = TypeUnits(Path.Combine(core, "Sets"), "IValueSet");
        var nodaSets   = TypeUnits(Path.Combine(noda, "Sets"), "IValueSet");
        var coreRanges = TypeUnits(core, "IRange");
        var nodaRanges = TypeUnits(noda, "IRange");

        // Every primitive set's element type, by type name: what an EF registration that infers its
        // type argument is about.
        var elementOf = coreSets.Concat(nodaSets)
                                .Where(unit => !unit.IsGeneric)
                                .ToDictionary(unit => unit.TypeName, unit => unit.Element, StringComparer.Ordinal);

        var efProjects    = new DirectoryInfo(src).EnumerateDirectories("*EFCore*").OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        var registrations = RegistrationUnits(efProjects, elementOf);

        var coreSetFamilies = (Primitives: Materialize(coreSets.Where(u => !u.IsGeneric)), Arities: Materialize(coreSets.Where(u => u.IsGeneric)));
        var nodaSetFamilies = (Primitives: Materialize(nodaSets.Where(u => !u.IsGeneric)), Arities: Materialize(nodaSets.Where(u => u.IsGeneric)));

        return
        [
            new CloneFamily("core value sets",          coreSetFamilies.Primitives),
            new CloneFamily("core wrapper arities",     coreSetFamilies.Arities),
            new CloneFamily("nodatime value sets",      nodaSetFamilies.Primitives),
            new CloneFamily("nodatime wrapper arities", nodaSetFamilies.Arities),
            new CloneFamily("core ranges",              Materialize(coreRanges)),
            new CloneFamily("nodatime ranges",          Materialize(nodaRanges)),
            new CloneFamily("efcore range definitions", Materialize(registrations.Where(r => r.Family == "RangeTypeDefinition").Select(r => r.Unit))),
            new CloneFamily("efcore set definitions",   Materialize(registrations.Where(r => r.Family == "SetTypeDefinition").Select(r => r.Unit))),
            new CloneFamily("efcore wrapper bridges",   Materialize(registrations.Where(r => r.Family == "Bridged").Select(r => r.Unit))),
            new CloneFamily("primitive vs arity",       PairUnits([.. coreSetFamilies.Primitives, .. nodaSetFamilies.Primitives], [.. coreSetFamilies.Arities, .. nodaSetFamilies.Arities]))
        ];
    }

    /// <summary>A sibling before normalization: its syntax, keyed, and the names its placeholders stand for.</summary>
    private sealed record RawUnit(string Name, string Source, string TypeName, bool IsGeneric, string Element, Placeholders Names, IReadOnlyList<RawMember> Members);

    /// <summary>A member before normalization: its key as syntax text, the node, and the part of it that is the body.</summary>
    private sealed record RawMember(string RawKey, SyntaxNode Node, SyntaxNode? Body);

    private sealed record Registration(string Family, RawUnit Unit);

    /// <summary>
    /// The top-level type declarations in <paramref name="directory"/> whose base list names
    /// <paramref name="markerInterface"/>, one unit each, with a wrapper arity's backing primitive taken
    /// from the non-generic sibling declared in the same file.
    /// </summary>
    private static List<RawUnit> TypeUnits(string directory, string markerInterface)
    {
        var units = new List<RawUnit>();
        if (!Directory.Exists(directory)) return units;

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            var root  = Stripped(file);
            var types = root.DescendantNodes()
                            .OfType<TypeDeclarationSyntax>()
                            .Where(t => t.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
                            .Select(t => (Type: t, Element: ElementOf(t, markerInterface)))
                            .Where(t => t.Element is not null)
                            .ToList();
            if (types.Count == 0) continue;

            var primitive = types.FirstOrDefault(t => t.Type.TypeParameterList is null);

            foreach (var (type, element) in types)
            {
                var typeName  = type.Identifier.Text;
                var isGeneric = type.TypeParameterList is not null;
                var names     = new Placeholders(typeName, element!, backing: isGeneric ? primitive.Element : null);
                var members   = new List<RawMember>();
                AddTypeMembers(type, prefix: "", members);

                var display = isGeneric ? $"{typeName}<{string.Join(", ", type.TypeParameterList!.Parameters.Select(p => p.Identifier.Text))}>" : typeName;
                units.Add(new RawUnit(display, Relative(file), typeName, isGeneric, element!, names, members));
            }
        }

        return units;
    }

    private static string? ElementOf(TypeDeclarationSyntax type, string markerInterface)
        => type.BaseList?.Types
               .Select(b => b.Type)
               .OfType<GenericNameSyntax>()
               .FirstOrDefault(g => g.Identifier.Text == markerInterface && g.TypeArgumentList.Arguments.Count == 1)
              ?.TypeArgumentList.Arguments[0].ToString();

    /// <summary>
    /// One raw member per declaration: the type's own header (attributes, modifiers, base list,
    /// constraints) under <c>&lt;type&gt;</c>, each method, property, field, operator and constructor
    /// under its signature, and each nested type under its name with its members prefixed by it.
    /// </summary>
    private static void AddTypeMembers(TypeDeclarationSyntax type, string prefix, List<RawMember> members)
    {
        var header = type.WithMembers(default).WithOpenBraceToken(default).WithCloseBraceToken(default);
        members.Add(new RawMember(prefix + "<type>", header, Body: null));

        foreach (var member in type.Members)
        {
            if (member is TypeDeclarationSyntax nested)
            {
                AddTypeMembers(nested, $"{prefix}{nested.Identifier.Text}{nested.TypeParameterList}.", members);
                continue;
            }

            members.Add(new RawMember(prefix + KeyOf(member), member, BodyOf(member)));
        }
    }

    private static string KeyOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m             => $"{m.ExplicitInterfaceSpecifier}{m.Identifier.Text}{m.TypeParameterList}({Types(m.ParameterList)})",
        PropertyDeclarationSyntax p           => $"{p.ExplicitInterfaceSpecifier}{p.Identifier.Text}",
        IndexerDeclarationSyntax i            => $"{i.ExplicitInterfaceSpecifier}this[{Types(i.ParameterList)}]",
        EventDeclarationSyntax e              => $"{e.ExplicitInterfaceSpecifier}{e.Identifier.Text}",
        FieldDeclarationSyntax f              => string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.Text)),
        EventFieldDeclarationSyntax f         => string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.Text)),
        OperatorDeclarationSyntax o           => $"operator {o.OperatorToken.Text}({Types(o.ParameterList)})",
        ConversionOperatorDeclarationSyntax c => $"{c.ImplicitOrExplicitKeyword.Text} operator {c.Type}({Types(c.ParameterList)})",
        ConstructorDeclarationSyntax c        => c.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : $".ctor({Types(c.ParameterList)})",
        DestructorDeclarationSyntax           => "~",
        _                                     => member.Kind().ToString()
    };

    /// <summary>The part of a member that is its body: what the similarity rule compares, apart from the signature.</summary>
    private static SyntaxNode? BodyOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m             => (SyntaxNode?)m.ExpressionBody ?? m.Body,
        PropertyDeclarationSyntax p           => (SyntaxNode?)p.ExpressionBody ?? p.Initializer ?? (SyntaxNode?)p.AccessorList,
        IndexerDeclarationSyntax i            => (SyntaxNode?)i.ExpressionBody ?? i.AccessorList,
        FieldDeclarationSyntax f              => f.Declaration.Variables.Select(v => v.Initializer?.Value).FirstOrDefault(v => v is not null),
        OperatorDeclarationSyntax o           => (SyntaxNode?)o.ExpressionBody ?? o.Body,
        ConversionOperatorDeclarationSyntax c => (SyntaxNode?)c.ExpressionBody ?? c.Body,
        ConstructorDeclarationSyntax c        => (SyntaxNode?)c.ExpressionBody ?? c.Body,
        _                                     => null
    };

    private static string Types(BaseParameterListSyntax parameters)
        => string.Join(", ", parameters.Parameters.Select(p => p.Type?.ToString() ?? "?"));

    /// <summary>
    /// The EF registrations in the EF projects: every <c>new RangeTypeDefinition&lt;R, T&gt;(…)</c> and
    /// <c>new SetTypeDefinition&lt;S, T&gt;(…)</c>, and every <c>Bridged&lt;P&gt;(…)</c> wrapper-family entry,
    /// as a unit whose members are the constructor's parameters by name, positional arguments resolved
    /// against the parameter list where it is declared.
    /// </summary>
    private static List<Registration> RegistrationUnits(IReadOnlyList<DirectoryInfo> projects, IReadOnlyDictionary<string, string> elementOf)
    {
        var roots = projects.SelectMany(p => p.EnumerateFiles("*.cs", SearchOption.AllDirectories))
                            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                            .OrderBy(f => f.FullName, StringComparer.Ordinal)
                            .ToDictionary(f => f.FullName, f => Stripped(f.FullName), StringComparer.Ordinal);

        var parameters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var root in roots.Values)
        {
            foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (type.Identifier.Text is "RangeTypeDefinition" or "SetTypeDefinition"
                 && type.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault() is { } ctor)
                    parameters[type.Identifier.Text] = ctor.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList();
            }

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.Identifier.Text == "Bridged"))
                parameters["Bridged"] = method.ParameterList.Parameters.Select(p => p.Identifier.Text).ToList();
        }

        var units = new List<Registration>();
        foreach (var (file, root) in roots)
        {
            foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (creation.Type is not GenericNameSyntax { Identifier.Text: "RangeTypeDefinition" or "SetTypeDefinition" } generic
                 || generic.TypeArgumentList.Arguments.Count != 2
                 || creation.ArgumentList is null)
                    continue;

                var family   = generic.Identifier.Text;
                var typeName = generic.TypeArgumentList.Arguments[0].ToString();
                var element  = generic.TypeArgumentList.Arguments[1].ToString();
                units.Add(new Registration(family, ArgumentUnit(typeName, typeName, element, creation.ArgumentList, parameters.GetValueOrDefault(family) ?? [], file)));
            }

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax access => access.Name,
                    SimpleNameSyntax simple             => simple,
                    _                                   => null
                };
                if (name is null || name.Identifier.Text != "Bridged") continue;

                var typeName = FamilyTypeOf(invocation);
                if (typeName is null) continue;

                var element = name is GenericNameSyntax g && g.TypeArgumentList.Arguments.Count == 1
                                  ? g.TypeArgumentList.Arguments[0].ToString()
                                  : elementOf.GetValueOrDefault(typeName, "?");
                units.Add(new Registration("Bridged", ArgumentUnit($"{typeName}<>", typeName, element, invocation.ArgumentList, parameters.GetValueOrDefault("Bridged") ?? [], file)));
            }
        }

        return units;
    }

    /// <summary>The <c>X</c> of the <c>typeof(X&lt;&gt;)</c> that keys a wrapper-family entry, in a dictionary initializer or a tuple.</summary>
    private static string? FamilyTypeOf(InvocationExpressionSyntax invocation)
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            var typeOf = ancestor switch
            {
                AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax access } => access.DescendantNodes().OfType<TypeOfExpressionSyntax>().FirstOrDefault(),
                TupleExpressionSyntax tuple => tuple.Arguments[0].DescendantNodesAndSelf().OfType<TypeOfExpressionSyntax>().FirstOrDefault(),
                _ => null
            };
            if (typeOf?.Type is GenericNameSyntax family) return family.Identifier.Text;
            if (ancestor is StatementSyntax or MemberDeclarationSyntax) break;
        }

        return null;
    }

    private static RawUnit ArgumentUnit(string display, string typeName, string element, ArgumentListSyntax arguments, IReadOnlyList<string> order, string file)
    {
        var members = new List<RawMember>();
        for (var i = 0; i < arguments.Arguments.Count; i++)
        {
            var argument = arguments.Arguments[i];
            var key      = argument.NameColon?.Name.Identifier.Text ?? (i < order.Count ? order[i] : $"#{i}");
            members.Add(new RawMember(key, argument.Expression, argument.Expression));
        }

        return new RawUnit(display, Relative(file), typeName, IsGeneric: false, element, new Placeholders(typeName, element, backing: null), members);
    }

    /// <summary>
    /// Each primitive set beside its own arity: the members only one of the two has. Across the pairs,
    /// what every pair differs in is structure; what one or two pairs differ in is a sibling lagging.
    /// </summary>
    private static List<CloneUnit> PairUnits(IReadOnlyList<CloneUnit> primitives, IReadOnlyList<CloneUnit> arities)
    {
        var pairs = new List<CloneUnit>();
        foreach (var primitive in primitives)
        {
            var arity = arities.FirstOrDefault(a => a.Source == primitive.Source);
            if (arity is null) continue;

            var members = new Dictionary<string, CloneMember>(StringComparer.Ordinal);
            foreach (var key in primitive.Members.Keys.Where(k => !arity.Members.ContainsKey(k)))
                members[$"primitive only: {key}"] = new CloneMember($"primitive only: {key}", [], [], []);
            foreach (var key in arity.Members.Keys.Where(k => !primitive.Members.ContainsKey(k)))
                members[$"arity only: {key}"] = new CloneMember($"arity only: {key}", [], [], []);

            pairs.Add(new CloneUnit($"{primitive.Name} / {arity.Name}", primitive.Source, members));
        }

        return pairs;
    }

    // ----------------------------------------------------------------------------------------------
    // Normalization
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Renders a group of raw siblings into comparable units: decides which stem shapes the group
    /// shares, then for each member picks the alias reading that agrees with the most siblings.
    /// </summary>
    private static List<CloneUnit> Materialize(IEnumerable<RawUnit> rawUnits)
    {
        var raw = rawUnits.ToList();
        if (raw.Count == 0) return [];

        // Pass 1: the words of every unit after the exact replacements, and the stem shapes each word realizes.
        var words        = raw.ToDictionary(u => u, u => new HashSet<string>(StringComparer.Ordinal));
        var realizations = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var unit in raw)
        {
            var text = string.Join("\n", unit.Members.Select(m => unit.Names.ApplyExact(Canonical(m.Node))));
            foreach (Match match in Placeholders.Word.Matches(text))
            {
                words[unit].Add(match.Value);
                if (unit.Names.Shape(match.Value) is { } shape)
                    (realizations.TryGetValue(shape.Shape, out var set) ? set : realizations[shape.Shape] = new(StringComparer.Ordinal)).Add(match.Value);
            }
        }

        // A shape is accepted when the group realizes it with at least two different words and no unit
        // spells it with a stem that is not its own (`TimeSpan` in a range over DateTimeOffset).
        var allStems = raw.SelectMany(u => u.Names.Stems).Distinct(StringComparer.Ordinal).ToList();
        var accepted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (shape, realized) in realizations)
        {
            if (realized.Count < 2) continue;
            var conflict = raw.Any(unit => allStems.Where(stem => !unit.Names.Stems.Contains(stem))
                                                   .Any(stem => words[unit].Contains(shape == "$T" ? stem : shape.Replace(Placeholders.StemToken, stem, StringComparison.Ordinal))));
            if (!conflict) accepted.Add(shape);
        }

        foreach (var unit in raw) unit.Names.Accept(accepted);

        // Pass 2: every member in every alias reading, primary (all aliases replaced) first.
        var rendered = raw.ToDictionary(u => u, u => u.Members.Select(m => Render(u.Names, m)).ToList());

        // Pass 3: the reading that agrees with the most other units, for the key and then for the text.
        var keyCounts  = Counts(rendered.Values.SelectMany(ms => ms.Select(m => m.Keys[0])));
        var textCounts = Counts(rendered.Values.SelectMany(ms => ms.Select(m => m.Texts[0].Text)));

        var units = new List<CloneUnit>();
        foreach (var unit in raw)
        {
            var members = new Dictionary<string, CloneMember>(StringComparer.Ordinal);
            foreach (var member in rendered[unit])
            {
                var key  = member.Keys.Select((k, i) => (Key: k, Others: keyCounts.GetValueOrDefault(k) - (i == 0 ? 1 : 0), Index: i))
                                 .OrderByDescending(c => c.Others).ThenBy(c => c.Index).First().Key;
                var text = member.Texts.Select((t, i) => (Text: t, Others: textCounts.GetValueOrDefault(t.Text) - (i == 0 ? 1 : 0), Index: i))
                                 .OrderByDescending(c => c.Others).ThenBy(c => c.Index).First().Text;

                var unique = key;
                for (var n = 2; members.ContainsKey(unique); n++) unique = $"{key} #{n}";
                members[unique] = new CloneMember(unique, text.Lines, text.Header, text.Body);
            }

            units.Add(new CloneUnit(unit.Name, unit.Source, members));
        }

        return units;
    }

    private static Dictionary<string, int> Counts(IEnumerable<string> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in values) counts[value] = counts.GetValueOrDefault(value) + 1;
        return counts;
    }

    private sealed record Rendering(string Text, IReadOnlyList<string> Lines, IReadOnlyList<string> Header, IReadOnlyList<string> Body);

    private sealed record RenderedMember(IReadOnlyList<string> Keys, IReadOnlyList<Rendering> Texts);

    private static RenderedMember Render(Placeholders names, RawMember member)
    {
        var keys  = Placeholders.Variants(names.Apply(member.RawKey), MaxAliasVariants).Select(Placeholders.Collapse).ToList();
        var whole = names.Apply(Canonical(member.Node));
        var body  = member.Body is null ? "" : names.Apply(Canonical(member.Body));
        var bodyTokenCount = Tokens(Placeholders.Fill(body, _ => true)).Count;

        var texts = Placeholders.Variants(whole, MaxAliasVariants).Select(text =>
        {
            var lines  = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var tokens = Tokens(text);
            var split  = Math.Max(0, tokens.Count - bodyTokenCount);
            return new Rendering(string.Join("\n", lines), lines, tokens[..split], tokens[split..]);
        }).ToList();

        return new RenderedMember(keys, texts);
    }

    private static string Canonical(SyntaxNode node) => node.NormalizeWhitespace().ToFullString();

    /// <summary>Identifiers, keywords, numbers, placeholders and string literals — punctuation carries no meaning worth matching.</summary>
    private static List<string> Tokens(string text)
        => TokenPattern.Matches(text).Select(m => m.Value).ToList();

    private static readonly Regex TokenPattern = new(@"\$?\w+|""(?:[^""\\]|\\.)*""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static SyntaxNode Stripped(string file)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.Preview), path: file);
        return new TriviaStripper().Visit(tree.GetRoot());
    }

    private static string Relative(string file)
    {
        var marker = $"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}";
        var at     = file.IndexOf(marker, StringComparison.Ordinal);
        return (at < 0 ? file : file[(at + 1)..]).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Drops every comment, documentation comment and preprocessor directive, keeping the code.</summary>
    private sealed class TriviaStripper : CSharpSyntaxRewriter
    {
        public TriviaStripper() : base(visitIntoStructuredTrivia: false) { }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
            => trivia.IsDirective
            || trivia.Kind() is SyntaxKind.SingleLineCommentTrivia
                             or SyntaxKind.MultiLineCommentTrivia
                             or SyntaxKind.SingleLineDocumentationCommentTrivia
                             or SyntaxKind.MultiLineDocumentationCommentTrivia
                   ? default
                   : trivia;
    }

    /// <summary>
    /// What a sibling may legitimately differ in, replaced by placeholders: the type's name
    /// (<c>$TYPE</c>), its element type (<c>$T</c>), the primitive a wrapper arity wraps (<c>$P</c>), and the
    /// type's stem inside an identifier whose shape the family shares (<c>$STEM</c>). A keyword alias of the
    /// element or the primitive (<c>int</c>, <c>string</c>) is ambiguous — a sibling may use it for its own
    /// sake — so it is marked rather than replaced, and <see cref="Variants"/> renders every reading.
    /// </summary>
    private sealed class Placeholders
    {
        public const string StemToken = "$STEM";

        private const char Mark = '\u0001';

        private static readonly IReadOnlyDictionary<string, string> KeywordOf = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Int16"] = "short", ["Int32"] = "int", ["Int64"] = "long", ["Decimal"] = "decimal", ["String"] = "string",
            ["Boolean"] = "bool", ["Double"] = "double", ["Single"] = "float", ["Byte"] = "byte", ["Char"] = "char"
        };

        public static readonly Regex Word = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly Dictionary<string, string> _exact     = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _ambiguous = new(StringComparer.Ordinal);
        private HashSet<string> _accepted = new(StringComparer.Ordinal);

        public Placeholders(string typeName, string element, string? backing)
        {
            _exact[typeName] = "$TYPE";
            Map(element, "$T");
            if (backing is not null) Map(backing, "$P");

            var stem = typeName.EndsWith("Range", StringComparison.Ordinal) ? typeName[..^5]
                     : typeName.EndsWith("Set",   StringComparison.Ordinal) ? typeName[..^3]
                     : typeName;
            Stems = CamelSuffixes(stem).Where(s => s.Length >= 3).OrderByDescending(s => s.Length).ToList();
        }

        /// <summary>The stem and its camel-case suffixes, longest first: <c>LocalDateTime</c>, <c>DateTime</c>, <c>Time</c>.</summary>
        public IReadOnlyList<string> Stems { get; }

        private void Map(string type, string placeholder)
        {
            var bare = type.TrimEnd('?');
            _exact.TryAdd(bare, placeholder);
            if (KeywordOf.TryGetValue(bare, out var keyword)) _ambiguous.TryAdd(keyword, placeholder);
            foreach (var (name, kw) in KeywordOf)
                if (kw == bare)
                {
                    _exact.TryAdd(name, placeholder);
                    _ambiguous.TryAdd(kw, placeholder);
                    _exact.Remove(kw);
                }
        }

        private static IEnumerable<string> CamelSuffixes(string stem)
        {
            yield return stem;
            for (var i = 1; i < stem.Length; i++)
                if (char.IsUpper(stem[i]) && !char.IsUpper(stem[i - 1])) yield return stem[i..];
        }

        public void Accept(HashSet<string> shapes) => _accepted = shapes;

        /// <summary>The stem shape of a word, if it contains one of the stems: <c>ParseDate</c> → <c>Parse$STEM</c>; a whole-word stem reads as the element.</summary>
        public (string Shape, string Stem)? Shape(string word)
        {
            if (_exact.ContainsKey(word) || _ambiguous.ContainsKey(word) || word.StartsWith('$')) return null;
            foreach (var stem in Stems)
            {
                if (word == stem) return ("$T", stem);
                var at = word.IndexOf(stem, StringComparison.Ordinal);
                if (at >= 0) return (string.Concat(word.AsSpan(0, at), StemToken, word.AsSpan(at + stem.Length)), stem);
            }

            return null;
        }

        /// <summary>The exact replacements only — the type name and the unambiguous element and primitive names.</summary>
        public string ApplyExact(string text)
            => Word.Replace(text, match => _exact.GetValueOrDefault(match.Value, match.Value));

        /// <summary>Exact replacements, accepted stem shapes, and the ambiguous aliases marked for <see cref="Variants"/>.</summary>
        public string Apply(string text)
            => Word.Replace(text, match =>
            {
                var word = match.Value;
                if (_exact.TryGetValue(word, out var placeholder)) return placeholder;
                if (_ambiguous.TryGetValue(word, out placeholder)) return $"{Mark}{placeholder}|{word}{Mark}";
                if (Shape(word) is { } shape && (_accepted.Contains(shape.Shape) || shape.Shape == "$T" && _accepted.Contains("$T"))) return shape.Shape;
                return word;
            });

        /// <summary>Every reading of the marked text: the first replaces every alias, the last none, with the mixed readings between.</summary>
        public static IReadOnlyList<string> Variants(string marked, int maxOccurrences)
        {
            var count = marked.Count(c => c == Mark) / 2;
            if (count == 0) return [marked];
            if (count > maxOccurrences) return [Fill(marked, _ => true), Fill(marked, _ => false)];

            var variants = new List<string>();
            for (var bits = (1 << count) - 1; bits >= 0; bits--)
            {
                var chosen = bits;
                variants.Add(Fill(marked, index => (chosen >> (count - 1 - index) & 1) == 1));
            }

            return variants;
        }

        /// <summary>Resolves each marked alias: the placeholder when <paramref name="replace"/> says so for its index, the alias otherwise.</summary>
        public static string Fill(string marked, Func<int, bool> replace)
        {
            if (!marked.Contains(Mark)) return marked;
            var result = new System.Text.StringBuilder(marked.Length);
            var index  = 0;
            var at     = 0;
            while (true)
            {
                var open = marked.IndexOf(Mark, at);
                if (open < 0) { result.Append(marked, at, marked.Length - at); break; }
                var close = marked.IndexOf(Mark, open + 1);
                var pipe  = marked.IndexOf('|', open);
                result.Append(marked, at, open - at)
                      .Append(replace(index++) ? marked[(open + 1)..pipe] : marked[(pipe + 1)..close]);
                at = close + 1;
            }

            return result.ToString();
        }

        public static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();
    }

    // ----------------------------------------------------------------------------------------------
    // Comparison
    // ----------------------------------------------------------------------------------------------

    /// <summary>The divergences of one family under the three rules.</summary>
    public static IReadOnlyList<CloneDivergence> Compare(CloneFamily family)
    {
        var units = family.Units;
        var n     = units.Count;
        var found = new List<CloneDivergence>();
        if (n < MinimumConsensus) return found;

        var consensus = Math.Max(MinimumConsensus, n - ConsensusSlack);
        var keys      = units.SelectMany(u => u.Members.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var having = units.Where(u => u.Members.ContainsKey(key)).ToList();

            if (having.Count >= consensus && having.Count < n)
            {
                var sample = having[0].Members[key].Lines.FirstOrDefault() ?? "";
                foreach (var unit in units.Where(u => !u.Members.ContainsKey(key)))
                    found.Add(new CloneDivergence(family.Name, unit.Name, key, MissingKind,
                                                  $"present in {having.Count} of {n} ({Names(having)}){(sample.Length > 0 ? $": {sample}" : "")}"));
            }

            if (having.Count == 1)
            {
                var unit = having[0];
                found.Add(new CloneDivergence(family.Name, unit.Name, key, ExtraKind,
                                              $"no sibling has it{(unit.Members[key].Lines.Count > 0 ? ": " + string.Join(" ⏎ ", unit.Members[key].Lines.Take(3)) : "")}"));
            }

            var byBody   = having.GroupBy(u => u.Members[key].Text, StringComparer.Ordinal).ToList();
            var clusters = byBody.Where(g => g.Count() >= 2).ToList();
            if (clusters.Count == 0) continue;

            foreach (var lone in byBody.Where(g => g.Count() == 1).Select(g => g.Single()))
            {
                var mine = lone.Members[key];
                var best = clusters.Select(c => (Cluster: c, Theirs: c.First().Members[key]))
                                   .Select(c => (c.Cluster, c.Theirs, Similarity: Similarity(mine.Body, c.Theirs.Body), SameHeader: mine.Header.Count > 0 && mine.Header.SequenceEqual(c.Theirs.Header)))
                                   .OrderByDescending(c => c.SameHeader).ThenByDescending(c => c.Similarity)
                                   .First();
                if (!best.SameHeader && best.Similarity < NearMissSimilarity) continue;

                found.Add(new CloneDivergence(family.Name, lone.Name, key, LoneBodyKind,
                                              Diff(mine.Lines, best.Theirs.Lines) + $" — as in {Names(best.Cluster.ToList())}"));
            }
        }

        return found;
    }

    private static string Names(IReadOnlyList<CloneUnit> units)
        => string.Join(", ", units.Select(u => u.Name));

    /// <summary>2·LCS / (|a| + |b|) over token sequences: 1 for identical, 0 for nothing in common.</summary>
    public static double Similarity(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1;
        return 2.0 * Lcs(a, b) / (a.Count + b.Count);
    }

    private static int Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var previous = new int[b.Count + 1];
        var current  = new int[b.Count + 1];
        for (var i = 1; i <= a.Count; i++)
        {
            for (var j = 1; j <= b.Count; j++)
                current[j] = a[i - 1] == b[j - 1] ? previous[j - 1] + 1 : Math.Max(previous[j], current[j - 1]);
            (previous, current) = (current, previous);
            Array.Clear(current);
        }

        return previous[b.Count];
    }

    /// <summary>The lines only one side has: <c>this:</c> for the lone body, <c>siblings:</c> for the shared one.</summary>
    private static string Diff(IReadOnlyList<string> mine, IReadOnlyList<string> theirs)
    {
        var common     = new HashSet<string>(mine.Intersect(theirs, StringComparer.Ordinal), StringComparer.Ordinal);
        var onlyMine   = mine.Where(l => !common.Contains(l)).ToList();
        var onlyTheirs = theirs.Where(l => !common.Contains(l)).ToList();
        return $"this: {Join(onlyMine)} | siblings: {Join(onlyTheirs)}";

        static string Join(List<string> lines)
            => lines.Count == 0 ? "(nothing)" : string.Join(" ⏎ ", lines.Take(4)) + (lines.Count > 4 ? $" ⏎ … {lines.Count - 4} more" : "");
    }
}

/// <summary>A hand-copied family: the sibling units compared with one another.</summary>
internal sealed record CloneFamily(string Name, IReadOnlyList<CloneUnit> Units);

/// <summary>One sibling — a type, a registration, or a primitive/arity pair — as its normalized members by key.</summary>
internal sealed record CloneUnit(string Name, string Source, IReadOnlyDictionary<string, CloneMember> Members);

/// <summary>A member's normalized text as lines, and its tokens split into signature and body for the near-miss rule.</summary>
internal sealed record CloneMember(string Key, IReadOnlyList<string> Lines, IReadOnlyList<string> Header, IReadOnlyList<string> Body)
{
    public string Text => string.Join("\n", Lines);
}

/// <summary>One finding: a unit of a family, the member it concerns, which rule fired and the evidence.</summary>
internal sealed record CloneDivergence(string Family, string Unit, string Member, string Kind, string Detail)
{
    /// <summary>The identity a waiver matches: everything but the evidence.</summary>
    public string Key => $"{Family} · {Unit} · {Member} · {Kind}";

    /// <summary>The waiver entry to paste, with the reason left to write.</summary>
    public string WaiverJson
        => $$$"""{ "family": {{{Quote(Family)}}}, "unit": {{{Quote(Unit)}}}, "member": {{{Quote(Member)}}}, "kind": {{{Quote(Kind)}}}, "reason": "" }""";

    private static string Quote(string s)
        => JsonSerializer.Serialize(s, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}

/// <summary>A divergence that is understood: its identity and the reason it needs no change.</summary>
internal sealed record CloneWaiver(string Family, string Unit, string Member, string Kind, string Reason)
{
    public bool Matches(CloneDivergence divergence)
        => string.Equals(Family, divergence.Family, StringComparison.Ordinal)
        && string.Equals(Unit,   divergence.Unit,   StringComparison.Ordinal)
        && string.Equals(Member, divergence.Member, StringComparison.Ordinal)
        && string.Equals(Kind,   divergence.Kind,   StringComparison.Ordinal);

    public string Key => $"{Family} · {Unit} · {Member} · {Kind}";
}

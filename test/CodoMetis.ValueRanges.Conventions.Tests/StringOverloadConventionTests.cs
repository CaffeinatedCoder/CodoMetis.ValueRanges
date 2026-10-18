using System.Reflection;
using CodoMetis.ValueRanges.Oracles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// No test calls <c>ISpanParsable&lt;T&gt;.Parse</c> or <c>TryParse</c> with a <see cref="string"/>
/// argument: that is the span overload reached by accident, where the test meant the string one.
/// </summary>
/// <remarks>
/// <para>
/// Under a constraint that derives from <see cref="ISpanParsable{TSelf}"/> — <c>IRangeFactory</c>,
/// <c>IValueSetFactory</c>, or <c>ISpanParsable</c> itself — <c>TValue.Parse(text, provider)</c> with a
/// string does not bind to <see cref="IParsable{TSelf}.Parse(string, IFormatProvider?)"/>. Overload
/// resolution removes a base interface's candidate when a derived interface's is applicable, and the
/// implicit string-to-span conversion makes the span overload applicable. Every type declares its
/// string overloads separately (and they are what reflection-based callers such as minimal-API binding
/// find), so a generic sweep written this way never calls them: before #142, eight sweeps that list
/// <c>Parse(string)</c> as an entry point compared the span overload with itself.
/// </para>
/// <para>
/// The check binds every invocation in every test project with Roslyn, so it judges what the compiler
/// chose rather than how the call is spelled. A call on a concrete type (<c>Int32Set.Parse(text, …)</c>)
/// binds to that type's own string method and is not flagged. Generic code goes through
/// <see cref="StringOverloads"/>, constrained on <see cref="IParsable{TSelf}"/> alone.
/// </para>
/// </remarks>
[TestClass]
public sealed class StringOverloadConventionTests
{
    /// <summary>The usings every test project gets from <c>ImplicitUsings</c> and its <c>&lt;Using&gt;</c> items.</summary>
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.VisualStudio.TestTools.UnitTesting;
        """;

    [TestMethod]
    public void NoGenericCall_ReachesTheSpanOverload_WithAString()
    {
        var violations = new List<string>();
        var bound      = 0;

        foreach (var project in TestProjects())
        {
            var compilation = Compile(project);

            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);

                foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Parse" or "TryParse" })
                        continue;

                    var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    if (symbol is null) continue;
                    bound++;

                    if (symbol.ContainingType.OriginalDefinition.ToDisplayString() != "System.ISpanParsable<TSelf>") continue;
                    if (invocation.ArgumentList.Arguments.FirstOrDefault() is not { } first) continue;
                    if (model.GetTypeInfo(first.Expression).Type?.SpecialType != SpecialType.System_String) continue;

                    var line = tree.GetLineSpan(invocation.Span).StartLinePosition.Line + 1;
                    violations.Add($"{Path.GetRelativePath(RepoLayout.Root.FullName, tree.FilePath)}:{line} — `{invocation}` binds to "
                                 + "ISpanParsable's span overload; call StringOverloads.Parse/TryParse to reach the string one");
                }
            }
        }

        // The check binds hundreds of Parse calls; finding none would mean the compilation stopped resolving them.
        Assert.IsTrue(bound >= 200, $"Bound only {bound} Parse/TryParse calls across the test projects.");
        Assert.AreEqual(0, violations.Count,
            "Generic calls that mean Parse(string) but reach the span overload:" + Environment.NewLine
          + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<DirectoryInfo> TestProjects() =>
        new DirectoryInfo(Path.Combine(RepoLayout.Root.FullName, "test"))
           .EnumerateDirectories()
           .Where(directory => directory.EnumerateFiles("*.csproj").Any())
           .OrderBy(directory => directory.Name, StringComparer.Ordinal);

    /// <summary>
    /// One compilation per test project, referencing the framework and everything this project's output
    /// holds — the four shipping assemblies, EF Core, Npgsql, MSTest. A package only another project uses
    /// (Testcontainers) stays unresolved, which leaves the calls on range and set types bound.
    /// </summary>
    private static CSharpCompilation Compile(DirectoryInfo project)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var trees   = project.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                             .Where(file => !IsBuildOutput(project, file))
                             .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file.FullName), options, file.FullName))
                             .Append(CSharpSyntaxTree.ParseText(GlobalUsings, options, "GlobalUsings.g.cs"))
                             .ToList();

        return CSharpCompilation.Create(project.Name, trees, References.Value,
                                        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static bool IsBuildOutput(DirectoryInfo project, FileInfo file)
    {
        var relative = Path.GetRelativePath(project.FullName, file.FullName).Split(Path.DirectorySeparatorChar);
        return relative[0] is "bin" or "obj";
    }

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var local    = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll");

        return [.. platform.Concat(local)
                           .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                           .Select(group => group.First())
                           .Where(IsManagedAssembly)
                           .Select(path => MetadataReference.CreateFromFile(path))];
    });

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }
}

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// No shipping package replaces EF Core's model differ or migrations SQL generator (#334).
/// </summary>
/// <remarks>
/// <para>
/// EF Core resolves <see cref="IMigrationsModelDiffer"/> and <see cref="IMigrationsSqlGenerator"/>
/// as single services, so when two packages replace one of them the last registration wins and the
/// other package's migration DDL disappears without an error. From 11.0.0 on,
/// <c>CodoMetis.EFCore.SchemaKit</c> is the one owner of both in the CodoMetis family: a
/// migration-aware feature goes into SchemaKit or plugs into its contract, never into a replacement
/// here. The custom store types (<c>timerange</c>, <c>float8range</c>, <c>float4range</c>) are
/// created through Npgsql's <c>HasPostgresRange</c>, not through a differ of ours.
/// </para>
/// <para>
/// Two checks, because either alone has a way around it: the compiled assemblies are searched for a
/// type that implements or derives from either service, and the sources under <c>src/</c> for any
/// reference to them in code — a <c>ReplaceService</c>, a <c>TryAdd</c>, a <c>ServiceDescriptor</c>
/// over <c>typeof(…)</c>, a base list. Comments and XML docs are not code and may name them.
/// </para>
/// <para>
/// Everything is discovered — projects by globbing <c>src/</c> — so a new EF satellite is covered
/// with no edit here.
/// </para>
/// </remarks>
[TestClass]
public sealed class MigrationServiceOwnershipConventionTests
{
    private static readonly Type[] OwnedServices = [typeof(IMigrationsModelDiffer), typeof(IMigrationsSqlGenerator)];

    /// <summary>
    /// The names a reference to either service is spelled with: the interfaces, EF's base
    /// implementations a replacement derives from, and Npgsql's generator.
    /// </summary>
    private static readonly string[] OwnedNames =
    [
        "IMigrationsModelDiffer", "MigrationsModelDiffer",
        "IMigrationsSqlGenerator", "MigrationsSqlGenerator", "NpgsqlMigrationsSqlGenerator"
    ];

    [TestMethod]
    public void NoShippingType_ImplementsTheModelDifferOrTheMigrationsSqlGenerator()
    {
        var efAssemblies = RepoLayout.PackableProjects
                                     .Select(LoadAssembly)
                                     .Where(assembly => assembly.GetReferencedAssemblies()
                                                                .Any(reference => reference.Name == "Microsoft.EntityFrameworkCore.Relational"))
                                     .ToList();

        // A sweep over no assembly passes for the wrong reason: if the EF packages stop being found,
        // this says so instead of going green.
        Assert.IsTrue(efAssemblies.Count >= 2,
                      $"Expected at least the two shipping EF assemblies, found {efAssemblies.Count}: "
                    + string.Join(", ", efAssemblies.Select(assembly => assembly.GetName().Name)));

        var violations = efAssemblies
                        .SelectMany(assembly => LoadableTypes(assembly).Select(type => (assembly, type)))
                        .SelectMany(pair => OwnedServices.Where(service => service.IsAssignableFrom(pair.type))
                                                         .Select(service => $"{pair.assembly.GetName().Name}: {pair.type.FullName} "
                                                                          + $"implements {service.Name}"))
                        .ToList();

        Assert.AreEqual(0, violations.Count,
                        "The model differ and the migrations SQL generator belong to CodoMetis.EFCore.SchemaKit; "
                      + "a migration-aware feature goes into SchemaKit or plugs into its contract:"
                      + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void NoShippingSource_ReplacesOrRegistersTheModelDifferOrTheMigrationsSqlGenerator()
    {
        var violations    = new List<string>();
        int files         = 0;
        int registrations = 0;

        foreach (var file in SourceFiles())
        {
            files++;
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file.FullName)).GetRoot();

            registrations += root.DescendantNodes().OfType<GenericNameSyntax>()
                                 .Count(name => name.Identifier.Text is "TryAdd" or "ReplaceService");

            // DescendantNodes does not descend into trivia, so comments and XML docs are skipped.
            violations.AddRange(root.DescendantNodes().OfType<SimpleNameSyntax>()
                                    .Where(name => OwnedNames.Contains(name.Identifier.Text, StringComparer.Ordinal))
                                    .Select(name => $"{Relative(file)}:{LineOf(name)} — {name.Identifier.Text} in `{LineText(name)}`"));

            // A service named by string (Type.GetType, a descriptor read from configuration) is the
            // same registration with the type checker taken out.
            violations.AddRange(root.DescendantTokens()
                                    .Where(token => token.IsKind(SyntaxKind.StringLiteralToken))
                                    .Where(token => OwnedNames.Any(owned => token.ValueText.Contains(owned, StringComparison.Ordinal)))
                                    .Select(token => $"{Relative(file)}:{LineOf(token.Parent!)} — names `{token.ValueText}` in a string"));
        }

        // The scan must be reading the files that register services, or it proves nothing.
        Assert.IsTrue(files > 0 && registrations > 0,
                      $"Scanned {files} source files under src/ and found {registrations} TryAdd/ReplaceService calls; "
                    + "expected the EF package's service registrations. Has the registration moved, or src/?");

        Assert.AreEqual(0, violations.Count,
                        "The model differ and the migrations SQL generator belong to CodoMetis.EFCore.SchemaKit; "
                      + "no package here may replace or register them:"
                      + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static Assembly LoadAssembly(PackableProject project)
    {
        var name = project.Property("AssemblyName") ?? Path.GetFileNameWithoutExtension(project.File.Name);

        try
        {
            return Assembly.Load(name);
        }
        catch (FileNotFoundException exception)
        {
            throw new AssertFailedException(
                $"Could not load '{name}', a shipping project under src/. Add a ProjectReference to it in "
              + "CodoMetis.ValueRanges.Conventions.Tests.csproj, which references every shipping project.", exception);
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static IEnumerable<FileInfo> SourceFiles() =>
        RepoLayout.PackableProjects
                  .SelectMany(project => project.Directory.EnumerateFiles("*.cs", SearchOption.AllDirectories)
                                                .Where(file => !IsBuildOutput(project, file)))
                  .OrderBy(file => file.FullName, StringComparer.Ordinal);

    // Generated sources under obj/ are the compiler's, not the package author's.
    private static bool IsBuildOutput(PackableProject project, FileInfo file) =>
        Path.GetRelativePath(project.Directory.FullName, file.FullName)
            .Split(Path.DirectorySeparatorChar)[0] is "obj" or "bin";

    private static string LineText(SyntaxNode node) =>
        node.SyntaxTree.GetText().Lines[LineOf(node) - 1].ToString().Trim();

    private static int LineOf(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepoLayout.Root.FullName, file.FullName);
}

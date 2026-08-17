using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Guards the two halves of the rule that four bugs came from: a binary range operation is a
/// function of the *pair* of shapes, and a shape the author did not think about must never be
/// answered with a plausible value.
/// </summary>
/// <remarks>
/// <para>
/// IsAdjacentTo (6.2.1), IsStrictlyLeftOf and Except (7.0.0) and the Infinity-operand subtraction
/// (8.0.0) were all the same defect: dispatch switched on the receiver's shape, the inner switch
/// covered some operand shapes, and the discard arm returned something well-formed for the rest.
/// Each was silently wrong in memory while the EF translation stayed correct, so the disagreement
/// was between the two sides of the wire rather than inside either.
/// </para>
/// <para>
/// C# cannot prove a switch over interface patterns exhaustive, so the discard arm itself cannot
/// be removed. What it can be is fatal: a missing pair then names itself at the first test that
/// reaches it instead of returning a value that looks right in a debugger.
/// </para>
/// <para>
/// Everything is discovered — engines by globbing <c>src/</c>, switches by parsing — so adding an
/// engine or an operation needs no edit here.
/// </para>
/// </remarks>
[TestClass]
public class EngineDispatchConventionTests
{
    private static readonly string[] ShapeInterfaces =
    [
        "IFiniteRange", "IUnboundedStartRange", "IUnboundedEndRange", "IInfinityRange", "IEmptyRange"
    ];

    /// <summary>
    /// A switch that dispatches on range shape must throw from its discard arm rather than
    /// produce a value. Applies to every <c>Internals/</c> file in every shipping project;
    /// switches over anything else (a pair of bools, a comparison result) are untouched.
    /// </summary>
    [TestMethod]
    public void ShapeDispatchDiscardArmsThrow()
    {
        var violations = new List<string>();
        int inspected  = 0;

        foreach (var file in InternalsFiles())
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file.FullName)).GetRoot();

            foreach (var expression in root.DescendantNodes().OfType<SwitchExpressionSyntax>())
            {
                if (!expression.Arms.Any(arm => DispatchesOnShape(arm.Pattern))) continue;

                inspected++;
                foreach (var arm in expression.Arms.Where(arm => arm.Pattern is DiscardPatternSyntax)
                                               .Where(arm => arm.Expression is not ThrowExpressionSyntax))
                {
                    violations.Add($"{Relative(file)}:{LineOf(arm)} — discard arm returns "
                                 + $"`{arm.Expression}` instead of throwing");
                }
            }

            foreach (var statement in root.DescendantNodes().OfType<SwitchStatementSyntax>())
            {
                if (!statement.Sections.SelectMany(section => section.Labels)
                              .OfType<CasePatternSwitchLabelSyntax>()
                              .Any(label => DispatchesOnShape(label.Pattern))) continue;

                inspected++;
                foreach (var section in statement.Sections
                                                 .Where(section => section.Labels.Any(label => label is DefaultSwitchLabelSyntax))
                                                 .Where(section => !section.Statements.Any(ThrowsOutright)))
                {
                    violations.Add($"{Relative(file)}:{LineOf(section)} — default section returns instead of throwing");
                }
            }
        }

        // A rule that inspects nothing passes for the wrong reason: if the shape interfaces are
        // renamed or Internals/ moves, this is what says so instead of quietly going green.
        Assert.IsTrue(inspected >= 3,
                      $"Expected to inspect at least the three engines' shape switches, found {inspected}. "
                    + "Have the shape interfaces been renamed, or has Internals/ moved?");

        Assert.AreEqual(0, violations.Count,
                        "A shape a binary operation was not written for must be fatal, never a value:"
                      + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// An engine's entry points take <c>IRange&lt;T&gt;</c> on both sides. A parameter typed as one
    /// specific shape *is* receiver-shaped dispatch — it pushes the pair apart into an outer
    /// overload and an inner switch, which is the structure every one of the four bugs had.
    /// Private helpers below the dispatch are exempt: by then the pair is already decided.
    /// </summary>
    [TestMethod]
    public void EngineEntryPointsDispatchOnThePair()
    {
        var violations = new List<string>();
        var engines    = InternalsFiles().Where(file => file.Name.EndsWith("Engine.cs", StringComparison.Ordinal)).ToList();

        Assert.IsTrue(engines.Count >= 3,
                      $"Expected at least three *Engine.cs files under src/, found {engines.Count}.");

        foreach (var file in engines)
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file.FullName)).GetRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                                       .Where(method => !method.Modifiers.Any(SyntaxKind.PrivateKeyword)))
            {
                foreach (var parameter in method.ParameterList.Parameters
                                                .Where(parameter => parameter.Type is not null)
                                                .Where(parameter => ShapeInterfaces.Any(shape => parameter.Type!.ToString().StartsWith(shape, StringComparison.Ordinal))))
                {
                    violations.Add($"{Relative(file)}:{LineOf(parameter)} — {method.Identifier} takes "
                                 + $"`{parameter.Type}` for `{parameter.Identifier}`; entry points take IRange<T> on both sides");
                }
            }
        }

        Assert.AreEqual(0, violations.Count,
                        "Engine entry points must decide on the shape pair, not on one operand's shape:"
                      + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool DispatchesOnShape(PatternSyntax pattern) =>
        ShapeInterfaces.Any(shape => pattern.ToString().Contains(shape, StringComparison.Ordinal));

    private static bool ThrowsOutright(StatementSyntax statement) =>
        statement is ThrowStatementSyntax || statement.DescendantNodes().OfType<ThrowStatementSyntax>().Any();

    private static IEnumerable<FileInfo> InternalsFiles() =>
        RepoLayout.PackableProjects
                  .Select(project => new DirectoryInfo(Path.Combine(project.Directory.FullName, "Internals")))
                  .Where(directory => directory.Exists)
                  .SelectMany(directory => directory.EnumerateFiles("*.cs", SearchOption.AllDirectories))
                  .OrderBy(file => file.FullName, StringComparer.Ordinal);

    private static int LineOf(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(RepoLayout.Root.FullName, file.FullName);
}

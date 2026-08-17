using System.Reflection;
using CodoMetis.ValueRanges.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// Every binary range operation must appear in <c>ShapeMatrixParityTests</c>.
/// </summary>
/// <remarks>
/// <para>
/// The shape matrix is the highest-yield check in the repository — it found three of the five
/// receiver-shaped-dispatch bugs by asking PostgreSQL for every ordered pair of shapes and diffing.
/// Its weakness is that it is a list: an operation added to <c>RangeExtensions</c> without a row
/// there is simply not swept, and nothing says so. The whole suite stays green while the one check
/// that would have caught the next instance quietly stops covering it.
/// </para>
/// <para>
/// So the operations are discovered by reflection and the coverage is read out of the matrix's own
/// source. Coverage is claimed two ways there — the predicates sit in a table of
/// <c>(name, operator)</c> pairs, the value-producing four are called inline against <c>*</c>,
/// <c>range_merge</c>, <c>+</c> and <c>-</c> — so both forms count: a string literal inside a
/// collection initializer, or a one-argument invocation on a receiver.
/// </para>
/// <para>
/// This deliberately does not check the <see cref="RangeSet{TRange,T}"/> operations. They have
/// their own exhaustive oracle in <c>SmallModelMultirangeOracleTests</c>, which sweeps all 65,536
/// ordered pairs of multiranges rather than a shape matrix.
/// </para>
/// </remarks>
[TestClass]
public sealed class ShapeMatrixCoverageTests
{
    // Contains, IsContainedBy, Overlaps, IsStrictlyLeftOf, IsStrictlyRightOf, DoesNotExtendRightOf,
    // DoesNotExtendLeftOf, IsAdjacentTo, Intersect, Merge, Union, Except — as of 7.0.1. A floor,
    // so adding an operation does not fail this assertion; the coverage check below covers it.
    private const int KnownBinaryOperations = 12;

    [TestMethod]
    public void EveryBinaryRangeOperation_IsSweptByTheShapeMatrix()
    {
        var operations = BinaryRangeOperations();

        Assert.IsTrue(
            operations.Count >= KnownBinaryOperations,
            $"Found {operations.Count} binary range operations, fewer than the "
          + $"{KnownBinaryOperations} known to exist: [{string.Join(", ", operations.Select(o => o.Name))}]. The "
          + "reflection filter has stopped matching, which would retire this check while leaving "
          + "it green.");

        var (tabulated, invoked) = NamesTheMatrixCovers();

        // A predicate claims its row in the table of (name, operator) pairs; a value-producing
        // operation claims it by being called against its SQL counterpart. Checking each against
        // only its own form matters: the InMemory dispatch switch invokes all eight predicates, so
        // an invocation would mark a predicate covered even after its row was deleted.
        var missing = operations
                     .Where(operation => !(operation.ReturnsBool ? tabulated : invoked).Contains(operation.Name))
                     .Select(operation => $"{operation.Name} ({(operation.ReturnsBool ? "no row in the predicate table" : "never invoked")})")
                     .ToList();

        Assert.AreEqual(
            0, missing.Count,
            $"These binary range operations have no row in ShapeMatrixParityTests: "
          + $"{string.Join(", ", missing)}. The matrix is what asks PostgreSQL for every ordered "
          + "pair of shapes, and it found three of the five bugs in that family — an operation "
          + "missing from it is one nobody is sweeping. Add it to the Predicates table if it "
          + "returns a bool, or to the value-operation sweep beside Intersect/Merge/Union/Except.");
    }

    /// <summary>
    /// Public operations on <see cref="RangeExtensions"/> taking a range receiver and one
    /// <see cref="IRange{T}"/> operand. C# 14 extension members lower to static methods with the
    /// receiver first, so they are ordinary reflection targets. Operations over an *element*
    /// (<c>Contains(T)</c>, <c>Clamp(T)</c>) are excluded — the matrix is about shape pairs.
    /// </summary>
    private static List<(string Name, bool ReturnsBool)> BinaryRangeOperations() =>
        [.. typeof(RangeExtensions)
           .GetMethods(BindingFlags.Public | BindingFlags.Static)
           .Where(method => method.GetParameters().Length == 2)
           .Where(method => IsRangeReceiver(method.GetParameters()[0].ParameterType))
           .Where(method => IsRangeInterface(method.GetParameters()[1].ParameterType))
           .Select(method => (method.Name, ReturnsBool: method.ReturnType == typeof(bool)))
           .Distinct()
           .OrderBy(operation => operation.Name, StringComparer.Ordinal)];

    private static bool IsRangeInterface(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRange<>);

    // Either the IRange<T> receiver of the predicate block, or the TRange receiver of the
    // value-producing block, whose constraints name IRange<T>.
    private static bool IsRangeReceiver(Type type) =>
        IsRangeInterface(type)
     || (type.IsGenericParameter && type.GetGenericParameterConstraints().Any(IsRangeInterface));

    /// <summary>
    /// The two ways the matrix claims coverage. <c>Tabulated</c> is every string literal in a
    /// collection initializer — the table of (name, operator) pairs the predicates are swept from.
    /// <c>Invoked</c> is every one-argument invocation on a receiver, which is how the
    /// value-producing operations are called against their SQL counterparts.
    /// </summary>
    private static (HashSet<string> Tabulated, HashSet<string> Invoked) NamesTheMatrixCovers()
    {
        var root      = CSharpSyntaxTree.ParseText(File.ReadAllText(MatrixSource().FullName)).GetRoot();
        var tabulated = new HashSet<string>(StringComparer.Ordinal);
        var invoked   = new HashSet<string>(StringComparer.Ordinal);

        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>()
                                    .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                                    .Where(literal => literal.Ancestors().Any(ancestor =>
                                               ancestor is InitializerExpressionSyntax or CollectionExpressionSyntax)))
        {
            tabulated.Add(literal.Token.ValueText);
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>()
                                       .Where(invocation => invocation.ArgumentList.Arguments.Count == 1))
        {
            if (invocation.Expression is MemberAccessExpressionSyntax access)
                invoked.Add(access.Name.Identifier.ValueText);
        }

        return (tabulated, invoked);
    }

    private static FileInfo MatrixSource()
    {
        var matches = new DirectoryInfo(Path.Combine(RepoLayout.Root.FullName, "test"))
                     .EnumerateFiles("ShapeMatrixParityTests.cs", SearchOption.AllDirectories)
                     .ToList();

        Assert.AreEqual(
            1, matches.Count,
            $"Expected exactly one ShapeMatrixParityTests.cs under test/, found {matches.Count}. "
          + "This check reads coverage out of that file, so a rename or a copy has to be noticed "
          + "here rather than silently leaving nothing to read.");

        return matches[0];
    }
}

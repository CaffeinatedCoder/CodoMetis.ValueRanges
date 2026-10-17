using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The public surface of the four shipping assemblies, enumerated by reflection: one key per
/// public member declared on a public type, <c>TypeName.Member</c>, nested types joined with
/// <c>+</c> and generic arity dropped (<c>RangeSet.Union</c>, <c>Int32Range+Finite.Start</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the second half of the oracle catalogue's input. The first is what the documents claim
/// (<c>test/oracles/claims.json</c>); this is what the API offers. Every key must be named by a
/// claim's <c>surfaces</c> or by a waiver in <c>test/oracles/surface-waivers.json</c>, so a public
/// member cannot be added without deciding which oracle holds it — or saying why none should.
/// </para>
/// <para>
/// Overloads collapse into one key: an oracle that holds <c>Contains</c> holds all of it. The
/// compiler-generated grouping types of C# 14 <c>extension</c> blocks are skipped (their members
/// are listed on the static class), as are explicit interface implementations and accessors.
/// </para>
/// </remarks>
internal static partial class ApiSurface
{
    /// <summary>One type per shipping assembly, so a satellite that is split off is still found.</summary>
    private static readonly Type[] s_assemblyMarkers =
    [
        typeof(Int32Range),
        typeof(LocalDateRange),
        typeof(Microsoft.EntityFrameworkCore.NpgsqlValueRangesDbContextOptionsBuilderExtensions),
        typeof(Microsoft.EntityFrameworkCore.NpgsqlValueRangesNodaTimeDbContextOptionsBuilderExtensions)
    ];

    /// <summary>Every public member key, sorted ordinally.</summary>
    internal static IReadOnlyList<string> Members { get; } = Enumerate();

    /// <summary>Compiles a surface pattern: <c>*</c> matches any run of characters, everything else is literal.</summary>
    internal static Regex Pattern(string glob) =>
        new("^" + string.Join(".*", glob.Split('*').Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant);

    /// <summary>The members a pattern names.</summary>
    internal static IReadOnlyList<string> Matching(string glob)
    {
        var pattern = Pattern(glob);
        return [.. Members.Where(member => pattern.IsMatch(member))];
    }

    private static IReadOnlyList<string> Enumerate()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in s_assemblyMarkers.Select(marker => marker.Assembly).Distinct())
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.Name.Contains('<') || type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null) continue;

            var typeName = Name(type);

            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                switch (member)
                {
                    case Type:
                        continue; // listed as its own type
                    case MethodInfo { IsSpecialName: true } accessor when !accessor.Name.StartsWith("op_", StringComparison.Ordinal):
                        continue; // property and event accessors; the property is the member
                    case MethodInfo { Name: var name } when name.Contains('.') || name.Contains('<'):
                        continue; // explicit interface implementations and compiler-generated members
                }

                keys.Add($"{typeName}.{(member is ConstructorInfo ? ".ctor" : member.Name)}");
            }
        }

        return [.. keys];
    }

    private static string Name(Type type)
    {
        var tick = type.Name.IndexOf('`');
        var bare = tick < 0 ? type.Name : type.Name[..tick];
        return type.DeclaringType is { } outer ? Name(outer) + "+" + bare : bare;
    }
}

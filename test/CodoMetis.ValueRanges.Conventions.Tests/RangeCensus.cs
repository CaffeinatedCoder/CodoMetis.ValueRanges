using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Conventions.Tests;

/// <summary>
/// The range unions the shipping assemblies hold, counted by what they are rather than by the
/// interface a sweep discovers them through — the expected count for every discovered-union sweep.
/// </summary>
/// <remarks>
/// <para>
/// The sweeps find their types by an interface: <c>IRangeFactory&lt;,&gt;</c>
/// (<see cref="RangeProbes.AllRangeTypes"/>) or <c>IRange&lt;&gt;</c> (the integration project's
/// <c>FuzzTypes</c>). A union that stopped declaring it would drop out of the sweep that reads it, and a
/// floor of "at least <i>n</i>" kept passing as long as <i>n</i> was stale: the floors said 11 after the
/// float family made it 13, so both float unions could have gone missing unnoticed (#222).
/// </para>
/// <para>
/// The census reads structure instead: a top-level abstract type that nests a sealed variant
/// implementing <see cref="IEmptyRange{T}"/>. Every union has one, and nothing else does. A sweep then
/// asserts that it reached every union of the census, so a new type needs no edit and a lost one is
/// named. The floor is only a guard against the census itself finding nothing: each of the two
/// assemblies holds at least one union.
/// </para>
/// </remarks>
internal static class RangeCensus
{
    /// <summary>Every union, ordered by name.</summary>
    internal static IReadOnlyList<Type> Unions { get; } =
    [
        .. new[] { typeof(Int32Range), typeof(LocalDateRange) }
           .Select(marker => marker.Assembly)
           .Distinct()
           .SelectMany(assembly => assembly.GetExportedTypes())
           .Where(type => type is { IsAbstract: true, IsNested: false, IsClass: true })
           .Where(type => type.GetNestedTypes().Any(IsEmptyVariant))
           .OrderBy(type => type.Name, StringComparer.Ordinal)
    ];

    /// <summary>
    /// Fails unless <paramref name="reached"/> holds every union of the census, naming the ones it
    /// does not.
    /// </summary>
    internal static void AssertReachedEveryUnion(IEnumerable<Type> reached, string sweep)
    {
        var assemblies = Unions.Select(union => union.Assembly).Distinct().Count();
        Assert.AreEqual(2, assemblies,
            $"The range census found unions in {assemblies} assemblies, not in both the core and the NodaTime one: "
          + string.Join(", ", Unions.Select(union => union.Name)));

        var missing = Unions.Except(reached).Select(union => union.Name).ToList();
        Assert.IsEmpty(missing,
            $"{sweep} did not reach {missing.Count} of the {Unions.Count} range unions: {string.Join(", ", missing)}.");
    }

    private static bool IsEmptyVariant(Type nested)
        => nested.IsSealed
        && nested.GetInterfaces().Any(@interface => @interface.IsGenericType
                                                 && @interface.GetGenericTypeDefinition() == typeof(IEmptyRange<>));
}

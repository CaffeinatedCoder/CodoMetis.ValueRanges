namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// The runtime seal on a range union: the check every union's copy constructor runs.
/// </summary>
/// <remarks>
/// A union is an abstract record with a private constructor and its variants nested inside it,
/// and the private constructor is what the documentation used to credit for keeping the union
/// closed. It never did. The compiler synthesizes a copy constructor on every non-sealed record
/// for <c>with</c> expressions, and refuses to let it be private (CS8878), so a record declared
/// anywhere could chain to it and become a sixth variant — one that implements no shape
/// interface, so <c>IsEmpty()</c>, <c>Contains</c> and <c>Overlaps</c> answered it <see langword="false"/>
/// rather than throwing. Each union therefore declares the copy constructor itself and calls
/// <see cref="Require{TUnion}"/>, which accepts exactly the types nested in the union and throws
/// for anything else, at construction and before the value can reach a switch.
/// </remarks>
internal static class ClosedUnion
{
    /// <summary>
    /// Throws unless <paramref name="instance"/>'s runtime type is one of the variants nested in
    /// <typeparamref name="TUnion"/>.
    /// </summary>
    internal static void Require<TUnion>(TUnion instance)
        where TUnion : class
    {
        var actual = instance.GetType();
        if (actual.DeclaringType == typeof(TUnion)) return;

        throw new InvalidOperationException(
            $"{actual} is not a variant of {typeof(TUnion).Name}. The union is closed: the variants nested in " +
            "it are the only ranges that can exist, and a type derived through the copy constructor is refused.");
    }
}

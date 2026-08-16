using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// Walks the values of a bounded discrete range, one step at a time.
/// </summary>
/// <remarks>
/// The walk is expressed once here rather than per type: <see cref="IRangeFactory{TRange,T}"/>
/// already supplies the step through <c>NextValueAfter</c>, so the engine needs nothing a
/// discrete range type does not already provide.
/// </remarks>
internal static class DiscreteEnumeration
{
    /// <summary>
    /// Validates that <paramref name="range"/> can be enumerated at all, then returns the walk.
    /// </summary>
    /// <remarks>
    /// Validation is eager and the walk is deferred, which is why they are separate methods: an
    /// iterator would postpone the exception to the first <c>MoveNext</c>, surfacing it at the
    /// <c>foreach</c> rather than at the call that was actually wrong.
    /// </remarks>
    internal static IEnumerable<T> Values<TRange, T>(IRange<T> range)
        where TRange : IRangeFactory<TRange, T>, IRange<T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        // Reachable only from the discrete range types, which expose Values() as an instance
        // member — a continuous type simply has no such member to call.
        System.Diagnostics.Debug.Assert(TRange.IsDiscrete, $"{typeof(TRange).Name} is not discrete");

        if (range is IEmptyRange<T>) return [];

        if (range is not IFiniteRange<T> finite)
            throw new NotSupportedException(
                $"Cannot enumerate an unbounded range ('{range}'): the sequence would not terminate. "
              + "Intersect it with a bounded range first.");

        return Walk<TRange, T>(finite.Start, finite.End);
    }

    /// <summary>
    /// Yields every value from <paramref name="start"/> through <paramref name="end"/> inclusive.
    /// Both bounds are inclusive because discrete ranges canonicalize to the closed form.
    /// </summary>
    internal static IEnumerable<T> Walk<TRange, T>(T start, T end)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        var current = start;
        while (true)
        {
            yield return current;

            // Stop on reaching the end, and also when the domain runs out beneath us: a range
            // closed at the maximum representable value has no successor to step to.
            if (current.CompareTo(end) >= 0) yield break;
            if (TRange.NextValueAfter(current) is not { } next) yield break;

            current = next;
        }
    }
}

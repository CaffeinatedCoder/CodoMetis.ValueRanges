using CodoMetis.ValueRanges.Core;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// Normalizes discrete range bounds to the canonical fully closed form <c>[start, end]</c>,
/// so that structural record equality coincides with set equality.
/// Must only be called by factories of discrete domains (types overriding
/// <see cref="IRangeFactory{TRange,T}.NextValueAfter"/>).
/// </summary>
internal static class DiscreteCanonical
{
    /// <summary>
    /// Closes any exclusive bound by stepping to its neighbor.
    /// Returns <see langword="null"/> when the bounds denote the empty set —
    /// including formerly hidden cases such as <c>(1, 2)</c> over integers.
    /// </summary>
    internal static (T Start, T End)? Finite<TRange, T>(T start, T end, bool startInclusive, bool endInclusive)
        where TRange : IRangeFactory<TRange, T>
        where T : struct, IComparable<T>, IEquatable<T>
    {
        if (!startInclusive)
        {
            // No successor ⇒ start is the domain maximum ⇒ (max, …] is empty.
            if (TRange.NextValueAfter(start) is not { } s) return null;
            start = s;
        }

        if (!endInclusive)
        {
            // No predecessor ⇒ end is the domain minimum ⇒ […, min) is empty.
            if (TRange.PreviousValueBefore(end) is not { } e) return null;
            end = e;
        }

        // Closing the bounds may invert them: (1, 2) → [2, 1] → empty.
        return start.CompareTo(end) <= 0 ? (start, end) : null;
    }
}
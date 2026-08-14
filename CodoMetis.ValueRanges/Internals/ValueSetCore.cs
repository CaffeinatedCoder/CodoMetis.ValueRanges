using System.Collections.Immutable;

namespace CodoMetis.ValueRanges.Internals;

/// <summary>
/// The generic engine behind all value set types: canonicalization, membership, equality, and
/// the set algebra over canonical arrays. Works for any element type purely through the
/// caller-supplied canonical comparer — no per-type code.
/// </summary>
/// <remarks>
/// Every binary operation assumes both operands hold the canonical invariant (sorted by the
/// same comparer, deduplicated, no <see langword="null"/>s) and preserves it, enabling
/// O(n+m) merge scans throughout.
/// </remarks>
internal static class ValueSetCore
{
    /// <summary>
    /// Normalizes arbitrary input into canonical form: validates no <see langword="null"/>
    /// elements, sorts by <paramref name="comparer"/>, and deduplicates.
    /// </summary>
    internal static ImmutableArray<T> Canonicalize<T>(IEnumerable<T> values, IComparer<T> comparer)
        where T : IEquatable<T>
    {
        ArgumentNullException.ThrowIfNull(values);

        var list = values is ICollection<T> collection ? new List<T>(collection.Count) : [];
        foreach (var value in values)
        {
            if (value is null)
                throw new ArgumentException("Value sets cannot contain null elements.", nameof(values));
            list.Add(value);
        }

        return CanonicalizeValidated(list, comparer);
    }

    /// <inheritdoc cref="Canonicalize{T}(IEnumerable{T}, IComparer{T})"/>
    internal static ImmutableArray<T> Canonicalize<T>(ReadOnlySpan<T> values, IComparer<T> comparer)
        where T : IEquatable<T>
    {
        var list = new List<T>(values.Length);
        foreach (var value in values)
        {
            if (value is null)
                throw new ArgumentException("Value sets cannot contain null elements.", nameof(values));
            list.Add(value);
        }

        return CanonicalizeValidated(list, comparer);
    }

    private static ImmutableArray<T> CanonicalizeValidated<T>(List<T> list, IComparer<T> comparer)
        where T : IEquatable<T>
    {
        if (list.Count == 0) return [];

        // OrderBy is a stable sort: among comparer-equal elements (e.g. decimal values of
        // different scale) the first in input order survives deduplication, keeping the
        // canonical array deterministic.
        var builder = ImmutableArray.CreateBuilder<T>(list.Count);
        foreach (var value in list.OrderBy(static x => x, comparer))
        {
            if (builder.Count == 0 || comparer.Compare(builder[^1], value) != 0)
                builder.Add(value);
        }

        return builder.Count == builder.Capacity ? builder.MoveToImmutable() : builder.ToImmutable();
    }

    /// <summary>Membership by element equality.</summary>
    internal static bool Contains<T>(ImmutableArray<T> elements, T value)
        where T : IEquatable<T>
    {
        foreach (var element in elements)
        {
            if (element.Equals(value)) return true;
        }

        return false;
    }

    /// <summary>Whether the operands share at least one element.</summary>
    internal static bool Overlaps<T>(ImmutableArray<T> left, ImmutableArray<T> right, IComparer<T> comparer)
    {
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            var comparison = comparer.Compare(left[i], right[j]);
            if (comparison == 0) return true;
            if (comparison < 0) i++;
            else j++;
        }

        return false;
    }

    /// <summary>Whether every element of <paramref name="left"/> is present in <paramref name="right"/>.</summary>
    internal static bool IsSubsetOf<T>(ImmutableArray<T> left, ImmutableArray<T> right, IComparer<T> comparer)
    {
        int i = 0, j = 0;
        while (i < left.Length)
        {
            if (j == right.Length) return false;

            var comparison = comparer.Compare(left[i], right[j]);
            if (comparison < 0) return false;
            if (comparison == 0) i++;
            j++;
        }

        return true;
    }

    /// <summary>
    /// The union of two canonical arrays, keeping <paramref name="left"/>'s representative
    /// among comparer-equal elements — the same "first in input order survives" tie-break
    /// <see cref="Canonicalize{T}(IEnumerable{T}, IComparer{T})"/> applies. Returns
    /// <paramref name="left"/> unchanged when the other side contributes nothing, so callers
    /// can preserve instance identity.
    /// </summary>
    internal static ImmutableArray<T> Union<T>(ImmutableArray<T> left, ImmutableArray<T> right, IComparer<T> comparer)
    {
        if (right.IsEmpty) return left;
        if (left.IsEmpty) return right;

        var builder = ImmutableArray.CreateBuilder<T>(left.Length + right.Length);
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            var comparison = comparer.Compare(left[i], right[j]);
            if (comparison < 0) builder.Add(left[i++]);
            else if (comparison > 0) builder.Add(right[j++]);
            else
            {
                builder.Add(left[i++]);
                j++;
            }
        }

        while (i < left.Length) builder.Add(left[i++]);
        while (j < right.Length) builder.Add(right[j++]);

        // Only the left-hand shortcut is sound. Every left element is added exactly once, so
        // a count equal to left's length proves nothing else was added and the result is left.
        // The mirrored check on right does NOT prove the same thing: it holds whenever left is
        // a subset, where the builder carries left's representatives for the comparer-equal
        // pairs and right carries its own — returning right would silently swap them (a
        // DecimalSet {1.0} unioned into {1.00,2} would come back as {1.00,2}).
        return builder.Count == left.Length ? left : builder.ToImmutable();
    }

    /// <summary>
    /// The intersection of two canonical arrays. Returns <paramref name="left"/> unchanged when
    /// every element survives, so callers can preserve instance identity.
    /// </summary>
    internal static ImmutableArray<T> Intersect<T>(ImmutableArray<T> left, ImmutableArray<T> right, IComparer<T> comparer)
    {
        if (left.IsEmpty || right.IsEmpty) return [];

        var builder = ImmutableArray.CreateBuilder<T>(Math.Min(left.Length, right.Length));
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            var comparison = comparer.Compare(left[i], right[j]);
            if (comparison < 0) i++;
            else if (comparison > 0) j++;
            else
            {
                builder.Add(left[i++]);
                j++;
            }
        }

        return builder.Count == left.Length ? left : builder.ToImmutable();
    }

    /// <summary>
    /// The elements of <paramref name="left"/> not present in <paramref name="right"/>. Returns
    /// <paramref name="left"/> unchanged when nothing is removed, so callers can preserve
    /// instance identity.
    /// </summary>
    internal static ImmutableArray<T> Except<T>(ImmutableArray<T> left, ImmutableArray<T> right, IComparer<T> comparer)
    {
        if (left.IsEmpty || right.IsEmpty) return left;

        var builder = ImmutableArray.CreateBuilder<T>(left.Length);
        int i = 0, j = 0;
        while (i < left.Length && j < right.Length)
        {
            var comparison = comparer.Compare(left[i], right[j]);
            if (comparison < 0) builder.Add(left[i++]);
            else if (comparison > 0) j++;
            else
            {
                i++;
                j++;
            }
        }

        while (i < left.Length) builder.Add(left[i++]);

        return builder.Count == left.Length ? left : builder.ToImmutable();
    }

    /// <summary>
    /// Adds an element at its canonical position. Returns the input array unchanged when a
    /// comparer-equal element is already present, so callers can preserve instance identity.
    /// </summary>
    internal static ImmutableArray<T> Add<T>(ImmutableArray<T> elements, T value, IComparer<T> comparer)
    {
        var index = elements.AsSpan().BinarySearch(value, comparer);
        return index >= 0 ? elements : elements.Insert(~index, value);
    }

    /// <summary>
    /// Removes an element by equality. Returns the input array unchanged when the element is
    /// absent, so callers can preserve instance identity.
    /// </summary>
    internal static ImmutableArray<T> Remove<T>(ImmutableArray<T> elements, T value)
        where T : IEquatable<T>
    {
        for (var i = 0; i < elements.Length; i++)
        {
            if (elements[i].Equals(value)) return elements.RemoveAt(i);
        }

        return elements;
    }

    /// <summary>Element-wise equality over canonical form — equivalent to set equality.</summary>
    internal static bool SetEquals<T>(ImmutableArray<T> left, ImmutableArray<T> right)
        where T : IEquatable<T>
    {
        if (left.Length != right.Length) return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i])) return false;
        }

        return true;
    }

    /// <summary>Order-dependent hash over canonical form, consistent with <see cref="SetEquals{T}"/>.</summary>
    internal static int SetHashCode<T>(ImmutableArray<T> elements)
        where T : IEquatable<T>
    {
        var hash = new HashCode();
        foreach (var element in elements) hash.Add(element);
        return hash.ToHashCode();
    }
}

using System.Collections.Immutable;
using CodoMetis.ValueRanges.Core;
using CodoMetis.ValueRanges.Internals;

namespace CodoMetis.ValueRanges;

/// <summary>
/// Extension members providing membership queries and the set algebra on value set types.
/// </summary>
/// <remarks>
/// Members that map to PostgreSQL array operators (noted per member) are translated to SQL by
/// the EF Core satellite; the remaining members evaluate client-side only and fail query
/// translation by design.
/// </remarks>
public static class ValueSetExtensions
{
    // Count and IsEmpty are instance properties on the concrete set types rather than
    // extension members: C# forbids extension property accesses inside expression trees
    // (CS9296), which would make them untranslatable in LINQ queries.
    extension<T>(IValueSet<T> set) where T : IEquatable<T>
    {
        /// <summary>
        /// Determines whether <paramref name="value"/> is an element of the set —
        /// PostgreSQL containment <c>column @&gt; ARRAY[value]</c> (GIN-servable).
        /// The probe is normalized the way <c>From</c> normalizes elements, so it is
        /// comparable with the stored ones.
        /// </summary>
        public bool Contains(T value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            return ValueSetCore.Contains(set.Values, set.NormalizeElement(value));
        }
    }

    extension<TSet, T>(IValueSetFactory<TSet, T> set)
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        /// <summary>
        /// Determines whether this set and <paramref name="other"/> share at least one element —
        /// PostgreSQL <c>&amp;&amp;</c>.
        /// </summary>
        public bool Overlaps(TSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return ValueSetCore.Overlaps(((IValueSet<T>)set).Values, ((IValueSet<T>)other).Values, TSet.CanonicalComparer);
        }

        /// <summary>
        /// Determines whether every element of this set is contained in <paramref name="other"/> —
        /// PostgreSQL <c>&lt;@</c>.
        /// </summary>
        public bool IsSubsetOf(TSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return ValueSetCore.IsSubsetOf(((IValueSet<T>)set).Values, ((IValueSet<T>)other).Values, TSet.CanonicalComparer);
        }

        /// <summary>
        /// Determines whether this set contains every element of <paramref name="other"/> —
        /// PostgreSQL <c>@&gt;</c>.
        /// </summary>
        public bool IsSupersetOf(TSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return ValueSetCore.IsSubsetOf(((IValueSet<T>)other).Values, ((IValueSet<T>)set).Values, TSet.CanonicalComparer);
        }

        /// <summary>
        /// Returns the union of this set and <paramref name="other"/> — PostgreSQL <c>||</c>
        /// (the server-side result re-canonicalizes on read).
        /// </summary>
        public TSet Union(TSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            var merged = ValueSetCore.Union(((IValueSet<T>)set).Values, ((IValueSet<T>)other).Values, TSet.CanonicalComparer);
            return WithElements<TSet, T>(set, other, merged);
        }

        /// <summary>
        /// Returns the elements present in both this set and <paramref name="other"/>.
        /// Client-side only — PostgreSQL has no native array intersection operator.
        /// </summary>
        public TSet Intersect(TSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            var intersection = ValueSetCore.Intersect(((IValueSet<T>)set).Values, ((IValueSet<T>)other).Values, TSet.CanonicalComparer);
            return WithElements<TSet, T>(set, other, intersection);
        }

        /// <summary>
        /// Returns the elements of this set not present in <paramref name="other"/>.
        /// Client-side only — PostgreSQL has no native array difference operator.
        /// </summary>
        public TSet Except(TSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            var difference = ValueSetCore.Except(((IValueSet<T>)set).Values, ((IValueSet<T>)other).Values, TSet.CanonicalComparer);
            return WithElements<TSet, T>(set, other, difference);
        }

        /// <summary>
        /// Returns a set with <paramref name="value"/> added, or this set when the element is
        /// already present. The element is normalized the way <c>From</c> normalizes it, so the
        /// result holds the canonical invariant. Client-side only.
        /// </summary>
        public TSet Add(T value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            var element = ((IValueSet<T>)set).NormalizeElement(value);
            var added   = ValueSetCore.Add(((IValueSet<T>)set).Values, element, TSet.CanonicalComparer);
            return WithElements<TSet, T>(set, null, added);
        }

        /// <summary>
        /// Returns a set with <paramref name="value"/> removed, or this set when the element is
        /// absent. The element is normalized the way <c>From</c> normalizes it, so it is
        /// comparable with the stored ones. Client-side only.
        /// </summary>
        public TSet Remove(T value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            var element = ((IValueSet<T>)set).NormalizeElement(value);
            var removed = ValueSetCore.Remove(((IValueSet<T>)set).Values, element);
            return WithElements<TSet, T>(set, null, removed);
        }
    }

    /// <summary>
    /// Wraps an operation result, preserving instance identity when the result is one of the
    /// operands' element arrays (the engine returns operand arrays unchanged in that case —
    /// the right operand's only where it is the whole answer, e.g. an empty left in
    /// <c>Union</c>, never as a stand-in for a merge that chose left's representatives).
    /// </summary>
    private static TSet WithElements<TSet, T>(IValueSetFactory<TSet, T> set, TSet? other, ImmutableArray<T> result)
        where TSet : class, IValueSetFactory<TSet, T>, IValueSet<T>
        where T : IEquatable<T>
    {
        if (result == ((IValueSet<T>)set).Values) return (TSet)set;
        if (other is not null && result == ((IValueSet<T>)other).Values) return other;
        return TSet.FromTrusted(result);
    }
}
